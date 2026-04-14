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
    }

    // Story 10.8 Task 9.4 + 9.5 + F3: OCE with status Running (disconnect path) writes
    // Failed and rethrows (Story 10.9 owns refining this path).
    [Test]
    public async Task Start_OceWithRunningStatus_WritesFailedAndRethrows()
    {
        AttachHttpContext();

        var pending = MakeInstance(InstanceStatus.Pending);
        // First call → Pending. Second call (inside OCE handler) → Running (disconnect path).
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
    }
}
