using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

// Story 11.16 — regression gate for the get_content body-extraction workflow
// integration. Asserts three things against the canonical shipped
// `umbraco-content-audit` workflow and its agent prompts:
//
// 1. **workflow.yaml config key.** The `content_audit_body_max_chars` key is
//    present in the config block with the default "15000" string value.
//
// 2. **scanner.md per-node template.** The old `- **Body sample ...**` line is
//    gone; the new `- **Body text:**` and `- **Body metadata:**` lines are
//    present in the Scan-Results Output Template block. The Step 3 instructions
//    direct the LLM to read the tool's pre-extracted fields rather than
//    re-extract prose itself.
//
// 3. **analyser.md rubrics.** Readability and Brand rubrics reference
//    `body_text`; Accessibility rubric references `body_metadata` AND carries
//    the "always fully extracted regardless of content length" note.
//    Truncation-marker citation guidance is present in Readability + Brand.
//
// No runtime LLM round-trip — Task 12 manual E2E is Adam's gate. These tests
// protect against workflow-content drift in CI post-11.16.
[TestFixture]
public class ContentAuditBodyExtractionTests
{
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

    private static string ReadAndNormalize(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n");

    // ---------- AC3: workflow.yaml config key ----------

    [Test]
    public void WorkflowYaml_HasContentAuditBodyMaxCharsConfigKey()
    {
        var yaml = File.ReadAllText(
            Path.Combine(ContentAuditFolder(), "workflow.yaml"));
        var definition = new WorkflowParser().Parse(yaml);

        Assert.That(definition.Config, Is.Not.Null,
            "workflow.yaml must declare a config: block");
        Assert.That(definition.Config!.ContainsKey("content_audit_body_max_chars"), Is.True,
            "config block must carry content_audit_body_max_chars key (11.16 / AC3 / D1)");
        Assert.That(definition.Config["content_audit_body_max_chars"], Is.EqualTo("15000"),
            "canonical default is 15000 (D1 fallback ladder)");

        // Validator must still accept the file — D1 introduces no new schema,
        // just a new key-value in the existing patternProperties config block.
        var validationResult = new WorkflowValidator().Validate(yaml);
        Assert.That(validationResult.Errors, Is.Empty,
            $"WorkflowValidator must accept the upgraded workflow: {string.Join("; ", validationResult.Errors.Select(e => e.Message))}");
    }

    // ---------- AC8: scanner.md per-node template ----------

    [Test]
    public void ScannerMd_PerNodeTemplate_HasBodyTextAndBodyMetadataLines()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        Assert.That(scanner, Does.Contain("- **Body text:**"),
            "Scanner per-node template must include the Body text line (11.16 / AC8)");
        Assert.That(scanner, Does.Contain("- **Body metadata:**"),
            "Scanner per-node template must include the Body metadata line (11.16 / AC8)");
    }

