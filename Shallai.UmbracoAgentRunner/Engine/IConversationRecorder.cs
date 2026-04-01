namespace Shallai.UmbracoAgentRunner.Engine;

public interface IConversationRecorder
{
    Task RecordAssistantTextAsync(string content, CancellationToken cancellationToken);

    Task RecordToolCallAsync(string toolCallId, string toolName, string arguments, CancellationToken cancellationToken);

    Task RecordToolResultAsync(string toolCallId, string toolResult, CancellationToken cancellationToken);

    Task RecordSystemMessageAsync(string message, CancellationToken cancellationToken);

    Task RecordUserMessageAsync(string content, CancellationToken cancellationToken);
}
