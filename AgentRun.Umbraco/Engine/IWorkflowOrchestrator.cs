using AgentRun.Umbraco.Engine.Events;

namespace AgentRun.Umbraco.Engine;

public interface IWorkflowOrchestrator
{
    Task ExecuteNextStepAsync(
        string workflowAlias,
        string instanceId,
        ISseEventEmitter emitter,
        CancellationToken cancellationToken);
}
