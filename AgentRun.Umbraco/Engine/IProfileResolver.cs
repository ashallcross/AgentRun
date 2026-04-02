using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public interface IProfileResolver
{
    Task<IChatClient> ResolveAndGetClientAsync(StepDefinition step, WorkflowDefinition workflow, CancellationToken cancellationToken);

    Task<bool> HasConfiguredProviderAsync(WorkflowDefinition? workflow, CancellationToken cancellationToken);
}
