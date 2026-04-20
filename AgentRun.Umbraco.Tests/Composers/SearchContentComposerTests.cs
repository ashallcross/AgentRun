using AgentRun.Umbraco.Tools;
using Examine;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace AgentRun.Umbraco.Tests.Composers;

/// <summary>
/// Smoke tests for Story 11.12 composer wiring. Reconstructs the minimum
/// service registrations an IUmbracoBuilder would apply in production,
/// then resolves the tool collection to assert wiring intent.
/// </summary>
[TestFixture]
public class SearchContentComposerTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Collaborators injected via constructor. Real implementations come
        // from Umbraco/Examine in production; stubs are sufficient for the
        // wiring smoke check.
        services.AddSingleton(Substitute.For<IExamineManager>());
        services.AddSingleton(Substitute.For<IUmbracoContextFactory>());
        services.AddSingleton(Substitute.For<IPublishedUrlProvider>());

        services.AddSingleton<IWorkflowTool, SearchContentTool>();

        return services.BuildServiceProvider();
    }

    [Test]
    public void SearchContentTool_Resolves_FromWorkflowToolsCollection()
    {
        using var provider = BuildProvider();

        var tools = provider.GetServices<IWorkflowTool>().ToArray();

        Assert.That(tools.Any(t => t is SearchContentTool), Is.True);
        Assert.That(tools.Count(t => t is SearchContentTool), Is.EqualTo(1),
            "Exactly one SearchContentTool instance must resolve");
    }

    [Test]
    public void SearchContentTool_ExposesSearchContentName()
    {
        using var provider = BuildProvider();

        var tool = provider.GetServices<IWorkflowTool>()
            .OfType<SearchContentTool>()
            .Single();

        Assert.That(tool.Name, Is.EqualTo("search_content"));
    }
}
