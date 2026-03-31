using Microsoft.Extensions.AI;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Engine;

public interface IProfileResolver
{
    Task<IChatClient> ResolveAndGetClientAsync(StepDefinition step, WorkflowDefinition workflow, CancellationToken cancellationToken);

    Task<bool> HasConfiguredProviderAsync(CancellationToken cancellationToken);
}
