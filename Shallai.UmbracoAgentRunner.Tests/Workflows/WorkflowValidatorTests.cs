using Shallai.UmbracoAgentRunner.Tests.Fixtures.SampleWorkflows;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Workflows;

[TestFixture]
public class WorkflowValidatorTests
{
    private WorkflowValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new WorkflowValidator();
    }

    [Test]
    public void Validate_ValidWorkflow_ReturnsNoErrors()
    {
        var yaml = SampleWorkflowLoader.Load("valid-workflow.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void Validate_MinimalWorkflow_ReturnsNoErrors()
    {
        var yaml = SampleWorkflowLoader.Load("minimal-workflow.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_MissingName_ReturnsError()
    {
        var yaml = SampleWorkflowLoader.Load("missing-name.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Exactly(1).Items);
        Assert.That(result.Errors[0].FieldPath, Is.EqualTo("name"));
        Assert.That(result.Errors[0].Message, Is.EqualTo("Workflow is missing required field 'name'"));
    }

    [Test]
    public void Validate_MissingSteps_ReturnsError()
    {
        var yaml = SampleWorkflowLoader.Load("missing-steps.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Exactly(1).Items);
        Assert.That(result.Errors[0].FieldPath, Is.EqualTo("steps"));
        Assert.That(result.Errors[0].Message, Is.EqualTo("Workflow is missing required field 'steps'"));
    }

    [Test]
    public void Validate_EmptySteps_ReturnsError()
    {
        var yaml = SampleWorkflowLoader.Load("empty-steps.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Exactly(1).Items);
        Assert.That(result.Errors[0].Message, Is.EqualTo("Workflow must have at least one step"));
    }

    [Test]
    public void Validate_UnknownProperties_RejectsWorkflow()
    {
        var yaml = SampleWorkflowLoader.Load("unknown-properties.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);

        var messages = result.Errors.Select(e => e.Message).ToList();
        Assert.That(messages, Has.Some.Contains("Unexpected property 'author' in workflow root"));
        Assert.That(messages, Has.Some.Contains("Unexpected property 'version' in workflow root"));
        Assert.That(messages, Has.Some.Contains("Unexpected property 'priority'"));
        Assert.That(messages, Has.Some.Contains("Unexpected property 'timeout'"));
    }

    [Test]
    public void Validate_InvalidMode_ReturnsError()
    {
        var yaml = SampleWorkflowLoader.Load("invalid-mode.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Exactly(1).Items);
        Assert.That(result.Errors[0].Message, Is.EqualTo("Workflow 'mode' must be 'interactive' or 'autonomous', got 'batch'"));
    }

    [Test]
    public void Validate_StepMissingRequiredFields_ReturnsErrors()
    {
        var yaml = """
            name: Bad Steps Workflow
            description: Steps with missing fields
            mode: autonomous
            steps:
              - name: No ID Step
                agent: agents/worker.md
              - id: no_name_step
                agent: agents/worker.md
              - id: no_agent_step
                name: No Agent Step
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);

        var messages = result.Errors.Select(e => e.Message).ToList();
        Assert.That(messages, Has.Some.Contains("Step at index 0 is missing required field 'id'"));
        Assert.That(messages, Has.Some.Contains("Step 'no_name_step' is missing required field 'name'"));
        Assert.That(messages, Has.Some.Contains("Step 'no_agent_step' is missing required field 'agent'"));
    }

    [Test]
    public void Validate_DuplicateStepIds_ReturnsError()
    {
        var yaml = """
            name: Duplicate IDs Workflow
            description: Has duplicate step IDs
            mode: autonomous
            steps:
              - id: step_one
                name: First Step
                agent: agents/worker.md
              - id: step_one
                name: Duplicate Step
                agent: agents/worker.md
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.Message.Contains("Duplicate step id 'step_one' found at index 1")));
    }

    [Test]
    public void Validate_WrongTypeForMode_ReturnsError()
    {
        var yaml = """
            name: Wrong Type Workflow
            description: Mode is a number
            mode: 123
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.Message.Contains("'mode' must be 'interactive' or 'autonomous'")));
    }

    [Test]
    public void Validate_ErrorMessagesContainFieldPath()
    {
        var yaml = SampleWorkflowLoader.Load("missing-name.yaml");

        var result = _validator.Validate(yaml);

        Assert.That(result.Errors[0].FieldPath, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Errors[0].Message, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Validate_EmptyDocument_ReturnsError()
    {
        var result = _validator.Validate(string.Empty);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Message, Is.EqualTo("Workflow file is empty"));
    }

    [Test]
    public void Validate_MalformedYaml_ReturnsParsingError()
    {
        var yaml = "name: [broken\nyaml: {invalid";

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors[0].Message, Does.StartWith("YAML parsing failed:"));
    }
}
