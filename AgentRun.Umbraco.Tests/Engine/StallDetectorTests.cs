using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class StallDetectorTests
{
    private static IReadOnlyList<FunctionCallContent> NoCalls => Array.Empty<FunctionCallContent>();

    private static IReadOnlyList<FunctionCallContent> WithCalls(params string[] names) =>
        names.Select((n, i) => new FunctionCallContent($"call-{i}", n, null)).ToList();

    private static List<ChatMessage> MessagesEndingWithToolResult()
    {
        // Simulate a normal interactive turn: System -> User -> Assistant(toolCall) -> Tool(result)
        return new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.User, "go"),
            new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("c1", "fetch_url", null) }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("c1", "<html>...</html>") }),
        };
    }

    [Test]
    public void ToolCallsPresent_AlwaysWins_RegardlessOfHistory()
    {
        var classification = StallDetector.Classify(
            MessagesEndingWithToolResult(),
            "I will call a tool",
            WithCalls("write_file"));

        Assert.That(classification, Is.EqualTo(StallClassification.ToolCallsPresent));
    }

    [Test]
    public void ToolCallsPresent_EmptyText_NoPrecedingToolResult()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.User, "go"),
        };

        var classification = StallDetector.Classify(messages, "", WithCalls("fetch_url"));

        Assert.That(classification, Is.EqualTo(StallClassification.ToolCallsPresent));
    }

    [Test]
    public void EmptyText_AfterToolResult_IsStallEmptyContent()
    {
        var classification = StallDetector.Classify(MessagesEndingWithToolResult(), "", NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.StallEmptyContent));
    }

    [Test]
    public void WhitespaceOnlyText_AfterToolResult_IsStallEmptyContent()
    {
        var classification = StallDetector.Classify(MessagesEndingWithToolResult(), "   \n  ", NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.StallEmptyContent));
    }

    [Test]
    public void NarrationEndingPeriod_AfterToolResult_IsStallNarration()
    {
        var classification = StallDetector.Classify(
            MessagesEndingWithToolResult(),
            "Let me now process that and write the results to a file.",
            NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.StallNarration));
    }

    [Test]
    public void QuestionEndingMark_AfterToolResult_IsWaitingForUserInput()
    {
        var classification = StallDetector.Classify(
            MessagesEndingWithToolResult(),
            "I retrieved the page. Would you like me to also fetch the sitemap?",
            NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.WaitingForUserInput));
    }

    [Test]
    public void QuestionWithTrailingWhitespace_AfterToolResult_IsWaitingForUserInput()
    {
        var classification = StallDetector.Classify(
            MessagesEndingWithToolResult(),
            "Are you ready?\n  ",
            NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.WaitingForUserInput));
    }

    [Test]
    public void TextEndingExclamationQuestion_AfterToolResult_IsStallNarration()
    {
        // Documented edge case: `?!` ends with `!`, so it's a stall. Prompt fix in 9.1b.
        var classification = StallDetector.Classify(
            MessagesEndingWithToolResult(),
            "Really?!",
            NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.StallNarration));
    }

    [Test]
    public void TextEndingSmartQuestionMark_AfterToolResult_IsStallNarration()
    {
        // Documented edge case: smart-quote `？` is not ASCII `?`. Prompt fix in 9.1b.
        var classification = StallDetector.Classify(
            MessagesEndingWithToolResult(),
            "本当に？",
            NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.StallNarration));
    }

    [Test]
    public void EmptyText_NoPrecedingToolResult_UserMessageMostRecent_IsNotApplicable()
    {
        // The user injected a message mid-step; the LLM produced an empty turn.
        // AC #4 — not a stall, existing input-wait branch runs.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.Assistant, "Hi, how can I help?"),
            new(ChatRole.User, "tell me more"),
            new(ChatRole.Assistant, ""),
        };

        var classification = StallDetector.Classify(messages, "", NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.NotApplicableNoToolResult));
    }

    [Test]
    public void EmptyText_EmptyMessages_IsNotApplicable()
    {
        var classification = StallDetector.Classify(new List<ChatMessage>(), "", NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.NotApplicableNoToolResult));
    }

    [Test]
    public void EmptyText_OnlySystemAndAssistantMessages_IsNotApplicable()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.Assistant, "Hi"),
        };

        var classification = StallDetector.Classify(messages, "", NoCalls);
        Assert.That(classification, Is.EqualTo(StallClassification.NotApplicableNoToolResult));
    }
}
