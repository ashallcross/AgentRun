namespace AgentRun.Umbraco.Workflows;

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Mode { get; set; } = "interactive";

    public string? DefaultProfile { get; set; }

    public List<StepDefinition> Steps { get; set; } = [];

    /// <summary>
    /// Flat map of workflow-level variable values exposed to agent prompts as
    /// <c>{key}</c> tokens via <see cref="Engine.PromptAssembler"/>. Story 11.7.
    /// Keys must match <c>^[a-z0-9_]+$</c>; values are strings. Null/absent
    /// when the workflow declares no <c>config:</c> block.
    /// </summary>
    public Dictionary<string, string>? Config { get; set; }

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
