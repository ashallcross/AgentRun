using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public sealed record PromptAssemblyContext(
    string WorkflowFolderPath,
    StepDefinition Step,
    IReadOnlyList<StepState> AllSteps,
    IReadOnlyList<StepDefinition> AllStepDefinitions,
    string InstanceFolderPath,
    IReadOnlyList<ToolDescription> DeclaredTools);
