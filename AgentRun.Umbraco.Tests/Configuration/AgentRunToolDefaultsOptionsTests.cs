using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Configuration;

[TestFixture]
public class AgentRunToolDefaultsOptionsTests
{
    [Test]
    public void BindsFromInMemoryConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRun:ToolDefaults:FetchUrl:MaxResponseBytes"] = "2097152",
                ["AgentRun:ToolDefaults:FetchUrl:TimeoutSeconds"] = "30",
                ["AgentRun:ToolDefaults:ToolLoop:UserMessageTimeoutSeconds"] = "600",
                ["AgentRun:ToolLimits:FetchUrl:MaxResponseBytesCeiling"] = "20971520",
                ["AgentRun:ToolLimits:ToolLoop:UserMessageTimeoutSecondsCeiling"] = "3600"
            })
            .Build();

        var options = new AgentRunOptions();
        config.GetSection("AgentRun").Bind(options);

        Assert.That(options.ToolDefaults, Is.Not.Null);
        Assert.That(options.ToolDefaults!.FetchUrl!.MaxResponseBytes, Is.EqualTo(2_097_152));
        Assert.That(options.ToolDefaults.FetchUrl.TimeoutSeconds, Is.EqualTo(30));
        Assert.That(options.ToolDefaults.ToolLoop!.UserMessageTimeoutSeconds, Is.EqualTo(600));

        Assert.That(options.ToolLimits, Is.Not.Null);
        Assert.That(options.ToolLimits!.FetchUrl!.MaxResponseBytesCeiling, Is.EqualTo(20_971_520));
        Assert.That(options.ToolLimits.ToolLoop!.UserMessageTimeoutSecondsCeiling, Is.EqualTo(3600));
    }

    [Test]
    public void EmptyConfig_LeavesOptionsNull()
    {
        var options = new AgentRunOptions();
        Assert.That(options.ToolDefaults, Is.Null);
        Assert.That(options.ToolLimits, Is.Null);
    }

    [Test]
    public void MalformedConfig_NonIntegerValue_ResolverFallsBackToEngineDefault()
    {
        // AC #10: malformed AgentRun:ToolDefaults must NOT crash the host. The
        // ToolLimitResolver catches the lazy-binder exception, logs once, and
        // falls back to engine defaults so the engine continues to operate.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRun:ToolDefaults:FetchUrl:MaxResponseBytes"] = "lots",
                ["AgentRun:ToolDefaults:FetchUrl:TimeoutSeconds"] = "soon"
            })
            .Build();

        // Mirror the production registration: services.Configure<AgentRunOptions>("AgentRun")
        var services = new ServiceCollection();
        services.Configure<AgentRunOptions>(config.GetSection("AgentRun"));
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AgentRunOptions>>();

        var resolver = new ToolLimitResolver(options);

        var step = new StepDefinition { Id = "s", Name = "S", Agent = "a.md" };
        var workflow = new WorkflowDefinition { Name = "T", Alias = "t", Steps = { step } };

        Assert.That(resolver.ResolveFetchUrlMaxResponseBytes(step, workflow),
            Is.EqualTo(EngineDefaults.FetchUrlMaxResponseBytes));
        Assert.That(resolver.ResolveFetchUrlTimeoutSeconds(step, workflow),
            Is.EqualTo(EngineDefaults.FetchUrlTimeoutSeconds));
    }

    [Test]
    public void MalformedConfig_NegativeValue_BindsButResolverSanitises()
    {
        // AC #10 + P3: a negative value in appsettings binds successfully (it's a
        // valid int). The resolver guards against non-positive site defaults at
        // read time so the tool stays usable.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRun:ToolDefaults:FetchUrl:MaxResponseBytes"] = "-100"
            })
            .Build();

        var options = new AgentRunOptions();
        Assert.DoesNotThrow(() => config.GetSection("AgentRun").Bind(options));
        Assert.That(options.ToolDefaults?.FetchUrl?.MaxResponseBytes, Is.EqualTo(-100));
        // Resolver behaviour for this is covered in ToolLimitResolverTests.SiteDefault_NonPositive_*.
    }
}
