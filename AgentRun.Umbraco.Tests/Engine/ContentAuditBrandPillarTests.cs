using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

// Story 11.13 — regression gate for the Content Audit v2 Brand pillar feature.
// Asserts three things end-to-end against the canonical shipped
// `umbraco-content-audit` workflow:
//
// 1. **Schema + structure:** workflow.yaml carries config.brand_voice_context
//    (empty-string default) and the required new tool declarations on scanner
//    + analyser.
//
// 2. **Dual-variant LLM-facing content is present on disk.** Both the 6-pillar
//    and 7-pillar verbatim-locked opening lines live in scanner.md; the Brand
//    rubric lives in analyser.md; the Brand cluster example lives in
//    reporter.md. A regression that accidentally paraphrases or deletes one
//    variant fails here immediately.
//
// 3. **`{brand_voice_context}` substitutes into the assembled prompt** under
//    both the disabled (empty string) and enabled (alias) paths, proving the
//    Brand Pillar Configuration preamble renders with the right value so the
//    LLM can select the correct variant.
//
// No runtime LLM round-trip — that's Adam's manual E2E gate (Task 10 of the
// story spec). These tests protect against workflow-content drift in CI.
[TestFixture]
public class ContentAuditBrandPillarTests
{
    private static readonly string[] ContentAuditStepIds = ["scanner", "analyser", "reporter"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentRun.Umbraco.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate repository root from test base directory — expected AgentRun.Umbraco.slnx marker");
        }
        return dir.FullName;
    }

    private static string ContentAuditFolder() =>
        Path.Combine(RepoRoot(), "AgentRun.Umbraco", "Workflows", "umbraco-content-audit");

    private static string FixtureFolder() =>
        Path.Combine(RepoRoot(), "AgentRun.Umbraco.Tests", "TestFixtures");

    // Tests assert against prompt-file contents which developers may author on
    // either LF (macOS / Linux) or CRLF (Windows) machines. `core.autocrlf=true`
    // checkouts carry CRLF. Normalise on read so substring assertions bind to
    // semantic content, not line-ending convention.
    private static string ReadAndNormalize(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n");

    // --- AC1: workflow.yaml schema + structure ---

    [Test]
    public void WorkflowYaml_HasBrandVoiceContextConfigAndToolAdditions()
    {
        // Parse directly via WorkflowParser. Avoids WorkflowRegistry's
        // tool-registration gate — this test is about YAML structure, not
        // about runtime tool availability (covered by composer-smoke tests
        // on the tools themselves in Stories 11.9 + 11.12).
        var yaml = File.ReadAllText(
            Path.Combine(ContentAuditFolder(), "workflow.yaml"));
        var definition = new WorkflowParser().Parse(yaml);

        Assert.That(definition.Config, Is.Not.Null,
            "workflow.yaml must declare a config: block");
        Assert.That(definition.Config!.ContainsKey("brand_voice_context"), Is.True,
            "config block must carry brand_voice_context key");
        Assert.That(definition.Config["brand_voice_context"], Is.EqualTo(""),
            "canonical default must be empty string (Brand disabled); v1.1 byte-compat path");

        var scanner = definition.Steps.Single(s => s.Id == "scanner");
        Assert.That(scanner.Tools, Does.Contain("get_ai_context"),
            "scanner needs get_ai_context for the Brand pre-validation gate (D3)");
        Assert.That(scanner.Tools, Does.Contain("search_content"),
            "scanner needs search_content for discovery probes (D3)");

        var analyser = definition.Steps.Single(s => s.Id == "analyser");
        Assert.That(analyser.Tools, Does.Contain("get_ai_context"),
            "analyser needs get_ai_context for Brand scoring (D3)");

        var reporter = definition.Steps.Single(s => s.Id == "reporter");
        Assert.That(reporter.Tools, Does.Not.Contain("get_ai_context"),
            "reporter must not have get_ai_context — it reads scored output (D3)");
        Assert.That(reporter.Tools, Does.Not.Contain("search_content"),
            "reporter tools unchanged from v1.1");

        // Engine boundary: WorkflowValidator accepts the file shape (schema +
        // structural rules). Tool-registration is a WorkflowRegistry concern
        // (11.9 / 11.12 composer-smoke tests cover real DI resolution).
        var validationResult = new WorkflowValidator().Validate(yaml);
        Assert.That(validationResult.Errors, Is.Empty,
            $"WorkflowValidator must accept the upgraded workflow: {string.Join("; ", validationResult.Errors.Select(e => e.Message))}");
    }

    // --- AC2 + AC3: dual-variant verbatim content on disk ---

    [Test]
    public void ScannerMd_ContainsBoth6PillarAnd7PillarOpeningLineVariants()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        // 6-pillar opening: byte-identical to v1.1 verbatim-locked text
        Assert.That(scanner, Does.Contain("Default is all six:"),
            "6-pillar opening line verbatim-locked text must be preserved byte-identical");
        Assert.That(scanner, Does.Contain("- Accessibility\n\nJust say"),
            "6-pillar opening line bullet list terminates at Accessibility (no Brand bullet)");

        // 7-pillar opening: new verbatim-locked text
        Assert.That(scanner, Does.Contain("Default is all seven:"),
            "7-pillar opening line must declare 7 pillars");
        Assert.That(scanner, Does.Contain("- Accessibility\n- Brand\n\nJust say"),
            "7-pillar opening line bullet list must include Brand after Accessibility");

        // Dual-variant selection instruction
        Assert.That(scanner, Does.Contain("## Opening Line — Variant Selection"));
        Assert.That(scanner, Does.Contain("### 6-Pillar Opening Line (Verbatim, Locked)"));
        Assert.That(scanner, Does.Contain("### 7-Pillar Opening Line (Verbatim, Locked)"));
    }

    // Byte-match fixtures catch paraphrase drift that substring assertions
    // miss. Each fixture captures the full body of a 6-pillar verbatim-locked
    // surface; any word-level edit fails this test.
    [TestCase("ScannerOpeningLine6Pillar.verbatim.txt",
        TestName = "SixPillarOpeningLine")]
    [TestCase("ScannerCapabilityAnswer6Pillar.verbatim.txt",
        TestName = "SixPillarCapabilityAnswer")]
    [TestCase("ScannerReprompt6Pillar.verbatim.txt",
        TestName = "SixPillarReprompt")]
    [TestCase("ScannerCharitableConfirmation6Pillar.verbatim.txt",
        TestName = "SixPillarCharitableConfirmation")]
    public void ScannerMd_SixPillarVerbatimLockedVariantsMatchFixtures(string fixtureFile)
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));
        var fixture = ReadAndNormalize(
            Path.Combine(FixtureFolder(), fixtureFile));

        Assert.That(scanner, Does.Contain(fixture),
            $"6-pillar verbatim-locked surface must be byte-identical to fixture '{fixtureFile}'. " +
            "Any paraphrase of v1.1 text violates the verbatim-lock invariant.");
    }

    [Test]
    public void ScannerMd_ContainsBrandHaltMessages_AndPreValidationGate()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        // Halt messages are now differentiated by error code (D2 resolution —
        // code review 2026-04-21). Three verbatim-locked variants live under
        // the umbrella `## Brand Pillar Halt Messages` section.
        Assert.That(scanner, Does.Contain("## Brand Pillar Halt Messages"),
            "Brand halt messages umbrella section must be present");
        Assert.That(scanner, Does.Contain("### Brand Pillar Halt Message — Configuration Error (Verbatim, Locked)"),
            "Configuration Error halt variant must be present (for `not_found` / `invalid_argument` codes)");
        Assert.That(scanner, Does.Contain("### Brand Pillar Halt Message — Service Unavailable (Verbatim, Locked)"),
            "Service Unavailable halt variant must be present (for `context_service_failure` code)");
        Assert.That(scanner, Does.Contain("### Brand Pillar Halt Message — Unexpected Error (Verbatim, Locked)"),
            "Unexpected Error halt variant must be present (fallback for other codes)");

        // Opening lines — each variant leads with a distinct first line.
        Assert.That(scanner, Does.Contain("**Brand pillar could not be enabled.**"),
            "Configuration Error variant's opening line (AC4)");
        Assert.That(scanner, Does.Contain("**Brand pillar could not be enabled — Umbraco.AI Context service is currently unavailable.**"),
            "Service Unavailable variant's opening line");
        Assert.That(scanner, Does.Contain("**Brand pillar could not be enabled — unexpected error.**"),
            "Unexpected Error variant's opening line");

        // Configuration Error closing line must direct the user to re-run
        // after fixing config (the original AC4 text, preserved verbatim).
        Assert.That(scanner, Does.Contain("No audit was started. Re-run the workflow after fixing the configuration."),
            "Configuration Error halt closing line must direct the user to re-run");

        // Scanner step 0 decision tree references the real error codes the
        // GetAiContextTool emits — not the phantom `alias_not_found` the
        // original spec used.
        Assert.That(scanner, Does.Contain("\"error\": \"not_found\""),
            "Step 0 must pattern-match on the real `not_found` error code emitted by GetAiContextTool");
        Assert.That(scanner, Does.Contain("\"error\": \"invalid_argument\""),
            "Step 0 must recognise `invalid_argument` envelope (malformed alias)");
        Assert.That(scanner, Does.Contain("\"error\": \"context_service_failure\""),
            "Step 0 must recognise `context_service_failure` envelope (transient service failure)");

        // Brand Pillar Pre-Validation Gate (step 0)
        Assert.That(scanner, Does.Contain("Brand Pillar Pre-Validation Gate"),
            "Step 0 Brand pre-validation gate must be present in Your Task");
        Assert.That(scanner, Does.Contain("get_ai_context(alias: \"{brand_voice_context}\")"),
            "Pre-validation gate must call get_ai_context with the substituted alias");
    }

    [Test]
    public void AnalyserMd_ContainsBrandRubricAndGetAiContextFlow()
    {
        var analyser = File.ReadAllText(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        Assert.That(analyser, Does.Contain("### Brand (1–10)"),
            "Brand scoring rubric section must be present (D5)");
        Assert.That(analyser, Does.Contain("Tone alignment"),
            "Brand rubric must describe the tone-alignment dimension");
        Assert.That(analyser, Does.Contain("Terminology consistency"),
            "Brand rubric must describe the terminology-consistency dimension");
        Assert.That(analyser, Does.Contain("Voice drift"),
            "Brand rubric must describe the voice-drift dimension");

        // Instruction step 2a for get_ai_context call
        Assert.That(analyser, Does.Contain("2a."),
            "Analyser instructions must include step 2a for the get_ai_context call");
        Assert.That(analyser, Does.Contain("Brand voice context:"),
            "Analyser must know to read the alias from the Audit Configuration block");
    }

    [Test]
    public void ReporterMd_ContainsBrandClusterExampleAndHealthGradeNote()
    {
        var reporter = File.ReadAllText(
            Path.Combine(ContentAuditFolder(), "agents", "reporter.md"));

        Assert.That(reporter, Does.Contain("Example cluster (Brand"),
            "Reporter must carry a Brand Consistency cluster example (D7)");
        Assert.That(reporter, Does.Contain("7-pillar mean"),
            "Reporter's At-a-Glance section must explain the 7-pillar mean for health grade");
    }

    // --- AC9: sanctum additive content ---

    [Test]
    public void ScannerSanctum_CapabilitiesContainsNewToolRows()
    {
        var caps = File.ReadAllText(Path.Combine(
            ContentAuditFolder(), "sidecars", "scanner", "CAPABILITIES.md"));

        Assert.That(caps, Does.Contain("`get_ai_context`"),
            "Scanner CAPABILITIES must list get_ai_context (Brand pre-validation)");
        Assert.That(caps, Does.Contain("`search_content`"),
            "Scanner CAPABILITIES must list search_content (discovery probes)");

        var creed = File.ReadAllText(Path.Combine(
            ContentAuditFolder(), "sidecars", "scanner", "CREED.md"));
        Assert.That(creed, Does.Contain("Brand voice context gates the Brand pillar"),
            "Scanner CREED must carry the Brand-gate discipline bullet (D8)");
    }

    [Test]
    public void AnalyserSanctum_CapabilitiesContainsGetAiContext_CreedTrustBullet()
    {
        var caps = File.ReadAllText(Path.Combine(
            ContentAuditFolder(), "sidecars", "analyser", "CAPABILITIES.md"));
        Assert.That(caps, Does.Contain("`get_ai_context`"),
            "Analyser CAPABILITIES must list get_ai_context");

        var creed = File.ReadAllText(Path.Combine(
            ContentAuditFolder(), "sidecars", "analyser", "CREED.md"));
        Assert.That(creed, Does.Contain("Brand voice context is author-curated"),
            "Analyser CREED must carry the trust-model bullet (D8)");
    }

    [Test]
    public void ReporterSanctum_CreedContainsBrandClusteringBullet()
    {
        var creed = File.ReadAllText(Path.Combine(
            ContentAuditFolder(), "sidecars", "reporter", "CREED.md"));
        Assert.That(creed, Does.Contain("Brand Consistency clusters frame terminology"),
            "Reporter CREED must carry the Brand-clustering bullet (D8)");
    }

    // --- AC2/AC3: PromptAssembler substitutes {brand_voice_context} under both paths ---

    [TestCase("", TestName = "BrandDisabled")]
    [TestCase("site-brand-voice", TestName = "BrandEnabled")]
    public async Task ScannerPrompt_BrandPreambleRendersConfiguredAlias(string configuredAlias)
    {
        var workflowFolder = ContentAuditFolder();
        var instanceDir = Path.Combine(
            Path.GetTempPath(),
            "agentrun-brandpillar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instanceDir);

        try
        {
            var assembler = new PromptAssembler(
                NullLogger<PromptAssembler>.Instance,
                new FakeTimeProvider(DateTimeOffset.Parse("2026-04-20T12:00:00Z")));

            var step = new StepDefinition
            {
                Id = "scanner",
                Name = "scanner",
                Agent = "agents/scanner.md"
            };

            var context = new PromptAssemblyContext(
                WorkflowFolderPath: workflowFolder,
                Step: step,
                AllSteps: [new StepState { Id = "scanner", Status = StepStatus.Active }],
                AllStepDefinitions: [step],
                InstanceFolderPath: instanceDir,
                DeclaredTools: [],
                InstanceId: "brandpillar-test-instance",
                WorkflowConfig: new Dictionary<string, string>
                {
                    ["brand_voice_context"] = configuredAlias
                });

            var prompt = await assembler.AssemblePromptAsync(context, CancellationToken.None);

            // The Brand Pillar Configuration preamble must render the
            // substituted alias. When disabled, renders `****` (empty-bold);
            // when enabled, renders `**site-brand-voice**`.
            Assert.That(prompt, Does.Contain($"Brand voice context alias: **{configuredAlias}**"),
                $"Scanner preamble must show the substituted alias (configured: '{configuredAlias}')");
            Assert.That(prompt, Does.Not.Contain("{brand_voice_context}"),
                "The token must be fully substituted — no literal braces in the rendered prompt");

            // Both variants are physically present in the assembled prompt
            // (the LLM selects at runtime). Verify both are reachable.
            Assert.That(prompt, Does.Contain("Default is all six:"),
                "6-pillar variant must be present in assembled prompt");
            Assert.That(prompt, Does.Contain("Default is all seven:"),
                "7-pillar variant must be present in assembled prompt");
        }
        finally
        {
            if (Directory.Exists(instanceDir))
            {
                Directory.Delete(instanceDir, recursive: true);
            }
        }
    }
}
