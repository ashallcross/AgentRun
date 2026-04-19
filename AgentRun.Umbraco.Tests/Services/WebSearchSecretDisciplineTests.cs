using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Services;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Services;

/// <summary>
/// Story 11.8 D7 — secret discipline is test-verified, not reviewer-verified.
/// Sentinel API key is injected; every adapter + tool invocation is
/// exercised across the failure matrix; sentinel must not appear in any
/// captured log line, tool result JSON, or exception ToString() output.
/// </summary>
[TestFixture]
public class WebSearchSecretDisciplineTests
{
    private const string SentinelApiKey = "test-key-0123456789-SENTINEL";

    private IHttpClientFactory _httpClientFactory = null!;
    private IWebSearchCache _cache = null!;
    private IOptions<AgentRunOptions> _options = null!;
    private CapturingLogger<WebSearchTool> _toolLogger = null!;
    private CapturingLogger<BraveWebSearchProvider> _braveLogger = null!;
    private CapturingLogger<TavilyWebSearchProvider> _tavilyLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _cache = new WebSearchCache(new MemoryCache(new MemoryCacheOptions()));
        _options = Options.Create(new AgentRunOptions
        {
            WebSearch = new AgentRunWebSearchOptions
            {
                DefaultProvider = "Brave",
                Providers =
                {
                    ["Brave"]  = new AgentRunWebSearchProviderOptions { ApiKey = SentinelApiKey },
                    ["Tavily"] = new AgentRunWebSearchProviderOptions { ApiKey = SentinelApiKey }
                }
            }
        });
        _toolLogger   = new CapturingLogger<WebSearchTool>();
        _braveLogger  = new CapturingLogger<BraveWebSearchProvider>();
        _tavilyLogger = new CapturingLogger<TavilyWebSearchProvider>();
    }

    private void SetupHttp(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var mock = new MockHttpHandler((req, _) => Task.FromResult(handler(req)));
        _httpClientFactory.CreateClient("WebSearch").Returns(new HttpClient(mock));
    }

    private (WebSearchTool tool, ToolExecutionContext ctx) BuildTool(string providerName)
    {
        var brave = new BraveWebSearchProvider(_httpClientFactory, _options, _braveLogger);
        var tavily = new TavilyWebSearchProvider(_httpClientFactory, _options, _tavilyLogger);
        var factory = new WebSearchProviderFactory(new IWebSearchProvider[] { brave, tavily });
        var tool = new WebSearchTool(factory, _cache, _options, _toolLogger);
        _options.Value.WebSearch!.DefaultProvider = providerName;

        var step = new StepDefinition { Id = "s-1", Name = "Research", Agent = "a.md" };
        var workflow = new WorkflowDefinition { Name = "T", Alias = "t", Steps = { step } };
        var ctx = new ToolExecutionContext(Path.GetTempPath(), "inst-1", "s-1", "t")
        {
            Step = step,
            Workflow = workflow
        };
        return (tool, ctx);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private void AssertNoSentinelAnywhere(string context, string toolResult)
    {
        Assert.That(toolResult, Does.Not.Contain(SentinelApiKey),
            $"{context}: sentinel leaked into tool result JSON");

        foreach (var entry in _toolLogger.Entries.Concat(_braveLogger.Entries).Concat(_tavilyLogger.Entries))
        {
            Assert.That(entry.Message, Does.Not.Contain(SentinelApiKey),
                $"{context}: sentinel leaked into log message");
            Assert.That(entry.Exception?.ToString() ?? "", Does.Not.Contain(SentinelApiKey),
                $"{context}: sentinel leaked into logged exception");
        }
    }

    [Test]
    public async Task HappyPath_Brave_NoSentinelInLogsOrResult()
    {
        const string okBody = """{"web":{"results":[{"title":"t","url":"https://x","description":"d","page_age":"2026-03-01T00:00:00"}]}}""";
        SetupHttp(_ => Json(HttpStatusCode.OK, okBody));
        var (tool, ctx) = BuildTool("Brave");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        AssertNoSentinelAnywhere("happy-path Brave", result);
    }

    [Test]
    public async Task RateLimited_Brave_NoSentinel()
    {
        SetupHttp(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return r;
        });
        var (tool, ctx) = BuildTool("Brave");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        Assert.That(result, Does.Contain("rate_limited"));
        AssertNoSentinelAnywhere("rate-limited Brave", result);
    }

    [Test]
    public async Task NotConfigured_Brave_NoSentinel()
    {
        // Wipe the key BEFORE constructing tool so the not-configured path fires.
        _options.Value.WebSearch!.Providers["Brave"].ApiKey = "";
        var (tool, ctx) = BuildTool("Brave");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        Assert.That(result, Does.Contain("not_configured"));
        AssertNoSentinelAnywhere("not-configured Brave", result);
    }

    [Test]
    public async Task MalformedResponse_Brave_NoSentinel_AndNoResponseBody()
    {
        SetupHttp(_ => Json(HttpStatusCode.OK, "not-json {{{"));
        var (tool, ctx) = BuildTool("Brave");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        Assert.That(result, Does.Contain("transport"));
        AssertNoSentinelAnywhere("malformed Brave", result);
        Assert.That(result, Does.Not.Contain("not-json"));
    }

    [Test]
    public async Task TransportFailure_Brave_NoSentinel()
    {
        var mock = new MockHttpHandler((_, _) => throw new HttpRequestException("Connection refused"));
        _httpClientFactory.CreateClient("WebSearch").Returns(new HttpClient(mock));
        var (tool, ctx) = BuildTool("Brave");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        Assert.That(result, Does.Contain("transport"));
        AssertNoSentinelAnywhere("transport Brave", result);
    }

    [Test]
    public async Task AuthFailure_Brave_NoSentinel_MessageHasAppsettingsHint()
    {
        SetupHttp(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (tool, ctx) = BuildTool("Brave");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        // D3 — 401/403 now routes through WebSearchNotConfiguredException and
        // the not_configured envelope, so the LLM does not retry futilely.
        Assert.That(result, Does.Contain("not_configured"));
        Assert.That(result, Does.Contain("AgentRun:WebSearch:Providers:Brave:ApiKey"));
        AssertNoSentinelAnywhere("auth-failure Brave", result);
    }

    [Test]
    public async Task HappyPath_Tavily_NoSentinel()
    {
        const string okBody = """{"results":[{"title":"t","url":"https://x","content":"c","score":0.9}]}""";
        SetupHttp(_ => Json(HttpStatusCode.OK, okBody));
        var (tool, ctx) = BuildTool("Tavily");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        AssertNoSentinelAnywhere("happy-path Tavily", result);
    }

    [Test]
    public async Task AuthFailure_Tavily_NoSentinel_MessageHasAppsettingsHint()
    {
        SetupHttp(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (tool, ctx) = BuildTool("Tavily");

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        // D3 — 401/403 now routes through WebSearchNotConfiguredException and
        // the not_configured envelope, so the LLM does not retry futilely.
        Assert.That(result, Does.Contain("not_configured"));
        Assert.That(result, Does.Contain("AgentRun:WebSearch:Providers:Tavily:ApiKey"));
        AssertNoSentinelAnywhere("auth-failure Tavily", result);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
