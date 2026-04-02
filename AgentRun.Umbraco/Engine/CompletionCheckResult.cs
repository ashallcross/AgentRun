namespace AgentRun.Umbraco.Engine;

public sealed record CompletionCheckResult(
    bool Passed,
    IReadOnlyList<string> MissingFiles);
