using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using AgentRun.Umbraco.Instances;

namespace AgentRun.Umbraco.Tests.Instances;

[TestFixture]
public class ConversationStoreTests
{
    private string _tempDir = null!;
    private ConversationStore _store = null!;

    private static ConversationEntry CreateTestEntry(
        string role = "assistant",
        string? content = "Hello",
        string? toolCallId = null,
        string? toolName = null,
        string? toolArguments = null,
        string? toolResult = null) => new()
    {
        Role = role,
        Content = content,
        Timestamp = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
        ToolCallId = toolCallId,
        ToolName = toolName,
        ToolArguments = toolArguments,
        ToolResult = toolResult
    };

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentrun-conv-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var logger = Substitute.For<ILogger<ConversationStore>>();
        _store = new ConversationStore(_tempDir, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task AppendAsync_CreatesJsonlFileAndWritesOneLine()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entry = CreateTestEntry();
        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry, CancellationToken.None);

        var filePath = Path.Combine(instanceDir, "conversation-step-one.jsonl");
        Assert.That(File.Exists(filePath), Is.True);

        var lines = await File.ReadAllLinesAsync(filePath);
        var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.That(nonEmpty, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task AppendAsync_MultipleEntries_ProducesMultipleLinesInOrder()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entry1 = CreateTestEntry(content: "First message");
        var entry2 = CreateTestEntry(content: "Second message");
        var entry3 = CreateTestEntry(content: "Third message");

        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry1, CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry2, CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry3, CancellationToken.None);

        var filePath = Path.Combine(instanceDir, "conversation-step-one.jsonl");
        var lines = (await File.ReadAllLinesAsync(filePath))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.That(lines, Has.Length.EqualTo(3));

