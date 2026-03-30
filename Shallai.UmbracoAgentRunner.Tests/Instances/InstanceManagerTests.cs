using Microsoft.Extensions.Logging;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Instances;

[TestFixture]
public class InstanceManagerTests
{
    private string _tempDir = null!;
    private InstanceManager _manager = null!;

    private static WorkflowDefinition CreateTestDefinition() => new()
    {
        Name = "Test Workflow",
        Description = "A test workflow",
        Mode = "autonomous",
        Steps =
        [
            new StepDefinition { Id = "step-one", Name = "Step One", Agent = "agents/one.md" },
            new StepDefinition { Id = "step-two", Name = "Step Two", Agent = "agents/two.md" }
        ]
    };

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shallai-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var logger = Substitute.For<ILogger<InstanceManager>>();
        _manager = new InstanceManager(_tempDir, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task CreateInstance_ProducesCorrectFolderStructureAndContent()
    {
        var definition = CreateTestDefinition();

        var state = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        // Folder exists
        var instanceDir = Path.Combine(_tempDir, "test-workflow", state.InstanceId);
        Assert.That(Directory.Exists(instanceDir), Is.True);

        // instance.yaml exists
        var yamlPath = Path.Combine(instanceDir, "instance.yaml");
        Assert.That(File.Exists(yamlPath), Is.True);

        // State fields
        Assert.That(state.WorkflowAlias, Is.EqualTo("test-workflow"));
        Assert.That(state.CurrentStepIndex, Is.EqualTo(0));
        Assert.That(state.Status, Is.EqualTo(InstanceStatus.Pending));
        Assert.That(state.CreatedBy, Is.EqualTo("admin@example.com"));
        Assert.That(state.Steps, Has.Count.EqualTo(2));
        Assert.That(state.Steps[0].Id, Is.EqualTo("step-one"));
        Assert.That(state.Steps[0].Status, Is.EqualTo(StepStatus.Pending));
        Assert.That(state.Steps[1].Id, Is.EqualTo("step-two"));
        Assert.That(state.Steps[1].Status, Is.EqualTo(StepStatus.Pending));
        Assert.That(state.InstanceId, Has.Length.EqualTo(32));
    }

    [Test]
    public async Task CreateInstance_YamlUsesSnakeCaseFieldNames()
    {
        var definition = CreateTestDefinition();

        var state = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        var yamlPath = Path.Combine(_tempDir, "test-workflow", state.InstanceId, "instance.yaml");
        var yaml = await File.ReadAllTextAsync(yamlPath);

        Assert.That(yaml, Does.Contain("workflow_alias:"));
        Assert.That(yaml, Does.Contain("current_step_index:"));
        Assert.That(yaml, Does.Contain("created_at:"));
        Assert.That(yaml, Does.Contain("updated_at:"));
        Assert.That(yaml, Does.Contain("created_by:"));
        Assert.That(yaml, Does.Contain("instance_id:"));
        // Verify PascalCase is NOT present
        Assert.That(yaml, Does.Not.Contain("WorkflowAlias"));
        Assert.That(yaml, Does.Not.Contain("CurrentStepIndex"));
    }

    [Test]
    public async Task GetInstance_ReturnsEquivalentState()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        var readBack = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

        Assert.That(readBack, Is.Not.Null);
        Assert.That(readBack!.WorkflowAlias, Is.EqualTo(created.WorkflowAlias));
        Assert.That(readBack.InstanceId, Is.EqualTo(created.InstanceId));
        Assert.That(readBack.CurrentStepIndex, Is.EqualTo(created.CurrentStepIndex));
        Assert.That(readBack.Status, Is.EqualTo(created.Status));
        Assert.That(readBack.CreatedBy, Is.EqualTo(created.CreatedBy));
        Assert.That(readBack.Steps, Has.Count.EqualTo(created.Steps.Count));
        Assert.That(readBack.Steps[0].Id, Is.EqualTo(created.Steps[0].Id));
    }

