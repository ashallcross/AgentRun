using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;

namespace AgentRun.Umbraco.Services;

public class ConversationRecorder : IConversationRecorder
{
    private readonly IConversationStore _store;
    private readonly string _workflowAlias;
    private readonly string _instanceId;
    private readonly string _stepId;
    private readonly ILogger<ConversationRecorder> _logger;

    public ConversationRecorder(
        IConversationStore store,
        string workflowAlias,
        string instanceId,
        string stepId,
        ILogger<ConversationRecorder> logger)
    {
        _store = store;
        _workflowAlias = workflowAlias;
        _instanceId = instanceId;
        _stepId = stepId;
        _logger = logger;
    }

    public async Task RecordAssistantTextAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new ConversationEntry
            {
                Role = "assistant",
                Content = content,
                Timestamp = DateTime.UtcNow
            };
            await _store.AppendAsync(_workflowAlias, _instanceId, _stepId, entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record assistant text for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                _stepId, _workflowAlias, _instanceId);
        }
    }

    public async Task RecordToolCallAsync(string toolCallId, string toolName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new ConversationEntry
            {
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                ToolCallId = toolCallId,
                ToolName = toolName,
                ToolArguments = arguments
            };
            await _store.AppendAsync(_workflowAlias, _instanceId, _stepId, entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record tool call {ToolName} for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                toolName, _stepId, _workflowAlias, _instanceId);
        }
    }

    public async Task RecordToolResultAsync(string toolCallId, string toolResult, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new ConversationEntry
            {
                Role = "tool",
                Timestamp = DateTime.UtcNow,
                ToolCallId = toolCallId,
                ToolResult = toolResult
            };
            await _store.AppendAsync(_workflowAlias, _instanceId, _stepId, entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record tool result for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                _stepId, _workflowAlias, _instanceId);
        }
    }

    public async Task RecordSystemMessageAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new ConversationEntry
            {
                Role = "system",
                Content = message,
                Timestamp = DateTime.UtcNow
            };
            await _store.AppendAsync(_workflowAlias, _instanceId, _stepId, entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record system message for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                _stepId, _workflowAlias, _instanceId);
        }
    }

    public async Task RecordUserMessageAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new ConversationEntry
            {
                Role = "user",
                Content = content,
                Timestamp = DateTime.UtcNow
            };
            await _store.AppendAsync(_workflowAlias, _instanceId, _stepId, entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record user message for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                _stepId, _workflowAlias, _instanceId);
        }
    }
}
