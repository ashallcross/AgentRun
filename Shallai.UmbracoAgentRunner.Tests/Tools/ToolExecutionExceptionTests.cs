using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Tests.Tools;

[TestFixture]
public class ToolExecutionExceptionTests
{
    [Test]
    public void InheritsFromAgentRunnerException()
    {
        var ex = new ToolExecutionException("test error");

        Assert.That(ex, Is.InstanceOf<AgentRunnerException>());
    }

    [Test]
    public void MessageConstructor_SetsMessage()
    {
        var ex = new ToolExecutionException("disk full");

        Assert.That(ex.Message, Is.EqualTo("disk full"));
    }

    [Test]
    public void InnerExceptionConstructor_SetsMessageAndInner()
    {
        var inner = new InvalidOperationException("io error");
        var ex = new ToolExecutionException("tool failed", inner);

        Assert.That(ex.Message, Is.EqualTo("tool failed"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [Test]
    public void NullMessage_ProducesDefaultExceptionMessage()
    {
        // Failure & Edge Case #4: null message propagates to base Exception
        var ex = new ToolExecutionException(null!);

        // Error result format: "Tool 'x' execution error: " — null message yields empty suffix
        var errorResult = $"Tool 'test_tool' execution error: {ex.Message}";
        Assert.That(errorResult, Does.Contain("execution error:"));
    }
}
