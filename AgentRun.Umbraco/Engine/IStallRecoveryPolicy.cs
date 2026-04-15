using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Engine;

public interface IStallRecoveryPolicy
{
    // Called when the LLM returned an empty turn (no function calls this turn).
    // Policy owns: first-turn ProviderEmpty detection, FinishReason telemetry,
    // stall classification via StallDetector, one-shot nudge recovery, and
    // post-nudge terminate-vs-fail decision. The caller (ToolLoop) owns
    // message-list mutation, the `nudgeAttempted` flag, and the user-input-
    // wait fallback path.
    //
    // Throws ProviderEmptyResponseException on first-turn bad-provider config.
    // Throws StallDetectedException on confirmed-stall (post-nudge OR nudge
    // path not available).
    //
    // Returns StallRecoveryAction.NoStall when classification judged the turn
    // is not a stall (e.g. progressing) — caller should fall through to the
    // user-input-wait logic. Returns Terminate when the run should exit with
    // success. Returns Nudge with the NudgeMessage to inject + re-enter the
    // loop; caller is responsible for setting its own `nudgeAttempted` flag
    // so the policy is stateless across invocations.
    Task<StallRecoveryDecision> EvaluateAsync(
        IList<ChatMessage> messages,
        string accumulatedText,
        IReadOnlyList<FunctionCallContent> functionCalls,
        IReadOnlyList<ChatResponseUpdate> updates,
        int assistantTurnCount,
        bool nudgeAttempted,
        bool isInteractive,
        Func<CancellationToken, Task<bool>>? completionCheck,
        ToolExecutionContext context,
        ILogger logger,
        CancellationToken cancellationToken);
}

public enum StallRecoveryAction
{
    // Classification did not identify a stall — caller falls through to
    // user-input-wait / other sequencing logic.
    NoStall,

    // Run should exit with success (completion check passed OR non-interactive
    // mode empty-completion). Caller returns its ChatResponse.
    Terminate,

    // One-shot recovery: caller should inject NudgeMessage as a User message +
    // re-enter the loop. Caller's `nudgeAttempted` flag must flip to true.
    Nudge,
}

public sealed record StallRecoveryDecision(
    StallRecoveryAction Action,
    string? NudgeMessage = null);
