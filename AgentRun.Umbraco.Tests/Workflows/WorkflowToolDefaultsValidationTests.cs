using Microsoft.Extensions.Options;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

[TestFixture]
public class WorkflowToolDefaultsValidationTests
{
    private WorkflowValidator _validator = null!;

    [SetUp]
    public void SetUp() => _validator = new WorkflowValidator();

    private static WorkflowValidator ValidatorWithCeilings(AgentRunOptions options)
        => new(Options.Create(options));

    private const string ValidStepsBlock = """
        steps:
          - id: scan
            name: Scan
            agent: agents/scan.md
        """;

    [Test]
    public void ToolDefaults_Block_AcceptedWhenValid()
    {
        var yaml = $"""
            name: Test
            description: Test workflow
            tool_defaults:
              fetch_url:
                max_response_bytes: 2097152
                timeout_seconds: 30
              tool_loop:
                user_message_timeout_seconds: 600
            {ValidStepsBlock}
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Test]
    public void ToolDefaults_UnknownToolName_RejectsWithDenyByDefault()
    {
        var yaml = $"""
            name: Test
            description: Test
            tool_defaults:
              fake_tool:
                max_response_bytes: 100
            {ValidStepsBlock}
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("Unknown tool 'fake_tool'")), Is.True);
    }

    [Test]
    public void ToolDefaults_UnknownSettingName_RejectsWithDenyByDefault()
    {
        var yaml = $"""
            name: Test
            description: Test
            tool_defaults:
              fetch_url:
                unknown_setting: 100
            {ValidStepsBlock}
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("Unknown setting 'unknown_setting'")), Is.True);
    }

    [Test]
    public void ToolDefaults_ZeroValue_RejectedAsNonPositive()
    {
        var yaml = $"""
            name: Test
            description: Test
            tool_defaults:
              fetch_url:
                max_response_bytes: 0
            {ValidStepsBlock}
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("must be a positive integer")), Is.True);
    }

    [Test]
    public void ToolDefaults_NegativeValue_Rejected()
    {
        var yaml = $"""
            name: Test
            description: Test
            tool_defaults:
              fetch_url:
                timeout_seconds: -5
            {ValidStepsBlock}
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("must be a positive integer")), Is.True);
    }

    [Test]
    public void StepToolOverrides_Block_AcceptedWhenValid()
    {
        var yaml = """
            name: Test
            description: Test
            steps:
              - id: scan
                name: Scan
                agent: agents/scan.md
                tool_overrides:
                  fetch_url:
                    max_response_bytes: 500000
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Test]
    public void StepToolOverrides_UnknownTool_Rejected()
    {
        var yaml = """
            name: Test
            description: Test
            steps:
              - id: scan
                name: Scan
                agent: agents/scan.md
                tool_overrides:
                  fake_tool:
                    foo: 1
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("Unknown tool 'fake_tool'")), Is.True);
    }

    [Test]
    public void StepToolOverrides_UnknownSetting_Rejected()
    {
        var yaml = """
            name: Test
            description: Test
            steps:
              - id: scan
                name: Scan
                agent: agents/scan.md
                tool_overrides:
                  fetch_url:
                    bogus_setting: 1
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("Unknown setting 'bogus_setting'")), Is.True);
    }

    [Test]
    public void StepToolOverrides_NonPositiveValue_Rejected()
    {
        var yaml = """
            name: Test
            description: Test
            steps:
              - id: scan
                name: Scan
                agent: agents/scan.md
                tool_overrides:
                  fetch_url:
                    timeout_seconds: 0
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.Message.Contains("must be a positive integer")), Is.True);
    }

    // --- EnforceCeilings (Story 9.6 — security boundary at workflow load time) ---

    [Test]
    public void EnforceCeilings_NoLimitsConfigured_NoOp()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "T", Alias = "test-wf",
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 9_999_999 } },
            Steps = { new StepDefinition { Id = "s", Name = "S", Agent = "a.md" } }
        };
        Assert.DoesNotThrow(() => new WorkflowValidator().EnforceCeilings(workflow));
    }

    [Test]
    public void EnforceCeilings_WorkflowValueAboveCeiling_ThrowsWithFieldPathAndAlias()
    {
        var options = new AgentRunOptions
        {
            ToolLimits = new() { FetchUrl = new() { MaxResponseBytesCeiling = 100_000 } }
        };
        var workflow = new WorkflowDefinition
        {
            Name = "T", Alias = "content-quality-audit",
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 200_000 } },
            Steps = { new StepDefinition { Id = "s", Name = "S", Agent = "a.md" } }
        };

        var ex = Assert.Throws<WorkflowConfigurationException>(
            () => ValidatorWithCeilings(options).EnforceCeilings(workflow));
        // P11: assert alias, field path, attempted value, and ceiling all appear.
        Assert.That(ex!.Message, Does.Contain("content-quality-audit"));
        Assert.That(ex.Message, Does.Contain("tool_defaults.fetch_url.max_response_bytes"));
        Assert.That(ex.Message, Does.Contain("200000"));
        Assert.That(ex.Message, Does.Contain("100000"));
        Assert.That(ex.Message, Does.Contain("FetchUrl:MaxResponseBytesCeiling"));
    }

    [Test]
    public void EnforceCeilings_StepOverrideAboveCeiling_ThrowsWithStepIdInFieldPath()
    {
        var options = new AgentRunOptions
        {
            ToolLimits = new() { FetchUrl = new() { TimeoutSecondsCeiling = 10 } }
        };
        var workflow = new WorkflowDefinition
        {
            Name = "T", Alias = "test-wf",
            Steps =
            {
                new StepDefinition
                {
                    Id = "scanner", Name = "S", Agent = "a.md",
                    ToolOverrides = new() { FetchUrl = new() { TimeoutSeconds = 60 } }
                }
            }
        };
        var ex = Assert.Throws<WorkflowConfigurationException>(
            () => ValidatorWithCeilings(options).EnforceCeilings(workflow));
        Assert.That(ex!.Message, Does.Contain("steps[scanner].tool_overrides.fetch_url.timeout_seconds"));
        Assert.That(ex.Message, Does.Contain("60"));
        Assert.That(ex.Message, Does.Contain("10"));
    }

    [Test]
    public void EnforceCeilings_ValueAtCeiling_DoesNotThrow()
    {
        var options = new AgentRunOptions
        {
            ToolLimits = new() { FetchUrl = new() { MaxResponseBytesCeiling = 100_000 } }
        };
        var workflow = new WorkflowDefinition
        {
            Name = "T", Alias = "test-wf",
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 100_000 } },
            Steps = { new StepDefinition { Id = "s", Name = "S", Agent = "a.md" } }
        };
        Assert.DoesNotThrow(() => ValidatorWithCeilings(options).EnforceCeilings(workflow));
    }

    [Test]
    public void Workflow_Without_ToolDefaults_StillValid()
    {
        var yaml = $"""
            name: Test
            description: Test
            {ValidStepsBlock}
            """;
        var result = _validator.Validate(yaml);
        Assert.That(result.IsValid, Is.True);
    }
}
