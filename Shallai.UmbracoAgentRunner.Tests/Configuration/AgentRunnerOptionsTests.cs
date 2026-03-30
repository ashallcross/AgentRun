using Shallai.UmbracoAgentRunner.Configuration;

namespace Shallai.UmbracoAgentRunner.Tests.Configuration;

[TestFixture]
public class AgentRunnerOptionsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var options = new AgentRunnerOptions();

        Assert.That(options.DataRootPath, Is.EqualTo("App_Data/Shallai.UmbracoAgentRunner/instances/"));
        Assert.That(options.DefaultProfile, Is.EqualTo(string.Empty));
        Assert.That(options.WorkflowPath, Is.EqualTo("App_Data/Shallai.UmbracoAgentRunner/workflows/"));
    }

    [Test]
    public void Properties_CanBeSet()
    {
        var options = new AgentRunnerOptions
        {
            DataRootPath = "/custom/path/",
            DefaultProfile = "my-profile",
            WorkflowPath = "/workflows/"
        };

        Assert.That(options.DataRootPath, Is.EqualTo("/custom/path/"));
        Assert.That(options.DefaultProfile, Is.EqualTo("my-profile"));
        Assert.That(options.WorkflowPath, Is.EqualTo("/workflows/"));
    }
}
