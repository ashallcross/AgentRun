namespace Shallai.UmbracoAgentRunner.Models.ApiModels;

public sealed class WorkflowSummary
{
    public required string Alias { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int StepCount { get; init; }
    public required string Mode { get; init; }
}
