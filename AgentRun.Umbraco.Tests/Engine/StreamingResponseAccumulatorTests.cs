using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Engine.Events;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class StreamingResponseAccumulatorTests
{
    private StreamingResponseAccumulator _accumulator = null!;

    [SetUp]
    public void SetUp()
    {
        _accumulator = new StreamingResponseAccumulator();
    }

    [Test]
    public async Task AccumulateAsync_TextDeltas_ConcatenatesAndEmitsAndRecordsOnce()
    {
        var emitter = Substitute.For<ISseEventEmitter>();
        var recorder = Substitute.For<IConversationRecorder>();
        var stream = AsyncEnumerableOf(
            Update("Hello "),
            Update("world"),
            Update("."));

        var result = await _accumulator.AccumulateAsync(stream, emitter, recorder, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Text, Is.EqualTo("Hello world."));
            Assert.That(result.Updates, Has.Count.EqualTo(3));
            await emitter.Received(3).EmitTextDeltaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await recorder.Received(1).RecordAssistantTextAsync("Hello world.", Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AccumulateAsync_EmptyStream_ReturnsEmptyTextAndDoesNotRecord()
    {
        var emitter = Substitute.For<ISseEventEmitter>();
        var recorder = Substitute.For<IConversationRecorder>();
        var stream = AsyncEnumerableOf(); // no updates

        var result = await _accumulator.AccumulateAsync(stream, emitter, recorder, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Text, Is.Empty);
            Assert.That(result.Updates, Is.Empty);
            await emitter.DidNotReceive().EmitTextDeltaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await recorder.DidNotReceive().RecordAssistantTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AccumulateAsync_EmitterThrowsMidStream_RecordsPartialTextAndRethrows()
    {
        var emitter = Substitute.For<ISseEventEmitter>();
        emitter.EmitTextDeltaAsync(Arg.Is<string>(s => s == "second"), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("emitter exploded"));

        var recorder = Substitute.For<IConversationRecorder>();
        var stream = AsyncEnumerableOf(
            Update("first "),
            Update("second"),
            Update(" third"));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _accumulator.AccumulateAsync(stream, emitter, recorder, CancellationToken.None));

        Assert.That(ex!.Message, Is.EqualTo("emitter exploded"));
        // Accumulator appends to the text builder BEFORE emitting, so the
        // in-flight chunk that failed to emit is persisted as part of the
        // partial text. Locks in the "no data loss at the recorder" contract.
        await recorder.Received(1).RecordAssistantTextAsync(
            "first second",
            Arg.Any<CancellationToken>());
    }

    private static ChatResponseUpdate Update(string text) => new(ChatRole.Assistant, text);

    private static async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerableOf(params ChatResponseUpdate[] updates)
    {
        foreach (var u in updates)
        {
            await Task.Yield();
            yield return u;
        }
    }
}
