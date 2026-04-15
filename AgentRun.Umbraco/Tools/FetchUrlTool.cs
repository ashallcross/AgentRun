using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentRun.Umbraco.Tools;

public class FetchUrlTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "url": { "type": "string", "description": "The URL to fetch via HTTP GET" },
                "extract": {
                    "type": "string",
                    "enum": ["raw", "structured"],
                    "default": "raw",
                    "description": "Return shape. 'raw' (default) writes the body to the fetch cache and returns a small handle pointing at it. 'structured' parses the response with AngleSharp server-side and returns a small structured handle (title, meta description, headings including ordered tag sequence, word count, image and link counts, anchor text samples, form field counts with label associations, semantic HTML element presence, and page language) without writing to disk."
                }
            },
            "required": ["url"]
        }
        """).RootElement;

    private static readonly char[] WhitespaceChars = { ' ', '\t', '\n', '\r', '\f', '\v' };

    // Story 9.1b Phase 1 carve-out (manual E2E gate, 2026-04-09): without an
    // explicit User-Agent, many WAFs / CDNs (Cloudflare, Fastly, AWS WAF) and
    // sites with bot protection (Wikipedia, GitHub, news sites) return 403 to
    // requests with no UA, treating them as suspicious script traffic. A sane
    // generic engine default unblocks any workflow that fetches arbitrary
    // public URLs. NOT workflow-specific — this is a property of fetch_url.
    // If a future workflow needs a custom UA, that becomes a Story 9.6-style
    // ToolLimitResolver / workflow-YAML tunable, not a hardcoded change here.
    private const string DefaultUserAgent = "AgentRun/1.0";

    private static readonly JsonSerializerOptions HandleJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly SsrfProtection _ssrfProtection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IToolLimitResolver _limitResolver;
    private readonly ILogger<FetchUrlTool> _logger;

    public string Name => "fetch_url";

    public string Description => "Fetches the contents of a URL via HTTP GET. Returns a small JSON handle " +
                                 "with metadata and a `saved_to` relative path; use `read_file` to load the cached body.";

    public JsonElement? ParameterSchema => Schema;

    public FetchUrlTool(
        SsrfProtection ssrfProtection,
        IHttpClientFactory httpClientFactory,
        IToolLimitResolver limitResolver,
        ILogger<FetchUrlTool>? logger = null)
    {
        _ssrfProtection = ssrfProtection;
        _httpClientFactory = httpClientFactory;
        _limitResolver = limitResolver;
        _logger = logger ?? NullLogger<FetchUrlTool>.Instance;
    }

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var urlString = ExtractStringArgument(arguments, "url");
        var extractMode = ExtractExtractMode(arguments);

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
            throw new ToolExecutionException($"Invalid URL: '{urlString}'");

        // Story 9.6: tool tuning values must come through the resolver chain.
        // Missing Step/Workflow on the execution context is an engine wiring bug.
        // Throw a typed AgentRunException subtype (Story 9.9 D2 cross-cutting fix)
        // so LlmErrorClassifier does not silently rewrite this as a generic
        // provider failure.
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "FetchUrlTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        // Story 9.7: raw mode requires the instance folder for cache writes.
        // Story 9.1b: structured mode skips the cache (architect-locked Q1 = (a)),
        // so the InstanceFolderPath check is only enforced for raw.
        if (extractMode == ExtractMode.Raw && string.IsNullOrEmpty(context.InstanceFolderPath))
        {
            throw new InvalidOperationException(
                "FetchUrlTool requires ToolExecutionContext.InstanceFolderPath to be set " +
                "so the tool can offload response bodies to the instance scratch path.");
        }

        var maxBytes       = _limitResolver.ResolveFetchUrlMaxResponseBytes(context.Step, context.Workflow);
        var timeoutSeconds = _limitResolver.ResolveFetchUrlTimeoutSeconds(context.Step, context.Workflow);

        // Per-request timeout via linked CTS — HttpClient.Timeout is no longer set.
        // Started before the SSRF DNS check so a slow DNS lookup is also bounded.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        HttpResponseMessage? response = null;
        try
        {
            await _ssrfProtection.ValidateUrlAsync(uri, timeoutCts.Token);

            // Story 10.6 Task 0.5 — cache-on-hit short-circuit (raw mode only).
            // Re-issued fetch_url calls for the same URL within the same instance
            // return the existing .fetch-cache/{hash}.html content without hitting
            // the network. Makes Story 10.6's Option 3 retry-restart cheap when
            // the conversation was wiped but the cache survives. Structured mode
            // does not cache (Q1 (a), 9.1b), so cache-on-hit does not apply there.
            // Known limitation: the original Content-Type header is not preserved
            // across cache reuse — the handle defaults to "text/html".
            if (extractMode == ExtractMode.Raw)
            {
                var cacheHit = TryReadCachedHandle(urlString, context.InstanceFolderPath!);
                if (cacheHit is not null)
                    return cacheHit;
            }

            var client = _httpClientFactory.CreateClient("FetchUrl");

            // Locked Decision #11 (Story 9.1b code-review fix pass 2026-04-09):
            // Manual redirect loop with per-hop SsrfProtection.ValidateUrlAsync
            // re-validation. Cap = 5 redirects (6 total requests). The named
            // HttpClient is registered with AllowAutoRedirect = false in
            // AgentRunComposer; do NOT re-enable automatic redirects.
            // Custom headers and cookies are intentionally NOT forwarded across
            // hops — a future contributor adding header forwarding here MUST
            // think through the security implications first.
            const int maxRedirects = 5;
            var currentUri = uri;
            for (var hop = 0; hop <= maxRedirects; hop++)
            {
                if (hop > 0)
                    await _ssrfProtection.ValidateUrlAsync(currentUri, timeoutCts.Token);

                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.UserAgent.ParseAdd(DefaultUserAgent);
                var hopResponse = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                var statusCode = (int)hopResponse.StatusCode;
                var isRedirect = statusCode is >= 300 and < 400 && hopResponse.Headers.Location is not null;
                if (isRedirect)
                {
                    var location = hopResponse.Headers.Location!;
                    hopResponse.Dispose();
                    if (hop == maxRedirects)
                        throw new ToolExecutionException("Too many redirects (max 5)");
                    // Resolve relative Location against the current request URI
                    // per RFC 7231 §7.1.2.
                    currentUri = new Uri(currentUri, location);
                    continue;
                }

                response = hopResponse;
                break;
            }

            // Defensive: the loop above always assigns or throws.
            if (response is null)
                throw new ToolExecutionException("Too many redirects (max 5)");

            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";

            using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);

            var buffer = new byte[maxBytes + 1];
            var totalRead = 0;
            int bytesRead;
            while (totalRead < buffer.Length &&
                   (bytesRead = await stream.ReadAsync(
                       buffer.AsMemory(totalRead, buffer.Length - totalRead),
                       timeoutCts.Token)) > 0)
            {
                totalRead += bytesRead;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var status      = (int)response.StatusCode;

            // No body (HTTP 204, or HTTP 200 with Content-Length: 0): return handle with
            // saved_to: null and write nothing to disk. Decision per architect 2026-04-08.
            // P5 (Story 9.1b code-review fix pass 2026-04-09): structured mode must
            // return the structured shape on empty body too — otherwise the analyser
            // sees raw-shape JSON keys when it asked for structured.
            if (totalRead == 0)
            {
                if (extractMode == ExtractMode.Structured)
                {
                    // Story 9.4 Task 1.D: empty-body short-circuit must still return
                    // the structured shape with zero-valued versions of every field,
                    // including the new generic primitives. Story 9.1b P5 precedent.
                    var emptyStructured = new StructuredFetchHandle(
                        urlString,
                        status,
                        Title: null,
                        MetaDescription: null,
                        Headings: new StructuredHeadings(Array.Empty<string>(), Array.Empty<string>(), 0, Array.Empty<string>()),
                        WordCount: 0,
                        Images: new StructuredImages(0, 0, 0),
                        Links: new StructuredLinks(0, 0, Array.Empty<string>(), false),
                        Forms: new StructuredForms(0, 0, 0),
                        SemanticElements: new StructuredSemanticElements(false, false, false, false),
                        Lang: null,
                        Truncated: false);
                    return JsonSerializer.Serialize(emptyStructured, HandleJsonOptions);
                }

                var emptyHandle = new FetchUrlHandle(urlString, status, contentType, 0, null, false);
                return JsonSerializer.Serialize(emptyHandle, HandleJsonOptions);
            }

            var truncated = totalRead > maxBytes;
            // Story 9.1b Q2 (architect-locked): the un-marked slice is the source of truth.
            // The raw branch derives bytesToWrite from this slice + marker; the structured
            // branch parses this slice directly so AngleSharp never sees the truncation
            // marker text as HTML.
            var unmarkedLength = truncated ? maxBytes : totalRead;

            // Story 9.1b: structured mode parses the un-marked bytes via AngleSharp and
            // returns a small structured handle. No disk write — Q1 (a), architect-locked.
            if (extractMode == ExtractMode.Structured)
            {
                // AC #9 / Edge Case #8: non-HTML content types fail loud rather than
                // silently fall back to raw. Check happens after empty-body short-circuit
                // (above) and before the parser is invoked.
                if (!IsHtmlContentType(contentType))
                {
                    throw new ToolExecutionException(
                        $"Cannot extract structured fields from content type '{contentType}'. Use extract: 'raw' instead.");
                }

                StructuredFetchHandle structured;
                try
                {
                    structured = ParseStructured(
                        urlString,
                        status,
                        buffer,
                        unmarkedLength,
                        truncated,
                        uri);
                }
                catch (ToolExecutionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Edge Case #11: do NOT silently fall back to raw — surface the parse failure.
                    throw new ToolExecutionException(
                        $"Failed to parse response as HTML: {ex.Message}");
                }

                return JsonSerializer.Serialize(structured, HandleJsonOptions);
            }

            byte[] bytesToWrite;
            if (truncated)
            {
                var marker = Encoding.UTF8.GetBytes($"\n\n[Response truncated at {maxBytes} bytes]");
                bytesToWrite = new byte[maxBytes + marker.Length];
                Buffer.BlockCopy(buffer, 0, bytesToWrite, 0, maxBytes);
                Buffer.BlockCopy(marker, 0, bytesToWrite, maxBytes, marker.Length);
            }
            else
            {
                bytesToWrite = new byte[totalRead];
                Buffer.BlockCopy(buffer, 0, bytesToWrite, 0, totalRead);
            }

            // Hash-derived filename (SHA-256, never SHA-1 or MD5).
            var relPath = $".fetch-cache/{ComputeUrlHash(urlString)}.html";

            // Defence-in-depth (architect review 2026-04-08): obtain the canonical
            // absolute path via PathSandbox.ValidatePath and write to *that* path —
            // do NOT separately compute a write target via Path.Combine.
            string validatedPath;
            try
            {
                validatedPath = PathSandbox.ValidatePath(relPath, context.InstanceFolderPath);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
            {
                throw new ToolExecutionException(
                    $"Failed to cache fetch_url response to {relPath}: {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(validatedPath)!);
                await File.WriteAllBytesAsync(validatedPath, bytesToWrite, timeoutCts.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ToolExecutionException(
                    $"Failed to cache fetch_url response to {relPath}: {ex.Message}");
            }

            var handle = new FetchUrlHandle(
                urlString,
                status,
                contentType,
                bytesToWrite.LongLength,
                relPath,
                truncated);

            return JsonSerializer.Serialize(handle, HandleJsonOptions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ToolExecutionException($"Request timed out after {timeoutSeconds} seconds");
        }
        catch (HttpRequestException ex)
        {
            throw new ToolExecutionException($"Connection failed: {ex.Message}");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Story 10.6 Task 0.5 — probe the instance's .fetch-cache/ for a prior
    // response to this URL and build a handle indistinguishable from the
    // cache-miss case (status, content_type, size_bytes, saved_to, truncated
    // all populated from the on-disk file). Returns null on any miss,
    // malformed cache path, or IO failure — callers fall through to the
    // normal HTTP path.
    private string? TryReadCachedHandle(string urlString, string instanceFolderPath)
    {
        var relPath = $".fetch-cache/{ComputeUrlHash(urlString)}.html";

        string validatedPath;
        try
        {
            validatedPath = PathSandbox.ValidatePath(relPath, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex,
                "fetch_url cache lookup skipped: path validation failed for {RelPath}", relPath);
            return null;
        }

        if (!File.Exists(validatedPath))
            return null;

        long size;
        bool truncated;
        try
        {
            var info = new FileInfo(validatedPath);
            size = info.Length;
            truncated = FileTailContainsTruncationMarker(validatedPath, size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex,
                "fetch_url cache lookup skipped: IO error reading {RelPath}", relPath);
            return null;
        }

        // Content-Type is not persisted with the cached body. Default to
        // text/html (the overwhelming majority of fetch_url usage); documented
        // in ExecuteAsync as a known limitation.
        var handle = new FetchUrlHandle(
            urlString,
            200,
            "text/html",
            size,
            relPath,
            truncated);

        return JsonSerializer.Serialize(handle, HandleJsonOptions);
    }

    private static bool FileTailContainsTruncationMarker(string path, long fileSize)
    {
        // The marker written by the cache-miss path is
        //   "\n\n[Response truncated at <N> bytes]"
        // where <N> fits in a signed int. A 64-byte tail comfortably covers it.
        const int tailBytes = 64;
        var readLength = (int)Math.Min(tailBytes, fileSize);
        if (readLength <= 0)
            return false;

        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        fs.Seek(fileSize - readLength, SeekOrigin.Begin);
        var buffer = new byte[readLength];
        var read = 0;
        while (read < readLength)
        {
            var n = fs.Read(buffer, read, readLength - read);
            if (n == 0) break;
            read += n;
        }
        var tail = Encoding.UTF8.GetString(buffer, 0, read);
        return tail.Contains("[Response truncated at ", StringComparison.Ordinal);
    }

    private enum ExtractMode { Raw, Structured }

    private static ExtractMode ExtractExtractMode(IDictionary<string, object?> arguments)
    {
        if (!arguments.TryGetValue("extract", out var value) || value is null)
            return ExtractMode.Raw;

        var stringValue = value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            _ => throw new ToolExecutionException(
                $"Invalid extract value: '{value}'. Must be 'raw' or 'structured'.")
        };

        if (stringValue is null)
            return ExtractMode.Raw;

        return stringValue switch
        {
            "raw" => ExtractMode.Raw,
            "structured" => ExtractMode.Structured,
            _ => throw new ToolExecutionException(
                $"Invalid extract value: '{stringValue}'. Must be 'raw' or 'structured'.")
        };
    }

    private static bool IsHtmlContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;
        return contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static StructuredFetchHandle ParseStructured(
        string url,
        int status,
        byte[] buffer,
        int length,
        bool truncated,
        Uri sourceUri)
    {
        // AngleSharp's HTML5 parser is the trust boundary for hostile input.
        var parser = new HtmlParser();
        using var stream = new MemoryStream(buffer, 0, length, writable: false);
        using var document = parser.ParseDocument(stream);

        var rawTitle = document.Title;
        var title = string.IsNullOrWhiteSpace(rawTitle) ? null : rawTitle.Trim();

        // P2 (Story 9.1b code-review fix pass): case-insensitive attribute match —
        // `<meta name="Description">` (capital D) is common CMS output and CSS
        // attribute values are case-sensitive in AngleSharp's selector engine
        // unless the `i` flag is set.
        var metaDescription = document
            .QuerySelector("meta[name=description i]")
            ?.GetAttribute("content");

        // Story 9.4 Task 1.B: single heading walk produces h1, h2, h3_h6_count,
        // AND the ordered tag sequence (capped at 200 entries via early break —
        // a pathological 50k-heading page must not allocate the full sequence
        // list). The h1/h2/h3_h6_count tallies are uncapped to preserve the
        // pre-9.4 CQA contract; only `sequence` is bounded.
        var h1 = new List<string>();
        var h2 = new List<string>();
        var h3h6Count = 0;
        var headingSequence = new List<string>();
        foreach (var heading in document.QuerySelectorAll("h1, h2, h3, h4, h5, h6"))
        {
            var tag = heading.TagName.ToLowerInvariant(); // "h1".."h6"
            switch (tag)
            {
                case "h1": h1.Add(heading.TextContent.Trim()); break;
                case "h2": h2.Add(heading.TextContent.Trim()); break;
                default:   h3h6Count++; break;
            }
            if (headingSequence.Count < HeadingSequenceCap)
                headingSequence.Add(tag);
        }

        // P3 (Story 9.1b code-review fix pass): strip <script> and <style>
        // descendants of <body> before computing word count, otherwise inline
        // JSON-LD / minified analytics inflates word_count by thousands and
        // breaks the deterministic-facts promise (the same source HTML can
        // produce wildly different word counts depending on inline-script weight).
        var bodyText = string.Empty;
        if (document.Body is { } body)
        {
            foreach (var node in body.QuerySelectorAll("script, style").ToList())
                node.Remove();
            bodyText = body.TextContent ?? string.Empty;
        }
        var wordCount = bodyText.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries).Length;

        var imgs = document.QuerySelectorAll("img");
        var imgTotal = imgs.Length;
        // Architect-locked 2026-04-09: attribute existence is the rule, not non-emptiness.
        // <img alt=""> is the accessibility-correct decorative-image marker.
        var imgWithAlt = imgs.Count(i => i.GetAttribute("alt") != null);
        var imgMissingAlt = imgTotal - imgWithAlt;

        // P4 (Story 9.1b code-review fix pass): respect <base href> when
        // resolving relative anchor URLs. Host comparison still uses the
        // source URL's host (D3 — strict host equality is intentional and
        // www-vs-apex stays parser-side strict; opinionated SEO judgements
        // are the analyser's job, not the parser's).
        var resolveBase = sourceUri;
        var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
        if (!string.IsNullOrEmpty(baseHref) && Uri.TryCreate(sourceUri, baseHref, out var parsedBase))
            resolveBase = parsedBase;

        var sourceHost = sourceUri.Host;
        var anchors = document.QuerySelectorAll("a[href]");
        var internalLinks = 0;
        var externalLinks = 0;
        // Story 9.4 Task 1.B: collect anchor text samples within the existing
        // anchor walk. Cap at 100 entries via early break on the COLLECTION only
        // — internal/external tallies stay uncapped so the 9.1b invariant
        // `internal + external == document.QuerySelectorAll("a[href]").Length`
        // continues to hold. Original case is preserved (the scanner prompt
        // does case-insensitive matching itself); empty-text anchors are skipped
        // from the sample list but still counted in internal/external.
        var anchorTexts = new List<string>();
        var anchorTextsTruncated = false;
        foreach (var a in anchors)
        {
            var href = a.GetAttribute("href") ?? "";
            if (Uri.TryCreate(resolveBase, href, out var resolved)
                && (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
            {
                if (string.Equals(resolved.Host, sourceHost, StringComparison.OrdinalIgnoreCase))
                    internalLinks++;
                else
                    externalLinks++;
            }
            else
            {
                // Non-http(s) (mailto:, tel:, javascript:, fragment-only) or malformed.
                // Classified as external so the invariant
                // internal + external == document.QuerySelectorAll("a[href]").Length holds.
                externalLinks++;
            }

            if (!anchorTextsTruncated)
            {
                var text = CollapseWhitespace(a.TextContent ?? string.Empty);
                if (text.Length > 0)
                {
                    if (anchorTexts.Count < AnchorTextsCap)
                        anchorTexts.Add(text);
                    if (anchorTexts.Count >= AnchorTextsCap)
                        anchorTextsTruncated = true;
                }
            }
        }

        var forms = BuildForms(document);
        var semanticElements = BuildSemanticElements(document);
        var langRaw = document.DocumentElement?.GetAttribute("lang");
        var lang = string.IsNullOrWhiteSpace(langRaw) ? null : langRaw;

        return new StructuredFetchHandle(
            url,
            status,
            title,
            metaDescription,
            new StructuredHeadings(h1, h2, h3h6Count, headingSequence),
            wordCount,
            new StructuredImages(imgTotal, imgWithAlt, imgMissingAlt),
            new StructuredLinks(internalLinks, externalLinks, anchorTexts, anchorTextsTruncated),
            forms,
            semanticElements,
            lang,
            truncated);
    }

    // Story 9.4 Task 1.B: generic HTML structural primitives appended to the
    // structured handle. The runner exposes facts; the workflow agents
    // (`accessibility-quick-scan/agents/scanner.md`) interpret them. There is
    // no workflow-domain vocabulary in this file (FR23 / NFR25 — runner stays
    // workflow-generic). The 200-entry heading cap and 100-entry anchor cap
    // are defensive ceilings against pathological inputs, not workflow rules.
    private const int HeadingSequenceCap = 200;
    private const int AnchorTextsCap = 100;

    private static StructuredForms BuildForms(AngleSharp.Dom.IDocument document)
    {
        int fieldCount = 0;
        int fieldsWithLabel = 0;
        foreach (var field in document.QuerySelectorAll("input, textarea, select"))
        {
            if (field.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
            {
                var type = (field.GetAttribute("type") ?? "text").ToLowerInvariant();
                if (type is "hidden" or "submit" or "button" or "reset" or "image")
                    continue;
            }
            fieldCount++;
            if (IsFieldLabelled(field, document))
                fieldsWithLabel++;
        }
        return new StructuredForms(
            FieldCount: fieldCount,
            FieldsWithLabel: fieldsWithLabel,
            FieldsMissingLabel: fieldCount - fieldsWithLabel);
    }

    private static StructuredSemanticElements BuildSemanticElements(AngleSharp.Dom.IDocument document)
        => new(
            Main:   HasSemanticElement(document, "main",   "main"),
            Nav:    HasSemanticElement(document, "nav",    "navigation"),
            Header: HasSemanticElement(document, "header", "banner"),
            Footer: HasSemanticElement(document, "footer", "contentinfo"));

    private static bool HasSemanticElement(AngleSharp.Dom.IDocument document, string element, string role)
    {
        if (document.QuerySelector(element) != null)
            return true;
        // ARIA `role` is a space-separated token list per spec — use ~= for case-insensitive token match.
        return document.QuerySelector($"[role~=\"{role}\" i]") != null;
    }

    private static string CollapseWhitespace(string raw)
    {
        // Trim + whitespace-collapse, **preserving original case** (the scanner
        // prompt does case-insensitive matching itself; preserving case lets
        // future workflows do case-sensitive analysis if they need it).
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var parts = raw.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        return string.Join(' ', parts);
    }

    private static bool IsFieldLabelled(AngleSharp.Dom.IElement field, AngleSharp.Dom.IDocument document)
    {
        // (a) field has an id and a <label for="<id>"> exists
        var id = field.GetAttribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            foreach (var label in document.QuerySelectorAll("label[for]"))
            {
                if (string.Equals(label.GetAttribute("for"), id, StringComparison.Ordinal))
                    return true;
            }
        }

        // (b) field is a descendant of a <label> element
        var ancestor = field.ParentElement;
        while (ancestor != null)
        {
            if (ancestor.TagName.Equals("LABEL", StringComparison.OrdinalIgnoreCase))
                return true;
            ancestor = ancestor.ParentElement;
        }

        // (c) non-empty aria-label
        var ariaLabel = field.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            return true;

        // (d) aria-labelledby: dereference each IDREF, concatenate TextContent,
        //     treat as labelled iff concatenated trimmed result is non-empty.
        //     Q3 (architect-locked 2026-04-10): unresolved IDREFs contribute
        //     empty text — they do not throw and do not disqualify the others.
        var labelledBy = field.GetAttribute("aria-labelledby");
        if (!string.IsNullOrEmpty(labelledBy))
        {
            var idrefs = labelledBy.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var idref in idrefs)
            {
                var target = document.GetElementById(idref);
                if (target != null)
                    sb.Append(target.TextContent);
            }
            if (!string.IsNullOrWhiteSpace(sb.ToString()))
                return true;
        }

        return false;
    }

    // P1 (Story 9.1b code-review fix pass 2026-04-09): the previous schema also
    // exposed `truncated_during_parse`, which was wired to the same value as
    // `truncated` and could never diverge. Two semantically distinct fields
    // collapsed to one bit was contract drift; the honest fix is to drop the
    // dead alias rather than fabricate a second signal. Scanner.md and the
    // tests were updated in the same commit.
    // Story 9.4 Task 1.B (re-review patch): top-level generic primitives
    // appended additively. CQA's existing scanner / analyser / reporter tests
    // and prompts consume the pre-9.4 shape unchanged. Do NOT rename, reorder,
    // or alter any existing fields or JsonPropertyName values. New fields
    // added here must be generic HTML primitives that any future workflow
    // could reuse — no workflow-domain vocabulary in this file.
    private sealed record StructuredFetchHandle(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("meta_description")] string? MetaDescription,
        [property: JsonPropertyName("headings")] StructuredHeadings Headings,
        [property: JsonPropertyName("word_count")] int WordCount,
        [property: JsonPropertyName("images")] StructuredImages Images,
        [property: JsonPropertyName("links")] StructuredLinks Links,
        [property: JsonPropertyName("forms")] StructuredForms Forms,
        [property: JsonPropertyName("semantic_elements")] StructuredSemanticElements SemanticElements,
        [property: JsonPropertyName("lang")] string? Lang,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record StructuredHeadings(
        [property: JsonPropertyName("h1")] IReadOnlyList<string> H1,
        [property: JsonPropertyName("h2")] IReadOnlyList<string> H2,
        [property: JsonPropertyName("h3_h6_count")] int H3H6Count,
        [property: JsonPropertyName("sequence")] IReadOnlyList<string> Sequence);

    private sealed record StructuredImages(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("with_alt")] int WithAlt,
        [property: JsonPropertyName("missing_alt")] int MissingAlt);

    private sealed record StructuredLinks(
        [property: JsonPropertyName("internal")] int Internal,
        [property: JsonPropertyName("external")] int External,
        [property: JsonPropertyName("anchor_texts")] IReadOnlyList<string> AnchorTexts,
        [property: JsonPropertyName("anchor_texts_truncated")] bool AnchorTextsTruncated);

    private sealed record StructuredForms(
        [property: JsonPropertyName("field_count")] int FieldCount,
        [property: JsonPropertyName("fields_with_label")] int FieldsWithLabel,
        [property: JsonPropertyName("fields_missing_label")] int FieldsMissingLabel);

    private sealed record StructuredSemanticElements(
        [property: JsonPropertyName("main")] bool Main,
        [property: JsonPropertyName("nav")] bool Nav,
        [property: JsonPropertyName("header")] bool Header,
        [property: JsonPropertyName("footer")] bool Footer);

    private sealed record FetchUrlHandle(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("size_bytes")] long SizeBytes,
        [property: JsonPropertyName("saved_to")] string? SavedTo,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private static string ExtractStringArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        var stringValue = value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString()!,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };

        if (string.IsNullOrWhiteSpace(stringValue))
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        return stringValue;
    }
}
