using Microsoft.Extensions.AI;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Tests.Tools;

[TestFixture]
public class AIFunctionWrappingTests
{
    private ToolExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new ToolExecutionContext("/tmp/instance", "inst-001", "step-1", "test-workflow");
    }

    /// <summary>
    /// Replicates the exact wrapping pattern from StepExecutor.cs:126-131.
    /// </summary>
    private AIFunction WrapTool(IWorkflowTool tool)
    {
        return AIFunctionFactory.Create(
            async (IDictionary<string, object?> arguments) =>
                await tool.ExecuteAsync(arguments, _context, CancellationToken.None),
            tool.Name,
            tool.Description);
    }

    [Test]
    public void Wrapper_HasCorrectNameAndDescription()
    {
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads a file from the instance folder");

        var aiFunction = WrapTool(tool);

        Assert.That(aiFunction.Name, Is.EqualTo("read_file"));
        Assert.That(aiFunction.Description, Is.EqualTo("Reads a file from the instance folder"));
    }

    [Test]
    public async Task Wrapper_CallsThroughToExecuteAsync()
    {
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads a file");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(), Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns("file contents here");

        var aiFunction = WrapTool(tool);

        // Invoke the AIFunction — the factory marshals by parameter name "arguments"
        var toolArgs = new Dictionary<string, object?> { ["path"] = "test.txt" };
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["arguments"] = toolArgs });
        var result = await aiFunction.InvokeAsync(args, CancellationToken.None);

        Assert.That(result?.ToString(), Is.EqualTo("file contents here"));

        await tool.Received(1).ExecuteAsync(
            Arg.Is<IDictionary<string, object?>>(a => a.ContainsKey("path")),
            Arg.Is<ToolExecutionContext>(c => c.StepId == "step-1"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void MultipleTools_ProduceMultipleWrappers()
    {
        var tool1 = Substitute.For<IWorkflowTool>();
        tool1.Name.Returns("read_file");
        tool1.Description.Returns("Reads");

        var tool2 = Substitute.For<IWorkflowTool>();
        tool2.Name.Returns("write_file");
        tool2.Description.Returns("Writes");

        var tool3 = Substitute.For<IWorkflowTool>();
        tool3.Name.Returns("list_files");
        tool3.Description.Returns("Lists");

        var wrappers = new[] { tool1, tool2, tool3 }.Select(WrapTool).ToList();

        Assert.That(wrappers, Has.Count.EqualTo(3));
        Assert.That(wrappers.Select(w => w.Name), Is.EquivalentTo(new[] { "read_file", "write_file", "list_files" }));
    }
}
