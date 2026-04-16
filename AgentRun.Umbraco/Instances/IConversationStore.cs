namespace AgentRun.Umbraco.Instances;

public interface IConversationStore
{
    Task AppendAsync(string workflowAlias, string instanceId, string stepId, ConversationEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationEntry>> GetHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken);

    Task TruncateLastAssistantEntryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken);

    /// <summary>
    /// Story 10.6 — atomically rename the step's conversation log to
    /// <c>conversation-{stepId}.failed-{ISO8601-UTC}.jsonl</c> (hyphens in the
    /// timestamp for filesystem portability), leaving the next step execution
    /// to start from a fresh empty conversation. Used by the retry endpoint
    /// when the replayed-degenerate-state recovery design (Winston, 2026-04-08,
    /// Option 3) fires.
    ///
    /// Failure-mode contract: the primitive MUST surface a clear error on
    /// rename failure. It MUST NOT silently delete the original file as a
    /// fallback. It MUST NOT proceed with a stale conversation. A missing
    /// conversation file is a no-op.
    ///
    /// Returns the archive filename on successful rename (for the endpoint-
    /// level recovery log's <c>ArchivedTo</c> field), or <c>null</c> when the
    /// conversation file did not exist (no-op).
    /// </summary>
    Task<string?> WipeHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken);
}
