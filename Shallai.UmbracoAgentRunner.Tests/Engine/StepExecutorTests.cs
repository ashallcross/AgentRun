using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Tools;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

[TestFixture]
public class StepExecutorTests
{
    private IProfileResolver _profileResolver = null!;
    private IPromptAssembler _promptAssembler = null!;
    private IInstanceManager _instanceManager = null!;
    private IArtifactValidator _artifactValidator = null!;
    private ICompletionChecker _completionChecker = null!;
    private IChatClient _chatClient = null!;
    private ILogger<StepExecutor> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _profileResolver = Substitute.For<IProfileResolver>();
        _promptAssembler = Substitute.For<IPromptAssembler>();
        _instanceManager = Substitute.For<IInstanceManager>();
        _artifactValidator = Substitute.For<IArtifactValidator>();
        _completionChecker = Substitute.For<ICompletionChecker>();
        _chatClient = Substitute.For<IChatClient>();
        _logger = NullLogger<StepExecutor>.Instance;

        // Default: profile resolver returns mock client
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);

        // Default: assembler returns a test prompt
        _promptAssembler.AssemblePromptAsync(Arg.Any<PromptAssemblyContext>(), Arg.Any<CancellationToken>())
            .Returns("test prompt");

        // Default: instance manager returns updated state
        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => MakeInstance());

        // Default: chat client returns no tool calls
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));

        // Default: artifact validation passes
        _artifactValidator.ValidateInputArtifactsAsync(Arg.Any<StepDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ArtifactValidationResult(true, []));

        // Default: completion check passes
        _completionChecker.CheckAsync(Arg.Any<CompletionCheckDefinition?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionCheckResult(true, []));
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
    }

    private StepExecutor CreateExecutor(IEnumerable<IWorkflowTool>? tools = null)
    {
        return new StepExecutor(
            _profileResolver,
            _promptAssembler,
            tools ?? [],
            _instanceManager,
            _artifactValidator,
            _completionChecker,
            _logger);
    }

    private static StepDefinition MakeStep(string id = "step-1", List<string>? tools = null) =>
        new() { Id = id, Name = "Test Step", Agent = "agents/test.md", Tools = tools };

    private static WorkflowDefinition MakeWorkflow() =>
        new() { Name = "test-workflow", Steps = [MakeStep()] };

    private static InstanceState MakeInstance(string workflowAlias = "test-workflow", string instanceId = "inst-001") =>
        new()
        {
            WorkflowAlias = workflowAlias,
            InstanceId = instanceId,
            Steps = [new StepState { Id = "step-1", Status = StepStatus.Pending }]
        };

    private static StepExecutionContext MakeExecutionContext(
        WorkflowDefinition? workflow = null,
        StepDefinition? step = null,
        InstanceState? instance = null) =>
        new(
            Workflow: workflow ?? MakeWorkflow(),
            Step: step ?? MakeStep(),
            Instance: instance ?? MakeInstance(),
            InstanceFolderPath: "/tmp/instances/inst-001",
            WorkflowFolderPath: "/tmp/workflows/test-workflow");

    [Test]
    public async Task SuccessfulExecution_UpdatesStepStatus_ActiveThenComplete()
    {
        // AC #1, #10: step status transitions Active → Complete
        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Received.InOrder(() =>
        {
            _instanceManager.UpdateStepStatusAsync("test-workflow", "inst-001", 0, StepStatus.Active, Arg.Any<CancellationToken>());
            _instanceManager.UpdateStepStatusAsync("test-workflow", "inst-001", 0, StepStatus.Complete, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task ExceptionDuringExecution_SetsStepStatusToError()
    {
        // AC #8: exceptions caught and surfaced as step errors
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProfileNotFoundException("bad-profile"));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        // Should NOT throw
        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnlyDeclaredTools_PassedToToolLoop()
    {
        // AC #2, #3: only step.Tools tools are used
        var declaredTool = Substitute.For<IWorkflowTool>();
        declaredTool.Name.Returns("read_file");
        declaredTool.Description.Returns("Reads");

        var undeclaredTool = Substitute.For<IWorkflowTool>();
        undeclaredTool.Name.Returns("delete_file");
        undeclaredTool.Description.Returns("Deletes");

        var step = MakeStep(tools: ["read_file"]);
        var workflow = new WorkflowDefinition { Name = "test-workflow", Steps = [step] };
        var instance = MakeInstance();
        var context = new StepExecutionContext(workflow, step, instance, "/tmp/inst", "/tmp/wf");

        var executor = CreateExecutor([declaredTool, undeclaredTool]);

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        // ChatOptions.Tools should only contain read_file
        await _chatClient.Received().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => o!.Tools!.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProfileResolved_BeforePromptAssembled_BeforeToolLoop()
    {
        // AC #1: correct execution order
        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Received.InOrder(() =>
        {
            _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
            _promptAssembler.AssemblePromptAsync(Arg.Any<PromptAssemblyContext>(), Arg.Any<CancellationToken>());
            _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task StructuredLogging_IncludesRequiredFields()
    {
        // AC #11: structured logging with WorkflowAlias, InstanceId, StepId
        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(_profileResolver, _promptAssembler, [], _instanceManager, _artifactValidator, _completionChecker, loggerSub);
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        // Verify at least one log call includes all three fields
        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("step-1")
                && o.ToString()!.Contains("test-workflow")
                && o.ToString()!.Contains("inst-001")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void CancellationToken_Propagated()
    {
        // AC #7: cancellation propagates through async calls
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await executor.ExecuteStepAsync(context, cts.Token));
    }

    [Test]
    public async Task ReadsFromValidationFailure_SetsStepToError_LLMNeverCalled()
    {
        // AC #1, #2: reads_from validation fails → Error, no LLM call
        _artifactValidator.ValidateInputArtifactsAsync(Arg.Any<StepDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ArtifactValidationResult(false, ["missing-input.md"]));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        await _chatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompletionCheckFailure_SetsStepToError()
    {
        // AC #5, #7: completion check fails → Error
        _completionChecker.CheckAsync(Arg.Any<CompletionCheckDefinition?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionCheckResult(false, ["output.md"]));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        await _instanceManager.DidNotReceive().UpdateStepStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), StepStatus.Complete, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompletionCheckPass_SetsStepToComplete()
    {
        // AC #5, #6: completion check passes → Complete
        _completionChecker.CheckAsync(Arg.Any<CompletionCheckDefinition?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionCheckResult(true, []));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Complete, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NullCompletionCheck_SetsStepToComplete()
    {
        // AC #8: null completion check → Complete (backwards compat)
        var step = MakeStep();
        // step.CompletionCheck is null by default
        var context = MakeExecutionContext(step: step);

        var executor = CreateExecutor();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Complete, Arg.Any<CancellationToken>());
    }
}
