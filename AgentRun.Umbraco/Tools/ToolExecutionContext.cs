using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tools;

public sealed record ToolExecutionContext(
    string InstanceFolderPath,
    string InstanceId,
    string StepId,
    string WorkflowAlias)
{
    /// <summary>
    /// The full step definition. Set by the executor; consumers that need to
    /// resolve tool tuning values via <see cref="Engine.IToolLimitResolver"/>
    /// can read it from here. Nullable so existing tests/constructions still work.
    /// </summary>
    public StepDefinition? Step { get; init; }

    /// <summary>
    /// The full workflow definition. See <see cref="Step"/>.
    /// </summary>
    public WorkflowDefinition? Workflow { get; init; }
}
