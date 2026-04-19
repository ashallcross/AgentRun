using System.Diagnostics.CodeAnalysis;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// In-memory request cache for web search responses. Keyed on
/// <c>(providerName, normalisedQuery, count, freshness)</c>. Purpose:
/// deduplicate sequential repeated queries within TTL. This cache does NOT
/// coalesce concurrent misses — two in-flight calls with the same key will
/// both reach the provider. Story 11.8 D1 — in-memory for v1; single-flight
/// coalescing and persistent cache across restarts are deferred to a later
/// pass (see deferred-work.md).
/// </summary>
public interface IWebSearchCache
{
    /// <summary>
    /// Looks up a cached result. Returns <c>true</c> with the cached JSON
    /// string on hit; <c>false</c> with <paramref name="resultJson"/> set to
    /// <c>null</c> on miss. The <see cref="NotNullWhenAttribute"/> encodes
    /// this contract so callers do not need a redundant null check on hit.
    /// </summary>
    bool TryGet(string providerName, WebSearchQuery query, [NotNullWhen(true)] out string? resultJson);

    /// <summary>
    /// Inserts or replaces a cached result with the given TTL. Only
    /// successful (non-error) provider responses should be cached.
    /// </summary>
    void Set(string providerName, WebSearchQuery query, string resultJson, TimeSpan ttl);
}
