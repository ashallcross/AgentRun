using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AgentRun.Umbraco.Endpoints;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Endpoints;

[TestFixture]
public class ExecutionEndpointsTests
{
    private IInstanceManager _instanceManager = null!;
    private IConversationStore _conversationStore = null!;
    private IActiveInstanceRegistry _activeInstanceRegistry = null!;
    private IWorkflowOrchestrator _orchestrator = null!;
    private IProfileResolver _profileResolver = null!;
    private IWorkflowRegistry _workflowRegistry = null!;
    private ExecutionEndpoints _endpoints = null!;

    [SetUp]
    public void SetUp()
    {
        _instanceManager = Substitute.For<IInstanceManager>();
        _conversationStore = Substitute.For<IConversationStore>();
        _activeInstanceRegistry = Substitute.For<IActiveInstanceRegistry>();
        _orchestrator = Substitute.For<IWorkflowOrchestrator>();
        _profileResolver = Substitute.For<IProfileResolver>();
        _workflowRegistry = Substitute.For<IWorkflowRegistry>();

        _endpoints = new ExecutionEndpoints(
            _instanceManager,
            _profileResolver,
            _orchestrator,
            _workflowRegistry,
            _conversationStore,
            _activeInstanceRegistry,
            NullLogger<ExecutionEndpoints>.Instance,
            NullLoggerFactory.Instance);
    }

