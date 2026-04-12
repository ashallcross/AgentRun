using System.Text.Json;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ListContentTypesToolTests
{
    private IContentTypeService _contentTypeService = null!;
    private FakeToolLimitResolver _resolver = null!;
    private ListContentTypesTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _contentTypeService = Substitute.For<IContentTypeService>();
        _resolver = new FakeToolLimitResolver();

        _tool = new ListContentTypesTool(_contentTypeService, _resolver);

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
        public int ListContentTypesMaxBytes { get; set; } = EngineDefaults.ListContentTypesMaxResponseBytes;
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlMaxResponseBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ReadFileMaxResponseBytes;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 300;
        public int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ListContentMaxResponseBytes;
        public int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.GetContentMaxResponseBytes;
        public int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => ListContentTypesMaxBytes;
    }

    private static IContentType MakeContentType(string alias, string name, string? description = null, string? icon = null)
    {
        var ct = Substitute.For<IContentType>();
        ct.Alias.Returns(alias);
        ct.Name.Returns(name);
        ct.Description.Returns(description ?? string.Empty);
        ct.Icon.Returns(icon ?? "icon-document");
        ct.CompositionPropertyTypes.Returns(new List<IPropertyType>());
        ct.ContentTypeComposition.Returns(new List<IContentTypeComposition>());
        ct.AllowedContentTypes.Returns(new List<ContentTypeSort>());
        return ct;
    }

    // ---------- Tests ----------

    [Test]
    public async Task ReturnsAllDocumentTypes()
    {
        var ct1 = MakeContentType("homePage", "Home Page");
        var ct2 = MakeContentType("blogPost", "Blog Post");
        _contentTypeService.GetAll().Returns(new[] { ct1, ct2 });

        var args = new Dictionary<string, object?>();
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
        Assert.That(items[0].GetProperty("alias").GetString(), Is.EqualTo("homePage"));
        Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("Home Page"));
    }

    [Test]
    public async Task AliasFilter_ReturnsSingleMatch()
    {
        var ct = MakeContentType("blogPost", "Blog Post", "A blog post type");
        _contentTypeService.Get("blogPost").Returns(ct);

        var args = new Dictionary<string, object?> { ["alias"] = "blogPost" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
        Assert.That(items[0].GetProperty("description").GetString(), Is.EqualTo("A blog post type"));
    }

    [Test]
    public async Task AliasFilter_NoMatch_ReturnsEmptyArray()
    {
        _contentTypeService.Get("nonExistent").Returns((IContentType?)null);

        var args = new Dictionary<string, object?> { ["alias"] = "nonExistent" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("[]"));
    }

    [Test]
    public async Task IncludesPropertiesAndCompositions()
    {
        var ct = MakeContentType("blogPost", "Blog Post");

        var prop = Substitute.For<IPropertyType>();
        prop.Alias.Returns("title");
        prop.Name.Returns("Title");
        prop.PropertyEditorAlias.Returns("Umbraco.TextBox");
        prop.Mandatory.Returns(true);
        ct.CompositionPropertyTypes.Returns(new List<IPropertyType> { prop });

        var composition = Substitute.For<IContentTypeComposition>();
        composition.Alias.Returns("seoComposition");
        ct.ContentTypeComposition.Returns(new List<IContentTypeComposition> { composition });

        _contentTypeService.GetAll().Returns(new[] { ct });

        var args = new Dictionary<string, object?>();
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        var firstItem = items[0];

        var props = firstItem.GetProperty("properties");
        Assert.That(props.GetArrayLength(), Is.EqualTo(1));
        Assert.That(props[0].GetProperty("alias").GetString(), Is.EqualTo("title"));
        Assert.That(props[0].GetProperty("mandatory").GetBoolean(), Is.True);

        var compositions = firstItem.GetProperty("compositions");
        Assert.That(compositions.GetArrayLength(), Is.EqualTo(1));
        Assert.That(compositions[0].GetString(), Is.EqualTo("seoComposition"));
    }

    [Test]
    public async Task TruncationAtSizeLimit_MarkerAppended()
    {
        _resolver.ListContentTypesMaxBytes = 50; // very small

        var ct1 = MakeContentType("homePage", "Home Page With A Really Long Name For Testing");
        var ct2 = MakeContentType("blogPost", "Blog Post With Another Really Long Name");
        _contentTypeService.GetAll().Returns(new[] { ct1, ct2 });

        var args = new Dictionary<string, object?>();
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.Contain("Response truncated"));
        Assert.That(result, Does.Contain("document types"));
    }

    [Test]
    public void UnrecognisedParameter_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["unknown"] = "val" };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Unrecognised parameter"));
    }

    [Test]
    public void NullStepOrWorkflow_ThrowsToolContextMissingException()
    {
        var ctxWithoutStep = new ToolExecutionContext("/tmp/test", "inst-001", "step-1", "test-workflow");
        var args = new Dictionary<string, object?>();

        var ex = Assert.ThrowsAsync<ToolContextMissingException>(
            () => _tool.ExecuteAsync(args, ctxWithoutStep, CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<AgentRunException>());
    }
}
