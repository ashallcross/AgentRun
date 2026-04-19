using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class WebSearchToolTests
{
    private const string SentinelApiKey = "test-key-0123456789-SENTINEL";

    private IWebSearchProviderFactory _factory = null!;
    private IWebSearchProvider _braveProvider = null!;
    private IWebSearchProvider _tavilyProvider = null!;
    private IWebSearchCache _cache = null!;
    private IOptions<AgentRunOptions> _options = null!;
    private CapturingLogger<WebSearchTool> _logger = null!;
    private WebSearchTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _braveProvider = Substitute.For<IWebSearchProvider>();
        _braveProvider.Name.Returns("Brave");
        _tavilyProvider = Substitute.For<IWebSearchProvider>();
        _tavilyProvider.Name.Returns("Tavily");
        _factory = Substitute.For<IWebSearchProviderFactory>();
        _factory.GetRegisteredProviderNames().Returns(new[] { "Brave", "Tavily" });
        _factory.GetAsync("Brave", Arg.Any<CancellationToken>()).Returns(_braveProvider);
        _factory.GetAsync("Tavily", Arg.Any<CancellationToken>()).Returns(_tavilyProvider);

        _cache = Substitute.For<IWebSearchCache>();
        string? unused;
        _cache.TryGet(Arg.Any<string>(), Arg.Any<WebSearchQuery>(), out unused!).Returns(false);

        _options = Options.Create(new AgentRunOptions
        {
            WebSearch = new AgentRunWebSearchOptions
            {
                DefaultProvider = "Brave",
                Providers =
                {
                    ["Brave"] = new AgentRunWebSearchProviderOptions { ApiKey = SentinelApiKey },
                    ["Tavily"] = new AgentRunWebSearchProviderOptions { ApiKey = "tavily-secret" }
                }
            }
        });

        _logger = new CapturingLogger<WebSearchTool>();
        _tool = new WebSearchTool(_factory, _cache, _options, _logger);

        var step = new StepDefinition { Id = "step-1", Name = "Research", Agent = "agents/research.md" };
        var workflow = new WorkflowDefinition { Name = "Test", Alias = "test", Steps = { step } };
        _context = new ToolExecutionContext(Path.GetTempPath(), "inst-001", "step-1", "test")
        {
            Step = step,
            Workflow = workflow
        };
    }

    private async Task<string> Exec(IDictionary<string, object?> args)
        => (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

    [Test]
    public void Name_IsWebSearch()
    {
        Assert.That(_tool.Name, Is.EqualTo("web_search"));
    }

    [Test]
    public void Description_MentionsResultShape_AndAdvisesFetchUrlFollowup()
    {
        var desc = _tool.Description.ToLowerInvariant();
        Assert.That(desc, Does.Contain("url").Or.Contain("urls"));
        Assert.That(desc, Does.Contain("title"));
        Assert.That(desc, Does.Contain("snippet").Or.Contain("description"));
        Assert.That(desc, Does.Contain("fetch_url"));
    }

    [Test]
    public void ParameterSchema_HasQueryCountFreshness_WithCorrectTypesAndDefaults()
    {
        Assert.That(_tool.ParameterSchema, Is.Not.Null);
        var schema = _tool.ParameterSchema!.Value;
        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.That(required, Does.Contain("query"));

        var props = schema.GetProperty("properties");
        Assert.That(props.GetProperty("query").GetProperty("type").GetString(), Is.EqualTo("string"));
        Assert.That(props.GetProperty("count").GetProperty("type").GetString(), Is.EqualTo("integer"));
        Assert.That(props.GetProperty("count").GetProperty("default").GetInt32(), Is.EqualTo(10));
        Assert.That(props.GetProperty("count").GetProperty("minimum").GetInt32(), Is.EqualTo(1));
        Assert.That(props.GetProperty("count").GetProperty("maximum").GetInt32(), Is.EqualTo(25));

        var freshness = props.GetProperty("freshness");
        var enumVals = freshness.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.That(enumVals, Is.EquivalentTo(new[] { "last_day", "last_week", "last_month", "last_year", "all" }));
        Assert.That(freshness.GetProperty("default").GetString(), Is.EqualTo("all"));
    }

    [Test]
    public async Task ExecuteAsync_HappyPath_ReturnsJsonWithResultsArray()
    {
        _braveProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new WebSearchResult("T1", "https://1", "s1", null, "Brave", null),
                new WebSearchResult("T2", "https://2", "s2", DateTimeOffset.Parse("2026-03-15"), "Brave", null)
            });

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "test" });

        using var doc = JsonDocument.Parse(result);
        var results = doc.RootElement.GetProperty("results");
        Assert.That(results.GetArrayLength(), Is.EqualTo(2));
        Assert.That(results[0].GetProperty("title").GetString(), Is.EqualTo("T1"));
        Assert.That(results[0].GetProperty("url").GetString(), Is.EqualTo("https://1"));
        Assert.That(results[0].GetProperty("snippet").GetString(), Is.EqualTo("s1"));
        Assert.That(results[0].GetProperty("sourceProvider").GetString(), Is.EqualTo("Brave"));
        Assert.That(results[1].GetProperty("publishedDate").GetString(), Does.Contain("2026-03-15"));
    }

    [Test]
    public async Task ExecuteAsync_CacheHit_DoesNotCallProvider()
    {
        const string cached = """{"results":[{"title":"cached","url":"https://c","snippet":"s","publishedDate":null,"sourceProvider":"Brave","relevanceScore":null}]}""";
        _cache.TryGet(Arg.Any<string>(), Arg.Any<WebSearchQuery>(), out Arg.Any<string?>()!)
            .Returns(x => { x[2] = cached; return true; });

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "test" });

        Assert.That(result, Is.EqualTo(cached));
        await _braveProvider.DidNotReceive().SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
        _cache.DidNotReceive().Set(
            Arg.Any<string>(), Arg.Any<WebSearchQuery>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Test]
    public async Task ExecuteAsync_CacheMiss_CallsProviderOnce_AndCachesResult()
    {
        _braveProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WebSearchResult("x", "https://x", "s", null, "Brave", null) });

        await Exec(new Dictionary<string, object?> { ["query"] = "test" });

        await _braveProvider.Received(1).SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
        _cache.Received(1).Set("Brave", Arg.Any<WebSearchQuery>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Test]
    public async Task ExecuteAsync_MissingApiKey_ReturnsStructuredNotConfiguredResult_NoKeyLeakage()
    {
        _factory.GetAsync("Brave", Arg.Any<CancellationToken>()).Returns(_ => throw new WebSearchNotConfiguredException("Brave"));

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("not_configured"));
        Assert.That(doc.RootElement.GetProperty("provider").GetString(), Is.EqualTo("Brave"));
        AssertNoKeyLeak(result);
        AssertNoKeyLeakInLogs();
    }

    [Test]
    public async Task ExecuteAsync_RateLimited_ReturnsStructuredResult_WithRetryAfter()
    {
        _braveProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<WebSearchResult[]>>(_ => throw new WebSearchRateLimitedException("Brave", 60));

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("rate_limited"));
        Assert.That(doc.RootElement.GetProperty("provider").GetString(), Is.EqualTo("Brave"));
        Assert.That(doc.RootElement.GetProperty("retryAfterSeconds").GetInt32(), Is.EqualTo(60));
    }

    [Test]
    public async Task ExecuteAsync_TransportFailure_ReturnsStructuredTransportError()
    {
        _braveProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<WebSearchResult[]>>(_ => throw new WebSearchException("Brave web search transport failure: Connection refused"));

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("transport"));
        Assert.That(doc.RootElement.GetProperty("provider").GetString(), Is.EqualTo("Brave"));
    }

    [Test]
    public async Task ExecuteAsync_EmptyQuery_ReturnsInvalidArgumentError()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "   " });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(doc.RootElement.GetProperty("message").GetString()!.ToLowerInvariant(), Does.Contain("query"));
        await _braveProvider.DidNotReceive().SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_MissingQueryArgument_ReturnsInvalidArgumentError()
    {
        var result = await Exec(new Dictionary<string, object?>());

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
    }

    [Test]
    public async Task ExecuteAsync_InvalidCount_ReturnsInvalidArgumentError()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q", ["count"] = 0 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
    }

    [Test]
    public async Task ExecuteAsync_CountAbove25_ReturnsInvalidArgumentError()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q", ["count"] = 26 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
    }

    [Test]
    [TestCase("last_decade")]
    [TestCase("ALL")]
    [TestCase("Last_Day")]
    [TestCase("LAST_WEEK")]
    [TestCase(" last_day ")]
    [TestCase("")]
    public async Task ExecuteAsync_InvalidFreshness_ReturnsInvalidArgumentError(string freshness)
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q", ["freshness"] = freshness });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        await _braveProvider.DidNotReceive().SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_ResolvesExplicitDefaultProvider()
    {
        _options.Value.WebSearch!.DefaultProvider = "Tavily";
        _tavilyProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WebSearchResult>());

        await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        await _tavilyProvider.Received(1).SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
        await _braveProvider.DidNotReceive().SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_DefaultProviderUnset_PicksFirstRegisteredWithKey()
    {
        _options.Value.WebSearch!.DefaultProvider = null;
        _braveProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WebSearchResult>());

        await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        await _braveProvider.Received(1).SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_DefaultProviderUnset_FirstRegisteredHasNoKey_PicksSecond()
    {
        _options.Value.WebSearch!.DefaultProvider = null;
        _options.Value.WebSearch.Providers["Brave"].ApiKey = null;
        _tavilyProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WebSearchResult>());

        await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        await _tavilyProvider.Received(1).SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_ExplicitDefaultProvider_KeyMissing_ReturnsNotConfigured()
    {
        _options.Value.WebSearch!.DefaultProvider = "Brave";
        _options.Value.WebSearch.Providers["Brave"].ApiKey = "";

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "q" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("not_configured"));
        Assert.That(doc.RootElement.GetProperty("provider").GetString(), Is.EqualTo("Brave"));
    }

    [Test]
    public async Task ExecuteAsync_NotConfigured_LogsWarningOncePer_ProviderStep()
    {
        // AC6 — deduplicate per (providerName, stepId) so an LLM that retries
        // the same not-configured tool call within a single step does not
        // spam ops logs. Use a fresh step id so this test is not affected by
        // prior tests that may have logged for the default step id.
        var freshStepId = Guid.NewGuid().ToString("N");
        var step = new StepDefinition { Id = freshStepId, Name = "Research", Agent = "agents/research.md" };
        var workflow = new WorkflowDefinition { Name = "Test", Alias = "test", Steps = { step } };
        var ctx = new ToolExecutionContext(Path.GetTempPath(), "inst-002", freshStepId, "test")
        {
            Step = step,
            Workflow = workflow
        };
        _factory.GetAsync("Brave", Arg.Any<CancellationToken>())
            .Returns(_ => throw new WebSearchNotConfiguredException("Brave"));

        for (var i = 0; i < 3; i++)
            await _tool.ExecuteAsync(new Dictionary<string, object?> { ["query"] = "q" }, ctx, CancellationToken.None);

        var warnings = _logger.Entries.Count(e => e.Message.Contains("web_search not configured"));
        Assert.That(warnings, Is.EqualTo(1),
            "Warning must be deduplicated per (providerName, stepId)");
    }

    [Test]
    public void Cancellation_Propagates_OperationCanceledException()
    {
        _braveProvider.SearchAsync(Arg.Any<WebSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<WebSearchResult[]>>(_ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _tool.ExecuteAsync(
                new Dictionary<string, object?> { ["query"] = "q" },
                _context,
                cts.Token));
    }

    private void AssertNoKeyLeak(string payload)
    {
        Assert.That(payload, Does.Not.Contain(SentinelApiKey),
            "API key must never be in tool result JSON");
    }

    private void AssertNoKeyLeakInLogs()
    {
        foreach (var entry in _logger.Entries)
        {
            Assert.That(entry.Message, Does.Not.Contain(SentinelApiKey),
                "API key must never appear in any log line");
            var exceptionText = entry.Exception?.ToString() ?? string.Empty;
            Assert.That(exceptionText, Does.Not.Contain(SentinelApiKey),
                "API key must never appear in any logged exception chain");
        }
    }

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
