namespace Shallai.UmbracoAgentRunner.Engine;

public sealed record CompletionCheckResult(
    bool Passed,
    IReadOnlyList<string> MissingFiles);
