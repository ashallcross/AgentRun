using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using AgentRun.Umbraco.Endpoints;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;

namespace AgentRun.Umbraco.Tests.Endpoints;

[TestFixture]
public class ConversationEndpointsTests
{
    private IConversationStore _conversationStore = null!;
    private IInstanceManager _instanceManager = null!;
    private ConversationEndpoints _endpoints = null!;

    private static InstanceState CreateTestInstance(
        string instanceId = "abc123",
        string workflowAlias = "content-audit") => new()
    {
        InstanceId = instanceId,
        WorkflowAlias = workflowAlias,
        Status = InstanceStatus.Running,
        CurrentStepIndex = 0,
        CreatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
        CreatedBy = "admin",
        Steps =
        [
            new StepState { Id = "step-one", Status = StepStatus.Active },
            new StepState { Id = "step-two", Status = StepStatus.Pending }
        ]
    };

    [SetUp]
    public void SetUp()
    {
        _conversationStore = Substitute.For<IConversationStore>();
        _instanceManager = Substitute.For<IInstanceManager>();
        _endpoints = new ConversationEndpoints(_conversationStore, _instanceManager);
    }

    [Test]
    public async Task GetConversation_ValidInstanceAndStep_Returns200WithEntries()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(instance);

        var entries = new List<ConversationEntry>
        {
            new() { Role = "system", Content = "You are an auditor.", Timestamp = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc) },
            new() { Role = "assistant", Content = "I'll start the audit.", Timestamp = new DateTime(2026, 4, 1, 10, 0, 1, DateTimeKind.Utc) }
        };
        _conversationStore.GetHistoryAsync("content-audit", "abc123", "step-one", Arg.Any<CancellationToken>())
            .Returns(entries.AsReadOnly());

        var result = await _endpoints.GetConversation("abc123", "step-one", CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var returnedEntries = okResult.Value as IReadOnlyList<ConversationEntry>;
        Assert.That(returnedEntries, Is.Not.Null);
        Assert.That(returnedEntries!, Has.Count.EqualTo(2));
        Assert.That(returnedEntries[0].Role, Is.EqualTo("system"));
        Assert.That(returnedEntries[1].Role, Is.EqualTo("assistant"));
    }

    [Test]
    public async Task GetConversation_NonExistentInstance_Returns404()
    {
        _instanceManager.FindInstanceAsync("unknown", Arg.Any<CancellationToken>())
            .Returns((InstanceState?)null);

        var result = await _endpoints.GetConversation("unknown", "step-one", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("instance_not_found"));
    }

    [Test]
    public async Task GetConversation_StepNotInInstance_Returns404()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(instance);

        var result = await _endpoints.GetConversation("abc123", "nonexistent-step", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("step_not_found"));
    }
}
