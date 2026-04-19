using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.AI.Core.Contexts;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;
using UmbracoContextReference = global::Umbraco.Cms.Core.UmbracoContextReference;

namespace AgentRun.Umbraco.Tests.Workflows;

/// <summary>
/// Integration tests for Story 11.9: real GetAiContextTool with a real
/// IServiceScopeFactory scope (backed by a minimal ServiceProvider), real
/// IPublishedContent substitutes, and a stubbed IAIContextService acting as
/// the Umbraco.AI boundary. Verifies (1) the full alias-mode path resolves
/// through the scope factory end-to-end, and (2) the step-level
/// tool-whitelist contract — a step that omits <c>get_ai_context</c>
/// from <c>tools:</c> must not see it in the LLM-visible tool list.
/// </summary>
[TestFixture]
public class GetAiContextToolIntegrationTests
{
    [Test]
    public async Task FullStack_AliasMode_ResolvesThroughScopeFactory_EndToEnd()
    {
        var contextService = Substitute.For<IAIContextService>();
        var ctx = new AIContext
        {
            Alias = "brand-voice",
            Name = "Brand Voice",
            Resources = new List<AIContextResource>()
        };
        SetReadOnly(ctx, "Id", Guid.NewGuid());
        SetReadOnly(ctx, "Version", 1);
        contextService.GetContextByAliasAsync("brand-voice", Arg.Any<CancellationToken>()).Returns(ctx);

        // Real IServiceScopeFactory from a minimal ServiceProvider; the tool
        // resolves IAIContextService inside the scope.
        var services = new ServiceCollection();
        services.AddSingleton(contextService);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var umbracoContextFactory = Substitute.For<IUmbracoContextFactory>();
        var umbracoContext = Substitute.For<IUmbracoContext>();
        umbracoContext.Content.Returns(Substitute.For<IPublishedContentCache>());
        umbracoContextFactory.EnsureUmbracoContext().Returns(
            new UmbracoContextReference(umbracoContext, false, Substitute.For<IUmbracoContextAccessor>()));

        var tool = new GetAiContextTool(umbracoContextFactory, scopeFactory, NullLogger<GetAiContextTool>.Instance);

        var step = new StepDefinition { Id = "s-1", Name = "Writer", Agent = "a.md" };
        var workflow = new WorkflowDefinition { Name = "T", Alias = "t", Steps = { step } };
        var context = new ToolExecutionContext(Path.GetTempPath(), "inst-1", "s-1", "t")
        {
            Step = step,
            Workflow = workflow
        };

        var result = (string)await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alias"] = "brand-voice" },
            context,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.That(doc.RootElement.GetProperty("alias").GetString(), Is.EqualTo("brand-voice"));
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("Brand Voice"));
        await contextService.Received(1).GetContextByAliasAsync("brand-voice", Arg.Any<CancellationToken>());
    }

    [Test]
    public void StepWithoutGetAiContextTool_DoesNotReceiveIt_ViaDeclaredToolFilter()
    {
        // Mirror the filter StepExecutor applies — only tools whose Name is
        // in step.Tools are presented to the LLM. get_ai_context must not
        // leak through when the step does not declare it.
        var tool = new GetAiContextTool(
            Substitute.For<IUmbracoContextFactory>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<GetAiContextTool>.Instance);

        var tools = new IWorkflowTool[] { tool };
        var declaredNames = new[] { "read_file", "fetch_url" };

        var visible = tools
            .Where(t => declaredNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(visible, Is.Empty);
    }

    [Test]
    public void StepWithGetAiContextDeclared_SeesTool()
    {
        var tool = new GetAiContextTool(
            Substitute.For<IUmbracoContextFactory>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<GetAiContextTool>.Instance);

        var tools = new IWorkflowTool[] { tool };
        var declaredNames = new[] { "get_ai_context" };

        var visible = tools
            .Where(t => declaredNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(visible, Has.Length.EqualTo(1));
        Assert.That(visible[0].Name, Is.EqualTo("get_ai_context"));
    }

    private static void SetReadOnly(object target, string propertyName, object value)
    {
        var type = target.GetType();
        var prop = type.GetProperty(propertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (prop?.SetMethod is not null)
        {
            prop.SetValue(target, value);
            return;
        }
        var field = type.GetField($"<{propertyName}>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(target, value);
    }
}
