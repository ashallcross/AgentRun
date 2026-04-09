using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;
using AngleSharp.Html.Parser;

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
                    "description": "Return shape. 'raw' (default) writes the body to the fetch cache and returns a small handle pointing at it. 'structured' parses the response with AngleSharp server-side and returns a small structured handle (title, meta description, headings, word count, image and link counts) without writing to disk."
                }
            },
            "required": ["url"]
        }
        """).RootElement;

    private static readonly char[] WhitespaceChars = { ' ', '\t', '\n', '\r', '\f', '\v' };

    private static readonly JsonSerializerOptions HandleJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly SsrfProtection _ssrfProtection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IToolLimitResolver _limitResolver;

    public string Name => "fetch_url";

    public string Description => "Fetches the contents of a URL via HTTP GET. Returns a small JSON handle " +
                                 "with metadata and a `saved_to` relative path; use `read_file` to load the cached body.";

    public JsonElement? ParameterSchema => Schema;

    public FetchUrlTool(
        SsrfProtection ssrfProtection,
        IHttpClientFactory httpClientFactory,
        IToolLimitResolver limitResolver)
    {
        _ssrfProtection = ssrfProtection;
        _httpClientFactory = httpClientFactory;
        _limitResolver = limitResolver;
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
                    var emptyStructured = new StructuredFetchHandle(
                        urlString,
                        status,
                        Title: null,
                        MetaDescription: null,
                        Headings: new StructuredHeadings(Array.Empty<string>(), Array.Empty<string>(), 0),
                        WordCount: 0,
                        Images: new StructuredImages(0, 0, 0),
                        Links: new StructuredLinks(0, 0),
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

        var h1 = document.QuerySelectorAll("h1")
            .Select(e => e.TextContent.Trim())
            .ToList();
        var h2 = document.QuerySelectorAll("h2")
            .Select(e => e.TextContent.Trim())
            .ToList();
        var h3h6Count = document.QuerySelectorAll("h3, h4, h5, h6").Length;

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
        }

        return new StructuredFetchHandle(
            url,
            status,
            title,
            metaDescription,
            new StructuredHeadings(h1, h2, h3h6Count),
            wordCount,
            new StructuredImages(imgTotal, imgWithAlt, imgMissingAlt),
            new StructuredLinks(internalLinks, externalLinks),
            truncated);
    }

    // P1 (Story 9.1b code-review fix pass 2026-04-09): the previous schema also
    // exposed `truncated_during_parse`, which was wired to the same value as
    // `truncated` and could never diverge. Two semantically distinct fields
    // collapsed to one bit was contract drift; the honest fix is to drop the
    // dead alias rather than fabricate a second signal. Scanner.md and the
    // tests were updated in the same commit.
    private sealed record StructuredFetchHandle(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("meta_description")] string? MetaDescription,
        [property: JsonPropertyName("headings")] StructuredHeadings Headings,
        [property: JsonPropertyName("word_count")] int WordCount,
        [property: JsonPropertyName("images")] StructuredImages Images,
        [property: JsonPropertyName("links")] StructuredLinks Links,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record StructuredHeadings(
        [property: JsonPropertyName("h1")] IReadOnlyList<string> H1,
        [property: JsonPropertyName("h2")] IReadOnlyList<string> H2,
        [property: JsonPropertyName("h3_h6_count")] int H3H6Count);

    private sealed record StructuredImages(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("with_alt")] int WithAlt,
        [property: JsonPropertyName("missing_alt")] int MissingAlt);

    private sealed record StructuredLinks(
        [property: JsonPropertyName("internal")] int Internal,
        [property: JsonPropertyName("external")] int External);

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
