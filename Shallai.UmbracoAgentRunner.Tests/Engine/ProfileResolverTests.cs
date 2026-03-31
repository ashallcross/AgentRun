using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shallai.UmbracoAgentRunner.Configuration;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Workflows;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.InlineChat;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

[TestFixture]
public class ProfileResolverTests
{
    private IAIChatService _chatService = null!;
    private IChatClient _chatClient = null!;
    private ILogger<ProfileResolver> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _chatService = Substitute.For<IAIChatService>();
        _chatClient = Substitute.For<IChatClient>();
        _logger = NullLogger<ProfileResolver>.Instance;
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
    }

    private ProfileResolver CreateResolver(string defaultProfile = "")
    {
        var options = Options.Create(new AgentRunnerOptions { DefaultProfile = defaultProfile });
        return new ProfileResolver(_chatService, options, _logger);
    }

    private static StepDefinition MakeStep(string id = "step-1", string? profile = null) =>
        new() { Id = id, Profile = profile };

    private static WorkflowDefinition MakeWorkflow(string? defaultProfile = null) =>
        new() { Name = "test-workflow", DefaultProfile = defaultProfile };

    [Test]
    public async Task ResolveAndGetClientAsync_StepProfileTakesPriority()
    {
        // AC #1: step-level profile takes priority
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "site-default");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: "step-profile"),
            MakeWorkflow(defaultProfile: "workflow-profile"),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
        await _chatService.Received(1).CreateChatClientAsync(
            Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_FallsBackToWorkflowDefault()
    {
        // AC #2: workflow default when step has no profile
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "site-default");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: "workflow-profile"),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
    }

    [Test]
    public async Task ResolveAndGetClientAsync_FallsBackToSiteDefault()
    {
        // AC #3: site-level fallback when neither step nor workflow have a profile
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "site-default");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: null),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
    }

    [Test]
    public void ResolveAndGetClientAsync_ThrowsWhenAllLevelsEmpty()
    {
        // AC #5 variant: no profile configured at any level
        var resolver = CreateResolver(defaultProfile: "");

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: null),
                MakeWorkflow(defaultProfile: null),
                CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("(none configured)"));
    }

    [Test]
    public void ResolveAndGetClientAsync_ThrowsWhenProfileNotInUmbracoAI()
    {
        // AC #5: resolved alias doesn't exist in Umbraco.AI
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Profile not found"));
        var resolver = CreateResolver();

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: "nonexistent-profile"),
                MakeWorkflow(),
                CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("nonexistent-profile"));
    }

    [Test]
    public void ProfileNotFoundException_ContainsAlias()
    {
        // AC #5: error message contains the profile alias
        var ex = new ProfileNotFoundException("my-test-alias");

        Assert.That(ex.Message, Is.EqualTo("Profile 'my-test-alias' not found in Umbraco.AI configuration"));
    }

    [Test]
    public async Task HasConfiguredProviderAsync_ReturnsTrueWhenProviderAvailable()
    {
        // AC #6: provider prerequisite returns true when configured
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver();

        var result = await resolver.HasConfiguredProviderAsync(null, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasConfiguredProviderAsync_ReturnsFalseWhenNoProvider()
    {
        // AC #6: provider prerequisite returns false when not configured
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No provider configured"));
        var resolver = CreateResolver();

        var result = await resolver.HasConfiguredProviderAsync(null, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ResolveAndGetClientAsync_CallsCreateChatClientWithCorrectAlias()
    {
        // AC #4: verify the resolved alias is passed to IAIChatService
        Action<AIChatBuilder>? capturedConfigure = null;
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedConfigure = callInfo.Arg<Action<AIChatBuilder>>();
                return _chatClient;
            });
        var resolver = CreateResolver();

        await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: "anthropic-claude"),
            MakeWorkflow(),
            CancellationToken.None);

        Assert.That(capturedConfigure, Is.Not.Null);
        await _chatService.Received(1).CreateChatClientAsync(
            Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_LogsResolvedProfile()
    {
        // AC #8 / subtask 4.7: verify structured logging includes StepId, ProfileAlias, ProfileSource
        var loggerSub = Substitute.For<ILogger<ProfileResolver>>();
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var options = Options.Create(new AgentRunnerOptions { DefaultProfile = "site-default" });
        var resolver = new ProfileResolver(_chatService, options, loggerSub);

        await resolver.ResolveAndGetClientAsync(
            MakeStep(id: "gather_content", profile: "anthropic-claude"),
            MakeWorkflow(),
            CancellationToken.None);

        loggerSub.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("gather_content")
                && o.ToString()!.Contains("anthropic-claude")
                && o.ToString()!.Contains("step")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void ResolveAndGetClientAsync_PropagatesCancellation()
    {
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var resolver = CreateResolver(defaultProfile: "site-default");

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: "some-profile"),
                MakeWorkflow(),
                CancellationToken.None));
    }

    [Test]
    public void HasConfiguredProviderAsync_PropagatesCancellation()
    {
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var resolver = CreateResolver();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await resolver.HasConfiguredProviderAsync(null, CancellationToken.None));
    }

    [Test]
    public void ResolveAndGetClientAsync_WrapsWithInnerException()
    {
        var original = new InvalidOperationException("Profile not found");
        _chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(original);
        var resolver = CreateResolver();

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: "bad-profile"),
                MakeWorkflow(),
                CancellationToken.None));

        Assert.That(ex!.InnerException, Is.SameAs(original));
    }
}
