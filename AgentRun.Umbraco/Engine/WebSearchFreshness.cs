namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Time-window filter for web search queries. Provider adapters translate
/// each value to the native freshness parameter. <see cref="All"/> means
/// "no restriction" — adapters omit the freshness parameter from the
/// provider request.
/// </summary>
public enum WebSearchFreshness
{
    All,
    LastDay,
    LastWeek,
    LastMonth,
    LastYear
}
