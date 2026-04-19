namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Provider-neutral web search result. Fields absent in the provider
/// response populate as <c>null</c> (dates that don't parse, providers that
/// don't emit a relevance score). <see cref="SourceProvider"/> identifies
/// the adapter that produced the result — useful for downstream agents that
/// want to reason about provider quality.
/// </summary>
public sealed record WebSearchResult(
    string Title,
    string Url,
    string Snippet,
    DateTimeOffset? PublishedDate,
    string SourceProvider,
    double? RelevanceScore);
