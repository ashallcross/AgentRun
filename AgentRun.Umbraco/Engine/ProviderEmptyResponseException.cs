namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Raised by ToolLoop when the LLM provider returns a 200 response with empty/null
/// content (no tool calls, no text) on the very first assistant turn. This indicates
/// a provider configuration issue (bad API key, no credit) rather than a mid-workflow
/// stall. Inherits <see cref="AgentRunException"/> so StepExecutor's catch block
/// surfaces the message via <c>run.error</c> SSE event.
/// </summary>
public sealed class ProviderEmptyResponseException : AgentRunException
{
    public string StepId { get; }
    public string InstanceId { get; }
    public string WorkflowAlias { get; }

    public ProviderEmptyResponseException(string stepId, string instanceId, string workflowAlias)
        : base($"The AI provider returned an empty response for step '{stepId}'. Check your provider configuration and API credit.")
    {
        StepId = stepId;
        InstanceId = instanceId;
        WorkflowAlias = workflowAlias;
    }
}
