namespace AgentRun.Umbraco.Configuration;

public class AgentRunOptions
{
    /// <summary>
    /// Root path for instance data storage.
    /// Default: {ContentRootPath}/App_Data/AgentRun.Umbraco/instances/
    /// </summary>
    public string DataRootPath { get; set; } = "App_Data/AgentRun.Umbraco/instances/";

    /// <summary>
    /// Default Umbraco.AI profile alias for LLM provider resolution.
    /// </summary>
    public string DefaultProfile { get; set; } = string.Empty;

    /// <summary>
    /// Path to workflow definitions folder. Relative to ContentRootPath.
    /// </summary>
    public string WorkflowPath { get; set; } = "App_Data/AgentRun.Umbraco/workflows/";
}
