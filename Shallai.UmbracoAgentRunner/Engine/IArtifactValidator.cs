using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Engine;

public interface IArtifactValidator
{
    Task<ArtifactValidationResult> ValidateInputArtifactsAsync(
        StepDefinition step,
        string instanceFolderPath,
        CancellationToken cancellationToken);
}
