using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

[TestFixture]
public class ToolLoopTests
{
    private IChatClient _chatClient = null!;
    private ILogger _logger = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _chatClient = Substitute.For<IChatClient>();
        _logger = NullLogger.Instance;
        _context = new ToolExecutionContext("/tmp/instance", "inst-001", "step-1", "test-workflow");
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
    }

    private static ChatResponse MakeResponseWithToolCalls(params (string callId, string name, IDictionary<string, object?>? args)[] calls)
    {
        var contents = new List<AIContent>();
        foreach (var (callId, name, args) in calls)
        {
            contents.Add(new FunctionCallContent(callId, name, args));
        }

        var message = new ChatMessage(ChatRole.Assistant, contents);
        return new ChatResponse(message);
    }

    private static ChatResponse MakeTextResponse(string text)
    {
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeResponseWithToolCalls(("call-1", "read_file", args));
                return MakeTextResponse("Done");
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeResponseWithToolCalls(("call-1", "unknown_tool", null));
                return MakeTextResponse("OK");
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeResponseWithToolCalls(("call-1", "failing_tool", null));
                return MakeTextResponse("handled");
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence <= 3)
                    return MakeResponseWithToolCalls(($"call-{callSequence}", "step_tool", null));
                return MakeTextResponse("Final answer");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var response = await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None);

        // 4 calls total: 3 with tool calls + 1 final
        await _chatClient.Received(4).GetResponseAsync(
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeResponseWithToolCalls(
                        ("call-1", "read_file", null),
                        ("call-2", "write_file", null));
                return MakeTextResponse("Done");
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

        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(MakeResponseWithToolCalls(("call-1", "cancelling_tool", null)));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await ToolLoop.RunAsync(_chatClient, messages, new ChatOptions(), declaredTools, _context, _logger, CancellationToken.None));
    }

    [Test]
    public void ExceedingMaxIterations_ThrowsAgentRunnerException()
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => MakeResponseWithToolCalls(($"call-{Guid.NewGuid()}", "looping_tool", null)));

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        var ex = Assert.ThrowsAsync<AgentRunnerException>(
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeResponseWithToolCalls(("call-1", "read_file", null));
                return MakeTextResponse("handled");
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
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                    return MakeResponseWithToolCalls(("call-1", "failing_tool", null));
                return MakeTextResponse("handled");
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
}
