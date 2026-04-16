namespace AgentRun.Umbraco.Workflows;

public interface IWorkflowParser
{
    WorkflowDefinition Parse(string yamlContent);
}
