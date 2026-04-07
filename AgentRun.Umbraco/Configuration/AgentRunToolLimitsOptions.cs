namespace AgentRun.Umbraco.Configuration;

/// <summary>
/// Site-level hard ceilings bound from <c>AgentRun:ToolLimits</c> in
/// <c>appsettings.json</c>. A workflow YAML value above one of these ceilings
/// is a workflow load failure (<see cref="Workflows.WorkflowConfigurationException"/>).
/// All values are nullable: <c>null</c> means "no ceiling configured for this field".
/// </summary>
public sealed class AgentRunToolLimitsOptions
{
    public FetchUrlLimits? FetchUrl { get; set; }

    public ToolLoopLimits? ToolLoop { get; set; }

    public sealed class FetchUrlLimits
    {
        public int? MaxResponseBytesCeiling { get; set; }
        public int? TimeoutSecondsCeiling { get; set; }
    }

    public sealed class ToolLoopLimits
    {
        public int? UserMessageTimeoutSecondsCeiling { get; set; }
    }
}
