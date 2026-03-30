namespace Shallai.UmbracoAgentRunner.Workflows;

public interface IWorkflowParser
{
    WorkflowDefinition Parse(string yamlContent);
}