        Assert.That(lines[0], Does.Contain("First message"));
        Assert.That(lines[1], Does.Contain("Second message"));
        Assert.That(lines[2], Does.Contain("Third message"));
    }

    [Test]
    public async Task GetHistoryAsync_ReturnsEntriesInChronologicalOrder()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entry1 = CreateTestEntry(content: "First");
        var entry2 = CreateTestEntry(content: "Second");

        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry1, CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry2, CancellationToken.None);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Content, Is.EqualTo("First"));
        Assert.That(history[1].Content, Is.EqualTo("Second"));
    }

    [Test]
    public async Task GetHistoryAsync_NonExistentFile_ReturnsEmptyList()
    {
        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "no-step", CancellationToken.None);

        Assert.That(history, Is.Empty);
    }

    [Test]
    public async Task GetHistoryAsync_SkipsEmptyAndWhitespaceLines()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var filePath = Path.Combine(instanceDir, "conversation-step-one.jsonl");
        var content = """
                      {"role":"assistant","content":"Valid line","timestamp":"2026-04-01T10:00:00Z"}


                      {"role":"assistant","content":"Another valid","timestamp":"2026-04-01T10:00:01Z"}

                      """;
        await File.WriteAllTextAsync(filePath, content);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Content, Is.EqualTo("Valid line"));
        Assert.That(history[1].Content, Is.EqualTo("Another valid"));
    }

    [Test]
    public async Task GetHistoryAsync_SkipsCorruptedLines()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var filePath = Path.Combine(instanceDir, "conversation-step-one.jsonl");
        var content = """
                      {"role":"assistant","content":"Before crash","timestamp":"2026-04-01T10:00:00Z"}
                      {"role":"assistant","con
                      {"role":"assistant","content":"After crash","timestamp":"2026-04-01T10:00:02Z"}
                      """;
        await File.WriteAllTextAsync(filePath, content);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Content, Is.EqualTo("Before crash"));
        Assert.That(history[1].Content, Is.EqualTo("After crash"));
    }

    [Test]
    public async Task PerStepIsolation_DifferentStepIds_ProduceDifferentFiles()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entryA = CreateTestEntry(content: "Step A message");
        var entryB = CreateTestEntry(content: "Step B message");

        await _store.AppendAsync("test-workflow", "inst-001", "step-a", entryA, CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-b", entryB, CancellationToken.None);

        var historyA = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-a", CancellationToken.None);
        var historyB = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-b", CancellationToken.None);

        Assert.That(historyA, Has.Count.EqualTo(1));
        Assert.That(historyA[0].Content, Is.EqualTo("Step A message"));
        Assert.That(historyB, Has.Count.EqualTo(1));
        Assert.That(historyB[0].Content, Is.EqualTo("Step B message"));

        Assert.That(File.Exists(Path.Combine(instanceDir, "conversation-step-a.jsonl")), Is.True);
        Assert.That(File.Exists(Path.Combine(instanceDir, "conversation-step-b.jsonl")), Is.True);
    }

    [Test]
    public async Task ConversationEntry_SerialisesWithCamelCasePropertyNames()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entry = CreateTestEntry(
            role: "assistant",
            toolCallId: "call_123",
            toolName: "read_file",
            toolArguments: "{\"path\":\"test.md\"}",
            toolResult: "file contents");

        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry, CancellationToken.None);

        var filePath = Path.Combine(instanceDir, "conversation-step-one.jsonl");
        var line = (await File.ReadAllLinesAsync(filePath)).First(l => !string.IsNullOrWhiteSpace(l));

        Assert.That(line, Does.Contain("\"role\""));
        Assert.That(line, Does.Contain("\"content\""));
        Assert.That(line, Does.Contain("\"timestamp\""));
        Assert.That(line, Does.Contain("\"toolCallId\""));
        Assert.That(line, Does.Contain("\"toolName\""));
        Assert.That(line, Does.Contain("\"toolArguments\""));
        Assert.That(line, Does.Contain("\"toolResult\""));
        Assert.That(line, Does.Not.Contain("\"Role\""));
        Assert.That(line, Does.Not.Contain("\"ToolCallId\""));
    }

    [Test]
    public async Task ConversationEntry_NullOptionalFields_OmittedFromJson()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entry = CreateTestEntry(role: "system", content: "You are an auditor.");

        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry, CancellationToken.None);

        var filePath = Path.Combine(instanceDir, "conversation-step-one.jsonl");
        var line = (await File.ReadAllLinesAsync(filePath)).First(l => !string.IsNullOrWhiteSpace(l));

        Assert.That(line, Does.Not.Contain("toolCallId"));
        Assert.That(line, Does.Not.Contain("toolName"));
        Assert.That(line, Does.Not.Contain("toolArguments"));
        Assert.That(line, Does.Not.Contain("toolResult"));
    }

    [Test]
    public async Task TruncateLastAssistantEntryAsync_RemovesLastAssistantEntry_PreservesAllPrior()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "system", content: "System prompt"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", content: "First response"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "user", content: "User follow-up"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", content: "Failed response"), CancellationToken.None);

        await _store.TruncateLastAssistantEntryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);
        Assert.That(history, Has.Count.EqualTo(3));
        Assert.That(history[0].Role, Is.EqualTo("system"));
        Assert.That(history[1].Role, Is.EqualTo("assistant"));
        Assert.That(history[1].Content, Is.EqualTo("First response"));
        Assert.That(history[2].Role, Is.EqualTo("user"));
    }

    [Test]
    public async Task TruncateLastAssistantEntryAsync_NoAssistantEntries_IsNoOp()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "system", content: "System prompt"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "user", content: "Hello"), CancellationToken.None);

        await _store.TruncateLastAssistantEntryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);
        Assert.That(history, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task TruncateLastAssistantEntryAsync_NonExistentFile_IsNoOp()
    {
        // Should not throw
        await _store.TruncateLastAssistantEntryAsync("test-workflow", "inst-001", "no-step", CancellationToken.None);
    }

    [Test]
    public async Task TruncateLastAssistantEntryAsync_PreservesToolCallAssistantEntries()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", toolCallId: "tc_001", toolName: "read_file", toolArguments: "{\"path\":\"test.md\"}"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "tool", toolCallId: "tc_001", toolResult: "file contents"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", content: "Failed response"), CancellationToken.None);

        await _store.TruncateLastAssistantEntryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);
        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Role, Is.EqualTo("assistant"));
        Assert.That(history[0].ToolCallId, Is.EqualTo("tc_001"));
        Assert.That(history[1].Role, Is.EqualTo("tool"));
    }

    [Test]
    public async Task TruncateLastAssistantEntryAsync_MultipleAssistantEntries_RemovesOnlyLast()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", content: "First"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", content: "Second"), CancellationToken.None);
        await _store.AppendAsync("test-workflow", "inst-001", "step-one",
            CreateTestEntry(role: "assistant", content: "Third"), CancellationToken.None);

        await _store.TruncateLastAssistantEntryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);
        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[0].Content, Is.EqualTo("First"));
        Assert.That(history[1].Content, Is.EqualTo("Second"));
    }

    [Test]
    public async Task ToolCallEntry_RoundTripsCorrectly()
    {
        var instanceDir = Path.Combine(_tempDir, "test-workflow", "inst-001");
        Directory.CreateDirectory(instanceDir);

        var entry = CreateTestEntry(
            role: "assistant",
            content: null,
            toolCallId: "call_abc123",
            toolName: "read_file",
            toolArguments: "{\"path\":\"content.md\"}",
            toolResult: "# Page Title\nContent here...");

        await _store.AppendAsync("test-workflow", "inst-001", "step-one", entry, CancellationToken.None);

        var history = await _store.GetHistoryAsync("test-workflow", "inst-001", "step-one", CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(1));
        var roundTripped = history[0];
        Assert.That(roundTripped.Role, Is.EqualTo("assistant"));
        Assert.That(roundTripped.Content, Is.Null);
        Assert.That(roundTripped.ToolCallId, Is.EqualTo("call_abc123"));
        Assert.That(roundTripped.ToolName, Is.EqualTo("read_file"));
        Assert.That(roundTripped.ToolArguments, Is.EqualTo("{\"path\":\"content.md\"}"));
        Assert.That(roundTripped.ToolResult, Is.EqualTo("# Page Title\nContent here..."));
    }
}
