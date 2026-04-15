using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;
// Umbraco.AI.Core.Chat also defines an IAIChatClientFactory; alias our engine
// interface to disambiguate.
using EngineChatClientFactory = AgentRun.Umbraco.Engine.IAIChatClientFactory;

namespace AgentRun.Umbraco.Services;

/// <summary>
/// Adapter between <see cref="EngineChatClientFactory"/> and Umbraco.AI's chat
/// services. This is the sole holder of <c>Umbraco.AI.*</c> dependencies in
/// the Engine-adjacent call chain — <c>Engine/ProfileResolver</c> depends on
/// this class only through the engine-side interface. Mirrors the
/// <c>ConversationRecorder</c> precedent for Engine-adjacent services in
/// <c>Services/</c> (Story 10.11 Track B).
/// </summary>
public sealed class UmbracoAIChatClientFactory : EngineChatClientFactory
{
    private readonly IAIChatService _chatService;
    private readonly IAIProfileService _profileService;

    public UmbracoAIChatClientFactory(IAIChatService chatService, IAIProfileService profileService)
    {
        _chatService = chatService;
        _profileService = profileService;
    }

    public Task<IChatClient> CreateChatClientAsync(
        string profileAlias,
        string requestAlias,
        CancellationToken cancellationToken)
    {
        return _chatService.CreateChatClientAsync(
            chat => chat.WithAlias(requestAlias).WithProfile(profileAlias),
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetChatProfileAliasesAsync(
        CancellationToken cancellationToken)
    {
        var profiles = await _profileService.GetProfilesAsync(AICapability.Chat, cancellationToken);
        return profiles?.Select(p => p.Alias).ToArray() ?? Array.Empty<string>();
    }
}
