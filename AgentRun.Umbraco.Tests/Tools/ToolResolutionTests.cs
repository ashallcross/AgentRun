using NSubstitute;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ToolResolutionTests
{
    private IWorkflowTool _readFile = null!;
    private IWorkflowTool _writeFile = null!;
    private IWorkflowTool _listFiles = null!;
    private List<IWorkflowTool> _allTools = null!;

    [SetUp]
    public void SetUp()
    {
        _readFile = Substitute.For<IWorkflowTool>();
        _readFile.Name.Returns("read_file");
        _readFile.Description.Returns("Reads a file");

        _writeFile = Substitute.For<IWorkflowTool>();
        _writeFile.Name.Returns("write_file");
        _writeFile.Description.Returns("Writes a file");

        _listFiles = Substitute.For<IWorkflowTool>();
        _listFiles.Name.Returns("list_files");
        _listFiles.Description.Returns("Lists files");

        _allTools = [_readFile, _writeFile, _listFiles];
    }

    /// <summary>
    /// Replicates the exact LINQ filtering from StepExecutor.cs:94-97.
    /// </summary>
    private static Dictionary<string, IWorkflowTool> FilterTools(
        IEnumerable<IWorkflowTool> allTools, List<string>? declaredToolNames)
    {
        var names = declaredToolNames ?? [];
        return allTools
            .Where(t => names.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void ExactName_FindsRegisteredTool()
    {
        var result = FilterTools(_allTools, ["read_file"]);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.ContainsKey("read_file"), Is.True);
    }

    [Test]
    public void CaseInsensitiveName_FindsRegisteredTool()
    {
        var result = FilterTools(_allTools, ["READ_FILE"]);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.ContainsKey("read_file"), Is.True);
    }

    [Test]
    public void UnregisteredName_ReturnsEmpty()
    {
        var result = FilterTools(_allTools, ["unknown_tool"]);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SingleDeclaredTool_FiltersFromMultipleRegistered()
    {
        var result = FilterTools(_allTools, ["read_file"]);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.ContainsKey("read_file"), Is.True);
        Assert.That(result.ContainsKey("write_file"), Is.False);
        Assert.That(result.ContainsKey("list_files"), Is.False);
    }

    [Test]
    public void NullToolsList_ReturnsEmpty()
    {
        var result = FilterTools(_allTools, null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EmptyToolsList_ReturnsEmpty()
    {
        var result = FilterTools(_allTools, []);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void DeclaredToolsNotInRegistry_ReturnsEmpty()
    {
        var result = FilterTools(_allTools, ["fetch_url", "execute_code"]);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void NoDIToolsRegistered_StepDeclaresTools_ReturnsEmpty()
    {
        var emptyTools = Enumerable.Empty<IWorkflowTool>();

        var result = FilterTools(emptyTools, ["read_file", "write_file"]);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void DuplicateToolNames_ThrowsArgumentException()
    {
        // Failure & Edge Case #1: two tools with same Name causes ToDictionary to throw
        var duplicate = Substitute.For<IWorkflowTool>();
        duplicate.Name.Returns("read_file");
        duplicate.Description.Returns("Duplicate reader");

        var toolsWithDuplicate = new List<IWorkflowTool> { _readFile, duplicate };

        Assert.Throws<ArgumentException>(() => FilterTools(toolsWithDuplicate, ["read_file"]));
    }

    [Test]
    public void NullToolName_NeverMatchesDeclaredTools()
    {
        // Failure & Edge Case #2: tool with null Name won't match any declared name
        var nullNameTool = Substitute.For<IWorkflowTool>();
        nullNameTool.Name.Returns((string)null!);
        nullNameTool.Description.Returns("Null name tool");

        var tools = new List<IWorkflowTool> { nullNameTool };

        var result = FilterTools(tools, ["read_file"]);
        Assert.That(result, Is.Empty);
    }
}
