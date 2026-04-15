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

    /// <summary>
    /// Story 10.6 Task 2.6 — set <see cref="InstanceState.CurrentStepIndex"/>
    /// to an explicit value. Used by the Retry endpoint to reconcile drift
    /// between the persisted CurrentStepIndex and the step that FindIndex
    /// actually resumes from (see deferred-work.md 2026-04-14, 10.9 review).
    /// The implementation validates <paramref name="stepIndex"/> as
    /// defence-in-depth and throws <see cref="ArgumentOutOfRangeException"/>
    /// if it is outside <c>[0, state.Steps.Count)</c>; callers should still
    /// range-check before calling to avoid a 500 response on misuse.
    /// </summary>
    Task<InstanceState> SetCurrentStepIndexAsync(string workflowAlias, string instanceId, int stepIndex, CancellationToken cancellationToken);

    string GetInstanceFolderPath(string workflowAlias, string instanceId);
}
