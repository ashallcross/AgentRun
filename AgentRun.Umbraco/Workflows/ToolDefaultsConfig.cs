namespace AgentRun.Umbraco.Workflows;

/// <summary>
/// Workflow- or step-level tool tuning values. Maps to the YAML
/// <c>tool_defaults</c> (workflow root) or <c>tool_overrides</c> (per step) block.
/// All values are nullable: <c>null</c> means "fall through to the next tier
/// in the resolution chain (workflow → site → engine default)".
/// </summary>
public sealed class ToolDefaultsConfig
{
    public FetchUrlConfig? FetchUrl { get; set; }

    public ReadFileConfig? ReadFile { get; set; }

    public ToolLoopConfig? ToolLoop { get; set; }

    public ListContentConfig? ListContent { get; set; }

    public GetContentConfig? GetContent { get; set; }

    public ListContentTypesConfig? ListContentTypes { get; set; }

    public sealed class FetchUrlConfig
    {
        public int? MaxResponseBytes { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    public sealed class ReadFileConfig
    {
        public int? MaxResponseBytes { get; set; }
    }

    public sealed class ToolLoopConfig
    {
        public int? UserMessageTimeoutSeconds { get; set; }
    }

    public sealed class ListContentConfig
    {
        public int? MaxResponseBytes { get; set; }
    }

    public sealed class GetContentConfig
    {
        public int? MaxResponseBytes { get; set; }
    }

    public sealed class ListContentTypesConfig
    {
        public int? MaxResponseBytes { get; set; }
    }
}
