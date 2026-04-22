namespace AgentRun.Umbraco.Tests.Migrations;

[TestFixture]
public class CopyWorkflowSchemaToDiskTests
{
    [Test]
    public void SchemaResourceName_MatchesEmbeddedResource()
    {
        var assembly = typeof(AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        Assert.That(
            resourceNames,
            Has.Member(AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk.SchemaResourceName),
            "The schema-resource-name constant used by the migration must match an actual "
            + "embedded resource in the assembly. If the csproj's EmbeddedResource entry moves "
            + "or renames the schema, update CopyWorkflowSchemaToDisk.SchemaResourceName.");
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
            AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk.SchemaTargetRelativePath,
            Is.EqualTo(Path.Combine("Schemas", "workflow-schema.json")));
    }

    [Test]
    public async Task CopySchemaIfMissing_WritesSchemaToExpectedPath_WhenAbsent()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"agentrun-schema-migration-test-{Guid.NewGuid():N}");

        try
        {
            var assembly = typeof(AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk).Assembly;

            await AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk
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
            $"agentrun-schema-migration-test-{Guid.NewGuid():N}");

        try
        {
            var schemasDir = Path.Combine(
                tempRoot, "App_Data", "AgentRun.Umbraco", "Schemas");
            Directory.CreateDirectory(schemasDir);

            var schemaPath = Path.Combine(schemasDir, "workflow-schema.json");
            const string sentinelContent = "USER-MODIFIED SCHEMA SENTINEL";
            await File.WriteAllTextAsync(schemaPath, sentinelContent);

            var assembly = typeof(AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk).Assembly;

            await AgentRun.Umbraco.Migrations.CopyWorkflowSchemaToDisk
                .CopySchemaIfMissingAsync(tempRoot, assembly, logger: null);

            var resultContent = await File.ReadAllTextAsync(schemaPath);
            Assert.That(resultContent, Is.EqualTo(sentinelContent),
                "existing schema file must NOT be overwritten — preserves user modifications "
                + "(same skip-if-exists discipline as the workflows-exist skip in "
                + "CopyExampleWorkflowsToDisk).");
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
