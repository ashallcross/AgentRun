using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AgentRun.Umbraco.Endpoints;
using AgentRun.Umbraco.Engine;
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
    private ExecutionEndpoints _endpoints = null!;

    [SetUp]
    public void SetUp()
    {
        _instanceManager = Substitute.For<IInstanceManager>();
        _conversationStore = Substitute.For<IConversationStore>();
        _activeInstanceRegistry = Substitute.For<IActiveInstanceRegistry>();

        _endpoints = new ExecutionEndpoints(
            _instanceManager,
            Substitute.For<IProfileResolver>(),
            Substitute.For<IWorkflowOrchestrator>(),
            Substitute.For<IWorkflowRegistry>(),
            _conversationStore,
            _activeInstanceRegistry,
            NullLogger<ExecutionEndpoints>.Instance,
            NullLoggerFactory.Instance);
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
}
