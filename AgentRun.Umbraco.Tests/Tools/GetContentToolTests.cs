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
    private string _instanceFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _instanceFolder = Path.Combine(Path.GetTempPath(), "agentrun-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_instanceFolder);

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
        _context = new ToolExecutionContext(_instanceFolder, "inst-001", "step-1", "test-workflow")
        {
            Step = step,
            Workflow = workflow
        };
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_instanceFolder, recursive: true); } catch { /* best effort */ }
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
        public int ResolveCompactionTurnThreshold(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.CompactionTurnThreshold;
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

    // Helper: reads the cached file content given a handle
    private JsonElement ReadCachedContent(JsonElement handle)
    {
        var savedTo = handle.GetProperty("saved_to").GetString()!;
        var fullPath = Path.Combine(_instanceFolder, savedTo);
        var content = File.ReadAllText(fullPath);
        // Content may have a truncation marker appended after valid JSON — try parse the JSON prefix
        return JsonSerializer.Deserialize<JsonElement>(content.Split("[Response truncated")[0]);
    }

    // ---------- Existing Tests (updated for handle-based return) ----------

    [Test]
    public async Task ValidNode_ReturnsHandleAndWritesCacheFile()
    {
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello World");
        var node = MakeNode(1, "Home", "homePage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Handle assertions
        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(handle.GetProperty("name").GetString(), Is.EqualTo("Home"));
        Assert.That(handle.GetProperty("contentType").GetString(), Is.EqualTo("homePage"));
        Assert.That(handle.GetProperty("saved_to").GetString(), Is.EqualTo(".content-cache/1.json"));
        Assert.That(handle.GetProperty("truncated").GetBoolean(), Is.False);
        Assert.That(handle.GetProperty("propertyCount").GetInt32(), Is.EqualTo(1));
        Assert.That(handle.GetProperty("size_bytes").GetInt32(), Is.GreaterThan(0));
        Assert.That(handle.GetProperty("url").GetString(), Is.EqualTo("/test-url/"));

        // Cached file assertions
        var cached = ReadCachedContent(handle);
        Assert.That(cached.GetProperty("properties").GetProperty("title").GetString(), Is.EqualTo("Hello World"));
    }

    [Test]
    public void NodeNotFound_ThrowsToolExecutionException_NoCacheFile()
    {
        _contentCache.GetById(9999).Returns((IPublishedContent?)null);

        var args = new Dictionary<string, object?> { ["id"] = 9999 };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("9999"));
        Assert.That(ex.Message, Does.Contain("not found or is not published"));

        // No file should be written for errors (AC4)
        var cachePath = Path.Combine(_instanceFolder, ".content-cache", "9999.json");
        Assert.That(File.Exists(cachePath), Is.False);
    }

    [Test]
    public async Task RichTextProperty_HtmlStrippedInCachedFile()
    {
        var richTextProp = MakeProperty("body", "Umbraco.RichText", "<p>Hello <strong>World</strong></p>");
        var node = MakeNode(1, "Page", "textPage", richTextProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);
        var bodyText = cached.GetProperty("properties").GetProperty("body").GetString();
        Assert.That(bodyText, Does.Not.Contain("<p>"));
        Assert.That(bodyText, Does.Not.Contain("<strong>"));
        Assert.That(bodyText, Does.Contain("Hello"));
        Assert.That(bodyText, Does.Contain("World"));
    }

    [Test]
    public async Task TextStringProperty_PreservedInCachedFile()
    {
        var textProp = MakeProperty("subtitle", "Umbraco.TextBox", "Simple text value");
        var node = MakeNode(1, "Page", "textPage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);
        Assert.That(cached.GetProperty("properties").GetProperty("subtitle").GetString(), Is.EqualTo("Simple text value"));
    }

    [Test]
    public async Task ContentPickerProperty_InCachedFile()
    {
        var pickedNode = Substitute.For<IPublishedContent>();
        pickedNode.Name.Returns("Target Page");

        var pickerProp = MakeProperty("link", "Umbraco.ContentPicker", pickedNode);
        var node = MakeNode(1, "Page", "textPage", pickerProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);
        var linkValue = cached.GetProperty("properties").GetProperty("link").GetString();
        Assert.That(linkValue, Does.Contain("Target Page"));
    }

    [Test]
    public async Task UnknownEditor_FallbackInCachedFile()
    {
        var unknownProp = MakeProperty("custom", "Custom.Editor", "raw-value");
        var node = MakeNode(1, "Page", "textPage", unknownProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);
        Assert.That(cached.GetProperty("properties").TryGetProperty("custom", out _), Is.True);
    }

    [Test]
    public async Task PropertyConverterThrows_FallbackInCachedFile()
    {
        var badProp = MakeProperty("broken", "Umbraco.TextBox", null);
        badProp.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Throws(new InvalidOperationException("converter error"));
        badProp.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);
        badProp.GetSourceValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("raw-fallback-value");

        var node = MakeNode(1, "Page", "textPage", badProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);
        Assert.That(cached.GetProperty("properties").TryGetProperty("broken", out _), Is.True);
    }

    [Test]
    public async Task SizeGuardTruncation_HandleShowsTruncated()
    {
        _resolver.GetContentMaxBytes = 50; // very small

        var textProp = MakeProperty("body", "Umbraco.TextBox", new string('x', 500));
        var node = MakeNode(1, "Page", "textPage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.GetProperty("truncated").GetBoolean(), Is.True);

        // Cached file should contain truncation marker
        var savedTo = handle.GetProperty("saved_to").GetString()!;
        var cachedRaw = File.ReadAllText(Path.Combine(_instanceFolder, savedTo));
        Assert.That(cachedRaw, Does.Contain("Response truncated"));
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
        var ctxWithoutStep = new ToolExecutionContext(_instanceFolder, "inst-001", "step-1", "test-workflow");
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

    // ---------- Offloading Tests (Story 10.2) ----------

    [Test]
    public async Task HandleShape_ContainsAllRequiredFields()
    {
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello");
        var node = MakeNode(42, "About Us", "contentPage", textProp);
        _contentCache.GetById(42).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 42 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Multiple(() =>
        {
            Assert.That(handle.GetProperty("id").GetInt32(), Is.EqualTo(42));
            Assert.That(handle.GetProperty("name").GetString(), Is.EqualTo("About Us"));
            Assert.That(handle.GetProperty("contentType").GetString(), Is.EqualTo("contentPage"));
            Assert.That(handle.GetProperty("url").GetString(), Is.Not.Empty);
            Assert.That(handle.GetProperty("propertyCount").GetInt32(), Is.EqualTo(1));
            Assert.That(handle.GetProperty("size_bytes").GetInt32(), Is.GreaterThan(0));
            Assert.That(handle.GetProperty("saved_to").GetString(), Is.EqualTo(".content-cache/42.json"));
            Assert.That(handle.GetProperty("truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task HandleSizeBound_Under1KB()
    {
        // Create a node with many properties and a long name — handle must still be under 1 KB
        var props = Enumerable.Range(0, 20)
            .Select(i => MakeProperty($"prop_{i}", "Umbraco.TextBox", new string('a', 200)))
            .ToArray();
        var node = MakeNode(1234, new string('N', 80), "contentPageWithLongAlias", props);
        _contentCache.GetById(1234).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1234 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(System.Text.Encoding.UTF8.GetByteCount(result), Is.LessThan(1024));
    }

    [Test]
    public async Task SavedToPath_IsRelative()
    {
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello");
        var node = MakeNode(1, "Home", "homePage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var savedTo = handle.GetProperty("saved_to").GetString()!;
        Assert.That(Path.IsPathRooted(savedTo), Is.False);
        Assert.That(savedTo, Does.StartWith(".content-cache/"));
    }

    [Test]
    public async Task FileWritten_ContainsValidJson()
    {
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello World");
        var node = MakeNode(1, "Home", "homePage", textProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var filePath = Path.Combine(_instanceFolder, handle.GetProperty("saved_to").GetString()!);
        Assert.That(File.Exists(filePath), Is.True);

        var fileContent = File.ReadAllText(filePath);
        var parsed = JsonSerializer.Deserialize<JsonElement>(fileContent);
        Assert.That(parsed.GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(parsed.GetProperty("properties").GetProperty("title").GetString(), Is.EqualTo("Hello World"));
    }

    [Test]
    public async Task OverwriteOnRepeatCall_SecondCallOverwritesCache()
    {
        var textProp1 = MakeProperty("title", "Umbraco.TextBox", "Version 1");
        var node1 = MakeNode(1, "Home", "homePage", textProp1);
        _contentCache.GetById(1).Returns(node1);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Change the property value
        var textProp2 = MakeProperty("title", "Umbraco.TextBox", "Version 2");
        var node2 = MakeNode(1, "Home", "homePage", textProp2);
        _contentCache.GetById(1).Returns(node2);

        var result2 = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        var handle2 = JsonSerializer.Deserialize<JsonElement>(result2);

        var cached = ReadCachedContent(handle2);
        Assert.That(cached.GetProperty("properties").GetProperty("title").GetString(), Is.EqualTo("Version 2"));
    }

    [Test]
    public async Task ZeroProperties_HandleReturnsPropertyCountZero()
    {
        // Node with no properties (F2: folder node)
        var node = MakeNode(1, "Folder", "folder");
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.GetProperty("propertyCount").GetInt32(), Is.EqualTo(0));
        Assert.That(handle.GetProperty("truncated").GetBoolean(), Is.False);

        // Cached file still valid
        var filePath = Path.Combine(_instanceFolder, handle.GetProperty("saved_to").GetString()!);
        Assert.That(File.Exists(filePath), Is.True);
    }

    // ---------- PathSandbox verification (AC2, Task 2) ----------

    [Test]
    public void PathSandbox_ContentCacheDir_IsAccepted()
    {
        var dir = Path.Combine(_instanceFolder, ".content-cache");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "1234.json");
        File.WriteAllText(file, "{}");

        var result = AgentRun.Umbraco.Security.PathSandbox.ValidatePath(
            ".content-cache/1234.json", _instanceFolder);

        Assert.That(result, Is.EqualTo(file));
    }

    [Test]
    public void PathSandbox_ContentCachePathEscape_Rejected()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => AgentRun.Umbraco.Security.PathSandbox.ValidatePath(
                ".content-cache/../../etc/passwd", _instanceFolder));
        Assert.That(ex!.Message, Does.Contain("outside the instance folder"));
    }

    // ---------- Story 11.16: body_text + body_metadata handle extension ----------

    [Test]
    public async Task Handle_IncludesBodyTextAndBodyMetadataFields_WhenRichTextBody()
    {
        // AC1 — handle envelope extended with two additive fields (AFTER the 8 existing).
        var rteProp = MakeProperty("bodyContent", "Umbraco.RichText",
            "<h2>Title</h2><p>Body prose.</p>");
        var node = MakeNode(99, "Article", "article", rteProp);
        _contentCache.GetById(99).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 99 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.TryGetProperty("body_text", out var bodyText), Is.True);
        Assert.That(bodyText.GetString(), Does.Contain("Body prose."));

        Assert.That(handle.TryGetProperty("body_metadata", out var meta), Is.True);
        Assert.That(meta.GetProperty("headings").GetArrayLength(), Is.EqualTo(1));
        Assert.That(meta.GetProperty("headings")[0].GetProperty("level").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task Handle_BodyTextAndMetadataNull_WhenNoBodyProperty()
    {
        // AC5 — layout-only node: null (explicit) for both fields.
        var headlineProp = MakeProperty("headline", "Umbraco.TextBox", "Not a body");
        var node = MakeNode(1, "Nav Item", "navigationItem", headlineProp);
        _contentCache.GetById(1).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 1 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.GetProperty("body_text").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(handle.GetProperty("body_metadata").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task CacheFile_DoesNotContainBodyTextOrBodyMetadataKeys()
    {
        // AC10 / D10 — cache file keeps the pre-11.16 10-field shape; body
        // fields are handle-only.
        var rteProp = MakeProperty("bodyContent", "Umbraco.RichText", "<p>Some prose</p>");
        var node = MakeNode(7, "Page", "article", rteProp);
        _contentCache.GetById(7).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 7 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);

        Assert.That(cached.TryGetProperty("body_text", out _), Is.False,
            "Cache file must not contain body_text key (handle-only per AC10/D10)");
        Assert.That(cached.TryGetProperty("body_metadata", out _), Is.False,
            "Cache file must not contain body_metadata key (handle-only per AC10/D10)");

        // Sanity: the 10 pre-11.16 fields ARE still present.
        var expected = new[]
        {
            "id", "name", "contentType", "url", "level", "createDate",
            "updateDate", "creatorName", "templateAlias", "properties"
        };
        foreach (var key in expected)
        {
            Assert.That(cached.TryGetProperty(key, out _), Is.True,
                $"Cache file missing pre-11.16 field '{key}'");
        }
    }

    [Test]
    public async Task Handle_BodyTextCapRespectsWorkflowConfigValue()
    {
        // AC3 — content_audit_body_max_chars from workflow.Config is read at
        // tool runtime and caps body_text.
        var longProse = new string('x', 600);
        var rteProp = MakeProperty("bodyContent", "Umbraco.RichText", "<p>" + longProse + "</p>");
        var node = MakeNode(5, "Long Article", "article", rteProp);
        _contentCache.GetById(5).Returns(node);

        var step = new StepDefinition { Id = "scanner", Name = "Scanner", Agent = "agents/scanner.md" };
        var workflow = new WorkflowDefinition
        {
            Name = "Content Audit",
            Alias = "content-audit",
            Steps = { step },
            Config = new Dictionary<string, string> { ["content_audit_body_max_chars"] = "100" }
        };
        var contextWithCap = new ToolExecutionContext(_instanceFolder, "inst-1", "scanner", "content-audit")
        {
            Step = step,
            Workflow = workflow
        };

        var args = new Dictionary<string, object?> { ["id"] = 5 };
        var result = (string)await _tool.ExecuteAsync(args, contextWithCap, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var bodyText = handle.GetProperty("body_text").GetString()!;
        Assert.That(bodyText, Does.Contain(" [...truncated at 100 of 600 chars]"));
    }

    // ---------- Story 11.16 + GUID retrofit: key in handle + GUID lookup ----------

    [Test]
    public async Task Handle_IncludesKeyFieldAsPrimaryNodeReference()
    {
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello");
        var node = MakeNode(50, "Test", "testType", textProp);
        _contentCache.GetById(50).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 50 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.TryGetProperty("key", out var keyProp), Is.True,
            "Handle must surface `key` (GUID) as primary node reference post-retrofit");
        var keyStr = keyProp.GetString();
        Assert.That(Guid.TryParse(keyStr, out _), Is.True,
            $"`key` must be a parseable GUID; got: {keyStr}");
    }

    [Test]
    public async Task GuidLookup_ResolvesNodeViaKey()
    {
        // GUID-first retrofit — tool accepts GUID string and resolves via
        // contentCache.GetById(Guid) overload.
        var guid = Guid.NewGuid();
        var textProp = MakeProperty("title", "Umbraco.TextBox", "GUID-lookup node");
        var node = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("article");
        node.Id.Returns(77);
        node.Name.Returns("GUID-lookup node");
        node.ContentType.Returns(contentType);
        node.Key.Returns(guid);
        node.Level.Returns(1);
        node.CreateDate.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        node.UpdateDate.Returns(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        node.CreatorId.Returns(1);
        node.TemplateId.Returns((int?)null);
        node.Properties.Returns(new[] { textProp });
        _contentCache.GetById(guid).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = guid.ToString("D") };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(handle.GetProperty("id").GetInt32(), Is.EqualTo(77));
        Assert.That(handle.GetProperty("key").GetString(), Is.EqualTo(guid.ToString("D")));
        Assert.That(handle.GetProperty("name").GetString(), Is.EqualTo("GUID-lookup node"));
    }

    [Test]
    public async Task CacheFile_IncludesKeyAsPrimaryReference()
    {
        // Post-GUID-retrofit the cache-file JSON gains `key` (was shape-locked
        // pre-retrofit at 10 fields; now 11 with key as first). The retrofit is
        // an intentional shape extension per deferred-work MUST-SHIP.
        var textProp = MakeProperty("title", "Umbraco.TextBox", "Hello");
        var node = MakeNode(42, "Page", "article", textProp);
        _contentCache.GetById(42).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 42 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);

        Assert.That(cached.TryGetProperty("key", out var cachedKey), Is.True,
            "Cache file must include key as primary node reference post-retrofit");
        Assert.That(Guid.TryParse(cachedKey.GetString(), out _), Is.True);
    }

    [TestCase("not-a-number")]
    [TestCase("0")]
    [TestCase("-50")]
    [TestCase("2147483647")]   // int.MaxValue — post-patch upper-bound clamp rejects this
    [TestCase("10000001")]     // one beyond BodyContentExtractor.MaxBodyMaxChars (10M)
    public async Task Handle_InvalidBodyMaxCharsConfig_FallsBackToDefault(string rawValue)
    {
        // AC3 — invalid cap values (non-integer, zero, negative, above upper
        // bound) fall back to the 15000-char default. Upper-bound clamp added
        // post-2026-04-22 code review (Edge Case Hunter #10) guards against
        // int.MaxValue / OOM allocations; anything beyond 10 MB is a config
        // error and falls through to the default with a Warning.
        var rteProp = MakeProperty("bodyContent", "Umbraco.RichText", "<p>Short</p>");
        var node = MakeNode(3, "Article", "article", rteProp);
        _contentCache.GetById(3).Returns(node);

        var step = new StepDefinition { Id = "scanner-" + rawValue, Name = "S", Agent = "agents/s.md" };
        var workflow = new WorkflowDefinition
        {
            Name = "W",
            Alias = "w",
            Steps = { step },
            Config = new Dictionary<string, string> { ["content_audit_body_max_chars"] = rawValue }
        };
        var ctx = new ToolExecutionContext(_instanceFolder, "inst-1", step.Id, "w")
        {
            Step = step,
            Workflow = workflow
        };

        var args = new Dictionary<string, object?> { ["id"] = 3 };
        var result = (string)await _tool.ExecuteAsync(args, ctx, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        // Short content fits well under the 15000 default — no truncation marker.
        Assert.That(handle.GetProperty("body_text").GetString(), Is.EqualTo("Short"));
    }

    // ---------- Post-2026-04-22 code review: AC1 / AC10 regression (Decision 1.1 amendment) ----------

    [Test]
    public void Schema_AcceptsBothIntegerAndGuidStringForId()
    {
        // Post-11.16 code review Decision 1.1 amendment: AC1 was amended to
        // document that the schema is DUAL-FORM (integer OR GUID string) per
        // the E2E bundle #1 GUID retrofit. This test locks that shape so it
        // can't silently drift back to integer-only.
        Assert.That(_tool.ParameterSchema, Is.Not.Null);
        var schema = _tool.ParameterSchema!.Value;
        var idSchema = schema.GetProperty("properties").GetProperty("id");
        var types = idSchema.GetProperty("type").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.That(types, Is.EquivalentTo(new[] { "integer", "string" }),
            "AC1 (amended 2026-04-22): id schema must accept both integer and GUID string");

        var required = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.That(required, Is.EqualTo(new[] { "id" }),
            "AC1: id remains the only required field");
    }

    [Test]
    public async Task Handle_FieldOrderBeginsWithKeyThenId()
    {
        // Post-11.16 code review Decision 1.1 amendment: AC1 was amended to
        // document that the handle leads with `key` (durable GUID) then `id`
        // (display-only int). This test locks the ordering so it can't drift.
        var rteProp = MakeProperty("bodyContent", "Umbraco.RichText", "<p>Hello</p>");
        var node = MakeNode(42, "Article", "article", rteProp);
        _contentCache.GetById(42).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 42 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var fieldNames = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.That(fieldNames[0], Is.EqualTo("key"),
            "AC1 (amended 2026-04-22): handle must lead with `key`");
        Assert.That(fieldNames[1], Is.EqualTo("id"),
            "AC1 (amended 2026-04-22): handle's second field must be `id`");

        var expected = new[]
        {
            "key", "id", "name", "contentType", "url",
            "propertyCount", "size_bytes", "saved_to", "truncated",
            "body_text", "body_metadata"
        };
        Assert.That(fieldNames, Is.EqualTo(expected),
            "AC1 (amended 2026-04-22): full handle field order locked");
    }

    [Test]
    public async Task CacheFile_HasExactlyElevenFieldsWithKeyFirst()
    {
        // Post-11.16 code review Decision 1.1 amendment: AC10 / D10 were
        // amended to document the NEW 11-field cache shape (key prepended to
        // the pre-11.16 10-field shape). This regression test locks the exact
        // shape so silent drift is CI-detectable.
        var rteProp = MakeProperty("bodyContent", "Umbraco.RichText", "<p>Hello</p>");
        var node = MakeNode(77, "Article", "article", rteProp);
        _contentCache.GetById(77).Returns(node);

        var args = new Dictionary<string, object?> { ["id"] = 77 };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var handle = JsonSerializer.Deserialize<JsonElement>(result);
        var cached = ReadCachedContent(handle);
        var fieldNames = cached.EnumerateObject().Select(p => p.Name).ToArray();

        var expected = new[]
        {
            "key", "id", "name", "contentType", "url",
            "level", "createDate", "updateDate", "creatorName",
            "templateAlias", "properties"
        };
        Assert.That(fieldNames, Is.EqualTo(expected),
            "AC10 (amended 2026-04-22): cache file must be exactly 11 fields with key first");

        Assert.That(cached.TryGetProperty("body_text", out _), Is.False,
            "AC10: cache file must NOT contain body_text (handle-only field)");
        Assert.That(cached.TryGetProperty("body_metadata", out _), Is.False,
            "AC10: cache file must NOT contain body_metadata (handle-only field)");
    }
}
