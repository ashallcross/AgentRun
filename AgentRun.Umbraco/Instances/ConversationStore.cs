using System.Text.Json;

namespace AgentRun.Umbraco.Instances;

public sealed class ConversationStore : IConversationStore
{
    private readonly string _dataRootPath;
    private readonly ILogger<ConversationStore> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConversationStore(IInstanceManager instanceManager, ILogger<ConversationStore> logger)
    {
        _logger = logger;

        // Extract the data root path from InstanceManager by getting a known path and walking up.
        // We delegate path resolution per-call to IInstanceManager.GetInstanceFolderPath.
        _dataRootPath = string.Empty; // Not used — production path resolution goes through IInstanceManager
        _instanceManager = instanceManager;
    }

    /// <summary>
    /// Constructor for testing — accepts a resolved data root path directly.
    /// </summary>
    internal ConversationStore(string dataRootPath, ILogger<ConversationStore> logger)
    {
        _logger = logger;
        _dataRootPath = Path.GetFullPath(dataRootPath);
        if (!_dataRootPath.EndsWith(Path.DirectorySeparatorChar))
        {
            _dataRootPath += Path.DirectorySeparatorChar;
        }

        _instanceManager = null;
    }

    private readonly IInstanceManager? _instanceManager;

    public async Task AppendAsync(string workflowAlias, string instanceId, string stepId, ConversationEntry entry, CancellationToken cancellationToken)
    {
        var filePath = GetConversationFilePath(workflowAlias, instanceId, stepId);
        var json = JsonSerializer.Serialize(entry, SerializerOptions);

        _logger.LogDebug("Appending conversation entry for {WorkflowAlias}/{InstanceId}/{StepId}", workflowAlias, instanceId, stepId);

        await File.AppendAllTextAsync(filePath, json + Environment.NewLine, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationEntry>> GetHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken)
    {
        var filePath = GetConversationFilePath(workflowAlias, instanceId, stepId);

        if (!File.Exists(filePath))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var entries = new List<ConversationEntry>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<ConversationEntry>(line, SerializerOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupted conversation line in {FilePath}: {Line}", filePath, line);
            }
        }

        return entries;
    }

    public async Task TruncateLastAssistantEntryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken)
    {
        var filePath = GetConversationFilePath(workflowAlias, instanceId, stepId);

        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No conversation file to truncate for {WorkflowAlias}/{InstanceId}/{StepId}", workflowAlias, instanceId, stepId);
            return;
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        // Find the last entry with role == "assistant" (text or tool call) and remove it.
        // Per the story: "remove the final failed assistant message" — the last assistant entry.
        var lastAssistantIndex = -1;
        for (var i = nonEmptyLines.Count - 1; i >= 0; i--)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<ConversationEntry>(nonEmptyLines[i], SerializerOptions);
                if (entry is not null && string.Equals(entry.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    lastAssistantIndex = i;
                    break;
                }
            }
            catch (JsonException)
            {
                // Skip corrupted lines during search
            }
        }

        if (lastAssistantIndex == -1)
        {
            _logger.LogDebug("No assistant entry to truncate for {WorkflowAlias}/{InstanceId}/{StepId}", workflowAlias, instanceId, stepId);
            return;
        }

        // Story 9.0 fix: if a tool result (or anything other than the assistant
        // entry itself) follows the last assistant entry, the conversation is
        // ALREADY at a clean tool_use → tool_result boundary. Removing the
        // assistant entry now would orphan the trailing tool_result and the
        // next provider call would 400 ("tool_result with no matching tool_use
        // in previous message"). This happens specifically when a stall fires
        // after a successful tool call: the stall's empty turn is not recorded,
        // so the last "assistant" entry is the tool_call that just succeeded.
        // The conversation is fine; truncating it is wrong.
        if (lastAssistantIndex != nonEmptyLines.Count - 1)
        {
            _logger.LogDebug(
                "Last assistant entry is not the final entry (followed by a tool result) for {WorkflowAlias}/{InstanceId}/{StepId} — conversation already at a clean boundary, skipping truncation",
                workflowAlias, instanceId, stepId);
            return;
        }

        nonEmptyLines.RemoveAt(lastAssistantIndex);

        _logger.LogInformation("Truncating last assistant entry for retry: {WorkflowAlias}/{InstanceId}/{StepId}", workflowAlias, instanceId, stepId);

        // Atomic rewrite: write to .tmp then move
        var tmpPath = filePath + ".tmp";
        var content = string.Join(Environment.NewLine, nonEmptyLines);
        if (nonEmptyLines.Count > 0)
        {
            content += Environment.NewLine;
        }

        await File.WriteAllTextAsync(tmpPath, content, cancellationToken);
        File.Move(tmpPath, filePath, overwrite: true);
    }

    public Task<string?> WipeHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = GetConversationFilePath(workflowAlias, instanceId, stepId);

        if (!File.Exists(filePath))
        {
            _logger.LogDebug(
                "No conversation file to wipe for {WorkflowAlias}/{InstanceId}/{StepId}",
                workflowAlias, instanceId, stepId);
            return Task.FromResult<string?>(null);
        }

        // ISO-8601 UTC with colons replaced by hyphens (colons are illegal on
        // NTFS and awkward in shells; hyphens are portable across Windows,
        // macOS, and Linux). Example: 2026-04-08T19-47-23Z.
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        var archiveFileName = $"conversation-{stepId}.failed-{timestamp}.jsonl";
        var archivePath = Path.Combine(Path.GetDirectoryName(filePath)!, archiveFileName);

        try
        {
            // File.Move with overwrite:false is atomic on the same volume.
            // Collision-safe: if an archive for this exact second already
            // exists the call throws IOException; we surface it to the caller
            // rather than silently overwriting. A second retry in the same
            // second is vanishingly rare — the caller (retry endpoint) treats
            // it as a hard failure.
            File.Move(filePath, archivePath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex,
                "Failed to archive conversation file for wipe: {WorkflowAlias}/{InstanceId}/{StepId} -> {ArchivePath}",
                workflowAlias, instanceId, stepId, archivePath);
            throw new InvalidOperationException(
                $"Failed to archive conversation file to '{archiveFileName}' during wipe: {ex.Message}", ex);
        }

        _logger.LogInformation(
            "Wiped conversation history for {WorkflowAlias}/{InstanceId}/{StepId}; original archived to {ArchivedTo}",
            workflowAlias, instanceId, stepId, archiveFileName);

        return Task.FromResult<string?>(archiveFileName);
    }

    private string GetConversationFilePath(string workflowAlias, string instanceId, string stepId)
    {
        string instanceFolder;
        if (_instanceManager is not null)
        {
            instanceFolder = _instanceManager.GetInstanceFolderPath(workflowAlias, instanceId);
        }
        else
        {
            instanceFolder = Path.Combine(_dataRootPath, workflowAlias, instanceId);
        }

        if (stepId.Contains('/') || stepId.Contains('\\') || stepId.Contains('\0'))
        {
            throw new ArgumentException(
                $"Step ID '{stepId}' contains illegal characters (path separators or null bytes)", nameof(stepId));
        }

        return Path.Combine(instanceFolder, $"conversation-{stepId}.jsonl");
    }
}
