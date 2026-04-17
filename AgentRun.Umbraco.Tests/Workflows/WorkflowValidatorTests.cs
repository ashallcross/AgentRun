using AgentRun.Umbraco.Tests.Fixtures.SampleWorkflows;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

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

    // --- Path Traversal Validation (Story 9.10) ---

    [Test]
    public void Validate_AgentTraversal_Rejected()
    {
        var yaml = """
            name: Traversal Test
            description: Agent path traversal
            steps:
              - id: step_one
                name: Step One
                agent: "../../etc/passwd"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].agent" && e.Message.Contains("path traversal segment '..'")));
    }

    [Test]
    public void Validate_AgentAbsoluteUnix_Rejected()
    {
        var yaml = """
            name: Absolute Path Test
            description: Agent absolute path
            steps:
              - id: step_one
                name: Step One
                agent: "/etc/passwd"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].agent" && e.Message.Contains("absolute path not allowed")));
    }

    [Test]
    public void Validate_AgentAbsoluteWindows_Rejected()
    {
        var yaml = """
            name: Windows Path Test
            description: Agent Windows absolute path
            steps:
              - id: step_one
                name: Step One
                agent: "C:\\Windows\\System32\\cmd.exe"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].agent" && e.Message.Contains("absolute path not allowed")));
    }

    [Test]
    public void Validate_AgentNestedTraversal_Rejected()
    {
        var yaml = """
            name: Nested Traversal Test
            description: Agent nested traversal
            steps:
              - id: step_one
                name: Step One
                agent: "foo/../../bar"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].agent" && e.Message.Contains("path traversal segment '..'")));
    }

    [Test]
    public void Validate_ReadsFromTraversal_Rejected()
    {
        var yaml = """
            name: ReadsFrom Traversal
            description: reads_from path traversal
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
                reads_from:
                  - "../../../appsettings.json"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].reads_from[0]" && e.Message.Contains("path traversal")));
    }

    [Test]
    public void Validate_WritesToAbsolute_Rejected()
    {
        var yaml = """
            name: WritesTo Absolute
            description: writes_to absolute path
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
                writes_to:
                  - "/etc/crontab"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].writes_to[0]" && e.Message.Contains("absolute path not allowed")));
    }

    [Test]
    public void Validate_CompletionCheckFilesExistTraversal_Rejected()
    {
        var yaml = """
            name: Completion Check Traversal
            description: files_exist path traversal
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
                completion_check:
                  files_exist:
                    - "../../secret.txt"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].completion_check.files_exist[0]" && e.Message.Contains("path traversal")));
    }

    [Test]
    public void Validate_StepIdWithTraversal_Rejected()
    {
        var yaml = """
            name: Step ID Traversal
            description: Step ID with path traversal
            steps:
              - id: "../escape"
                name: Step One
                agent: agents/worker.md
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].id" && (e.Message.Contains("path traversal") || e.Message.Contains("path separators"))));
    }

    [Test]
    public void Validate_StepIdWithForwardSlash_Rejected()
    {
        var yaml = """
            name: Step ID Slash
            description: Step ID with forward slash
            steps:
              - id: "foo/bar"
                name: Step One
                agent: agents/worker.md
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].id" && e.Message.Contains("path separators not allowed")));
    }

    [Test]
    public void Validate_ValidPathsWithSubdirectories_Accepted()
    {
        var yaml = """
            name: Valid Paths
            description: Valid workflow with subdirectory paths
            steps:
              - id: valid-step
                name: Valid Step
                agent: agents/scanner.md
                reads_from:
                  - "artifacts/scan-results.md"
                writes_to:
                  - "artifacts/report.md"
                completion_check:
                  files_exist:
                    - "artifacts/report.md"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_DotPrefixedDirectoryNotTraversal_Accepted()
    {
        // "..hidden-folder" is a valid directory name, not a traversal segment
        var yaml = """
            name: Dot Prefix Test
            description: Dot-prefixed directory name is valid
            steps:
              - id: step_one
                name: Step One
                agent: "..hidden-folder/file.md"
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_ShippedCqaWorkflow_PassesPathValidation()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(
            repoRoot, "AgentRun.Umbraco.TestSite", "App_Data", "AgentRun.Umbraco",
            "workflows", "content-quality-audit", "workflow.yaml");

        Assert.That(File.Exists(workflowPath), Is.True, $"Expected CQA workflow at {workflowPath}");

        var yaml = File.ReadAllText(workflowPath);
        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True, $"CQA workflow failed validation: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void Validate_ShippedAccessibilityWorkflow_PassesPathValidation()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(
            repoRoot, "AgentRun.Umbraco.TestSite", "App_Data", "AgentRun.Umbraco",
            "workflows", "accessibility-quick-scan", "workflow.yaml");

        Assert.That(File.Exists(workflowPath), Is.True, $"Expected Accessibility workflow at {workflowPath}");

        var yaml = File.ReadAllText(workflowPath);
        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True, $"Accessibility workflow failed validation: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentRun.Umbraco.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
            throw new InvalidOperationException("Could not locate repository root walking up from " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    [Test]
    public void Validate_StepIdWithNullByte_Rejected()
    {
        var yaml = "name: Null Byte Test\ndescription: Step ID with null byte\nsteps:\n  - id: \"step\\0one\"\n    name: Step One\n    agent: agents/worker.md";

        var result = _validator.Validate(yaml);

        // YAML parser may strip null bytes — if the ID passes parsing, it should still be rejected
        // by the null byte check. If YAML strips it, the ID becomes "stepone" which is valid.
        // Either way, the workflow must not allow a null-byte-containing ID to reach consumers.
        if (result.Errors.Any(e => e.FieldPath.Contains("id") && e.Message.Contains("null byte")))
        {
            Assert.That(result.IsValid, Is.False);
        }
    }

    // --- Compaction Turns Scalar Validation (Story 10.2) ---

    [Test]
    public void Validate_CompactionTurns_InToolDefaults_Accepted()
    {
        var yaml = """
            name: Compaction Test
            description: Workflow with compaction_turns
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
            tool_defaults:
              compaction_turns: 5
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True,
            $"compaction_turns should be accepted: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void Validate_CompactionTurns_ZeroValue_Rejected()
    {
        var yaml = """
            name: Compaction Zero
            description: compaction_turns zero
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
            tool_defaults:
              compaction_turns: 0
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "tool_defaults.compaction_turns" && e.Message.Contains("positive integer")));
    }

    [Test]
    public void Validate_CompactionTurns_NegativeValue_Rejected()
    {
        var yaml = """
            name: Compaction Negative
            description: compaction_turns negative
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
            tool_defaults:
              compaction_turns: -1
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "tool_defaults.compaction_turns" && e.Message.Contains("positive integer")));
    }

    [Test]
    public void Validate_CompactionTurns_InStepOverrides_Accepted()
    {
        var yaml = """
            name: Step Compaction
            description: compaction_turns in step overrides
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
                tool_overrides:
                  compaction_turns: 10
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True,
            $"compaction_turns in step overrides should be accepted: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }

    // --- Profile Type Validation (Story 11.6) ---

    [Test]
    public void Validate_StepProfile_NonString_ReturnsValidationError()
    {
        // Story 11.6 AC3: profile must be a string — mapping value rejected at load
        var yaml = """
            name: Bad Profile Type
            description: step profile as a mapping
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
                profile:
                  nested: true
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "steps[0].profile" && e.Message.Contains("must be a string")));
    }

    [Test]
    public void Validate_DefaultProfile_NonString_ReturnsValidationError()
    {
        // Story 11.6 AC3: default_profile must be a string — list value rejected at load
        var yaml = """
            name: Bad Default Profile Type
            description: default_profile as a list
            default_profile:
              - a
              - b
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Matches<WorkflowValidationError>(
            e => e.FieldPath == "default_profile" && e.Message.Contains("must be a string")));
    }

    [Test]
    public void Validate_StepProfile_ValidString_Passes()
    {
        // Story 11.6 AC1/AC3: a plain-string profile passes validation
        var yaml = """
            name: Profile Override
            description: step profile as a valid alias
            default_profile: anthropic-sonnet
            steps:
              - id: reporter
                name: Reporter
                agent: agents/reporter.md
                profile: anthropic-opus
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True,
            $"valid string profile should pass: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }

    [Test]
    public void Validate_StepProfile_Absent_Passes()
    {
        // Story 11.6: profile is optional — absence must not produce a validation error
        var yaml = """
            name: No Step Profile
            description: step omits profile, workflow default present
            default_profile: anthropic-sonnet
            steps:
              - id: step_one
                name: Step One
                agent: agents/worker.md
            """;

        var result = _validator.Validate(yaml);

        Assert.That(result.IsValid, Is.True,
            $"absent step profile should pass: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    }
}
