namespace Shallai.UmbracoAgentRunner.Workflows;

public sealed class RegisteredWorkflow
{
    public string Alias { get; }

    public string FolderPath { get; }

    public WorkflowDefinition Definition { get; }

    public RegisteredWorkflow(string alias, string folderPath, WorkflowDefinition definition)
    {
        Alias = alias;
        FolderPath = folderPath;
        Definition = definition;
    }
}
