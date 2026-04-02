using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ToolExtensibilityTests
{
    /// <summary>
    /// A concrete IWorkflowTool created entirely in-test — proves no engine changes needed (NFR25).
    /// </summary>
    private sealed class InTestCustomTool : IWorkflowTool
    {
        public string Name => "custom_tool";
        public string Description => "A custom tool created in-test";

        public Task<object> ExecuteAsync(
            IDictionary<string, object?> arguments,
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            var greeting = arguments.TryGetValue("name", out var name) ? $"Hello, {name}!" : "Hello!";
            return Task.FromResult<object>(greeting);
        }
    }

    private IChatClient _chatClient = null!;

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamToolCalls(params (string callId, string name, IDictionary<string, object?>? args)[] calls)
    {
        foreach (var (callId, name, args) in calls)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent(callId, name, args)]
            };
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamText(string text)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
        await Task.CompletedTask;
    }

    [SetUp]
    public void SetUp()
    {
        _chatClient = Substitute.For<IChatClient>();
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient.Dispose();
    }

    [Test]
    public async Task CustomToolImplementation_DispatchedByToolLoop_WithoutEngineChanges()
    {
        // AC #9: new tool requires only IWorkflowTool + DI registration, no engine modification
        var customTool = new InTestCustomTool();

        var declaredTools = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["custom_tool"] = customTool
        };

        var context = new ToolExecutionContext("/tmp/instance", "inst-001", "step-1", "test-workflow");

        var callSequence = 0;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return StreamToolCalls(("call-1", "custom_tool", new Dictionary<string, object?> { ["name"] = "Adam" }));
                }
                return StreamText("Done");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "test") };

        await ToolLoop.RunAsync(
            _chatClient, messages, new ChatOptions(), declaredTools, context,
            NullLogger.Instance, CancellationToken.None);

        // Verify the tool result was added to messages
        var toolMessage = messages.FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.That(toolMessage, Is.Not.Null);
        var resultContent = toolMessage!.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.That(resultContent, Is.Not.Null);
        Assert.That(resultContent!.Result?.ToString(), Is.EqualTo("Hello, Adam!"));
    }
}
