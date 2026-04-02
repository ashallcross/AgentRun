namespace AgentRun.Umbraco.Engine;

public sealed record ArtifactValidationResult(
    bool Passed,
    IReadOnlyList<string> MissingFiles);
