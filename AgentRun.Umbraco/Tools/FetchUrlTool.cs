using System.Text;
using System.Text.Json;
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

    private readonly SsrfProtection _ssrfProtection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IToolLimitResolver _limitResolver;

    public string Name => "fetch_url";

    public string Description => "Fetches the contents of a URL via HTTP GET";

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
        // Missing Step/Workflow on the execution context is a wiring bug — fail loud.
        if (context.Step is null || context.Workflow is null)
        {
            throw new InvalidOperationException(
                "FetchUrlTool requires ToolExecutionContext.Step and ToolExecutionContext.Workflow to be set " +
                "so the tool limit resolver can read step/workflow tuning values.");
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

            var text = Encoding.UTF8.GetString(buffer, 0, Math.Min(totalRead, maxBytes));
            if (totalRead > maxBytes)
                text += $"\n\n[Response truncated at {maxBytes} bytes]";

            return text;
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
