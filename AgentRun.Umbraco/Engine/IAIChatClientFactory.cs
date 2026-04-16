using Microsoft.Extensions.AI;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Engine-side abstraction over the chat-client provider. Implementations live
/// outside <c>Engine/</c> so this namespace can stay free of Umbraco.AI types
/// (see architecture.md Decision 1). Exposes only primitive types and
/// <see cref="Microsoft.Extensions.AI.IChatClient"/>.
/// </summary>
public interface IAIChatClientFactory
{
    /// <summary>
    /// Creates an <see cref="IChatClient"/> bound to the given profile alias
    /// and request alias. Exceptions from the underlying provider propagate;
    /// callers (e.g. <see cref="ProfileResolver"/>) translate them into
    /// <see cref="ProfileNotFoundException"/>.
    /// </summary>
    Task<IChatClient> CreateChatClientAsync(
        string profileAlias,
        string requestAlias,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns chat-capable profile aliases discovered from the underlying
    /// provider. Order is undefined — callers that need deterministic
    /// selection must sort. Implementations MUST return a non-null sequence
    /// (empty when no profiles are configured).
    /// </summary>
    Task<IReadOnlyList<string>> GetChatProfileAliasesAsync(
        CancellationToken cancellationToken);
}
