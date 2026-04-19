using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Default <see cref="IWebSearchCache"/> implementation backed by
/// <see cref="IMemoryCache"/>. Thread-safety is inherited from
/// <see cref="IMemoryCache"/>. Story 11.8 D1 / D4.
/// </summary>
public sealed class WebSearchCache : IWebSearchCache
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private readonly IMemoryCache _memoryCache;

    public WebSearchCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public bool TryGet(string providerName, WebSearchQuery query, out string? resultJson)
    {
        var key = BuildKey(providerName, query);
        if (_memoryCache.TryGetValue(key, out var value) && value is string cached)
        {
            resultJson = cached;
            return true;
        }

        resultJson = null;
        return false;
    }

    public void Set(string providerName, WebSearchQuery query, string resultJson, TimeSpan ttl)
    {
        var key = BuildKey(providerName, query);
        using var entry = _memoryCache.CreateEntry(key);
        entry.Value = resultJson;
        entry.AbsoluteExpirationRelativeToNow = ttl;
    }

    internal static string BuildKey(string providerName, WebSearchQuery query)
    {
        var normalisedProvider = providerName.ToLowerInvariant();
        var normalisedQuery = NormaliseQuery(query.Query);
        return $"{normalisedProvider}|{normalisedQuery}|{query.Count}|{query.Freshness}";
    }

    internal static string NormaliseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;
        var lowered = query.ToLowerInvariant().Trim();
        return WhitespaceRun.Replace(lowered, " ");
    }
}
