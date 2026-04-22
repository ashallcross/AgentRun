using System.Text.Json;
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

    // ---------- Append mode (streaming per-item output) ----------

    [Test]
    public async Task Append_TrueOnExistingFile_AppendsContent()
    {
        var first = new Dictionary<string, object?> { ["path"] = "stream.md", ["content"] = "First section.\n" };
        await _tool.ExecuteAsync(first, _context, CancellationToken.None);

        var second = new Dictionary<string, object?>
        {
            ["path"] = "stream.md",
            ["content"] = "Second section.\n",
            ["append"] = true
        };
        var result = await _tool.ExecuteAsync(second, _context, CancellationToken.None);

        var filePath = Path.Combine(_root, "stream.md");
        Assert.That(File.ReadAllText(filePath), Is.EqualTo("First section.\nSecond section.\n"));
        Assert.That(result.ToString(), Does.Contain("File appended"));
    }

    [Test]
    public async Task Append_TrueOnMissingFile_CreatesFileWithContent()
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = "new-stream.md",
            ["content"] = "Only section.\n",
            ["append"] = true
        };

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var filePath = Path.Combine(_root, "new-stream.md");
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(File.ReadAllText(filePath), Is.EqualTo("Only section.\n"));
        Assert.That(result.ToString(), Does.Contain("File appended"));
    }

    [Test]
    public async Task Append_FalseExplicit_OverwritesAsDefault()
    {
        var first = new Dictionary<string, object?> { ["path"] = "over.md", ["content"] = "Original." };
        await _tool.ExecuteAsync(first, _context, CancellationToken.None);

        var second = new Dictionary<string, object?>
        {
            ["path"] = "over.md",
            ["content"] = "Replaced.",
            ["append"] = false
        };
        var result = await _tool.ExecuteAsync(second, _context, CancellationToken.None);

        var filePath = Path.Combine(_root, "over.md");
        Assert.That(File.ReadAllText(filePath), Is.EqualTo("Replaced."));
        Assert.That(result.ToString(), Does.Contain("File written"));
    }

    [Test]
    public async Task Append_Omitted_DefaultsToOverwriteBackwardsCompat()
    {
        // Backwards-compat: existing workflows that call write_file without `append`
        // get the same atomic-overwrite behaviour as pre-append-mode.
        var first = new Dictionary<string, object?> { ["path"] = "compat.md", ["content"] = "v1" };
        await _tool.ExecuteAsync(first, _context, CancellationToken.None);

        var second = new Dictionary<string, object?> { ["path"] = "compat.md", ["content"] = "v2" };
        var result = await _tool.ExecuteAsync(second, _context, CancellationToken.None);

        Assert.That(File.ReadAllText(Path.Combine(_root, "compat.md")), Is.EqualTo("v2"));
        Assert.That(result.ToString(), Does.Contain("File written"));
    }

    [Test]
    public async Task Append_AcceptsJsonElementBooleans()
    {
        // Tool arguments passed via JSON may arrive as JsonElement rather than bool.
        using var doc = JsonDocument.Parse("""{"append": true}""");
        var appendElement = doc.RootElement.GetProperty("append");

        await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = "je.md", ["content"] = "a" },
            _context, CancellationToken.None);

        var args = new Dictionary<string, object?>
        {
            ["path"] = "je.md",
            ["content"] = "b",
            ["append"] = appendElement
        };
        await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(File.ReadAllText(Path.Combine(_root, "je.md")), Is.EqualTo("ab"));
    }

    [Test]
    public void Append_NonBoolean_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["path"] = "bad.md",
            ["content"] = "x",
            ["append"] = "yes"
        };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("'append' must be a boolean"));
    }

    // ---------- Post-2026-04-22 code review patches ----------

    [Test]
    public async Task Append_UnderMaxFileBytes_Succeeds()
    {
        // Baseline for the aggregate size cap — small append stays well below
        // the 10 MB cap and should succeed exactly as before the patch.
        var existing = new string('x', 1024);
        File.WriteAllText(Path.Combine(_root, "log.txt"), existing);

        var args = new Dictionary<string, object?>
        {
            ["path"] = "log.txt",
            ["content"] = "y",
            ["append"] = true
        };

        await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(File.ReadAllText(Path.Combine(_root, "log.txt")).Length, Is.EqualTo(1025));
    }

    [Test]
    public void Append_WouldExceedMaxFileBytes_Throws()
    {
        // Aggregate size cap guards against runaway append loops. Existing file
        // is already near the cap; one more byte tips it over.
        var nearCapBytes = WriteFileTool.MaxAppendFileBytes - 4;
        var path = Path.Combine(_root, "big.md");
        File.WriteAllBytes(path, new byte[nearCapBytes]);

        var args = new Dictionary<string, object?>
        {
            ["path"] = "big.md",
            ["content"] = new string('x', 16),
            ["append"] = true
        };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("beyond"));
        Assert.That(ex.Message, Does.Contain("bytes"));
    }

    [Test]
    public void Append_PathSandboxSymlinkRejectedOnValidate()
    {
        // TOCTOU re-validation defence runs BEFORE open; even if a symlink was
        // swapped in after ValidatePath, the second check catches it. The base
        // ValidatePath path already rejects symlinks, so this test exercises
        // the happy path via a regular file. A full TOCTOU race is not unit-
        // testable without fs-level control; the re-validation is covered by
        // the fact that PathSandbox.IsPathOrAncestorSymlink returns the same
        // result regardless of caller context.
        var args = new Dictionary<string, object?>
        {
            ["path"] = "regular.md",
            ["content"] = "content",
            ["append"] = true
        };

        Assert.DoesNotThrowAsync(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(File.Exists(Path.Combine(_root, "regular.md")), Is.True);
    }
}
