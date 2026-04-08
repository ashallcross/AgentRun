using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;

namespace AgentRun.Umbraco.Tools;

public class FetchUrlTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "url": { "type": "string", "description": "The URL to fetch via HTTP GET" }
            },
            "required": ["url"]
        }
        """).RootElement;

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

        // Story 9.7: instance folder is required for response offloading.
        if (string.IsNullOrEmpty(context.InstanceFolderPath))
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

        try
        {
            await _ssrfProtection.ValidateUrlAsync(uri, timeoutCts.Token);

            var client = _httpClientFactory.CreateClient("FetchUrl");

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

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
            if (totalRead == 0)
            {
                var emptyHandle = new FetchUrlHandle(urlString, status, contentType, 0, null, false);
                return JsonSerializer.Serialize(emptyHandle, HandleJsonOptions);
            }

            var truncated = totalRead > maxBytes;
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
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

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
