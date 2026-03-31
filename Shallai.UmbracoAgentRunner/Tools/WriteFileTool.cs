using Shallai.UmbracoAgentRunner.Security;

namespace Shallai.UmbracoAgentRunner.Tools;

public class WriteFileTool : IWorkflowTool
{
    public string Name => "write_file";

    public string Description => "Writes content to a file within the instance folder";

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = ExtractStringArgument(arguments, "path");
        var content = ExtractStringArgument(arguments, "content", allowEmpty: true);
        var canonicalPath = ValidatePathSandboxed(path, context.InstanceFolderPath);

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);

        var tmpPath = canonicalPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, content, cancellationToken);
            File.Move(tmpPath, canonicalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); }
                catch { /* best effort cleanup */ }
            }
        }

        var relativePath = PathSandbox.GetRelativePath(canonicalPath, context.InstanceFolderPath);
        return $"File written: '{relativePath}'";
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

    private static string ExtractStringArgument(IDictionary<string, object?> arguments, string name, bool allowEmpty = false)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        var stringValue = value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString()!,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };

        if (!allowEmpty && string.IsNullOrWhiteSpace(stringValue))
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        return stringValue;
    }
}
