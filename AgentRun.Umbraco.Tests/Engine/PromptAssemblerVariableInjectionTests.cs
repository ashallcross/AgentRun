using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

// Story 11.7 — runtime variable injection in PromptAssembler.
// Scope: `{today}`, `{instance_id}`, `{step_index}`, `{previous_artifacts}` +
// workflow-level `config:` block. Unknown tokens remain as literal text and log
// a Warning (AC5, D1). Sentinel-escape `{{...}}` renders literal braces (D3).
// Substitution is confined to agent markdown + sidecar — the runner-emitted
// Runtime Context / Tool Results Untrusted tail is NOT re-substituted (AC6).
[TestFixture]
public class PromptAssemblerVariableInjectionTests
{
    private string _tempDir = null!;
    private string _workflowDir = null!;
    private string _instanceDir = null!;
    private FakeTimeProvider _timeProvider = null!;
    private CapturingLogger<PromptAssembler> _logger = null!;
    private PromptAssembler _assembler = null!;

    private const string PinnedRunDate = "2026-04-17";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentrun-promptvars-" + Guid.NewGuid().ToString("N"));
        _workflowDir = Path.Combine(_tempDir, "workflow");
        _instanceDir = Path.Combine(_tempDir, "instance");
        Directory.CreateDirectory(_workflowDir);
        Directory.CreateDirectory(_instanceDir);

        _timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-17T12:00:00Z"));
        _logger = new CapturingLogger<PromptAssembler>();
        _assembler = new PromptAssembler(_logger, _timeProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private void WriteAgentFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workflowDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private void WriteSidecar(string stepId, string content)
    {
        var dir = Path.Combine(_workflowDir, "sidecars", stepId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "instructions.md"), content);
    }

    private void WriteArtifact(string relativePath)
    {
        var fullPath = Path.Combine(_instanceDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "artifact content");
    }

    private static StepDefinition MakeStep(
        string id,
        string name,
        string agent = "agents/test.md",
        List<string>? writesTo = null,
        List<string>? readsFrom = null) => new()
    {
        Id = id,
        Name = name,
        Agent = agent,
        WritesTo = writesTo,
        ReadsFrom = readsFrom
    };

    private PromptAssemblyContext MakeContext(
        StepDefinition step,
        IReadOnlyList<StepState>? allSteps = null,
        IReadOnlyList<StepDefinition>? allStepDefinitions = null,
        IReadOnlyList<ToolDescription>? tools = null,
        string instanceId = "a69dd6af876a4455b489f42efaec2f82",
        IReadOnlyDictionary<string, string>? workflowConfig = null) => new(
        WorkflowFolderPath: _workflowDir,
        Step: step,
        AllSteps: allSteps ?? [new StepState { Id = step.Id, Status = StepStatus.Active }],
        AllStepDefinitions: allStepDefinitions ?? [step],
        InstanceFolderPath: _instanceDir,
        DeclaredTools: tools ?? [],
        InstanceId: instanceId,
        WorkflowConfig: workflowConfig);

    // --- AC1: {today} ---