    [Test]
    public void ScannerMd_PerNodeTemplate_RemovesOldBodySampleLine()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        Assert.That(scanner, Does.Not.Contain("- **Body sample (first ~500 chars"),
            "The pre-11.16 `Body sample` template line must be removed (replaced by Body text + Body metadata)");
    }

    [Test]
    public void ScannerMd_Step3Instructions_DirectLlmToReadPreExtractedFields()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        Assert.That(scanner, Does.Contain("get_content` response DIRECTLY"),
            "Scanner Step 3 must instruct the LLM to read get_content fields directly — not re-extract prose (11.16 / AC8)");
        Assert.That(scanner, Does.Contain("pre-computes `body_text`"),
            "Scanner Step 3 must name the pre-computed body_text field");
        Assert.That(scanner, Does.Contain("body_metadata"),
            "Scanner Step 3 must name the pre-computed body_metadata field");
    }

    [Test]
    public void ScannerMd_DocumentsNullAndEmptyStateFallbacks()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        Assert.That(scanner, Does.Contain("N/A — no body-copy property on this content type"),
            "Scanner must document the AC5 null-state fallback text");
        Assert.That(scanner, Does.Contain("Empty — body property exists but has no content"),
            "Scanner must document the AC6 empty-string fallback text");
    }

    [Test]
    public void ScannerMd_DocumentsTruncationMarkerRenderingAndMetadataUncappedNote()
    {
        var scanner = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "scanner.md"));

        Assert.That(scanner, Does.Contain("[...truncated at N of M chars]"),
            "Scanner must document the truncation marker literal for the LLM to render verbatim");
        Assert.That(scanner, Does.Contain("Body metadata is always complete"),
            "Scanner must state that body_metadata is uncapped (AC4 / D2 invariant — prevents LLM applying the cap caveat to structural audits)");
    }

    // ---------- AC9: analyser.md rubric updates ----------

    [Test]
    public void AnalyserMd_ReadabilityRubric_ReferencesBodyText()
    {
        var analyser = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        // Anchor to the Readability rubric context by checking both the section
        // and the body_text reference appear together.
        Assert.That(analyser, Does.Contain("### Readability"));
        Assert.That(analyser, Does.Contain("body_text` value recorded in the scan-results.md \"Body text:\" line"),
            "Readability rubric must read from body_text in scan-results.md (11.16 / AC9)");
        Assert.That(analyser, Does.Contain("body_text averages 34 words/sentence"),
            "Readability good-finding example must use body_text (not bodyContent)");
    }

    [Test]
    public void AnalyserMd_ReadabilityRubric_HasThreeStateFallback()
    {
        var analyser = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        // AC5 null → Not applicable
        Assert.That(analyser, Does.Contain("N/A — no body-copy property on this content type"),
            "Readability must cite the AC5 null-state Body text sentinel for Not-applicable routing");
        // AC6 empty → 3/10 score (NOT Not applicable — D11 load-bearing distinction)
        Assert.That(analyser, Does.Contain("Empty — body property exists but has no content"),
            "Readability must cite the AC6 empty-state Body text sentinel");
        Assert.That(analyser, Does.Contain("Readability: 3/10 — body property is empty"),
            "Readability must score empty body as 3/10 per D11 load-bearing distinction");
    }

    [Test]
    public void AnalyserMd_AccessibilityRubric_ReferencesBodyMetadataAndAlwaysFullyExtractedNote()
    {
        var analyser = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        Assert.That(analyser, Does.Contain("### Accessibility"));
        Assert.That(analyser, Does.Contain("body_metadata` structural audit recorded in scan-results.md \"Body metadata:\""),
            "Accessibility rubric must read from body_metadata (11.16 / AC9)");
        Assert.That(analyser, Does.Contain("Body metadata is ALWAYS fully extracted regardless of content length"),
            "Accessibility rubric must carry the D2 always-uncapped invariant statement to prevent LLM applying the body_text cap to structural findings");
    }

    [Test]
    public void AnalyserMd_AccessibilityRubric_UsesThreeStateAltDistinctionInGoodFinding()
    {
        var analyser = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        // D6 three-state alt distinction: valid / empty (decorative) / null (missing WCAG failure)
        Assert.That(analyser, Does.Contain("alt: null in body_metadata"),
            "Accessibility good-finding must cite alt: null as the WCAG-failing state");
        Assert.That(analyser, Does.Contain("alt: \"\""),
            "Accessibility good-finding must cite alt: \"\" as the accessibility-correct decorative state");
    }

    [Test]
    public void AnalyserMd_BrandRubric_ReferencesBodyText()
    {
        var analyser = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        Assert.That(analyser, Does.Contain("### Brand"));
        Assert.That(analyser, Does.Contain("body_text contains deprecated product name"),
            "Brand good-finding example must cite body_text (not bodyContent)");
        Assert.That(analyser, Does.Contain("scanned node's `body_text` AND references specific brand rules"),
            "Brand evidence discipline must reference body_text (11.16 / AC9)");
    }

    [Test]
    public void AnalyserMd_TruncationMarkerCitationGuidancePresentInReadabilityAndBrand()
    {
        var analyser = ReadAndNormalize(
            Path.Combine(ContentAuditFolder(), "agents", "analyser.md"));

        // The truncation-marker citation pattern appears in both Readability and
        // Brand (the two pillars that consume body_text and can therefore be
        // affected by the cap).
        Assert.That(analyser, Does.Contain("[...truncated at N of M chars]"),
            "Analyser must reference the literal truncation marker so the LLM cites it in findings");
        Assert.That(analyser, Does.Contain("first 15000 of 42800 chars"),
            "Analyser must show an example of how to cite the sampling boundary in a finding");
    }
}
