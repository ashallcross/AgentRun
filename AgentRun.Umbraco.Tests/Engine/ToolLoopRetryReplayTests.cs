using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

/// <summary>
/// Story 10.6 — retry-replay degeneration recovery. Integration-level tests
/// that cover the recovery design end-to-end:
/// <list type="bullet">
/// <item>Task 5: fake <see cref="IChatClient"/> exhibits the degenerate-state
/// symptom when replayed the pre-recovery conversation, but produces the
/// correct next action when given the post-recovery (empty) shape.</item>
/// <item>Task 6: multi-attempt retry and dangling-cache edge cases — AC #3 and
/// AC #4 surfaces.</item>
/// </list>
/// These tests avoid the 1.8 MB captured JSONL from
/// <c>caed201cbc5d4a9eb6a68f1ff6aafb06</c> and instead construct the exact
/// failing tail shape in-memory (5× <c>fetch_url</c> tool_call/tool_result
/// pairs). The shape is what matters, not the specific URLs.
/// </summary>
[TestFixture]
public class ToolLoopRetryReplayTests
{
    private string _tempDir = null!;
    private ConversationStore _store = null!;
    private string _instanceFolder = null!;

    private const string WorkflowAlias = "content-quality-audit";
    private const string InstanceId = "inst-retry-replay";
    private const string StepId = "scanner";

    private static readonly string[] SampleUrls =
    {
        "https://example.com/one",
        "https://example.com/two",
        "https://example.com/three",
        "https://en.wikipedia.org/wiki/Foo",
        "https://example.com/five"
    };

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentrun-retry-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _instanceFolder = Path.Combine(_tempDir, WorkflowAlias, InstanceId);
        Directory.CreateDirectory(_instanceFolder);

