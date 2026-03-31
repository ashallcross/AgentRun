namespace Shallai.UmbracoAgentRunner.Engine.Events;

public static class SseEventTypes
{
    public const string RunStarted = "run.started";
    public const string TextDelta = "text.delta";
    public const string ToolStart = "tool.start";
    public const string ToolArgs = "tool.args";
    public const string ToolEnd = "tool.end";
    public const string ToolResult = "tool.result";
    public const string StepStarted = "step.started";
    public const string StepFinished = "step.finished";
    public const string RunFinished = "run.finished";
    public const string RunError = "run.error";
    public const string SystemMessage = "system.message";
}

public sealed record RunStartedPayload(string InstanceId);
public sealed record TextDeltaPayload(string Content);
public sealed record ToolStartPayload(string ToolCallId, string ToolName);
public sealed record ToolArgsPayload(string ToolCallId, object Arguments);
public sealed record ToolEndPayload(string ToolCallId);
public sealed record ToolResultPayload(string ToolCallId, object Result);
public sealed record StepStartedPayload(string StepId, string StepName);
public sealed record StepFinishedPayload(string StepId, string Status);
public sealed record RunFinishedPayload(string InstanceId, string Status);
public sealed record RunErrorPayload(string Error, string Message);
public sealed record SystemMessagePayload(string Message);
