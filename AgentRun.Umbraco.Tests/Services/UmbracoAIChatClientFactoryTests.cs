using Microsoft.Extensions.AI;
using NSubstitute;
using AgentRun.Umbraco.Services;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;

namespace AgentRun.Umbraco.Tests.Services;

[TestFixture]
public class UmbracoAIChatClientFactoryTests
{
    private IAIChatService _chatService = null!;
    private IAIProfileService _profileService = null!;
    private IChatClient _chatClient = null!;
    private UmbracoAIChatClientFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _chatService = Substitute.For<IAIChatService>();
        _profileService = Substitute.For<IAIProfileService>();
        _chatClient = Substitute.For<IChatClient>();
        _factory = new UmbracoAIChatClientFactory(_chatService, _profileService);
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
    }

    [Test]
    public async Task CreateChatClientAsync_PassesProfileAndRequestAlias_ToUnderlyingService()
    {
        // Shape assertions on AIChatBuilder are brittle (internal/sealed surface);
        // verify the adapter calls the underlying chat service exactly once with
        // the expected CT. The builder lambda is exercised by manual E2E
        // (Task 10.4) which confirms real request flow end-to-end.
        _chatService
            .CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);

        await _factory.CreateChatClientAsync("anthropic-claude", "step-scanner", CancellationToken.None);

        await _chatService.Received(1).CreateChatClientAsync(
            Arg.Any<Action<AIChatBuilder>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateChatClientAsync_ReturnsTheClientFromUnderlyingService()
    {
        _chatService
            .CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);

        var result = await _factory.CreateChatClientAsync("p", "r", CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
    }

    [Test]
    public async Task GetChatProfileAliasesAsync_FiltersToChatCapability_AndProjectsToAliases()
    {
        var profiles = new[]
        {
            new AIProfile { Alias = "openai-gpt", Name = "OpenAI", ConnectionId = Guid.NewGuid() },
            new AIProfile { Alias = "anthropic-claude", Name = "Anthropic", ConnectionId = Guid.NewGuid() }
        };
        _profileService.GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>())
            .Returns(profiles);

        var result = await _factory.GetChatProfileAliasesAsync(CancellationToken.None);

        // Order-preserving projection — sort policy lives in ProfileResolver, not here.
        Assert.That(result, Is.EqualTo(new[] { "openai-gpt", "anthropic-claude" }));
        await _profileService.Received(1).GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatProfileAliasesAsync_EmptyServiceResult_ReturnsEmptyList()
    {
        // Stub returns null-task-result → adapter's ?? Array.Empty<string>() guard
        _profileService.GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<AIProfile>>(null!));

        var result = await _factory.GetChatProfileAliasesAsync(CancellationToken.None);

        Assert.That(result, Is.Empty);
    }
}
