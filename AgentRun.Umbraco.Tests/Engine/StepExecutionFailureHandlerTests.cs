using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class StepExecutionFailureHandlerTests
{
    private StepExecutionFailureHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StepExecutionFailureHandler();
    }

    [Test]
    public void Classify_AgentRunException_BypassesLlmErrorClassifier_AndPreservesMessage()
    {
        // Load-bearing invariant (memory: feedback_agentrun_exception_classifier.md).
        // Engine-domain exceptions must keep their messages intact so the user sees
        // the actual engine-level failure reason, not a generic "provider_error".
        var ex = new AgentRunException("step 'analyser' failed: missing required artifact");

        var result = _handler.Classify(ex);
        Assert.That(result, Is.Not.Null);
        var (code, message) = result!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo("step_failed"));
            Assert.That(message, Is.EqualTo("step 'analyser' failed: missing required artifact"));
        });
    }

    [Test]
    public void Classify_StallDetectedException_RoutesToStallDetected_AndPreservesConstructedMessage()
    {
        var ex = new StallDetectedException(
            lastToolCall: "write_file",
            stepId: "analyser",
            instanceId: "inst-001",
            workflowAlias: "content-quality-audit");

        var result = _handler.Classify(ex);
        Assert.That(result, Is.Not.Null);
        var (code, message) = result!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo("stall_detected"));
            Assert.That(message, Is.EqualTo(ex.Message));
        });
    }

    [Test]
    public void Classify_ProviderEmptyResponseException_RoutesToProviderEmptyResponse()
    {
        var ex = new ProviderEmptyResponseException(
            stepId: "scanner",
            instanceId: "inst-001",
            workflowAlias: "content-quality-audit");

        var result = _handler.Classify(ex);
        Assert.That(result, Is.Not.Null);
        var (code, message) = result!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo("provider_empty_response"));
            Assert.That(message, Is.EqualTo(ex.Message));
        });
    }

    [Test]
    public void Classify_NonEngineException_RoutesToLlmErrorClassifier()
    {
        // Story 10.7a review patch P14 — non-engine exceptions fall through
        // to LlmErrorClassifier. Guards against a future regression that
        // short-circuits all exceptions via the engine-domain arms.
        var ex = new TaskCanceledException("provider timed out");

        var result = _handler.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("timeout"));
    }

    [Test]
    public void Classify_UserCancellation_ReturnsNull()
    {
        // LlmErrorClassifier's contract: OCE with cancellation requested returns
        // null ("do not record an error"). Handler must faithfully propagate.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new OperationCanceledException(cts.Token);

        var result = _handler.Classify(ex);

        Assert.That(result, Is.Null);
    }
}
