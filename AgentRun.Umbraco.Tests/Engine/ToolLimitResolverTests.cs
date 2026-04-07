using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class ToolLimitResolverTests
{
    private static ToolLimitResolver MakeResolver(AgentRunOptions? options = null)
        => new(Options.Create(options ?? new AgentRunOptions()));

    private static StepDefinition MakeStep(ToolDefaultsConfig? overrides = null)
        => new() { Id = "step-1", Name = "Test", Agent = "agents/test.md", ToolOverrides = overrides };

    private static WorkflowDefinition MakeWorkflow(ToolDefaultsConfig? defaults = null)
        => new() { Name = "Test", Alias = "test-wf", ToolDefaults = defaults, Steps = { MakeStep() } };

    [Test]
    public void EngineDefault_AppliesWhenNothingConfigured()
    {
        var resolver = MakeResolver();
        var result = resolver.ResolveFetchUrlMaxResponseBytes(MakeStep(), MakeWorkflow());
        Assert.That(result, Is.EqualTo(EngineDefaults.FetchUrlMaxResponseBytes));
    }

    [Test]
    public void SiteDefault_OverridesEngineDefault()
    {
        var options = new AgentRunOptions
        {
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 500_000 } }
        };
        var resolver = MakeResolver(options);
        var result = resolver.ResolveFetchUrlMaxResponseBytes(MakeStep(), MakeWorkflow());
        Assert.That(result, Is.EqualTo(500_000));
    }

    [Test]
    public void WorkflowDefault_OverridesSiteDefault()
    {
        var options = new AgentRunOptions
        {
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 500_000 } }
        };
        var workflow = MakeWorkflow(new() { FetchUrl = new() { MaxResponseBytes = 700_000 } });
        var result = MakeResolver(options).ResolveFetchUrlMaxResponseBytes(MakeStep(), workflow);
        Assert.That(result, Is.EqualTo(700_000));
    }

    [Test]
    public void StepOverride_TakesPrecedenceOverEverything()
    {
        var options = new AgentRunOptions
        {
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 500_000 } }
        };
        var workflow = MakeWorkflow(new() { FetchUrl = new() { MaxResponseBytes = 700_000 } });
        var step = MakeStep(new() { FetchUrl = new() { MaxResponseBytes = 999_000 } });
        var result = MakeResolver(options).ResolveFetchUrlMaxResponseBytes(step, workflow);
        Assert.That(result, Is.EqualTo(999_000));
    }

    [Test]
    public void Ceiling_BelowEngineDefault_SilentlyClampsForUnconfiguredWorkflow()
    {
        var options = new AgentRunOptions
        {
            ToolLimits = new() { FetchUrl = new() { MaxResponseBytesCeiling = 50_000 } }
        };
        var resolver = MakeResolver(options);

        // Workflow declares nothing — engine default would be 1MB but ceiling clamps it.
        var result = resolver.ResolveFetchUrlMaxResponseBytes(MakeStep(), MakeWorkflow());
        Assert.That(result, Is.EqualTo(50_000));
    }

    [Test]
    public void Ceiling_BelowSiteDefault_SilentlyClamps()
    {
        var options = new AgentRunOptions
        {
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 800_000 } },
            ToolLimits = new() { FetchUrl = new() { MaxResponseBytesCeiling = 200_000 } }
        };
        var resolver = MakeResolver(options);
        var result = resolver.ResolveFetchUrlMaxResponseBytes(MakeStep(), MakeWorkflow());
        Assert.That(result, Is.EqualTo(200_000));
    }

    [Test]
    public void TimeoutSeconds_ResolvesViaSameChain()
    {
        var workflow = MakeWorkflow(new() { FetchUrl = new() { TimeoutSeconds = 45 } });
        var result = MakeResolver().ResolveFetchUrlTimeoutSeconds(MakeStep(), workflow);
        Assert.That(result, Is.EqualTo(45));
    }

    [Test]
    public void UserMessageTimeout_DefaultsToEngineDefault()
    {
        var result = MakeResolver().ResolveToolLoopUserMessageTimeoutSeconds(MakeStep(), MakeWorkflow());
        Assert.That(result, Is.EqualTo(EngineDefaults.ToolLoopUserMessageTimeoutSeconds));
    }

    [Test]
    public void UserMessageTimeout_WorkflowOverrideApplied()
    {
        var workflow = MakeWorkflow(new() { ToolLoop = new() { UserMessageTimeoutSeconds = 60 } });
        var result = MakeResolver().ResolveToolLoopUserMessageTimeoutSeconds(MakeStep(), workflow);
        Assert.That(result, Is.EqualTo(60));
    }

    [Test]
    public void SiteDefault_NonPositive_IsIgnoredAndFallsThroughToEngineDefault()
    {
        // P3: a misconfigured appsettings entry (zero or negative) must not produce
        // a permanently broken tool. The resolver sanitises and falls through.
        var options = new AgentRunOptions
        {
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 0, TimeoutSeconds = -5 } }
        };
        var resolver = MakeResolver(options);
        Assert.That(resolver.ResolveFetchUrlMaxResponseBytes(MakeStep(), MakeWorkflow()),
            Is.EqualTo(EngineDefaults.FetchUrlMaxResponseBytes));
        Assert.That(resolver.ResolveFetchUrlTimeoutSeconds(MakeStep(), MakeWorkflow()),
            Is.EqualTo(EngineDefaults.FetchUrlTimeoutSeconds));
    }
}
