using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class ToolLoopCompactionTests
{
    private static readonly ILogger Logger = NullLogger.Instance;

    private static readonly ToolExecutionContext TestContext =
        new("/tmp/test", "inst-001", "step-1", "test-workflow");

    /// <summary>
    /// Build a message list simulating a multi-turn tool loop conversation.
    /// Returns the toolResultTurnMap populated at construction time.
    /// </summary>
    private static (IList<ChatMessage> messages, Dictionary<string, int> turnMap)
        BuildConversation(int toolCallCount, int assistantTurnsAfterTools)
    {
        var messages = new List<ChatMessage>();
        var turnMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var assistantTurn = 0;

        for (var i = 0; i < toolCallCount; i++)
        {
            var callId = $"call_{i}";

            // Assistant message with a function call
            var assistantMsg = new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(callId, $"tool_{i}", new Dictionary<string, object?> { ["arg"] = "val" })]);
            messages.Add(assistantMsg);
            assistantTurn++;

            // Tool result
            var resultContent = new string('x', 2000 + i * 500); // varying sizes, all above CompactionMinSizeBytes
            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent(callId, resultContent)]));
            turnMap[callId] = assistantTurn;
        }

        // Add additional assistant turns (text-only responses)
        for (var i = 0; i < assistantTurnsAfterTools; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, $"Thinking about turn {i}..."));
            assistantTurn++;
        }

        return (messages, turnMap);
    }

    [Test]
    public void ThresholdBehaviour_OldResultsCompacted_NewResultsPreserved()
    {
        // 3 tool calls, then 4 assistant turns → first tool result is 4 turns old,
        // second is 3, third is 2. With threshold=3, first and second should be compacted.
        var (messages, turnMap) = BuildConversation(toolCallCount: 3, assistantTurnsAfterTools: 4);
        var compacted = new HashSet<string>(StringComparer.Ordinal);
        var currentTurn = 3 + 4; // 3 tool-call turns + 4 text turns

        // Add one more tool call as "most recent batch" that should be immune
        var recentCallId = "call_recent";
        messages.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(recentCallId, "tool_recent")]));
        messages.Add(new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(recentCallId, "recent result data")]));
        turnMap[recentCallId] = currentTurn;

        ToolLoop.CompactOldToolResults(messages, currentTurn, threshold: 3, compacted, turnMap, Logger, TestContext);

        // call_0 age = 7-1 = 6 → compacted
        Assert.That(compacted.Contains("call_0"), Is.True, "call_0 should be compacted (age 6 >= threshold 3)");
        // call_1 age = 7-2 = 5 → compacted
        Assert.That(compacted.Contains("call_1"), Is.True, "call_1 should be compacted (age 5 >= threshold 3)");
        // call_2 age = 7-3 = 4 → compacted
        Assert.That(compacted.Contains("call_2"), Is.True, "call_2 should be compacted (age 4 >= threshold 3)");
        // call_recent is in the most recent tool message → immune
        Assert.That(compacted.Contains("call_recent"), Is.False, "recent batch should be immune");

        // Verify the recent result content is unchanged
        var lastToolMsg = messages.Last(m => m.Role == ChatRole.Tool);
        var recentResult = lastToolMsg.Contents.OfType<FunctionResultContent>().First();
        Assert.That(recentResult.Result?.ToString(), Is.EqualTo("recent result data"));
    }

    [Test]
    public void PlaceholderContent_ContainsSizeAndToolName()
    {
        var (messages, turnMap) = BuildConversation(toolCallCount: 1, assistantTurnsAfterTools: 5);
        var compacted = new HashSet<string>(StringComparer.Ordinal);
        var currentTurn = 6;

        // Add a dummy most-recent tool message so call_0 is not in the last batch
        messages.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_dummy", "dummy")]));
        messages.Add(new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent("call_dummy", "dummy")]));
        turnMap["call_dummy"] = currentTurn;

        ToolLoop.CompactOldToolResults(messages, currentTurn, threshold: 3, compacted, turnMap, Logger, TestContext);

        // Find the compacted result
        var toolMsg = messages.First(m => m.Role == ChatRole.Tool);
        var frc = toolMsg.Contents.OfType<FunctionResultContent>().First();
        var placeholder = frc.Result?.ToString()!;

        Assert.That(placeholder, Does.StartWith(ToolLoop.CompactionPlaceholderPrefix));
        Assert.That(placeholder, Does.Contain("tool_0")); // tool name
        Assert.That(placeholder, Does.Contain("bytes")); // size mention
        Assert.That(placeholder, Does.Contain("conversation log")); // recovery pointer
    }

    [Test]
    public void MessageContract_NoOrphanedToolCalls()
    {
        var (messages, turnMap) = BuildConversation(toolCallCount: 5, assistantTurnsAfterTools: 10);
        var compacted = new HashSet<string>(StringComparer.Ordinal);
        var currentTurn = 15;

        // Add dummy recent batch
        messages.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_final", "final")]));
        messages.Add(new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent("call_final", "final result")]));
        turnMap["call_final"] = currentTurn;

        ToolLoop.CompactOldToolResults(messages, currentTurn, threshold: 3, compacted, turnMap, Logger, TestContext);

        // Collect all FunctionCallContent CallIds
        var callIds = messages
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(fcc => fcc.CallId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        // Collect all FunctionResultContent CallIds
        var resultIds = messages
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Select(frc => frc.CallId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        // Every call must have a matching result (no orphans)
        foreach (var callId in callIds)
        {
            Assert.That(resultIds.Contains(callId!), Is.True,
                $"FunctionCallContent {callId} has no matching FunctionResultContent after compaction");
        }
    }

    [Test]
    public void UserAndSystemMessages_NeverModified()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Please audit this content."),
            new(ChatRole.Assistant, [new FunctionCallContent("call_0", "get_content", new Dictionary<string, object?> { ["id"] = 1 })]),
            new(ChatRole.Tool, [new FunctionResultContent("call_0", new string('x', 5000))]),
            new(ChatRole.Assistant, "Processing..."),
            new(ChatRole.Assistant, "More processing..."),
            new(ChatRole.Assistant, "Still going..."),
            new(ChatRole.User, "How's it going?"),
            // Recent batch
            new(ChatRole.Assistant, [new FunctionCallContent("call_1", "write_file")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "OK")])
        };

        var turnMap = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["call_0"] = 1,
            ["call_1"] = 5
        };
        var compacted = new HashSet<string>(StringComparer.Ordinal);

        ToolLoop.CompactOldToolResults(messages, currentAssistantTurn: 5, threshold: 2, compacted, turnMap, Logger, TestContext);

        // System message untouched
        Assert.That(messages[0].Text, Is.EqualTo("You are a helpful assistant."));
        // User messages untouched
        Assert.That(messages[1].Text, Is.EqualTo("Please audit this content."));
        Assert.That(messages[7].Text, Is.EqualTo("How's it going?"));
        // Assistant text messages untouched
        Assert.That(messages[4].Text, Is.EqualTo("Processing..."));
    }

    [Test]
    public void MostRecentBatch_NeverCompacted()
    {
        // Only one tool call — it IS the most recent batch
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("call_0", "get_content")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_0", new string('y', 10000))])
        };
        var turnMap = new Dictionary<string, int>(StringComparer.Ordinal) { ["call_0"] = 0 };
        var compacted = new HashSet<string>(StringComparer.Ordinal);

        ToolLoop.CompactOldToolResults(messages, currentAssistantTurn: 100, threshold: 1, compacted, turnMap, Logger, TestContext);

        // Even though age (100) > threshold (1), this is the most recent batch
        Assert.That(compacted, Is.Empty);
        var frc = messages[1].Contents.OfType<FunctionResultContent>().First();
        Assert.That(frc.Result?.ToString()!.Length, Is.EqualTo(10000));
    }

    [Test]
    public void CustomThreshold_RespectedCorrectly()
    {
        var (messages, turnMap) = BuildConversation(toolCallCount: 2, assistantTurnsAfterTools: 6);
        var compacted = new HashSet<string>(StringComparer.Ordinal);
        var currentTurn = 8;

        // Add recent batch
        messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_latest", "latest")]));
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_latest", "latest")]));
        turnMap["call_latest"] = currentTurn;

        // With threshold=5: call_0 age=7, call_1 age=6 → both compacted
        ToolLoop.CompactOldToolResults(messages, currentTurn, threshold: 5, compacted, turnMap, Logger, TestContext);
        Assert.That(compacted.Contains("call_0"), Is.True, "call_0 age 7 >= threshold 5");
        Assert.That(compacted.Contains("call_1"), Is.True, "call_1 age 6 >= threshold 5");

        // Reset and use threshold=8: call_0 age=7, call_1 age=6 → neither compacted
        compacted.Clear();
        // Need fresh messages since results are already compacted
        var (messages2, turnMap2) = BuildConversation(toolCallCount: 2, assistantTurnsAfterTools: 6);
        messages2.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_latest2", "latest")]));
        messages2.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_latest2", "latest")]));
        turnMap2["call_latest2"] = currentTurn;

        ToolLoop.CompactOldToolResults(messages2, currentTurn, threshold: 8, compacted, turnMap2, Logger, TestContext);
        Assert.That(compacted, Is.Empty, "No results old enough for threshold=8");
    }

    [Test]
    public void AlreadyCompacted_SkippedOnSubsequentCalls()
    {
        var (messages, turnMap) = BuildConversation(toolCallCount: 1, assistantTurnsAfterTools: 5);
        var compacted = new HashSet<string>(StringComparer.Ordinal);
        var currentTurn = 6;

        // Add recent batch
        messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_recent", "recent")]));
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_recent", "recent")]));
        turnMap["call_recent"] = currentTurn;

        // First compaction
        ToolLoop.CompactOldToolResults(messages, currentTurn, threshold: 3, compacted, turnMap, Logger, TestContext);
        Assert.That(compacted.Contains("call_0"), Is.True);

        // Get the placeholder
        var toolMsg = messages.First(m => m.Role == ChatRole.Tool);
        var frc = toolMsg.Contents.OfType<FunctionResultContent>().First();
        var placeholderAfterFirst = frc.Result?.ToString();

        // Second compaction — should be a no-op for call_0
        ToolLoop.CompactOldToolResults(messages, currentTurn + 5, threshold: 3, compacted, turnMap, Logger, TestContext);
        var placeholderAfterSecond = frc.Result?.ToString();

        Assert.That(placeholderAfterSecond, Is.EqualTo(placeholderAfterFirst),
            "Already-compacted results should not be re-processed");
    }

    [Test]
    public void SmallResults_NeverCompacted()
    {
        // Simulate a small tool result (like an offloaded handle — under 1 KB)
        var smallResult = """{"id":1120,"name":"Home","contentType":"home","url":"/","propertyCount":5,"size_bytes":4200,"saved_to":".content-cache/1120.json","truncated":false}""";
        Assert.That(System.Text.Encoding.UTF8.GetByteCount(smallResult), Is.LessThanOrEqualTo(EngineDefaults.CompactionMinSizeBytes),
            "Test precondition: result must be at or below the compaction min size threshold");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("call_small", "get_content")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_small", smallResult)]),
            new(ChatRole.Assistant, "Processing..."),
            new(ChatRole.Assistant, "More processing..."),
            new(ChatRole.Assistant, "Still processing..."),
            new(ChatRole.Assistant, "Done thinking."),
            // Recent batch so call_small is not the last tool message
            new(ChatRole.Assistant, [new FunctionCallContent("call_other", "write_file")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_other", "OK")])
        };
        var turnMap = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["call_small"] = 1,
            ["call_other"] = 5
        };
        var compacted = new HashSet<string>(StringComparer.Ordinal);

        ToolLoop.CompactOldToolResults(messages, currentAssistantTurn: 5, threshold: 1, compacted, turnMap, Logger, TestContext);

        // Small result should NOT be compacted even though age (4) > threshold (1)
        Assert.That(compacted.Contains("call_small"), Is.False,
            "Small results (handles) should never be compacted");
        var frc = messages[1].Contents.OfType<FunctionResultContent>().First();
        Assert.That(frc.Result?.ToString(), Is.EqualTo(smallResult),
            "Handle content must be preserved");
    }

    [Test]
    public void LargeResults_StillCompacted()
    {
        // A large result (over 1 KB) should still be compacted normally
        var largeResult = new string('x', 2000);
        Assert.That(System.Text.Encoding.UTF8.GetByteCount(largeResult), Is.GreaterThan(EngineDefaults.CompactionMinSizeBytes),
            "Test precondition: result must exceed the compaction min size threshold");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("call_large", "get_content")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_large", largeResult)]),
            new(ChatRole.Assistant, "Processing..."),
            new(ChatRole.Assistant, "More processing..."),
            new(ChatRole.Assistant, "Done."),
            // Recent batch
            new(ChatRole.Assistant, [new FunctionCallContent("call_other", "write_file")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_other", "OK")])
        };
        var turnMap = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["call_large"] = 1,
            ["call_other"] = 4
        };
        var compacted = new HashSet<string>(StringComparer.Ordinal);

        ToolLoop.CompactOldToolResults(messages, currentAssistantTurn: 4, threshold: 2, compacted, turnMap, Logger, TestContext);

        Assert.That(compacted.Contains("call_large"), Is.True,
            "Large results should still be compacted");
    }
}
