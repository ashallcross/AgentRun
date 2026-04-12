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
using Umbraco.Cms.Core.Web;
using UmbracoContextReference = global::Umbraco.Cms.Core.UmbracoContextReference;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class GetContentToolTests
{
    private IUmbracoContextFactory _contextFactory = null!;
    private IPublishedContentCache _contentCache = null!;
    private IPublishedUrlProvider _urlProvider = null!;
    private FakeToolLimitResolver _resolver = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private GetContentTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _contextFactory = Substitute.For<IUmbracoContextFactory>();
        _resolver = new FakeToolLimitResolver();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();

        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(serviceProvider);
        _scopeFactory.CreateScope().Returns(scope);

        var umbracoContext = Substitute.For<IUmbracoContext>();
        _contentCache = Substitute.For<IPublishedContentCache>();
        umbracoContext.Content.Returns(_contentCache);

        _urlProvider = Substitute.For<IPublishedUrlProvider>();
        _urlProvider.GetUrl(Arg.Any<IPublishedContent>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns("/test-url/");

        var contextAccessor = Substitute.For<global::Umbraco.Cms.Core.Web.IUmbracoContextAccessor>();
        var contextRef = new UmbracoContextReference(umbracoContext, false, contextAccessor);
        _contextFactory.EnsureUmbracoContext().Returns(contextRef);

        _tool = new GetContentTool(
            _contextFactory,
            _urlProvider,
            _resolver,
            _scopeFactory,
            NullLogger<GetContentTool>.Instance);

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
        public int GetContentMaxBytes { get; set; } = EngineDefaults.GetContentMaxResponseBytes;
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlMaxResponseBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ReadFileMaxResponseBytes;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 300;
        public int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ListContentMaxResponseBytes;
        public int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => GetContentMaxBytes;
        public int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ListContentTypesMaxResponseBytes;
    }

    private static IPublishedContent MakeNode(int id, string name, string contentTypeAlias, params IPublishedProperty[] properties)
    {
        var node = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        node.Id.Returns(id);
        node.Name.Returns(name);
        node.ContentType.Returns(contentType);
        node.Key.Returns(Guid.NewGuid());
        node.Level.Returns(1);
        node.CreateDate.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        node.UpdateDate.Returns(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        node.CreatorId.Returns(1);
        node.TemplateId.Returns((int?)null);
        node.Properties.Returns(properties);
        return node;
    }

    private static IPublishedProperty MakeProperty(string alias, string editorAlias, object? value, bool hasValue = true)
    {
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns(editorAlias);
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(hasValue);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        prop.GetSourceValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        return prop;
    }

    // ---------- Tests ----------

    [Test]
    public async Task ValidNode_ReturnsFullProperties()
    {
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello World");
        var node = MakeNode(1, "Home", "homePage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(json.GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(json.GetProperty("name").GetString(), Is.EqualTo("Home"));
        Assert.That(json.GetProperty("contentType").GetString(), Is.EqualTo("homePage"));
        Assert.That(json.GetProperty("properties").GetProperty("title").GetString(), Is.EqualTo("Hello World"));
    }

    [Test]
    public void NodeNotFound_ThrowsToolExecutionException()
    {
        _contentCache.GetById(9999).Returns((IPublishedContent?)null);

        var args = new Dictionary<string, object?> { ["id"] = 9999 };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("9999"));
        Assert.That(ex.Message, Does.Contain("not found or is not published"));
    }

    [Test]
    public async Task RichTextProperty_HtmlStrippedToPlainText()
    {
        var richTextProp = MakeProperty("body", "Umbraco.RichText", "<p>Hello <strong>World</strong></p>");
        var node = MakeNode(1, "Page", "textPage", richTextProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        var bodyText = json.GetProperty("properties").GetProperty("body").GetString();
        Assert.That(bodyText, Does.Not.Contain("<p>"));
        Assert.That(bodyText, Does.Not.Contain("<strong>"));
        Assert.That(bodyText, Does.Contain("Hello"));
        Assert.That(bodyText, Does.Contain("World"));
    }

    [Test]
    public async Task TextStringProperty_ReturnedAsIs()
    {
        var textProp = MakeProperty("subtitle", "Umbraco.TextBox", "Simple text value");
        var node = MakeNode(1, "Page", "textPage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(json.GetProperty("properties").GetProperty("subtitle").GetString(), Is.EqualTo("Simple text value"));
    }

    [Test]
    public async Task ContentPickerProperty_ReturnsNameAndUrl()
    {
        var pickedNode = Substitute.For<IPublishedContent>();
        pickedNode.Name.Returns("Target Page");

        var pickerProp = MakeProperty("link", "Umbraco.ContentPicker", pickedNode);
        var node = MakeNode(1, "Page", "textPage", pickerProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        var linkValue = json.GetProperty("properties").GetProperty("link").GetString();
        Assert.That(linkValue, Does.Contain("Target Page"));
    }

    [Test]
    public async Task UnknownEditor_FallbackToJsonSerialized()
    {
        var unknownProp = MakeProperty("custom", "Custom.Editor", "raw-value");
        var node = MakeNode(1, "Page", "textPage", unknownProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        // Fallback uses GetSourceValue + JsonSerializer.Serialize
        Assert.That(json.GetProperty("properties").TryGetProperty("custom", out _), Is.True);
    }

    [Test]
    public async Task PropertyConverterThrows_FallbackWithWarning()
    {
        var badProp = MakeProperty("broken", "Umbraco.TextBox", null);
        badProp.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Throws(new InvalidOperationException("converter error"));
        badProp.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);
        badProp.GetSourceValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("raw-fallback-value");

        var node = MakeNode(1, "Page", "textPage", badProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        // Should have the property with fallback value, not crash
        Assert.That(json.GetProperty("properties").TryGetProperty("broken", out _), Is.True);
    }

    [Test]
    public async Task SizeGuardTruncation_MarkerAppended()
    {
        _resolver.GetContentMaxBytes = 50; // very small

        var textProp = MakeProperty("body", "Umbraco.TextBox", new string('x', 500));
        var node = MakeNode(1, "Page", "textPage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.Contain("Response truncated"));
    }

    [Test]
    public void MissingIdParameter_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?>();
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Missing required argument"));
    }

    [Test]
    public void NullStepOrWorkflow_ThrowsToolContextMissingException()
    {
        var ctxWithoutStep = new ToolExecutionContext("/tmp/test", "inst-001", "step-1", "test-workflow");
        var args = new Dictionary<string, object?> { ["id"] = 1 };

        var ex = Assert.ThrowsAsync<ToolContextMissingException>(
            () => _tool.ExecuteAsync(args, ctxWithoutStep, CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<AgentRunException>());
    }

    [Test]
    public void UnrecognisedParameter_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["id"] = 1, ["unknown"] = "val" };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Unrecognised parameter"));
    }

    [Test]
    public void ZeroId_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["id"] = 0 };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("positive integer"));
    }
}
