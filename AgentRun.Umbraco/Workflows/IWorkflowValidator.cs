namespace AgentRun.Umbraco.Workflows;

public interface IWorkflowValidator
{
    WorkflowValidationResult Validate(string yamlContent);

    /// <summary>
    /// Enforces site-level hard ceilings on a parsed workflow's tool tuning values.
    /// Throws <see cref="WorkflowConfigurationException"/> on the first violation.
    /// Called by the workflow registry at load time after parsing succeeds.
    /// </summary>
    void EnforceCeilings(WorkflowDefinition workflow);
}
