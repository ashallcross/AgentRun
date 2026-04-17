using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Workflows;

/// <summary>
/// Regression gate for Story 11.6 — proves the per-step profile cascade
/// wires correctly from workflow.yaml on disk through the registry to the
/// <see cref="ProfileResolver"/> and out to <see cref="IAIChatClientFactory"/>.
/// Unit coverage in ProfileResolverTests + WorkflowParserTests is sufficient
/// for the individual layers; this file asserts the seam between them.
/// </summary>
[TestFixture]
public class ProfileOverrideIntegrationTests
{
    private string _tempRoot = null!;
    private WorkflowRegistry _registry = null!;
    private IAIChatClientFactory _chatClientFactory = null!;
    private IChatClient _chatClient = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);

        _registry = new WorkflowRegistry(
            new WorkflowParser(),
            new WorkflowValidator(),
            Enumerable.Empty<IWorkflowTool>(),
            NullLogger<WorkflowRegistry>.Instance);

        _chatClientFactory = Substitute.For<IAIChatClientFactory>();
        _chatClient = Substitute.For<IChatClient>();
        _chatClientFactory
            .CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public async Task RegisteredStep_WithExplicitProfile_ResolvesToStepAlias()
    {
        // Arrange — workflow.yaml declares workflow-level default_profile AND a step-level override
        var yaml = """
            name: Profile Override Integration
            description: Covers the Story 11.6 cascade end-to-end
            default_profile: workflow-default
            steps:
              - id: reporter
                name: Reporter
                agent: agents/reporter.md
                profile: step-override
            """;
        SetupWorkflowOnDisk("profile-override-wf", yaml, agentRelativePaths: ["agents/reporter.md"]);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var registered = _registry.GetWorkflow("profile-override-wf");
        Assert.That(registered, Is.Not.Null, "workflow should load cleanly");

        var reporterStep = registered!.Definition.Steps.Single(s => s.Id == "reporter");
        var resolver = CreateResolver();

        // Act
        var client = await resolver.ResolveAndGetClientAsync(
            reporterStep,
            registered.Definition,
            CancellationToken.None);

        // Assert
        Assert.That(client, Is.SameAs(_chatClient));
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "step-override", "step-reporter", Arg.Any<CancellationToken>());
        await _chatClientFactory.DidNotReceive().CreateChatClientAsync(
            "workflow-default", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisteredStep_WithoutProfile_ResolvesToWorkflowDefault()
    {
        // Arrange — step has no explicit profile; cascade must fall back to workflow default_profile
        var yaml = """
            name: Workflow Default Fallback
            description: Covers the Story 11.6 workflow-default tier
            default_profile: workflow-default
            steps:
              - id: scanner
                name: Scanner
                agent: agents/scanner.md
            """;
        SetupWorkflowOnDisk("workflow-default-wf", yaml, agentRelativePaths: ["agents/scanner.md"]);

        await _registry.LoadWorkflowsAsync(_tempRoot);

        var registered = _registry.GetWorkflow("workflow-default-wf");
        Assert.That(registered, Is.Not.Null, "workflow should load cleanly");

        var scannerStep = registered!.Definition.Steps.Single(s => s.Id == "scanner");
        Assert.That(scannerStep.Profile, Is.Null, "step profile must be absent for this test");
        var resolver = CreateResolver();

        // Act
        await resolver.ResolveAndGetClientAsync(
            scannerStep,
            registered.Definition,
            CancellationToken.None);

        // Assert
        await _chatClientFactory.Received(1).CreateChatClientAsync(
            "workflow-default", "step-scanner", Arg.Any<CancellationToken>());
    }

    private ProfileResolver CreateResolver()
    {
        return new ProfileResolver(
            _chatClientFactory,
            Options.Create(new AgentRunOptions()),
            NullLogger<ProfileResolver>.Instance);
    }

    private void SetupWorkflowOnDisk(string alias, string yamlContent, IEnumerable<string> agentRelativePaths)
    {
        var folder = Path.Combine(_tempRoot, alias);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "workflow.yaml"), yamlContent);

        foreach (var agentRelative in agentRelativePaths)
        {
            var agentFullPath = Path.Combine(folder, agentRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(agentFullPath)!);
            File.WriteAllText(agentFullPath, "# stub agent");
        }
    }
}
