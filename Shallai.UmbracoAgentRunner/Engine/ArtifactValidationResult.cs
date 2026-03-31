namespace Shallai.UmbracoAgentRunner.Engine;

public sealed record ArtifactValidationResult(
    bool Passed,
    IReadOnlyList<string> MissingFiles);
