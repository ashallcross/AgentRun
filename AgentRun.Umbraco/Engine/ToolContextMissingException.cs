namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Thrown when a tool's <see cref="ToolExecutionContext"/> is missing fields the
/// tool requires from its caller (e.g. <c>Step</c> / <c>Workflow</c> needed by the
/// limit resolver). This is an <b>engine wiring bug</b> in the executor — not a
/// workflow-author error and not an LLM/provider error. Derives from
/// <see cref="AgentRunException"/> so the <c>LlmErrorClassifier</c> does not
/// silently rewrite it as a generic provider failure.
/// </summary>
public sealed class ToolContextMissingException : AgentRunException
{
    public ToolContextMissingException(string message) : base(message)
    {
    }
}
