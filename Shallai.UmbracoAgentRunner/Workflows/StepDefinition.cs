namespace Shallai.UmbracoAgentRunner.Workflows;

public sealed class StepDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Agent { get; set; } = string.Empty;

    public string? Profile { get; set; }

    public List<string>? Tools { get; set; }

    public List<string>? ReadsFrom { get; set; }

    public List<string>? WritesTo { get; set; }

    public CompletionCheckDefinition? CompletionCheck { get; set; }
}
