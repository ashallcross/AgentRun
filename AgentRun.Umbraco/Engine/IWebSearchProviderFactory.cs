namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Engine-side abstraction over provider name-based resolution. Mirrors the
/// shape of <see cref="IAIChatClientFactory"/> — implementations live in
/// <c>Services/</c> and index registered <see cref="IWebSearchProvider"/>
/// instances by <see cref="IWebSearchProvider.Name"/>. Story 11.8.
/// </summary>
public interface IWebSearchProviderFactory
{
    /// <summary>
    /// Resolves the provider whose <see cref="IWebSearchProvider.Name"/>
    /// matches <paramref name="providerName"/>. Matching is case-insensitive
    /// (<see cref="StringComparer.OrdinalIgnoreCase"/>).
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="providerName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="WebSearchException">When no provider with the given name is registered. Message enumerates the registered provider names so adopters can diagnose config drift.</exception>
    IWebSearchProvider GetAsync(string providerName, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the names of all registered providers in registration order.
    /// Used by <see cref="WebSearchTool"/>'s "first registered with
    /// configured key" fallback when <c>AgentRunOptions.WebSearch.DefaultProvider</c>
    /// is unset.
    /// </summary>
    IReadOnlyList<string> GetRegisteredProviderNames();
}
