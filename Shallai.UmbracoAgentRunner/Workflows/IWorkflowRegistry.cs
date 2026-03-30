namespace Shallai.UmbracoAgentRunner.Workflows;

public interface IWorkflowRegistry
{
    IReadOnlyList<RegisteredWorkflow> GetAllWorkflows();

    RegisteredWorkflow? GetWorkflow(string alias);

    Task LoadWorkflowsAsync(string workflowsRootPath, CancellationToken cancellationToken = default);
}
