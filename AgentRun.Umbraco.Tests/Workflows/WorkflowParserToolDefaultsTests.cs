using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

[TestFixture]
public class WorkflowParserToolDefaultsTests
{
    private WorkflowParser _parser = null!;

    [SetUp]
    public void SetUp() => _parser = new WorkflowParser();

    [Test]
    public void ToolDefaults_RoundTripsThroughDeserializer()
    {
        var yaml = """
            name: Test
            description: Test
            tool_defaults:
              fetch_url:
                max_response_bytes: 2097152
                timeout_seconds: 30
              tool_loop:
                user_message_timeout_seconds: 600
            steps:
              - id: scan
                name: Scan
                agent: agents/scan.md
                tool_overrides:
                  fetch_url:
                    max_response_bytes: 500000
            """;

        var def = _parser.Parse(yaml);

        Assert.That(def.ToolDefaults, Is.Not.Null);
        Assert.That(def.ToolDefaults!.FetchUrl, Is.Not.Null);
        Assert.That(def.ToolDefaults.FetchUrl!.MaxResponseBytes, Is.EqualTo(2_097_152));
        Assert.That(def.ToolDefaults.FetchUrl.TimeoutSeconds, Is.EqualTo(30));
        Assert.That(def.ToolDefaults.ToolLoop, Is.Not.Null);
        Assert.That(def.ToolDefaults.ToolLoop!.UserMessageTimeoutSeconds, Is.EqualTo(600));

        Assert.That(def.Steps[0].ToolOverrides, Is.Not.Null);
        Assert.That(def.Steps[0].ToolOverrides!.FetchUrl!.MaxResponseBytes, Is.EqualTo(500_000));
    }

    [Test]
    public void Workflow_Without_ToolDefaults_ParsesToNull()
    {
        var yaml = """
            name: Test
            description: Test
            steps:
              - id: scan
                name: Scan
                agent: agents/scan.md
            """;
        var def = _parser.Parse(yaml);
        Assert.That(def.ToolDefaults, Is.Null);
        Assert.That(def.Steps[0].ToolOverrides, Is.Null);
    }
}
