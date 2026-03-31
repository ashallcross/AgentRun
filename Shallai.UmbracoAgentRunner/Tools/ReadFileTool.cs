using Shallai.UmbracoAgentRunner.Security;

namespace Shallai.UmbracoAgentRunner.Tools;

public class ReadFileTool : IWorkflowTool
{
    public string Name => "read_file";

    public string Description => "Reads the contents of a file within the instance folder";

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = ExtractStringArgument(arguments, "path");
        var canonicalPath = ValidatePathSandboxed(path, context.InstanceFolderPath);

        if (!File.Exists(canonicalPath))
        {
            var relativePath = PathSandbox.GetRelativePath(canonicalPath, context.InstanceFolderPath);
            throw new ToolExecutionException($"File not found: '{relativePath}'");
        }

        return await File.ReadAllTextAsync(canonicalPath, cancellationToken);
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

    private static string ExtractStringArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        var stringValue = value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString()!,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };

        if (string.IsNullOrWhiteSpace(stringValue))
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        return stringValue;
    }
}
