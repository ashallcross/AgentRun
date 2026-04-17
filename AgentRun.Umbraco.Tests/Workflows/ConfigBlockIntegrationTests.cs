using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

/// <summary>
/// Regression gate for Story 11.7 — proves the workflow-level <c>config:</c>
/// block + runtime-provided variables travel from workflow.yaml on disk
/// through <see cref="WorkflowRegistry"/> to <see cref="PromptAssembler"/>,
/// with the resulting prompt carrying substituted values. Individual layers
/// are covered by <c>WorkflowValidatorTests</c>, <c>WorkflowSchemaTests</c>,
/// and <c>PromptAssemblerVariableInjectionTests</c>; this file asserts the
/// seam between them so a future refactor can't silently break the round-trip.
/// </summary>
[TestFixture]
public class ConfigBlockIntegrationTests
{
    private string _tempRoot = null!;
    private string _instanceDir = null!;
    private WorkflowRegistry _registry = null!;
    private FakeTimeProvider _timeProvider = null!;
    private PromptAssembler _assembler = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "agentrun-configblock-" + Guid.NewGuid().ToString("N"));
        _instanceDir = Path.Combine(_tempRoot, "instance");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_instanceDir);

        _registry = new WorkflowRegistry(
            new WorkflowParser(),
            new WorkflowValidator(),
            Enumerable.Empty<IWorkflowTool>(),
            NullLogger<WorkflowRegistry>.Instance);

        _timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-17T12:00:00Z"));
        _assembler = new PromptAssembler(NullLogger<PromptAssembler>.Instance, _timeProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public async Task RegisteredWorkflow_WithConfigBlock_ExposesValuesToPromptAssembler()
    {
        // AC4 + the full round-trip: workflow YAML declares config, parser
        // populates WorkflowDefinition.Config, StepExecutor would forward it
        // into PromptAssemblyContext, and PromptAssembler substitutes.
        var yaml = """
            name: Config Block Workflow
            description: exposes config values to prompts
            config:
              language: en-GB
              severity_threshold: medium
            steps:
              - id: analyser
                name: Analyser
                agent: agents/analyser.md
            """;
        var agentBody = "Language setting: {language}. Threshold: {severity_threshold}. Today: {today}.";
        SetupWorkflowOnDisk(
            "config-block-wf", yaml,
            agentFiles: new() { ["agents/analyser.md"] = agentBody });

        await _registry.LoadWorkflowsAsync(_tempRoot);
        var registered = _registry.GetWorkflow("config-block-wf");
        Assert.That(registered, Is.Not.Null);
        Assert.That(registered!.Definition.Config, Is.Not.Null);
        Assert.That(registered.Definition.Config!["language"], Is.EqualTo("en-GB"));

        var step = registered.Definition.Steps.Single();
        var context = new PromptAssemblyContext(
            WorkflowFolderPath: Path.Combine(_tempRoot, "config-block-wf"),
            Step: step,
            AllSteps: [new StepState { Id = "analyser", Status = StepStatus.Active }],
            AllStepDefinitions: registered.Definition.Steps,
            InstanceFolderPath: _instanceDir,
            DeclaredTools: [],
            InstanceId: "integration-instance-id",
            WorkflowConfig: registered.Definition.Config);

        var prompt = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(prompt, Does.Contain("Language setting: en-GB"));
        Assert.That(prompt, Does.Contain("Threshold: medium"));
        Assert.That(prompt, Does.Contain("Today: 2026-04-17"));
    }

    [Test]
    public async Task RegisteredWorkflow_WithoutConfigBlock_StillResolvesRuntimeVariables()
    {
        // AC1 / backwards-compat — workflows that ship no `config:` block still
        // get the four runner-provided variables. Confirms the feature is additive.
        var yaml = """
            name: No Config Workflow
            description: exercises runtime-only variables
            steps:
              - id: reporter
                name: Reporter
                agent: agents/reporter.md
            """;
        var agentBody = "Instance: {instance_id}. Step index: {step_index}. Today: {today}.";
        SetupWorkflowOnDisk(
            "no-config-wf", yaml,
            agentFiles: new() { ["agents/reporter.md"] = agentBody });

        await _registry.LoadWorkflowsAsync(_tempRoot);
        var registered = _registry.GetWorkflow("no-config-wf");
        Assert.That(registered, Is.Not.Null);
        Assert.That(registered!.Definition.Config, Is.Null,
            "workflow without config: block must deserialise to Config == null");

        var step = registered.Definition.Steps.Single();
        var context = new PromptAssemblyContext(
            WorkflowFolderPath: Path.Combine(_tempRoot, "no-config-wf"),
            Step: step,
            AllSteps: [new StepState { Id = "reporter", Status = StepStatus.Active }],
            AllStepDefinitions: registered.Definition.Steps,
            InstanceFolderPath: _instanceDir,
            DeclaredTools: [],
            InstanceId: "no-config-integration-id",
            WorkflowConfig: registered.Definition.Config);

        var prompt = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(prompt, Does.Contain("Instance: no-config-integration-id"));
        Assert.That(prompt, Does.Contain("Step index: 0"));
        Assert.That(prompt, Does.Contain("Today: 2026-04-17"));
    }

    private void SetupWorkflowOnDisk(string alias, string yamlContent, Dictionary<string, string> agentFiles)
    {
        var folder = Path.Combine(_tempRoot, alias);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "workflow.yaml"), yamlContent);

        foreach (var (relPath, body) in agentFiles)
        {
            var fullPath = Path.Combine(folder, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, body);
        }
    }
}
