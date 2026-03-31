using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Engine;

public interface ICompletionChecker
{
    Task<CompletionCheckResult> CheckAsync(
        CompletionCheckDefinition? check,
        string instanceFolderPath,
        CancellationToken cancellationToken);
}
