using System.Text;
using System.Text.Json;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class FetchCacheWriterTests
{
    private FetchCacheWriter _writer = null!;
    private string _instanceRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _writer = new FetchCacheWriter();
        _instanceRoot = Path.Combine(Path.GetTempPath(), "agentrun-cachewriter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_instanceRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_instanceRoot))
            Directory.Delete(_instanceRoot, recursive: true);
    }

    [Test]
    public async Task WriteHandleAsync_HappyPath_WritesFileAndReturnsHandleJson()
    {
        var body = Encoding.UTF8.GetBytes("<html><body>hi</body></html>");

        var result = await _writer.WriteHandleAsync(
            _instanceRoot,
            "https://example.com",
            status: 200,
            contentType: "text/html",
            body: body,
            unmarkedLength: body.Length,
            truncated: false,
            cancellationToken: CancellationToken.None);

        using var handle = JsonDocument.Parse(result);
        var savedTo = handle.RootElement.GetProperty("saved_to").GetString();
        Assert.That(savedTo, Is.Not.Null);
        Assert.That(savedTo, Does.StartWith(".fetch-cache/"));

        var absolutePath = Path.Combine(_instanceRoot, savedTo!);
        Assert.That(File.Exists(absolutePath), Is.True, "cache file must be created at sandbox-resolved path");
        Assert.That(handle.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task WriteHandleAsync_PathSandboxReject_ThrowsToolExecutionException()
    {
        // Non-existent root with traversal-unfriendly name: PathSandbox still resolves
        // the canonical path, but Directory.CreateDirectory should fail under an invalid
        // root. A cleaner reject is an empty-string root which fails path validation.
        var body = Encoding.UTF8.GetBytes("hi");

        Assert.ThrowsAsync<ToolExecutionException>(async () =>
            await _writer.WriteHandleAsync(
                instanceFolderPath: "",
                url: "https://example.com",
                status: 200,
                contentType: "text/html",
                body: body,
                unmarkedLength: body.Length,
                truncated: false,
                cancellationToken: CancellationToken.None));
    }

    [Test]
    public async Task WriteHandleAsync_DirectoryAutoCreatedWhenMissing()
    {
        // .fetch-cache/ does not exist at this point; the writer must create it.
        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.False);

        var body = Encoding.UTF8.GetBytes("ok");
        var result = await _writer.WriteHandleAsync(
            _instanceRoot,
            "https://example.com/a",
            status: 200,
            contentType: "text/html",
            body: body,
            unmarkedLength: body.Length,
            truncated: false,
            cancellationToken: CancellationToken.None);

        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.True);
        using var handle = JsonDocument.Parse(result);
        Assert.That(handle.RootElement.GetProperty("saved_to").GetString(), Does.StartWith(".fetch-cache/"));
    }
}
