using System.Text.Json;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;
using Examine;
using Examine.Search;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using UmbracoContextReference = global::Umbraco.Cms.Core.UmbracoContextReference;
using UrlMode = global::Umbraco.Cms.Core.Models.PublishedContent.UrlMode;

namespace AgentRun.Umbraco.Tests.Tools;

/// <summary>
/// Unit tests for Story 11.12 <see cref="SearchContentTool"/>. Covers schema + registration
/// (AC1), happy-path query + result mapping (AC2), contentType filter (AC3),
/// parentId subtree filter incl. prefix-collision regression (AC4), count default/cap/clamp
/// (AC5), Lucene-escape injection defence (AC6), zero-results (AC7), argument validation
/// matrix (AC8), index-unavailable + query-failure graceful errors (AC9).
/// </summary>
[TestFixture]
public class SearchContentToolTests
{
    private IExamineManager _examineManager = null!;
    private IUmbracoContextFactory _contextFactory = null!;
    private IPublishedUrlProvider _urlProvider = null!;
    private CapturingLogger<SearchContentTool> _logger = null!;
    private SearchContentTool _tool = null!;
    private ToolExecutionContext _context = null!;

    // Search-pipeline mocks captured so tests can inspect the chain calls.
    private IIndex _index = null!;
    private ISearcher _searcher = null!;
    private IQuery _query = null!;
    private IBooleanOperation _booleanOp = null!;
    private IQuery _andQuery = null!;
    private IBooleanOperation _andBoolean = null!;
    private QueryOptions? _capturedQueryOptions;
    private string? _capturedNativeQuery;
    private string? _capturedFilterField;
    private string? _capturedFilterValue;

    [SetUp]
    public void SetUp()
    {
        _examineManager = Substitute.For<IExamineManager>();
        _contextFactory = Substitute.For<IUmbracoContextFactory>();
        _urlProvider = Substitute.For<IPublishedUrlProvider>();
        _logger = new CapturingLogger<SearchContentTool>();

        // Umbraco context — tool wraps GetUrl calls in EnsureUmbracoContext per D10
        var umbracoContext = Substitute.For<IUmbracoContext>();
        var contextAccessor = Substitute.For<IUmbracoContextAccessor>();
        var contextRef = new UmbracoContextReference(umbracoContext, false, contextAccessor);
        _contextFactory.EnsureUmbracoContext().Returns(contextRef);

        // Default index pipeline — each test can override as needed
        _index = Substitute.For<IIndex>();
        _searcher = Substitute.For<ISearcher>();
        _query = Substitute.For<IQuery>();
        _booleanOp = Substitute.For<IBooleanOperation>();
        _andQuery = Substitute.For<IQuery>();
        _andBoolean = Substitute.For<IBooleanOperation>();

        _index.Searcher.Returns(_searcher);
        _searcher.CreateQuery(Arg.Any<string>(), Arg.Any<BooleanOperation>()).Returns(_query);
        _query.NativeQuery(Arg.Do<string>(s => _capturedNativeQuery = s)).Returns(_booleanOp);
        _booleanOp.And().Returns(_andQuery);
        _andQuery.Field(
            Arg.Do<string>(f => _capturedFilterField = f),
            Arg.Do<string>(v => _capturedFilterValue = v)).Returns(_andBoolean);

        _examineManager.TryGetIndex(Arg.Any<string>(), out Arg.Any<IIndex?>())
            .Returns(ci =>
            {
                ci[1] = _index;
                return true;
            });

        _tool = new SearchContentTool(_examineManager, _contextFactory, _urlProvider, _logger);

        var step = new StepDefinition { Id = "step-1", Name = "Scout", Agent = "agents/scout.md" };
        var workflow = new WorkflowDefinition { Name = "Test", Alias = "test", Steps = { step } };
        _context = new ToolExecutionContext(Path.GetTempPath(), "inst-001", "step-1", "test")
        {
            Step = step,
            Workflow = workflow
        };
    }

    private void ArrangeResults(params ISearchResult[] hits)
    {
        var results = Substitute.For<ISearchResults>();
        results.GetEnumerator().Returns(ci => ((IEnumerable<ISearchResult>)hits).GetEnumerator());
        _booleanOp.Execute(Arg.Do<QueryOptions>(q => _capturedQueryOptions = q)).Returns(results);
        _andBoolean.Execute(Arg.Do<QueryOptions>(q => _capturedQueryOptions = q)).Returns(results);
    }

