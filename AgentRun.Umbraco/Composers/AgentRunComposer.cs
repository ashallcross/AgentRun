using Microsoft.Extensions.DependencyInjection;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Security;
using AgentRun.Umbraco.Services;
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

        // Tool limit resolver — singleton, stateless, depends only on IOptions<>.
        builder.Services.AddSingleton<IToolLimitResolver, ToolLimitResolver>();

        // Engine services
        builder.Services.AddSingleton<IPromptAssembler, PromptAssembler>();
        // Engine-boundary adapter — holds Umbraco.AI.* deps so ProfileResolver
        // (Engine/) can stay Umbraco-free. Registered BEFORE IProfileResolver
        // so DI resolution order is intuitive.
        builder.Services.AddSingleton<IAIChatClientFactory, UmbracoAIChatClientFactory>();
        builder.Services.AddSingleton<IProfileResolver, ProfileResolver>();
        builder.Services.AddSingleton<ICompletionChecker, CompletionChecker>();
        builder.Services.AddSingleton<IArtifactValidator, ArtifactValidator>();

        // Startup handler
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WorkflowRegistryInitializer>();

        // Active instance registry (message queue for running instances)
        builder.Services.AddSingleton<IActiveInstanceRegistry, ActiveInstanceRegistry>();

        // Step execution engine. StepExecutor delegates failure classification
        // to IStepExecutionFailureHandler (preserves the AgentRunException
        // bypass invariant — must not be routed through LlmErrorClassifier).
        builder.Services.AddSingleton<IStepExecutionFailureHandler, StepExecutionFailureHandler>();
        // IStreamingResponseAccumulator + IStallRecoveryPolicy are instantiated
        // directly by ToolLoop as stateless static-readonly defaults — ToolLoop
        // is a static class and cannot consume constructor-injected services,
        // so DI registration would just be a dead branch.
        builder.Services.AddSingleton<IStepExecutor, StepExecutor>();

        // Workflow orchestrator (step sequencing + mode logic)
        builder.Services.AddSingleton<IWorkflowOrchestrator, WorkflowOrchestrator>();

        // Tools — register each IWorkflowTool individually (Epic 5)
        builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
        builder.Services.AddSingleton<IWorkflowTool, WriteFileTool>();
        builder.Services.AddSingleton<IWorkflowTool, ListFilesTool>();

        // SSRF protection + fetch_url tool
        builder.Services.AddSingleton<INetworkAccessPolicy, DefaultNetworkAccessPolicy>();
        builder.Services.AddSingleton<SsrfProtection>();
        // FetchUrl HttpClient — timeout is applied per-request via
        // IToolLimitResolver, NOT via HttpClient.Timeout. Auto-redirect is
        // disabled so FetchUrlTool can run its manual redirect loop that
        // re-validates every Location target through SsrfProtection — this
        // closes an SSRF redirect bypass. Do NOT switch back to
        // AllowAutoRedirect = true under any circumstances.
        builder.Services.AddHttpClient("FetchUrl")
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        // FetchUrlTool coordinates over two collaborators:
        //   IHtmlStructureExtractor owns AngleSharp parsing + structured-fact walks.
        //   IFetchCacheWriter owns .fetch-cache/ path sandbox + file I/O.
        builder.Services.AddSingleton<IHtmlStructureExtractor, HtmlStructureExtractor>();
        builder.Services.AddSingleton<IFetchCacheWriter, FetchCacheWriter>();
        builder.Services.AddSingleton<IWorkflowTool, FetchUrlTool>();

        // Umbraco content tools — in-process access to published content
        builder.Services.AddSingleton<IWorkflowTool, ListContentTool>();
        builder.Services.AddSingleton<IWorkflowTool, GetContentTool>();
        builder.Services.AddSingleton<IWorkflowTool, ListContentTypesTool>();
    }
}