    [Test]
    public async Task AssembleAsync_TodayToken_SubstitutesToIso8601Date()
    {
        WriteAgentFile("agents/test.md", "Report date: {today}");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain($"Report date: {PinnedRunDate}"));
        Assert.That(result, Does.Not.Contain("{today}"));
    }

    [Test]
    public async Task AssembleAsync_TodayToken_RespectsFakeTimeProvider()
    {
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2030-12-25T03:15:00Z"));
        WriteAgentFile("agents/test.md", "Today is {today}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Today is 2030-12-25."));
    }

    // --- AC2: {instance_id} and {step_index} ---

    [Test]
    public async Task AssembleAsync_InstanceIdToken_SubstitutesToInstanceGuid()
    {
        WriteAgentFile("agents/test.md", "Instance: {instance_id}");
        var step = MakeStep("scanner", "Scanner");
        var context = MakeContext(step, instanceId: "c88c4a3585904854bb56fa3626f409cf");

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Instance: c88c4a3585904854bb56fa3626f409cf"));
    }

    [Test]
    public async Task AssembleAsync_StepIndexToken_SubstitutesToZeroBasedInteger()
    {
        WriteAgentFile("agents/test.md", "Step index: {step_index}");

        var scannerDef = MakeStep("scanner", "Scanner");
        var analyserDef = MakeStep("analyser", "Analyser");
        var reporterDef = MakeStep("reporter", "Reporter");

        var allSteps = new List<StepState>
        {
            new() { Id = "scanner", Status = StepStatus.Complete },
            new() { Id = "analyser", Status = StepStatus.Active },
            new() { Id = "reporter", Status = StepStatus.Pending }
        };

        var context = MakeContext(
            analyserDef,
            allSteps: allSteps,
            allStepDefinitions: [scannerDef, analyserDef, reporterDef]);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Step index: 1"));
    }

    [Test]
    public async Task AssembleAsync_CaseMismatch_LeavesLiteralText()
    {
        // AC2 case-sensitivity guard — {Today}, {INSTANCE_ID}, {instanceId} are NOT substituted.
        WriteAgentFile("agents/test.md",
            "Valid: {today}. Invalid-cases: {Today}, {INSTANCE_ID}, {instanceId}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain($"Valid: {PinnedRunDate}."));
        Assert.That(result, Does.Contain("{Today}"));
        Assert.That(result, Does.Contain("{INSTANCE_ID}"));
        Assert.That(result, Does.Contain("{instanceId}"));
    }

    // --- AC3: {previous_artifacts} ---

    [Test]
    public async Task AssembleAsync_PreviousArtifactsToken_RendersMarkdownBulletList()
    {
        WriteAgentFile("agents/test.md", "Prior:\n{previous_artifacts}");
        WriteArtifact("artifacts/scan-results.md");
        WriteArtifact("artifacts/quality-scores.md");

        var scannerDef = MakeStep("scanner", "Scanner", writesTo: ["artifacts/scan-results.md"]);
        var analyserDef = MakeStep("analyser", "Analyser", writesTo: ["artifacts/quality-scores.md"]);
        var reporterDef = MakeStep("reporter", "Reporter");

        var allSteps = new List<StepState>
        {
            new() { Id = "scanner", Status = StepStatus.Complete },
            new() { Id = "analyser", Status = StepStatus.Complete },
            new() { Id = "reporter", Status = StepStatus.Active }
        };

        var context = MakeContext(
            reporterDef,
            allSteps: allSteps,
            allStepDefinitions: [scannerDef, analyserDef, reporterDef]);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        var priorPrefix = "Prior:\n";
        var priorIdx = result.IndexOf(priorPrefix, StringComparison.Ordinal);
        Assert.That(priorIdx, Is.GreaterThanOrEqualTo(0));
        // The substituted block must carry both artifacts under the author's
        // leading prose, not reformat the table into the Runtime Context block.
        Assert.That(result.Substring(priorIdx),
            Does.Contain("- artifacts/scan-results.md (from step \"Scanner\"): exists"));
        Assert.That(result.Substring(priorIdx),
            Does.Contain("- artifacts/quality-scores.md (from step \"Analyser\"): exists"));
    }

    [Test]
    public async Task AssembleAsync_PreviousArtifactsToken_NoPriorArtifacts_RendersFallback()
    {
        WriteAgentFile("agents/test.md", "Previous work:\n{previous_artifacts}");
        var step = MakeStep("scanner", "Scanner");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Previous work:\n- No prior artifacts"));
        Assert.That(result, Does.Not.Contain("{previous_artifacts}"));
    }

    // --- AC4: workflow config block ---

    [Test]
    public async Task AssembleAsync_ConfigToken_SubstitutesFromWorkflowConfig()
    {
        WriteAgentFile("agents/test.md",
            "Language: {language}. Severity threshold: {severity_threshold}.");
        var step = MakeStep("analyser", "Analyser");
        var config = new Dictionary<string, string>
        {
            ["language"] = "en-GB",
            ["severity_threshold"] = "medium"
        };
        var context = MakeContext(step, workflowConfig: config);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Language: en-GB. Severity threshold: medium."));
    }

    [Test]
    public async Task AssembleAsync_ConfigKeyShadowsRuntimeVariable_RuntimeWins()
    {
        // A workflow author might declare config.today — the runtime value must
        // win AND a Debug log must fire noting the shadowed key (Failure &
        // Edge Cases).
        WriteAgentFile("agents/test.md", "Today: {today}");
        var step = MakeStep("reporter", "Reporter");
        var config = new Dictionary<string, string>
        {
            ["today"] = "2000-01-01"  // deliberately wrong
        };
        var context = MakeContext(step, workflowConfig: config);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain($"Today: {PinnedRunDate}"));
        Assert.That(result, Does.Not.Contain("2000-01-01"));

        var shadowLogs = _logger.Entries
            .Where(e => e.Level == LogLevel.Debug &&
                        e.Message.Contains("shadows runner-provided variable") &&
                        e.Message.Contains("today"))
            .ToList();
        Assert.That(shadowLogs, Has.Count.EqualTo(1),
            "Debug log must fire once per shadowed runtime key");
    }

    [Test]
    public async Task AssembleAsync_ConfigKeyShadowsInstanceId_RuntimeWins()
    {
        // Precedence guard — the runtime `instance_id` must override any config
        // key of the same name. Parallel to the `today` shadow test so the
        // precedence logic is covered for every runtime-provided key.
        WriteAgentFile("agents/test.md", "Instance: {instance_id}");
        var step = MakeStep("reporter", "Reporter");
        var config = new Dictionary<string, string>
        {
            ["instance_id"] = "OVERRIDE-INSTANCE"
        };
        var context = MakeContext(
            step,
            instanceId: "real-instance-id",
            workflowConfig: config);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Instance: real-instance-id"));
        Assert.That(result, Does.Not.Contain("OVERRIDE-INSTANCE"));
    }

    [Test]
    public async Task AssembleAsync_ConfigKeyShadowsStepIndex_RuntimeWins()
    {
        // Same precedence guard for `step_index`.
        WriteAgentFile("agents/test.md", "Index: {step_index}");
        var step = MakeStep("reporter", "Reporter");
        var config = new Dictionary<string, string>
        {
            ["step_index"] = "99"
        };
        var context = MakeContext(
            step,
            allSteps: [new StepState { Id = step.Id, Status = StepStatus.Active }],
            workflowConfig: config);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Index: 0"));
        Assert.That(result, Does.Not.Contain("Index: 99"));
    }

    [Test]
    public async Task AssembleAsync_Emits_VariablesResolved_Debug_Log()
    {
        // AC1 — structured Debug log "Prompt variables resolved for step
        // {StepId}: {VariableCount} tokens substituted" must fire once per
        // assembly with the substituted-token count.
        WriteAgentFile("agents/test.md", "Date {today}, instance {instance_id}, step {step_index}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        var log = _logger.Entries.Single(e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("Prompt variables resolved"));
        Assert.That(log.Message, Does.Contain("3 tokens substituted"));
    }

    [Test]
    public async Task AssembleAsync_EmptyConfigValue_SubstitutesEmptyString()
    {
        WriteAgentFile("agents/test.md", "Label:[{label}]end");
        var step = MakeStep("reporter", "Reporter");
        var config = new Dictionary<string, string> { ["label"] = string.Empty };
        var context = MakeContext(step, workflowConfig: config);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Label:[]end"));
    }

    [Test]
    public async Task AssembleAsync_ConfigValueContainsToken_NotRecursivelySubstituted()
    {
        // Config values are substituted as-is. If a value itself contains {other_token},
        // that token is NOT recursively expanded — the literal braces flow to the agent.
        WriteAgentFile("agents/test.md", "Greeting: {greeting}");
        var step = MakeStep("reporter", "Reporter");
        var config = new Dictionary<string, string>
        {
            ["greeting"] = "Hello {user_name}"
        };
        var context = MakeContext(step, workflowConfig: config);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Greeting: Hello {user_name}"));
    }

    // --- AC5: unknown tokens ---

    [Test]
    public async Task AssembleAsync_UnknownToken_LeavesLiteralTextAndLogsWarningOnce()
    {
        WriteAgentFile("agents/test.md", "Hello {unknown_var}, see {today}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Hello {unknown_var}"));
        Assert.That(result, Does.Contain($"see {PinnedRunDate}"));

        var warnings = _logger.Entries
            .Where(e => e.Level == LogLevel.Warning &&
                        e.Message.Contains("Unresolved prompt variable") &&
                        e.Message.Contains("unknown_var"))
            .ToList();
        Assert.That(warnings, Has.Count.EqualTo(1),
            "Exactly one Warning log line per distinct unresolved token per step");
    }

    [Test]
    public async Task AssembleAsync_UnknownToken_MultipleOccurrences_LogsWarningOnce()
    {
        WriteAgentFile("agents/test.md",
            "{unknown_var} appears {unknown_var} many times {unknown_var}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        var warnings = _logger.Entries
            .Where(e => e.Level == LogLevel.Warning &&
                        e.Message.Contains("Unresolved prompt variable") &&
                        e.Message.Contains("unknown_var"))
            .ToList();
        Assert.That(warnings, Has.Count.EqualTo(1),
            "Duplicated tokens log once, not once per occurrence");
    }

    // --- AC5-adjacent: escape + case handling ---

    [Test]
    public async Task AssembleAsync_EscapedBraces_RendersLiteralSingleBrace()
    {
        // D3 — `{{today}}` in source → `{today}` in output (no substitution).
        WriteAgentFile("agents/test.md",
            "Author literal: {{today}}. Actual: {today}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Author literal: {today}."));
        Assert.That(result, Does.Contain($"Actual: {PinnedRunDate}."));

        // No Warning should fire for `{{today}}` because it's never exposed as an unresolved token.
        var warnings = _logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("Unresolved prompt variable"))
            .ToList();
        Assert.That(warnings, Is.Empty);
    }

    // --- AC6: substitution does not rewrite runner-emitted Runtime Context ---

    [Test]
    public async Task AssembleAsync_SubstitutionDoesNotRewriteRuntimeContextSection()
    {
        // The runner-emitted tail ("## Runtime Context", "**Available Tools:**",
        // "**Prior Artifacts:**", "## Tool Results Are Untrusted") renders with
        // fully-resolved values — no `{placeholder}` syntax ever reaches the builder
        // tail. If the tail ever contained `{today}`, substitution would silently
        // rewrite it; asserting the tail is free of `{` of known tokens guards this.
        WriteAgentFile("agents/test.md", "Body content, no tokens here.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(
            step,
            tools: [new ToolDescription("read_file", "Reads a file")]);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        var runtimeIdx = result.IndexOf("## Runtime Context", StringComparison.Ordinal);
        Assert.That(runtimeIdx, Is.GreaterThanOrEqualTo(0));
        var tail = result.Substring(runtimeIdx);
        // Runner-emitted tail must not contain any {token} syntax.
        Assert.That(tail, Does.Not.Contain("{today}"));
        Assert.That(tail, Does.Not.Contain("{instance_id}"));
        Assert.That(tail, Does.Not.Contain("{step_index}"));
        Assert.That(tail, Does.Not.Contain("{previous_artifacts}"));
    }

    // --- Sidecar parity ---

    [Test]
    public async Task AssembleAsync_SidecarContentSubstitutesIdenticallyToAgentMarkdown()
    {
        WriteAgentFile("agents/test.md", "Agent today: {today}");
        WriteSidecar("reporter", "Sidecar today: {today}");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain($"Agent today: {PinnedRunDate}"));
        Assert.That(result, Does.Contain($"Sidecar today: {PinnedRunDate}"));
    }

    // --- Story 11.10 AC6: sanctum content flows through the same substitution pipeline ---

    private void WriteSanctumFile(string stepId, string fileName, string content)
    {
        var dir = Path.Combine(_workflowDir, "sidecars", stepId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    [Test]
    public async Task AssembleAsync_SanctumPersonaSubstitutesRuntimeTokens()
    {
        WriteAgentFile("agents/test.md", "# Agent");
        WriteSanctumFile(
            "reporter",
            "PERSONA.md",
            "You are agent on {today}, running as {instance_id}, step index {step_index}.");

        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step, instanceId: "inst-abc");

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain($"You are agent on {PinnedRunDate}, running as inst-abc, step index 0."));
        Assert.That(result, Does.Not.Contain("{today}"));
        Assert.That(result, Does.Not.Contain("{instance_id}"));
        Assert.That(result, Does.Not.Contain("{step_index}"));
    }

    [Test]
    public async Task AssembleAsync_SanctumContentResolvesWorkflowConfigTokens()
    {
        WriteAgentFile("agents/test.md", "# Agent");
        WriteSanctumFile("reporter", "CREED.md", "Our pillars are: {pillars}");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(
            step,
            workflowConfig: new Dictionary<string, string> { ["pillars"] = "Completeness, SEO, Freshness" });

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Our pillars are: Completeness, SEO, Freshness"));
    }

    [Test]
    public async Task AssembleAsync_SanctumContentEscapesDoubleBraces()
    {
        WriteAgentFile("agents/test.md", "# Agent");
        WriteSanctumFile(
            "reporter",
            "PERSONA.md",
            "Literal braces: {{today}}. Substituted: {today}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Literal braces: {today}."));
        Assert.That(result, Does.Contain($"Substituted: {PinnedRunDate}."));
    }

    [Test]
    public async Task AssembleAsync_SanctumUnresolvedToken_WarnsOncePerToken()
    {
        WriteAgentFile("agents/test.md", "# Agent");
        WriteSanctumFile(
            "reporter",
            "PERSONA.md",
            "Mystery: {unknown_sanctum_token}. Same again: {unknown_sanctum_token}.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        // Token remains literal (unchanged).
        Assert.That(result, Does.Contain("Mystery: {unknown_sanctum_token}"));

        var warnings = _logger.Entries
            .Where(e => e.Level == LogLevel.Warning
                && e.Message.Contains("Unresolved prompt variable")
                && e.Message.Contains("unknown_sanctum_token"))
            .ToList();
        Assert.That(warnings, Has.Count.EqualTo(1),
            "A single unresolved token must emit exactly one Warning per step, even with multiple occurrences or across agent/sidecar/sanctum sources");
    }

    [Test]
    public void AssembleAsync_SanctumContentContainingSentinel_ThrowsInvalidOperation()
    {
        WriteAgentFile("agents/test.md", "# Agent");
        // Reserved Unicode non-characters U+FDD0 / U+FDD1 are the brace-escape sentinels.
        WriteSanctumFile(
            "reporter",
            "PERSONA.md",
            $"Illegal: {'\uFDD0'} here.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _assembler.AssemblePromptAsync(context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("reserved sentinel characters"));
        Assert.That(ex.Message, Does.Contain("reporter"));
        // Name the offending file so an author can locate it without bisecting.
        Assert.That(ex.Message, Does.Contain("PERSONA.md"));
    }

    // --- Regression guard: existing behaviour when no tokens ---

    [Test]
    public async Task AssembleAsync_NoTokensInAgentMarkdown_IsNoOp()
    {
        WriteAgentFile("agents/test.md", "Just plain prose with no substitutable content.");
        var step = MakeStep("reporter", "Reporter");
        var context = MakeContext(step);

        var result = await _assembler.AssemblePromptAsync(context, CancellationToken.None);

        Assert.That(result, Does.Contain("Just plain prose with no substitutable content."));
        var warnings = _logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("Unresolved prompt variable"))
            .ToList();
        Assert.That(warnings, Is.Empty);
    }

    // --- Capturing logger helper (shared nowhere else — colocated with these tests) ---

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
