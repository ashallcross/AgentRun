using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ListFilesToolTests
{
    private string _root = null!;
    private ListFilesTool _tool = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentrun-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tool = new ListFilesTool();
        _context = new ToolExecutionContext(_root, "inst-001", "step-1", "test-workflow");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task ListsFiles_Recursively()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");
        File.WriteAllText(Path.Combine(_root, "sub", "b.txt"), "");

        var args = new Dictionary<string, object?>();

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        var lines = result.ToString()!.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines, Does.Contain("a.txt"));
        Assert.That(lines, Does.Contain(Path.Combine("sub", "b.txt")));
    }

    [Test]
    public async Task ReturnsRelativePaths()
    {
        File.WriteAllText(Path.Combine(_root, "file.txt"), "");
        var args = new Dictionary<string, object?>();

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result.ToString(), Does.Not.Contain(_root));
        Assert.That(result.ToString(), Is.EqualTo("file.txt"));
    }

    [Test]
    public async Task EmptyDirectory_ReturnsEmptyString()
    {
        var args = new Dictionary<string, object?>();

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void NonExistentDirectory_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["path"] = "nonexistent" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Directory not found"));
    }

    [Test]
    public void PathTraversal_ThrowsToolExecutionException()
    {
        var args = new Dictionary<string, object?> { ["path"] = "../../etc" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Access denied"));
    }
}
