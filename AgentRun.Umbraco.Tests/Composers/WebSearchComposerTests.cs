using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Services;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Composers;

/// <summary>
/// Smoke tests for Story 11.8 composer wiring. Reconstructs the minimum
/// service registrations an IUmbracoBuilder would apply in production,
/// then resolves the tool collection + factory to assert wiring intent.
/// </summary>
[TestFixture]
public class WebSearchComposerTests
{
    private static ServiceProvider BuildProvider(AgentRunWebSearchOptions? webSearch = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddMemoryCache();
        services.Configure<AgentRunOptions>(o => o.WebSearch = webSearch);

        services.AddSingleton<IWebSearchCache, WebSearchCache>();
        services.AddSingleton<IWebSearchProvider, BraveWebSearchProvider>();
        services.AddSingleton<IWebSearchProvider, TavilyWebSearchProvider>();
        services.AddSingleton<IWebSearchProviderFactory, WebSearchProviderFactory>();
        services.AddSingleton<IWorkflowTool, WebSearchTool>();

        return services.BuildServiceProvider();
    }

    [Test]
    public void WebSearchTool_Resolves_FromWorkflowToolsCollection()
    {
        using var provider = BuildProvider();

        var tools = provider.GetServices<IWorkflowTool>().ToArray();

        Assert.That(tools.Any(t => t is WebSearchTool), Is.True);
        Assert.That(tools.Count(t => t is WebSearchTool), Is.EqualTo(1));
    }

    [Test]
    public void Factory_Registers_BraveThenTavily_InOrder()
    {
        using var provider = BuildProvider();

        var factory = provider.GetRequiredService<IWebSearchProviderFactory>();

        var names = factory.GetRegisteredProviderNames();
        Assert.That(names, Is.EqualTo(new[] { "Brave", "Tavily" }));
    }

    [Test]
    public void IWebSearchCache_ResolvesAsSingleton_BackedByIMemoryCache()
    {
        using var provider = BuildProvider();

        var cacheA = provider.GetRequiredService<IWebSearchCache>();
        var cacheB = provider.GetRequiredService<IWebSearchCache>();

        Assert.That(cacheA, Is.SameAs(cacheB), "IWebSearchCache must be a singleton");
        Assert.That(provider.GetRequiredService<IMemoryCache>(), Is.Not.Null);
    }
}
