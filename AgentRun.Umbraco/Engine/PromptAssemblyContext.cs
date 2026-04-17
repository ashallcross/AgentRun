using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

// Story 11.7 — new fields InstanceId + WorkflowConfig at the end of the record
// so existing positional callers keep working. Both default to safe no-ops so
// test fixtures that don't care about variable injection can ignore them.
public sealed record PromptAssemblyContext(
    string WorkflowFolderPath,
    StepDefinition Step,
    IReadOnlyList<StepState> AllSteps,
    IReadOnlyList<StepDefinition> AllStepDefinitions,
    string InstanceFolderPath,
    IReadOnlyList<ToolDescription> DeclaredTools,
    string InstanceId = "",
    IReadOnlyDictionary<string, string>? WorkflowConfig = null);
