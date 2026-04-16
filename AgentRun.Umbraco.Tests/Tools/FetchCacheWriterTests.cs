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
    public void WriteHandleAsync_EmptyInstanceFolderPath_ThrowsToolExecutionException()
    {
        // Argument-validation path: an empty instance folder fails PathSandbox's
        // null/whitespace guard before any file I/O is attempted.
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
    [Platform(Exclude = "Win", Reason = "Symlink creation requires admin on Windows")]
    public void WriteHandleAsync_SymlinkedCacheDir_RejectedBySandbox()
    {
        // Defence-in-depth (architect review 2026-04-08, Story 9.10): PathSandbox
        // must reject when the cache directory is a symlink. Proves the writer
        // propagates the sandbox rejection as ToolExecutionException rather than
        // silently writing through the symlink.
        var fetchCacheDir = Path.Combine(_instanceRoot, ".fetch-cache");
        var realTarget = Path.Combine(Path.GetTempPath(), "agentrun-symlink-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(realTarget);
        try
        {
            Directory.CreateSymbolicLink(fetchCacheDir, realTarget);

            var body = Encoding.UTF8.GetBytes("leak");
            var ex = Assert.ThrowsAsync<ToolExecutionException>(async () =>
                await _writer.WriteHandleAsync(
                    _instanceRoot,
                    "https://example.com/sym",
                    status: 200,
                    contentType: "text/html",
                    body: body,
                    unmarkedLength: body.Length,
                    truncated: false,
                    cancellationToken: CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain(".fetch-cache/"));
            Assert.That(File.Exists(Path.Combine(realTarget, "leak.html")), Is.False,
                "sandbox must not allow writes through the symlink target");
        }
        finally
        {
            if (Directory.Exists(fetchCacheDir))
                Directory.Delete(fetchCacheDir);
            if (Directory.Exists(realTarget))
                Directory.Delete(realTarget, recursive: true);
        }
    }

    [Test]
    public void WriteHandleAsync_UnmarkedLengthOutOfRange_ThrowsArgumentOutOfRange()
    {
        // Story 10.7a review patch P5 — unguarded Buffer.BlockCopy used to escape
        // the IOException filter on a bad unmarkedLength. Now explicitly rejected.
        var body = Encoding.UTF8.GetBytes("hi");

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _writer.WriteHandleAsync(
                _instanceRoot,
                "https://example.com/bad",
                status: 200,
                contentType: "text/html",
                body: body,
                unmarkedLength: body.Length + 10,
                truncated: false,
                cancellationToken: CancellationToken.None));
    }

    [Test]
    public async Task TryReadHandleAsync_RoundTripsStatusAndContentType()
    {
        // Story 10.7a review patch P4 — cache-hit must return the real HTTP status
        // and Content-Type that were persisted on write, not the (200, text/html)
        // defaults the pre-patch code hardcoded.
        var body = Encoding.UTF8.GetBytes("{\"ok\":true}");
        await _writer.WriteHandleAsync(
            _instanceRoot,
            "https://api.example.com/x",
            status: 202,
            contentType: "application/json",
            body: body,
            unmarkedLength: body.Length,
            truncated: false,
            cancellationToken: CancellationToken.None);

        var hit = await _writer.TryReadHandleAsync(
            _instanceRoot,
            "https://api.example.com/x",
            CancellationToken.None);

        Assert.That(hit, Is.Not.Null);
        using var handle = JsonDocument.Parse(hit!);
        Assert.That(handle.RootElement.GetProperty("status").GetInt32(), Is.EqualTo(202));
        Assert.That(handle.RootElement.GetProperty("content_type").GetString(), Is.EqualTo("application/json"));
    }

    [Test]
    public async Task TryReadHandleAsync_LegacyCacheWithoutSidecar_FallsBackToDefaults()
    {
        // Backwards compat: caches written before patch P4 have no .meta.json.
        // Reader must silently fall back to (200, text/html) rather than erroring.
        var fetchCacheDir = Path.Combine(_instanceRoot, ".fetch-cache");
        Directory.CreateDirectory(fetchCacheDir);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes("https://legacy.example.com"))).ToLowerInvariant();
        await File.WriteAllBytesAsync(
            Path.Combine(fetchCacheDir, $"{hash}.html"),
            Encoding.UTF8.GetBytes("<html>legacy</html>"));

        var hit = await _writer.TryReadHandleAsync(
            _instanceRoot,
            "https://legacy.example.com",
            CancellationToken.None);

        Assert.That(hit, Is.Not.Null);
        using var handle = JsonDocument.Parse(hit!);
        Assert.That(handle.RootElement.GetProperty("status").GetInt32(), Is.EqualTo(200));
        Assert.That(handle.RootElement.GetProperty("content_type").GetString(), Is.EqualTo("text/html"));
    }

    [Test]
    public void TryReadHandleAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Story 10.7a review patch P2 — reader must honor the cancellation token.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _writer.TryReadHandleAsync(
                _instanceRoot,
                "https://example.com",
                cts.Token));
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
