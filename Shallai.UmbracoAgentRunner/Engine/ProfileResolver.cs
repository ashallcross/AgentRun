using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Shallai.UmbracoAgentRunner.Configuration;
using Shallai.UmbracoAgentRunner.Workflows;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;

namespace Shallai.UmbracoAgentRunner.Engine;

public class ProfileResolver : IProfileResolver
{
    private readonly IAIChatService _chatService;
    private readonly AgentRunnerOptions _options;
    private readonly ILogger<ProfileResolver> _logger;

    public ProfileResolver(
        IAIChatService chatService,
        IOptions<AgentRunnerOptions> options,
        ILogger<ProfileResolver> logger)
    {
        _chatService = chatService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IChatClient> ResolveAndGetClientAsync(
        StepDefinition step,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        var (alias, source) = ResolveProfileAlias(step, workflow);

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

    public async Task<bool> HasConfiguredProviderAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = await _chatService.CreateChatClientAsync(
                chat => chat.WithAlias("provider-check"), cancellationToken);
            (client as IDisposable)?.Dispose();
            _logger.LogDebug("Provider prerequisite check passed: at least one Umbraco.AI provider is configured");
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

    private (string Alias, string Source) ResolveProfileAlias(StepDefinition step, WorkflowDefinition workflow)
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

        throw new ProfileNotFoundException("(none configured)");
    }
}
