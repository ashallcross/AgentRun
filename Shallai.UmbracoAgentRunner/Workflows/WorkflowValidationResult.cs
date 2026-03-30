namespace Shallai.UmbracoAgentRunner.Workflows;

public sealed class WorkflowValidationResult
{
    public IReadOnlyList<WorkflowValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public WorkflowValidationResult(IReadOnlyList<WorkflowValidationError> errors)
    {
        Errors = errors;
    }

    public static WorkflowValidationResult Success() => new([]);
}
