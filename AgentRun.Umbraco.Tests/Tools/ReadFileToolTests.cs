using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ReadFileToolTests
{
    private string _root = null!;
    private ReadFileTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentrun-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tool = new ReadFileTool();
        _context = new ToolExecutionContext(_root, "inst-001", "step-1", "test-workflow");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

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
}
