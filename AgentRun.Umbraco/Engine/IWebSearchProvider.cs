namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Engine-side abstraction over a web search provider. Implementations live
/// in <c>Services/</c> and hold provider-specific HTTP clients + API keys;
/// this interface exposes only provider-neutral types so <c>Engine/</c>
/// stays Umbraco-free and adapter-agnostic (Story 10.11 engine-boundary
/// invariant, Story 11.8 provider abstraction).
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Short display name used by <see cref="IWebSearchProviderFactory"/>
    /// to index registered providers (e.g. <c>"Brave"</c>, <c>"Tavily"</c>).
    /// Comparison is case-insensitive at the factory level.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Issues a search request against the provider and returns normalised
    /// results. Throws <see cref="WebSearchNotConfiguredException"/> when
    /// the API key is missing, <see cref="WebSearchRateLimitedException"/>
    /// on HTTP 429, and <see cref="WebSearchException"/> for any other
    /// transport / authentication / response-shape failure. Secrets must
    /// never appear in exception messages.
    /// </summary>
    Task<WebSearchResult[]> SearchAsync(
        WebSearchQuery query,
        CancellationToken cancellationToken);
}
