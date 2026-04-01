using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Endpoints;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Endpoints;

[TestFixture]
public class InstanceEndpointsTests
{
    private IInstanceManager _instanceManager = null!;
    private IWorkflowRegistry _workflowRegistry = null!;
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
        _endpoints = new InstanceEndpoints(_instanceManager, _workflowRegistry);

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
}
