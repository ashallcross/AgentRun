using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public class ProfileResolver : IProfileResolver
{
    private readonly IAIChatClientFactory _chatClientFactory;
    private readonly AgentRunOptions _options;
    private readonly ILogger<ProfileResolver> _logger;

    public ProfileResolver(
        IAIChatClientFactory chatClientFactory,
        IOptions<AgentRunOptions> options,
        ILogger<ProfileResolver> logger)
    {
        _chatClientFactory = chatClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IChatClient> ResolveAndGetClientAsync(
        StepDefinition step,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        var (alias, source) = await ResolveProfileAliasAsync(step, workflow, cancellationToken);

        _logger.LogInformation(
            "Profile resolved for step {StepId}: {ProfileAlias} (source: {ProfileSource})",
            step.Id,
            alias,
            source);

        try
        {
            return await _chatClientFactory.CreateChatClientAsync(
                alias, $"step-{step.Id}", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ProfileNotFoundException)
        {
            throw new ProfileNotFoundException(alias, ex);
        }
    }

    public async Task<bool> HasConfiguredProviderAsync(
        WorkflowDefinition? workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve profile using the same fallback chain as execution:
            // workflow default_profile → site-default → auto-detect.
            string? profile = null;
            if (!string.IsNullOrWhiteSpace(workflow?.DefaultProfile))
                profile = workflow.DefaultProfile;
            else if (!string.IsNullOrWhiteSpace(_options.DefaultProfile))
                profile = _options.DefaultProfile;

            if (profile is null)
            {
                profile = await AutoDetectProfileAliasAsync(cancellationToken);
            }

            // Short-circuit: no profile anywhere means no provider is configured.
            // Previously we attempted a CreateChatClientAsync(profile=null) call
            // which sometimes threw a misleading exception; the factory interface
            // no longer accepts null aliases (F10).
            if (profile is null)
            {
                _logger.LogWarning(
                    "Provider prerequisite check failed: no Umbraco.AI profile configured");
                return false;
            }

            var client = await _chatClientFactory.CreateChatClientAsync(
                profile, "provider-check", cancellationToken);
            (client as IDisposable)?.Dispose();
            _logger.LogDebug(
                "Provider prerequisite check passed: Umbraco.AI provider is configured (profile: {Profile})",
                profile);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider prerequisite check failed: no Umbraco.AI provider is configured");
            return false;
        }
    }

    private async Task<(string Alias, string Source)> ResolveProfileAliasAsync(
        StepDefinition step,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(step.Profile))
        {
            return (step.Profile, "step");
        }

        if (!string.IsNullOrWhiteSpace(workflow.DefaultProfile))
        {
            return (workflow.DefaultProfile, "workflow");
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultProfile))
        {
            return (_options.DefaultProfile, "site-default");
        }

        // Auto-detect via the engine-boundary factory. Sort-and-pick stays here
        // (locked decision 7): selection policy is engine concern, adapter is
        // translation only.
        try
        {
            var aliases = await _chatClientFactory.GetChatProfileAliasesAsync(cancellationToken);
            var sorted = aliases
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sorted.Count == 0)
            {
                throw new ProfileNotFoundException(
                    "No AI provider configured. Go to Settings > AI to set up a provider.");
            }

            var selected = sorted[0];

            _logger.LogInformation(
                "Auto-detected Umbraco.AI profile '{ProfileAlias}' for workflow execution",
                selected);

            if (sorted.Count > 1)
            {
                _logger.LogInformation(
                    "Multiple Umbraco.AI profiles found — using '{ProfileAlias}'. Set default_profile in your workflow YAML for deterministic selection.",
                    selected);
            }

            return (selected, "auto-detected");
        }
        catch (ProfileNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-detect Umbraco.AI profiles — falling through to ProfileNotFoundException");
            throw new ProfileNotFoundException(
                "No AI provider configured. Go to Settings > AI to set up a provider.");
        }
    }

    /// <summary>
    /// Best-effort auto-detection for the provider check path. Returns null if detection fails.
    /// </summary>
    private async Task<string?> AutoDetectProfileAliasAsync(CancellationToken cancellationToken)
    {
        try
        {
            var aliases = await _chatClientFactory.GetChatProfileAliasesAsync(cancellationToken);
            return aliases
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-detect failed during provider prerequisite check");
            return null;
        }
    }
}
