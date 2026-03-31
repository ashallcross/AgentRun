using Shallai.UmbracoAgentRunner.Engine.Events;

namespace Shallai.UmbracoAgentRunner.Engine;

public interface IWorkflowOrchestrator
{
    Task ExecuteNextStepAsync(
        string workflowAlias,
        string instanceId,
        ISseEventEmitter emitter,
        CancellationToken cancellationToken);
}