    private void AttachHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        _endpoints.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Test]
    public void SendMessage_NoRunningInstance_Returns409()
    {
        _activeInstanceRegistry.GetMessageWriter("inst-001").Returns((ChannelWriter<string>?)null);

        var result = _endpoints.SendMessage("inst-001", new SendMessageRequest { Message = "Hello" });

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("not_running"));
    }

    [Test]
    public void SendMessage_EmptyMessage_Returns400()
    {
        var result = _endpoints.SendMessage("inst-001", new SendMessageRequest { Message = "   " });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        var error = badRequest.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("empty_message"));
    }

    [Test]
    public async Task RetryInstance_NonFailedInstance_Returns409()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Running,
            Steps = [new StepState { Id = "step-0", Status = StepStatus.Active }]
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);

        var result = await _endpoints.RetryInstance("inst-001", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("invalid_state"));
    }

    [Test]
    public async Task RetryInstance_NonExistentInstance_Returns404()
    {
        _instanceManager.FindInstanceAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((InstanceState?)null);

        var result = await _endpoints.RetryInstance("nonexistent", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task RetryInstance_FailedInstance_CallsTruncationAndResetsStep()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Failed,
            Steps =
            [
                new StepState { Id = "step-0", Status = StepStatus.Complete },
                new StepState { Id = "step-1", Status = StepStatus.Error }
            ],
            CurrentStepIndex = 1
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);
        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);

        // The endpoint calls ExecuteSseAsync which needs HttpResponse — it will throw because
        // we don't have a real HTTP context. Verify the state changes were called before the throw.
        try
        {
            await _endpoints.RetryInstance("inst-001", CancellationToken.None);
        }
        catch (NullReferenceException)
        {
            // Expected — no real HttpResponse in test context
        }

        // Verify truncation was called for the error step
        await _conversationStore.Received(1).TruncateLastAssistantEntryAsync(
            "test-wf", "inst-001", "step-1", Arg.Any<CancellationToken>());

        // Verify step was reset to Pending
        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-wf", "inst-001", 1, StepStatus.Pending, Arg.Any<CancellationToken>());

        // Verify instance was set to Running
        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Running, Arg.Any<CancellationToken>());
    }

    [Test]
    public void SendMessage_RunningInstance_WritesToChannelAndReturns200()
    {
        var channel = Channel.CreateUnbounded<string>();
        _activeInstanceRegistry.GetMessageWriter("inst-001").Returns(channel.Writer);

        var result = _endpoints.SendMessage("inst-001", new SendMessageRequest { Message = "Hello agent" });

        Assert.That(result, Is.InstanceOf<OkResult>());
        Assert.That(channel.Reader.TryRead(out var msg), Is.True);
        Assert.That(msg, Is.EqualTo("Hello agent"));
    }

    // ---------------- Story 10.8: SSE OCE handler ---------------- //

    private static InstanceState MakeInstance(InstanceStatus status)
    {
        return new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = status,
            Steps = [new StepState { Id = "step-0", Status = StepStatus.Pending }]
        };
    }

    // Story 10.8 Task 9.3 + AC6: OCE with persisted Cancelled skips the Failed overwrite
    // and returns cleanly (does NOT rethrow — rethrowing produces an unhandled-exception log).
    [Test]
    public async Task Start_OceWithCancelledStatus_ReturnsEmptyResultAndSkipsFailedOverwrite()
    {
        AttachHttpContext();

        var pending = MakeInstance(InstanceStatus.Pending);
        // First call (pre-start) → Pending. Second call (inside OCE handler) → Cancelled.
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(pending, MakeInstance(InstanceStatus.Cancelled));

        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(pending);

        _profileResolver.HasConfiguredProviderAsync(Arg.Any<WorkflowDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _orchestrator
            .ExecuteNextStepAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ISseEventEmitter>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());

        var result = await _endpoints.StartInstance("inst-001", CancellationToken.None);

        // Cancelled path completes cleanly with EmptyResult (no rethrow, no unhandled-exception log).
        Assert.That(result, Is.InstanceOf<EmptyResult>());

        // The initial Pending → Running transition was persisted on entry. Guards against
        // a regression where the initial transition is accidentally skipped.
        await _instanceManager.Received(1).SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Running, Arg.Any<CancellationToken>());

        // No Failed overwrite was attempted (AC6 — Cancelled path preserves the persisted status).
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Failed, Arg.Any<CancellationToken>());

        // Story 10.9 AC3 regression guard: Cancelled branch must NOT fall through to
        // the disconnect branch. Interrupted write would indicate branch ordering drift.
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Interrupted, Arg.Any<CancellationToken>());
    }

    // Story 10.8 Task 9.4 + 9.5 + F3: OCE with status Running and NO controller-token
    // cancellation (provider-internal OCE) writes Failed and rethrows.
    // Story 10.9 AC2: this exercises the internal-OCE discrimination — the controller
    // `cancellationToken` parameter is `CancellationToken.None` (never cancelled), so
    // the disconnect branch evaluates false and the Failed fallback runs.
    [Test]
    public async Task Start_OceWithRunningStatus_WritesFailedAndRethrows()
    {
        AttachHttpContext();

        var pending = MakeInstance(InstanceStatus.Pending);
        // First call → Pending. Second call (inside OCE handler) → Running.
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(pending, MakeInstance(InstanceStatus.Running));

        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(pending);

        _profileResolver.HasConfiguredProviderAsync(Arg.Any<WorkflowDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _orchestrator
            .ExecuteNextStepAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ISseEventEmitter>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await _endpoints.StartInstance("inst-001", CancellationToken.None));

        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Failed, Arg.Any<CancellationToken>());

        // Story 10.9 AC2: no Interrupted write — the controller token was never cancelled
        // so the disconnect branch must not fire.
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Interrupted, Arg.Any<CancellationToken>());
    }

    // ---------------- Story 10.9: SSE disconnect resilience ---------------- //

    // Story 10.9 AC1: OCE fires AND the controller cancellationToken is cancelled
    // (client disconnect — tab close / network drop / F5). Handler must persist
    // Interrupted (not Failed) and return EmptyResult without rethrowing.
    [Test]
    public async Task Start_OceWithRunningStatus_ClientDisconnect_WritesInterruptedAndReturnsEmpty()
    {
        AttachHttpContext();

        var pending = MakeInstance(InstanceStatus.Pending);
        // First call → Pending (pre-start). Second call (inside OCE handler) → Running
        // (the run was progressing normally when the SSE connection dropped).
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(pending, MakeInstance(InstanceStatus.Running));

        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(pending);

        _profileResolver.HasConfiguredProviderAsync(Arg.Any<WorkflowDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _orchestrator
            .ExecuteNextStepAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ISseEventEmitter>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());

        // Pre-cancel the controller token to simulate HttpContext.RequestAborted firing
        // (the model-binder-provided CancellationToken derives from RequestAborted).
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _endpoints.StartInstance("inst-001", cts.Token);

        // Disconnect path returns EmptyResult cleanly — NO rethrow (same pattern as
        // 10.8 Cancelled branch; avoids the 100-line unhandled-exception log).
        Assert.That(result, Is.InstanceOf<EmptyResult>());

        // Interrupted was persisted.
        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Interrupted, Arg.Any<CancellationToken>());

        // Failed was NOT persisted — the disconnect branch intercepted before the fallback.
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Failed, Arg.Any<CancellationToken>());
    }

    // Story 10.9 AC5: Retry on an Interrupted instance resets the StepStatus.Active
    // step to Pending and transitions instance to Running. Does NOT truncate JSONL
    // (no failed assistant message was committed — the stream was torn down mid-response).
    [Test]
    public async Task Retry_OnInterrupted_ResetsActiveStepAndStreams()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Interrupted,
            Steps =
            [
                new StepState { Id = "step-0", Status = StepStatus.Complete },
                new StepState { Id = "step-1", Status = StepStatus.Active }
            ],
            CurrentStepIndex = 1
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);
        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);

        // No HTTP context → ExecuteSseAsync will throw NRE on Response access. That's
        // fine — we only need to assert the state mutations that happen before.
        try
        {
            await _endpoints.RetryInstance("inst-001", CancellationToken.None);
        }
        catch (NullReferenceException)
        {
            // Expected — no real HttpResponse in test context.
        }

        // Step-discovery found the Active step and reset it to Pending.
        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-wf", "inst-001", 1, StepStatus.Pending, Arg.Any<CancellationToken>());

        // Instance transitioned Interrupted → Running.
        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "test-wf", "inst-001", InstanceStatus.Running, Arg.Any<CancellationToken>());

        // AC5 + AC6 + Task 4: JSONL truncation is SKIPPED for the Interrupted path.
        await _conversationStore.DidNotReceive().TruncateLastAssistantEntryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Story 10.9 AC6: Retry on Interrupted with no Active step returns 409
    // (pathological: Interrupted persisted after all steps finished, or before any started).
    [Test]
    public async Task Retry_OnInterrupted_NoActiveStep_Returns409()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Interrupted,
            Steps =
            [
                new StepState { Id = "step-0", Status = StepStatus.Complete },
                new StepState { Id = "step-1", Status = StepStatus.Pending }
            ]
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);

        var result = await _endpoints.RetryInstance("inst-001", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("invalid_state"));
        Assert.That(error?.Message, Does.Contain("active step"));

        // No mutation happened — neither step reset nor status change.
        await _instanceManager.DidNotReceive().UpdateStepStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>());
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Running, Arg.Any<CancellationToken>());
    }

    // Story 10.9 code-review P1: Disconnect branch must only fire when the persisted
    // status is Running. If the orchestrator has already written a terminal status
    // (Completed/Failed/Cancelled) before the OCE propagates, the handler must NOT
    // attempt an Interrupted write — the log would lie and the manager's terminal
    // guard would refuse silently.
    [Test]
    public async Task Start_OceWithCompletedStatus_DoesNotWriteInterruptedEvenOnDisconnect()
    {
        AttachHttpContext();

        var pending = MakeInstance(InstanceStatus.Pending);
        // First call (pre-start) → Pending. Second call (inside OCE handler) → Completed.
        // The orchestrator persisted Completed just before the OCE propagated.
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(pending, MakeInstance(InstanceStatus.Completed));

        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(pending);

        _profileResolver.HasConfiguredProviderAsync(Arg.Any<WorkflowDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _orchestrator
            .ExecuteNextStepAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ISseEventEmitter>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Falls through all three branches: Cancelled (no), Disconnect (no — status
        // is Completed), Failed fallback runs. The Failed-write is a no-op in the
        // real manager (terminal guard) but the test asserts the handler's INTENT
        // — no spurious Interrupted write.
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _endpoints.StartInstance("inst-001", cts.Token));

        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Interrupted, Arg.Any<CancellationToken>());
    }

    // Story 10.9 code-review P4 / Task 8.7: regression guard that the Failed-path
    // Retry still truncates the JSONL. The branching change in Task 4 must not
    // accidentally skip truncation for Failed.
    [Test]
    public async Task Retry_OnFailed_StillTruncatesConversation()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Failed,
            Steps =
            [
                new StepState { Id = "step-0", Status = StepStatus.Complete },
                new StepState { Id = "step-1", Status = StepStatus.Error }
            ],
            CurrentStepIndex = 1
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);
        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);

        try
        {
            await _endpoints.RetryInstance("inst-001", CancellationToken.None);
        }
        catch (NullReferenceException)
        {
            // Expected — no real HttpResponse in test context.
        }

        // AC5 regression guard: Failed path must still call truncation.
        await _conversationStore.Received(1).TruncateLastAssistantEntryAsync(
            "test-wf", "inst-001", "step-1", Arg.Any<CancellationToken>());
    }

    // Story 10.9 code-review P2: concurrent Retry must not surface as 500.
    // The second request races past the status gate (still reads Interrupted),
    // but SetInstanceStatusAsync throws "already running" because the first
    // request already transitioned to Running. Handler must map to 409.
    [Test]
    public async Task Retry_OnInterrupted_ConcurrentRetry_Returns409NotUnhandled()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Interrupted,
            Steps =
            [
                new StepState { Id = "step-0", Status = StepStatus.Active }
            ]
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);
        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        _instanceManager.SetInstanceStatusAsync(Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Running, Arg.Any<CancellationToken>())
            .Returns<InstanceState>(_ => throw new InvalidOperationException(
                "Instance inst-001 is already running. Concurrent execution is not permitted."));

        var result = await _endpoints.RetryInstance("inst-001", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("already_running"));
    }

    // Story 10.9 code-review P3: parallel-orchestrator guard. If a prior orchestrator
    // is still draining (registry writer slot still occupied), Retry must 409 without
    // mutating state — mirrors StartInstance's already_running guard.
    [Test]
    public async Task Retry_OnInterrupted_WithActiveRegistryWriter_Returns409()
    {
        var instance = new InstanceState
        {
            InstanceId = "inst-001",
            WorkflowAlias = "test-wf",
            Status = InstanceStatus.Interrupted,
            Steps =
            [
                new StepState { Id = "step-0", Status = StepStatus.Active }
            ]
        };
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);

        // Simulate a prior orchestrator that has not yet released its writer slot.
        var channel = Channel.CreateUnbounded<string>();
        _activeInstanceRegistry.GetMessageWriter("inst-001").Returns(channel.Writer);

        var result = await _endpoints.RetryInstance("inst-001", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("already_running"));

        // No state mutation happened.
        await _instanceManager.DidNotReceive().UpdateStepStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>());
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), InstanceStatus.Running, Arg.Any<CancellationToken>());
    }

    // Story 10.9 AC9: Start on an Interrupted instance returns 409 — user must Retry, not Start.
    [Test]
    public async Task Start_OnInterrupted_Returns409()
    {
        var instance = MakeInstance(InstanceStatus.Interrupted);
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>()).Returns(instance);

        var result = await _endpoints.StartInstance("inst-001", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result;
        var error = conflict.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("invalid_status"));

        // No status mutation attempted — the guard short-circuits before SetInstanceStatusAsync.
        await _instanceManager.DidNotReceive().SetInstanceStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InstanceStatus>(), Arg.Any<CancellationToken>());
    }
}
