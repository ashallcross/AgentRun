namespace AgentRun.Umbraco.Workflows;

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Mode { get; set; } = "interactive";

    public string? DefaultProfile { get; set; }

    public List<StepDefinition> Steps { get; set; } = [];

    /// <summary>
    /// Workflow-level tool tuning defaults. Maps to YAML <c>tool_defaults</c>.
    /// </summary>
    public ToolDefaultsConfig? ToolDefaults { get; set; }

    /// <summary>
    /// Workflow alias (folder name). Set by <see cref="WorkflowRegistry"/> after parse.
    /// Not deserialized from YAML.
    /// </summary>
    [YamlDotNet.Serialization.YamlIgnore]
    public string Alias { get; set; } = string.Empty;
}
