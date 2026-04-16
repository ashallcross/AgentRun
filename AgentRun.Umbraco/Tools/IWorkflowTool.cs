using System.Text.Json;

namespace AgentRun.Umbraco.Tools;

public interface IWorkflowTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// JSON Schema describing the tool's parameters. Used to generate tool declarations
    /// for the LLM. Return null to declare a tool with no parameters.
    /// </summary>
    JsonElement? ParameterSchema => null;

    Task<object> ExecuteAsync(IDictionary<string, object?> arguments,
        ToolExecutionContext context, CancellationToken cancellationToken);
}
