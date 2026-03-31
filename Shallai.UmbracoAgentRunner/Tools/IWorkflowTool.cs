namespace Shallai.UmbracoAgentRunner.Tools;

public interface IWorkflowTool
{
    string Name { get; }

    string Description { get; }

    Task<object> ExecuteAsync(IDictionary<string, object?> arguments,
        ToolExecutionContext context, CancellationToken cancellationToken);
}
