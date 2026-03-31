using Microsoft.Extensions.DependencyInjection;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Tools;
using Shallai.UmbracoAgentRunner.Workflows;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Shallai.UmbracoAgentRunner.Composers;

public class AgentRunnerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Configuration
        builder.Services.Configure<Configuration.AgentRunnerOptions>(
            builder.Config.GetSection("Shallai:AgentRunner"));

        // Workflow services (singletons — stateless parsers + startup-loaded registry)
        builder.Services.AddSingleton<IWorkflowParser, WorkflowParser>();
        builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        builder.Services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>();

        // Instance management (singleton — disk-based, no in-memory cache)
        builder.Services.AddSingleton<IInstanceManager, InstanceManager>();

        // Engine services
        builder.Services.AddSingleton<IPromptAssembler, PromptAssembler>();
        builder.Services.AddSingleton<IProfileResolver, ProfileResolver>();
        builder.Services.AddSingleton<ICompletionChecker, CompletionChecker>();
        builder.Services.AddSingleton<IArtifactValidator, ArtifactValidator>();

        // Startup handler
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WorkflowRegistryInitializer>();

        // Step execution engine
        builder.Services.AddSingleton<IStepExecutor, StepExecutor>();

        // Workflow orchestrator (step sequencing + mode logic)
        builder.Services.AddSingleton<IWorkflowOrchestrator, WorkflowOrchestrator>();

        // Tools — register each IWorkflowTool individually (Epic 5)
        builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
        builder.Services.AddSingleton<IWorkflowTool, WriteFileTool>();
        builder.Services.AddSingleton<IWorkflowTool, ListFilesTool>();
        // builder.Services.AddSingleton<IWorkflowTool, FetchUrlTool>(); // Story 5.3
    }
}
