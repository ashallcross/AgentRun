using System.Threading.Channels;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public sealed record StepExecutionContext(
    WorkflowDefinition Workflow,
    StepDefinition Step,
    InstanceState Instance,
    string InstanceFolderPath,
    string WorkflowFolderPath,
    ISseEventEmitter? EventEmitter = null,
    IConversationRecorder? ConversationRecorder = null,
    ChannelReader<string>? UserMessageReader = null)
{
    public (string ErrorCode, string UserMessage)? LlmError { get; set; }
}
