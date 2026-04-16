namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Raised by ToolLoop when an interactive-mode step produces an empty or
/// narrative assistant turn following a tool result. Story 9.0 (BETA BLOCKER).
///
/// Inherits <see cref="AgentRunException"/> so it flows through the existing
/// Story 7.1 endpoint exception filter and surfaces on the frontend as a
/// <c>run.error</c> SSE event with this exception's <see cref="Exception.Message"/>
/// rendered into the chat panel. The Story 7.2 retry button (which keys off
/// step Failed status) then re-enters the loop unchanged.
/// </summary>
public sealed class StallDetectedException : AgentRunException
{
    public string LastToolCall { get; }
    public string StepId { get; }
    public string InstanceId { get; }
    public string WorkflowAlias { get; }

    public StallDetectedException(string lastToolCall, string stepId, string instanceId, string workflowAlias)
        : base("The agent stopped responding mid-task. Click retry to try again.")
    {
        LastToolCall = lastToolCall;
        StepId = stepId;
        InstanceId = instanceId;
        WorkflowAlias = workflowAlias;
    }
}
