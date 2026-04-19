using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Services;

/// <summary>
/// Brave Search API adapter. GET <c>https://api.search.brave.com/res/v1/web/search</c>
/// with <c>X-Subscription-Token</c> header. Research-confirmed shape
/// (2026-04-18, Story 11.8 Task 4.1):
/// <list type="bullet">
///   <item><c>count</c> max 20 (adapter clamps internally)</item>
///   <item>freshness values <c>pd</c>/<c>pw</c>/<c>pm</c>/<c>py</c>; omit param for <see cref="WebSearchFreshness.All"/></item>
///   <item>response fields <c>web.results[].{title,url,description,page_age|age}</c></item>
/// </list>
/// API keys are never logged, never surfaced in exception messages.
/// </summary>
public sealed class BraveWebSearchProvider : IWebSearchProvider
{
    private const string ProviderNameValue = "Brave";
    private const string Endpoint = "https://api.search.brave.com/res/v1/web/search";
    private const int MaxCount = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AgentRunOptions> _options;
    private readonly ILogger<BraveWebSearchProvider> _logger;

    public BraveWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AgentRunOptions> options,
        ILogger<BraveWebSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string Name => ProviderNameValue;

    public async Task<WebSearchResult[]> SearchAsync(WebSearchQuery query, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();

        var clampedCount = Math.Max(1, Math.Min(query.Count, MaxCount));
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["q"] = query.Query;
        qs["count"] = clampedCount.ToString(CultureInfo.InvariantCulture);
        var freshness = MapFreshness(query.Freshness);
        if (freshness is not null)
            qs["freshness"] = freshness;

        var url = $"{Endpoint}?{qs}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Subscription-Token", apiKey);
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
            throw new WebSearchException("Brave web search timed out after 30s.");
        }
        catch (HttpRequestException ex)
        {
            throw new WebSearchException($"Brave web search transport failure: {ex.GetType().Name}");
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
                    $"Brave web search returned HTTP {(int)response.StatusCode}.");
            }

            BraveResponse? parsed;
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                parsed = JsonSerializer.Deserialize<BraveResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                throw new WebSearchException("Brave web search returned a malformed response body.");
            }
            catch (HttpRequestException ex)
            {
                throw new WebSearchException($"Brave web search body-read failure: {ex.GetType().Name}");
            }
            catch (IOException ex)
            {
                throw new WebSearchException($"Brave web search body-read failure: {ex.GetType().Name}");
            }

            var rawResults = (IEnumerable<BraveResult>?)parsed?.Web?.Results ?? Array.Empty<BraveResult>();
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

    private static string? MapFreshness(WebSearchFreshness freshness) => freshness switch
    {
        WebSearchFreshness.LastDay => "pd",
        WebSearchFreshness.LastWeek => "pw",
        WebSearchFreshness.LastMonth => "pm",
        WebSearchFreshness.LastYear => "py",
        _ => null
    };

    private static int? ParseRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return (int)delta.TotalSeconds;
        return null;
    }

    private static WebSearchResult MapResult(BraveResult raw)
    {
        var rawDate = raw.PageAge ?? raw.Age;
        DateTimeOffset? publishedDate = null;
        if (!string.IsNullOrWhiteSpace(rawDate)
            && DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            publishedDate = parsed;
        }

        return new WebSearchResult(
            Title: raw.Title ?? string.Empty,
            Url: raw.Url ?? string.Empty,
            Snippet: raw.Description ?? string.Empty,
            PublishedDate: publishedDate,
            SourceProvider: ProviderNameValue,
            RelevanceScore: null);
    }

    private sealed class BraveResponse
    {
        [JsonPropertyName("web")] public BraveWebBlock? Web { get; set; }
    }

    private sealed class BraveWebBlock
    {
        [JsonPropertyName("results")] public List<BraveResult>? Results { get; set; }
    }

    private sealed class BraveResult
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("page_age")] public string? PageAge { get; set; }
        [JsonPropertyName("age")] public string? Age { get; set; }
    }
}
