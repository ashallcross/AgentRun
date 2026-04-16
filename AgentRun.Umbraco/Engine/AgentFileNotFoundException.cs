namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Thrown when prompt assembly cannot locate the agent markdown file for a step.
/// Message is sanitised to a workflow-relative path so the absolute filesystem
/// location does not leak via the run.error SSE payload to the chat panel.
/// Full canonical path is logged at Error inside PromptAssembler.AssemblePromptAsync
/// before this exception is thrown — the exception itself never carries it.
/// </summary>
public sealed class AgentFileNotFoundException : AgentRunException
{
    public AgentFileNotFoundException(string agentRelativePath)
        : base($"Agent file not found: '{agentRelativePath}'")
    {
    }
}
