using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class StallRecoveryPolicyTests
{
    private StallRecoveryPolicy _policy = null!;
    private ILogger _logger = null!;
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = new StallRecoveryPolicy();
        _logger = NullLogger.Instance;
        _context = new ToolExecutionContext(
            InstanceFolderPath: "/tmp/inst",
            InstanceId: "inst-001",
            StepId: "scanner",
            WorkflowAlias: "content-quality-audit");
    }

    [Test]
    public async Task EvaluateAsync_NonInteractiveMode_ReturnsTerminate()
    {
        // Non-interactive: empty turn after some progress = exit cleanly.
        var messages = MessagesWithToolResult();
        var result = await _policy.EvaluateAsync(
            messages, accumulatedText: "", functionCalls: Array.Empty<FunctionCallContent>(),
            updates: Array.Empty<ChatResponseUpdate>(),
            assistantTurnCount: 2, nudgeAttempted: false, isInteractive: false,
            completionCheck: null, _context, _logger, CancellationToken.None);

        Assert.That(result.Action, Is.EqualTo(StallRecoveryAction.Terminate));
    }

    [Test]
    public async Task EvaluateAsync_InteractiveStallWithCompletionPassing_ReturnsTerminate()
    {
        var messages = MessagesWithToolResult();
        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(true);

        var result = await _policy.EvaluateAsync(
            messages, accumulatedText: "", functionCalls: Array.Empty<FunctionCallContent>(),
            updates: Array.Empty<ChatResponseUpdate>(),
            assistantTurnCount: 3, nudgeAttempted: false, isInteractive: true,
            completionCheck, _context, _logger, CancellationToken.None);

        Assert.That(result.Action, Is.EqualTo(StallRecoveryAction.Terminate));
    }

    [Test]
    public async Task EvaluateAsync_StallWithCompletionFailing_ReturnsNudgeWithMessage()
    {
        var messages = MessagesWithToolResult();
        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(false);

        var result = await _policy.EvaluateAsync(
            messages, accumulatedText: "", functionCalls: Array.Empty<FunctionCallContent>(),
            updates: Array.Empty<ChatResponseUpdate>(),
            assistantTurnCount: 3, nudgeAttempted: false, isInteractive: true,
            completionCheck, _context, _logger, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(StallRecoveryAction.Nudge));
            Assert.That(result.NudgeMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(result.NudgeMessage, Does.Contain("Continue"));
        });
    }

    [Test]
    public void EvaluateAsync_NudgeAlreadyAttemptedAndCompletionFailing_ThrowsStallDetected()
    {
        var messages = MessagesWithToolResult();
        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(false);

        Assert.ThrowsAsync<StallDetectedException>(async () =>
            await _policy.EvaluateAsync(
                messages, accumulatedText: "", functionCalls: Array.Empty<FunctionCallContent>(),
                updates: Array.Empty<ChatResponseUpdate>(),
                assistantTurnCount: 4, nudgeAttempted: true, isInteractive: true,
                completionCheck, _context, _logger, CancellationToken.None));
    }

    [Test]
    public void EvaluateAsync_FirstTurnEmptyContentNoToolResults_ThrowsProviderEmpty()
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, "You are a scanner.") };

        Assert.ThrowsAsync<ProviderEmptyResponseException>(async () =>
            await _policy.EvaluateAsync(
                messages, accumulatedText: "", functionCalls: Array.Empty<FunctionCallContent>(),
                updates: Array.Empty<ChatResponseUpdate>(),
                assistantTurnCount: 1, nudgeAttempted: false, isInteractive: true,
                completionCheck: null, _context, _logger, CancellationToken.None));
    }

    private static List<ChatMessage> MessagesWithToolResult()
    {
        // Stall classification only fires when the most recent non-assistant
        // message is a tool result (i.e. tool round-trip just completed).
        return new List<ChatMessage>
        {
            new(ChatRole.System, "You are a scanner."),
            new(ChatRole.User, "Audit the homepage."),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "fetch_url", null)]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "ok")]),
        };
    }
}
