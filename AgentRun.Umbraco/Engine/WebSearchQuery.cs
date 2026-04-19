namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Provider-neutral web search request. <see cref="Count"/> is the desired
/// number of results; adapters may clamp to a provider-specific ceiling
/// silently (Brave and Tavily both cap at 20 at the time of writing).
/// </summary>
public sealed record WebSearchQuery(
    string Query,
    int Count,
    WebSearchFreshness Freshness);
