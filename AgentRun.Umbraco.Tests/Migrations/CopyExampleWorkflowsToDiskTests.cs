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

    [Test]
    public void SchemaResourceName_MatchesEmbeddedResource()
    {
        var assembly = typeof(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        Assert.That(
            resourceNames,
            Has.Member(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk.SchemaResourceName),
            "The schema-resource-name constant used by the migration must match an actual "
            + "embedded resource in the assembly. If the csproj's EmbeddedResource entry moves "
            + "or renames the schema, update CopyExampleWorkflowsToDisk.SchemaResourceName.");
    }

    [Test]
    public void SchemaTargetRelativePath_LandsAtAppDataSchemasWorkflowSchemaJson()
    {
        // The yaml-language-server directive at the top of every workflow.yaml is
        // `$schema=../../Schemas/workflow-schema.json` — relative to a workflow at
        // App_Data/AgentRun.Umbraco/workflows/<name>/workflow.yaml that resolves to
        // App_Data/AgentRun.Umbraco/Schemas/workflow-schema.json. The migration copies
        // the schema to exactly that location so IDE validation works on a fresh install.
        Assert.That(
            AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk.SchemaTargetRelativePath,
            Is.EqualTo(Path.Combine("Schemas", "workflow-schema.json")));
    }

    [Test]
    public async Task CopySchemaIfMissing_WritesSchemaToExpectedPath_WhenAbsent()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"agentrun-migration-test-{Guid.NewGuid():N}");

        try
        {
            var assembly = typeof(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk).Assembly;

            await AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk
                .CopySchemaIfMissingAsync(tempRoot, assembly, logger: null);

            var expectedPath = Path.Combine(
                tempRoot, "App_Data", "AgentRun.Umbraco",
                "Schemas", "workflow-schema.json");

            Assert.That(File.Exists(expectedPath), Is.True, "schema file should exist after copy");
            var content = await File.ReadAllTextAsync(expectedPath);
            Assert.That(content, Does.Contain("\"$schema\""), "content should be the JSON schema");
            Assert.That(content, Does.Contain("AgentRun.Umbraco Workflow Definition"),
                "content should be the shipped schema (look at the title field at line 3)");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task CopySchemaIfMissing_IsNoOp_WhenSchemaAlreadyExists()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"agentrun-migration-test-{Guid.NewGuid():N}");

        try
        {
            var schemasDir = Path.Combine(
                tempRoot, "App_Data", "AgentRun.Umbraco", "Schemas");
            Directory.CreateDirectory(schemasDir);

            var schemaPath = Path.Combine(schemasDir, "workflow-schema.json");
            const string sentinelContent = "USER-MODIFIED SCHEMA SENTINEL";
            await File.WriteAllTextAsync(schemaPath, sentinelContent);

            var assembly = typeof(AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk).Assembly;

            await AgentRun.Umbraco.Migrations.CopyExampleWorkflowsToDisk
                .CopySchemaIfMissingAsync(tempRoot, assembly, logger: null);

            var resultContent = await File.ReadAllTextAsync(schemaPath);
            Assert.That(resultContent, Is.EqualTo(sentinelContent),
                "existing schema file must NOT be overwritten — preserves user modifications "
                + "(same discipline as the workflows-exist skip at CopyExampleWorkflowsToDisk.cs:48-52)");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