    [Test]
    public async Task GetInstance_NonExistent_ReturnsNull()
    {
        var result = await _manager.GetInstanceAsync("test-workflow", "nonexistent", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpdateStepStatus_PersistsCorrectlyAndUpdatesTimestamps()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        var originalUpdatedAt = created.UpdatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(10);

        var updated = await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Active, CancellationToken.None);

        Assert.That(updated.Steps[0].Status, Is.EqualTo(StepStatus.Active));
        Assert.That(updated.Steps[0].StartedAt, Is.Not.Null);
        Assert.That(updated.UpdatedAt, Is.GreaterThan(originalUpdatedAt));

        // Verify persisted to disk
        var readBack = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);
        Assert.That(readBack!.Steps[0].Status, Is.EqualTo(StepStatus.Active));
    }

    [Test]
    public async Task UpdateStepStatus_CompleteStatus_SetsCompletedAt()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Active, CancellationToken.None);
        var completed = await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Complete, CancellationToken.None);

        Assert.That(completed.Steps[0].CompletedAt, Is.Not.Null);
        Assert.That(completed.Steps[0].StartedAt, Is.Not.Null);
    }

    [Test]
    public async Task SetInstanceStatus_RunningWhenAlreadyRunning_ThrowsInvalidOperationException()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);

        Assert.That(
            async () => await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("already running"));
    }

    [Test]
    public async Task ListInstances_ReturnsAllInstancesForWorkflowAlias()
    {
        var definition = CreateTestDefinition();
        await _manager.CreateInstanceAsync("workflow-a", definition, "admin@example.com", CancellationToken.None);
        await _manager.CreateInstanceAsync("workflow-a", definition, "admin@example.com", CancellationToken.None);
        await _manager.CreateInstanceAsync("workflow-b", definition, "admin@example.com", CancellationToken.None);

        var results = await _manager.ListInstancesAsync("workflow-a", CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(r => r.WorkflowAlias == "workflow-a"), Is.True);
    }

    [Test]
    public async Task ListInstances_NullAlias_ReturnsInstancesAcrossAllWorkflows()
    {
        var definition = CreateTestDefinition();
        await _manager.CreateInstanceAsync("workflow-a", definition, "admin@example.com", CancellationToken.None);
        await _manager.CreateInstanceAsync("workflow-b", definition, "admin@example.com", CancellationToken.None);

        var results = await _manager.ListInstancesAsync(null, CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DeleteInstance_RemovesFolderForCompletedInstance()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Completed, CancellationToken.None);

        var deleted = await _manager.DeleteInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

        Assert.That(deleted, Is.True);
        var instanceDir = Path.Combine(_tempDir, "test-workflow", created.InstanceId);
        Assert.That(Directory.Exists(instanceDir), Is.False);
    }

    [Test]
    public async Task DeleteInstance_RejectsRunningInstance()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);

        Assert.That(
            async () => await _manager.DeleteInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Cannot delete"));
    }

    [Test]
    public async Task DeleteInstance_RemovesFolderForFailedInstance()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Failed, CancellationToken.None);

        var deleted = await _manager.DeleteInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

        Assert.That(deleted, Is.True);
        var instanceDir = Path.Combine(_tempDir, "test-workflow", created.InstanceId);
        Assert.That(Directory.Exists(instanceDir), Is.False);
    }

    [Test]
    public async Task DeleteInstance_RemovesFolderForCancelledInstance()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Cancelled, CancellationToken.None);

        var deleted = await _manager.DeleteInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

        Assert.That(deleted, Is.True);
        var instanceDir = Path.Combine(_tempDir, "test-workflow", created.InstanceId);
        Assert.That(Directory.Exists(instanceDir), Is.False);
    }

    [Test]
    public async Task DeleteInstance_RejectsPendingInstance()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        Assert.That(
            async () => await _manager.DeleteInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("Cannot delete"));
    }

    [Test]
    public async Task InterruptedInstanceDetection_RunningWithCompleteStep_CanBeIdentified()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        // Simulate partial execution: set running + first step complete
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Complete, CancellationToken.None);

        // Read back and verify detection criteria
        var state = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Status, Is.EqualTo(InstanceStatus.Running));
        Assert.That(state.Steps.Any(s => s.Status == StepStatus.Complete), Is.True);

        // This is the detection pattern: running + at least one complete step = interrupted
        var isInterrupted = state.Status == InstanceStatus.Running
            && state.Steps.Any(s => s.Status == StepStatus.Complete);
        Assert.That(isInterrupted, Is.True);
    }

    [Test]
    public void GetInstance_PathTraversal_ThrowsArgumentException()
    {
        Assert.That(
            async () => await _manager.GetInstanceAsync("../../etc", "passwd", CancellationToken.None),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains("Path traversal"));
    }

    [Test]
    public async Task FindInstance_ReturnsInstanceAcrossWorkflows()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("workflow-a", definition, "admin@example.com", CancellationToken.None);

        var found = await _manager.FindInstanceAsync(created.InstanceId, CancellationToken.None);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.InstanceId, Is.EqualTo(created.InstanceId));
        Assert.That(found.WorkflowAlias, Is.EqualTo("workflow-a"));
    }

    [Test]
    public async Task FindInstance_ReturnsNull_WhenNotFound()
    {
        var result = await _manager.FindInstanceAsync("nonexistent", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindInstance_ReturnsNull_WhenNoDataRoot()
    {
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "shallai-nonexistent-" + Guid.NewGuid().ToString("N"));
        var logger = NSubstitute.Substitute.For<ILogger<InstanceManager>>();
        var manager = new InstanceManager(nonExistentDir, logger);

        var result = await manager.FindInstanceAsync("a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    [TestCase("../traversal")]
    [TestCase("abc")]
    [TestCase("ABCDEF01234567890ABCDEF012345678")]
    [TestCase("a0b1c2d3-e4f5-a6b7-c8d9-e0f1a2b3c4d5")]
    public async Task FindInstance_ReturnsNull_WhenInstanceIdFormatInvalid(string invalidId)
    {
        var result = await _manager.FindInstanceAsync(invalidId, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AtomicWrites_NoTmpFilePersistedAfterWrite()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        var instanceDir = Path.Combine(_tempDir, "test-workflow", created.InstanceId);
        var files = Directory.GetFiles(instanceDir);

        Assert.That(files, Has.Length.EqualTo(1));
        Assert.That(files[0], Does.EndWith("instance.yaml"));
        Assert.That(files.Any(f => f.EndsWith(".tmp")), Is.False);
    }
}
