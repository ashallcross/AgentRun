using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace AgentRun.Umbraco.Migrations;

/// <summary>
/// Migration that copies example workflow files from embedded resources to disk.
/// Writes to <c>App_Data/AgentRun.Umbraco/workflows/</c>. Skips any workflow folder
/// that already exists to avoid overwriting user modifications.
/// </summary>
public class CopyExampleWorkflowsToDisk : AsyncMigrationBase
{
    private static readonly string[] WorkflowFolders =
    [
        "content-quality-audit",
        "accessibility-quick-scan",
        "umbraco-content-audit"
    ];

    /// <summary>
    /// Embedded resource prefix. Files at <c>Workflows/{folder}/...</c> become
    /// <c>AgentRun.Umbraco.Workflows.{folder_with_underscores}.{filename}</c>.
    /// </summary>
    private const string ResourcePrefix = "AgentRun.Umbraco.Workflows.";

    /// <summary>
    /// Embedded-resource name for the workflow JSON schema. Must match the csproj
    /// <c>&lt;EmbeddedResource Include="Schemas\workflow-schema.json" /&gt;</c> entry.
    /// </summary>
    public const string SchemaResourceName = "AgentRun.Umbraco.Schemas.workflow-schema.json";

    /// <summary>
    /// Relative path under <c>App_Data/AgentRun.Umbraco/</c> where the schema lands.
    /// Matches the <c>yaml-language-server: $schema=../../Schemas/workflow-schema.json</c>
    /// directive shipped at the top of every example workflow.yaml — that directive,
    /// resolved from <c>App_Data/AgentRun.Umbraco/workflows/&lt;name&gt;/workflow.yaml</c>,
    /// expects the schema at <c>App_Data/AgentRun.Umbraco/Schemas/workflow-schema.json</c>.
    /// </summary>
    public static readonly string SchemaTargetRelativePath = Path.Combine("Schemas", "workflow-schema.json");

    private readonly IHostEnvironment _hostEnvironment;

    public CopyExampleWorkflowsToDisk(IMigrationContext context, IHostEnvironment hostEnvironment)
        : base(context)
    {
        _hostEnvironment = hostEnvironment;
    }

    protected override async Task MigrateAsync()
    {
        var contentRoot = _hostEnvironment.ContentRootPath;
        var workflowsRoot = Path.Combine(contentRoot, "App_Data", "AgentRun.Umbraco", "workflows");

        var assembly = Assembly.GetExecutingAssembly();
        var allResourceNames = assembly.GetManifestResourceNames();

        foreach (var folder in WorkflowFolders)
        {
            var targetFolder = Path.Combine(workflowsRoot, folder);

            if (Directory.Exists(targetFolder))
            {
                Logger.LogDebug("Workflow folder '{Folder}' already exists — skipping", folder);
                continue;
            }

            // Embedded resources use underscores for hyphens in folder names
            var resourceFolder = folder.Replace('-', '_');
            var prefix = $"{ResourcePrefix}{resourceFolder}.";

            var matchingResources = allResourceNames
                .Where(r => r.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            if (matchingResources.Count == 0)
            {
                Logger.LogWarning("No embedded resources found for workflow '{Folder}' with prefix '{Prefix}'", folder, prefix);
                continue;
            }

            Directory.CreateDirectory(targetFolder);

            foreach (var resourceName in matchingResources)
            {
                var relativePath = ResourceNameToRelativePath(resourceName, prefix, folder);
                var targetPath = Path.Combine(workflowsRoot, relativePath);

                var targetDir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(targetDir);

                await using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    Logger.LogWarning("Embedded resource '{Resource}' could not be read", resourceName);
                    continue;
                }

                // Atomic write: .tmp + File.Move
                var tmpPath = $"{targetPath}.tmp";
                await using (var fileStream = File.Create(tmpPath))
                {
                    await stream.CopyToAsync(fileStream);
                }

                File.Move(tmpPath, targetPath, overwrite: true);
            }

            Logger.LogInformation("Copied {Count} files for workflow '{Folder}'", matchingResources.Count, folder);
        }

        // Copy the JSON schema so the `yaml-language-server: $schema=../../Schemas/...`
        // directive at the top of every shipped workflow.yaml resolves to a real file
        // on disk. Without this, adopters see "Unable to load schema: No content" in
        // the IDE + dotnet output on first install. Kept as a separate step from the
        // workflow loop because the schema lives under a different resource prefix
        // (Schemas. vs Workflows.) and the skip-if-exists discipline preserves any
        // user-modified copy.
        await CopySchemaIfMissingAsync(contentRoot, assembly, Logger);
    }

    /// <summary>
    /// Copies <c>Schemas/workflow-schema.json</c> from embedded resources to
    /// <c>App_Data/AgentRun.Umbraco/Schemas/workflow-schema.json</c> on the consumer's
    /// disk. No-op if the target file already exists — preserves user modifications,
    /// same discipline as the workflow-folder skip in <see cref="MigrateAsync" />.
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

    /// <summary>
    /// Converts an embedded resource name back to a relative file path under the workflow folder.
    /// E.g. <c>AgentRun.Umbraco.Workflows.content_quality_audit.agents.scanner.md</c>
    /// becomes <c>content-quality-audit/agents/scanner.md</c>.
    /// </summary>
    internal static string ResourceNameToRelativePath(string resourceName, string prefix, string folder)
    {
        // Strip the prefix: "agents.scanner.md"
        var remainder = resourceName[prefix.Length..];

        // The last segment with a dot-extension is the filename.
        // Everything before it is subfolder structure (dot-separated).
        // "agents.scanner.md" → subfolder "agents", file "scanner.md"
        var parts = remainder.Split('.');

        // Find the file extension boundary: the last dot before a known extension
        // or fall back to treating the last two segments as "name.ext"
        if (parts.Length >= 3)
        {
            // e.g. ["agents", "scanner", "md"] → subfolder = "agents", file = "scanner.md"
            var subfolders = string.Join(Path.DirectorySeparatorChar.ToString(), parts[..^2]);
            var fileName = $"{parts[^2]}.{parts[^1]}";
            return Path.Combine(folder, subfolders, fileName);
        }

        if (parts.Length == 2)
        {
            // e.g. ["workflow", "yaml"] → file = "workflow.yaml"
            return Path.Combine(folder, $"{parts[0]}.{parts[1]}");
        }

        // Fallback — shouldn't happen with well-formed resources
        return Path.Combine(folder, remainder);
    }
}
