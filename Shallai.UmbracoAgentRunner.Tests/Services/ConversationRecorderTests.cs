using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Services;

namespace Shallai.UmbracoAgentRunner.Tests.Services;

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
}
