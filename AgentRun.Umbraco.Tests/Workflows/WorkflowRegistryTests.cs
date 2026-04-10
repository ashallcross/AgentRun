using Microsoft.Extensions.Logging;
using NSubstitute;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

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
        _registry = new WorkflowRegistry(_parser, _validator, Enumerable.Empty<IWorkflowTool>(), _logger);
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
    public async Task LoadWorkflowsAsync_MissingAgentFile_WorkflowRejected()
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

        Assert.That(_registry.GetWorkflow("test-workflow"), Is.Null);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("missing-agent.md")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_ExistingAgentFile_NoError()
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

        Assert.That(_registry.GetWorkflow("test-workflow"), Is.Not.Null);

        _logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("agent file")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_MultipleMissingAgentFiles_AllErrorsLogged()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-workflow", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "scanner", Name = "Scanner", Agent = "agents/scanner.md" },
                new StepDefinition { Id = "analyser", Name = "Analyser", Agent = "agents/analyser.md" },
                new StepDefinition { Id = "reporter", Name = "Reporter", Agent = "agents/reporter.md" }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetWorkflow("test-workflow"), Is.Null);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("scanner.md")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("analyser.md")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("reporter.md")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_MixedValidAndMissingAgentFiles_WorkflowRejected()
    {
        var yaml = "name: Test";
        var folderPath = CreateWorkflowFolder("test-workflow", yaml);

        var agentsDir = Path.Combine(folderPath, "agents");
        Directory.CreateDirectory(agentsDir);
        await File.WriteAllTextAsync(Path.Combine(agentsDir, "scanner.md"), "# Scanner Agent");

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "scanner", Name = "Scanner", Agent = "agents/scanner.md" },
                new StepDefinition { Id = "analyser", Name = "Analyser", Agent = "agents/missing-analyser.md" }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetWorkflow("test-workflow"), Is.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_MissingAgentFileWorkflow_DoesNotBlockValidWorkflow()
    {
        CreateWorkflowFolder("invalid-wf", "name: Invalid");
        var validFolder = CreateWorkflowFolder("valid-wf", "name: Valid");

        var agentsDir = Path.Combine(validFolder, "agents");
        Directory.CreateDirectory(agentsDir);
        await File.WriteAllTextAsync(Path.Combine(agentsDir, "scanner.md"), "# Scanner Agent");

        var invalidDef = new WorkflowDefinition
        {
            Name = "Invalid",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Agent = "agents/missing.md" }
            ]
        };
        var validDef = new WorkflowDefinition
        {
            Name = "Valid",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Agent = "agents/scanner.md" }
            ]
        };

        _validator.Validate("name: Invalid").Returns(WorkflowValidationResult.Success());
        _parser.Parse("name: Invalid").Returns(invalidDef);
        _validator.Validate("name: Valid").Returns(WorkflowValidationResult.Success());
        _parser.Parse("name: Valid").Returns(validDef);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetWorkflow("invalid-wf"), Is.Null);
        Assert.That(_registry.GetWorkflow("valid-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_MissingAgentAndInvalidTool_BothErrorsLogged()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-workflow", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Agent = "agents/missing.md", Tools = ["nonexistent_tool"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-workflow"), Is.Null);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("missing.md")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("nonexistent_tool")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_EmptyAgentPath_SkippedNoError()
    {
        var yaml = "name: Test";
        var folderPath = CreateWorkflowFolder("test-workflow", yaml);

        var agentsDir = Path.Combine(folderPath, "agents");
        Directory.CreateDirectory(agentsDir);
        await File.WriteAllTextAsync(Path.Combine(agentsDir, "valid.md"), "# Agent");

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Agent = "agents/valid.md" },
                new StepDefinition { Id = "step2", Name = "Step 2", Agent = "" },
                new StepDefinition { Id = "step3", Name = "Step 3", Agent = "   " }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(_registry.GetWorkflow("test-workflow"), Is.Not.Null);

        _logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("not found")),
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

    // --- Tool Validation Tests ---

    [Test]
    public async Task LoadWorkflowsAsync_ValidToolReferences_WorkflowLoads()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["read_file", "write_file"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file", "write_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_UnknownToolReference_WorkflowRejected()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["nonexistent_tool"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_UnknownToolReference_ErrorMessageContainsDetails()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "analyse", Name = "Analyse", Tools = ["bad_tool"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file", "write_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o =>
                o.ToString()!.Contains("analyse") &&
                o.ToString()!.Contains("bad_tool") &&
                o.ToString()!.Contains("read_file, write_file")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_MixOfValidAndInvalidTools_WorkflowRejected()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["read_file", "nonexistent_tool"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file", "write_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_InvalidToolWorkflow_ValidWorkflowStillLoads()
    {
        CreateWorkflowFolder("invalid-wf", "name: Invalid");
        CreateWorkflowFolder("valid-wf", "name: Valid");

        var invalidDef = new WorkflowDefinition
        {
            Name = "Invalid",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["nonexistent_tool"] }
            ]
        };
        var validDef = new WorkflowDefinition
        {
            Name = "Valid",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["read_file"] }
            ]
        };

        _validator.Validate("name: Invalid").Returns(WorkflowValidationResult.Success());
        _parser.Parse("name: Invalid").Returns(invalidDef);
        _validator.Validate("name: Valid").Returns(WorkflowValidationResult.Success());
        _parser.Parse("name: Valid").Returns(validDef);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("invalid-wf"), Is.Null);
        Assert.That(registry.GetWorkflow("valid-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_NullToolsList_PassesValidation()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = null }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_EmptyToolsList_PassesValidation()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = [] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_ToolNameMatchingIsCaseInsensitive()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["Read_File", "WRITE_FILE"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file", "write_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_AllToolsFromRegisteredSet_PassesValidation()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["fetch_url", "list_files", "read_file", "write_file"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("fetch_url", "list_files", "read_file", "write_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_MultipleStepsWithDifferentInvalidTools_AllErrorsLogged()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["bad_tool_a"] },
                new StepDefinition { Id = "step2", Name = "Step 2", Tools = ["bad_tool_b"] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Null);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("bad_tool_a")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("bad_tool_b")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LoadWorkflowsAsync_NoStepsDeclaringTools_PassesValidation()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1" },
                new StepDefinition { Id = "step2", Name = "Step 2" }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Not.Null);
    }

    [Test]
    public async Task LoadWorkflowsAsync_NullToolNameInList_WorkflowRejected()
    {
        var yaml = "name: Test";
        CreateWorkflowFolder("test-wf", yaml);

        var definition = new WorkflowDefinition
        {
            Name = "Test",
            Steps =
            [
                new StepDefinition { Id = "step1", Name = "Step 1", Tools = ["read_file", null!] }
            ]
        };

        _validator.Validate(yaml).Returns(WorkflowValidationResult.Success());
        _parser.Parse(yaml).Returns(definition);

        var registry = CreateRegistryWithTools("read_file");
        await registry.LoadWorkflowsAsync(_tempRoot);

        Assert.That(registry.GetWorkflow("test-wf"), Is.Null);
    }

    private WorkflowRegistry CreateRegistryWithTools(params string[] toolNames)
    {
        var tools = toolNames.Select(name =>
        {
            var tool = Substitute.For<IWorkflowTool>();
            tool.Name.Returns(name);
            return tool;
        });
        return new WorkflowRegistry(_parser, _validator, tools, _logger);
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
