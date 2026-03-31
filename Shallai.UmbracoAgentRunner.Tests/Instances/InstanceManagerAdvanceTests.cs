using Microsoft.Extensions.Logging;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Instances;

[TestFixture]
public class InstanceManagerAdvanceTests
{
    private string _tempDir = null!;
    private InstanceManager _manager = null!;

    private static WorkflowDefinition CreateTestDefinition() => new()
    {
        Name = "Test Workflow",
        Description = "A test workflow",
        Mode = "interactive",
        Steps =
        [
            new StepDefinition { Id = "step-one", Name = "Step One", Agent = "agents/one.md" },
            new StepDefinition { Id = "step-two", Name = "Step Two", Agent = "agents/two.md" },
            new StepDefinition { Id = "step-three", Name = "Step Three", Agent = "agents/three.md" }
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
    public async Task AdvanceStepAsync_IncrementsCurrentStepIndex()
    {
        var definition = CreateTestDefinition();
        var state = await _manager.CreateInstanceAsync("test-wf", definition, "admin", CancellationToken.None);

        Assert.That(state.CurrentStepIndex, Is.EqualTo(0));

        var advanced = await _manager.AdvanceStepAsync("test-wf", state.InstanceId, CancellationToken.None);

        Assert.That(advanced.CurrentStepIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task AdvanceStepAsync_CanAdvanceMultipleTimes()
    {
        var definition = CreateTestDefinition();
        var state = await _manager.CreateInstanceAsync("test-wf", definition, "admin", CancellationToken.None);

        await _manager.AdvanceStepAsync("test-wf", state.InstanceId, CancellationToken.None);
        var advanced = await _manager.AdvanceStepAsync("test-wf", state.InstanceId, CancellationToken.None);

        Assert.That(advanced.CurrentStepIndex, Is.EqualTo(2));
    }

    [Test]
    public async Task AdvanceStepAsync_OnLastStep_Throws()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Single Step",
            Description = "One step",
            Mode = "interactive",
            Steps = [new StepDefinition { Id = "only", Name = "Only Step", Agent = "agents/only.md" }]
        };

        var state = await _manager.CreateInstanceAsync("test-wf", definition, "admin", CancellationToken.None);

        Assert.That(state.CurrentStepIndex, Is.EqualTo(0));
        Assert.That(state.Steps, Has.Count.EqualTo(1));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.AdvanceStepAsync("test-wf", state.InstanceId, CancellationToken.None));
    }

    [Test]
    public async Task AdvanceStepAsync_PersistsToDisc()
    {
        var definition = CreateTestDefinition();
        var state = await _manager.CreateInstanceAsync("test-wf", definition, "admin", CancellationToken.None);

        await _manager.AdvanceStepAsync("test-wf", state.InstanceId, CancellationToken.None);

        // Re-read from disk
        var reloaded = await _manager.GetInstanceAsync("test-wf", state.InstanceId, CancellationToken.None);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.CurrentStepIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task GetInstanceFolderPath_ReturnsCorrectPath()
    {
        var definition = CreateTestDefinition();
        var state = await _manager.CreateInstanceAsync("test-wf", definition, "admin", CancellationToken.None);

        var path = _manager.GetInstanceFolderPath("test-wf", state.InstanceId);

        Assert.That(path, Does.Contain("test-wf"));
        Assert.That(path, Does.Contain(state.InstanceId));
        Assert.That(Directory.Exists(path), Is.True);
    }
}
