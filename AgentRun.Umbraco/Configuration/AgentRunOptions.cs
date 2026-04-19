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

    /// <summary>
    /// Site-level default tool tuning values. Bound from <c>AgentRun:ToolDefaults</c>.
    /// </summary>
    public AgentRunToolDefaultsOptions? ToolDefaults { get; set; }

    /// <summary>
    /// Site-level hard ceilings for tool tuning values. Bound from <c>AgentRun:ToolLimits</c>.
    /// </summary>
    public AgentRunToolLimitsOptions? ToolLimits { get; set; }

    /// <summary>
    /// Interval between SSE keepalive comment lines. Default: 15s. Clamped to [5s, 300s]
    /// at the consumption site. Reverse proxies (nginx/AWS ALB default 60s, Cloudflare
    /// orange-cloud ~30s) close idle HTTP connections; this heartbeat keeps SSE streams
    /// alive during long LLM thinking windows. Lower for aggressive proxies, higher to
    /// reduce network chatter.
    /// </summary>
    public TimeSpan KeepaliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Web search provider configuration (API keys, default provider, cache TTL).
    /// Bound from <c>AgentRun:WebSearch</c>. Story 11.8.
    /// </summary>
    public AgentRunWebSearchOptions? WebSearch { get; set; }
}
