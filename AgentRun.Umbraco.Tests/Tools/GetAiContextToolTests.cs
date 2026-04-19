using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.AI.Core.Contexts;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;
using UmbracoContextReference = global::Umbraco.Cms.Core.UmbracoContextReference;

namespace AgentRun.Umbraco.Tests.Tools;

/// <summary>
/// Unit tests for Story 11.9 <see cref="GetAiContextTool"/>. Covers tool
/// registration + schema shape, alias mode happy path + miss, content_node_id
/// tree-walk + multi-select + deleted-context skip, argument validation
/// matrix, and Umbraco.AI service-failure structured result.
/// </summary>
[TestFixture]
public class GetAiContextToolTests
{
    private IUmbracoContextFactory _contextFactory = null!;
    private IPublishedContentCache _contentCache = null!;
    private IAIContextService _contextService = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private CapturingLogger<GetAiContextTool> _logger = null!;
    private GetAiContextTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _contextService = Substitute.For<IAIContextService>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAIContextService)).Returns(_contextService);
        scope.ServiceProvider.Returns(serviceProvider);
        _scopeFactory.CreateScope().Returns(scope);

        _contextFactory = Substitute.For<IUmbracoContextFactory>();
        var umbracoContext = Substitute.For<IUmbracoContext>();
        _contentCache = Substitute.For<IPublishedContentCache>();
        umbracoContext.Content.Returns(_contentCache);
        var contextAccessor = Substitute.For<IUmbracoContextAccessor>();
        var contextRef = new UmbracoContextReference(umbracoContext, false, contextAccessor);
        _contextFactory.EnsureUmbracoContext().Returns(contextRef);

        _logger = new CapturingLogger<GetAiContextTool>();
        _tool = new GetAiContextTool(_contextFactory, _scopeFactory, _logger);

        var step = new StepDefinition { Id = "step-1", Name = "Writer", Agent = "agents/writer.md" };
        var workflow = new WorkflowDefinition { Name = "Test", Alias = "test", Steps = { step } };
        _context = new ToolExecutionContext(Path.GetTempPath(), "inst-001", "step-1", "test")
        {
            Step = step,
            Workflow = workflow
        };
    }

    private async Task<string> Exec(IDictionary<string, object?> args)
        => (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

    // ---------- AC1: registration + schema ----------

    [Test]
    public void Name_IsGetAiContext()
    {
        Assert.That(_tool.Name, Is.EqualTo("get_ai_context"));
    }

    [Test]
    public void Description_MentionsBothModes_TreeInheritance_InjectionMode_UnderLimit()
    {
        var desc = _tool.Description;
        Assert.That(desc.Length, Is.LessThanOrEqualTo(400),
            "Description should be under 400 chars per AC1 — keeps tool-schema payload tight");
        var lower = desc.ToLowerInvariant();
        Assert.That(lower, Does.Contain("alias"));
        Assert.That(lower, Does.Contain("content_node_id"));
        Assert.That(lower, Does.Contain("ancestor").Or.Contain("tree"));
        Assert.That(lower, Does.Contain("injection").Or.Contain("on_demand").Or.Contain("always"));
    }

    [Test]
    public void ParameterSchema_HasOptionalAliasAndContentNodeId_AdditionalPropertiesFalse_NoRequiredEntry()
    {
        Assert.That(_tool.ParameterSchema, Is.Not.Null);
        var schema = _tool.ParameterSchema!.Value;
        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));
        Assert.That(schema.GetProperty("additionalProperties").GetBoolean(), Is.False);
        Assert.That(schema.TryGetProperty("required", out _), Is.False,
            "Schema must not declare either argument required — validation is inside ExecuteAsync");

        var props = schema.GetProperty("properties");
        Assert.That(props.GetProperty("alias").GetProperty("type").GetString(), Is.EqualTo("string"));

        // content_node_id accepts either an integer (int ID) or a string (GUID key)
        var nodeIdType = props.GetProperty("content_node_id").GetProperty("type");
        Assert.That(nodeIdType.ValueKind, Is.EqualTo(JsonValueKind.Array));
        var allowedTypes = nodeIdType.EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.That(allowedTypes, Is.EquivalentTo(new[] { "integer", "string" }));
    }

    // ---------- AC2: alias mode happy path ----------

    [Test]
    public async Task AliasMode_ReturnsContextJson_ResourcesOrderedBySortOrder_CamelCase_SnakeCaseInjectionMode()
    {
        // Realistic brand-voice Settings shape as shipped in Umbraco.AI 1.8:
        // four free-text fields (toneDescription / targetAudience /
        // styleGuidelines / avoidPatterns). Confirmed empirically 2026-04-18
        // in the Task 9 manual E2E — exact field names captured from the
        // serialised tool response.
        var settings1 = new Dictionary<string, object?>
        {
            ["toneDescription"] = "Warm, authoritative, never jargon-heavy.",
            ["targetAudience"] = "UK SMB decision-makers, age 30-55.",
            ["styleGuidelines"] = "Active voice. Short sentences. Pound signs for money.",
            ["avoidPatterns"] = "Exclamation marks. US spellings. Corporate buzzwords."
        };
        // Text resource shape: single `content` string field.
        var settings2 = new Dictionary<string, object?>
        {
            ["content"] = "© 2026 Corporate Ltd. All rights reserved."
        };
        var ctx = MakeAIContext(
            "corporate-brand-voice",
            "Corporate Brand Voice",
            version: 3,
            resources: new[]
            {
                MakeResource("Legal Disclaimer", "Required footer text", "text", 1, AIContextResourceInjectionMode.OnDemand, settings2),
                MakeResource("Tone of Voice", "Our house style guide", "brand-voice", 0, AIContextResourceInjectionMode.Always, settings1)
            });

        _contextService.GetContextByAliasAsync("corporate-brand-voice", Arg.Any<CancellationToken>())
            .Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["alias"] = "corporate-brand-voice" });

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("alias").GetString(), Is.EqualTo("corporate-brand-voice"));
        Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Corporate Brand Voice"));
        Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(3));
        Assert.That(root.TryGetProperty("resolvedFrom", out _), Is.False, "Alias mode must not include resolvedFrom");

        var resources = root.GetProperty("resources");
        Assert.That(resources.GetArrayLength(), Is.EqualTo(2));
        // Ordered by sortOrder ascending — Tone of Voice (0) before Legal Disclaimer (1)
        Assert.That(resources[0].GetProperty("name").GetString(), Is.EqualTo("Tone of Voice"));
        Assert.That(resources[0].GetProperty("resourceTypeId").GetString(), Is.EqualTo("brand-voice"));
        Assert.That(resources[0].GetProperty("sortOrder").GetInt32(), Is.EqualTo(0));
        Assert.That(resources[0].GetProperty("injectionMode").GetString(), Is.EqualTo("always"));
        var brandVoiceSettings = resources[0].GetProperty("settings");
        Assert.That(brandVoiceSettings.GetProperty("toneDescription").GetString(),
            Is.EqualTo("Warm, authoritative, never jargon-heavy."));
        Assert.That(brandVoiceSettings.GetProperty("targetAudience").GetString(),
            Does.Contain("UK SMB"));
        Assert.That(brandVoiceSettings.GetProperty("avoidPatterns").GetString(),
            Does.Contain("US spellings"));

        Assert.That(resources[1].GetProperty("name").GetString(), Is.EqualTo("Legal Disclaimer"));
        Assert.That(resources[1].GetProperty("injectionMode").GetString(), Is.EqualTo("on_demand"));
        Assert.That(resources[1].GetProperty("settings").GetProperty("content").GetString(),
            Does.Contain("© 2026"));
    }

    [Test]
    public async Task AliasMode_ContextWithZeroResources_ReturnsEmptyResourcesArray()
    {
        var ctx = MakeAIContext("empty", "Empty", 1, Array.Empty<AIContextResource>());
        _contextService.GetContextByAliasAsync("empty", Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["alias"] = "empty" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("resources").GetArrayLength(), Is.EqualTo(0));
    }

    // ---------- AC3: alias miss ----------

    [Test]
    public async Task AliasMode_MissingAlias_ReturnsNotFound_AndLogsWarning()
    {
        _contextService.GetContextByAliasAsync("does-not-exist", Arg.Any<CancellationToken>())
            .Returns((AIContext?)null);

        var result = await Exec(new Dictionary<string, object?> { ["alias"] = "does-not-exist" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("not_found"));
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("does-not-exist"));
        Assert.That(doc.RootElement.GetProperty("message").GetString(),
            Does.Contain("does-not-exist"));
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("does-not-exist")),
            Is.True);
    }

    // ---------- AC4: content_node_id tree-walk happy path ----------

    [Test]
    public async Task ContentNodeIdMode_PickerOnSelf_ReturnsResolvedContext_WithResolvedFromMetadata()
    {
        var contextId = Guid.NewGuid();
        var ctx = MakeAIContext("services-voice", "Services Voice", 1, Array.Empty<AIContextResource>());

        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", contextId);
        var node = MakeNode(2000, "Services", "servicesPage", parent: null, props: pickerProp);
        _contentCache.GetById(2000).Returns(node);
        _contextService.GetContextAsync(contextId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2000 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("services-voice"));
        var resolvedFrom = doc.RootElement.GetProperty("resolvedFrom");
        Assert.That(resolvedFrom.GetProperty("contentNodeId").GetInt32(), Is.EqualTo(2000));
        Assert.That(resolvedFrom.GetProperty("contentNodeName").GetString(), Is.EqualTo("Services"));
    }

    [Test]
    public async Task ContentNodeIdMode_AcceptsGuidKey_ResolvesViaGetByIdGuid()
    {
        var contextId = Guid.NewGuid();
        var nodeKey = Guid.Parse("e80ead52-b504-4c58-af3b-771083be8806");
        var ctx = MakeAIContext("services-voice", "Services Voice", 1, Array.Empty<AIContextResource>());

        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", contextId);
        var node = MakeNode(2000, "Services", "servicesPage", parent: null, props: pickerProp);
        _contentCache.GetById(nodeKey).Returns(node);
        _contextService.GetContextAsync(contextId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = nodeKey.ToString() });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("services-voice"));
        var resolvedFrom = doc.RootElement.GetProperty("resolvedFrom");
        Assert.That(resolvedFrom.GetProperty("contentNodeId").GetInt32(), Is.EqualTo(2000),
            "resolvedFrom exposes the resolved node's int ID regardless of input shape");
        // Confirm int-path was NOT taken (would have called GetById(int) which isn't stubbed)
        _contentCache.DidNotReceive().GetById(Arg.Any<int>());
        _contentCache.Received(1).GetById(nodeKey);
    }

    [Test]
    public async Task ContentNodeIdMode_MalformedIdString_ReturnsInvalidArgument()
    {
        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = "not-a-number-and-not-a-guid" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(doc.RootElement.GetProperty("message").GetString()!.ToLowerInvariant(),
            Does.Contain("integer").And.Contain("guid"));
    }

    [Test]
    public async Task ContentNodeIdMode_EmptyGuid_ReturnsInvalidArgument()
    {
        var result = await Exec(new Dictionary<string, object?>
        {
            ["content_node_id"] = "00000000-0000-0000-0000-000000000000"
        });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
    }

    [Test]
    public async Task ContentNodeIdMode_PickerOnAncestor_WalksUpTree_ResolvedFromIsAncestor()
    {
        var contextId = Guid.NewGuid();
        var ctx = MakeAIContext("services-voice", "Services Voice", 1, Array.Empty<AIContextResource>());

        var ancestorPickerProp = MakeProperty("aiContext", "Uai.ContextPicker", contextId);
        var ancestorNode = MakeNode(2000, "Services", "servicesPage", parent: null, props: ancestorPickerProp);

        // Self node has the picker property but the value is empty
        var selfEmptyPicker = MakeProperty("aiContext", "Uai.ContextPicker", value: null);
        var selfNode = MakeNode(3000, "Divorce Law", "servicePage", parent: ancestorNode, props: selfEmptyPicker);

        _contentCache.GetById(3000).Returns(selfNode);
        _contextService.GetContextAsync(contextId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 3000 });

        using var doc = JsonDocument.Parse(result);
        var resolvedFrom = doc.RootElement.GetProperty("resolvedFrom");
        Assert.That(resolvedFrom.GetProperty("contentNodeId").GetInt32(), Is.EqualTo(2000));
        Assert.That(resolvedFrom.GetProperty("contentNodeName").GetString(), Is.EqualTo("Services"));
    }

    // ---------- AC5: content_node_id no picker anywhere ----------

    [Test]
    public async Task ContentNodeIdMode_NoPickerAnywhere_ReturnsNoContextForNode()
    {
        // Self has a non-picker property only; parent is null
        var titleProp = MakeProperty("title", "Umbraco.TextBox", "hello");
        var node = MakeNode(4000, "Plain", "plainPage", parent: null, props: titleProp);
        _contentCache.GetById(4000).Returns(node);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 4000 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("no_context_for_node"));
        Assert.That(doc.RootElement.GetProperty("contentNodeId").GetString(), Is.EqualTo("4000"));
    }

    [Test]
    public async Task ContentNodeIdMode_ContentNodeNotFound_ReturnsContentNotFound()
    {
        _contentCache.GetById(999).Returns((IPublishedContent?)null);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 999 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("content_not_found"));
        Assert.That(doc.RootElement.GetProperty("contentNodeId").GetString(), Is.EqualTo("999"));
    }

    [Test]
    public async Task ContentNodeIdMode_MultiPicker_FirstResolvable_SkipsDeletedContext_LogsInfo()
    {
        var deletedId = Guid.NewGuid();
        var goodId = Guid.NewGuid();
        var goodContext = MakeAIContext("services-voice", "Services Voice", 1, Array.Empty<AIContextResource>());

        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", new[] { deletedId, goodId });
        var node = MakeNode(2000, "Services", "servicesPage", parent: null, props: pickerProp);
        _contentCache.GetById(2000).Returns(node);

        _contextService.GetContextAsync(deletedId, Arg.Any<CancellationToken>()).Returns((AIContext?)null);
        _contextService.GetContextAsync(goodId, Arg.Any<CancellationToken>()).Returns(goodContext);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2000 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("services-voice"));
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("multi-picker")),
            Is.True, "Should log Info for multi-picker");
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("missing Context")),
            Is.True, "Should log Warning for deleted Context");
    }

    [Test]
    public async Task ContentNodeIdMode_PickerValueIsAIContextDirect_ViaPropertyValueConverter_Works()
    {
        // If Umbraco.AI's PropertyValueConverter is active, GetValue() returns
        // the hydrated AIContext directly. Helper should extract its Id.
        var hydrated = MakeAIContext("direct", "Direct", 5, Array.Empty<AIContextResource>());
        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", hydrated);
        var node = MakeNode(2500, "Via PVC", "page", parent: null, props: pickerProp);
        _contentCache.GetById(2500).Returns(node);
        _contextService.GetContextAsync(hydrated.Id, Arg.Any<CancellationToken>()).Returns(hydrated);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2500 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("direct"));
    }

    [Test]
    public async Task ContentNodeIdMode_PickerValueIsJsonStringGuid_ParsedAndResolved()
    {
        var contextId = Guid.NewGuid();
        var ctx = MakeAIContext("json-str", "JsonStr", 1, Array.Empty<AIContextResource>());
        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", contextId.ToString());
        var node = MakeNode(2600, "JSON string", "page", parent: null, props: pickerProp);
        _contentCache.GetById(2600).Returns(node);
        _contextService.GetContextAsync(contextId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2600 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("json-str"));
    }

    [Test]
    public async Task ContentNodeIdMode_PickerValueIsMalformedJson_ContinuesWalkingAncestors_LogsWarning()
    {
        // Self has malformed picker value; ancestor has valid picker
        var selfMalformed = MakeProperty("aiContext", "Uai.ContextPicker", "[not-a-json-array");
        var goodId = Guid.NewGuid();
        var ctx = MakeAIContext("ancestor-voice", "Ancestor Voice", 1, Array.Empty<AIContextResource>());
        var ancestorPicker = MakeProperty("aiContext", "Uai.ContextPicker", goodId);
        var ancestor = MakeNode(1000, "Root", "homepage", parent: null, props: ancestorPicker);
        var self = MakeNode(2000, "Broken", "page", parent: ancestor, props: selfMalformed);

        _contentCache.GetById(2000).Returns(self);
        _contextService.GetContextAsync(goodId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2000 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("ancestor-voice"));
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("malformed")),
            Is.True);
    }

    [Test]
    public async Task ContentNodeIdMode_PickerValueIsIEnumerableOfString_ResolvesFirstValidGuid()
    {
        // Picker-value-converter variant: value surfaces as List<string> of
        // GUID strings (e.g. when the PVC is disabled but the NVARCHAR column
        // stores a list). Exercises the IEnumerable<>→string-item branch of
        // ExtractContextGuids.
        var ctxId = Guid.NewGuid();
        var ctx = MakeAIContext("listed-strings", "Listed", 1, Array.Empty<AIContextResource>());
        var stringList = new List<string> { ctxId.ToString() };
        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", stringList);
        var node = MakeNode(2700, "String list", "page", parent: null, props: pickerProp);
        _contentCache.GetById(2700).Returns(node);
        _contextService.GetContextAsync(ctxId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2700 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("listed-strings"));
    }

    [Test]
    public async Task ContentNodeIdMode_PickerValueIsSingleGuidArray_Resolves()
    {
        // Single-element Guid[] variant — isolated from the multi-picker
        // test which exercises the >1-entry path. Confirms the `case Guid g`
        // arm of the enumerable branch in ExtractContextGuids.
        var ctxId = Guid.NewGuid();
        var ctx = MakeAIContext("single-guid-array", "Single", 1, Array.Empty<AIContextResource>());
        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", new[] { ctxId });
        var node = MakeNode(2800, "Single-GUID array", "page", parent: null, props: pickerProp);
        _contentCache.GetById(2800).Returns(node);
        _contextService.GetContextAsync(ctxId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2800 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("single-guid-array"));
    }

    [Test]
    public async Task ContentNodeIdMode_PickerValueIsJsonObjectWithIdField_Resolves()
    {
        // Defensive object-shape — some legacy pickers serialise as
        // {"id":"<guid>"}; covered by the JSON-object branch of
        // ParseGuidsFromString.
        var ctxId = Guid.NewGuid();
        var ctx = MakeAIContext("json-object", "Obj", 1, Array.Empty<AIContextResource>());
        var jsonObj = $"{{\"id\":\"{ctxId}\"}}";
        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", jsonObj);
        var node = MakeNode(2900, "Object-shape", "page", parent: null, props: pickerProp);
        _contentCache.GetById(2900).Returns(node);
        _contextService.GetContextAsync(ctxId, Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = 2900 });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("json-object"));
    }

    [Test]
    public void ContentNodeIdMode_CancellationDuringTreeWalk_PropagatesOperationCanceledException()
    {
        // Cancellation must surface through ResolveFromTreeAsync mid-walk —
        // not get swallowed by the outer catch (which returns
        // context_service_failure for non-OCE exceptions).
        var pickerId = Guid.NewGuid();
        var pickerProp = MakeProperty("aiContext", "Uai.ContextPicker", pickerId);
        var node = MakeNode(3100, "Slow", "page", parent: null, props: pickerProp);
        _contentCache.GetById(3100).Returns(node);
        _contextService.GetContextAsync(pickerId, Arg.Any<CancellationToken>())
            .Returns<AIContext?>(_ => throw new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _tool.ExecuteAsync(
                new Dictionary<string, object?> { ["content_node_id"] = 3100 },
                _context,
                cts.Token));
    }

    // ---------- AC6: argument validation ----------

    [Test]
    public async Task NeitherAliasNorContentNodeId_ReturnsInvalidArgument()
    {
        var result = await Exec(new Dictionary<string, object?>());

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(doc.RootElement.GetProperty("message").GetString()!.ToLowerInvariant(),
            Does.Contain("alias").And.Contain("content_node_id"));
    }

    [Test]
    public async Task EmptyAlias_ReturnsInvalidArgument()
    {
        var result = await Exec(new Dictionary<string, object?> { ["alias"] = "   " });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(doc.RootElement.GetProperty("message").GetString()!.ToLowerInvariant(),
            Does.Contain("alias"));
        await _contextService.DidNotReceive().GetContextByAliasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-100)]
    public async Task NonPositiveContentNodeId_ReturnsInvalidArgument(int nodeId)
    {
        var result = await Exec(new Dictionary<string, object?> { ["content_node_id"] = nodeId });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
    }

    [Test]
    public async Task BothAliasAndContentNodeId_AliasWins_LogsDebug()
    {
        var ctx = MakeAIContext("x", "X", 1, Array.Empty<AIContextResource>());
        _contextService.GetContextByAliasAsync("x", Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?>
        {
            ["alias"] = "x",
            ["content_node_id"] = 999
        });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("x"));
        await _contextService.Received(1).GetContextByAliasAsync("x", Arg.Any<CancellationToken>());
        await _contextService.DidNotReceive().GetContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        // Content cache should never be touched in alias mode
        _contentCache.DidNotReceive().GetById(Arg.Any<int>());
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Debug && e.Message.Contains("both")), Is.True);
    }

    [Test]
    public async Task AliasValid_ContentNodeIdMalformed_AliasStillWins()
    {
        // AC6 precedence: alias wins when both supplied. A malformed
        // content_node_id must NOT shadow the alias path with
        // invalid_argument — validation is skipped when alias is supplied.
        var ctx = MakeAIContext("x", "X", 1, Array.Empty<AIContextResource>());
        _contextService.GetContextByAliasAsync("x", Arg.Any<CancellationToken>()).Returns(ctx);

        var result = await Exec(new Dictionary<string, object?>
        {
            ["alias"] = "x",
            ["content_node_id"] = "not-a-valid-id-or-guid"
        });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("x"),
            "Alias must win even when content_node_id is malformed");
        await _contextService.Received(1).GetContextByAliasAsync("x", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnknownParameter_ReturnsInvalidArgument()
    {
        var result = await Exec(new Dictionary<string, object?>
        {
            ["alias"] = "x",
            ["unknown_key"] = "y"
        });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(doc.RootElement.GetProperty("message").GetString(), Does.Contain("unknown_key"));
    }

    // ---------- AC8: service failure ----------

    [Test]
    public async Task AliasMode_ContextServiceThrows_ReturnsStructuredFailure_LogsWarningWithException()
    {
        _contextService.GetContextByAliasAsync("boom", Arg.Any<CancellationToken>())
            .Returns<AIContext?>(_ => throw new InvalidOperationException("No AmbientContext was found"));

        var result = await Exec(new Dictionary<string, object?> { ["alias"] = "boom" });

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("context_service_failure"));
        Assert.That(doc.RootElement.GetProperty("message").GetString()!.ToLowerInvariant(),
            Does.Contain("unavailable").Or.Contain("temporarily"));
        Assert.That(_logger.Entries.Any(e =>
            e.Level == LogLevel.Warning && e.Exception is InvalidOperationException), Is.True);
    }

    [Test]
    public void NullStep_ThrowsToolContextMissingException()
    {
        // Failure & Edge Cases (spec line 224): engine-wiring bug must throw
        // ToolContextMissingException identical to GetContentTool.cs:73-78.
        var badContext = new ToolExecutionContext(Path.GetTempPath(), "inst-1", "step-1", "test");
        // Leaves Step and Workflow unset.

        Assert.ThrowsAsync<ToolContextMissingException>(async () =>
            await _tool.ExecuteAsync(
                new Dictionary<string, object?> { ["alias"] = "x" },
                badContext,
                CancellationToken.None));
    }

    [Test]
    public void Cancellation_PropagatesOperationCanceledException()
    {
        _contextService.GetContextByAliasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<AIContext?>(_ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _tool.ExecuteAsync(
                new Dictionary<string, object?> { ["alias"] = "x" },
                _context,
                cts.Token));
    }

    // ---------- helpers ----------

    private static AIContext MakeAIContext(string alias, string name, int version, IEnumerable<AIContextResource> resources)
    {
        var ctx = new AIContext
        {
            Alias = alias,
            Name = name,
            Resources = resources.ToList()
        };
        // Id and Version are read-only externally (managed by the repository
        // layer on save). Tests need deterministic fixture construction, so
        // set them via the backing property reflection — test-only, does not
        // leak into production code.
        SetReadOnly(ctx, "Id", Guid.NewGuid());
        SetReadOnly(ctx, "Version", version);
        return ctx;
    }

    private static AIContextResource MakeResource(
        string name,
        string? description,
        string resourceTypeId,
        int sortOrder,
        AIContextResourceInjectionMode injectionMode,
        object? settings)
    {
        var res = new AIContextResource
        {
            Name = name,
            Description = description,
            ResourceTypeId = resourceTypeId,
            SortOrder = sortOrder,
            InjectionMode = injectionMode,
            Settings = settings
        };
        SetReadOnly(res, "Id", Guid.NewGuid());
        return res;
    }

    private static void SetReadOnly(object target, string propertyName, object value)
    {
        var type = target.GetType();
        // Try property setter first (init-only or private set)
        var prop = type.GetProperty(propertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (prop?.SetMethod is not null)
        {
            prop.SetValue(target, value);
            return;
        }
        // Fall back to backing field
        var backingFieldName = $"<{propertyName}>k__BackingField";
        var field = type.GetField(backingFieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field is null)
        {
            throw new InvalidOperationException(
                $"Cannot set '{propertyName}' on {type.Name} — no setter and no backing field found");
        }
        field.SetValue(target, value);
    }

    private static IPublishedContent MakeNode(
        int id,
        string name,
        string contentTypeAlias,
        IPublishedContent? parent,
        params IPublishedProperty[] props)
    {
        var node = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        node.Id.Returns(id);
        node.Name.Returns(name);
        node.ContentType.Returns(contentType);
        node.Properties.Returns(props);
#pragma warning disable CS0618
        node.Parent.Returns(parent);
#pragma warning restore CS0618
        return node;
    }

    private static IPublishedProperty MakeProperty(string alias, string editorAlias, object? value)
    {
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns(editorAlias);
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        return prop;
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
