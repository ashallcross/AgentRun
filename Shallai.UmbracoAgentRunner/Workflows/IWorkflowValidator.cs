namespace Shallai.UmbracoAgentRunner.Workflows;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(string yamlContent);
}
