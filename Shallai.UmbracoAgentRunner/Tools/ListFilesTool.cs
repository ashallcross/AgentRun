using System.Text.Json;
using Shallai.UmbracoAgentRunner.Security;

namespace Shallai.UmbracoAgentRunner.Tools;

public class ListFilesTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "Relative path to the directory within the instance folder. Omit or leave empty to list from root." }
            }
        }
        """).RootElement;

    public string Name => "list_files";

    public string Description => "Lists files within a directory in the instance folder";

    public JsonElement? ParameterSchema => Schema;

    public Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = ExtractOptionalStringArgument(arguments, "path") ?? "";

        string canonicalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            // Validate the instance root itself (symlink check) without going through
            // PathSandbox.ValidatePath which rejects empty paths
            canonicalPath = Path.GetFullPath(context.InstanceFolderPath);
            if (PathSandbox.IsPathOrAncestorSymlink(canonicalPath))
            {
                throw new ToolExecutionException("Access denied: symbolic links are not permitted");
            }
        }
        else
        {
            canonicalPath = ValidatePathSandboxed(path, context.InstanceFolderPath);
        }

        if (!Directory.Exists(canonicalPath))
        {
            var relativePath = PathSandbox.GetRelativePath(canonicalPath, context.InstanceFolderPath);
            throw new ToolExecutionException($"Directory not found: '{relativePath}'");
        }

        var files = Directory.EnumerateFiles(canonicalPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(canonicalPath, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var result = string.Join('\n', files);
        return Task.FromResult<object>(result);
    }

    private static string ValidatePathSandboxed(string path, string instanceFolderPath)
    {
        try
        {
            return PathSandbox.ValidatePath(path, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            throw new ToolExecutionException(ex.Message);
        }
    }

    private static string? ExtractOptionalStringArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            return null;

        return value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString(),
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };
    }
}
