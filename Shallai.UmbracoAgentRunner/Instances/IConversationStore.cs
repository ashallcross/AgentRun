namespace Shallai.UmbracoAgentRunner.Instances;

public interface IConversationStore
{
    Task AppendAsync(string workflowAlias, string instanceId, string stepId, ConversationEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationEntry>> GetHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken);
}
