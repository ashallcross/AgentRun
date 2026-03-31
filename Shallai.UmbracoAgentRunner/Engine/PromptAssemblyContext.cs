using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Engine;

public sealed record PromptAssemblyContext(
    string WorkflowFolderPath,
    StepDefinition Step,
    IReadOnlyList<StepState> AllSteps,
    IReadOnlyList<StepDefinition> AllStepDefinitions,
    string InstanceFolderPath,
    IReadOnlyList<ToolDescription> DeclaredTools);
