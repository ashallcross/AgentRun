using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Tools;

/// <summary>
/// <c>web_search</c> tool. Resolves a provider via
/// <see cref="IWebSearchProviderFactory"/>, short-circuits repeated queries
/// through <see cref="IWebSearchCache"/>, and surfaces failure modes as
/// structured JSON results (never throws to the step unless the caller
/// cancels). Story 11.8.
/// </summary>
public sealed class WebSearchTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The search query"
            },
            "count": {
              "type": "integer",
              "default": 10,
              "minimum": 1,
              "maximum": 25,
              "description": "Number of results to return. Provider-specific maxima may clamp lower (Brave: 20, Tavily: 20)."
            },
            "freshness": {
              "type": "string",
              "enum": ["last_day", "last_week", "last_month", "last_year", "all"],
              "default": "all",
              "description": "Restrict results to a recent time window. Use `all` for no restriction."
            }
          },
          "required": ["query"]
        }
        """).RootElement;

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    // AC6 dedup — one Warning per (providerName, stepId) over the process
    // lifetime so a chatty LLM retrying a not-configured tool call within a
    // single step does not spam ops logs. StepIds are GUIDs so bounded growth
    // is not a concern for realistic workflow volumes.
    private static readonly ConcurrentDictionary<(string ProviderName, string StepId), byte> NotConfiguredWarned = new();

    private readonly IWebSearchProviderFactory _providerFactory;
    private readonly IWebSearchCache _cache;
    private readonly IOptions<AgentRunOptions> _options;
    private readonly ILogger<WebSearchTool> _logger;

    public WebSearchTool(
        IWebSearchProviderFactory providerFactory,
        IWebSearchCache cache,
        IOptions<AgentRunOptions> options,
        ILogger<WebSearchTool> logger)
    {
        _providerFactory = providerFactory;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public string Name => "web_search";

    public string Description =>
        "Searches the web via a configured provider (Brave by default) and returns a list of results with title, url, and snippet. " +
        "URLs are NOT automatically fetched — call `fetch_url` on any result URL you need to read in full. " +
        "Returns structured error JSON for rate-limit, missing-key, and transport failures so you can reason about them.";

    public JsonElement? ParameterSchema => Schema;

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryExtractQuery(arguments, out var query, out var queryError))
            return InvalidArgument(queryError!);

        if (!TryExtractCount(arguments, out var count, out var countError))
            return InvalidArgument(countError!);

        if (!TryExtractFreshness(arguments, out var freshness, out var freshnessError))
            return InvalidArgument(freshnessError!);

        string providerName;
        IWebSearchProvider provider;
        try
        {
            providerName = ResolveProviderName();
            provider = _providerFactory.GetAsync(providerName, cancellationToken);
        }
        catch (WebSearchNotConfiguredException ex)
        {
            LogNotConfiguredOnce(ex.ProviderName, context.StepId);
            return NotConfigured(ex.ProviderName);
        }

        var webQuery = new WebSearchQuery(query!, count, freshness);

        if (_cache.TryGet(providerName, webQuery, out var cached))
        {
            _logger.LogDebug(
                "web_search cache hit for provider {ProviderName}, query '{NormalisedQuery}'",
                providerName, WebSearchCache.NormaliseQuery(query!));
            return cached;
        }

        WebSearchResult[] results;
        try
        {
            results = await provider.SearchAsync(webQuery, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WebSearchNotConfiguredException ex)
        {
            LogNotConfiguredOnce(ex.ProviderName, context.StepId);
            return NotConfigured(ex.ProviderName);
        }
        catch (WebSearchRateLimitedException ex)
        {
            _logger.LogWarning(
                "web_search rate-limited by provider {ProviderName}, retry-after {RetryAfterSeconds}s (step {StepId})",
                ex.ProviderName, ex.RetryAfterSeconds, context.StepId);
            return RateLimited(ex);
        }
        catch (WebSearchException ex)
        {
            _logger.LogWarning(
                "web_search transport / provider failure via {ProviderName} (step {StepId})",
                providerName, context.StepId);
            return TransportError(providerName, ex.Message);
        }

        var resultJson = JsonSerializer.Serialize(
            new SuccessEnvelope(providerName, results),
            ResultJsonOptions);

        _cache.Set(providerName, webQuery, resultJson, _options.Value.WebSearch?.CacheTtl ?? TimeSpan.FromHours(1));

        return resultJson;
    }

    private string ResolveProviderName()
    {
        var webSearch = _options.Value.WebSearch;
        var configuredDefault = webSearch?.DefaultProvider;

        if (!string.IsNullOrWhiteSpace(configuredDefault))
        {
            if (!HasConfiguredKey(configuredDefault))
                throw new WebSearchNotConfiguredException(configuredDefault);
            return configuredDefault;
        }

        foreach (var name in _providerFactory.GetRegisteredProviderNames())
        {
            if (HasConfiguredKey(name))
                return name;
        }

        var registered = _providerFactory.GetRegisteredProviderNames();
        var primary = registered.Count > 0 ? registered[0] : "Brave";
        throw new WebSearchNotConfiguredException(primary);
    }

    private bool HasConfiguredKey(string providerName)
    {
        var providers = _options.Value.WebSearch?.Providers;
        if (providers is null)
            return false;
        if (!providers.TryGetValue(providerName, out var cfg))
            return false;
        return !string.IsNullOrWhiteSpace(cfg.ApiKey);
    }

    private static bool TryExtractQuery(IDictionary<string, object?> args, out string? query, out string? error)
    {
        query = null;
        error = null;
        if (!args.TryGetValue("query", out var raw) || raw is null)
        {
            error = "'query' is required";
            return false;
        }
        var value = raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString() ?? string.Empty,
            _ => raw.ToString() ?? string.Empty
        };
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "'query' must be a non-empty string";
            return false;
        }
        query = value;
        return true;
    }

    private static bool TryExtractCount(IDictionary<string, object?> args, out int count, out string? error)
    {
        count = 10;
        error = null;
        if (!args.TryGetValue("count", out var raw) || raw is null)
            return true;

        int? parsed = raw switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt32(out var n) => n,
            string s when int.TryParse(s, out var n) => n,
            _ => null
        };

        if (parsed is null || parsed < 1 || parsed > 25)
        {
            error = "'count' must be an integer in [1, 25]";
            return false;
        }
        count = parsed.Value;
        return true;
    }

    private static bool TryExtractFreshness(IDictionary<string, object?> args, out WebSearchFreshness freshness, out string? error)
    {
        freshness = WebSearchFreshness.All;
        error = null;
        if (!args.TryGetValue("freshness", out var raw) || raw is null)
            return true;

        var value = raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString() ?? string.Empty,
            _ => raw.ToString() ?? string.Empty
        };

        // Exact-match only; no case variants, no whitespace tolerance. The
        // schema declares the enum in lowercase snake_case and that is the
        // single accepted wire form — anything else is an error, never a
        // silent remap.
        switch (value)
        {
            case "all": freshness = WebSearchFreshness.All; return true;
            case "last_day": freshness = WebSearchFreshness.LastDay; return true;
            case "last_week": freshness = WebSearchFreshness.LastWeek; return true;
            case "last_month": freshness = WebSearchFreshness.LastMonth; return true;
            case "last_year": freshness = WebSearchFreshness.LastYear; return true;
            default:
                error = "'freshness' must be one of: last_day, last_week, last_month, last_year, all";
                return false;
        }
    }

    private void LogNotConfiguredOnce(string providerName, string stepId)
    {
        if (NotConfiguredWarned.TryAdd((providerName, stepId), 0))
        {
            _logger.LogWarning(
                "web_search not configured: provider {ProviderName} has no API key (step {StepId})",
                providerName, stepId);
        }
    }

    private static string InvalidArgument(string message)
        => JsonSerializer.Serialize(new ErrorEnvelope("invalid_argument", null, message, null), ResultJsonOptions);

    private static string NotConfigured(string providerName)
        => JsonSerializer.Serialize(new ErrorEnvelope(
            "not_configured",
            providerName,
            $"web_search is not configured; add an API key under AgentRun:WebSearch:Providers:{providerName}:ApiKey in appsettings.json",
            null), ResultJsonOptions);

    private static string RateLimited(WebSearchRateLimitedException ex)
        => JsonSerializer.Serialize(new ErrorEnvelope(
            "rate_limited",
            ex.ProviderName,
            ex.RetryAfterSeconds is int s
                ? $"Search provider rate limit exceeded. Retry after {s} seconds, or try a different provider."
                : "Search provider rate limit exceeded. Try a different provider or wait before retrying.",
            ex.RetryAfterSeconds), ResultJsonOptions);

    private static string TransportError(string providerName, string message)
        => JsonSerializer.Serialize(new ErrorEnvelope("transport", providerName, message, null), ResultJsonOptions);

    private sealed record SuccessEnvelope(
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("results")] WebSearchResult[] Results);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("retryAfterSeconds")] int? RetryAfterSeconds);
}
