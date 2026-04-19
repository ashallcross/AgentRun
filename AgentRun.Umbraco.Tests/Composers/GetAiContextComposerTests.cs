using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.AI.Core.Contexts;
using Umbraco.Cms.Core.Web;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Composers;

/// <summary>
/// Smoke tests for Story 11.9 composer wiring. Reconstructs the minimum
/// service registrations an IUmbracoBuilder would apply in production,
/// then resolves the tool collection to assert wiring intent.
/// </summary>
[TestFixture]
public class GetAiContextComposerTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Collaborators the tool resolves via constructor injection.
        // Real implementations come from Umbraco/Umbraco.AI in production;
        // stubs are sufficient for smoke wiring.
        services.AddSingleton(Substitute.For<IUmbracoContextFactory>());
        services.AddSingleton(Substitute.For<IAIContextService>());

        services.AddSingleton<IWorkflowTool, GetAiContextTool>();

        return services.BuildServiceProvider();
    }

    [Test]
    public void GetAiContextTool_Resolves_FromWorkflowToolsCollection()
    {
        using var provider = BuildProvider();

        var tools = provider.GetServices<IWorkflowTool>().ToArray();

        Assert.That(tools.Any(t => t is GetAiContextTool), Is.True);
        Assert.That(tools.Count(t => t is GetAiContextTool), Is.EqualTo(1),
            "Exactly one GetAiContextTool instance must resolve");
    }

    [Test]
    public void GetAiContextTool_ExposesGetAiContextName()
    {
        using var provider = BuildProvider();

        var tool = provider.GetServices<IWorkflowTool>()
            .OfType<GetAiContextTool>()
            .Single();

        Assert.That(tool.Name, Is.EqualTo("get_ai_context"));
    }
}
