using Microsoft.Extensions.Configuration;
using AgentRun.Umbraco.Configuration;

namespace AgentRun.Umbraco.Tests.Configuration;

[TestFixture]
public class AgentRunWebSearchOptionsTests
{
    [Test]
    public void Defaults_AreSetCorrectly()
    {
        var options = new AgentRunWebSearchOptions();

        Assert.That(options.DefaultProvider, Is.Null);
        Assert.That(options.CacheTtl, Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(options.Providers, Is.Not.Null);
        Assert.That(options.Providers, Is.Empty);
    }

    [Test]
    public void ConfigurationBinding_NestedProvidersShape_RoundTrips()
    {
        var json = """
            {
              "AgentRun": {
                "WebSearch": {
                  "DefaultProvider": "Brave",
                  "CacheTtl": "01:30:00",
                  "Providers": {
                    "Brave":  { "ApiKey": "brave-secret" },
                    "Tavily": { "ApiKey": "tavily-secret" }
                  }
                }
              }
            }
            """;
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var options = new AgentRunOptions();
        config.GetSection("AgentRun").Bind(options);

        Assert.That(options.WebSearch, Is.Not.Null);
        Assert.That(options.WebSearch!.DefaultProvider, Is.EqualTo("Brave"));
        Assert.That(options.WebSearch.CacheTtl, Is.EqualTo(TimeSpan.FromMinutes(90)));
        Assert.That(options.WebSearch.Providers, Has.Count.EqualTo(2));
        Assert.That(options.WebSearch.Providers["Brave"].ApiKey, Is.EqualTo("brave-secret"));
        Assert.That(options.WebSearch.Providers["Tavily"].ApiKey, Is.EqualTo("tavily-secret"));
    }

    [Test]
    public void ConfigurationBinding_IsCaseInsensitive_ForProviderNameLookup()
    {
        var json = """
            {
              "AgentRun": {
                "WebSearch": {
                  "Providers": {
                    "Brave": { "ApiKey": "secret" }
                  }
                }
              }
            }
            """;
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var options = new AgentRunOptions();
        config.GetSection("AgentRun").Bind(options);

        Assert.That(options.WebSearch!.Providers.ContainsKey("Brave"), Is.True);
        Assert.That(options.WebSearch.Providers.ContainsKey("brave"), Is.True,
            "dictionary comparer should be OrdinalIgnoreCase so env-var overrides with any casing resolve");
    }
}