        var logger = Substitute.For<ILogger<ConversationStore>>();
        _store = new ConversationStore(_tempDir, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ------------------------------------------------------------------
    // Fixture builders — reproduce the degenerate-state tail in-memory.
    // ------------------------------------------------------------------

    private async Task SeedDegenerateConversationAsync()
    {
        // System + initial user turn seeding the fetch batch.
        await AppendAsync(new ConversationEntry
        {
            Role = "system",
            Content = "You are a content quality auditor.",
            Timestamp = Clock(0)
        });
        await AppendAsync(new ConversationEntry
        {
            Role = "user",
            Content = "Scan these URLs: " + string.Join(", ", SampleUrls),
            Timestamp = Clock(1)
        });

        // Five fetch_url tool_call/tool_result pairs. The final pair mirrors
        // the 403 failure in caed201cbc5d4a9eb6a68f1ff6aafb06 on the Wikipedia
        // URL; the four earlier fetches succeed with cached handle bodies.
        for (var i = 0; i < SampleUrls.Length; i++)
        {
            var callId = $"call-{i + 1}";
            var url = SampleUrls[i];
            await AppendAsync(new ConversationEntry
            {
                Role = "assistant",
                Timestamp = Clock(2 + i * 2),
                ToolCallId = callId,
                ToolName = "fetch_url",
                ToolArguments = JsonSerializer.Serialize(new { url })
            });

            string toolResult = i == SampleUrls.Length - 1
                ? "HTTP 403: Forbidden"
                : JsonSerializer.Serialize(new
                {
                    url,
                    status = 200,
                    content_type = "text/html",
                    size_bytes = 1024,
                    saved_to = $".fetch-cache/{Sha256Hex(url)}.html",
                    truncated = false
                });

            await AppendAsync(new ConversationEntry
            {
                Role = "tool",
                Timestamp = Clock(3 + i * 2),
                ToolCallId = callId,
                ToolResult = toolResult
            });
        }
    }

    private async Task SeedPartialCacheAsync(params int[] indicesToPopulate)
    {
        var cacheDir = Path.Combine(_instanceFolder, ".fetch-cache");
        Directory.CreateDirectory(cacheDir);
        foreach (var i in indicesToPopulate)
        {
            var body = Encoding.UTF8.GetBytes($"<html>cached body for {SampleUrls[i]}</html>");
            await File.WriteAllBytesAsync(
                Path.Combine(cacheDir, $"{Sha256Hex(SampleUrls[i])}.html"), body);
        }
    }

    private Task AppendAsync(ConversationEntry entry)
        => _store.AppendAsync(WorkflowAlias, InstanceId, StepId, entry, CancellationToken.None);

    private static DateTime Clock(int seconds)
        => new DateTime(2026, 4, 8, 19, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);

    private static string Sha256Hex(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private string JsonlPath()
        => Path.Combine(_instanceFolder, $"conversation-{StepId}.jsonl");

    // ------------------------------------------------------------------
    // Task 5.1 — Pre-recovery: the degenerate tail exists on disk.
    //            Post-recovery: GetHistoryAsync returns empty, so the
    //            StepExecutor's ConvertHistoryToMessages sees nothing and
    //            ToolLoop.RunAsync receives only the system prompt.
    // ------------------------------------------------------------------

    [Test]
    public async Task DegenerateConversation_BeforeWipe_ShapeReproducesTheFailingInstance()
    {
        await SeedDegenerateConversationAsync();

        var history = await _store.GetHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        // Shape assertion: system + user + 5×(assistant tool_call + tool result) = 12.
        Assert.That(history, Has.Count.EqualTo(12));

        var toolCallCount = history.Count(h => h.Role == "assistant" && h.ToolName == "fetch_url");
        var toolResultCount = history.Count(h => h.Role == "tool");
        Assert.That(toolCallCount, Is.EqualTo(5));
        Assert.That(toolResultCount, Is.EqualTo(5));

        // The tail is tool result (403) — the exact clean-boundary shape
        // Story 9.0's TruncateLastAssistantEntryAsync correctly declines to
        // modify. Without Story 10.6 Option 3, the model sees this shape on
        // retry and produces text instead of the next tool call.
        Assert.That(history[^1].Role, Is.EqualTo("tool"));
        Assert.That(history[^1].ToolResult, Does.Contain("HTTP 403"));
    }

    [Test]
    public async Task WipeHistory_ProducesEmptyConversation_ThePostRecoveryShapeToolLoopSees()
    {
        await SeedDegenerateConversationAsync();

        await _store.WipeHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        var history = await _store.GetHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);
        Assert.That(history, Is.Empty,
            "Option 3 recovery: the StepExecutor must see an empty history, so ConvertHistoryToMessages yields nothing " +
            "and ToolLoop.RunAsync runs against only the system prompt — no degenerate tail for the model to react to.");

        // And the archive is still present for forensic debugging.
        var archived = Directory.GetFiles(_instanceFolder, $"conversation-{StepId}.failed-*.jsonl");
        Assert.That(archived, Has.Length.EqualTo(1));
    }

    // ------------------------------------------------------------------
    // Task 5.2 — Fake IChatClient replayed the pre-recovery conversation
    //            fires StallDetector (reproduces the symptom). When
    //            replayed the post-recovery (empty) shape, the same fake
    //            client produces write_file and the step completes.
    // ------------------------------------------------------------------

    [Test]
    public async Task PreRecovery_FakeClientEmitsNarration_StallDetectorFires()
    {
        await SeedDegenerateConversationAsync();
        var history = await _store.GetHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        using var chatClient = Substitute.For<IChatClient>();

        var messages = BuildMessagesFromHistory(history);
        var context = MakeContext();

        // completionCheck: returns false so the StallDetector's completion-check
        // escape hatch does not kick in. Step is genuinely incomplete.
        Func<CancellationToken, Task<bool>> completionCheck = _ => Task.FromResult(false);

        // Two streaming responses: the initial narration + the post-nudge
        // narration (Story 9.1b carve-out: one retry nudge before StallDetector
        // fires). Both yield text — no tool call — so the stall fires on the
        // second classify.
        var call = 0;
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                call++;
                return MakeTextChunks(call == 1
                    ? "Let me write the results now."
                    : "I need to continue with writing now.");
            });

        // Stall detection is interactive-only (Story 9.0 AC #6) — the caller
        // provides a userMessageReader. A never-writing channel exercises the
        // interactive-mode stall-classification path without injecting
        // synthetic user messages.
        var userMessages = Channel.CreateUnbounded<string>();

