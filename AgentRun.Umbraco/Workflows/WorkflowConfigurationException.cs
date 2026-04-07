using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Workflows;

/// <summary>
/// Thrown when a workflow's declared tool tuning value violates a site-level
/// hard ceiling configured in <c>AgentRun:ToolLimits</c>. The workflow is
/// rejected at load time and never registered.
/// </summary>
public sealed class WorkflowConfigurationException : AgentRunException
{
    public WorkflowConfigurationException(string message) : base(message)
    {
    }

    public WorkflowConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