    private static ISearchResult MakeHit(
        int id,
        string name = "",
        string contentType = "",
        string path = "",
        int level = 1,
        float score = 1.0f)
    {
        var result = Substitute.For<ISearchResult>();
        result.Id.Returns(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        result.Score.Returns(score);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(name)) values["nodeName"] = name;
        if (!string.IsNullOrEmpty(contentType)) values["__NodeTypeAlias"] = contentType;
        if (!string.IsNullOrEmpty(path)) values["__Path"] = path;
        values["level"] = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Values.Returns(values);
        return result;
    }

    private async Task<string> Exec(IDictionary<string, object?> args)
        => (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

    // ---------- AC1: registration + schema ----------

    [Test]
    public void Name_IsSearchContent()
    {
        Assert.That(_tool.Name, Is.EqualTo("search_content"));
    }

    [Test]
    public void Description_IsUnderLimit_AndDescribesTool()
    {
        var desc = _tool.Description;
        Assert.That(desc.Length, Is.LessThanOrEqualTo(400),
            "Description must be under 400 chars per AC1");
        Assert.That(desc, Does.Contain("search").IgnoreCase);
        Assert.That(desc, Does.Contain("Examine"));
    }

    [Test]
    public void ParameterSchema_HasExpectedShape()
    {
        Assert.That(_tool.ParameterSchema, Is.Not.Null);
        var schema = _tool.ParameterSchema!.Value;

        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));
        Assert.That(schema.GetProperty("additionalProperties").GetBoolean(), Is.False);

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.That(required, Is.EqualTo(new[] { "query" }));

        var props = schema.GetProperty("properties");
        foreach (var name in new[] { "query", "contentType", "parentId", "count" })
        {
            Assert.That(props.TryGetProperty(name, out _), Is.True,
                $"Schema must declare '{name}' property");
        }

