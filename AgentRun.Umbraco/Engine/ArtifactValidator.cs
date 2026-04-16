using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public sealed class ArtifactValidator : IArtifactValidator
{
    private readonly ILogger<ArtifactValidator> _logger;

    public ArtifactValidator(ILogger<ArtifactValidator> logger)
    {
        _logger = logger;
    }

    public Task<ArtifactValidationResult> ValidateInputArtifactsAsync(
        StepDefinition step,
        string instanceFolderPath,
        CancellationToken cancellationToken)
    {
        if (step.ReadsFrom is null || step.ReadsFrom.Count == 0)
        {
            return Task.FromResult(new ArtifactValidationResult(true, []));
        }

        var missingFiles = new List<string>();

        var canonicalInstanceFolder = Path.GetFullPath(instanceFolderPath);

        foreach (var file in step.ReadsFrom)
        {
            var fullPath = Path.Combine(instanceFolderPath, file);
            var canonicalPath = Path.GetFullPath(fullPath);
            if (!canonicalPath.StartsWith(
                    canonicalInstanceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Access denied: artifact path '{file}' resolves outside the instance folder");
            }

            _logger.LogDebug("Checking input artifact: {FilePath}", fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Input artifact missing: {FilePath}", file);
                missingFiles.Add(file);
            }
        }

        var result = new ArtifactValidationResult(missingFiles.Count == 0, missingFiles);
        return Task.FromResult(result);
    }
}
