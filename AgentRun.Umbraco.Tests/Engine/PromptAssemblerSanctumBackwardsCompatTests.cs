using Microsoft.Extensions.Logging.Abstractions;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

// Story 11.10 AC5 + AC10 backwards-compat fixture.
//
// Runs PromptAssembler against every shipped TestSite workflow that does NOT
// introduce sanctum files. Asserts that no `## Agent Sanctum` section is
// emitted for any of their steps. This proves the hard backwards-compat
// constraint: agents without sanctum files behave identically to pre-11.10 —
// same prompt shape, same section boundaries, same stable-prefix discipline
// (Story 11.5) — so any third-party adopter's existing workflows keep working
// unchanged after the package update.
//
// The fixture uses the real filesystem layout under
// `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/` so drift
// between shipped defaults and the loader is caught at test-time, not at
// runtime.
[TestFixture]
public class PromptAssemblerSanctumBackwardsCompatTests
{
    // Backwards-compat proof over the canonical shipped workflows (under
    // AgentRun.Umbraco/Workflows/, gitted) that do NOT author sanctum files.
    // Only these two workflows ship to every NuGet adopter; the local TestSite
    // workflows (content-tools-test / web-search-test / hip-hop-lawyer-test)
    // are dev scratchpads, not part of the package, so they're not suitable
    // for a CI-stable fixture.
    private static readonly string[] BackwardsCompatWorkflowSlugs =
    [
        "content-quality-audit",
        "accessibility-quick-scan",
    ];

    private static string RepoRoot()
    {
        // Walk up from the test assembly location until we find the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentRun.Umbraco.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate repository root from test base directory — expected AgentRun.Umbraco.slnx marker file");
        }
        return dir.FullName;
    }

    private static string WorkflowFolder(string slug) =>
        Path.Combine(
            RepoRoot(),
            "AgentRun.Umbraco",
            "Workflows",
            slug);

    [TestCaseSource(nameof(BackwardsCompatWorkflowSlugs))]
    public async Task ShippedWorkflow_WithoutSanctumFiles_AssembledPromptHasNoSanctumSection(string slug)
    {
        var workflowFolder = WorkflowFolder(slug);
        if (!Directory.Exists(workflowFolder))
        {
            Assert.Ignore($"Workflow {slug} not present in TestSite — skipping backwards-compat fixture");
            return;
        }

        // Guard: none of the BW-compat workflows should have sanctum files. If
        // one ever does, the author consciously opted in and this fixture should
        // not flag it — but we DO want a loud signal, so fail with a pointer.
        var sidecarsRoot = Path.Combine(workflowFolder, "sidecars");
        if (Directory.Exists(sidecarsRoot))
        {
            var stray = Directory.EnumerateFiles(sidecarsRoot, "*.md", SearchOption.AllDirectories)
                .Where(p =>
                {
                    var name = Path.GetFileName(p);
                    return string.Equals(name, "PERSONA.md", StringComparison.Ordinal)
                        || string.Equals(name, "CREED.md", StringComparison.Ordinal)
                        || string.Equals(name, "CAPABILITIES.md", StringComparison.Ordinal);
                })
                .ToList();
            Assert.That(stray, Is.Empty,
                $"Workflow {slug} unexpectedly contains sanctum files — if intentional, move it off the backwards-compat fixture list. Stray files: {string.Join(", ", stray)}");
        }

        // Probe every step's assembled prompt for the sanctum marker.
        var agentsFolder = Path.Combine(workflowFolder, "agents");
        if (!Directory.Exists(agentsFolder))
        {
            Assert.Inconclusive($"Workflow {slug} has no agents/ folder — skipping");
            return;
        }

        var tempInstanceDir = Path.Combine(
            Path.GetTempPath(),
            "agentrun-bwcompat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstanceDir);

        try
        {
            var assembler = new PromptAssembler(NullLogger<PromptAssembler>.Instance, TimeProvider.System);

            foreach (var agentPath in Directory.EnumerateFiles(agentsFolder, "*.md"))
            {
                var stepId = Path.GetFileNameWithoutExtension(agentPath);
                var relativeAgent = Path.Combine("agents", Path.GetFileName(agentPath));
                var step = new StepDefinition
                {
                    Id = stepId,
                    Name = stepId,
                    Agent = relativeAgent
                };

                var context = new PromptAssemblyContext(
                    WorkflowFolderPath: workflowFolder,
                    Step: step,
                    AllSteps: [new StepState { Id = stepId, Status = StepStatus.Active }],
                    AllStepDefinitions: [step],
                    InstanceFolderPath: tempInstanceDir,
                    DeclaredTools: []);

                var result = await assembler.AssemblePromptAsync(context, CancellationToken.None);

                Assert.That(result, Does.Not.Contain("## Agent Sanctum"),
                    $"Workflow {slug}, step {stepId} emitted an Agent Sanctum section without any sanctum files present — backwards-compat broken");
                Assert.That(result, Does.Not.Contain("### INDEX"),
                    $"Workflow {slug}, step {stepId} emitted an INDEX subsection without any sanctum files present — backwards-compat broken");
            }
        }
        finally
        {
            if (Directory.Exists(tempInstanceDir))
            {
                Directory.Delete(tempInstanceDir, recursive: true);
            }
        }
    }
}
