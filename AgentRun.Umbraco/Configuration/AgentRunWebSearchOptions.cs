namespace AgentRun.Umbraco.Configuration;

/// <summary>
/// Web-search configuration bound from <c>AgentRun:WebSearch</c> in
/// <c>appsettings.json</c>. Story 11.8 D9. API-key storage UI is
/// intentionally deferred to the "AgentRun backoffice config area"
/// initiative (v2-future-considerations.md §8 Layer 2) — v1 is
/// appsettings-only.
/// </summary>
public sealed class AgentRunWebSearchOptions
{
    /// <summary>
    /// Registered providers keyed by name. Case-insensitive lookup so
    /// env-var overrides (<c>AGENTRUN__WEBSEARCH__PROVIDERS__BRAVE__APIKEY</c>)
    /// resolve regardless of casing.
    /// </summary>
    public Dictionary<string, AgentRunWebSearchProviderOptions> Providers { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit provider-name preference (e.g. <c>"Brave"</c>, <c>"Tavily"</c>).
    /// When unset, <c>WebSearchTool</c> picks the first registered provider
    /// that has a configured API key. Story 11.8 D2.
    /// </summary>
    public string? DefaultProvider { get; set; }

    /// <summary>
    /// Absolute-expiration TTL for the in-memory request cache. Defaults to
    /// 1 hour. Entries expire <see cref="TimeSpan"/> after insertion; there
    /// is no sliding / LRU policy.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Per-provider settings. Additional keys can be added here (endpoint
/// override, request-depth tuning) without reshaping the root options.
/// </summary>
public sealed class AgentRunWebSearchProviderOptions
{
    public string? ApiKey { get; set; }
}
