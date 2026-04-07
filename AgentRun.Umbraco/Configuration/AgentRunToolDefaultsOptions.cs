namespace AgentRun.Umbraco.Configuration;

/// <summary>
/// Site-level default tool tuning values bound from <c>AgentRun:ToolDefaults</c>
/// in <c>appsettings.json</c>. All values are nullable: <c>null</c> means
/// "no site default — fall through to the engine default".
/// </summary>
public sealed class AgentRunToolDefaultsOptions
{
    public FetchUrlDefaults? FetchUrl { get; set; }

    public ToolLoopDefaults? ToolLoop { get; set; }

    public sealed class FetchUrlDefaults
    {
        public int? MaxResponseBytes { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    public sealed class ToolLoopDefaults
    {
        public int? UserMessageTimeoutSeconds { get; set; }
    }
}
