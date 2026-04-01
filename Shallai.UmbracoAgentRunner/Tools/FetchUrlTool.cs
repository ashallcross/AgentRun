using System.Text;
using System.Text.Json;
using Shallai.UmbracoAgentRunner.Security;

namespace Shallai.UmbracoAgentRunner.Tools;

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

    private const int JsonSizeLimitBytes = 204_800;   // 200KB
    private const int HtmlSizeLimitBytes = 102_400;   // 100KB

    private readonly SsrfProtection _ssrfProtection;
    private readonly IHttpClientFactory _httpClientFactory;

    public string Name => "fetch_url";

    public string Description => "Fetches the contents of a URL via HTTP GET";

    public JsonElement? ParameterSchema => Schema;

    public FetchUrlTool(SsrfProtection ssrfProtection, IHttpClientFactory httpClientFactory)
    {
        _ssrfProtection = ssrfProtection;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var urlString = ExtractStringArgument(arguments, "url");

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
            throw new ToolExecutionException($"Invalid URL: '{urlString}'");

        await _ssrfProtection.ValidateUrlAsync(uri, cancellationToken);

        var client = _httpClientFactory.CreateClient("FetchUrl");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ToolExecutionException("Request timed out after 15 seconds");
        }
        catch (HttpRequestException ex)
        {
            throw new ToolExecutionException($"Connection failed: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";

            var limit = GetSizeLimit(response.Content.Headers.ContentType?.MediaType);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[limit + 1];
            var totalRead = 0;
            int bytesRead;
            while (totalRead < buffer.Length &&
                   (bytesRead = await stream.ReadAsync(
                       buffer.AsMemory(totalRead, buffer.Length - totalRead),
                       cancellationToken)) > 0)
            {
                totalRead += bytesRead;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, Math.Min(totalRead, limit));
            if (totalRead > limit)
                text += $"\n\n[Response truncated at {limit / 1024}KB]";

            return text;
        }
    }

    private static int GetSizeLimit(string? mediaType)
    {
        return mediaType switch
        {
            "text/html" or "text/xml" or "application/xml" or "application/xhtml+xml" => HtmlSizeLimitBytes,
            _ => JsonSizeLimitBytes
        };
    }

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
