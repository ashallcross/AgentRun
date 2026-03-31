using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Shallai.UmbracoAgentRunner.Configuration;
using Shallai.UmbracoAgentRunner.Workflows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shallai.UmbracoAgentRunner.Instances;

public sealed partial class InstanceManager : IInstanceManager
{
    private readonly string _dataRootPath;
    private readonly ILogger<InstanceManager> _logger;

    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    public InstanceManager(
        IOptions<AgentRunnerOptions> options,
        IWebHostEnvironment hostEnvironment,
        ILogger<InstanceManager> logger)
    {
        _logger = logger;

        var configuredPath = options.Value.DataRootPath ?? "App_Data/Shallai.UmbracoAgentRunner/instances/";
        _dataRootPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, configuredPath));

        // Normalise trailing separator
        if (!_dataRootPath.EndsWith(Path.DirectorySeparatorChar))
        {
            _dataRootPath += Path.DirectorySeparatorChar;
        }

        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Constructor for testing — accepts a resolved data root path directly.
    /// </summary>
    internal InstanceManager(string dataRootPath, ILogger<InstanceManager> logger)
    {
        _logger = logger;

        _dataRootPath = Path.GetFullPath(dataRootPath);
        if (!_dataRootPath.EndsWith(Path.DirectorySeparatorChar))
        {
            _dataRootPath += Path.DirectorySeparatorChar;
        }

        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<InstanceState> CreateInstanceAsync(
        string workflowAlias,
        WorkflowDefinition definition,
        string createdBy,
        CancellationToken cancellationToken)
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var state = new InstanceState
        {
            WorkflowAlias = workflowAlias,
            InstanceId = instanceId,
            CurrentStepIndex = 0,
            Status = InstanceStatus.Pending,
            Steps = definition.Steps.Select(s => new StepState
            {
                Id = s.Id,
                Status = StepStatus.Pending
            }).ToList(),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy
        };

        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        Directory.CreateDirectory(instanceDir);

        await WriteStateAtomicAsync(instanceDir, state, cancellationToken);

        _logger.LogInformation(
            "Created instance {InstanceId} for workflow {WorkflowAlias}",
            instanceId, workflowAlias);

        return state;
    }

    public async Task<InstanceState?> GetInstanceAsync(
        string workflowAlias,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        if (!File.Exists(yamlPath))
        {
            return null;
        }

        return await ReadStateAsync(yamlPath, cancellationToken);
    }

    public async Task<IReadOnlyList<InstanceState>> ListInstancesAsync(
        string? workflowAlias,
        CancellationToken cancellationToken)
    {
        var results = new List<InstanceState>();

        if (workflowAlias is not null)
        {
            var workflowDir = Path.Combine(_dataRootPath, workflowAlias);
            if (Directory.Exists(workflowDir))
            {
                await CollectInstancesFromDirectoryAsync(workflowDir, results, cancellationToken);
            }
        }
        else
        {
            if (Directory.Exists(_dataRootPath))
            {
                foreach (var workflowDir in Directory.GetDirectories(_dataRootPath))
                {
                    await CollectInstancesFromDirectoryAsync(workflowDir, results, cancellationToken);
                }
            }
        }

        return results;
    }

    public async Task<InstanceState> UpdateStepStatusAsync(
        string workflowAlias,
        string instanceId,
        int stepIndex,
        StepStatus status,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        var state = await ReadStateAsync(yamlPath, cancellationToken)
            ?? throw new InvalidOperationException($"Instance {instanceId} not found for workflow {workflowAlias}.");

        if (stepIndex < 0 || stepIndex >= state.Steps.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex),
                $"Step index {stepIndex} is out of range. Instance has {state.Steps.Count} steps.");
        }

        var step = state.Steps[stepIndex];
        step.Status = status;

        if (status == StepStatus.Active && step.StartedAt is null)
        {
            step.StartedAt = DateTime.UtcNow;
        }

        if (status == StepStatus.Complete)
        {
            step.CompletedAt = DateTime.UtcNow;
        }

        state.UpdatedAt = DateTime.UtcNow;

        await WriteStateAtomicAsync(instanceDir, state, cancellationToken);

        _logger.LogInformation(
            "Updated step {StepId} (index {StepIndex}) to {Status} for instance {InstanceId} of workflow {WorkflowAlias}",
            step.Id, stepIndex, status, instanceId, workflowAlias);

        return state;
    }

    public async Task<InstanceState> SetInstanceStatusAsync(
        string workflowAlias,
        string instanceId,
        InstanceStatus status,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        var state = await ReadStateAsync(yamlPath, cancellationToken)
            ?? throw new InvalidOperationException($"Instance {instanceId} not found for workflow {workflowAlias}.");

        if (status == InstanceStatus.Running && state.Status == InstanceStatus.Running)
        {
            throw new InvalidOperationException(
                $"Instance {instanceId} is already running. Concurrent execution is not permitted.");
        }

        state.Status = status;
        state.UpdatedAt = DateTime.UtcNow;

        await WriteStateAtomicAsync(instanceDir, state, cancellationToken);

        _logger.LogInformation(
            "Set instance {InstanceId} status to {Status} for workflow {WorkflowAlias}",
            instanceId, status, workflowAlias);

        return state;
    }

    public async Task<bool> DeleteInstanceAsync(
        string workflowAlias,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        if (!File.Exists(yamlPath))
        {
            return false;
        }

        var state = await ReadStateAsync(yamlPath, cancellationToken);
        if (state is null)
        {
            return false;
        }

        if (state.Status is not (InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled))
        {
            throw new InvalidOperationException(
                $"Cannot delete instance {instanceId} with status {state.Status}. Only completed, failed, or cancelled instances can be deleted.");
        }

        Directory.Delete(instanceDir, recursive: true);

        _logger.LogInformation(
            "Deleted instance {InstanceId} for workflow {WorkflowAlias}",
            instanceId, workflowAlias);

        return true;
    }

    public async Task<InstanceState?> FindInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        if (!InstanceIdRegex().IsMatch(instanceId))
        {
            return null;
        }

        if (!Directory.Exists(_dataRootPath))
        {
            return null;
        }

        foreach (var workflowDir in Directory.GetDirectories(_dataRootPath))
        {
            var instanceDir = Path.GetFullPath(Path.Combine(workflowDir, instanceId));
            if (!instanceDir.StartsWith(_dataRootPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var yamlPath = Path.Combine(instanceDir, "instance.yaml");

            if (File.Exists(yamlPath))
            {
                try
                {
                    return await ReadStateAsync(yamlPath, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to read instance state from {Path}, skipping", yamlPath);
                    return null;
                }
            }
        }

        return null;
    }

    public async Task<InstanceState> AdvanceStepAsync(
        string workflowAlias,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        var state = await ReadStateAsync(yamlPath, cancellationToken)
            ?? throw new InvalidOperationException($"Instance {instanceId} not found for workflow {workflowAlias}.");

        if (state.CurrentStepIndex >= state.Steps.Count - 1)
        {
            throw new InvalidOperationException(
                $"Cannot advance step index for instance {instanceId}: already on the last step (index {state.CurrentStepIndex} of {state.Steps.Count}).");
        }

        state.CurrentStepIndex++;
        state.UpdatedAt = DateTime.UtcNow;

        await WriteStateAtomicAsync(instanceDir, state, cancellationToken);

        _logger.LogInformation(
            "Advanced CurrentStepIndex to {StepIndex} for instance {InstanceId} of workflow {WorkflowAlias}",
            state.CurrentStepIndex, instanceId, workflowAlias);

        return state;
    }

    public string GetInstanceFolderPath(string workflowAlias, string instanceId)
        => GetInstanceDirectory(workflowAlias, instanceId);

    [System.Text.RegularExpressions.GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial System.Text.RegularExpressions.Regex InstanceIdRegex();

    private string GetInstanceDirectory(string workflowAlias, string instanceId)
    {
        var combined = Path.GetFullPath(Path.Combine(_dataRootPath, workflowAlias, instanceId));
        if (!combined.StartsWith(_dataRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Path traversal detected. The resolved path escapes the data root.");
        }

        return combined;
    }

    private async Task WriteStateAtomicAsync(
        string instanceDir,
        InstanceState state,
        CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(instanceDir, "instance.yaml");
        var tmpPath = targetPath + ".tmp";

        var yaml = _yamlSerializer.Serialize(state);
        await File.WriteAllTextAsync(tmpPath, yaml, cancellationToken);
        File.Move(tmpPath, targetPath, overwrite: true);
    }

    private async Task<InstanceState?> ReadStateAsync(
        string yamlPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(yamlPath))
        {
            return null;
        }

        var yaml = await File.ReadAllTextAsync(yamlPath, cancellationToken);
        return _yamlDeserializer.Deserialize<InstanceState>(yaml);
    }

    private async Task CollectInstancesFromDirectoryAsync(
        string workflowDir,
        List<InstanceState> results,
        CancellationToken cancellationToken)
    {
        foreach (var instanceDir in Directory.GetDirectories(workflowDir))
        {
            var yamlPath = Path.Combine(instanceDir, "instance.yaml");
            if (File.Exists(yamlPath))
            {
                try
                {
                    var state = await ReadStateAsync(yamlPath, cancellationToken);
                    if (state is not null)
                    {
                        results.Add(state);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to read instance state from {Path}, skipping", yamlPath);
                }
            }
        }
    }
}
