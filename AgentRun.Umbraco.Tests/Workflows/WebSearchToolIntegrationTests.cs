using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Services;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

/// <summary>
/// Integration tests for Story 11.8: factory + real adapters + real cache
/// + mocked HttpClient. Verifies (1) cache short-circuits repeated
/// invocations across different tool instances sharing the same
/// IWebSearchCache singleton, and (2) the step-level tool-whitelist
/// contract — a step that omits <c>web_search</c> from <c>tools:</c>
/// must not see it in the LLM-visible tool list.
/// </summary>
[TestFixture]
public class WebSearchToolIntegrationTests
{
    private const string BraveKey = "brave-integration-key";

    private IHttpClientFactory _httpClientFactory = null!;
    private int _httpCallCount;
    private IMemoryCache _memoryCache = null!;
    private IWebSearchCache _cache = null!;
    private IOptions<AgentRunOptions> _options = null!;
    private WebSearchProviderFactory _factory = null!;
    private WebSearchTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _httpCallCount = 0;
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        var handler = new MockHttpHandler((req, _) =>
        {
            _httpCallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"web":{"results":[{"title":"t","url":"https://x","description":"d"}]}}""",
                    Encoding.UTF8, "application/json")
            });
        });
        _httpClientFactory.CreateClient("WebSearch").Returns(new HttpClient(handler));

        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cache = new WebSearchCache(_memoryCache);
        _options = Options.Create(new AgentRunOptions
        {
            WebSearch = new AgentRunWebSearchOptions
            {
                DefaultProvider = "Brave",
                CacheTtl = TimeSpan.FromHours(1),
                Providers = { ["Brave"] = new AgentRunWebSearchProviderOptions { ApiKey = BraveKey } }
            }
        });

        var brave = new BraveWebSearchProvider(
            _httpClientFactory, _options, NullLogger<BraveWebSearchProvider>.Instance);
        var tavily = new TavilyWebSearchProvider(
            _httpClientFactory, _options, NullLogger<TavilyWebSearchProvider>.Instance);
        _factory = new WebSearchProviderFactory(new IWebSearchProvider[] { brave, tavily });

        _tool = new WebSearchTool(_factory, _cache, _options, NullLogger<WebSearchTool>.Instance);
    }

    [TearDown]
    public void TearDown() => _memoryCache?.Dispose();

    [Test]
    public async Task FullStack_SameQueryTwice_ProviderCalledOnce_CacheShortCircuits()
    {
        var step = new StepDefinition { Id = "s-1", Name = "Research", Agent = "a.md" };
        var workflow = new WorkflowDefinition { Name = "T", Alias = "t", Steps = { step } };
        var ctx = new ToolExecutionContext(Path.GetTempPath(), "inst-1", "s-1", "t")
        {
            Step = step,
            Workflow = workflow
        };

        var r1 = (string)await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "same query" }, ctx, CancellationToken.None);
        var r2 = (string)await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "SAME QUERY" /* case variant */ }, ctx, CancellationToken.None);

        Assert.That(_httpCallCount, Is.EqualTo(1), "cache normalisation should collapse case variants");
        Assert.That(r1, Is.EqualTo(r2));
    }

    [Test]
    public void StepWithoutWebSearchTool_DoesNotReceiveIt_ViaDeclaredToolFilter()
    {
        // Mirror the filter StepExecutor applies — only tools whose Name is
        // in step.Tools are presented to the LLM. web_search must not leak
        // through when the step does not declare it.
        var tools = new IWorkflowTool[] { _tool /* web_search */ };
        var declaredNames = new[] { "read_file", "fetch_url" };

        var visible = tools
            .Where(t => declaredNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(visible, Is.Empty);
    }

    [Test]
    public void StepWithWebSearchDeclared_SeesTool()
    {
        var tools = new IWorkflowTool[] { _tool };
        var declaredNames = new[] { "web_search" };

        var visible = tools
            .Where(t => declaredNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(visible, Has.Length.EqualTo(1));
        Assert.That(visible[0].Name, Is.EqualTo("web_search"));
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
