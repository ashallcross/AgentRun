using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Instances;

public interface IInstanceManager
{
    Task<InstanceState> CreateInstanceAsync(string workflowAlias, WorkflowDefinition definition, string createdBy, CancellationToken cancellationToken);

    Task<InstanceState?> GetInstanceAsync(string workflowAlias, string instanceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InstanceState>> ListInstancesAsync(string? workflowAlias, CancellationToken cancellationToken);

    Task<InstanceState> UpdateStepStatusAsync(string workflowAlias, string instanceId, int stepIndex, StepStatus status, CancellationToken cancellationToken);

    Task<InstanceState> SetInstanceStatusAsync(string workflowAlias, string instanceId, InstanceStatus status, CancellationToken cancellationToken);

    Task<bool> DeleteInstanceAsync(string workflowAlias, string instanceId, CancellationToken cancellationToken);

    Task<InstanceState?> FindInstanceAsync(string instanceId, CancellationToken cancellationToken);

    Task<InstanceState> AdvanceStepAsync(string workflowAlias, string instanceId, CancellationToken cancellationToken);

    string GetInstanceFolderPath(string workflowAlias, string instanceId);
}
