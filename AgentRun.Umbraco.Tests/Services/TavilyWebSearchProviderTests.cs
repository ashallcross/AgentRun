using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Services;

namespace AgentRun.Umbraco.Tests.Services;

[TestFixture]
public class TavilyWebSearchProviderTests
{
    private const string SentinelApiKey = "test-key-0123456789-SENTINEL";

    private IHttpClientFactory _httpClientFactory = null!;
    private TavilyWebSearchProvider _provider = null!;
    private List<(HttpRequestMessage request, string? body)> _capturedRequests = null!;

    [SetUp]
    public void SetUp()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _capturedRequests = new List<(HttpRequestMessage, string?)>();
        var options = Options.Create(new AgentRunOptions
        {
            WebSearch = new AgentRunWebSearchOptions
            {
                Providers =
                {
                    ["Tavily"] = new AgentRunWebSearchProviderOptions { ApiKey = SentinelApiKey }
                }
            }
        });
        _provider = new TavilyWebSearchProvider(
            _httpClientFactory,
            options,
            NullLogger<TavilyWebSearchProvider>.Instance);
    }

    private void SetupHttp(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var mock = new MockHttpHandler(async (req, _) =>
        {
            string? body = null;
            if (req.Content is not null)
                body = await req.Content.ReadAsStringAsync();
            _capturedRequests.Add((req, body));
            return handler(req);
        });
        _httpClientFactory.CreateClient("WebSearch").Returns(new HttpClient(mock));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string CannedOk = """
        {
          "query": "q",
          "results": [
            {"title":"First","url":"https://a.example","content":"first snippet","score":0.95},
            {"title":"Second","url":"https://b.example","content":"second snippet","score":0.72}
          ]
        }
        """;

    [Test]
    public void Name_IsTavily()
    {
        Assert.That(_provider.Name, Is.EqualTo("Tavily"));
    }

    [Test]
    public async Task SearchAsync_SendsPostWithBearerHeader_AndJsonBody()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(
            new WebSearchQuery("test query", 5, WebSearchFreshness.LastMonth),
            CancellationToken.None);

        Assert.That(_capturedRequests, Has.Count.EqualTo(1));
        var (req, body) = _capturedRequests[0];
        Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(req.RequestUri!.ToString(), Is.EqualTo("https://api.tavily.com/search"));
        Assert.That(req.Headers.Authorization, Is.Not.Null);
        Assert.That(req.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(req.Headers.Authorization.Parameter, Is.EqualTo(SentinelApiKey));

        Assert.That(body, Is.Not.Null);
        using var doc = JsonDocument.Parse(body!);
        Assert.That(doc.RootElement.GetProperty("query").GetString(), Is.EqualTo("test query"));
        Assert.That(doc.RootElement.GetProperty("max_results").GetInt32(), Is.EqualTo(5));
        Assert.That(doc.RootElement.GetProperty("time_range").GetString(), Is.EqualTo("month"));
        // Bearer auth — api_key must NOT be in the body
        Assert.That(doc.RootElement.TryGetProperty("api_key", out _), Is.False);
    }

    [Test]
    public async Task SearchAsync_OmitsTimeRange_WhenAll()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(
            new WebSearchQuery("q", 3, WebSearchFreshness.All),
            CancellationToken.None);

        var body = _capturedRequests[0].body!;
        using var doc = JsonDocument.Parse(body);
        Assert.That(doc.RootElement.TryGetProperty("time_range", out _), Is.False);
    }

    [Test]
    public async Task SearchAsync_ClampsMaxResultsTo20_WhenAbove()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(
            new WebSearchQuery("q", 25, WebSearchFreshness.All),
            CancellationToken.None);

        var body = _capturedRequests[0].body!;
        using var doc = JsonDocument.Parse(body);
        Assert.That(doc.RootElement.GetProperty("max_results").GetInt32(), Is.EqualTo(20));
    }

    [Test]
    public async Task SearchAsync_MapsResponseFields_IncludingScore_PublishedDateAlwaysNull()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        var results = await _provider.SearchAsync(
            new WebSearchQuery("q", 5, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(results, Has.Length.EqualTo(2));
        Assert.That(results[0].Title, Is.EqualTo("First"));
        Assert.That(results[0].Url, Is.EqualTo("https://a.example"));
        Assert.That(results[0].Snippet, Is.EqualTo("first snippet"));
        Assert.That(results[0].SourceProvider, Is.EqualTo("Tavily"));
        Assert.That(results[0].RelevanceScore, Is.EqualTo(0.95).Within(0.001));
        Assert.That(results[0].PublishedDate, Is.Null,
            "Tavily standard search response does not expose published_date — map to null");
        Assert.That(results[1].RelevanceScore, Is.EqualTo(0.72).Within(0.001));
    }

    [Test]
    public async Task SearchAsync_ZeroResults_ReturnsEmptyArray()
    {
        const string body = """{"results":[]}""";
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, body));

        var results = await _provider.SearchAsync(
            new WebSearchQuery("q", 10, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void SearchAsync_Http429_ThrowsRateLimited_WithRetryAfter()
    {
        SetupHttp(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return resp;
        });

        var ex = Assert.ThrowsAsync<WebSearchRateLimitedException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.ProviderName, Is.EqualTo("Tavily"));
        Assert.That(ex.RetryAfterSeconds, Is.EqualTo(45));
    }

    [Test]
    public void SearchAsync_Http429_WithoutRetryAfter_NullRetry()
    {
        SetupHttp(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var ex = Assert.ThrowsAsync<WebSearchRateLimitedException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.RetryAfterSeconds, Is.Null);
    }

    [Test]
    public void SearchAsync_Http401_Throws_NotConfigured_WithoutApiKeyInMessage()
    {
        SetupHttp(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // D3 — 401/403 rejection is semantically equivalent to "API key
        // missing": the configured key cannot reach the provider. Surfacing
        // this as WebSearchNotConfiguredException lets the tool return the
        // not_configured envelope so the LLM does not retry futilely.
        var ex = Assert.ThrowsAsync<WebSearchNotConfiguredException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.ProviderName, Is.EqualTo("Tavily"));
        Assert.That(ex.Message.ToLowerInvariant(), Does.Contain("authentication"));
        Assert.That(ex.Message, Does.Not.Contain(SentinelApiKey));
        Assert.That(ex.Message, Does.Contain("AgentRun:WebSearch:Providers:Tavily:ApiKey"));
    }

    [Test]
    public void SearchAsync_MissingApiKey_ThrowsNotConfigured()
    {
        var providerNoKey = new TavilyWebSearchProvider(
            _httpClientFactory,
            Options.Create(new AgentRunOptions { WebSearch = new AgentRunWebSearchOptions() }),
            NullLogger<TavilyWebSearchProvider>.Instance);

        var ex = Assert.ThrowsAsync<WebSearchNotConfiguredException>(async () =>
            await providerNoKey.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.ProviderName, Is.EqualTo("Tavily"));
    }

    [TestCase(WebSearchFreshness.LastDay, "day")]
    [TestCase(WebSearchFreshness.LastWeek, "week")]
    [TestCase(WebSearchFreshness.LastMonth, "month")]
    [TestCase(WebSearchFreshness.LastYear, "year")]
    public async Task SearchAsync_TimeRangeMapping_MatchesTavilyEnum(WebSearchFreshness input, string expected)
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(new WebSearchQuery("q", 5, input), CancellationToken.None);

        var body = _capturedRequests[0].body!;
        using var doc = JsonDocument.Parse(body);
        Assert.That(doc.RootElement.GetProperty("time_range").GetString(), Is.EqualTo(expected));
    }

    [Test]
    public void SearchAsync_MalformedJson_Throws_WithoutKeyOrBody()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, "not-json {{{"));

        var ex = Assert.ThrowsAsync<WebSearchException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.Message, Does.Not.Contain(SentinelApiKey));
        Assert.That(ex.Message, Does.Not.Contain("not-json"));
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
