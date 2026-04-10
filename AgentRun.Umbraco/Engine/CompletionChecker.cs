using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public sealed class CompletionChecker : ICompletionChecker
{
    private readonly ILogger<CompletionChecker> _logger;

    public CompletionChecker(ILogger<CompletionChecker> logger)
    {
        _logger = logger;
    }

    public Task<CompletionCheckResult> CheckAsync(
        CompletionCheckDefinition? check,
        string instanceFolderPath,
        CancellationToken cancellationToken)
    {
        if (check is null || check.FilesExist.Count == 0)
        {
            return Task.FromResult(new CompletionCheckResult(true, []));
        }

        var missingFiles = new List<string>();
        var canonicalInstanceFolder = Path.GetFullPath(instanceFolderPath);

        foreach (var file in check.FilesExist)
        {
            var fullPath = Path.Combine(instanceFolderPath, file);
            var canonicalPath = Path.GetFullPath(fullPath);
            if (!canonicalPath.StartsWith(
                    canonicalInstanceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Access denied: completion check path '{file}' resolves outside the instance folder");
            }

            _logger.LogDebug("Checking completion file: {FilePath}", fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Completion check file missing: {FilePath}", file);
                missingFiles.Add(file);
            }
        }

        var result = new CompletionCheckResult(missingFiles.Count == 0, missingFiles);
        return Task.FromResult(result);
    }
}
