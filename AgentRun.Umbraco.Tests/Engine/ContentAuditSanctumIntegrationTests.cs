using Microsoft.Extensions.Logging.Abstractions;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

// Story 11.10 AC8 + AC10 — integration test over the shipped
// `umbraco-content-audit` workflow. Asserts that every step (scanner /
// analyser / reporter) has PERSONA + CREED + CAPABILITIES sanctum files
// present, that PromptAssembler renders the `## Agent Sanctum` section with
// all three subsections, and that the rendered content matches the extracted
// identity/principles from the shipped agent .md files.
//
// This is the production-surface proof that the pattern works — a regression
// where any of the 9 sanctum files goes missing or the PromptAssembler fails
// to load them fails this fixture immediately.
[TestFixture]
public class ContentAuditSanctumIntegrationTests
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

    // Points at the CANONICAL shipped workflow under AgentRun.Umbraco/Workflows/
    // — what's embedded in the NuGet package and copied to App_Data on first run.
    // TestSite's App_Data/AgentRun.Umbraco/ is .gitignore'd, so a CI checkout has
    // nothing there. Canonical path is the CI-stable source of truth.
    private static string ContentAuditFolder() =>
        Path.Combine(
            RepoRoot(),
            "AgentRun.Umbraco",
            "Workflows",
            "umbraco-content-audit");

    [TestCaseSource(nameof(ContentAuditStepIds))]
    public void EveryContentAuditStep_HasCompleteSanctumOnDisk(string stepId)
    {
        var folder = Path.Combine(ContentAuditFolder(), "sidecars", stepId);

        Assert.That(Directory.Exists(folder), Is.True,
            $"Sidecar folder missing for {stepId}: {folder}");

        foreach (var fileName in new[] { "PERSONA.md", "CREED.md", "CAPABILITIES.md" })
        {
            var filePath = Path.Combine(folder, fileName);
            Assert.That(File.Exists(filePath), Is.True,
                $"Sanctum file missing: {filePath}");

            var content = File.ReadAllText(filePath).TrimEnd();
            Assert.That(content, Is.Not.Empty,
                $"Sanctum file is empty: {filePath}");
        }
    }

    [TestCaseSource(nameof(ContentAuditStepIds))]
    public async Task AssembledPrompt_ForContentAuditStep_ContainsAgentSanctumWithAllSubsections(string stepId)
    {
        var workflowFolder = ContentAuditFolder();
        var instanceDir = Path.Combine(
            Path.GetTempPath(),
            "agentrun-contentaudit-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instanceDir);

        try
        {
            var assembler = new PromptAssembler(NullLogger<PromptAssembler>.Instance, TimeProvider.System);
            var step = new StepDefinition
            {
                Id = stepId,
                Name = stepId,
                Agent = $"agents/{stepId}.md"
            };

            var context = new PromptAssemblyContext(
                WorkflowFolderPath: workflowFolder,
                Step: step,
                AllSteps: [new StepState { Id = stepId, Status = StepStatus.Active }],
                AllStepDefinitions: [step],
                InstanceFolderPath: instanceDir,
                DeclaredTools: []);

            var result = await assembler.AssemblePromptAsync(context, CancellationToken.None);

            Assert.That(result, Does.Contain("## Agent Sanctum"),
                $"Step {stepId} prompt is missing the Agent Sanctum section");
            Assert.That(result, Does.Contain("### INDEX"));
            Assert.That(result, Does.Contain("- **PERSONA** —"));
            Assert.That(result, Does.Contain("- **CREED** —"));
            Assert.That(result, Does.Contain("- **CAPABILITIES** —"));
            Assert.That(result, Does.Contain("### Persona"));
            Assert.That(result, Does.Contain("### Creed"));
            Assert.That(result, Does.Contain("### Capabilities"));

            // Sanctum slot sits between sidecar and Runtime Context — the
            // stable-first caching boundary that Story 11.5 depends on.
            var sanctumIdx = result.IndexOf("## Agent Sanctum", StringComparison.Ordinal);
            var runtimeIdx = result.IndexOf("## Runtime Context", StringComparison.Ordinal);
            Assert.That(sanctumIdx, Is.GreaterThan(0));
            Assert.That(runtimeIdx, Is.GreaterThan(sanctumIdx),
                "Sanctum must sit BEFORE Runtime Context for the cache prefix to survive");
        }
        finally
        {
            if (Directory.Exists(instanceDir))
            {
                Directory.Delete(instanceDir, recursive: true);
            }
        }
    }

    [TestCaseSource(nameof(ContentAuditStepIds))]
    public void AgentMarkdown_DoesNotDuplicateIdentityOrPrinciplesHeadings(string stepId)
    {
        var agentPath = Path.Combine(ContentAuditFolder(), "agents", $"{stepId}.md");
        var content = File.ReadAllText(agentPath);

        // After extraction to sanctum, the agent file should no longer carry its
        // own `## Identity` or `## Principles` headings — those live in
        // PERSONA.md and CREED.md respectively. A regression that re-inlines
        // either heading would duplicate identity content in the assembled prompt.
        Assert.That(content, Does.Not.Contain("\n## Identity\n"),
            $"Agent file {stepId}.md still has a `## Identity` heading — should be in PERSONA.md");
        Assert.That(content, Does.Not.Contain("\n## Principles\n"),
            $"Agent file {stepId}.md still has a `## Principles` heading — should be in CREED.md");
    }
}
