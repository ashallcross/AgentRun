using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Engine.Events;

namespace AgentRun.Umbraco.Tests.Engine.Events;

[TestFixture]
public class SseEventEmitterTests
{
    [Test]
    public async Task EmitRunStartedAsync_WritesCorrectSseFormat()
    {
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);

        await emitter.EmitRunStartedAsync("abc123", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(output, Does.StartWith("event: run.started\n"));
        Assert.That(output, Does.Contain("data: "));
        Assert.That(output, Does.Contain("\"instanceId\":\"abc123\""));
        Assert.That(output, Does.EndWith("\n\n"));
    }

    [Test]
    public async Task EmitTextDeltaAsync_WritesCorrectPayload()
    {
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);

        await emitter.EmitTextDeltaAsync("Hello world", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(output, Does.Contain("event: text.delta\n"));
        Assert.That(output, Does.Contain("\"content\":\"Hello world\""));
    }

    [Test]
    public async Task EmitToolStartAsync_WritesToolCallIdAndName()
    {
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);

        await emitter.EmitToolStartAsync("tc_001", "read_file", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(output, Does.Contain("event: tool.start\n"));
        Assert.That(output, Does.Contain("\"toolCallId\":\"tc_001\""));
        Assert.That(output, Does.Contain("\"toolName\":\"read_file\""));
    }

    [Test]
    public async Task EmitStepFinishedAsync_WritesStepIdAndStatus()
    {
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);

        await emitter.EmitStepFinishedAsync("scanner", "Complete", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(output, Does.Contain("event: step.finished\n"));
        Assert.That(output, Does.Contain("\"stepId\":\"scanner\""));
        Assert.That(output, Does.Contain("\"status\":\"Complete\""));
    }

    [Test]
    public async Task EmitRunErrorAsync_WritesErrorAndMessage()
    {
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);

        await emitter.EmitRunErrorAsync("step_failed", "Missing scan-results.md", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(output, Does.Contain("event: run.error\n"));
        Assert.That(output, Does.Contain("\"error\":\"step_failed\""));
        Assert.That(output, Does.Contain("\"message\":\"Missing scan-results.md\""));
    }

    [Test]
    public async Task MultipleEmits_ProducesSeparateEvents()
    {
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);

        await emitter.EmitRunStartedAsync("id1", CancellationToken.None);
        await emitter.EmitStepStartedAsync("s1", "Step One", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        var events = output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(events, Has.Length.EqualTo(2));
    }

    // --- Story 10.11 Track A: SSE keepalive tests ---

    [Test]
    public async Task StartKeepaliveAsync_AfterMultipleIntervals_WritesKeepaliveCommentEachTime()
    {
        // AC1: emits `: keepalive\n\n` at the configured interval
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var cts = new CancellationTokenSource();

        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromMilliseconds(50), cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        await heartbeat;

        var output = Encoding.UTF8.GetString(stream.ToArray());
        var keepaliveCount = output.Split(": keepalive\n\n", StringSplitOptions.None).Length - 1;
        Assert.That(keepaliveCount, Is.GreaterThanOrEqualTo(3),
            $"Expected ≥3 keepalive lines in 250ms at 50ms interval, got {keepaliveCount}. Output: {output}");
    }

    [Test]
    public async Task StartKeepaliveAsync_ConcurrentWithEmitTextDelta_DoesNotInterleaveWrites()
    {
        // AC2: heartbeat + event writes serialise via the semaphore
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var cts = new CancellationTokenSource();

        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromMilliseconds(10), cts.Token);

