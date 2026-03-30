namespace Shallai.UmbracoAgentRunner.Instances;

public sealed class InstanceState
{
    public string WorkflowAlias { get; set; } = string.Empty;

    public int CurrentStepIndex { get; set; }

    public InstanceStatus Status { get; set; } = InstanceStatus.Pending;

    public List<StepState> Steps { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;
}

public sealed class StepState
{
    public string Id { get; set; } = string.Empty;

    public StepStatus Status { get; set; } = StepStatus.Pending;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
