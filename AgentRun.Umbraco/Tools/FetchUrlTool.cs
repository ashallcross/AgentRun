using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;
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

    // Without an explicit User-Agent, many WAFs / CDNs (Cloudflare, Fastly,
    // AWS WAF) and sites with bot protection (Wikipedia, GitHub, news sites)
    // return 403 to requests with no UA, treating them as suspicious script
    // traffic. A sane generic engine default unblocks any workflow that
    // fetches arbitrary public URLs. NOT workflow-specific — this is a
    // property of fetch_url. A custom UA would become a ToolLimitResolver /
    // workflow-YAML tunable, not a hardcoded change here.
    private const string DefaultUserAgent = "AgentRun/1.0";

    private static readonly JsonSerializerOptions HandleJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly SsrfProtection _ssrfProtection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IToolLimitResolver _limitResolver;
    private readonly IHtmlStructureExtractor _htmlExtractor;
    private readonly IFetchCacheWriter _cacheWriter;
    private readonly ILogger<FetchUrlTool> _logger;

    public string Name => "fetch_url";

    public string Description => "Fetches the contents of a URL via HTTP GET. Returns a small JSON handle " +
                                 "with metadata and a `saved_to` relative path; use `read_file` to load the cached body.";

    public JsonElement? ParameterSchema => Schema;

    public FetchUrlTool(
        SsrfProtection ssrfProtection,
        IHttpClientFactory httpClientFactory,
        IToolLimitResolver limitResolver,
        IHtmlStructureExtractor htmlExtractor,
        IFetchCacheWriter cacheWriter,
        ILogger<FetchUrlTool>? logger = null)
    {
        _ssrfProtection = ssrfProtection;
        _httpClientFactory = httpClientFactory;
        _limitResolver = limitResolver;
        _htmlExtractor = htmlExtractor;
        _cacheWriter = cacheWriter;
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

        // Tool tuning values must come through the resolver chain. Missing
        // Step/Workflow on the execution context is an engine wiring bug.
        // Throw a typed AgentRunException subtype so LlmErrorClassifier does
        // not silently rewrite this as a generic provider failure.
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "FetchUrlTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        // Raw mode requires the instance folder for cache writes. Structured
        // mode skips the cache, so the InstanceFolderPath check is only
        // enforced for raw.
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
            // return the existing cached content without hitting the network.
            // Makes Story 10.6's Option 3 retry-restart cheap when the
            // conversation was wiped but the cache survives. Structured mode
            // does not cache (Q1 (a), 9.1b), so cache-on-hit does not apply there.
            // Known limitation: the original Content-Type header is not preserved
            // across cache reuse — the handle defaults to "text/html".
            if (extractMode == ExtractMode.Raw)
            {
                var cacheHit = await _cacheWriter.TryReadHandleAsync(context.InstanceFolderPath!, urlString, timeoutCts.Token);
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
                    // Empty-body short-circuit must still return the structured
                    // shape with zero-valued versions of every field, including
                    // the generic primitives.
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

                // Empty-body raw branch: nothing to cache, so we do NOT go through
                // WriteHandleAsync (which always writes). IFetchCacheWriter.BuildEmptyHandle
                // keeps the JSON wire shape pinned to a single implementation so the
                // empty-body handle can't drift from the cache-written handle's shape.
                return _cacheWriter.BuildEmptyHandle(urlString, status, contentType);
            }

            var truncated = totalRead > maxBytes;
            // The un-marked slice is the source of truth. The raw branch
            // derives bytesToWrite from this slice + marker; the structured
            // branch parses this slice directly so AngleSharp never sees the
            // truncation marker text as HTML.
            var unmarkedLength = truncated ? maxBytes : totalRead;

            // Structured mode parses the un-marked bytes via AngleSharp and
            // returns a small structured handle. No disk write.
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

                StructuredHtmlContent content;
                try
                {
                    content = _htmlExtractor.Extract(buffer, uri, unmarkedLength, truncated);
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

                var structured = new StructuredFetchHandle(
                    urlString,
                    status,
                    content.Title,
                    content.MetaDescription,
                    content.Headings,
                    content.WordCount,
                    content.Images,
                    content.Links,
                    content.Forms,
                    content.SemanticElements,
                    content.Lang,
                    truncated);
                return JsonSerializer.Serialize(structured, HandleJsonOptions);
            }

            return await _cacheWriter.WriteHandleAsync(
                context.InstanceFolderPath!,
                urlString,
                status,
                contentType,
                buffer,
                unmarkedLength,
                truncated,
                timeoutCts.Token);
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

    // Top-level generic primitives. Existing CQA scanner / analyser / reporter
    // tests and prompts consume this shape unchanged — do NOT rename, reorder,
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
