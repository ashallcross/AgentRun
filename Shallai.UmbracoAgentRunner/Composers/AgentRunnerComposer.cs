using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Shallai.UmbracoAgentRunner.Composers;

public class AgentRunnerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Register configuration options
        // builder.Services.Configure<Configuration.AgentRunnerOptions>(
        //     builder.Config.GetSection("Shallai:AgentRunner"));

        // Register interfaces, not concrete types
        // Singletons for stateless services:
        // builder.Services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>();

        // Scoped services for per-request state:
        // builder.Services.AddScoped<IStepExecutor, StepExecutor>();

        // Tools registered individually:
        // builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
    }
}
