using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Services;

namespace AgentRun.Umbraco.Tests.Services;

[TestFixture]
public class ConversationRecorderTests
{
    private IConversationStore _store = null!;
    private ConversationRecorder _recorder = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IConversationStore>();
        _recorder = new ConversationRecorder(
            _store, "test-workflow", "inst-001", "step-1",
            NullLogger<ConversationRecorder>.Instance);
    }

    [Test]
    public async Task RecordAssistantTextAsync_CreatesEntryWithCorrectRole()
    {
        await _recorder.RecordAssistantTextAsync("Hello world", CancellationToken.None);

        await _store.Received(1).AppendAsync(
            "test-workflow", "inst-001", "step-1",
            Arg.Is<ConversationEntry>(e => e.Role == "assistant" && e.Content == "Hello world"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordToolCallAsync_CreatesEntryWithToolFields()
    {
        await _recorder.RecordToolCallAsync("call-1", "read_file", "{\"path\":\"test.txt\"}", CancellationToken.None);

        await _store.Received(1).AppendAsync(
            "test-workflow", "inst-001", "step-1",
            Arg.Is<ConversationEntry>(e =>
                e.Role == "assistant" &&
                e.ToolCallId == "call-1" &&
                e.ToolName == "read_file" &&
                e.ToolArguments == "{\"path\":\"test.txt\"}"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordToolResultAsync_CreatesEntryWithToolRole()
    {
        await _recorder.RecordToolResultAsync("call-1", "file contents", CancellationToken.None);

        await _store.Received(1).AppendAsync(
            "test-workflow", "inst-001", "step-1",
            Arg.Is<ConversationEntry>(e =>
                e.Role == "tool" &&
                e.ToolCallId == "call-1" &&
                e.ToolResult == "file contents"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordSystemMessageAsync_CreatesEntryWithSystemRole()
    {
        await _recorder.RecordSystemMessageAsync("Step started", CancellationToken.None);

        await _store.Received(1).AppendAsync(
            "test-workflow", "inst-001", "step-1",
            Arg.Is<ConversationEntry>(e => e.Role == "system" && e.Content == "Step started"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordUserMessageAsync_CreatesEntryWithUserRole()
    {
        await _recorder.RecordUserMessageAsync("Hello agent", CancellationToken.None);

        await _store.Received(1).AppendAsync(
            "test-workflow", "inst-001", "step-1",
            Arg.Is<ConversationEntry>(e => e.Role == "user" && e.Content == "Hello agent"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordingFailure_IsCaughtAndLogged_DoesNotPropagate()
    {
        _store.AppendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConversationEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));

        // Should not throw
        await _recorder.RecordAssistantTextAsync("test", CancellationToken.None);
        await _recorder.RecordToolCallAsync("c1", "tool", "{}", CancellationToken.None);
        await _recorder.RecordToolResultAsync("c1", "result", CancellationToken.None);
        await _recorder.RecordSystemMessageAsync("msg", CancellationToken.None);
        await _recorder.RecordUserMessageAsync("user msg", CancellationToken.None);

        // Verify all 5 methods were attempted despite failures
        await _store.Received(5).AppendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ConversationEntry>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Story 10.6 Task 4.5 — pin the load-bearing invariant that
    /// <see cref="ConversationRecorder"/> writes ONLY on completion boundaries,
    /// never on partial/mid-stream chunks. This assumption underwrites both:
    /// <list type="bullet">
    /// <item>Story 10.9's Interrupted retry path — which skips truncation on
    /// the grounds that no partial assistant entry can exist on disk.</item>
    /// <item>Story 10.6's wipe-and-restart path on retry-after-Interrupted —
    /// same load-bearing assumption.</item>
    /// </list>
    /// If a future change deliberately starts flushing partial content (e.g.
    /// streaming deltas to JSONL), THIS TEST MUST BE UPDATED DELIBERATELY.
    /// A silent break in this invariant silently regresses both stories
    /// (duplicate completed assistant entries after retry).
    /// </summary>
    [Test]
    public async Task ConversationRecorder_DoesNotWritePartialChunks_IsLoadBearingFor10_9And10_6()
    {
        // Simulate a "mid-stream" situation: only the full recording methods
        // on the recorder's public surface may commit to the store. There is
        // no WritePartial / WriteDelta method. If a new method is ever added
        // that flushes partial content, this assert must be revisited.

        var recorderMethods = typeof(ConversationRecorder)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(ConversationRecorder))
            .Select(m => m.Name)
            .ToHashSet();

        // The only write-direction methods the recorder exposes today. Any
        // new public method whose name implies partial/delta/chunk semantics
        // must be wired to a separate sink or this test updated.
        Assert.That(recorderMethods, Is.EquivalentTo(new[]
        {
            "RecordAssistantTextAsync",
            "RecordToolCallAsync",
            "RecordToolResultAsync",
            "RecordSystemMessageAsync",
            "RecordUserMessageAsync"
        }), "ConversationRecorder's public API has changed — if you added a streaming/delta method, " +
            "revisit Story 10.9's Interrupted retry path and Story 10.6's wipe-and-restart path. " +
            "Both rely on the JSONL only containing completion-boundary entries.");

        // Behavioural sanity: a completed text recording produces exactly one
        // AppendAsync call with the full content — no partial writes beforehand.
        await _recorder.RecordAssistantTextAsync("complete answer", CancellationToken.None);

        await _store.Received(1).AppendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<ConversationEntry>(e =>
                e.Role == "assistant" && e.Content == "complete answer"),
            Arg.Any<CancellationToken>());
    }
}
