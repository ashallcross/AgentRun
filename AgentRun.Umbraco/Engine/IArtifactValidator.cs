using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public interface IArtifactValidator
{
    Task<ArtifactValidationResult> ValidateInputArtifactsAsync(
        StepDefinition step,
        string instanceFolderPath,
        CancellationToken cancellationToken);
}
