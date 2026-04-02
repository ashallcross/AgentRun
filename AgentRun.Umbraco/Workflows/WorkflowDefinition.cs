namespace AgentRun.Umbraco.Workflows;

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string? DefaultProfile { get; set; }

    public List<StepDefinition> Steps { get; set; } = [];
}
