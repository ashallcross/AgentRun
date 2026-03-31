using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shallai.UmbracoAgentRunner.Engine.Events;

namespace Shallai.UmbracoAgentRunner.Tests.Engine.Events;

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
}