        // Race heartbeat against many EmitTextDelta calls.
        var emitTasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            emitTasks.Add(emitter.EmitTextDeltaAsync("hello", CancellationToken.None));
        }
        await Task.WhenAll(emitTasks);
        await Task.Delay(50);
        cts.Cancel();
        await heartbeat;

        var output = Encoding.UTF8.GetString(stream.ToArray());

        // Split on the SSE frame terminator `\n\n`. Every frame must be either a
        // complete event (starts with `event: `) or a complete keepalive comment
        // (`: keepalive`). No partial/interleaved fragments.
        var frames = output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var frame in frames)
        {
            var isKeepalive = frame == ": keepalive";
            var isEvent = frame.StartsWith("event: text.delta\ndata: ", StringComparison.Ordinal)
                && frame.Contains("\"content\":\"hello\"");
            Assert.That(isKeepalive || isEvent, Is.True,
                $"Frame is neither a clean keepalive nor a clean text.delta event: [{frame}]");
        }
    }

    [Test]
    public async Task StartKeepaliveAsync_TokenCancelled_ExitsWithinOneInterval()
    {
        // AC3: cancellation exits the loop cleanly
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var cts = new CancellationTokenSource();

        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromSeconds(1), cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var completed = await Task.WhenAny(heartbeat, Task.Delay(TimeSpan.FromMilliseconds(1500)));
        Assert.That(completed, Is.SameAs(heartbeat), "Heartbeat did not terminate within 1.5s after cancel");
        Assert.That(heartbeat.IsFaulted, Is.False, "Heartbeat task should complete without faulting");
    }

    [Test]
    public async Task StartKeepaliveAsync_StreamThrowsIOException_ExitsCleanly()
    {
        // AC4: IOException is caught and the loop exits cleanly
        var stream = Substitute.For<Stream>();
        stream.CanWrite.Returns(true);
        stream.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new IOException("disconnected")));
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var cts = new CancellationTokenSource();

        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromMilliseconds(20), cts.Token);
        var completed = await Task.WhenAny(heartbeat, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.That(completed, Is.SameAs(heartbeat), "Heartbeat should exit on IOException without hanging");
        Assert.That(heartbeat.IsFaulted, Is.False, "IOException must be swallowed, not propagated");
    }

    [Test]
    public async Task StartKeepaliveAsync_StreamThrowsObjectDisposedException_ExitsCleanly()
    {
        // AC4: ObjectDisposedException is caught and the loop exits cleanly
        var stream = Substitute.For<Stream>();
        stream.CanWrite.Returns(true);
        stream.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new ObjectDisposedException("Stream")));
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var cts = new CancellationTokenSource();

        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromMilliseconds(20), cts.Token);
        var completed = await Task.WhenAny(heartbeat, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.That(completed, Is.SameAs(heartbeat));
        Assert.That(heartbeat.IsFaulted, Is.False);
    }

    [Test]
    public async Task StartKeepaliveAsync_LinkedCtsCancelThenDispose_ExitsCleanly()
    {
        // Regression: ExecutionEndpoints.ExecuteSseAsync uses a linked CTS that
        // is Cancel()'d and then Dispose()'d in finally. CancellationTokenSource
        // .Dispose() does NOT cancel the token — without the explicit Cancel()
        // the heartbeat would only exit when the stream next fails to write.
        // This test mirrors the production call pattern and asserts clean exit.
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var requestCts = new CancellationTokenSource();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCts.Token);
        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromMilliseconds(50), linkedCts.Token);
        await Task.Delay(100);

        linkedCts.Cancel();
        linkedCts.Dispose();

        var completed = await Task.WhenAny(heartbeat, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.That(completed, Is.SameAs(heartbeat),
            "Heartbeat must exit deterministically when the linked CTS is cancelled in finally");
        Assert.That(heartbeat.IsFaulted, Is.False,
            "Cancel-then-Dispose on a linked CTS must not fault the heartbeat task");
    }

    [Test]
    public async Task EmitAsync_AfterCancelledKeepalive_StillWritesEvent()
    {
        // AC3: semaphore is released by the cancelled heartbeat (no deadlock)
        using var stream = new MemoryStream();
        var emitter = new SseEventEmitter(stream, NullLogger<SseEventEmitter>.Instance);
        using var cts = new CancellationTokenSource();

        var heartbeat = emitter.StartKeepaliveAsync(TimeSpan.FromMilliseconds(50), cts.Token);
        await Task.Delay(60);
        cts.Cancel();
        await heartbeat;

        // If the cancelled heartbeat had leaked the lock, this would deadlock forever.
        await emitter.EmitTextDeltaAsync("after-cancel", CancellationToken.None);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.That(output, Does.Contain("\"content\":\"after-cancel\""));
    }
}