        Assert.That(props.GetProperty("query").GetProperty("type").GetString(), Is.EqualTo("string"));
        Assert.That(props.GetProperty("contentType").GetProperty("type").GetString(), Is.EqualTo("string"));
        // parentId accepts both integer (legacy) and GUID string (preferred) post-GUID retrofit.
        var parentIdType = props.GetProperty("parentId").GetProperty("type");
        var parentIdTypes = parentIdType.EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.That(parentIdTypes, Is.EquivalentTo(new[] { "integer", "string" }));
        Assert.That(props.GetProperty("count").GetProperty("type").GetString(), Is.EqualTo("integer"));
    }

    // ---------- AC2: happy-path query + result mapping ----------

    [Test]
    public async Task HappyPath_QueryReturnsHitsInCamelCaseJsonArray()
    {
        _urlProvider.GetUrl(1100, Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns("/articles/pension-reform-2026");
        _urlProvider.GetUrl(1200, Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns("/services/pension");

        ArrangeResults(
            MakeHit(1100, "Pension Reform 2026", "articlePage", "-1,1000,1100", 3, 4.72f),
            MakeHit(1200, "Our Pension Services", "servicesPage", "-1,1000,1200", 2, 3.15f));

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "pension" });
        var json = JsonDocument.Parse(result).RootElement;

        Assert.That(json.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(json.GetArrayLength(), Is.EqualTo(2));

        var first = json[0];
        Assert.That(first.GetProperty("id").GetInt32(), Is.EqualTo(1100));
        Assert.That(first.GetProperty("name").GetString(), Is.EqualTo("Pension Reform 2026"));
        Assert.That(first.GetProperty("contentType").GetString(), Is.EqualTo("articlePage"));
        Assert.That(first.GetProperty("url").GetString(), Is.EqualTo("/articles/pension-reform-2026"));
        Assert.That(first.GetProperty("level").GetInt32(), Is.EqualTo(3));
        Assert.That(first.GetProperty("relevanceScore").GetDouble(), Is.EqualTo(4.72d).Within(0.001));

        _examineManager.Received(1).TryGetIndex("ExternalIndex", out Arg.Any<IIndex?>());
        _searcher.Received(1).CreateQuery("content", BooleanOperation.And);
        Assert.That(_capturedQueryOptions, Is.Not.Null);
        Assert.That(_capturedQueryOptions!.Skip, Is.EqualTo(0));
        Assert.That(_capturedQueryOptions.Take, Is.EqualTo(10), "Default count per D9 is 10");
    }

    [Test]
    public async Task HappyPath_MissingFields_FallBackToSafeDefaults()
    {
        var result = Substitute.For<ISearchResult>();
        result.Id.Returns("500");
        result.Score.Returns(2.0f);
        result.Values.Returns(new Dictionary<string, string>(StringComparer.Ordinal));
        ArrangeResults(result);

        var json = JsonDocument.Parse(await Exec(new Dictionary<string, object?> { ["query"] = "x" })).RootElement;
        Assert.That(json[0].GetProperty("name").GetString(), Is.EqualTo(""));
        Assert.That(json[0].GetProperty("contentType").GetString(), Is.EqualTo(""));
        Assert.That(json[0].GetProperty("url").GetString(), Is.EqualTo(""));
        Assert.That(json[0].GetProperty("level").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task RelevanceScore_IsRoundedToTwoDecimals()
    {
        ArrangeResults(MakeHit(1, "x", score: 3.14159f));
        var json = JsonDocument.Parse(await Exec(new Dictionary<string, object?> { ["query"] = "x" })).RootElement;
        Assert.That(json[0].GetProperty("relevanceScore").GetDouble(), Is.EqualTo(3.14d).Within(0.001));
    }

    // ---------- AC3: contentType filter ----------

    [Test]
    public async Task ContentTypeFilter_ChainsAndFieldNodeTypeAlias()
    {
        ArrangeResults(MakeHit(1100, "Pension Reform", "articlePage", "-1,1100"));

        await Exec(new Dictionary<string, object?>
        {
            ["query"] = "pension",
            ["contentType"] = "articlePage"
        });

        _booleanOp.Received(1).And();
        Assert.That(_capturedFilterField, Is.EqualTo("__NodeTypeAlias"),
            "D5: the canonical field is __NodeTypeAlias (underscore-prefixed), NOT nodeTypeAlias");
        Assert.That(_capturedFilterValue, Is.EqualTo("articlePage"),
            "D7: contentType is NOT Lucene-escaped — Umbraco aliases are validated identifiers");
    }

    [Test]
    public async Task ContentTypeFilter_WhitespaceIsTreatedAsOmitted()
    {
        ArrangeResults();
        await Exec(new Dictionary<string, object?>
        {
            ["query"] = "x",
            ["contentType"] = "   "
        });
        _booleanOp.DidNotReceive().And();
    }

    // ---------- AC4: parentId subtree filter (post-query CSV walk per D6) ----------

    [Test]
    public async Task ParentIdFilter_IncludesDescendantsAndExcludesOthers()
    {
        _urlProvider.GetUrl(Arg.Any<int>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns(string.Empty);

        ArrangeResults(
            MakeHit(1100, "Child", path: "-1,1000,1100"),
            MakeHit(1200, "Other Child", path: "-1,1000,1200"),
            MakeHit(2100, "Different Tree", path: "-1,2000,2100"),
            MakeHit(1300, "Grandchild", path: "-1,1000,1100,1300"));

        var json = JsonDocument.Parse(await Exec(new Dictionary<string, object?>
        {
            ["query"] = "test",
            ["parentId"] = 1000
        })).RootElement;

        var ids = json.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        Assert.That(ids, Is.EquivalentTo(new[] { 1100, 1200, 1300 }),
            "Descendants of 1000 only; 2100 (different tree) excluded");
    }

    [Test]
    public async Task ParentIdFilter_DoesNotPrefixMatch_RegressionForIdCollision()
    {
        _urlProvider.GetUrl(Arg.Any<int>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns(string.Empty);

        // Critical regression: naive Contains("1000") would false-match path "-1,10000,10001".
        // D6's comma-delimited predicate (",1000," or end-",1000") must exclude that case.
        ArrangeResults(
            MakeHit(1100, "Real descendant", path: "-1,1000,1100"),
            MakeHit(10001, "Prefix collision", path: "-1,10000,10001"));

        var json = JsonDocument.Parse(await Exec(new Dictionary<string, object?>
        {
            ["query"] = "test",
            ["parentId"] = 1000
        })).RootElement;

        var ids = json.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        Assert.That(ids, Is.EqualTo(new[] { 1100 }),
            "10001 must NOT be returned — prefix-collision bug guard");
    }

    [Test]
    public async Task ParentIdFilter_IncludesNodeThatMatchesParentIdItself()
    {
        _urlProvider.GetUrl(Arg.Any<int>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>())
            .Returns(string.Empty);

        // Node whose __Path ends with ,1000 — the node IS the parent. Included
        // (matches AncestorsOrSelf semantics).
        ArrangeResults(MakeHit(1000, "The parent node itself", path: "-1,1000"));

        var json = JsonDocument.Parse(await Exec(new Dictionary<string, object?>
        {
            ["query"] = "test",
            ["parentId"] = 1000
        })).RootElement;

        Assert.That(json.GetArrayLength(), Is.EqualTo(1));
    }

    // ---------- AC5: count default/cap/clamp ----------

    [Test]
    public async Task Count_DefaultsTo10()
    {
        ArrangeResults();
        await Exec(new Dictionary<string, object?> { ["query"] = "x" });
        Assert.That(_capturedQueryOptions!.Take, Is.EqualTo(10));
    }

    [Test]
    public async Task Count_ExplicitValueIsHonored()
    {
        ArrangeResults();
        await Exec(new Dictionary<string, object?> { ["query"] = "x", ["count"] = 25 });
        Assert.That(_capturedQueryOptions!.Take, Is.EqualTo(25));
    }

    [Test]
    public async Task Count_AboveMaxIsClampedTo50_WithDebugLog()
    {
        ArrangeResults();
        await Exec(new Dictionary<string, object?> { ["query"] = "x", ["count"] = 100 });
        Assert.That(_capturedQueryOptions!.Take, Is.EqualTo(50));
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Debug && e.Message.Contains("clamped to 50")), Is.True,
            "Debug log should record the clamp per AC5");
    }

    // ---------- AC6: Lucene escape ----------

    [Test]
    public async Task Query_LuceneReservedCharsAreEscapedBeforeNativeQuery()
    {
        ArrangeResults();
        await Exec(new Dictionary<string, object?> { ["query"] = "terms* with (parens) & bar:value" });

        Assert.That(_capturedNativeQuery, Is.Not.Null);
        // Expected output from Lucene.Net.QueryParsers.Classic.QueryParser.Escape
        Assert.That(_capturedNativeQuery, Is.EqualTo(@"terms\* with \(parens\) \& bar\:value"));
    }

    [Test]
    public async Task Query_AdversarialOperatorsDoNotThrow()
    {
        ArrangeResults();
        // "a && b" and "(oops" would break Lucene's parser if unescaped.
        await Exec(new Dictionary<string, object?> { ["query"] = "a && b (oops" });
        Assert.That(_capturedNativeQuery, Does.Contain(@"\&\&"));
        Assert.That(_capturedNativeQuery, Does.Contain(@"\("));
    }

    // ---------- AC7: zero-results ----------

    [Test]
    public async Task ZeroResults_ReturnsEmptyArrayNoError()
    {
        ArrangeResults();
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "zzznomatch" });
        Assert.That(result, Is.EqualTo("[]"));
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Debug && e.Message.Contains("0 hits")), Is.True);
    }

    // ---------- AC8: argument validation matrix ----------

    [Test]
    public void NoQuery_ThrowsToolExecutionException()
    {
        Assert.That(async () => await Exec(new Dictionary<string, object?>()),
            Throws.TypeOf<ToolExecutionException>()
                  .With.Message.Contains("query"));
    }

    [Test]
    public async Task EmptyQuery_ReturnsInvalidArgumentStructured()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "" });
        var json = JsonDocument.Parse(result).RootElement;
        Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(json.GetProperty("message").GetString(), Does.Contain("query"));
    }

    [Test]
    public async Task WhitespaceQuery_ReturnsInvalidArgumentStructured()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "   " });
        var json = JsonDocument.Parse(result).RootElement;
        Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
    }

    [Test]
    public async Task NegativeParentId_ReturnsInvalidArgumentStructured()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "x", ["parentId"] = -1 });
        var json = JsonDocument.Parse(result).RootElement;
        Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(json.GetProperty("message").GetString(), Does.Contain("parentId"));
    }

    [Test]
    public async Task ZeroCount_ReturnsInvalidArgumentStructured()
    {
        var result = await Exec(new Dictionary<string, object?> { ["query"] = "x", ["count"] = 0 });
        var json = JsonDocument.Parse(result).RootElement;
        Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("invalid_argument"));
        Assert.That(json.GetProperty("message").GetString(), Does.Contain("count"));
    }

    [Test]
    public void UnknownParameter_ThrowsToolExecutionException()
    {
        Assert.That(async () => await Exec(new Dictionary<string, object?>
        {
            ["query"] = "x",
            ["unknown_key"] = "y"
        }), Throws.TypeOf<ToolExecutionException>()
                  .With.Message.Contains("Unrecognised"));
    }

    [Test]
    public void NonStringQuery_ThrowsToolExecutionException()
    {
        Assert.That(async () => await Exec(new Dictionary<string, object?> { ["query"] = 42 }),
            Throws.TypeOf<ToolExecutionException>()
                  .With.Message.Contains("query").And.Message.Contains("string"));
    }

    // ---------- AC9: index-unavailable + search-failure + cancellation ----------

    [Test]
    public async Task IndexUnavailable_ReturnsStructuredErrorAndWarningLog()
    {
        _examineManager.TryGetIndex(Arg.Any<string>(), out Arg.Any<IIndex?>())
            .Returns(ci =>
            {
                ci[1] = null;
                return false;
            });

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "x" });
        var json = JsonDocument.Parse(result).RootElement;
        Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("index_unavailable"));
        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("ExternalIndex")), Is.True);
    }

    [Test]
    public async Task SearchFailure_ReturnsStructuredErrorAndWarningLogWithException()
    {
        _booleanOp.Execute(Arg.Any<QueryOptions>())
            .Throws(new IOException("segments file missing"));

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "x" });
        var json = JsonDocument.Parse(result).RootElement;
        Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("search_failure"));

        var warnings = _logger.Entries.Where(e => e.Level == LogLevel.Warning).ToArray();
        Assert.That(warnings.Any(w => w.Exception is IOException), Is.True,
            "Warning log must capture the IOException for diagnosis");
    }

    [Test]
    public void Cancellation_PropagatesOperationCanceledException()
    {
        _booleanOp.Execute(Arg.Any<QueryOptions>()).Throws(new OperationCanceledException());

        Assert.That(async () =>
            (string)await _tool.ExecuteAsync(
                new Dictionary<string, object?> { ["query"] = "x" },
                _context,
                CancellationToken.None),
            Throws.TypeOf<OperationCanceledException>());
    }

    // ---------- Engine-wiring ----------

    [Test]
    public void NullStep_ThrowsToolContextMissing()
    {
        var ctx = new ToolExecutionContext(Path.GetTempPath(), "i", "s", "w")
        {
            Step = null,
            Workflow = null
        };

        Assert.That(async () => await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "x" }, ctx, CancellationToken.None),
            Throws.TypeOf<ToolContextMissingException>());
    }

    // ---------- Post-2026-04-22 code review patches ----------

    [Test]
    public async Task KeyResolution_Fails_EmitsNullKeyNotGuidEmpty()
    {
        // Regression for Blind Hunter #2: when neither the Examine __Key field
        // nor the content cache resolves the hit's GUID key, the pre-patch code
        // emitted Guid.Empty. That value (a) collides across failed hits and
        // (b) is rejected by ExtractRequiredNodeRefArgument. The patch emits
        // `key: null` instead so agents can fall back to `id`.
        ArrangeResults(
            MakeHit(1100, "Unresolved 1", "articlePage", "-1,1000,1100"),
            MakeHit(1200, "Unresolved 2", "articlePage", "-1,1000,1200"));

        var result = await Exec(new Dictionary<string, object?> { ["query"] = "x" });
        var json = JsonDocument.Parse(result).RootElement;

        Assert.That(json.GetArrayLength(), Is.EqualTo(2),
            "Hits with unresolvable key must still be emitted, not dropped");

        foreach (var hit in json.EnumerateArray())
        {
            var keyElement = hit.GetProperty("key");
            Assert.That(keyElement.ValueKind, Is.EqualTo(JsonValueKind.Null),
                "key must be null (not Guid.Empty) when neither __Key nor content cache resolves");
        }

        Assert.That(_logger.Entries.Count(e => e.Level == LogLevel.Warning
            && e.Message.Contains("without a resolvable GUID key", StringComparison.Ordinal)),
            Is.EqualTo(1),
            "Per-execution Warning must fire exactly once regardless of the number of unresolved hits");
    }

    // ---------- Shared test helper ----------

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
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
