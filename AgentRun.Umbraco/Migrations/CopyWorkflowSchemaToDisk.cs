using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace AgentRun.Umbraco.Migrations;

/// <summary>
/// Migration that copies the workflow JSON schema from embedded resources to
/// <c>App_Data/AgentRun.Umbraco/Schemas/workflow-schema.json</c>.
/// </summary>
/// <remarks>
/// Shipped as a standalone migration (separate from <see cref="CopyExampleWorkflowsToDisk"/>)
/// with its own GUID so that adopters upgrading from v1.0 or v1.1 — where
/// <c>CopyExampleWorkflowsToDisk</c> has already run and Umbraco's migration runner
/// treats that GUID as already-applied — still pick up the schema on next startup. On
/// fresh installs the plan runs both migrations in order.
///
/// The target path matches the <c>yaml-language-server: $schema=../../Schemas/workflow-schema.json</c>
/// directive at the top of every shipped workflow.yaml — resolving from
/// <c>App_Data/AgentRun.Umbraco/workflows/&lt;name&gt;/workflow.yaml</c> lands exactly
/// at the write target. Without this migration, IDE YAML validation fails with
/// "Unable to load schema: No content" on every adopter install.
/// </remarks>
public class CopyWorkflowSchemaToDisk : AsyncMigrationBase
{
    /// <summary>
    /// Embedded-resource name for the workflow JSON schema. Must match the csproj
    /// <c>&lt;EmbeddedResource Include="Schemas\workflow-schema.json" /&gt;</c> entry.
    /// </summary>
    public const string SchemaResourceName = "AgentRun.Umbraco.Schemas.workflow-schema.json";

    /// <summary>
    /// Relative path under <c>App_Data/AgentRun.Umbraco/</c> where the schema lands.
    /// Matches the yaml-language-server directive path every example workflow.yaml ships with.
    /// </summary>
    public static readonly string SchemaTargetRelativePath = Path.Combine("Schemas", "workflow-schema.json");

    private readonly IHostEnvironment _hostEnvironment;

    public CopyWorkflowSchemaToDisk(IMigrationContext context, IHostEnvironment hostEnvironment)
        : base(context)
    {
        _hostEnvironment = hostEnvironment;
    }

    protected override async Task MigrateAsync()
    {
        var contentRoot = _hostEnvironment.ContentRootPath;
        var assembly = Assembly.GetExecutingAssembly();

        await CopySchemaIfMissingAsync(contentRoot, assembly, Logger);
    }

    /// <summary>
    /// Copies <c>Schemas/workflow-schema.json</c> from embedded resources to
    /// <c>App_Data/AgentRun.Umbraco/Schemas/workflow-schema.json</c> on the consumer's
    /// disk. No-op if the target file already exists — preserves user modifications.
    /// </summary>
    /// <remarks>
    /// Public + static so it can be unit-tested against a temp content root without
    /// instantiating the full migration + its IMigrationContext dependency.
    /// </remarks>
    public static async Task CopySchemaIfMissingAsync(
        string contentRoot,
        Assembly assembly,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var targetPath = Path.Combine(
            contentRoot, "App_Data", "AgentRun.Umbraco", SchemaTargetRelativePath);

        if (File.Exists(targetPath))
        {
            logger?.LogDebug(
                "Schema file already exists at {Path} — skipping to preserve user modifications",
                targetPath);
            return;
        }

        await using var stream = assembly.GetManifestResourceStream(SchemaResourceName);
        if (stream is null)
        {
            logger?.LogWarning(
                "Embedded resource '{Resource}' could not be read — workflow-schema.json will "
                + "not be available for IDE validation on this install",
                SchemaResourceName);
            return;
        }

        var targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);

        var tmpPath = $"{targetPath}.tmp";
        await using (var fileStream = File.Create(tmpPath))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        File.Move(tmpPath, targetPath, overwrite: true);

        logger?.LogInformation(
            "Copied workflow-schema.json to App_Data/AgentRun.Umbraco/Schemas/ for IDE validation");
    }
}
