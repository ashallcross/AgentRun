using AgentRun.Umbraco.Tests.Fixtures.SampleWorkflows;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

[TestFixture]
public class WorkflowParserTests
{
    private WorkflowParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new WorkflowParser();
    }

    [Test]
    public void Parse_ValidWorkflow_PopulatesAllFields()
    {
        var yaml = SampleWorkflowLoader.Load("valid-workflow.yaml");

        var result = _parser.Parse(yaml);

        Assert.That(result.Name, Is.EqualTo("Content Quality Audit"));
        Assert.That(result.Description, Is.EqualTo("Audits content for quality and consistency"));
        Assert.That(result.Mode, Is.EqualTo("autonomous"));
        Assert.That(result.DefaultProfile, Is.EqualTo("anthropic-claude"));
        Assert.That(result.Steps, Has.Count.EqualTo(2));
    }

    [Test]
    public void Parse_ValidWorkflow_PopulatesStepFields()
    {
        var yaml = SampleWorkflowLoader.Load("valid-workflow.yaml");

        var result = _parser.Parse(yaml);
        var step = result.Steps[0];

        Assert.That(step.Id, Is.EqualTo("gather_content"));
        Assert.That(step.Name, Is.EqualTo("Gather Content"));
        Assert.That(step.Agent, Is.EqualTo("agents/content-gatherer.md"));
        Assert.That(step.Tools, Is.EqualTo(new[] { "read_file", "list_files" }));
        Assert.That(step.WritesTo, Is.EqualTo(new[] { "artifacts/content-inventory.json" }));
        Assert.That(step.CompletionCheck, Is.Not.Null);
        Assert.That(step.CompletionCheck!.FilesExist, Is.EqualTo(new[] { "artifacts/content-inventory.json" }));
    }

    [Test]
    public void Parse_ValidWorkflow_StepWithProfileOverride()
    {
        var yaml = SampleWorkflowLoader.Load("valid-workflow.yaml");

        var result = _parser.Parse(yaml);
        var step = result.Steps[1];

        Assert.That(step.Id, Is.EqualTo("analyse_quality"));
        Assert.That(step.Profile, Is.EqualTo("openai-gpt4"));
        Assert.That(step.ReadsFrom, Is.EqualTo(new[] { "artifacts/content-inventory.json" }));
    }

    [Test]
    public void Parse_MinimalWorkflow_OnlyRequiredFields()
    {
        var yaml = SampleWorkflowLoader.Load("minimal-workflow.yaml");

        var result = _parser.Parse(yaml);

        Assert.That(result.Name, Is.EqualTo("Minimal Workflow"));
        Assert.That(result.Mode, Is.EqualTo("interactive"));
        Assert.That(result.DefaultProfile, Is.Null);
        Assert.That(result.Steps, Has.Count.EqualTo(1));

        var step = result.Steps[0];
        Assert.That(step.Profile, Is.Null);
        Assert.That(step.Tools, Is.Null);
        Assert.That(step.ReadsFrom, Is.Null);
        Assert.That(step.WritesTo, Is.Null);
        Assert.That(step.CompletionCheck, Is.Null);
    }

    [Test]
    public void Parse_SnakeCaseToPascalCase_MapsCorrectly()
    {
        var yaml = SampleWorkflowLoader.Load("valid-workflow.yaml");

        var result = _parser.Parse(yaml);

        // default_profile → DefaultProfile
        Assert.That(result.DefaultProfile, Is.EqualTo("anthropic-claude"));

        // reads_from → ReadsFrom
        Assert.That(result.Steps[1].ReadsFrom, Is.Not.Null);

        // writes_to → WritesTo
        Assert.That(result.Steps[0].WritesTo, Is.Not.Null);

        // completion_check → CompletionCheck
        Assert.That(result.Steps[0].CompletionCheck, Is.Not.Null);

        // files_exist → FilesExist
        Assert.That(result.Steps[0].CompletionCheck!.FilesExist, Has.Count.GreaterThan(0));
    }

    [Test]
    public void Parse_InvalidYaml_ThrowsInvalidOperationException()
    {
        var invalidYaml = "name: [invalid\nyaml: {broken";

        Assert.That(
            () => _parser.Parse(invalidYaml),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartsWith("Failed to parse workflow YAML:"));
    }
}
