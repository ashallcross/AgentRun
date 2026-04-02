using AgentRun.Umbraco.Configuration;

namespace AgentRun.Umbraco.Tests.Configuration;

[TestFixture]
public class AgentRunOptionsTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var options = new AgentRunOptions();

        Assert.That(options.DataRootPath, Is.EqualTo("App_Data/AgentRun.Umbraco/instances/"));
        Assert.That(options.DefaultProfile, Is.EqualTo(string.Empty));
        Assert.That(options.WorkflowPath, Is.EqualTo("App_Data/AgentRun.Umbraco/workflows/"));
    }

    [Test]
    public void Properties_CanBeSet()
    {
        var options = new AgentRunOptions
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
