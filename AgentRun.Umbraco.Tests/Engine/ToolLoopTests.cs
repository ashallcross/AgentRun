using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class ToolLoopTests
{
    private IChatClient _chatClient = null!;
    private ILogger _logger = null!;
    private ToolExecutionContext _context = null!;
    private TimeSpan _userMessageTimeout = TimeSpan.FromSeconds(2);

    [SetUp]
    public void SetUp()
    {
        _chatClient = Substitute.For<IChatClient>();
        _logger = NullLogger.Instance;
        _context = new ToolExecutionContext("/tmp/instance", "inst-001", "step-1", "test-workflow");
        _userMessageTimeout = TimeSpan.FromSeconds(2);
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> MakeStreamingToolCalls(params (string callId, string name, IDictionary<string, object?>? args)[] calls)
    {
        foreach (var (callId, name, args) in calls)
        {
            var update = new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent(callId, name, args)]
            };
            yield return update;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> MakeEmptyStreamingResponse()
    {
        // Single whitespace-only assistant chunk — accumulatedText becomes whitespace,
        // and an empty assistant message is added to the conversation. No tool calls.
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(" ")]
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> MakeStreamingTextResponse(string text)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };

        await Task.CompletedTask;
    }

    [Test]
    public async Task ToolCallDetected_DispatchesToCorrectTool()
    {
        // AC #5: valid tool calls dispatched to corresponding IWorkflowTool
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("file contents");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = tool
        };

        var args = new Dictionary<string, object?> { ["path"] = "test.txt" };
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "read_file", args));
                return MakeStreamingTextResponse("Done");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };
        var options = new ChatOptions();

        await ToolLoop.RunAsync(_chatClient, messages, options, declaredTools, _context, _logger, CancellationToken.None);

        await tool.Received(1).ExecuteAsync(
            Arg.Is<IDictionary<string, object?>>(a => a.ContainsKey("path")),
            Arg.Is<ToolExecutionContext>(c => c.StepId == "step-1"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UndeclaredToolCall_ReturnsErrorResult_NotThrown()
    {
        // AC #3, #4: undeclared tool → error result to LLM
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "unknown_tool", null));
                return MakeStreamingTextResponse("OK");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var result = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        // Should have added tool result message with error
        var toolMessage = messages.FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.That(toolMessage, Is.Not.Null);
        var resultContent = toolMessage!.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.That(resultContent, Is.Not.Null);
        Assert.That(resultContent!.Result?.ToString(), Does.Contain("not declared for this step"));
    }

    [Test]
    public async Task ToolExecutionError_CaughtAndReturnedAsErrorResult()
    {
        // AC #9: tool errors returned as results, not thrown
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("failing_tool");
        tool.Description.Returns("A tool that fails");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["failing_tool"] = tool
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "failing_tool", null));
                return MakeStreamingTextResponse("handled");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        // Should not throw
        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        var toolMessage = messages.FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.That(toolMessage, Is.Not.Null);
        var resultContent = toolMessage!.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.That(resultContent!.Result?.ToString(), Does.Contain("disk full"));
    }

    [Test]
    public async Task LoopContinues_UntilNoToolCalls()
    {
        // AC #6: loop until no FunctionCallContent
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("step_tool");
        tool.Description.Returns("Iterates");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("result");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["step_tool"] = tool
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence <= 3)
                    return MakeStreamingToolCalls(($"call-{callSequence}", "step_tool", null));
                return MakeStreamingTextResponse("Final answer");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        // 4 calls total: 3 with tool calls + 1 final
        _chatClient.Received(4).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
        await tool.Received(3).ExecuteAsync(
            Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MultipleToolCalls_InSingleResponse_AllDispatched()
    {
        // AC #5: multiple tool calls in one response
        var tool1 = Substitute.For<IWorkflowTool>();
        tool1.Name.Returns("read_file");
        tool1.Description.Returns("Reads");
        tool1.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("content1");

        var tool2 = Substitute.For<IWorkflowTool>();
        tool2.Name.Returns("write_file");
        tool2.Description.Returns("Writes");
        tool2.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("content2");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = tool1,
            ["write_file"] = tool2
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(
                        ("call-1", "read_file", null),
                        ("call-2", "write_file", null));
                return MakeStreamingTextResponse("Done");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        await tool1.Received(1).ExecuteAsync(
            Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>());
        await tool2.Received(1).ExecuteAsync(
            Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void OperationCanceledException_PropagatesFromTool()
    {
        // AC #9 / project-context: OperationCanceledException must propagate
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("cancelling_tool");
        tool.Description.Returns("Cancels");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["cancelling_tool"] = tool
        };

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingToolCalls(("call-1", "cancelling_tool", null)));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None));
    }

    [Test]
    public void ExceedingMaxIterations_ThrowsAgentRunException()
    {
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("looping_tool");
        tool.Description.Returns("Loops");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["looping_tool"] = tool
        };

        // Always return a tool call — triggers infinite loop
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => MakeStreamingToolCalls(($"call-{Guid.NewGuid()}", "looping_tool", null)));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var ex = Assert.ThrowsAsync<AgentRunException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("exceeded maximum"));
    }

    [Test]
    public async Task ToolExecutionException_ReturnedAsStructuredErrorResult()
    {
        // AC #8: ToolExecutionException caught with structured error message format
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ToolExecutionException("file not found: test.txt"));

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = tool
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "read_file", null));
                return MakeStreamingTextResponse("handled");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        var toolMessage = messages.FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.That(toolMessage, Is.Not.Null);
        var resultContent = toolMessage!.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.That(resultContent!.Result?.ToString(), Does.Contain("execution error"));
        Assert.That(resultContent.Result?.ToString(), Does.Contain("file not found: test.txt"));
    }

    [Test]
    public async Task GenericException_StillReturnedAsErrorResult_AfterToolExecutionExceptionCatch()
    {
        // AC #8: generic exceptions still handled by catch-all (backwards compatible)
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("failing_tool");
        tool.Description.Returns("Fails generically");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("unexpected error"));

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["failing_tool"] = tool
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "failing_tool", null));
                return MakeStreamingTextResponse("handled");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        var toolMessage = messages.FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.That(toolMessage, Is.Not.Null);
        var resultContent = toolMessage!.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        // Generic catch uses "failed:" not "execution error:"
        Assert.That(resultContent!.Result?.ToString(), Does.Contain("failed:"));
        Assert.That(resultContent.Result?.ToString(), Does.Contain("unexpected error"));
    }

    [Test]
    public async Task WithRecorder_CallsRecordAssistantTextAsync_AfterLlmTextResponse()
    {
        var recorder = Substitute.For<IConversationRecorder>();
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Hello world"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None, recorder: recorder);

        await recorder.Received(1).RecordAssistantTextAsync("Hello world", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithRecorder_CallsRecordToolCallAndResult_AroundToolExecution()
    {
        var recorder = Substitute.For<IConversationRecorder>();
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("file contents");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = tool
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "read_file", null));
                return MakeStreamingTextResponse("Done");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None, recorder: recorder);

        await recorder.Received(1).RecordToolCallAsync("call-1", "read_file", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await recorder.Received(1).RecordToolResultAsync("call-1", "file contents", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NullRecorder_DoesNotThrow()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Hello"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        // Should not throw — recorder is null by default
        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);
    }

    // --- User message tests (updated from ConcurrentQueue to Channel) ---

    [Test]
    public async Task UserMessage_PreQueued_AddedToConversationAndRecorded()
    {
        var recorder = Substitute.For<IConversationRecorder>();
        var emitter = Substitute.For<ISseEventEmitter>();
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("Hello agent");

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Hi there, how can I help?"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader, emitter: emitter, recorder: recorder,
            userMessageTimeoutOverride: TimeSpan.FromMilliseconds(100));

        // User message should be in conversation messages (drained at start of iteration, before LLM call)
        var userMsg = messages.FirstOrDefault(m => m.Role == ChatRole.User);
        Assert.That(userMsg, Is.Not.Null);
        Assert.That(userMsg!.Text, Is.EqualTo("Hello agent"));

        // Should have been recorded and emitted
        await recorder.Received(1).RecordUserMessageAsync("Hello agent", Arg.Any<CancellationToken>());
        await emitter.Received(1).EmitUserMessageAsync("Hello agent", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UserMessage_DuringToolExecution_DrainedAfterToolResults()
    {
        var recorder = Substitute.For<IConversationRecorder>();
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("file contents");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    // Simulate user sending message during tool execution
                    channel.Writer.TryWrite("User message during tools");
                    return MakeStreamingToolCalls(("call-1", "read_file", null));
                }
                return MakeStreamingTextResponse("Done");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader, recorder: recorder,
            userMessageTimeoutOverride: _userMessageTimeout);

        // User message should appear in messages after tool result
        var userMsg = messages.FirstOrDefault(m => m.Role == ChatRole.User);
        Assert.That(userMsg, Is.Not.Null);
        Assert.That(userMsg!.Text, Is.EqualTo("User message during tools"));

        await recorder.Received(1).RecordUserMessageAsync("User message during tools", Arg.Any<CancellationToken>());
    }

    // --- Wait-for-user-message tests ---

    [Test]
    public async Task NullReader_NoToolCalls_ExitsImmediately()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Hello"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        // Should return immediately without waiting — only 1 LLM call
        _chatClient.Received(1).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task Reader_WaitsThenDrains_WhenMessageArrivesAfterDelay()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    // First call: LLM responds with text (no tools) — will trigger wait
                    // Write message after a short delay to simulate user typing
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        channel.Writer.TryWrite("Delayed user response");
                    });
                    return MakeStreamingTextResponse("What would you like to do?");
                }
                // Second call after user message drained
                return MakeStreamingTextResponse("Great choice!");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            userMessageTimeoutOverride: _userMessageTimeout);

        // LLM called twice: once initial, once after user message
        _chatClient.Received(2).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());

        var userMsg = messages.FirstOrDefault(m => m.Role == ChatRole.User);
        Assert.That(userMsg, Is.Not.Null);
        Assert.That(userMsg!.Text, Is.EqualTo("Delayed user response"));
    }

    [Test]
    public async Task Reader_TimeoutExpires_ReturnsNormally()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Any questions?"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        // No message written to channel — will timeout
        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            userMessageTimeoutOverride: TimeSpan.FromMilliseconds(100));

        // Should return normally (not throw)
        Assert.That(response, Is.Not.Null);

        // Only 1 LLM call — timed out waiting, then exited
        _chatClient.Received(1).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Reader_CancellationDuringWait_ThrowsOCE()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Waiting..."));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, cts.Token,
                userMessageReader: channel.Reader,
                userMessageTimeoutOverride: _userMessageTimeout));
    }

    [Test]
    public async Task Reader_MultipleMessagesDrained_AllAddedBeforeNextLlmCall()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    // Queue 3 messages before the wait
                    channel.Writer.TryWrite("Message 1");
                    channel.Writer.TryWrite("Message 2");
                    channel.Writer.TryWrite("Message 3");
                    return MakeStreamingTextResponse("What do you think?");
                }
                return MakeStreamingTextResponse("Thanks for all that!");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            userMessageTimeoutOverride: _userMessageTimeout);

        var userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
        Assert.That(userMessages, Has.Count.EqualTo(3));
        Assert.That(userMessages[0].Text, Is.EqualTo("Message 1"));
        Assert.That(userMessages[1].Text, Is.EqualTo("Message 2"));
        Assert.That(userMessages[2].Text, Is.EqualTo("Message 3"));
    }

    [Test]
    public async Task Reader_RealResolver_WorkflowOverride_AppliedToWaitTimeout()
    {
        // P9 / AC #7: prove the resolver path is wired by passing a real
        // ToolLimitResolver and a workflow declaring a custom timeout. The wait
        // should time out at the resolved value and exit normally — not throw.
        var realResolver = new ToolLimitResolver(Options.Create(new AgentRunOptions()));
        var step = new StepDefinition { Id = "step-1", Name = "Step", Agent = "a.md" };
        var workflow = new WorkflowDefinition
        {
            Name = "T", Alias = "test-wf",
            ToolDefaults = new() { ToolLoop = new() { UserMessageTimeoutSeconds = 1 } },
            Steps = { step }
        };
        var context = new ToolExecutionContext("/tmp", "inst", "step-1", "test-wf")
        {
            Step = step,
            Workflow = workflow
        };

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Any questions?"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var startedAt = DateTime.UtcNow;
        var response = await ToolLoop.RunAsync(
            _chatClient, messages, new ChatOptions(), declaredTools, context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            toolLimitResolver: realResolver);
        var elapsed = DateTime.UtcNow - startedAt;

        Assert.That(response, Is.Not.Null);
        // Workflow declared 1s — must have timed out reasonably close to that, not the 300s engine default.
        Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void Reader_NoOverride_NoResolver_ThrowsInvalidOperationException()
    {
        // D2: in interactive mode, missing override AND missing resolver/context is a wiring bug.
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeStreamingTextResponse("Hello"));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ToolLoop.RunAsync(
                _chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
                userMessageReader: channel.Reader));
    }

    [Test]
    public async Task Reader_WaitCountsTowardMaxIterations()
    {
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                // Always respond with text (no tools) — each triggers a wait-drain-continue cycle
                // Pre-queue a message so the wait returns immediately
                channel.Writer.TryWrite($"msg-{callSequence}");
                return MakeStreamingTextResponse($"Response {callSequence}");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        // MaxIterations is 100 — this will hit it
        var ex = Assert.ThrowsAsync<AgentRunException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
                userMessageReader: channel.Reader,
                userMessageTimeoutOverride: _userMessageTimeout));
        Assert.That(ex!.Message, Does.Contain("exceeded maximum"));
    }

    // --- Story 9.0 stall detection wiring tests ---

    [Test]
    public void Stall_Interactive_EmptyAssistantTurnAfterToolResult_ThrowsStallDetectedException()
    {
        // AC #1: empty turn after a tool result in interactive mode → fail fast.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("fetch_url");
        tool.Description.Returns("Fetches a URL");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("<html>...</html>");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["fetch_url"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "fetch_url", null));
                return MakeEmptyStreamingResponse();
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var ex = Assert.ThrowsAsync<StallDetectedException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
                userMessageReader: channel.Reader,
                userMessageTimeoutOverride: _userMessageTimeout));

        Assert.That(ex!.Message, Is.EqualTo("The agent stopped responding mid-task. Click retry to try again."));
        Assert.That(ex.LastToolCall, Is.EqualTo("fetch_url"));
        Assert.That(ex.StepId, Is.EqualTo("step-1"));
        Assert.That(ex.InstanceId, Is.EqualTo("inst-001"));
        Assert.That(ex.WorkflowAlias, Is.EqualTo("test-workflow"));
    }

    [Test]
    public void Stall_Interactive_NarrativeTextAfterToolResult_ThrowsStallDetectedException()
    {
        // AC #3: narration without tool call after a tool result is a stall.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("fetch_url");
        tool.Description.Returns("Fetches a URL");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("<html>...</html>");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["fetch_url"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "fetch_url", null));
                return MakeStreamingTextResponse("Let me process that and write the results.");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var ex = Assert.ThrowsAsync<StallDetectedException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
                userMessageReader: channel.Reader,
                userMessageTimeoutOverride: _userMessageTimeout));

        Assert.That(ex!.LastToolCall, Is.EqualTo("fetch_url"));
    }

    [Test]
    public async Task Stall_Interactive_QuestionAfterToolResult_DoesNotThrow_WaitsForUser()
    {
        // AC #2: text ending in `?` after a tool result is a wait, not a stall.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("fetch_url");
        tool.Description.Returns("Fetches a URL");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("<html>...</html>");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["fetch_url"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "fetch_url", null));
                return MakeStreamingTextResponse("I retrieved the page. Anything else you want me to fetch?");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        // Times out waiting on the channel — exits normally, not as a stall exception.
        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            userMessageTimeoutOverride: TimeSpan.FromMilliseconds(100));

        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task Stall_Autonomous_EmptyTurnAfterToolResult_DoesNotThrow_ExitsNormally()
    {
        // AC #6: stall detection is interactive-only. Autonomous mode must NOT throw on
        // an empty turn following a tool result — the existing exit branch handles it.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("fetch_url");
        tool.Description.Returns("Fetches a URL");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("<html>...</html>");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["fetch_url"] = tool
        };

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "fetch_url", null));
                return MakeEmptyStreamingResponse();
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        // No userMessageReader → autonomous mode. Must exit normally.
        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task Stall_Interactive_NoPrecedingToolResult_DoesNotThrow_RunsInputWait()
    {
        // AC #4: empty turn at step start (no tool result yet) → not a stall.
        // Existing input-wait path runs.
        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<string>();

        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeEmptyStreamingResponse());

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            userMessageTimeoutOverride: TimeSpan.FromMilliseconds(100));

        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task Stall_Interactive_NarrationButCompletionCheckPasses_ReturnsCleanly_NoStall()
    {
        // Story 9.0 live-test refinement (2026-04-07): when the model narrates after a
        // tool result but the step's completion criteria are already satisfied, the run
        // actually succeeded. Treat as success, NOT a stall — running the completion
        // check first short-circuits the stall throw. Discovered when the scanner
        // workflow successfully wrote artifacts/scan-results.md, then the model said
        // "Done!" and Story 9.0 was incorrectly failing the instance.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("write_file");
        tool.Description.Returns("Writes a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["write_file"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "write_file", null));
                // Narration after a successful tool round-trip — would have been a
                // StallNarration without the completion-check short-circuit.
                return MakeStreamingTextResponse("All done! I have written the results.");
            });

        // Completion check passes — file exists, run is logically complete.
        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(true);

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(
            _chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            completionCheck: completionCheck,
            userMessageTimeoutOverride: _userMessageTimeout);

        Assert.That(response, Is.Not.Null);
        // Sanity: only two LLM calls — tool round-trip + the narrative final turn.
        // No retry, no extra calls. The narration final turn is recorded normally.
        _chatClient.Received(2).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Stall_Interactive_NarrationAndCompletionCheckFails_NudgesOnceThenThrows()
    {
        // Story 9.1b Phase 1 carve-out: when narration stall hits AND completion
        // check fails, the engine now injects a one-shot synthetic user nudge
        // before throwing. This test asserts: (a) the nudge fires (3 LLM calls
        // observed), (b) if the nudge ALSO stalls, StallDetectedException still
        // throws as before. Guards against the recovery being either silently
        // disabled or going into an infinite retry loop.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("write_file");
        tool.Description.Returns("Writes a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["write_file"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeStreamingToolCalls(("call-1", "write_file", null));
                // Both the original stall and the post-nudge retry produce
                // narration with no tool call — model is genuinely stuck.
                return MakeStreamingTextResponse("Let me think about this for a moment.");
            });

        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(false);

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        Assert.ThrowsAsync<StallDetectedException>(
            async () => await ToolLoop.RunAsync(
                _chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
                userMessageReader: channel.Reader,
                completionCheck: completionCheck,
                userMessageTimeoutOverride: _userMessageTimeout));

        // 3 LLM calls: tool round-trip (1) + first stalling turn (2) + post-nudge retry that ALSO stalls (3).
        _chatClient.Received(3).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Stall_Interactive_EmptyTurnAndCompletionCheckFails_NudgeRecoversWithToolCall()
    {
        // Story 9.1b Phase 1 carve-out: the success case for the nudge-and-retry
        // path. Sonnet 4.6 stalls after the final fetch_url in a multi-URL batch,
        // then recovers when given a directive synthetic user message and emits
        // the missing write_file call. This test asserts: (a) no exception is
        // thrown, (b) the post-nudge tool call is executed, (c) exactly 3 LLM
        // calls are observed (initial tool round-trip + stall + post-nudge
        // tool call). Diagnosed via the CQA 5-URL batch manual E2E gate
        // (instance e7e7dfc50bec4d4a8f7db76a5cd6328a).
        var fileWritten = false;
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("write_file");
        tool.Description.Returns("Writes a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fileWritten = true;
                return Task.FromResult<object>("ok");
            });

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["write_file"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        // Completion check toggles once write_file has been called — mirrors the
        // real workflow scenario where the file's existence is the success signal.
        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(fileWritten);

        // The scenario needs the loop to enter the stall classification path,
        // which requires a preceding tool result in the conversation. We seed
        // that with a fetch_url-style tool result already in the messages list
        // below, so the first LLM call IS the empty stall turn (not a tool
        // round-trip).
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                return callSequence switch
                {
                    // Turn 1: model emits an empty turn (no text, no tool call) —
                    // simulates the post-batch stall.
                    1 => MakeEmptyStreamingResponse(),
                    // Turn 2: after the synthetic nudge, model calls write_file.
                    2 => MakeStreamingToolCalls(("call-1", "write_file", null)),
                    // Turn 3: model narrates "done" — completion check now passes
                    // (file was written) so this short-circuits to success.
                    _ => MakeStreamingTextResponse("Done.")
                };
            });

        // Seed a prior tool result so the empty turn is correctly classified as
        // a post-tool-call stall (not a fresh-start input wait).
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "test"),
            new(ChatRole.Assistant, [new FunctionCallContent("seed-call", "fetch_url", null)]),
            new(ChatRole.Tool, [new FunctionResultContent("seed-call", "{\"status\":200}")])
        };

        var response = await ToolLoop.RunAsync(
            _chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            completionCheck: completionCheck,
            userMessageTimeoutOverride: _userMessageTimeout);

        Assert.That(response, Is.Not.Null);
        Assert.That(fileWritten, Is.True, "post-nudge write_file must have been executed");
        // 3 LLM calls: empty stall (1) + post-nudge write_file tool call (2) + final narration (3).
        _chatClient.Received(3).GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Stall_Interactive_MultiTurn_UserRepliedThenEmptyTurn_DoesNotThrow()
    {
        // Failure & Edge Cases: after a user reply (most recent non-assistant is User,
        // not Tool), an empty turn must NOT trigger stall detection. AC #4 path.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("fetch_url");
        tool.Description.Returns("Fetches a URL");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("<html>...</html>");

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["fetch_url"] = tool
        };

        var channel = Channel.CreateUnbounded<string>();
        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return MakeStreamingToolCalls(("call-1", "fetch_url", null));
                }
                if (callSequence == 2)
                {
                    // LLM asks a question after the tool result — input-wait branch runs.
                    // Pre-queue the user reply so the wait drains and the loop continues.
                    channel.Writer.TryWrite("yes please");
                    return MakeStreamingTextResponse("Want me to fetch the sitemap too?");
                }
                // After the user reply the LLM produces an empty turn. The most recent
                // non-assistant message is now ChatRole.User → NotApplicable, NOT a stall.
                return MakeEmptyStreamingResponse();
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None,
            userMessageReader: channel.Reader,
            userMessageTimeoutOverride: TimeSpan.FromMilliseconds(100));

        Assert.That(response, Is.Not.Null);
    }
}
