using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Engine.Events;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

[TestFixture]
public class WorkflowOrchestratorTests
{
    private IInstanceManager _instanceManager = null!;
    private IWorkflowRegistry _workflowRegistry = null!;
    private IStepExecutor _stepExecutor = null!;
    private ISseEventEmitter _emitter = null!;
    private WorkflowOrchestrator _orchestrator = null!;

    private static WorkflowDefinition CreateWorkflow(string mode = "interactive", int stepCount = 2) => new()
    {
        Name = "Test Workflow",
        Description = "Test",
        Mode = mode,
        Steps = Enumerable.Range(0, stepCount).Select(i => new StepDefinition
        {
            Id = $"step-{i}",
            Name = $"Step {i}",
            Agent = $"agents/step-{i}.md"
        }).ToList()
    };

    private static InstanceState CreateInstance(int currentStepIndex = 0, int stepCount = 2) => new()
    {
        InstanceId = "inst-001",
        WorkflowAlias = "test-wf",
        CurrentStepIndex = currentStepIndex,
        Status = InstanceStatus.Running,
        Steps = Enumerable.Range(0, stepCount).Select(i => new StepState
        {
            Id = $"step-{i}",
            Status = i < currentStepIndex ? StepStatus.Complete : StepStatus.Pending
        }).ToList(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    [SetUp]
    public void SetUp()
    {
        _instanceManager = Substitute.For<IInstanceManager>();
        _workflowRegistry = Substitute.For<IWorkflowRegistry>();
        _stepExecutor = Substitute.For<IStepExecutor>();
        _emitter = Substitute.For<ISseEventEmitter>();

        _instanceManager.GetInstanceFolderPath(Arg.Any<string>(), Arg.Any<string>())
            .Returns("/tmp/instances/inst-001");

        _orchestrator = new WorkflowOrchestrator(
            _instanceManager,
            _workflowRegistry,
            _stepExecutor,
            Substitute.For<IConversationStore>(),
            Substitute.For<IActiveInstanceRegistry>(),
            NullLoggerFactory.Instance,
            NullLogger<WorkflowOrchestrator>.Instance);
    }

    private void SetUpWorkflow(string mode = "interactive", int stepCount = 2)
    {
        var workflow = CreateWorkflow(mode, stepCount);
        _workflowRegistry.GetWorkflow("test-wf")
            .Returns(new RegisteredWorkflow("test-wf", "/workflows/test-wf", workflow));
    }

    private void SetUpInstance(int currentStepIndex = 0, int stepCount = 2, StepStatus currentStepStatusAfterExec = StepStatus.Complete)
    {
        var beforeExec = CreateInstance(currentStepIndex, stepCount);
        var afterExec = CreateInstance(currentStepIndex, stepCount);
        afterExec.Steps[currentStepIndex].Status = currentStepStatusAfterExec;

        // First call returns before-exec state, second returns after-exec state
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(beforeExec, afterExec);

        _instanceManager.AdvanceStepAsync("test-wf", "inst-001", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var advanced = CreateInstance(currentStepIndex + 1, stepCount);
                return advanced;
            });
    }

    [Test]
    public async Task InteractiveMode_ExecutesStepAndReturnsWithoutAdvancingToNext()
    {
        SetUpWorkflow("interactive");
        SetUpInstance(0);

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        // Step executed
        await _stepExecutor.Received(1).ExecuteStepAsync(
            Arg.Any<StepExecutionContext>(), Arg.Any<CancellationToken>());

        // Step index advanced
        await _instanceManager.Received(1).AdvanceStepAsync(
            "test-wf", "inst-001", Arg.Any<CancellationToken>());

        // No auto-advance — only one ExecuteNextStepAsync call (no recursion)
        await _emitter.DidNotReceive().EmitSystemMessageAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AutonomousMode_AutoAdvancesToNextStep()
    {
        SetUpWorkflow("autonomous");

        // First step: index 0, completes
        var firstCallInstance = CreateInstance(0);
        var afterFirstExec = CreateInstance(0);
        afterFirstExec.Steps[0].Status = StepStatus.Complete;

        // Second step: index 1 (last step), completes
        var secondCallInstance = CreateInstance(1);
        var afterSecondExec = CreateInstance(1);
        afterSecondExec.Steps[1].Status = StepStatus.Complete;

        // FindInstance returns different states on successive calls
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(firstCallInstance, afterFirstExec, secondCallInstance, afterSecondExec);

        _instanceManager.AdvanceStepAsync("test-wf", "inst-001", Arg.Any<CancellationToken>())
            .Returns(CreateInstance(1));

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        // StepExecutor called twice (once per step)
        await _stepExecutor.Received(2).ExecuteStepAsync(
            Arg.Any<StepExecutionContext>(), Arg.Any<CancellationToken>());

        // System message emitted for auto-advance
        await _emitter.Received(1).EmitSystemMessageAsync(
            Arg.Is<string>(s => s.Contains("Auto-advancing")), Arg.Any<CancellationToken>());

        // Instance completed
        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Completed, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FinalStepCompletes_InstanceSetToCompleted()
    {
        SetUpWorkflow("interactive", 1);
        SetUpInstance(0, 1);

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Completed, Arg.Any<CancellationToken>());

        await _emitter.Received(1).EmitRunFinishedAsync(
            "inst-001", "Completed", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StepFails_InstanceSetToFailed_RunErrorEmitted()
    {
        SetUpWorkflow("interactive");
        SetUpInstance(0, 2, StepStatus.Error);

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Failed, Arg.Any<CancellationToken>());

        await _emitter.Received(1).EmitRunErrorAsync(
            "step_failed", Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _emitter.Received(1).EmitStepFinishedAsync(
            "step-0", "Error", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StepFailsWithLlmError_EmitsClassifiedErrorCodeAndMessage()
    {
        SetUpWorkflow("interactive");
        SetUpInstance(0, 2, StepStatus.Error);

        // When step executor runs, set LlmError on the context
        _stepExecutor.ExecuteStepAsync(Arg.Any<StepExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ctx = callInfo.Arg<StepExecutionContext>();
                ctx.LlmError = ("rate_limit", "The AI provider returned a rate limit error. Wait a moment and retry.");
                return Task.CompletedTask;
            });

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        await _emitter.Received(1).EmitRunErrorAsync(
            "rate_limit",
            "The AI provider returned a rate limit error. Wait a moment and retry.",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StepFailsWithoutLlmError_EmitsStepFailedGenericError()
    {
        SetUpWorkflow("interactive");
        SetUpInstance(0, 2, StepStatus.Error);

        // Step executor does NOT set LlmError (non-LLM failure)
        _stepExecutor.ExecuteStepAsync(Arg.Any<StepExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        await _emitter.Received(1).EmitRunErrorAsync(
            "step_failed",
            "Step 'Step 0' failed",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryScenario_PendingStep_ExecutesAndCompletes()
    {
        SetUpWorkflow("interactive", 1);

        // Simulate retry: step was reset to Pending after Error
        var beforeExec = CreateInstance(0, 1);
        beforeExec.Steps[0].Status = StepStatus.Pending;

        var afterExec = CreateInstance(0, 1);
        afterExec.Steps[0].Status = StepStatus.Complete;

        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(beforeExec, afterExec);

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        await _stepExecutor.Received(1).ExecuteStepAsync(
            Arg.Any<StepExecutionContext>(), Arg.Any<CancellationToken>());

        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Completed, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryScenario_StepFailsAgain_InstanceGoesBackToFailed()
    {
        SetUpWorkflow("interactive", 2);

        // Simulate retry: step 0 reset to Pending
        var beforeExec = CreateInstance(0, 2);
        beforeExec.Steps[0].Status = StepStatus.Pending;

        var afterExec = CreateInstance(0, 2);
        afterExec.Steps[0].Status = StepStatus.Error;

        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(beforeExec, afterExec);

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Failed, Arg.Any<CancellationToken>());

        await _emitter.Received(1).EmitRunErrorAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SseEventsEmittedInCorrectOrder()
    {
        SetUpWorkflow("interactive", 1);
        SetUpInstance(0, 1);

        var callOrder = new List<string>();
        _emitter.EmitRunStartedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask).AndDoes(_ => callOrder.Add("run.started"));
        _emitter.EmitStepStartedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask).AndDoes(_ => callOrder.Add("step.started"));
        _emitter.EmitStepFinishedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask).AndDoes(_ => callOrder.Add("step.finished"));
        _emitter.EmitRunFinishedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask).AndDoes(_ => callOrder.Add("run.finished"));

        await _orchestrator.ExecuteNextStepAsync("test-wf", "inst-001", _emitter, CancellationToken.None);

        Assert.That(callOrder, Is.EqualTo(new[] { "run.started", "step.started", "step.finished", "run.finished" }));
    }
}
