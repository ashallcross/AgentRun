using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class WriteFileToolTests
{
    private string _root = null!;
    private WriteFileTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentrun-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tool = new WriteFileTool();
        _context = new ToolExecutionContext(_root, "inst-001", "step-1", "test-workflow");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task WritesFile_ReturnsConfirmation()
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = "output.txt",
            ["content"] = "Hello, world!"
        };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result.ToString(), Does.Contain("File written"));
        Assert.That(File.ReadAllText(Path.Combine(_root, "output.txt")), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task CreatesParentDirectories_IfNotExist()
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = "sub/deep/file.txt",
            ["content"] = "nested content"
        };

        await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(_root, "sub", "deep", "file.txt")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_root, "sub", "deep", "file.txt")), Is.EqualTo("nested content"));
    }

    [Test]
    public async Task OverwritesExistingFile_ViaAtomicWrite()
    {
        var filePath = Path.Combine(_root, "existing.txt");
        File.WriteAllText(filePath, "original");

        var args = new Dictionary<string, object?>
        {
            ["path"] = "existing.txt",
            ["content"] = "updated"
        };

        await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(File.ReadAllText(filePath), Is.EqualTo("updated"));
        // Verify no .tmp file left behind
        Assert.That(File.Exists(filePath + ".tmp"), Is.False);
    }

    [Test]
    public void PathTraversal_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = "../../etc/evil",
            ["content"] = "bad"
        };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Access denied"));
    }

    [Test]
    public void MissingPathArgument_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["content"] = "hello" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Missing required argument: 'path'"));
    }

    [Test]
    public void MissingContentArgument_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["path"] = "test.txt" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Missing required argument: 'content'"));
    }

    [Test]
    public async Task EmptyContent_CreatesEmptyFile()
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = "empty.txt",
            ["content"] = ""
        };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var filePath = Path.Combine(_root, "empty.txt");
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(File.ReadAllText(filePath), Is.EqualTo(string.Empty));
        Assert.That(result.ToString(), Does.Contain("File written"));
    }
}
