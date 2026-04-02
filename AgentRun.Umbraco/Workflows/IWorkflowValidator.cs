namespace AgentRun.Umbraco.Workflows;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(string yamlContent);
}
