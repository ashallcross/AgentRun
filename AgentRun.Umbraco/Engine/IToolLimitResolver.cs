using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Resolves tool tuning values through the chain:
/// step.tool_overrides → workflow.tool_defaults → site default → engine default.
/// Site-level hard ceilings are enforced at workflow load time by
/// <see cref="IWorkflowValidator.EnforceCeilings"/>; this resolver only clamps
/// admin/engine-default values that would exceed the ceiling (defense in depth).
/// </summary>
/// <remarks>
/// Singleton. Stateless.
/// </remarks>
public interface IToolLimitResolver
{
    /// <summary>
    /// Resolves <c>fetch_url.max_response_bytes</c> for the given step in the given workflow.
    /// Chain: step.tool_overrides → workflow.tool_defaults → site default → engine default (1 MB).
    /// </summary>
    int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);

    /// <summary>
    /// Resolves <c>read_file.max_response_bytes</c> for the given step in the given workflow.
    /// Chain: step.tool_overrides → workflow.tool_defaults → site default → engine default (256 KB).
    /// Story 9.9 — defence in depth for tool result offloading.
    /// </summary>
    int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);

    /// <summary>
    /// Resolves <c>fetch_url.timeout_seconds</c> for the given step in the given workflow.
    /// </summary>
    int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow);

    /// <summary>
    /// Resolves <c>tool_loop.user_message_timeout_seconds</c> for the given step in the given workflow.
    /// </summary>
    int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow);

    /// <summary>
    /// Resolves <c>list_content.max_response_bytes</c> for the given step in the given workflow.
    /// Chain: step.tool_overrides → workflow.tool_defaults → site default → engine default (256 KB).
    /// Story 9.12 — Umbraco content tools.
    /// </summary>
    int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);

    /// <summary>
    /// Resolves <c>get_content.max_response_bytes</c> for the given step in the given workflow.
    /// Chain: step.tool_overrides → workflow.tool_defaults → site default → engine default (256 KB).
    /// Story 9.12 — Umbraco content tools.
    /// </summary>
    int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);

    /// <summary>
    /// Resolves <c>list_content_types.max_response_bytes</c> for the given step in the given workflow.
    /// Chain: step.tool_overrides → workflow.tool_defaults → site default → engine default (256 KB).
    /// Story 9.12 — Umbraco content tools.
    /// </summary>
    int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);
}
