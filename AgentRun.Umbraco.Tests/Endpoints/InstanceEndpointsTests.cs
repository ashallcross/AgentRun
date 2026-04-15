using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using AgentRun.Umbraco.Endpoints;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Endpoints;

[TestFixture]
public class InstanceEndpointsTests
{
    private IInstanceManager _instanceManager = null!;
    private IWorkflowRegistry _workflowRegistry = null!;
    private IActiveInstanceRegistry _activeInstanceRegistry = null!;
    private InstanceEndpoints _endpoints = null!;

    private static readonly WorkflowDefinition TestDefinition = new()
    {
        Name = "Content Audit",
        Description = "Audits content quality",
        Mode = "interactive",
        Steps =
        [
            new StepDefinition { Id = "step-1", Name = "Analyse", Agent = "agent.md", WritesTo = ["scan-results.md"] },
            new StepDefinition { Id = "step-2", Name = "Report", Agent = "report.md", WritesTo = ["quality-scores.md"] }
        ]
    };

    private static InstanceState CreateTestInstance(
        string instanceId = "abc123",
        string workflowAlias = "content-audit",
        InstanceStatus status = InstanceStatus.Pending)
    {
        return new InstanceState
        {
            InstanceId = instanceId,
            WorkflowAlias = workflowAlias,
            Status = status,
            CurrentStepIndex = 0,
            CreatedAt = new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc),
            CreatedBy = "admin",
            Steps =
            [
                new StepState { Id = "step-1", Status = StepStatus.Pending },
                new StepState { Id = "step-2", Status = StepStatus.Pending }
            ]
        };
    }

    [SetUp]
    public void SetUp()
    {
        _instanceManager = Substitute.For<IInstanceManager>();
        _workflowRegistry = Substitute.For<IWorkflowRegistry>();
        _activeInstanceRegistry = Substitute.For<IActiveInstanceRegistry>();
        _endpoints = new InstanceEndpoints(_instanceManager, _workflowRegistry, _activeInstanceRegistry);

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin@example.com")], "test");
        var principal = new ClaimsPrincipal(identity);
        _endpoints.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    // Task 4.1: POST creates instance and returns correct shape
    [Test]
    public async Task CreateInstance_ReturnsCreatedWithCorrectShape()
    {
        var registered = new RegisteredWorkflow("content-audit", "/workflows/content-audit", TestDefinition);
        _workflowRegistry.GetWorkflow("content-audit").Returns(registered);

        var state = CreateTestInstance();
        _instanceManager.CreateInstanceAsync("content-audit", TestDefinition, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(state);

        var request = new CreateInstanceRequest { WorkflowAlias = "content-audit" };
        var result = await _endpoints.CreateInstance(request, CancellationToken.None);

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(201));

        var response = objectResult.Value as InstanceResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Id, Is.EqualTo("abc123"));
        Assert.That(response.WorkflowAlias, Is.EqualTo("content-audit"));
        Assert.That(response.Status, Is.EqualTo(InstanceStatus.Pending));
        Assert.That(response.CurrentStepIndex, Is.EqualTo(0));
        Assert.That(response.CreatedAt, Is.EqualTo(new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc)));
        Assert.That(response.UpdatedAt, Is.EqualTo(new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc)));
    }

    // Task 4.2: POST returns 404 when workflow alias not found
    [Test]
    public async Task CreateInstance_Returns404_WhenWorkflowNotFound()
    {
        _workflowRegistry.GetWorkflow("nonexistent").Returns((RegisteredWorkflow?)null);

        var request = new CreateInstanceRequest { WorkflowAlias = "nonexistent" };
        var result = await _endpoints.CreateInstance(request, CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("workflow_not_found"));
    }

    // Task 4.3: GET list returns all instances
    [Test]
    public async Task ListInstances_ReturnsAllInstances()
    {
        var instances = new List<InstanceState>
        {
            CreateTestInstance("id1", "content-audit"),
            CreateTestInstance("id2", "seo-check")
        };

        _instanceManager.ListInstancesAsync(null, Arg.Any<CancellationToken>())
            .Returns(instances.AsReadOnly());

        var result = await _endpoints.ListInstances(null, CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var responses = okResult.Value as InstanceResponse[];
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!, Has.Length.EqualTo(2));
        Assert.That(responses[0].Id, Is.EqualTo("id1"));
        Assert.That(responses[1].Id, Is.EqualTo("id2"));
    }

    // Task 4.4: GET list with workflowAlias filter
    [Test]
    public async Task ListInstances_FiltersBy_WorkflowAlias()
    {
        var instances = new List<InstanceState>
        {
            CreateTestInstance("id1", "content-audit")
        };

        _instanceManager.ListInstancesAsync("content-audit", Arg.Any<CancellationToken>())
            .Returns(instances.AsReadOnly());

        var result = await _endpoints.ListInstances("content-audit", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var responses = okResult!.Value as InstanceResponse[];
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!, Has.Length.EqualTo(1));
        Assert.That(responses[0].WorkflowAlias, Is.EqualTo("content-audit"));

        await _instanceManager.Received(1).ListInstancesAsync("content-audit", Arg.Any<CancellationToken>());
    }

    // Task 4.5: GET detail returns full instance with steps
    [Test]
    public async Task GetInstance_ReturnsFullDetailWithSteps()
    {
        var state = CreateTestInstance();
        state.Steps[0].Status = StepStatus.Complete;
        state.Steps[0].StartedAt = new DateTime(2026, 3, 30, 10, 1, 0, DateTimeKind.Utc);
        state.Steps[0].CompletedAt = new DateTime(2026, 3, 30, 10, 5, 0, DateTimeKind.Utc);

        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var registered = new RegisteredWorkflow("content-audit", "/workflows/content-audit", TestDefinition);
        _workflowRegistry.GetWorkflow("content-audit").Returns(registered);

        var result = await _endpoints.GetInstance("abc123", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var detail = okResult.Value as InstanceDetailResponse;
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Id, Is.EqualTo("abc123"));
        Assert.That(detail.CreatedBy, Is.EqualTo("admin"));
        Assert.That(detail.WorkflowName, Is.EqualTo("Content Audit"));
        Assert.That(detail.WorkflowMode, Is.EqualTo("interactive"));
        Assert.That(detail.Steps, Has.Length.EqualTo(2));
        Assert.That(detail.Steps[0].Id, Is.EqualTo("step-1"));
        Assert.That(detail.Steps[0].Name, Is.EqualTo("Analyse"));
        Assert.That(detail.Steps[0].Status, Is.EqualTo(StepStatus.Complete));
        Assert.That(detail.Steps[0].StartedAt, Is.Not.Null);
        Assert.That(detail.Steps[0].CompletedAt, Is.Not.Null);
        Assert.That(detail.Steps[0].WritesTo, Is.EqualTo(new[] { "scan-results.md" }));
        Assert.That(detail.Steps[1].Id, Is.EqualTo("step-2"));
        Assert.That(detail.Steps[1].Name, Is.EqualTo("Report"));
        Assert.That(detail.Steps[1].Status, Is.EqualTo(StepStatus.Pending));
        Assert.That(detail.Steps[1].WritesTo, Is.EqualTo(new[] { "quality-scores.md" }));
    }

    // Story 3.4 Task 1.6: GET detail falls back gracefully when workflow is missing
    [Test]
    public async Task GetInstance_FallsBackWhenWorkflowDeleted()
    {
        var state = CreateTestInstance();
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        _workflowRegistry.GetWorkflow("content-audit").Returns((RegisteredWorkflow?)null);

        var result = await _endpoints.GetInstance("abc123", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var detail = okResult!.Value as InstanceDetailResponse;
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.WorkflowName, Is.EqualTo(string.Empty));
        Assert.That(detail.WorkflowMode, Is.EqualTo("interactive"));
        Assert.That(detail.Steps[0].Name, Is.EqualTo("step-1"));
        Assert.That(detail.Steps[0].WritesTo, Is.Null);
        Assert.That(detail.Steps[1].Name, Is.EqualTo("step-2"));
    }

    // Task 4.6: GET detail returns 404 for unknown id
    [Test]
    public async Task GetInstance_Returns404_WhenNotFound()
    {
        _instanceManager.FindInstanceAsync("unknown", Arg.Any<CancellationToken>())
            .Returns((InstanceState?)null);

        var result = await _endpoints.GetInstance("unknown", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("instance_not_found"));
    }

    // Task 4.7: POST cancel sets status to Cancelled
    [Test]
    public async Task CancelInstance_SetsCancelledStatus()
    {
        var state = CreateTestInstance(status: InstanceStatus.Running);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var cancelledState = CreateTestInstance(status: InstanceStatus.Cancelled);
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(cancelledState);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var response = okResult.Value as InstanceResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(InstanceStatus.Cancelled));
    }

    // Task 4.8: POST cancel returns 409 when not running
    [Test]
    public async Task CancelInstance_Returns409_WhenAlreadyCompleted()
    {
        var state = CreateTestInstance(status: InstanceStatus.Completed);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult, Is.Not.Null);
        Assert.That(conflictResult!.StatusCode, Is.EqualTo(409));

        var error = conflictResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("invalid_status"));
    }

    [Test]
    public async Task CancelInstance_Returns409_WhenAlreadyCancelled()
    {
        var state = CreateTestInstance(status: InstanceStatus.Cancelled);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult, Is.Not.Null);
        Assert.That(conflictResult!.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task CancelInstance_Returns409_WhenFailed()
    {
        var state = CreateTestInstance(status: InstanceStatus.Failed);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult, Is.Not.Null);
        Assert.That(conflictResult!.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task CancelInstance_Returns404_WhenNotFound()
    {
        _instanceManager.FindInstanceAsync("unknown", Arg.Any<CancellationToken>())
            .Returns((InstanceState?)null);

        var result = await _endpoints.CancelInstance("unknown", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));
    }

    // Task 4.9: DELETE removes completed instance
    [Test]
    public async Task DeleteInstance_Returns204_WhenCompleted()
    {
        var state = CreateTestInstance(status: InstanceStatus.Completed);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.DeleteInstanceAsync("content-audit", "abc123", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var noContentResult = result as NoContentResult;
        Assert.That(noContentResult, Is.Not.Null);
        Assert.That(noContentResult!.StatusCode, Is.EqualTo(204));
    }

    [Test]
    public async Task DeleteInstance_Returns204_WhenFailed()
    {
        var state = CreateTestInstance(status: InstanceStatus.Failed);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.DeleteInstanceAsync("content-audit", "abc123", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var noContentResult = result as NoContentResult;
        Assert.That(noContentResult, Is.Not.Null);
    }

    [Test]
    public async Task DeleteInstance_Returns204_WhenCancelled()
    {
        var state = CreateTestInstance(status: InstanceStatus.Cancelled);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.DeleteInstanceAsync("content-audit", "abc123", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var noContentResult = result as NoContentResult;
        Assert.That(noContentResult, Is.Not.Null);
    }

    // Task 4.10: DELETE returns 409 when instance is pending/running
    [Test]
    public async Task DeleteInstance_Returns409_WhenPending()
    {
        var state = CreateTestInstance(status: InstanceStatus.Pending);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult, Is.Not.Null);
        Assert.That(conflictResult!.StatusCode, Is.EqualTo(409));

        var error = conflictResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("invalid_status"));
    }

    [Test]
    public async Task DeleteInstance_Returns409_WhenRunning()
    {
        var state = CreateTestInstance(status: InstanceStatus.Running);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult, Is.Not.Null);
        Assert.That(conflictResult!.StatusCode, Is.EqualTo(409));
    }

    // Task 4.11: DELETE returns 404 for unknown id
    [Test]
    public async Task DeleteInstance_Returns404_WhenNotFound()
    {
        _instanceManager.FindInstanceAsync("unknown", Arg.Any<CancellationToken>())
            .Returns((InstanceState?)null);

        var result = await _endpoints.DeleteInstance("unknown", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("instance_not_found"));
    }

    [Test]
    public async Task DeleteInstance_Returns404_WhenDeleteReturnsFalse()
    {
        var state = CreateTestInstance(status: InstanceStatus.Completed);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.DeleteInstanceAsync("content-audit", "abc123", Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));
    }

    // Task 4.12: All responses use correct HTTP status codes (covered above, plus cancel pending)
    [Test]
    public async Task CancelInstance_AcceptsPendingInstances()
    {
        var state = CreateTestInstance(status: InstanceStatus.Pending);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var cancelledState = CreateTestInstance(status: InstanceStatus.Cancelled);
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(cancelledState);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task ListInstances_ReturnsEmptyArray_WhenNoInstances()
    {
        _instanceManager.ListInstancesAsync(null, Arg.Any<CancellationToken>())
            .Returns(new List<InstanceState>().AsReadOnly());

        var result = await _endpoints.ListInstances(null, CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var responses = okResult!.Value as InstanceResponse[];
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!, Is.Empty);
    }

    // Story 10.8 Task 9.1: cancel calls RequestCancellation on registry
    [Test]
    public async Task CancelInstance_CallsRequestCancellationOnRegistry()
    {
        var state = CreateTestInstance(status: InstanceStatus.Running);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(CreateTestInstance(status: InstanceStatus.Cancelled));

        await _endpoints.CancelInstance("abc123", CancellationToken.None);

        _activeInstanceRegistry.Received(1).RequestCancellation("abc123");
    }

    // Story 10.8 Task 9.2: cancel order — persist before signal (locked decision 3)
    [Test]
    public async Task CancelInstance_PersistsCancelledBeforeSignallingCts()
    {
        var state = CreateTestInstance(status: InstanceStatus.Running);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(CreateTestInstance(status: InstanceStatus.Cancelled));

        var callOrder = new List<string>();
        _instanceManager
            .When(m => m.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("persist"));
        _activeInstanceRegistry
            .When(r => r.RequestCancellation("abc123"))
            .Do(_ => callOrder.Add("signal"));

        await _endpoints.CancelInstance("abc123", CancellationToken.None);

        Assert.That(callOrder, Is.EqualTo(new[] { "persist", "signal" }));
    }

    // Story 10.8 F2 / AC8: 409 rejections do not reach the registry
    [Test]
    public async Task CancelInstance_DoesNotCallRequestCancellation_When409()
    {
        var state = CreateTestInstance(status: InstanceStatus.Completed);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        await _endpoints.CancelInstance("abc123", CancellationToken.None);

        _activeInstanceRegistry.DidNotReceive().RequestCancellation(Arg.Any<string>());
    }

    // ---------------- Story 10.9: Interrupted guards ---------------- //

    // Story 10.9 AC8: cancel on an Interrupted instance returns 409 — the existing
    // `not (Running or Pending)` guard already handles this. No code change required;
    // this test pins the behaviour so a future refactor of the guard catches regressions.
    [Test]
    public async Task CancelInstance_Returns409_WhenInterrupted()
    {
        var state = CreateTestInstance(status: InstanceStatus.Interrupted);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.That(conflictResult, Is.Not.Null);
        Assert.That(conflictResult!.StatusCode, Is.EqualTo(409));

        var error = conflictResult.Value as ErrorResponse;
        Assert.That(error?.Error, Is.EqualTo("invalid_status"));

        // No CTS signal attempted — guard short-circuits before RequestCancellation.
        _activeInstanceRegistry.DidNotReceive().RequestCancellation(Arg.Any<string>());
    }

    // Story 10.9 AC7: delete on an Interrupted instance returns 204 — users need
    // an exit other than Retry.
    [Test]
    public async Task DeleteInstance_Returns204_WhenInterrupted()
    {
        var state = CreateTestInstance(status: InstanceStatus.Interrupted);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.DeleteInstanceAsync("content-audit", "abc123", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _endpoints.DeleteInstance("abc123", CancellationToken.None);

        var noContentResult = result as NoContentResult;
        Assert.That(noContentResult, Is.Not.Null);
        Assert.That(noContentResult!.StatusCode, Is.EqualTo(204));

        await _instanceManager.Received(1).DeleteInstanceAsync(
            "content-audit", "abc123", Arg.Any<CancellationToken>());
    }

    // Story 10.8 AC2: cancel is safe when no orchestrator is active (registry no-op)
    [Test]
    public async Task CancelInstance_ReturnsOk_WhenRegistryHasNoEntry()
    {
        var state = CreateTestInstance(status: InstanceStatus.Pending);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(state);
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(CreateTestInstance(status: InstanceStatus.Cancelled));

        // RequestCancellation is a no-op by default on the substitute; no setup needed.
        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
        _activeInstanceRegistry.Received(1).RequestCancellation("abc123");
    }

    // ---------------- Story 10.10: step-status cleanup on cancel ---------------- //

    private static InstanceState CreateRunningInstanceWithStepStatuses(params StepStatus[] stepStatuses)
    {
        var steps = stepStatuses
            .Select((s, i) => new StepState { Id = $"step-{i + 1}", Status = s })
            .ToList();
        return new InstanceState
        {
            InstanceId = "abc123",
            WorkflowAlias = "content-audit",
            Status = InstanceStatus.Running,
            CurrentStepIndex = 0,
            CreatedAt = new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 30, 10, 0, 0, DateTimeKind.Utc),
            CreatedBy = "admin",
            Steps = steps
        };
    }

    // Story 10.10 AC10: cancel finds the Active step and sets it to Cancelled
    [Test]
    public async Task CancelInstance_WithActiveStep_SetsStepToCancelled()
    {
        var running = CreateRunningInstanceWithStepStatuses(StepStatus.Complete, StepStatus.Active);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(running);

        // After SetInstanceStatusAsync the returned state retains the Active step —
        // the cleanup loop is what transitions it. Mirror production shape.
        var afterInstanceCancel = CreateRunningInstanceWithStepStatuses(StepStatus.Complete, StepStatus.Active);
        afterInstanceCancel.Status = InstanceStatus.Cancelled;
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterInstanceCancel);

        var afterStepCancel = CreateRunningInstanceWithStepStatuses(StepStatus.Complete, StepStatus.Cancelled);
        afterStepCancel.Status = InstanceStatus.Cancelled;
        _instanceManager.UpdateStepStatusAsync("content-audit", "abc123", 1, StepStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterStepCancel);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        Assert.That(result, Is.TypeOf<OkObjectResult>());

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "content-audit", "abc123", 1, StepStatus.Cancelled, Arg.Any<CancellationToken>());
        _activeInstanceRegistry.Received(1).RequestCancellation("abc123");
    }

    // Story 10.10 AC11: cancel with no Active step still succeeds (between-steps window)
    [Test]
    public async Task CancelInstance_WithNoActiveStep_StillSucceeds()
    {
        var running = CreateRunningInstanceWithStepStatuses(StepStatus.Complete, StepStatus.Pending);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(running);

        var afterInstanceCancel = CreateRunningInstanceWithStepStatuses(StepStatus.Complete, StepStatus.Pending);
        afterInstanceCancel.Status = InstanceStatus.Cancelled;
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterInstanceCancel);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        Assert.That(result, Is.TypeOf<OkObjectResult>());

        await _instanceManager.Received(1).SetInstanceStatusAsync(
            "content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>());
        await _instanceManager.DidNotReceive().UpdateStepStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>());
        _activeInstanceRegistry.Received(1).RequestCancellation("abc123");
    }

    // Story 10.10 AC12: pathological guard — every Active step gets cleaned up
    [Test]
    public async Task CancelInstance_WithMultipleActiveSteps_CleansAllOfThem()
    {
        var running = CreateRunningInstanceWithStepStatuses(StepStatus.Active, StepStatus.Active);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(running);

        var afterInstanceCancel = CreateRunningInstanceWithStepStatuses(StepStatus.Active, StepStatus.Active);
        afterInstanceCancel.Status = InstanceStatus.Cancelled;
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterInstanceCancel);

        // First cleanup call returns state where index 0 is Cancelled; second call's read
        // sees index 1 still Active. Both writes must land.
        var afterFirstCleanup = CreateRunningInstanceWithStepStatuses(StepStatus.Cancelled, StepStatus.Active);
        afterFirstCleanup.Status = InstanceStatus.Cancelled;
        _instanceManager.UpdateStepStatusAsync("content-audit", "abc123", 0, StepStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterFirstCleanup);

        var afterSecondCleanup = CreateRunningInstanceWithStepStatuses(StepStatus.Cancelled, StepStatus.Cancelled);
        afterSecondCleanup.Status = InstanceStatus.Cancelled;
        _instanceManager.UpdateStepStatusAsync("content-audit", "abc123", 1, StepStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterSecondCleanup);

        var result = await _endpoints.CancelInstance("abc123", CancellationToken.None);

        Assert.That(result, Is.TypeOf<OkObjectResult>());

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "content-audit", "abc123", 0, StepStatus.Cancelled, Arg.Any<CancellationToken>());
        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "content-audit", "abc123", 1, StepStatus.Cancelled, Arg.Any<CancellationToken>());
    }

    // Story 10.10 AC15 / "persist first, signal second" ordering regression guard
    [Test]
    public async Task CancelInstance_StepCleanupRunsBeforeRequestCancellation()
    {
        var running = CreateRunningInstanceWithStepStatuses(StepStatus.Active);
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(running);

        var afterInstanceCancel = CreateRunningInstanceWithStepStatuses(StepStatus.Active);
        afterInstanceCancel.Status = InstanceStatus.Cancelled;
        _instanceManager.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterInstanceCancel);

        var afterStepCancel = CreateRunningInstanceWithStepStatuses(StepStatus.Cancelled);
        afterStepCancel.Status = InstanceStatus.Cancelled;
        _instanceManager.UpdateStepStatusAsync("content-audit", "abc123", 0, StepStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(afterStepCancel);

        var callOrder = new List<string>();
        _instanceManager
            .When(m => m.SetInstanceStatusAsync("content-audit", "abc123", InstanceStatus.Cancelled, Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("set-instance-status"));
        _instanceManager
            .When(m => m.UpdateStepStatusAsync("content-audit", "abc123", 0, StepStatus.Cancelled, Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("update-step-status"));
        _activeInstanceRegistry
            .When(r => r.RequestCancellation("abc123"))
            .Do(_ => callOrder.Add("signal"));

        await _endpoints.CancelInstance("abc123", CancellationToken.None);

        Assert.That(callOrder, Is.EqualTo(new[] { "set-instance-status", "update-step-status", "signal" }));
    }

    // Story 10.1 AC12 + Task 6.11: cancel endpoint's SetInstanceStatusAsync(Cancelled)
    // serialises on the per-instance lock against a concurrent orchestrator mutation.
    // Uses a REAL InstanceManager so the SemaphoreSlim is exercised (mocks can't model
    // serialisation). An "orchestrator holder" task hammers UpdateStepStatusAsync to
    // keep the lock hot; cancel must still complete promptly and the final persisted
    // state must be Cancelled — no torn or lost write.
    [Test]
    public async Task CancelInstance_SerialisesAgainstConcurrentOrchestratorMutation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agentrun-ac12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var managerLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InstanceManager>>();
            var manager = new InstanceManager(tempDir, managerLogger);
            var registry = Substitute.For<IActiveInstanceRegistry>();
            var workflowRegistry = Substitute.For<IWorkflowRegistry>();
            var endpoints = new InstanceEndpoints(manager, workflowRegistry, registry);

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin@example.com")], "test");
            endpoints.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            var definition = new WorkflowDefinition
            {
                Name = "AC12",
                Description = "ac12",
                Mode = "interactive",
                Steps = [new StepDefinition { Id = "s1", Name = "Step 1", Agent = "a.md" }]
            };
            var created = await manager.CreateInstanceAsync("ac12", definition, "admin", CancellationToken.None);
            await manager.SetInstanceStatusAsync("ac12", created.InstanceId, InstanceStatus.Running, CancellationToken.None);

            // Simulate an orchestrator flipping step status under the per-instance
            // lock. Each iteration acquires + releases the lock, so the cancel
            // endpoint's own SetInstanceStatusAsync(Cancelled) may have to wait
            // for one in-flight iteration — sub-second on any realistic machine.
            using var stopOrchestrator = new CancellationTokenSource();
            var orchestratorHolder = Task.Run(async () =>
            {
                var nextStatus = StepStatus.Active;
                while (!stopOrchestrator.IsCancellationRequested)
                {
                    try
                    {
                        await manager.UpdateStepStatusAsync("ac12", created.InstanceId, 0, nextStatus, CancellationToken.None);
                        nextStatus = nextStatus == StepStatus.Active ? StepStatus.Pending : StepStatus.Active;
                    }
                    catch (OperationCanceledException) { break; }
                }
            });

            // Let the holder get a few iterations in before the cancel fires.
            await Task.Delay(50);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await endpoints.CancelInstance(created.InstanceId, CancellationToken.None);
            sw.Stop();

            stopOrchestrator.Cancel();
            try { await orchestratorHolder; } catch { /* expected */ }

            Assert.That(result, Is.InstanceOf<OkObjectResult>(),
                "Cancel endpoint should return 200 OK for a running instance it successfully cancelled.");

            // Cancel must complete promptly despite the concurrent holder — a single
            // in-flight iteration (~ms) is the worst-case wait.
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000),
                $"Cancel took {sw.ElapsedMilliseconds}ms — lock serialisation appears to be blocking beyond the orchestrator's critical section.");

            var finalState = await manager.GetInstanceAsync("ac12", created.InstanceId, CancellationToken.None);
            Assert.That(finalState!.Status, Is.EqualTo(InstanceStatus.Cancelled),
                "Final persisted state must be Cancelled — cancel observed the orchestrator's writes and its own write landed under the lock.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
