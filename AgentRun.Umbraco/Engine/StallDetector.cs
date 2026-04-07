using Microsoft.Extensions.AI;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Pure detection logic for ToolLoop stalls in interactive mode. Mirrors the
/// <see cref="LlmErrorClassifier"/> pattern from Story 7.1: static, no DI,
/// no I/O, single entry point. The trichotomy is locked in Story 9.0
/// (_bmad-output/implementation-artifacts/9-0-toolloop-stall-recovery.md).
///
/// Separation of detection from recovery is intentional: the caller decides
/// what to do with a stall classification (today: throw <see cref="StallDetectedException"/>;
/// future: pluggable retry-with-nudge strategy). This keeps the V2 door open
/// without re-litigating the engine surface.
/// </summary>
public static class StallDetector
{
    /// <summary>
    /// Classifies an empty-tool-call assistant turn into one of:
    /// <list type="bullet">
    /// <item><see cref="StallClassification.ToolCallsPresent"/> — function calls were produced; tool branch runs.</item>
    /// <item><see cref="StallClassification.NotApplicableNoToolResult"/> — preceding non-assistant message is not a tool result; existing input-wait branch runs.</item>
    /// <item><see cref="StallClassification.WaitingForUserInput"/> — non-empty text ending in `?` after a tool result; existing input-wait branch runs.</item>
    /// <item><see cref="StallClassification.StallEmptyContent"/> — empty/whitespace text after a tool result; caller raises a stall (within <see cref="EngineDefaults.StallDetectionWindowSeconds"/> seconds).</item>
    /// <item><see cref="StallClassification.StallNarration"/> — non-empty text not ending in `?` after a tool result; caller raises a stall.</item>
    /// </list>
    /// Deny-by-default: any empty-turn shape that follows a tool result and is not a recognised question is a stall.
    /// </summary>
    public static StallClassification Classify(
        IList<ChatMessage> messages,
        string accumulatedText,
        IReadOnlyList<FunctionCallContent> functionCalls)
    {
        if (functionCalls.Count > 0)
        {
            return StallClassification.ToolCallsPresent;
        }

        // Walk backwards skipping assistant messages to find the most recent
        // non-assistant role. Streaming chunk boundaries can produce multiple
        // consecutive assistant messages, so a positional check (messages[^2])
        // is not robust.
        ChatRole? mostRecentNonAssistantRole = null;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.Assistant) continue;
            mostRecentNonAssistantRole = messages[i].Role;
            break;
        }

        if (mostRecentNonAssistantRole != ChatRole.Tool)
        {
            return StallClassification.NotApplicableNoToolResult;
        }

        if (string.IsNullOrWhiteSpace(accumulatedText))
        {
            return StallClassification.StallEmptyContent;
        }

        if (accumulatedText.TrimEnd().EndsWith('?'))
        {
            return StallClassification.WaitingForUserInput;
        }

        return StallClassification.StallNarration;
    }
}

public enum StallClassification
{
    ToolCallsPresent,
    WaitingForUserInput,
    NotApplicableNoToolResult,
    StallEmptyContent,
    StallNarration,
}
