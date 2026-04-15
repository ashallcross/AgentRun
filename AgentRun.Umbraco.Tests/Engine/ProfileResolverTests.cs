using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class ProfileResolverTests
{
    private IAIChatClientFactory _chatClientFactory = null!;
    private IChatClient _chatClient = null!;
    private ILogger<ProfileResolver> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _chatClientFactory = Substitute.For<IAIChatClientFactory>();
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
        var options = Options.Create(new AgentRunOptions { DefaultProfile = defaultProfile });
        return new ProfileResolver(_chatClientFactory, options, _logger);
    }

    private static StepDefinition MakeStep(string id = "step-1", string? profile = null) =>
        new() { Id = id, Profile = profile };

    private static WorkflowDefinition MakeWorkflow(string? defaultProfile = null) =>
        new() { Name = "test-workflow", DefaultProfile = defaultProfile };

    [Test]
    public async Task ResolveAndGetClientAsync_StepProfileTakesPriority()
    {
        // AC #1: step-level profile takes priority
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "site-default");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: "step-profile"),
            MakeWorkflow(defaultProfile: "workflow-profile"),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "step-profile", "step-step-1", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_FallsBackToWorkflowDefault()
    {
        // AC #2: workflow default when step has no profile
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "site-default");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: "workflow-profile"),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "workflow-profile", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_FallsBackToSiteDefault()
    {
        // AC #3: site-level fallback when neither step nor workflow have a profile
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "site-default");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: null),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "site-default", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void ResolveAndGetClientAsync_ThrowsWhenAllLevelsEmpty_AndNoProfiles()
    {
        // AC #6: no profile at any level AND no Umbraco.AI profiles
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var resolver = CreateResolver(defaultProfile: "");

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: null),
                MakeWorkflow(defaultProfile: null),
                CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("No AI provider configured"));
    }

    [Test]
    public void ResolveAndGetClientAsync_ThrowsWhenProfileNotInUmbracoAI()
    {
        // AC #5: resolved alias doesn't exist in Umbraco.AI — underlying factory throws
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "my-provider" });
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver();

        var result = await resolver.HasConfiguredProviderAsync(null, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasConfiguredProviderAsync_ReturnsFalseWhenNoProvider()
    {
        // AC #6: provider prerequisite returns false when not configured (factory throws)
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "some-provider" });
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No provider configured"));
        var resolver = CreateResolver();

        var result = await resolver.HasConfiguredProviderAsync(null, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ResolveAndGetClientAsync_CallsFactoryWithResolvedAliasAndRequestAlias()
    {
        // AC #4: verify the resolved alias + request alias reach the factory
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver();

        await resolver.ResolveAndGetClientAsync(
            MakeStep(id: "scanner", profile: "anthropic-claude"),
            MakeWorkflow(),
            CancellationToken.None);

        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "anthropic-claude", "step-scanner", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_LogsResolvedProfile()
    {
        // AC #8 / subtask 4.7: verify structured logging includes StepId, ProfileAlias, ProfileSource
        var loggerSub = Substitute.For<ILogger<ProfileResolver>>();
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var options = Options.Create(new AgentRunOptions { DefaultProfile = "site-default" });
        var resolver = new ProfileResolver(_chatClientFactory, options, loggerSub);

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
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var resolver = CreateResolver(defaultProfile: "site-default");

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await resolver.HasConfiguredProviderAsync(null, CancellationToken.None));
    }

    [Test]
    public void ResolveAndGetClientAsync_WrapsWithInnerException()
    {
        var original = new InvalidOperationException("Profile not found");
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(original);
        var resolver = CreateResolver();

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: "bad-profile"),
                MakeWorkflow(),
                CancellationToken.None));

        Assert.That(ex!.InnerException, Is.SameAs(original));
    }

    // --- Auto-detection fallback tests (Story 10.12, AC4-AC6) ---

    [Test]
    public async Task ResolveAndGetClientAsync_AutoDetectsSingleProfile()
    {
        // AC4: single Umbraco.AI profile auto-detected when no explicit profile configured
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "my-anthropic" });
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "");

        var result = await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: null),
            CancellationToken.None);

        Assert.That(result, Is.SameAs(_chatClient));
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "my-anthropic", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_MultipleProfiles_PicksFirstAlphabetically()
    {
        // AC5: multiple profiles — picks first by alias alphabetical order (case-insensitive)
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "openai-gpt", "anthropic-claude", "azure-openai" });
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);

        var loggerSub = Substitute.For<ILogger<ProfileResolver>>();
        var resolver = new ProfileResolver(_chatClientFactory, Options.Create(new AgentRunOptions()), loggerSub);

        await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: null),
            CancellationToken.None);

        // Verify the alphabetical winner "anthropic-claude" was resolved and logged
        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("anthropic-claude")
                && o.ToString()!.Contains("auto-detected")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "anthropic-claude", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAndGetClientAsync_MultipleProfiles_LogsGuidance()
    {
        // AC5: logs guidance suggesting explicit default_profile when multiple profiles found
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "openai-gpt", "anthropic-claude" });
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);

        var loggerSub = Substitute.For<ILogger<ProfileResolver>>();
        var resolver = new ProfileResolver(_chatClientFactory, Options.Create(new AgentRunOptions()), loggerSub);

        await resolver.ResolveAndGetClientAsync(
            MakeStep(profile: null),
            MakeWorkflow(defaultProfile: null),
            CancellationToken.None);

        // Verify guidance log message suggesting default_profile
        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("default_profile")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void ResolveAndGetClientAsync_ZeroProfiles_ThrowsClearMessage()
    {
        // AC6: zero profiles throws with clear "No AI provider configured" message
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var resolver = CreateResolver(defaultProfile: "");

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: null),
                MakeWorkflow(defaultProfile: null),
                CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("No AI provider configured"));
        Assert.That(ex.Message, Does.Contain("Settings > AI"));
    }

    [Test]
    public void ResolveAndGetClientAsync_ProfileServiceThrows_FallsThroughToProfileNotFoundException()
    {
        // Edge case: factory throws (e.g. database not initialised)
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Database not ready"));
        var resolver = CreateResolver(defaultProfile: "");

        var ex = Assert.ThrowsAsync<ProfileNotFoundException>(async () =>
            await resolver.ResolveAndGetClientAsync(
                MakeStep(profile: null),
                MakeWorkflow(defaultProfile: null),
                CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("No AI provider configured"));
    }

    [Test]
    public async Task HasConfiguredProviderAsync_AutoDetectsWhenNoExplicitProfile()
    {
        // HasConfiguredProviderAsync uses auto-detection when no workflow/site default
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "my-provider" });
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
        var resolver = CreateResolver(defaultProfile: "");

        var result = await resolver.HasConfiguredProviderAsync(
            MakeWorkflow(defaultProfile: null), CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasConfiguredProviderAsync_ReturnsFalseWhenNoProfilesAndNoProvider()
    {
        // F10: No profiles available and no provider — short-circuits before factory call
        _chatClientFactory
            .GetChatProfileAliasesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var resolver = CreateResolver(defaultProfile: "");

        var result = await resolver.HasConfiguredProviderAsync(null, CancellationToken.None);

        Assert.That(result, Is.False);
        // Short-circuits before factory call — no CreateChatClientAsync invocation
        await _chatClientFactory.DidNotReceive().CreateChatClientAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
