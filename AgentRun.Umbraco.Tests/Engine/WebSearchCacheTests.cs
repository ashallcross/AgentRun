using Microsoft.Extensions.Caching.Memory;
using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class WebSearchCacheTests
{
    private IMemoryCache _memoryCache = null!;
    private WebSearchCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cache = new WebSearchCache(_memoryCache);
    }

    [TearDown]
    public void TearDown()
    {
        _memoryCache?.Dispose();
    }

    private static WebSearchQuery Q(string query, int count = 10, WebSearchFreshness freshness = WebSearchFreshness.All)
        => new(query, count, freshness);

    [Test]
    public void Set_ThenTryGet_RoundTripsValue()
    {
        _cache.Set("Brave", Q("test query"), "{\"results\":[]}", TimeSpan.FromMinutes(10));

        var found = _cache.TryGet("Brave", Q("test query"), out var value);

        Assert.That(found, Is.True);
        Assert.That(value, Is.EqualTo("{\"results\":[]}"));
    }

    [Test]
    public void TryGet_CacheMiss_ReturnsFalse()
    {
        var found = _cache.TryGet("Brave", Q("missing"), out var value);

        Assert.That(found, Is.False);
        Assert.That(value, Is.Null.Or.Empty);
    }

    [Test]
    public void QueryNormalisation_UnifiesWhitespaceAndCaseVariants()
    {
        _cache.Set("Brave", Q("Test Query"), "VALUE", TimeSpan.FromMinutes(10));

        Assert.That(_cache.TryGet("Brave", Q("test query"), out _), Is.True);
        Assert.That(_cache.TryGet("Brave", Q("  Test    Query  "), out _), Is.True);
        Assert.That(_cache.TryGet("Brave", Q("TEST QUERY"), out _), Is.True);
    }

    [Test]
    public void ProviderName_NamespacesCacheEntries_DistinctAcrossProviders()
    {
        _cache.Set("Brave", Q("same query"), "BRAVE-RESULT", TimeSpan.FromMinutes(10));
        _cache.Set("Tavily", Q("same query"), "TAVILY-RESULT", TimeSpan.FromMinutes(10));

        _cache.TryGet("Brave", Q("same query"), out var braveValue);
        _cache.TryGet("Tavily", Q("same query"), out var tavilyValue);

        Assert.That(braveValue, Is.EqualTo("BRAVE-RESULT"));
        Assert.That(tavilyValue, Is.EqualTo("TAVILY-RESULT"));
    }

    [Test]
    public void CountAndFreshness_AreSeparateCacheKeys()
    {
        _cache.Set("Brave", Q("q", count: 5, freshness: WebSearchFreshness.All), "A", TimeSpan.FromMinutes(10));
        _cache.Set("Brave", Q("q", count: 10, freshness: WebSearchFreshness.All), "B", TimeSpan.FromMinutes(10));
        _cache.Set("Brave", Q("q", count: 5, freshness: WebSearchFreshness.LastDay), "C", TimeSpan.FromMinutes(10));

        _cache.TryGet("Brave", Q("q", count: 5, freshness: WebSearchFreshness.All), out var a);
        _cache.TryGet("Brave", Q("q", count: 10, freshness: WebSearchFreshness.All), out var b);
        _cache.TryGet("Brave", Q("q", count: 5, freshness: WebSearchFreshness.LastDay), out var c);

        Assert.That(a, Is.EqualTo("A"));
        Assert.That(b, Is.EqualTo("B"));
        Assert.That(c, Is.EqualTo("C"));
    }

    [Test]
    public void ProviderName_IsCaseInsensitive_ForCacheHit()
    {
        _cache.Set("Brave", Q("q"), "VALUE", TimeSpan.FromMinutes(10));

        Assert.That(_cache.TryGet("brave", Q("q"), out _), Is.True);
        Assert.That(_cache.TryGet("BRAVE", Q("q"), out _), Is.True);
    }

    [Test]
    public void TryGet_AfterTtlElapses_ReturnsFalse()
    {
        // AC5 — entry set at t=0 with 5-minute TTL must be missing at t=6min.
        // Drive the MemoryCache's clock so the absolute expiration fires
        // deterministically without a wall-clock delay.
        var clock = new FakeSystemClock(DateTimeOffset.UnixEpoch);
        using var timedCache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var cache = new WebSearchCache(timedCache);

        cache.Set("Brave", Q("q"), "VALUE", TimeSpan.FromMinutes(5));
        Assert.That(cache.TryGet("Brave", Q("q"), out var fresh), Is.True);
        Assert.That(fresh, Is.EqualTo("VALUE"));

        clock.UtcNow = clock.UtcNow.AddMinutes(6);

        Assert.That(cache.TryGet("Brave", Q("q"), out _), Is.False);
    }

    // Minimal ISystemClock for deterministic TTL tests. MemoryCache reads
    // UtcNow on every access and compares against the entry's absolute
    // expiration timestamp.
    private sealed class FakeSystemClock : Microsoft.Extensions.Internal.ISystemClock
    {
        public FakeSystemClock(DateTimeOffset start) { UtcNow = start; }
        public DateTimeOffset UtcNow { get; set; }
    }
}
