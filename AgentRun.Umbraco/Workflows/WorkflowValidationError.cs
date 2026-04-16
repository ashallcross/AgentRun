namespace AgentRun.Umbraco.Workflows;

public sealed class WorkflowValidationError
{
    public string FieldPath { get; }

    public string Message { get; }

    public WorkflowValidationError(string fieldPath, string message)
    {
        FieldPath = fieldPath;
        Message = message;
    }
}
