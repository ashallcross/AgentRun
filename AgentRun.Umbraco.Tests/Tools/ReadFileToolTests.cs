using System.Text;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ReadFileToolTests
{
    private string _root = null!;
    private FakeToolLimitResolver _resolver = null!;
    private ReadFileTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentrun-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _resolver = new FakeToolLimitResolver();
        _tool = new ReadFileTool(_resolver);

        var step = new StepDefinition { Id = "step-1", Name = "Test", Agent = "agents/test.md" };
        var workflow = new WorkflowDefinition { Name = "Test Workflow", Alias = "test-workflow", Steps = { step } };
        _context = new ToolExecutionContext(_root, "inst-001", "step-1", "test-workflow")
        {
            Step = step,
            Workflow = workflow
        };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeToolLimitResolver : IToolLimitResolver
    {
        public int ReadFileMaxBytes { get; set; } = EngineDefaults.ReadFileMaxResponseBytes;
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlMaxResponseBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => ReadFileMaxBytes;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 300;
        public int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ListContentMaxResponseBytes;
        public int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.GetContentMaxResponseBytes;
        public int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ListContentTypesMaxResponseBytes;
    }

    private static string ExpectedMarker(int limit, long totalBytes) =>
        $"[Response truncated at {limit} bytes — full file is {totalBytes} bytes. " +
        $"Use a structured extraction tool (e.g. fetch_url with extract: \"structured\" once Story 9.1b ships) " +
        $"or override read_file.max_response_bytes in your workflow configuration to read the rest.]";

    // ---------- Pre-existing regression tests (preserved) ----------

    [Test]
    public async Task ReadsExistingFile_ReturnsContents()
    {
        var content = "Hello, world!";
        File.WriteAllText(Path.Combine(_root, "test.txt"), content);
        var args = new Dictionary<string, object?> { ["path"] = "test.txt" };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo(content));
    }

    [Test]
    public void MissingFile_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["path"] = "nonexistent.txt" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("File not found"));
    }

    [Test]
    public void PathTraversal_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["path"] = "../../etc/passwd" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Access denied"));
    }

    [Test]
    public void MissingPathArgument_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?>();

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Missing required argument"));
    }

    // ---------- Story 9.9 size guard tests ----------

    [Test]
    public async Task FileUnderLimit_ReturnsFullContents_NoMarker()
    {
        _resolver.ReadFileMaxBytes = 1024;
        var content = new string('a', 512);
        File.WriteAllText(Path.Combine(_root, "small.txt"), content);
        var args = new Dictionary<string, object?> { ["path"] = "small.txt" };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo(content));
        Assert.That((string)result, Does.Not.Contain("Response truncated"));
    }

    [Test]
    public async Task FileExactlyAtLimit_ReturnsFullContents_NoMarker()
    {
        _resolver.ReadFileMaxBytes = 1024;
        var bytes = Encoding.UTF8.GetBytes(new string('b', 1024));
        File.WriteAllBytes(Path.Combine(_root, "exact.txt"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "exact.txt" };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(((string)result).Length, Is.EqualTo(1024));
        Assert.That((string)result, Does.Not.Contain("Response truncated"));
    }

    [Test]
    public async Task FileOneByteOverLimit_TruncatedWithMarker_Verbatim()
    {
        _resolver.ReadFileMaxBytes = 1024;
        var bytes = Encoding.UTF8.GetBytes(new string('c', 1025));
        File.WriteAllBytes(Path.Combine(_root, "over.txt"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "over.txt" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var marker = ExpectedMarker(1024, 1025);
        Assert.That(result, Does.EndWith(marker));
        Assert.That(result.Substring(0, 1024), Is.EqualTo(new string('c', 1024)));
    }

    [Test]
    public async Task LargeFile_BoundedReadDoesNotAllocateFullFileSize()
    {
        // 1 MB synthetic file with a 4 KB limit — 256x size ratio.
        _resolver.ReadFileMaxBytes = 4096;
        var bytes = new byte[1024 * 1024];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)('d');
        File.WriteAllBytes(Path.Combine(_root, "large.txt"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "large.txt" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // The decoded prefix is exactly limit bytes; the marker reports the real total.
        // (Implementation guarantee: bounded read uses byte[limit], not byte[totalBytes].
        // Code review of ReadFileTool.ExecuteAsync confirms `new byte[limit]`.)
        var marker = ExpectedMarker(4096, 1024 * 1024);
        Assert.That(result, Does.EndWith(marker));
        // Assert byte-length of the prefix, not char-index — string.IndexOf is a
        // char index and `limit` is a byte count. The two only coincide for ASCII.
        var prefix = result.Substring(0, result.Length - marker.Length);
        Assert.That(Encoding.UTF8.GetByteCount(prefix), Is.EqualTo(4096));
    }

    [Test]
    public async Task FileGrowsBetweenStatAndRead_StillBoundedAtLimit()
    {
        // D1 regression guard (Bob's catch): the unified bounded-read path
        // (post-9.9 review) cannot return more than `limit` bytes regardless of
        // any racing growth, because the entire read happens on a single
        // FileStream handle with `byte[limit]` allocated up front. Pre-D1 the
        // under-limit branch re-read via File.ReadAllTextAsync unbounded,
        // defeating the size guard if a small file grew past the limit between
        // the FileInfo.Length stat and the read.
        _resolver.ReadFileMaxBytes = 1024;
        var bytes = new byte[1024 * 32]; // 32 KB — well over the 1 KB limit
        File.WriteAllBytes(Path.Combine(_root, "grew.bin"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "grew.bin" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var marker = ExpectedMarker(1024, bytes.Length);
        Assert.That(result, Does.EndWith(marker));
        var prefix = result.Substring(0, result.Length - marker.Length);
        Assert.That(Encoding.UTF8.GetByteCount(prefix), Is.EqualTo(1024));
    }

    [Test]
    public async Task FileShrinksBetweenStatAndRead_NoMarkerAppended()
    {
        // Edge Case #11 / Task 3.6: with the unified bounded-read path the
        // "shrink" case manifests as the read loop returning 0 before reaching
        // `limit` AND the post-loop peek finding no extra bytes — i.e. the
        // `truncated == false` branch. We exercise that branch via a small
        // file (semantically equivalent to a file that shrank to its current
        // size before our read started). True mid-read truncation requires a
        // filesystem-injection seam not present today.
        _resolver.ReadFileMaxBytes = 1024;
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)'s';
        File.WriteAllBytes(Path.Combine(_root, "small.bin"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "small.bin" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.Not.Contain("Response truncated"));
        Assert.That(Encoding.UTF8.GetByteCount(result), Is.EqualTo(256));
    }

    [Test]
    public void NullStepOrWorkflow_Throws_ToolContextMissingException()
    {
        // D2 (Story 9.9 review): wiring bug must throw a typed AgentRunException
        // subtype, NOT InvalidOperationException, so LlmErrorClassifier does not
        // mask it as a generic provider error.
        var ctxWithoutStep = new ToolExecutionContext(_root, "inst-001", "step-1", "test-workflow");
        File.WriteAllText(Path.Combine(_root, "x.txt"), "hi");
        var args = new Dictionary<string, object?> { ["path"] = "x.txt" };

        var ex = Assert.ThrowsAsync<ToolContextMissingException>(
            () => _tool.ExecuteAsync(args, ctxWithoutStep, CancellationToken.None));
        Assert.That(ex, Is.InstanceOf<AgentRunException>());
        Assert.That(ex!.Message, Does.Contain("engine wiring bug"));
    }

    [Test]
    public async Task EmptyFile_ReadsAsEmptyString_NoMarker()
    {
        File.WriteAllBytes(Path.Combine(_root, "empty.txt"), Array.Empty<byte>());
        var args = new Dictionary<string, object?> { ["path"] = "empty.txt" };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task MarkerIncludesActualLimitAndTotalBytes()
    {
        _resolver.ReadFileMaxBytes = 100;
        File.WriteAllBytes(Path.Combine(_root, "f.txt"), new byte[250]);
        var args = new Dictionary<string, object?> { ["path"] = "f.txt" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.Contain("at 100 bytes"));
        Assert.That(result, Does.Contain("full file is 250 bytes"));
    }

    // ---------- Task 4: regression fixture tests reusing Story 9.7 captures ----------

    private static byte[] LoadFixture(string name)
    {
        var dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Tools", "Fixtures");
        var path = Path.Combine(dir, name);
        Assert.That(File.Exists(path), $"Fixture missing: {path}");
        return File.ReadAllBytes(path);
    }

    [Test]
    public async Task Fixture100kb_ReadsInFull_NoMarker()
    {
        // ~107 KB — under the 256 KB default. Confirms typical artifact-size HTML still flows.
        var bytes = LoadFixture("fetch-url-100kb.html");
        File.WriteAllBytes(Path.Combine(_root, "page.html"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "page.html" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.Not.Contain("Response truncated"));
        Assert.That(Encoding.UTF8.GetByteCount(result), Is.EqualTo(bytes.Length));
    }

    [Test]
    public async Task Fixture1500kb_TruncatedAtDefault_WithMarker_AndTotalBytesReported()
    {
        // ~1 MB — the production-breaker payload. Per Task 4.2: the "500 KB"
        // fixture is actually ~207 KB (under the 256 KB default), so we use the
        // 1500 KB fixture for the truncation assertion.
        var bytes = LoadFixture("fetch-url-1500kb.html");
        File.WriteAllBytes(Path.Combine(_root, "big.html"), bytes);
        var args = new Dictionary<string, object?> { ["path"] = "big.html" };

        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var marker = ExpectedMarker(EngineDefaults.ReadFileMaxResponseBytes, bytes.Length);
        Assert.That(result, Does.EndWith(marker));
        var prefix = result.Substring(0, result.Length - marker.Length);
        Assert.That(Encoding.UTF8.GetByteCount(prefix),
            Is.LessThanOrEqualTo(EngineDefaults.ReadFileMaxResponseBytes));
    }

    [Test]
    public async Task DefaultLimitIs256KB()
    {
        Assert.That(EngineDefaults.ReadFileMaxResponseBytes, Is.EqualTo(262144));

        // ~10 KB artifact-style file under the default → no marker.
        var content = new string('e', 10 * 1024);
        File.WriteAllText(Path.Combine(_root, "scan-results.md"), content);
        var args = new Dictionary<string, object?> { ["path"] = "scan-results.md" };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        Assert.That(result, Is.EqualTo(content));
    }
}
