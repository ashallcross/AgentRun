using System.Text.Json;

namespace Shallai.UmbracoAgentRunner.Instances;

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

        return Path.Combine(instanceFolder, $"conversation-{stepId}.jsonl");
    }
}
