using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Workflows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentRun.Umbraco.Instances;

public sealed partial class InstanceManager : IInstanceManager
{
    private readonly string _dataRootPath;
    private readonly ILogger<InstanceManager> _logger;

    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    // Story 10.1: per-instance SemaphoreSlim serialises read-modify-write on the
    // locked mutation methods (SetInstanceStatusAsync, UpdateStepStatusAsync,
    // AdvanceStepAsync, DeleteInstanceAsync). Pure reads are unlocked (file-level
    // File.Move atomicity is sufficient — AC4). Lock dict is never pruned in v1
    // (AC13): size is bounded by total instance count in process lifetime, each
    // lock is ≈ 96 bytes, 10k instances < 1 MB. Pruning under contention is a v2
    // concern. Locks survive terminal transitions and are reused on any
    // subsequent mutation path (e.g., DeleteInstanceAsync of a completed run).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _instanceLocks = new();

    // Test seam for AC13 lock-dictionary growth visibility. Expose count internally
    // so InstanceManagerTests can assert growth without making the dict public.
    internal int InstanceLockCount => _instanceLocks.Count;

    private SemaphoreSlim GetOrCreateInstanceLock(string instanceId)
        => _instanceLocks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1));

    public InstanceManager(
        IOptions<AgentRunOptions> options,
        IWebHostEnvironment hostEnvironment,
        ILogger<InstanceManager> logger)
    {
        _logger = logger;

        var configuredPath = options.Value.DataRootPath ?? "App_Data/AgentRun.Umbraco/instances/";
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

        // Story 10.1: serialise read-modify-write on this instance.
        var sem = GetOrCreateInstanceLock(instanceId);
        await sem.WaitAsync(cancellationToken);
        try
        {
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
        finally
        {
            sem.Release();
        }
    }

    public async Task<InstanceState> SetInstanceStatusAsync(
        string workflowAlias,
        string instanceId,
        InstanceStatus status,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        // Story 10.1: serialise read-modify-write on this instance.
        var sem = GetOrCreateInstanceLock(instanceId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(yamlPath, cancellationToken)
                ?? throw new InvalidOperationException($"Instance {instanceId} not found for workflow {workflowAlias}.");

            if (status == InstanceStatus.Running && state.Status == InstanceStatus.Running)
            {
                throw new InstanceAlreadyRunningException(instanceId);
            }

            // Terminal-status guard (Story 10.8 code review, tightened in Story 10.9
            // manual E2E): if the persisted state is terminal, refuse *sideways*
            // terminal transitions (e.g., Cancel overwriting a freshly-written
            // Completed). But Retry is a deliberate recovery — it resets a terminal
            // Failed to Running, and Retry is the only path that ever writes Running
            // to a terminal state (the RetryInstance gate only admits Failed or
            // Interrupted, and StartInstance rejects all three terminals). Allow
            // transitions INTO Running so Retry(Failed) works; everything else into
            // a terminal state still refuses.
            if (state.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled
                && state.Status != status
                && status != InstanceStatus.Running)
            {
                _logger.LogInformation(
                    "Ignored status transition for instance {InstanceId}: already in terminal state {CurrentStatus}, refused transition to {NewStatus}",
                    instanceId, state.Status, status);
                return state;
            }

            state.Status = status;
            state.UpdatedAt = DateTime.UtcNow;

            await WriteStateAtomicAsync(instanceDir, state, cancellationToken);

            _logger.LogInformation(
                "Set instance {InstanceId} status to {Status} for workflow {WorkflowAlias}",
                instanceId, status, workflowAlias);

            return state;
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<bool> DeleteInstanceAsync(
        string workflowAlias,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        // Story 10.1: serialise delete against any in-flight read-modify-write
        // on the same instance. Pure File.Exists before the lock is a cheap
        // fast path but not a TOCTOU — the re-check after the lock is held is
        // authoritative.
        if (!File.Exists(yamlPath))
        {
            return false;
        }

        var sem = GetOrCreateInstanceLock(instanceId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(yamlPath, cancellationToken);
            if (state is null)
            {
                return false;
            }

            if (state.Status is not (InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled or InstanceStatus.Interrupted))
            {
                throw new InvalidOperationException(
                    $"Cannot delete instance {instanceId} with status {state.Status}. Only completed, failed, cancelled, or interrupted instances can be deleted.");
            }

            Directory.Delete(instanceDir, recursive: true);

            _logger.LogInformation(
                "Deleted instance {InstanceId} for workflow {WorkflowAlias}",
                instanceId, workflowAlias);

            return true;
        }
        finally
        {
            sem.Release();
        }
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

        // Story 10.1: serialise read-modify-write on this instance.
        var sem = GetOrCreateInstanceLock(instanceId);
        await sem.WaitAsync(cancellationToken);
        try
        {
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
        finally
        {
            sem.Release();
        }
    }

    public async Task<InstanceState> SetCurrentStepIndexAsync(
        string workflowAlias,
        string instanceId,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        var instanceDir = GetInstanceDirectory(workflowAlias, instanceId);
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");

        var sem = GetOrCreateInstanceLock(instanceId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(yamlPath, cancellationToken)
                ?? throw new InvalidOperationException($"Instance {instanceId} not found for workflow {workflowAlias}.");

            if (stepIndex < 0 || stepIndex >= state.Steps.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(stepIndex),
                    $"Step index {stepIndex} is out of range. Instance has {state.Steps.Count} steps.");
            }

            if (state.CurrentStepIndex == stepIndex)
            {
                return state;
            }

            var previous = state.CurrentStepIndex;
            state.CurrentStepIndex = stepIndex;
            state.UpdatedAt = DateTime.UtcNow;

            await WriteStateAtomicAsync(instanceDir, state, cancellationToken);

            _logger.LogInformation(
                "Reconciled CurrentStepIndex {Previous} -> {StepIndex} for instance {InstanceId} of workflow {WorkflowAlias}",
                previous, stepIndex, instanceId, workflowAlias);

            return state;
        }
        finally
        {
            sem.Release();
        }
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
