using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Services;

namespace AgentRun.Umbraco.Tests.Services;

[TestFixture]
public class BraveWebSearchProviderTests
{
    private const string SentinelApiKey = "test-key-0123456789-SENTINEL";

    private IHttpClientFactory _httpClientFactory = null!;
    private BraveWebSearchProvider _provider = null!;
    private List<HttpRequestMessage> _capturedRequests = null!;

    [SetUp]
    public void SetUp()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _capturedRequests = new List<HttpRequestMessage>();
        var options = Options.Create(new AgentRunOptions
        {
            WebSearch = new AgentRunWebSearchOptions
            {
                Providers =
                {
                    ["Brave"] = new AgentRunWebSearchProviderOptions { ApiKey = SentinelApiKey }
                }
            }
        });
        _provider = new BraveWebSearchProvider(
            _httpClientFactory,
            options,
            NullLogger<BraveWebSearchProvider>.Instance);
    }

    private void SetupHttp(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var mock = new MockHttpHandler((req, _) =>
        {
            // Clone-capture before returning to keep message intact for assertions.
            _capturedRequests.Add(req);
            return Task.FromResult(handler(req));
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
          "type": "search",
          "query": {"original": "q"},
          "web": {
            "results": [
              {
                "title": "Umbraco 16 Release",
                "url": "https://umbraco.com/blog/16",
                "description": "Umbraco 16 released today...",
                "page_age": "2026-03-15T12:00:00"
              },
              {
                "title": "Second",
                "url": "https://example.com/second",
                "description": "second snippet",
                "age": "2026-02-01T10:00:00"
              },
              {
                "title": "Third",
                "url": "https://example.com/third",
                "description": "third snippet"
              }
            ]
          }
        }
        """;

    [Test]
    public void Name_IsBrave()
    {
        Assert.That(_provider.Name, Is.EqualTo("Brave"));
    }

    [Test]
    public async Task SearchAsync_SendsGetWithCorrectQueryString_AndSubscriptionHeader()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(
            new WebSearchQuery("test query", 5, WebSearchFreshness.LastWeek),
            CancellationToken.None);

        Assert.That(_capturedRequests, Has.Count.EqualTo(1));
        var req = _capturedRequests[0];
        Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(req.RequestUri!.Host, Is.EqualTo("api.search.brave.com"));
        Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/res/v1/web/search"));
        Assert.That(req.RequestUri.Query, Does.Contain("q=test+query").Or.Contain("q=test%20query"));
        Assert.That(req.RequestUri.Query, Does.Contain("count=5"));
        Assert.That(req.RequestUri.Query, Does.Contain("freshness=pw"));
        Assert.That(req.Headers.Contains("X-Subscription-Token"), Is.True);
        Assert.That(req.Headers.GetValues("X-Subscription-Token").First(), Is.EqualTo(SentinelApiKey));
    }

    [Test]
    public async Task SearchAsync_OmitsFreshnessParameterWhenAll()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(
            new WebSearchQuery("q", 3, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(_capturedRequests[0].RequestUri!.Query, Does.Not.Contain("freshness"));
    }

    [Test]
    public async Task SearchAsync_ClampsCountTo20_WhenAbove()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(
            new WebSearchQuery("q", 25, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(_capturedRequests[0].RequestUri!.Query, Does.Contain("count=20"));
    }

    [Test]
    public async Task SearchAsync_MapsResponseFields_IncludingPageAgeAndAgeFallback()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        var results = await _provider.SearchAsync(
            new WebSearchQuery("q", 5, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(results, Has.Length.EqualTo(3));
        Assert.That(results[0].Title, Is.EqualTo("Umbraco 16 Release"));
        Assert.That(results[0].Url, Is.EqualTo("https://umbraco.com/blog/16"));
        Assert.That(results[0].Snippet, Is.EqualTo("Umbraco 16 released today..."));
        Assert.That(results[0].PublishedDate, Is.Not.Null);
        Assert.That(results[0].SourceProvider, Is.EqualTo("Brave"));
        Assert.That(results[0].RelevanceScore, Is.Null);
        Assert.That(results[1].PublishedDate, Is.Not.Null, "fallback to age field when page_age absent");
        Assert.That(results[2].PublishedDate, Is.Null, "no date field present → null");
    }

    [Test]
    public async Task SearchAsync_UnparseableDateBecomesNull()
    {
        const string body = """
            {
              "web": {
                "results": [
                  {"title":"t","url":"https://x","description":"d","page_age":"3 hours ago"}
                ]
              }
            }
            """;
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, body));

        var results = await _provider.SearchAsync(
            new WebSearchQuery("q", 1, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(results[0].PublishedDate, Is.Null);
    }

    [Test]
    public async Task SearchAsync_ZeroResults_ReturnsEmptyArray()
    {
        const string body = """{"web":{"results":[]}}""";
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, body));

        var results = await _provider.SearchAsync(
            new WebSearchQuery("q", 10, WebSearchFreshness.All),
            CancellationToken.None);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void SearchAsync_Http429_ThrowsRateLimited_WithRetryAfterFromHeader()
    {
        SetupHttp(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return resp;
        });

        var ex = Assert.ThrowsAsync<WebSearchRateLimitedException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.ProviderName, Is.EqualTo("Brave"));
        Assert.That(ex.RetryAfterSeconds, Is.EqualTo(60));
    }

    [Test]
    public void SearchAsync_Http429_WithoutRetryAfter_ThrowsWithNullRetryAfter()
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

        Assert.That(ex!.ProviderName, Is.EqualTo("Brave"));
        Assert.That(ex.Message.ToLowerInvariant(), Does.Contain("authentication"));
        Assert.That(ex.Message, Does.Not.Contain(SentinelApiKey));
        Assert.That(ex.Message, Does.Contain("AgentRun:WebSearch:Providers:Brave:ApiKey"));
    }

    [Test]
    public void SearchAsync_Http500_Throws_WithoutApiKey()
    {
        SetupHttp(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = Assert.ThrowsAsync<WebSearchException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.Message, Does.Not.Contain(SentinelApiKey));
    }

    [Test]
    public void SearchAsync_MissingApiKey_ThrowsNotConfigured()
    {
        var providerNoKey = new BraveWebSearchProvider(
            _httpClientFactory,
            Options.Create(new AgentRunOptions
            {
                WebSearch = new AgentRunWebSearchOptions()
            }),
            NullLogger<BraveWebSearchProvider>.Instance);

        var ex = Assert.ThrowsAsync<WebSearchNotConfiguredException>(async () =>
            await providerNoKey.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.ProviderName, Is.EqualTo("Brave"));
        Assert.That(ex.Message, Does.Contain("AgentRun:WebSearch:Providers:Brave:ApiKey"));
    }

    [Test]
    public void SearchAsync_WhitespaceOnlyApiKey_ThrowsNotConfigured()
    {
        var providerBlankKey = new BraveWebSearchProvider(
            _httpClientFactory,
            Options.Create(new AgentRunOptions
            {
                WebSearch = new AgentRunWebSearchOptions
                {
                    Providers = { ["Brave"] = new AgentRunWebSearchProviderOptions { ApiKey = "   " } }
                }
            }),
            NullLogger<BraveWebSearchProvider>.Instance);

        Assert.ThrowsAsync<WebSearchNotConfiguredException>(async () =>
            await providerBlankKey.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));
    }

    [Test]
    public void SearchAsync_MalformedJson_Throws_WithoutResponseBodyOrKey()
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, "not-valid-json {{{"));

        var ex = Assert.ThrowsAsync<WebSearchException>(async () =>
            await _provider.SearchAsync(new WebSearchQuery("q", 5, WebSearchFreshness.All), CancellationToken.None));

        Assert.That(ex!.Message, Does.Not.Contain(SentinelApiKey));
        Assert.That(ex.Message, Does.Not.Contain("not-valid-json"));
    }

    [TestCase(WebSearchFreshness.LastDay, "pd")]
    [TestCase(WebSearchFreshness.LastWeek, "pw")]
    [TestCase(WebSearchFreshness.LastMonth, "pm")]
    [TestCase(WebSearchFreshness.LastYear, "py")]
    public async Task SearchAsync_FreshnessMapping_MatchesBraveEnum(WebSearchFreshness input, string expected)
    {
        SetupHttp(_ => JsonResponse(HttpStatusCode.OK, CannedOk));

        await _provider.SearchAsync(new WebSearchQuery("q", 5, input), CancellationToken.None);

        Assert.That(_capturedRequests[0].RequestUri!.Query, Does.Contain($"freshness={expected}"));
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
