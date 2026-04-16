using AgentRun.Umbraco.Instances;

namespace AgentRun.Umbraco.Models.ApiModels;

public sealed class InstanceResponse
{
    public required string Id { get; init; }
    public required string WorkflowAlias { get; init; }
    public required InstanceStatus Status { get; init; }
    public required int CurrentStepIndex { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
