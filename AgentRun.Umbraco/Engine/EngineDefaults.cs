namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Engine-default tool tuning values. The single canonical home for these
/// constants — every other layer reaches them via <see cref="IToolLimitResolver"/>.
/// </summary>
public static class EngineDefaults
{
    /// <summary>1 MB. Single value applied regardless of content type.</summary>
    public const int FetchUrlMaxResponseBytes = 1_048_576;

    /// <summary>15 seconds. Per-request timeout for fetch_url.</summary>
    public const int FetchUrlTimeoutSeconds = 15;

    /// <summary>300 seconds (5 minutes). Interactive-mode user input wait window.</summary>
    public const int ToolLoopUserMessageTimeoutSeconds = 300;
}
