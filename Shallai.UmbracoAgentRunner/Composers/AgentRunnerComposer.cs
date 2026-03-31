using Microsoft.Extensions.DependencyInjection;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Instances;
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

        // Startup handler
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WorkflowRegistryInitializer>();

        // Scoped services for per-request state:
        // builder.Services.AddScoped<IStepExecutor, StepExecutor>();

        // Tools registered individually:
        // builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
    }
}
