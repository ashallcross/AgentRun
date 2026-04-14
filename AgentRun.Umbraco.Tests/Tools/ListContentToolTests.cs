using System.Text.Json;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using UmbracoContextReference = global::Umbraco.Cms.Core.UmbracoContextReference;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ListContentToolTests
{
    private IUmbracoContextFactory _contextFactory = null!;
    private IDocumentNavigationQueryService _navService = null!;
    private IPublishedContentStatusFilteringService _filteringService = null!;
    private IPublishedUrlProvider _urlProvider = null!;
    private FakeToolLimitResolver _resolver = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private ListContentTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _contextFactory = Substitute.For<IUmbracoContextFactory>();
        _navService = Substitute.For<IDocumentNavigationQueryService>();
        _filteringService = Substitute.For<IPublishedContentStatusFilteringService>();
        _urlProvider = Substitute.For<IPublishedUrlProvider>();
        _urlProvider.GetUrl(Arg.Any<IPublishedContent>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns(callInfo => $"/{((IPublishedContent)callInfo[0]).Name?.ToLowerInvariant().Replace(" ", "-")}/");
        _resolver = new FakeToolLimitResolver();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();

        // Set up scope factory to return a mock scope
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(serviceProvider);
        _scopeFactory.CreateScope().Returns(scope);

        var umbracoContext = Substitute.For<IUmbracoContext>();
        var contentCache = Substitute.For<IPublishedContentCache>();
        umbracoContext.Content.Returns(contentCache);

        var contextAccessor = Substitute.For<global::Umbraco.Cms.Core.Web.IUmbracoContextAccessor>();
        var contextRef = new UmbracoContextReference(umbracoContext, false, contextAccessor);
        _contextFactory.EnsureUmbracoContext().Returns(contextRef);

        _tool = new ListContentTool(
            _contextFactory,
            _navService,
            _filteringService,
            _urlProvider,
            _resolver,
            _scopeFactory,
            NullLogger<ListContentTool>.Instance);

        var step = new StepDefinition { Id = "step-1", Name = "Test", Agent = "agents/test.md" };
        var workflow = new WorkflowDefinition { Name = "Test Workflow", Alias = "test-workflow", Steps = { step } };
        _context = new ToolExecutionContext("/tmp/test", "inst-001", "step-1", "test-workflow")
        {
            Step = step,
            Workflow = workflow
        };
    }

    private sealed class FakeToolLimitResolver : IToolLimitResolver
    {
        public int ListContentMaxBytes { get; set; } = EngineDefaults.ListContentMaxResponseBytes;
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlMaxResponseBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ReadFileMaxResponseBytes;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 300;
        public int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => ListContentMaxBytes;
        public int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.GetContentMaxResponseBytes;
        public int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ListContentTypesMaxResponseBytes;
        public int ResolveCompactionTurnThreshold(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.CompactionTurnThreshold;
    }

    private static IPublishedContent MakeNode(int id, string name, string contentTypeAlias, Guid key, int level = 1, int creatorId = 1)
    {
        var node = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        node.Id.Returns(id);
        node.Name.Returns(name);
        node.ContentType.Returns(contentType);
        node.Key.Returns(key);
        node.Level.Returns(level);
        node.CreateDate.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        node.UpdateDate.Returns(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        node.CreatorId.Returns(creatorId);
        return node;
    }

    private void SetupRootNodes(params IPublishedContent[] nodes)
    {
        var rootKeys = nodes.Select(n => n.Key).ToList();
        _navService.TryGetRootKeys(out Arg.Any<IEnumerable<Guid>>())
            .Returns(x =>
            {
                x[0] = rootKeys.AsEnumerable();
                return true;
            });

        _filteringService.FilterAvailable(
            Arg.Is<IEnumerable<Guid>>(keys => keys.SequenceEqual(rootKeys)),
            Arg.Any<string?>())
            .Returns(nodes.ToList());

        // No children by default
        foreach (var node in nodes)
        {
            _navService.TryGetChildrenKeys(node.Key, out Arg.Any<IEnumerable<Guid>>())
                .Returns(x =>
                {
                    x[1] = Enumerable.Empty<Guid>();
                    return true;
                });
            _filteringService.FilterAvailable(
                Arg.Is<IEnumerable<Guid>>(keys => !keys.Any()),
                Arg.Any<string?>())
                .Returns(new List<IPublishedContent>());
        }
    }

    // ---------- Tests ----------

    [Test]
    public async Task EmptyContentTree_ReturnsEmptyArray()
    {
        _navService.TryGetRootKeys(out Arg.Any<IEnumerable<Guid>>())
            .Returns(x =>
            {
                x[0] = Enumerable.Empty<Guid>();
                return true;
            });
        _filteringService.FilterAvailable(Arg.Any<IEnumerable<Guid>>(), Arg.Any<string?>())
            .Returns(new List<IPublishedContent>());

        var args = new Dictionary<string, object?>();
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("[]"));
    }

    [Test]
    public async Task MultipleNodes_ReturnsAllWithCorrectFields()
    {
        var node1 = MakeNode(1, "Home", "homePage", Guid.NewGuid());
        var node2 = MakeNode(2, "About", "textPage", Guid.NewGuid());
        SetupRootNodes(node1, node2);

        var args = new Dictionary<string, object?>();
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
        Assert.That(items[0].GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("Home"));
        Assert.That(items[0].GetProperty("contentType").GetString(), Is.EqualTo("homePage"));
        Assert.That(items[1].GetProperty("id").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task ContentTypeFilter_ReturnsOnlyMatchingType()
    {
        var node1 = MakeNode(1, "Home", "homePage", Guid.NewGuid());
        var node2 = MakeNode(2, "Blog", "blogPost", Guid.NewGuid());
        SetupRootNodes(node1, node2);

        var args = new Dictionary<string, object?> { ["contentType"] = "blogPost" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
        Assert.That(items[0].GetProperty("contentType").GetString(), Is.EqualTo("blogPost"));
    }

    [Test]
    public async Task ContentTypeFilter_NoMatch_ReturnsEmptyArray()
    {
        var node1 = MakeNode(1, "Home", "homePage", Guid.NewGuid());
        SetupRootNodes(node1);

        var args = new Dictionary<string, object?> { ["contentType"] = "nonExistent" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("[]"));
    }

    [Test]
    public async Task ParentIdFilter_ReturnDirectChildren()
    {
        var parentKey = Guid.NewGuid();
        var parent = MakeNode(100, "Parent", "folder", parentKey);
        var child1 = MakeNode(101, "Child A", "textPage", Guid.NewGuid(), level: 2);
        var child2 = MakeNode(102, "Child B", "textPage", Guid.NewGuid(), level: 2);

        var contentCache = _contextFactory.EnsureUmbracoContext().UmbracoContext.Content!;
        contentCache.GetById(100).Returns(parent);

        var childKeys = new[] { child1.Key, child2.Key };
        _navService.TryGetChildrenKeys(parentKey, out Arg.Any<IEnumerable<Guid>>())
            .Returns(x =>
            {
                x[1] = childKeys.AsEnumerable();
                return true;
            });
        _filteringService.FilterAvailable(
            Arg.Is<IEnumerable<Guid>>(keys => keys.SequenceEqual(childKeys)),
            Arg.Any<string?>())
            .Returns(new List<IPublishedContent> { child1, child2 });

        // Set up childCount = 0 for each child
        _navService.TryGetChildrenKeys(child1.Key, out Arg.Any<IEnumerable<Guid>>())
            .Returns(x => { x[1] = Enumerable.Empty<Guid>(); return true; });
        _navService.TryGetChildrenKeys(child2.Key, out Arg.Any<IEnumerable<Guid>>())
            .Returns(x => { x[1] = Enumerable.Empty<Guid>(); return true; });
        _filteringService.FilterAvailable(
            Arg.Is<IEnumerable<Guid>>(keys => !keys.Any()),
            Arg.Any<string?>())
            .Returns(new List<IPublishedContent>());

        var args = new Dictionary<string, object?> { ["parentId"] = 100 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task ParentIdNoMatch_ReturnsEmptyArray()
    {
        var contentCache = _contextFactory.EnsureUmbracoContext().UmbracoContext.Content!;
        contentCache.GetById(9999).Returns((IPublishedContent?)null);

        var args = new Dictionary<string, object?> { ["parentId"] = 9999 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("[]"));
    }

    [Test]
    public async Task TruncationAtSizeLimit_MarkerAppendedWithCounts()
    {
        _resolver.ListContentMaxBytes = 100; // very small limit

        var node1 = MakeNode(1, "Home Page", "homePage", Guid.NewGuid());
        var node2 = MakeNode(2, "About Us", "textPage", Guid.NewGuid());
        var node3 = MakeNode(3, "Contact", "textPage", Guid.NewGuid());
        SetupRootNodes(node1, node2, node3);

        var args = new Dictionary<string, object?>();
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.Contain("Response truncated"));
        Assert.That(result, Does.Contain("of 3 content nodes"));
    }

    [Test]
    public void UnrecognisedParameter_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["foo"] = "bar" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Unrecognised parameter"));
        Assert.That(ex.Message, Does.Contain("foo"));
    }

    [Test]
    public void NullStepOrWorkflow_ThrowsToolContextMissingException()
    {
        var ctxWithoutStep = new ToolExecutionContext("/tmp/test", "inst-001", "step-1", "test-workflow");
        var args = new Dictionary<string, object?>();

        var ex = Assert.ThrowsAsync<ToolContextMissingException>(
            () => _tool.ExecuteAsync(args, ctxWithoutStep, CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<AgentRunException>());
        Assert.That(ex!.Message, Does.Contain("engine wiring bug"));
    }

    [Test]
    public void NegativeParentId_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["parentId"] = -1 };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("positive integer"));
    }

    [Test]
    public async Task BothFilters_Applied()
    {
        var parentKey = Guid.NewGuid();
        var parent = MakeNode(100, "Parent", "folder", parentKey);
        var child1 = MakeNode(101, "Blog A", "blogPost", Guid.NewGuid(), level: 2);
        var child2 = MakeNode(102, "Text A", "textPage", Guid.NewGuid(), level: 2);

        var contentCache = _contextFactory.EnsureUmbracoContext().UmbracoContext.Content!;
        contentCache.GetById(100).Returns(parent);

        var childKeys = new[] { child1.Key, child2.Key };
        _navService.TryGetChildrenKeys(parentKey, out Arg.Any<IEnumerable<Guid>>())
            .Returns(x => { x[1] = childKeys.AsEnumerable(); return true; });
        _filteringService.FilterAvailable(
            Arg.Is<IEnumerable<Guid>>(keys => keys.SequenceEqual(childKeys)),
            Arg.Any<string?>())
            .Returns(new List<IPublishedContent> { child1, child2 });

        _navService.TryGetChildrenKeys(child1.Key, out Arg.Any<IEnumerable<Guid>>())
            .Returns(x => { x[1] = Enumerable.Empty<Guid>(); return true; });
        _filteringService.FilterAvailable(
            Arg.Is<IEnumerable<Guid>>(keys => !keys.Any()),
            Arg.Any<string?>())
            .Returns(new List<IPublishedContent>());

        var args = new Dictionary<string, object?> { ["parentId"] = 100, ["contentType"] = "blogPost" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
        Assert.That(items[0].GetProperty("contentType").GetString(), Is.EqualTo("blogPost"));
    }
}
