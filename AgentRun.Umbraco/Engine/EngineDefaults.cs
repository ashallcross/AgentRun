namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Engine-default tool tuning values. The single canonical home for these
/// constants — every other layer reaches them via <see cref="IToolLimitResolver"/>.
/// </summary>
public static class EngineDefaults
{
    /// <summary>1 MB. Single value applied regardless of content type.</summary>
    public const int FetchUrlMaxResponseBytes = 1_048_576;

    /// <summary>
    /// 256 KB. Per-call byte cap for <c>read_file</c>. Files at or below this
    /// limit return as today; files over the limit return the first
    /// <see cref="ReadFileMaxResponseBytes"/> bytes plus the verbatim truncation
    /// marker. Story 9.9 — defence in depth for tool result offloading.
    /// </summary>
    public const int ReadFileMaxResponseBytes = 262_144;

    /// <summary>15 seconds. Per-request timeout for fetch_url.</summary>
    public const int FetchUrlTimeoutSeconds = 15;

    /// <summary>300 seconds (5 minutes). Interactive-mode user input wait window.</summary>
    public const int ToolLoopUserMessageTimeoutSeconds = 300;

    /// <summary>
    /// 10 seconds. Upper bound a stall (Story 9.0) is allowed to take from
    /// detection to surfacing as a <c>run.error</c>. The actual throw is
    /// synchronous, so this exists for documentation/observability and as the
    /// promise codified in Story 9.0 AC #1.
    /// </summary>
    public const int StallDetectionWindowSeconds = 10;

    /// <summary>
    /// 256 KB. Per-call byte cap for <c>list_content</c> JSON result.
    /// Story 9.12 — Umbraco content tools.
    /// </summary>
    public const int ListContentMaxResponseBytes = 262_144;

    /// <summary>
    /// 256 KB. Per-call byte cap for <c>get_content</c> JSON result.
    /// Story 9.12 — Umbraco content tools.
    /// </summary>
    public const int GetContentMaxResponseBytes = 262_144;

    /// <summary>
    /// 256 KB. Per-call byte cap for <c>list_content_types</c> JSON result.
    /// Story 9.12 — Umbraco content tools.
    /// </summary>
    public const int ListContentTypesMaxResponseBytes = 262_144;
}
