namespace Shallai.UmbracoAgentRunner.Tools;

public sealed record ToolExecutionContext(
    string InstanceFolderPath,
    string InstanceId,
    string StepId,
    string WorkflowAlias);
