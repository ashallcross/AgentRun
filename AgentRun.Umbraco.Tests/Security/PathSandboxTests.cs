using AgentRun.Umbraco.Security;

namespace AgentRun.Umbraco.Tests.Security;

[TestFixture]
public class PathSandboxTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentrun-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ValidPath_WithinRoot_ReturnsCanonicalPath()
    {
        var subDir = Path.Combine(_root, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "test.txt"), "hello");

        var result = PathSandbox.ValidatePath("sub/test.txt", _root);

        Assert.That(result, Is.EqualTo(Path.Combine(_root, "sub", "test.txt")));
    }

    [Test]
    public void PathWithDotDot_StaysWithinRoot_IsAccepted()
    {
        var subDir = Path.Combine(_root, "a", "b");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_root, "a", "target.txt"), "hello");

        var result = PathSandbox.ValidatePath("a/b/../target.txt", _root);

        Assert.That(result, Is.EqualTo(Path.Combine(_root, "a", "target.txt")));
    }

    [Test]
    public void PathWithDotDot_EscapesRoot_ThrowsUnauthorizedAccessException()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => PathSandbox.ValidatePath("../../etc/passwd", _root));

        Assert.That(ex!.Message, Does.Contain("Access denied"));
        Assert.That(ex.Message, Does.Contain("outside the instance folder"));
    }

    [Test]
    public void AbsolutePathOutsideRoot_ThrowsUnauthorizedAccessException()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => PathSandbox.ValidatePath("/etc/passwd", _root));

        Assert.That(ex!.Message, Does.Contain("Access denied"));
    }

    [Test]
    public void SymlinkFile_ThrowsUnauthorizedAccessException()
    {
        var targetFile = Path.Combine(_root, "real.txt");
        File.WriteAllText(targetFile, "real content");
        var linkPath = Path.Combine(_root, "link.txt");

        try
        {
            File.CreateSymbolicLink(linkPath, targetFile);
        }
        catch (Exception)
        {
            Assert.Inconclusive("Symlink creation not supported on this platform/elevation");
            return;
        }

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => PathSandbox.ValidatePath("link.txt", _root));

        Assert.That(ex!.Message, Does.Contain("symbolic links are not permitted"));
    }

    [Test]
    public void SymlinkDirectory_ThrowsUnauthorizedAccessException()
    {
        var realDir = Path.Combine(_root, "realdir");
        Directory.CreateDirectory(realDir);
        File.WriteAllText(Path.Combine(realDir, "file.txt"), "content");
        var linkDir = Path.Combine(_root, "linkdir");

        try
        {
            Directory.CreateSymbolicLink(linkDir, realDir);
        }
        catch (Exception)
        {
            Assert.Inconclusive("Symlink creation not supported on this platform/elevation");
            return;
        }

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => PathSandbox.ValidatePath("linkdir/file.txt", _root));

        Assert.That(ex!.Message, Does.Contain("symbolic links are not permitted"));
    }

    [Test]
    public void GetRelativePath_ReturnsCorrectRelativePath()
    {
        var fullPath = Path.Combine(_root, "sub", "file.txt");

        var result = PathSandbox.GetRelativePath(fullPath, _root);

        Assert.That(result, Is.EqualTo(Path.Combine("sub", "file.txt")));
    }

    [Test]
    public void EmptyPath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => PathSandbox.ValidatePath("", _root));

        Assert.That(ex!.Message, Does.Contain("must not be null or empty"));
    }

    [Test]
    public void NullPath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => PathSandbox.ValidatePath(null!, _root));

        Assert.That(ex!.Message, Does.Contain("must not be null or empty"));
    }

    [Test]
    public void WhitespacePath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => PathSandbox.ValidatePath("   ", _root));

        Assert.That(ex!.Message, Does.Contain("must not be null or empty"));
    }

    [Test]
    public void RootWithTrailingSeparator_HandledConsistently()
    {
        File.WriteAllText(Path.Combine(_root, "test.txt"), "hello");
        var rootWithSep = _root + Path.DirectorySeparatorChar;

        var result = PathSandbox.ValidatePath("test.txt", rootWithSep);

        Assert.That(result, Is.EqualTo(Path.Combine(_root, "test.txt")));
    }

    [Test]
    public void RootWithoutTrailingSeparator_HandledConsistently()
    {
        File.WriteAllText(Path.Combine(_root, "test.txt"), "hello");

        var result = PathSandbox.ValidatePath("test.txt", _root.TrimEnd(Path.DirectorySeparatorChar));

        Assert.That(result, Is.EqualTo(Path.Combine(_root, "test.txt")));
    }
}
