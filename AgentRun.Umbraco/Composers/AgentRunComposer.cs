using Microsoft.Extensions.DependencyInjection;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Security;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace AgentRun.Umbraco.Composers;

public class AgentRunComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Configuration
        builder.Services.Configure<Configuration.AgentRunOptions>(
            builder.Config.GetSection("AgentRun"));

        // Workflow services (singletons — stateless parsers + startup-loaded registry)
        builder.Services.AddSingleton<IWorkflowParser, WorkflowParser>();
        builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        builder.Services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>();

        // Instance management (singleton — disk-based, no in-memory cache)
        builder.Services.AddSingleton<IInstanceManager, InstanceManager>();

        // Conversation persistence (JSONL append-only per step)
        builder.Services.AddSingleton<IConversationStore, ConversationStore>();

        // Tool limit resolver (Story 9.6) — singleton, stateless, depends only on IOptions<>.
        builder.Services.AddSingleton<IToolLimitResolver, ToolLimitResolver>();

        // Engine services
        builder.Services.AddSingleton<IPromptAssembler, PromptAssembler>();
        builder.Services.AddSingleton<IProfileResolver, ProfileResolver>();
        builder.Services.AddSingleton<ICompletionChecker, CompletionChecker>();
        builder.Services.AddSingleton<IArtifactValidator, ArtifactValidator>();

        // Startup handler
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WorkflowRegistryInitializer>();

        // Active instance registry (message queue for running instances)
        builder.Services.AddSingleton<IActiveInstanceRegistry, ActiveInstanceRegistry>();

        // Step execution engine
        builder.Services.AddSingleton<IStepExecutor, StepExecutor>();

        // Workflow orchestrator (step sequencing + mode logic)
        builder.Services.AddSingleton<IWorkflowOrchestrator, WorkflowOrchestrator>();

        // Tools — register each IWorkflowTool individually (Epic 5)
        builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
        builder.Services.AddSingleton<IWorkflowTool, WriteFileTool>();
        builder.Services.AddSingleton<IWorkflowTool, ListFilesTool>();

        // SSRF protection + fetch_url tool (Story 5.3)
        builder.Services.AddSingleton<INetworkAccessPolicy, DefaultNetworkAccessPolicy>();
        builder.Services.AddSingleton<SsrfProtection>();
        // FetchUrl HttpClient — timeout is applied per-request via IToolLimitResolver
        // (Story 9.6), NOT via HttpClient.Timeout.
        // Auto-redirect is disabled (Story 9.1b Locked Decision #11): FetchUrlTool
        // implements a manual redirect loop that re-runs SsrfProtection.ValidateUrlAsync
        // against every Location target, closing a pre-existing SSRF redirect bypass.
        // Do NOT switch this back to AllowAutoRedirect = true under any circumstances.
        builder.Services.AddHttpClient("FetchUrl")
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        builder.Services.AddSingleton<IWorkflowTool, FetchUrlTool>();
    }
}
