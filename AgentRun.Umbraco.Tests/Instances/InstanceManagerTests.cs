using Microsoft.Extensions.Logging;
using NSubstitute;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Instances;

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
        _tempDir = Path.Combine(Path.GetTempPath(), "agentrun-tests-" + Guid.NewGuid().ToString("N"));
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
            Throws.TypeOf<InstanceAlreadyRunningException>()
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
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "agentrun-nonexistent-" + Guid.NewGuid().ToString("N"));
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

    // ---------------- Story 10.9: Interrupted status ---------------- //

    // Story 10.9 AC10 + locked decision 3: Interrupted is NOT a terminal state.
    // The terminal-transition guard must allow Interrupted → Running so Retry can
    // recover the run. Adding Interrupted to the guard would break Retry.
    [Test]
    public async Task SetInstanceStatusAsync_InterruptedToRunning_Allowed()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        // Pending → Interrupted (via Running → Interrupted to stay on a plausible code path).
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Interrupted, CancellationToken.None);

        // Interrupted → Running must succeed (this is the Retry transition).
        var resumed = await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);

        Assert.That(resumed.Status, Is.EqualTo(InstanceStatus.Running));

        // Verify the on-disk YAML reflects the transition (guard didn't silently refuse).
        var readBack = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);
        Assert.That(readBack!.Status, Is.EqualTo(InstanceStatus.Running));
    }

    // Story 10.9 AC4: Interrupted round-trips through the YAML serializer/deserializer.
    [Test]
    public async Task SetInstanceStatusAsync_Interrupted_RoundTripsThroughYaml()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Interrupted, CancellationToken.None);

        // Read-back via FindInstanceAsync exercises the full deserialization path.
        var readBack = await _manager.FindInstanceAsync(created.InstanceId, CancellationToken.None);

        Assert.That(readBack, Is.Not.Null);
        Assert.That(readBack!.Status, Is.EqualTo(InstanceStatus.Interrupted));
    }

    // Story 10.9 AC7 (engine side): DeleteInstanceAsync accepts Interrupted.
    [Test]
    public async Task DeleteInstanceAsync_OnInterrupted_Succeeds()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Interrupted, CancellationToken.None);

        var deleted = await _manager.DeleteInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

        Assert.That(deleted, Is.True);
        var instanceDir = Path.Combine(_tempDir, "test-workflow", created.InstanceId);
        Assert.That(Directory.Exists(instanceDir), Is.False);
    }

    // Story 10.9 AC4: JSON serialization of Interrupted produces "Interrupted".
    [Test]
    public void InstanceStatus_Interrupted_SerializesAsInterruptedString()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(InstanceStatus.Interrupted);

        Assert.That(json, Is.EqualTo("\"Interrupted\""));

        // Round-trip through deserialization.
        var parsed = System.Text.Json.JsonSerializer.Deserialize<InstanceStatus>(json);
        Assert.That(parsed, Is.EqualTo(InstanceStatus.Interrupted));
    }

    // Story 10.10 AC9: JSON serialization of StepStatus.Cancelled produces "Cancelled".
    [Test]
    public void StepStatus_Cancelled_SerializesAsCancelledString()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(StepStatus.Cancelled);

        Assert.That(json, Is.EqualTo("\"Cancelled\""));

        var parsed = System.Text.Json.JsonSerializer.Deserialize<StepStatus>(json);
        Assert.That(parsed, Is.EqualTo(StepStatus.Cancelled));
    }

    // Story 10.10 AC9: YAML round-trip — UpdateStepStatusAsync persists Cancelled and
    // reads back as Cancelled. Pins the on-disk enum representation for cancel cleanup.
    [Test]
    public async Task UpdateStepStatusAsync_SetsCancelledStatus()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Active, CancellationToken.None);
        var cancelled = await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Cancelled, CancellationToken.None);

        Assert.That(cancelled.Steps[0].Status, Is.EqualTo(StepStatus.Cancelled));

        // Verify persisted to disk (YAML round-trip).
        var readBack = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);
        Assert.That(readBack!.Steps[0].Status, Is.EqualTo(StepStatus.Cancelled));
    }

    // Story 10.9 manual E2E code review: Retry(Failed) must transition Failed → Running.
    // Pre-10.9 the terminal-transition guard refused this silently, leaving Retry broken —
    // RetryInstance would reset the step, call SetInstanceStatusAsync(Running) which no-op'd,
    // and the orchestrator ran with the stale Failed status so the UI stayed stuck on Retry.
    // The fix narrows the guard to allow transitions INTO Running (Retry is the only path
    // that ever writes Running to a terminal state; RetryInstance's gate only admits
    // Failed|Interrupted, and StartInstance rejects all terminals).
    [Test]
    public async Task SetInstanceStatusAsync_FailedToRunning_Allowed()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Failed, CancellationToken.None);

        var retried = await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);

        Assert.That(retried.Status, Is.EqualTo(InstanceStatus.Running));

        var readBack = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);
        Assert.That(readBack!.Status, Is.EqualTo(InstanceStatus.Running));
    }

    // Story 10.9 manual E2E code review: same for Cancelled → Running (defensive —
    // RetryInstance's gate does not admit Cancelled, but the engine-level method
    // allows the transition for symmetry. If a future Retry-Cancelled path is
    // introduced, the guard won't need to change.)
    [Test]
    public async Task SetInstanceStatusAsync_CancelledToRunning_Allowed()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Cancelled, CancellationToken.None);

        var recovered = await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);

        Assert.That(recovered.Status, Is.EqualTo(InstanceStatus.Running));
    }

    // Story 10.8 regression guard for Story 10.9: terminal-transition guard still
    // refuses sideways transitions between terminal states (Completed → Interrupted,
    // Completed → Cancelled, Failed → Cancelled, etc.). Only transitions INTO Running
    // are permitted post-10.9 guard tightening.
    [Test]
    public async Task SetInstanceStatusAsync_TerminalToAnything_Refused()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Completed, CancellationToken.None);

        // Terminal → anything else: guard returns state unchanged (does NOT throw, logs Info).
        var attempted = await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Interrupted, CancellationToken.None);

        Assert.That(attempted.Status, Is.EqualTo(InstanceStatus.Completed));

        var readBack = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);
        Assert.That(readBack!.Status, Is.EqualTo(InstanceStatus.Completed));
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

    // -------------- Story 10.1: per-instance locking -------------- //

    // AC1 + AC2: Lost-update prevention across mutation methods. Without the
    // per-instance lock, two concurrent read-modify-write operations can each
    // load the pre-write state, produce independent mutations, and race their
    // writes — the loser's changes silently vanish. Running both on distinct
    // fields of the same state (Status vs Steps[0]) with iteration flushes out
    // any non-determinism: with the lock both writes always land; without it,
    // at least one iteration will observe a lost update.
    [Test]
    public async Task Lock_ConcurrentSetStatusAndUpdateStep_NeitherWriteLost()
    {
        var definition = CreateTestDefinition();

        const int iterations = 50;
        for (var i = 0; i < iterations; i++)
        {
            var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

            var barrier = new Barrier(2);
            var t1 = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
            });
            var t2 = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Active, CancellationToken.None);
            });

            await Task.WhenAll(t1, t2);

            var final = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

            Assert.That(final, Is.Not.Null);
            Assert.That(final!.Status, Is.EqualTo(InstanceStatus.Running),
                $"Iteration {i}: Status write was lost — concurrent UpdateStepStatus overwrote it.");
            Assert.That(final.Steps[0].Status, Is.EqualTo(StepStatus.Active),
                $"Iteration {i}: Steps[0].Status write was lost — concurrent SetInstanceStatus overwrote it.");
        }
    }

    // AC2: Two concurrent UpdateStepStatusAsync calls on DIFFERENT steps of the
    // same instance serialise and both mutations land. Iterated so a lost-update
    // regression surfaces non-deterministically rather than passing by luck on a
    // single trial.
    [Test]
    public async Task Lock_ConcurrentUpdateStepStatus_DifferentSteps_BothWritesLand()
    {
        var definition = CreateTestDefinition();

        const int iterations = 50;
        for (var i = 0; i < iterations; i++)
        {
            var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

            var barrier = new Barrier(2);
            var t1 = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 0, StepStatus.Complete, CancellationToken.None);
            });
            var t2 = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, 1, StepStatus.Active, CancellationToken.None);
            });

            await Task.WhenAll(t1, t2);

            var final = await _manager.GetInstanceAsync("test-workflow", created.InstanceId, CancellationToken.None);

            Assert.That(final, Is.Not.Null);
            Assert.That(final!.Steps[0].Status, Is.EqualTo(StepStatus.Complete),
                $"Iteration {i}: Steps[0] write was lost.");
            Assert.That(final.Steps[1].Status, Is.EqualTo(StepStatus.Active),
                $"Iteration {i}: Steps[1] write was lost.");
        }
    }

    // AC7: Per-instance scope — a write on instance A does not block a write on
    // instance B. Deterministic variant: grab A's SemaphoreSlim via reflection,
    // hold it from outside InstanceManager, then prove B's write completes
    // without waiting on A. The holder releases A at the end, confirming no
    // semaphore was leaked.
    [Test]
    public async Task Lock_DifferentInstances_DoNotBlockEachOther()
    {
        var definition = CreateTestDefinition();
        var a = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
        var b = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        // Touch A's lock once so it's allocated in the dictionary, then grab
        // the SemaphoreSlim via reflection and hold it outside InstanceManager.
        await _manager.SetInstanceStatusAsync("test-workflow", a.InstanceId, InstanceStatus.Running, CancellationToken.None);

        var locksField = typeof(InstanceManager).GetField(
            "_instanceLocks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate _instanceLocks field via reflection.");
        var locks = (System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>)locksField.GetValue(_manager)!;
        var aSemaphore = locks[a.InstanceId];

        // Hold A's semaphore. Any SetInstanceStatusAsync on A would block here.
        await aSemaphore.WaitAsync();

        try
        {
            // B must complete without waiting on A's held semaphore. A 2-second
            // NUnit-level timeout would catch a deadlock; the assertion below
            // catches a subtle cross-instance contention regression.
            var bTask = _manager.SetInstanceStatusAsync("test-workflow", b.InstanceId, InstanceStatus.Running, CancellationToken.None);
            var completed = await Task.WhenAny(bTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.That(completed, Is.SameAs(bTask),
                "Instance B's write did not complete within 2s while A's lock was held — locks are not per-instance.");
            await bTask; // observe any exception
        }
        finally
        {
            aSemaphore.Release();
        }
    }

    // AC6: Lock is released on exception — a failing mutation does not leave the
    // lock held. We trigger an ArgumentOutOfRangeException by passing an invalid
    // stepIndex; the finally block must release so a subsequent call proceeds.
    [Test]
    public async Task Lock_ReleasedOnException_SubsequentCallProceeds()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, stepIndex: 99, StepStatus.Complete, CancellationToken.None));

        // Subsequent call on the same instance must not deadlock — test completes
        // with a reasonable timeout (NUnit default per-test) if the lock was
        // released in finally.
        var result = await _manager.UpdateStepStatusAsync("test-workflow", created.InstanceId, stepIndex: 0, StepStatus.Complete, CancellationToken.None);
        Assert.That(result.Steps[0].Status, Is.EqualTo(StepStatus.Complete));
    }

    // AC5: WaitAsync cancellation does not spuriously release the lock. When a
    // caller's token fires before the lock is acquired, WaitAsync throws OCE and
    // the finally must NOT run Release (the lock was never held). We verify the
    // semaphore is still in a usable 1-permit state by running a subsequent
    // successful write.
    [Test]
    public async Task Lock_WaitAsyncCancellation_DoesNotSpuriouslyRelease()
    {
        var definition = CreateTestDefinition();
        var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // CatchAsync (not ThrowsAsync) to accept TaskCanceledException, the derived
        // type SemaphoreSlim.WaitAsync throws when its token is signalled.
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, cts.Token));

        // Subsequent write must succeed — no leaked permit and no deadlock.
        var result = await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(InstanceStatus.Running));
    }

    // AC13: Lock dictionary grows monotonically — 100 mutated instances produce
    // at least 100 lock entries. This is a grep-able growth-visibility smoke test,
    // not a count enforcement (pruning is explicitly deferred to v2). Asserts
    // a pre-condition of zero and a post-condition of >= instanceCount so a
    // future SetUp that warms locks (e.g., a shared fixture) does not cause
    // silent false failures.
    [Test]
    public async Task Lock_DictionaryGrowsWithInstanceCount_NeverPruned()
    {
        var definition = CreateTestDefinition();

        Assert.That(_manager.InstanceLockCount, Is.EqualTo(0),
            "SetUp baseline precondition: fresh InstanceManager starts with an empty lock dictionary.");

        const int instanceCount = 100;
        for (var i = 0; i < instanceCount; i++)
        {
            var created = await _manager.CreateInstanceAsync("test-workflow", definition, "admin@example.com", CancellationToken.None);
            await _manager.SetInstanceStatusAsync("test-workflow", created.InstanceId, InstanceStatus.Running, CancellationToken.None);
        }

        Assert.That(_manager.InstanceLockCount, Is.GreaterThanOrEqualTo(instanceCount),
            "Lock dictionary should have at least one entry per mutated instance (no pruning in v1 — AC13).");
    }
}