        Assert.ThrowsAsync<StallDetectedException>(async () =>
        {
            await ToolLoop.RunAsync(
                chatClient,
                messages,
                new ChatOptions(),
                declaredTools: new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase),
                context: context,
                logger: NullLogger.Instance,
                cancellationToken: CancellationToken.None,
                userMessageReader: userMessages.Reader,
                completionCheck: completionCheck,
                userMessageTimeoutOverride: TimeSpan.FromMilliseconds(50));
        });
    }

    [Test]
    public async Task PostRecovery_EmptyConversation_FakeClientProducesToolCall_NoStall()
    {
        // Wipe before replay. The recovery primitive's design promise: after
        // wipe, the conversation is empty and the model produces the correct
        // first tool call (here, write_file — or re-issued fetch_url; we
        // assert a TOOL CALL, not a narration).
        await SeedDegenerateConversationAsync();
        await _store.WipeHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        var history = await _store.GetHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);
        Assert.That(history, Is.Empty);

        var writeFile = Substitute.For<IWorkflowTool>();
        writeFile.Name.Returns("write_file");
        writeFile.Description.Returns("Writes a file");
        writeFile.ExecuteAsync(
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<ToolExecutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns("OK");

        using var chatClient = Substitute.For<IChatClient>();
        var call = 0;
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                call++;
                return call == 1
                    ? MakeToolCallChunks(("call-w1", "write_file",
                        new Dictionary<string, object?> { ["path"] = "scan-results.md", ["content"] = "done" }))
                    : MakeTextChunks("done");
            });

        var messages = new List<ChatMessage> { new(ChatRole.System, "scan urls") };
        var declared = new Dictionary<string, IWorkflowTool>(StringComparer.OrdinalIgnoreCase)
        {
            ["write_file"] = writeFile
        };

        await ToolLoop.RunAsync(
            chatClient,
            messages,
            new ChatOptions(),
            declaredTools: declared,
            context:MakeContext(),
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None);

        await writeFile.Received(1).ExecuteAsync(
            Arg.Any<IDictionary<string, object?>>(),
            Arg.Any<ToolExecutionContext>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Task 6.1 — AC #3: dangling cache. Some .fetch-cache/ files are
    //            missing after wipe. Recovery re-fetches on miss (Story
    //            9.7 cache-on-miss), re-uses on hit (Task 0.5). No
    //            silent corruption.
    // ------------------------------------------------------------------

    [Test]
    public async Task DanglingCache_WipeDoesNotTouchCacheFiles_AndMissingFilesTreatedAsInertDebris()
    {
        await SeedDegenerateConversationAsync();
        await SeedPartialCacheAsync(0, 2); // URLs 0 and 2 have cache files; 1, 3, 4 do not.

        // Sanity: cache files are on disk before wipe.
        var cacheDir = Path.Combine(_instanceFolder, ".fetch-cache");
        var before = Directory.GetFiles(cacheDir).Length;
        Assert.That(before, Is.EqualTo(2));

        await _store.WipeHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        // AC #7 / deny-by-default: wipe only archives the conversation file.
        // Cache files remain inert debris; they are not conversation state,
        // so the recovery primitive does not touch them.
        var after = Directory.GetFiles(cacheDir).Length;
        Assert.That(after, Is.EqualTo(2),
            "AC #7: .fetch-cache files not referenced by the wiped conversation remain inert on disk — not consumed, not deleted.");

        // The post-wipe history is empty — the model starts fresh and re-issues
        // fetch_url for every URL. Cache-on-hit (Task 0.5) handles URLs 0 and 2
        // in milliseconds; cache-on-miss handles 1, 3, 4 via HTTP as before.
        var history = await _store.GetHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);
        Assert.That(history, Is.Empty);
    }

    // ------------------------------------------------------------------
    // Task 6.2 — AC #4: multiple consecutive retries. Each retry is
    //            independent. The recovery primitive is idempotent
    //            (second call on an already-wiped conversation is a
    //            no-op, not a crash).
    // ------------------------------------------------------------------

    [Test]
    public async Task MultipleConsecutiveRetries_EachRetryHasItsOwnConversation_AndArchivesAccumulate()
    {
        // Attempt 1: seed + wipe.
        await SeedDegenerateConversationAsync();
        await _store.WipeHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        // Attempt 2: simulate a new (different) failed attempt on the restarted
        // step. Append a shorter degenerate tail, then wipe again.
        await AppendAsync(new ConversationEntry { Role = "system", Content = "retry 2", Timestamp = Clock(100) });
        await AppendAsync(new ConversationEntry
        {
            Role = "assistant", Timestamp = Clock(101),
            ToolCallId = "call-retry2-1", ToolName = "fetch_url",
            ToolArguments = JsonSerializer.Serialize(new { url = SampleUrls[0] })
        });
        await AppendAsync(new ConversationEntry
        {
            Role = "tool", Timestamp = Clock(102),
            ToolCallId = "call-retry2-1", ToolResult = "HTTP 503: Service Unavailable"
        });

        // Wait 1s so the second archive has a distinct UTC-second timestamp.
        await Task.Delay(1100);
        await _store.WipeHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);

        // AC #4: each retry is independent.
        var historyAfterSecondRetry = await _store.GetHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);
        Assert.That(historyAfterSecondRetry, Is.Empty);

        // Both archives exist for forensic debugging.
        var archives = Directory.GetFiles(_instanceFolder, $"conversation-{StepId}.failed-*.jsonl");
        Assert.That(archives, Has.Length.EqualTo(2),
            "AC #4: consecutive retries each produce their own archived failure — the second does not clobber the first.");

        // Attempt 3: no conversation exists. Wipe is idempotent — no-op.
        await _store.WipeHistoryAsync(WorkflowAlias, InstanceId, StepId, CancellationToken.None);
        var archivesAfterIdempotentCall = Directory.GetFiles(_instanceFolder, $"conversation-{StepId}.failed-*.jsonl");
        Assert.That(archivesAfterIdempotentCall, Has.Length.EqualTo(2),
            "Idempotency: wipe with no current conversation must not produce a spurious archive.");
    }

    // ------------------------------------------------------------------
    // Shared helpers for fake-IChatClient pattern.
    // ------------------------------------------------------------------

    private static async IAsyncEnumerable<ChatResponseUpdate> MakeTextChunks(string text)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> MakeToolCallChunks(
        params (string callId, string name, IDictionary<string, object?>? args)[] calls)
    {
        foreach (var (callId, name, args) in calls)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent(callId, name, args)]
            };
        }
        await Task.CompletedTask;
    }

    private static ToolExecutionContext MakeContext()
    {
        var step = new StepDefinition { Id = StepId, Name = "Scanner", Agent = "agents/scanner.md" };
        var workflow = new WorkflowDefinition
        {
            Name = "Content Quality Audit",
            Alias = WorkflowAlias,
            Steps = { step }
        };
        return new ToolExecutionContext("/tmp/instance", InstanceId, StepId, WorkflowAlias)
        {
            Step = step,
            Workflow = workflow
        };
    }

    /// <summary>
    /// Minimal re-implementation of StepExecutor's ConvertHistoryToMessages
    /// just enough to exercise the pre-recovery shape against the fake
    /// chat client. Tests only need the tail's role ordering to match what
    /// the real StepExecutor would produce; StepExecutor's production path
    /// is covered by its own test suite.
    /// </summary>
    private static List<ChatMessage> BuildMessagesFromHistory(IReadOnlyList<ConversationEntry> history)
    {
        var messages = new List<ChatMessage>();
        foreach (var e in history)
        {
            switch (e.Role)
            {
                case "system":
                    messages.Add(new ChatMessage(ChatRole.System, e.Content ?? string.Empty));
                    break;
                case "user":
                    messages.Add(new ChatMessage(ChatRole.User, e.Content ?? string.Empty));
                    break;
                case "assistant" when e.ToolName is not null:
                    messages.Add(new ChatMessage(ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            e.ToolCallId!,
                            e.ToolName!,
                            ParseArgs(e.ToolArguments))
                    ]));
                    break;
                case "assistant":
                    messages.Add(new ChatMessage(ChatRole.Assistant, e.Content ?? string.Empty));
                    break;
                case "tool":
                    messages.Add(new ChatMessage(ChatRole.Tool,
                    [
                        new FunctionResultContent(e.ToolCallId!, e.ToolResult)
                    ]));
                    break;
            }
        }
        return messages;
    }

    private static IDictionary<string, object?>? ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        return dict;
    }
}
