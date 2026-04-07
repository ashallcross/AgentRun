using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

/// <inheritdoc cref="IToolLimitResolver"/>
public sealed class ToolLimitResolver : IToolLimitResolver
{
    private static readonly AgentRunOptions FallbackOptions = new();

    private readonly IOptions<AgentRunOptions> _options;
    private readonly ILogger<ToolLimitResolver> _logger;
    private bool _bindingErrorLogged;

    public ToolLimitResolver(IOptions<AgentRunOptions> options)
        : this(options, NullLogger<ToolLimitResolver>.Instance) { }

    public ToolLimitResolver(IOptions<AgentRunOptions> options, ILogger<ToolLimitResolver> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Reads the bound options. If the IOptions binder throws (AC #10 — malformed
    /// appsettings), logs once at Error level and falls back to empty defaults so
    /// the engine continues to use engine defaults rather than crashing.
    /// </summary>
    private AgentRunOptions SafeOptions()
    {
        try
        {
            return _options.Value;
        }
        catch (Exception ex)
        {
            if (!_bindingErrorLogged)
            {
                _logger.LogError(ex,
                    "AgentRun:ToolDefaults / AgentRun:ToolLimits binding failed — falling back to engine defaults. " +
                    "Fix the appsettings entry to restore custom configuration.");
                _bindingErrorLogged = true;
            }
            return FallbackOptions;
        }
    }

    public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow)
    {
        var options = SafeOptions();
        return ResolveCore(
            stepValue:     step.ToolOverrides?.FetchUrl?.MaxResponseBytes,
            workflowValue: workflow.ToolDefaults?.FetchUrl?.MaxResponseBytes,
            siteDefault:   options.ToolDefaults?.FetchUrl?.MaxResponseBytes,
            engineDefault: EngineDefaults.FetchUrlMaxResponseBytes,
            ceiling:       options.ToolLimits?.FetchUrl?.MaxResponseBytesCeiling);
    }

    public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow)
    {
        var options = SafeOptions();
        return ResolveCore(
            stepValue:     step.ToolOverrides?.FetchUrl?.TimeoutSeconds,
            workflowValue: workflow.ToolDefaults?.FetchUrl?.TimeoutSeconds,
            siteDefault:   options.ToolDefaults?.FetchUrl?.TimeoutSeconds,
            engineDefault: EngineDefaults.FetchUrlTimeoutSeconds,
            ceiling:       options.ToolLimits?.FetchUrl?.TimeoutSecondsCeiling);
    }

    public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow)
    {
        var options = SafeOptions();
        return ResolveCore(
            stepValue:     step.ToolOverrides?.ToolLoop?.UserMessageTimeoutSeconds,
            workflowValue: workflow.ToolDefaults?.ToolLoop?.UserMessageTimeoutSeconds,
            siteDefault:   options.ToolDefaults?.ToolLoop?.UserMessageTimeoutSeconds,
            engineDefault: EngineDefaults.ToolLoopUserMessageTimeoutSeconds,
            ceiling:       options.ToolLimits?.ToolLoop?.UserMessageTimeoutSecondsCeiling);
    }

    private static int ResolveCore(
        int? stepValue,
        int? workflowValue,
        int? siteDefault,
        int engineDefault,
        int? ceiling)
    {
        // Pick first non-null in chain. Workflow/step values are validated as
        // positive at workflow load time; site defaults from appsettings are
        // sanitized here (non-positive → fall through) so a misconfigured
        // appsettings entry cannot produce a permanently broken tool.
        int? sanitizedSite = siteDefault is > 0 ? siteDefault : null;

        int picked =
            stepValue
            ?? workflowValue
            ?? sanitizedSite
            ?? engineDefault;

        // Defense in depth: workflow values exceeding the ceiling are blocked at
        // load time by the validator. At runtime we only need to silently clamp
        // admin/engine-default values that exceed the ceiling.
        if (ceiling.HasValue && picked > ceiling.Value)
            return ceiling.Value;

        return picked;
    }
}
