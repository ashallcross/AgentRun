using Microsoft.Extensions.Logging;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Workflows;

[TestFixture]
public class WorkflowRegistryTests
{
    private string _tempRoot = null!;
    private IWorkflowParser _parser = null!;
    private IWorkflowValidator _validator = null!;
    private ILogger<WorkflowRegistry> _logger = null!;
    private WorkflowRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);

        _parser = Substitute.For<IWorkflowParser>();
        _validator = Substitute.For<IWorkflowValidator>();
        _logger = Substitute.For<ILogger<WorkflowRegistry>>();
        _registry = new WorkflowRegistry(_parser, _validator, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public async Task LoadWorkflowsAsync_MultipleValidWorkflows_DiscoversAll()
    {
        CreateWorkflowFolder("workflow-a", "name: A");
        CreateWorkflowFolder("workflow-b", "name: B");

        SetupValidWorkflow("name: A", "A");
        SetupValidWorkflow("name: B", "B");

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var all = _registry.GetAllWorkflows();
        Assert.That(all, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadWorkflowsAsync_AliasFromFolderName()
    {
        CreateWorkflowFolder("content-quality-audit", "name: CQA");
        SetupValidWorkflow("name: CQA", "CQA");

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var workflow = _registry.GetWorkflow("content-quality-audit");
        Assert.That(workflow, Is.Not.Null);
        Assert.That(workflow!.Alias, Is.EqualTo("content-quality-audit"));
    }

    [Test]
    public async Task LoadWorkflowsAsync_MissingAgentFile_LogsWarningAndLoads()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-workflow", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Agent = "agents/missing-agent.md" }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var workflow = _registry.GetWorkflow("test-workflow");
        Assert.That(workflow, Is.Not.Null);

        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("missing-agent.md")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_ExistingAgentFile_NoWarning()
    {
        var yaml = "name: Test";
        var folderPath = CreateWorkflowFolder("test-workflow", yaml);

        var agentsDir = Path.Combine(folderPath, "agents");
        Directory.CreateDirectory(agentsDir);
        await File.WriteAllTextAsync(Path.Combine(agentsDir, "my-agent.md"), "# Agent");

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Agent = "agents/my-agent.md" }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        _logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_InvalidWorkflowSkipped_ValidStillLoads()
    {
        CreateWorkflowFolder("valid-wf", "name: Valid");
        CreateWorkflowFolder("invalid-wf", "name: Invalid");

        SetupValidWorkflow("name: Valid", "Valid");

        var errors = new List<WorkflowValidationError>
        {
            new("name", "Missing required field")
        };
        _validator.Validate("name: Invalid").Returns(new WorkflowValidationResult(errors));

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetAllWorkflows(), Has.Count.EqualTo(1));
        Assert.That(_registry.GetWorkflow("valid-wf"), Is.Not.Null);
        Assert.That(_registry.GetWorkflow("invalid-wf"), Is.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_EmptyDirectory_ReturnsEmptyRegistry()
    {
        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetAllWorkflows(), Is.Empty);
    }

    [Test]
    public async Task LoadWorkflowsAsync_NonExistentDirectory_HandledGracefully()
    {
        var nonExistent = Path.Combine(_tempRoot, "does-not-exist");

        await _registry.LoadWorkflowsAsync(nonExistent);

        Assert.That(_registry.GetAllWorkflows(), Is.Empty);
    }

    [Test]
    public async Task GetWorkflow_UnknownAlias_ReturnsNull()
    {
        CreateWorkflowFolder("known", "name: Known");
        SetupValidWorkflow("name: Known", "Known");

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetWorkflow("unknown-alias"), Is.Null);
    }

    [Test]
    public async Task GetAllWorkflows_ReturnsAllLoadedWorkflows()
    {
        CreateWorkflowFolder("wf-1", "name: One");
        CreateWorkflowFolder("wf-2", "name: Two");
        CreateWorkflowFolder("wf-3", "name: Three");

        SetupValidWorkflow("name: One", "One");
        SetupValidWorkflow("name: Two", "Two");
        SetupValidWorkflow("name: Three", "Three");

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var all = _registry.GetAllWorkflows();
        Assert.That(all, Has.Count.EqualTo(3));

        var aliases = all.Select(w => w.Alias).OrderBy(a => a).ToList();
        Assert.That(aliases, Is.EqualTo(new[] { "wf-1", "wf-2", "wf-3" }));
    }

    [Test]
    public async Task LoadWorkflowsAsync_SubdirWithoutWorkflowYaml_SkippedSilently()
    {
        CreateWorkflowFolder("valid-wf", "name: Valid");
        SetupValidWorkflow("name: Valid", "Valid");

        // Create a subdirectory with no workflow.yaml
        Directory.CreateDirectory(Path.Combine(_tempRoot, "no-yaml-here"));

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetAllWorkflows(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LoadWorkflowsAsync_ParseFailure_SkipsWorkflow()
    {
        CreateWorkflowFolder("broken", "bad yaml");

        _validator.Validate("bad yaml").Returns(WorkflowValidationResult.Success());
        _parser.Parse("bad yaml").Returns<WorkflowDefinition>(
            _ => throw new InvalidOperationException("Parse failed"));

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetAllWorkflows(), Is.Empty);
    }

    [Test]
    public async Task LoadWorkflowsAsync_IoException_SkipsWorkflowAndContinues()
    {
        CreateWorkflowFolder("io-error", "name: Bad");
        CreateWorkflowFolder("valid-wf", "name: Valid");

        _validator.Validate("name: Bad").Returns<WorkflowValidationResult>(
            _ => throw new IOException("Disk read failed"));
        SetupValidWorkflow("name: Valid", "Valid");

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetAllWorkflows(), Has.Count.EqualTo(1));
        Assert.That(_registry.GetWorkflow("valid-wf"), Is.Not.Null);
        Assert.That(_registry.GetWorkflow("io-error"), Is.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_UnexpectedException_SkipsWorkflowAndContinues()
    {
        CreateWorkflowFolder("unexpected", "name: Bad");
        CreateWorkflowFolder("valid-wf", "name: Valid");

        _validator.Validate("name: Bad").Returns<WorkflowValidationResult>(
            _ => throw new ArgumentException("Unexpected failure"));
        SetupValidWorkflow("name: Valid", "Valid");

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetAllWorkflows(), Has.Count.EqualTo(1));
        Assert.That(_registry.GetWorkflow("valid-wf"), Is.Not.Null);
        Assert.That(_registry.GetWorkflow("unexpected"), Is.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_StoresDefinitionAndFolderPath()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("my-wf", yaml);

        var definition = new WorkflowDefinition { Name = "Test" };
        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var workflow = _registry.GetWorkflow("my-wf");
        Assert.That(workflow, Is.Not.Null);
        Assert.That(workflow!.Definition, Is.SameAs(definition));
        Assert.That(workflow.FolderPath, Is.EqualTo(Path.Combine(_tempRoot, "my-wf")));
    }

    private string CreateWorkflowFolder(string folderName, string yamlContent)
    {
        var folderPath = Path.Combine(_tempRoot, folderName);
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "workflow.yaml"), yamlContent);
        return folderPath;
    }

    private void SetupValidWorkflow(string yamlContent, string name)
    {
        var definition = new WorkflowDefinition { Name = name };
        _validator.Validate(yamlContent).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yamlContent).Returns(definition);
    }
}
