using System.Threading.Channels;
using Shallai.UmbracoAgentRunner.Engine.Events;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Engine;

public sealed record StepExecutionContext(
    WorkflowDefinition Workflow,
    StepDefinition Step,
    InstanceState Instance,
    string InstanceFolderPath,
    string WorkflowFolderPath,
    ISseEventEmitter? EventEmitter = null,
    IConversationRecorder? ConversationRecorder = null,
    ChannelReader<string>? UserMessageReader = null);
