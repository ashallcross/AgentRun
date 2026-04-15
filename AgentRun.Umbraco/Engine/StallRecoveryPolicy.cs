using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Engine;

// Story 10.7a Track B: extracted from ToolLoop.cs lines 154-294. Owns the
// "empty-turn → what to do" decision tree:
//   - first-turn bad-provider detection (Story 10.12)
//   - FinishReason telemetry (Story 9.1b carve-out #3)
//   - stall classification via StallDetector (Story 9.0)
//   - one-shot nudge recovery (Story 9.1b Phase 1 carve-out)
//   - post-nudge terminate-vs-fail (Story 9.1b)
//
// Stateless — nudgeAttempted is passed in so the policy itself does not
// carry state across invocations. ToolLoop owns the flag because it
// persists across loop iterations within a single RunAsync call.
public class StallRecoveryPolicy : IStallRecoveryPolicy
{
    private readonly ILogger<StallRecoveryPolicy> _logger;

    public StallRecoveryPolicy(ILogger<StallRecoveryPolicy>? logger = null)
    {
        _logger = logger ?? NullLogger<StallRecoveryPolicy>.Instance;
    }

    public async Task<StallRecoveryDecision> EvaluateAsync(
        IList<ChatMessage> messages,
        string accumulatedText,
        IReadOnlyList<FunctionCallContent> functionCalls,
        IReadOnlyList<ChatResponseUpdate> updates,
        int assistantTurnCount,
        bool nudgeAttempted,
        bool isInteractive,
        Func<CancellationToken, Task<bool>>? completionCheck,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Story 10.12: First-turn empty completion detection.
        // Provider returned 200 but no content on the very first assistant turn
        // (no prior tool results in context). This is a provider configuration
        // error (bad API key, no credit), not a mid-workflow stall. Surface
        // immediately as a provider error instead of entering stall detection.
        if (assistantTurnCount == 1
            && string.IsNullOrWhiteSpace(accumulatedText)
            && !messages.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
        {
            throw new ProviderEmptyResponseException(
                context.StepId, context.InstanceId, context.WorkflowAlias);
        }

        // Permanent structured engine telemetry: capture the API
        // FinishReason (Anthropic stop_reason equivalent) on every
        // empty assistant turn. Established as permanent in Story
        // 9.1b Phase 1 carve-out #3 (2026-04-09) after this exact
        // instrumentation reclassified the CQA scanner stall from
        // a workflow rhythm regression to a MaxOutputTokens ceiling
        // bug (FinishReason=length, accumulatedTextLength=0). This
        // class of failure (silent truncation presenting as "model
        // stalled") is nearly invisible without instrumentation
        // and trivially diagnosable with it — the cost of keeping
        // the log line is one entry per stall (rare by definition),
        // and it protects every future workflow from the same
        // silent-truncation footgun.
        //
        // Event name: engine.empty_turn.finish_reason
        var finishReason = updates.Select(u => u.FinishReason).LastOrDefault(r => r is not null);
        _logger.LogWarning(
            "engine.empty_turn.finish_reason for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}: {FinishReason} (accumulatedTextLength={Len}, updateCount={Updates})",
            context.StepId, context.WorkflowAlias, context.InstanceId,
            finishReason?.Value ?? "<null>", accumulatedText.Length, updates.Count);

        // No tool calls — non-interactive mode exits immediately. Stall
        // detection is interactive-only (Story 9.0 AC #6). Classification
        // happens AFTER this gate so the StallDetector walk is only paid
        // on paths that actually consume the result (review patch P10).
        if (!isInteractive)
        {
            return new StallRecoveryDecision(StallRecoveryAction.Terminate);
        }

        // Story 9.1b Phase 1 carve-out: post-nudge confirmed-stall
        // throw. If we've already injected the synthetic recovery
        // nudge once and we're back in the no-tool-call branch, the
        // model has burned its second chance. The classifier will NOT
        // fire as a stall on this turn (the most recent non-assistant
        // message is now the synthetic user nudge, not a tool result),
        // so we cannot rely on the classification path below — we have
        // to throw directly. The success-path check still wins if the
        // model called the recovery tool on the post-nudge turn AND
        // the completion check now passes (e.g. write_file in turn 2,
        // narration in turn 3) — that path is reached because the
        // tool-call turn does not enter this branch at all.
        if (nudgeAttempted)
        {
            if (completionCheck is not null && await completionCheck(cancellationToken))
            {
                _logger.LogInformation(
                    "Completion check passed on post-nudge turn for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} — treating as successful recovery",
                    context.StepId, context.WorkflowAlias, context.InstanceId);
                return new StallRecoveryDecision(StallRecoveryAction.Terminate);
            }

            var lastToolCallNamePostNudge = ExtractLastToolCallName(messages);
            _logger.LogWarning(
                "Post-nudge turn produced no tool call for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} after tool call {LastToolCall} — synthetic nudge already attempted, failing the run",
                context.StepId, context.WorkflowAlias, context.InstanceId, lastToolCallNamePostNudge);

            throw new StallDetectedException(
                lastToolCallNamePostNudge, context.StepId, context.InstanceId, context.WorkflowAlias);
        }

        // Story 9.0: classify the empty-tool-call turn. Detection is pure;
        // recovery is swapped based on classification. Deferred until here
        // because the post-nudge branch above throws without reading it
        // and the non-interactive branch terminates without reading it
        // (review patch P10).
        var stallClassification = StallDetector.Classify(messages, accumulatedText, functionCalls);

        if (stallClassification == StallClassification.StallEmptyContent
            || stallClassification == StallClassification.StallNarration)
        {
            // Story 9.0 refinement (live test 2026-04-07): if the step's
            // completion criteria are already satisfied, the run actually
            // succeeded — the model just narrated "I'm done" instead of
            // going silent. Failing it would mask a successful run as a
            // bug and force a pointless retry. Run the completion check
            // first; if it passes, exit cleanly with success. Only throw
            // a stall when there is no verified evidence of completion.
            // This preserves "deny by default" — unverified narration
            // still stalls — while letting verified-complete runs finish.
            if (completionCheck is not null && await completionCheck(cancellationToken))
            {
                _logger.LogInformation(
                    "Completion check passed despite empty/narrative final turn for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} — treating as successful completion, not a stall",
                    context.StepId, context.WorkflowAlias, context.InstanceId);
                return new StallRecoveryDecision(StallRecoveryAction.Terminate);
            }

            // Story 9.1b Phase 1 carve-out: single-shot stall recovery
            // via synthetic user nudge. Gated on `completionCheck is not
            // null` so the engine only attempts recovery when the workflow
            // has supplied a verifiable success signal — otherwise we have
            // no way to know if the post-nudge turn produced real progress
            // and the safer behaviour is to throw immediately as before.
            if (completionCheck is not null)
            {
                var nudge = "Continue with the next required action — your previous turn was empty and the task is not yet complete.";
                _logger.LogInformation(
                    "Empty/narrative stall detected with completion check failing for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} — injecting one-shot synthetic user nudge before failing",
                    context.StepId, context.WorkflowAlias, context.InstanceId);
                return new StallRecoveryDecision(StallRecoveryAction.Nudge, nudge);
            }

            var lastToolCallName = ExtractLastToolCallName(messages);
            var stallType = stallClassification == StallClassification.StallEmptyContent
                ? "empty"
                : "narration";

            _logger.LogWarning(
                "ToolLoop stall detected ({StallType}) for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} after tool call {LastToolCall} — no completion check available, failing the run",
                stallType, context.StepId, context.WorkflowAlias, context.InstanceId, lastToolCallName);

            throw new StallDetectedException(
                lastToolCallName, context.StepId, context.InstanceId, context.WorkflowAlias);
        }

        // Not a stall — caller falls through to user-input-wait sequencing.
        return new StallRecoveryDecision(StallRecoveryAction.NoStall);
    }

    internal static string ExtractLastToolCallName(IList<ChatMessage> messages)
    {
        // Walk backwards through assistant messages and find the most recent
        // FunctionCallContent — that is the tool call the LLM saw a result for
        // before stalling. "none" if there is no tool call in the conversation
        // (defensive — stall detection should not fire in that case).
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.Assistant) continue;
            var fnCall = messages[i].Contents.OfType<FunctionCallContent>().LastOrDefault();
            if (fnCall is not null) return fnCall.Name;
        }
        return "none";
    }
}
