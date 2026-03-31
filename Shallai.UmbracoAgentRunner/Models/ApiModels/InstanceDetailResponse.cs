using Shallai.UmbracoAgentRunner.Instances;

namespace Shallai.UmbracoAgentRunner.Models.ApiModels;

public sealed class InstanceDetailResponse
{
    public required string Id { get; init; }
    public required string WorkflowAlias { get; init; }
    public required string WorkflowName { get; init; }
    public required InstanceStatus Status { get; init; }
    public required int CurrentStepIndex { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public required StepResponse[] Steps { get; init; }
}

public sealed class StepResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required StepStatus Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string[]? WritesTo { get; init; }
}
