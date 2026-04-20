using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // Story 11.7 — TimeProvider is the clock seam for `{today}` resolution
        // in PromptAssembler. TryAddSingleton so Umbraco or another composer
        // can provide a custom TimeProvider if needed (e.g. a fixed-date clock
        // for integration testing).
        builder.Services.TryAddSingleton(TimeProvider.System);
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

        // Keyword search via Umbraco's Examine External index (Story 11.12).
        // Complements list_content (enumerate-by-filter) — same workflow can use both.
        // v1 scope limits documented on SearchContentTool.
        builder.Services.AddSingleton<IWorkflowTool, SearchContentTool>();

        // Umbraco.AI Context read-tool (Story 11.9). Platform tool: any workflow
        // step can opt in via `tools: [get_ai_context]`. Reads Contexts by alias
        // or by content-node tree-inheritance. No cache in v1.
        builder.Services.AddSingleton<IWorkflowTool, GetAiContextTool>();

        // Web search (Story 11.8). Platform tool: any workflow step can opt in
        // via `tools: [web_search]`. Provider adapters live in Services/ and
        // hold their own HttpClient + API keys; factory mirrors
        // IAIChatClientFactory. Cache is in-memory IMemoryCache wrapper.
        builder.Services.AddHttpClient("WebSearch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            // 2 MB — well above any plausible JSON search response. Caps
            // unbounded-body DoS from a rogue or compromised provider; the
            // tool is LLM-invokable so the blast radius of an unbounded read
            // is amplified by retries.
            client.MaxResponseContentBufferSize = 2_000_000;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentRun/1.0");
        });
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IWebSearchCache, WebSearchCache>();
        // Registration order is load-bearing (Story 11.8 D2): the first
        // registered provider with a configured key wins the fallback when
        // AgentRun:WebSearch:DefaultProvider is unset.
        builder.Services.AddSingleton<IWebSearchProvider, BraveWebSearchProvider>();
        builder.Services.AddSingleton<IWebSearchProvider, TavilyWebSearchProvider>();
        builder.Services.AddSingleton<IWebSearchProviderFactory, WebSearchProviderFactory>();
        builder.Services.AddSingleton<IWorkflowTool, WebSearchTool>();
    }
}
