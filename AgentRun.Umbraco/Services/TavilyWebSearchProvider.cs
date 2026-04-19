using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Services;

/// <summary>
/// Tavily Search API adapter. POST <c>https://api.tavily.com/search</c>
/// with Bearer auth header. Research-confirmed shape (2026-04-18, Story
/// 11.8 Task 5.1):
/// <list type="bullet">
///   <item>Bearer-header auth is primary; prior <c>api_key</c> in-body form retired</item>
///   <item><c>max_results</c> default 5, ceiling 20; adapter clamps internally</item>
///   <item><c>time_range</c> long-form <c>day</c>/<c>week</c>/<c>month</c>/<c>year</c>; omit for <see cref="WebSearchFreshness.All"/></item>
///   <item>response fields <c>results[].{title,url,content,score}</c>; no standard published-date field</item>
/// </list>
/// API keys are never logged, never surfaced in exception messages.
/// </summary>
public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    private const string ProviderNameValue = "Tavily";
    private const string Endpoint = "https://api.tavily.com/search";
    private const int MaxResults = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AgentRunOptions> _options;
    private readonly ILogger<TavilyWebSearchProvider> _logger;

    public TavilyWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AgentRunOptions> options,
        ILogger<TavilyWebSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string Name => ProviderNameValue;

    public async Task<WebSearchResult[]> SearchAsync(WebSearchQuery query, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();

        var clampedCount = Math.Max(1, Math.Min(query.Count, MaxResults));
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["query"] = query.Query,
            ["max_results"] = clampedCount,
            ["search_depth"] = "basic"
        };
        if (MapTimeRange(query.Freshness) is { } timeRange)
            payload["time_range"] = timeRange;

        var bodyJson = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _httpClientFactory.CreateClient("WebSearch");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new WebSearchException("Tavily web search timed out after 30s.");
        }
        catch (HttpRequestException ex)
        {
            throw new WebSearchException($"Tavily web search transport failure: {ex.GetType().Name}");
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = ParseRetryAfterSeconds(response);
                throw new WebSearchRateLimitedException(ProviderNameValue, retryAfter);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new WebSearchNotConfiguredException(ProviderNameValue, (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new WebSearchException(
                    $"Tavily web search returned HTTP {(int)response.StatusCode}.");
            }

            TavilyResponse? parsed;
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                parsed = JsonSerializer.Deserialize<TavilyResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                throw new WebSearchException("Tavily web search returned a malformed response body.");
            }
            catch (HttpRequestException ex)
            {
                throw new WebSearchException($"Tavily web search body-read failure: {ex.GetType().Name}");
            }
            catch (IOException ex)
            {
                throw new WebSearchException($"Tavily web search body-read failure: {ex.GetType().Name}");
            }

            var rawResults = (IEnumerable<TavilyResult>?)parsed?.Results ?? Array.Empty<TavilyResult>();
            var results = rawResults.Select(MapResult).ToArray();

            _logger.LogInformation(
                "web_search success via {ProviderName}: {ResultCount} results for query '{NormalisedQuery}'",
                ProviderNameValue,
                results.Length,
                WebSearchCache.NormaliseQuery(query.Query));

            return results;
        }
        finally
        {
            response.Dispose();
        }
    }

    private string ResolveApiKey()
    {
        var key = _options.Value.WebSearch?.Providers is { } providers
                  && providers.TryGetValue(ProviderNameValue, out var cfg)
            ? cfg.ApiKey
            : null;

        if (string.IsNullOrWhiteSpace(key))
            throw new WebSearchNotConfiguredException(ProviderNameValue);

        return key;
    }

    private static string? MapTimeRange(WebSearchFreshness freshness) => freshness switch
    {
        WebSearchFreshness.LastDay => "day",
        WebSearchFreshness.LastWeek => "week",
        WebSearchFreshness.LastMonth => "month",
        WebSearchFreshness.LastYear => "year",
        _ => null
    };

    private static int? ParseRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return (int)delta.TotalSeconds;
        return null;
    }

    private static WebSearchResult MapResult(TavilyResult raw)
        => new(
            Title: raw.Title ?? string.Empty,
            Url: raw.Url ?? string.Empty,
            Snippet: raw.Content ?? string.Empty,
            PublishedDate: null,
            SourceProvider: ProviderNameValue,
            RelevanceScore: raw.Score);

    private sealed class TavilyResponse
    {
        [JsonPropertyName("results")] public List<TavilyResult>? Results { get; set; }
    }

    private sealed class TavilyResult
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("score")] public double? Score { get; set; }
    }
}
