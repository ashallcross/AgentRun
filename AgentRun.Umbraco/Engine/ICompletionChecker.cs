using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public interface ICompletionChecker
{
    Task<CompletionCheckResult> CheckAsync(
        CompletionCheckDefinition? check,
        string instanceFolderPath,
        CancellationToken cancellationToken);
}
