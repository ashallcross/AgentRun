namespace AgentRun.Umbraco.Tests.Migrations;

[TestFixture]
public class CopyExampleWorkflowsToDiskTests
{
    [Test]
    public void ResourceNameToRelativePath_WorkflowYaml_ReturnsCorrectPath()
    {
        var result = AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk.ResourceNameToRelativePath(
            "AgentRun.Umbraco.Workflows.content_quality_audit.workflow.yaml",
            "AgentRun.Umbraco.Workflows.content_quality_audit.",
            "content-quality-audit");

        Assert.That(result, Is.EqualTo(Path.Combine("content-quality-audit", "workflow.yaml")));
    }

    [Test]
    public void ResourceNameToRelativePath_AgentPrompt_ReturnsCorrectPath()
    {
        var result = AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk.ResourceNameToRelativePath(
            "AgentRun.Umbraco.Workflows.content_quality_audit.agents.scanner.md",
            "AgentRun.Umbraco.Workflows.content_quality_audit.",
            "content-quality-audit");

        Assert.That(result, Is.EqualTo(Path.Combine("content-quality-audit", "agents", "scanner.md")));
    }

    [Test]
    public void ResourceNameToRelativePath_DeeplyNested_ReturnsCorrectPath()
    {
        var result = AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk.ResourceNameToRelativePath(
            "AgentRun.Umbraco.Workflows.content_quality_audit.agents.reporter.md",
            "AgentRun.Umbraco.Workflows.content_quality_audit.",
            "content-quality-audit");

        Assert.That(result, Is.EqualTo(Path.Combine("content-quality-audit", "agents", "reporter.md")));
    }

    [Test]
    public void EmbeddedResources_ContainAllThreeWorkflows()
    {
        var assembly = typeof(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        Assert.That(resourceNames, Has.Some.StartsWith("AgentRun.Umbraco.Workflows.content_quality_audit."));
        Assert.That(resourceNames, Has.Some.StartsWith("AgentRun.Umbraco.Workflows.accessibility_quick_scan."));
        Assert.That(resourceNames, Has.Some.StartsWith("AgentRun.Umbraco.Workflows.umbraco_content_audit."));
    }

    [Test]
    public void EmbeddedResources_EachWorkflowHasWorkflowYaml()
    {
        var assembly = typeof(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        Assert.That(resourceNames, Has.Some.EndsWith("content_quality_audit.workflow.yaml"));
        Assert.That(resourceNames, Has.Some.EndsWith("accessibility_quick_scan.workflow.yaml"));
        Assert.That(resourceNames, Has.Some.EndsWith("umbraco_content_audit.workflow.yaml"));
    }

    [Test]
    public void EmbeddedResources_EachWorkflowHasAgentPrompts()
    {
        var assembly = typeof(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        // Content Quality Audit has 3 agents
        Assert.That(resourceNames, Has.Some.Contain("content_quality_audit.agents.scanner.md"));
        Assert.That(resourceNames, Has.Some.Contain("content_quality_audit.agents.analyser.md"));
        Assert.That(resourceNames, Has.Some.Contain("content_quality_audit.agents.reporter.md"));

        // Accessibility has 2 agents
        Assert.That(resourceNames, Has.Some.Contain("accessibility_quick_scan.agents.scanner.md"));
        Assert.That(resourceNames, Has.Some.Contain("accessibility_quick_scan.agents.reporter.md"));

        // Content Audit has 3 agents
        Assert.That(resourceNames, Has.Some.Contain("umbraco_content_audit.agents.scanner.md"));
        Assert.That(resourceNames, Has.Some.Contain("umbraco_content_audit.agents.analyser.md"));
        Assert.That(resourceNames, Has.Some.Contain("umbraco_content_audit.agents.reporter.md"));
    }
}
