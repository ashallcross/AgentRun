using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Workflows;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;

namespace AgentRun.Umbraco.Engine;

public class ProfileResolver : IProfileResolver
{
    private readonly IAIChatService _chatService;
    private readonly IAIProfileService _profileService;
    private readonly AgentRunOptions _options;
    private readonly ILogger<ProfileResolver> _logger;

    public ProfileResolver(
        IAIChatService chatService,
        IAIProfileService profileService,
        IOptions<AgentRunOptions> options,
        ILogger<ProfileResolver> logger)
    {
        _chatService = chatService;
        _profileService = profileService;
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
            return await _chatService.CreateChatClientAsync(
                chat => chat.WithAlias($"step-{step.Id}").WithProfile(alias),
                cancellationToken);
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
            // workflow default_profile → site-default → auto-detect → bare (no profile)
            string? profile = null;
            if (!string.IsNullOrWhiteSpace(workflow?.DefaultProfile))
                profile = workflow.DefaultProfile;
            else if (!string.IsNullOrWhiteSpace(_options.DefaultProfile))
                profile = _options.DefaultProfile;

            // Auto-detect if no explicit profile configured
            if (profile is null)
            {
                profile = await AutoDetectProfileAliasAsync(cancellationToken);
            }

            var client = await _chatService.CreateChatClientAsync(
                chat =>
                {
                    chat.WithAlias("provider-check");
                    if (profile is not null) chat.WithProfile(profile);
                },
                cancellationToken);
            (client as IDisposable)?.Dispose();
            _logger.LogDebug("Provider prerequisite check passed: Umbraco.AI provider is configured (profile: {Profile})",
                profile ?? "(default)");
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

        // Auto-detect from Umbraco.AI profiles
        try
        {
            var profiles = await _profileService.GetProfilesAsync(AICapability.Chat, cancellationToken);
            var profileList = profiles?.ToList();

            if (profileList is null || profileList.Count == 0)
            {
                throw new ProfileNotFoundException(
                    "No AI provider configured. Go to Settings > AI to set up a provider.");
            }

            var selected = profileList.OrderBy(p => p.Alias, StringComparer.OrdinalIgnoreCase).First();

            _logger.LogInformation(
                "Auto-detected Umbraco.AI profile '{ProfileAlias}' for workflow execution",
                selected.Alias);

            if (profileList.Count > 1)
            {
                _logger.LogInformation(
                    "Multiple Umbraco.AI profiles found — using '{ProfileAlias}'. Set default_profile in your workflow YAML for deterministic selection.",
                    selected.Alias);
            }

            return (selected.Alias, "auto-detected");
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
            var profiles = await _profileService.GetProfilesAsync(AICapability.Chat, cancellationToken);
            var first = profiles?.OrderBy(p => p.Alias, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            return first?.Alias;
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
