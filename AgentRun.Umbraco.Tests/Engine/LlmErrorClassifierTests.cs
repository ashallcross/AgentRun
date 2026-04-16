using System.Net;
using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class LlmErrorClassifierTests
{
    [Test]
    public void HttpRequestException_Status429_ReturnsRateLimit()
    {
        var ex = new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("rate_limit"));
        Assert.That(result.Value.UserMessage, Does.Contain("rate limit"));
    }

    [Test]
    public void HttpRequestException_Status401_ReturnsAuthError()
    {
        var ex = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("auth_error"));
        Assert.That(result.Value.UserMessage, Does.Contain("API key"));
    }

    [Test]
    public void HttpRequestException_Status403_ReturnsAuthError()
    {
        var ex = new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("auth_error"));
    }

    [Test]
    public void HttpRequestException_Status500_ReturnsProviderError()
    {
        var ex = new HttpRequestException("Internal Server Error", null, HttpStatusCode.InternalServerError);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("provider_error"));
        Assert.That(result.Value.UserMessage, Does.Contain("returned an error"));
    }

    [Test]
    public void HttpRequestException_NoStatusCode_MessageContainsRateLimit_ReturnsRateLimit()
    {
        var ex = new HttpRequestException("rate limit exceeded");

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("rate_limit"));
    }

    [Test]
    public void TaskCanceledException_NonCancelledToken_ReturnsTimeout()
    {
        var ex = new TaskCanceledException("The operation timed out");

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("timeout"));
        Assert.That(result.Value.UserMessage, Does.Contain("timed out"));
    }

    [Test]
    public void TaskCanceledException_WithCancelledInternalToken_ReturnsTimeout()
    {
        // HttpClient timeout uses an internal CTS that IS cancelled — must still classify as timeout
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("The operation was canceled.", null, cts.Token);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("timeout"));
    }

    [Test]
    public void OperationCanceledException_CancelledToken_ReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new OperationCanceledException(cts.Token);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GenericException_MessageContainsUnauthorized_ReturnsAuthError()
    {
        var ex = new Exception("The request was unauthorized by the provider");

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("auth_error"));
    }

    [Test]
    public void GenericException_UnrelatedMessage_ReturnsProviderError()
    {
        var ex = new Exception("Something unexpected happened");

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("provider_error"));
    }

    // --- Billing/quota tests (Story 10.12, AC7) ---

    [Test]
    public void HttpRequestException_Status402_ReturnsBillingError()
    {
        var ex = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
        Assert.That(result.Value.UserMessage, Does.Contain("billing or quota"));
    }

    [Test]
    public void HttpRequestException_Status429_WithBillingMessage_ReturnsBillingError()
    {
        var ex = new HttpRequestException("You exceeded your current quota", null, HttpStatusCode.TooManyRequests);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    [TestCase("billing limit exceeded")]
    [TestCase("insufficient_funds")]
    [TestCase("quota exceeded for this model")]
    [TestCase("credit balance is zero")]
    [TestCase("budget limit reached")]
    public void MessageBased_BillingPatterns_ReturnBillingError(string message)
    {
        var ex = new Exception(message);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    [Test]
    public void HttpRequestException_Status429_NonBillingMessage_ReturnsRateLimit()
    {
        // 429 without billing keywords remains rate_limit
        var ex = new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("rate_limit"));
    }

    [Test]
    public void MessageBased_BillingBeatsRateLimit_OrderingIsLocked()
    {
        // Pin the rule: when message-based fallback runs (StatusCode stripped by
        // SDK), a body that mentions BOTH billing and rate-limit signals must
        // resolve to billing_error. Real Azure quota errors trail with
        // "...the default rate limit." — moving rate-limit ahead of billing here
        // (a defensible-looking refactor) would silently regress that fixture.
        var ex = new Exception("You have exceeded your monthly quota. Please retry after 5s — the default rate limit.");

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    // --- Inner-exception walk tests (Story 10.13, AC1) ---

    [Test]
    public void InnerExceptionWalk_OuterCatchAllWrapsHttp402_ReturnsBillingError()
    {
        // OpenAI SDK pattern: ApiException("The request failed") wrapping
        // HttpRequestException(StatusCode=402). Outer message-based fallback would
        // produce provider_error and lose the billing signal.
        var inner = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);
        var outer = new InvalidOperationException("The request failed", inner);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    [Test]
    public void InnerExceptionWalk_TopLevelSpecificBeatsInner()
    {
        // If the top level is already specific (auth_error here), we must not walk
        // past it. The inner billing signal is incidental noise.
        var inner = new HttpRequestException("quota exceeded", null, (HttpStatusCode)402);
        var outer = new HttpRequestException("Unauthorized", inner, HttpStatusCode.Unauthorized);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("auth_error"));
    }

    [Test]
    public void InnerExceptionWalk_FiveLevelChain_ResolvesAtCapEdge()
    {
        // EnqueueInner(outer, depth=0) puts l1 at depth 0; each further hop
        // increments depth by 1, so inner5 lands at depth 4 — the last depth
        // accepted before the `if (depth >= InnerExceptionWalkCap) continue;`
        // gate fires. Cap=5 means depths 0..4 are visited; depth 5 is rejected.
        var inner5 = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);
        var l4 = new InvalidOperationException("wrapper 4", inner5);
        var l3 = new InvalidOperationException("wrapper 3", l4);
        var l2 = new InvalidOperationException("wrapper 2", l3);
        var l1 = new InvalidOperationException("wrapper 1", l2);
        var outer = new InvalidOperationException("wrapper 0", l1);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    [Test]
    public void InnerExceptionWalk_SixLevelChain_FallsBackToTopLevel()
    {
        // Recognised exception at depth 6 — beyond the cap. Walk terminates and
        // falls back to top-level catch-all classification.
        var deep = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);
        var l5 = new InvalidOperationException("w5", deep);
        var l4 = new InvalidOperationException("w4", l5);
        var l3 = new InvalidOperationException("w3", l4);
        var l2 = new InvalidOperationException("w2", l3);
        var l1 = new InvalidOperationException("w1", l2);
        var outer = new InvalidOperationException("Something unexpected", l1);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("provider_error"));
    }

    [Test]
    public void InnerExceptionWalk_CircularChain_TerminatesViaSeenSet()
    {
        // Reflection-set InnerException to a parent to construct a self-referential
        // graph. The walk's reference-equality `seen` set prevents infinite loops
        // independently of the depth cap.
        var inner = new InvalidOperationException("inner");
        var outer = new InvalidOperationException("outer", inner);
        var innerExceptionField = typeof(Exception).GetField(
            "_innerException",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(innerExceptionField, Is.Not.Null, "Runtime renamed Exception._innerException — adjust test");
        innerExceptionField!.SetValue(inner, outer);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("provider_error"));
    }

    [Test]
    public void InnerExceptionWalk_OuterOperationCanceled_ShortCircuitsToNull()
    {
        // Cancellation safety net runs at top level only. An OCE wrapping a
        // recognised provider exception still returns null — the user signal wins.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var inner = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);
        var outer = new OperationCanceledException("cancelled", inner, cts.Token);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void InnerExceptionWalk_NullInnerException_NoRegression()
    {
        // Top-level only with no inner — must classify identically to pre-walk behaviour.
        var ex = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    [Test]
    public void InnerExceptionWalk_AggregateException_ClassifiesSiblingSpecific()
    {
        // Task.WhenAll pattern: AggregateException wraps a generic helper failure at
        // index 0 and the authoritative provider HRE at index 1. Walking only
        // `.InnerException` (which aliases `.InnerExceptions[0]`) would miss the
        // billing signal and misclassify as provider_error. Siblings must be
        // enumerated. P1 from code review.
        var siblingA = new InvalidOperationException("Background task failed");
        var siblingB = new HttpRequestException("Payment Required", null, (HttpStatusCode)402);
        var outer = new AggregateException("One or more errors", siblingA, siblingB);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("billing_error"));
    }

    [Test]
    public void InnerExceptionWalk_AggregateException_AllSiblingsGeneric_FallsBackToTopLevel()
    {
        // AggregateException with every sibling unclassifiable — walk terminates,
        // top-level classification (provider_error default) is returned.
        var siblingA = new InvalidOperationException("task A failed");
        var siblingB = new InvalidOperationException("task B failed");
        var outer = new AggregateException("One or more errors", siblingA, siblingB);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("provider_error"));
    }

    [Test]
    public void InnerExceptionWalk_AggregateException_NestedSiblingHre_ResolvesViaChildWalk()
    {
        // AggregateException where each sibling itself wraps a child: index 0 is a
        // plain wrapper; index 1 wraps the recognised HRE one level deeper. The
        // walk must descend into siblings, not just classify them directly.
        var siblingA = new InvalidOperationException("generic A");
        var deepHre = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        var siblingB = new InvalidOperationException("generic B wrapping HRE", deepHre);
        var outer = new AggregateException("One or more errors", siblingA, siblingB);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo("auth_error"));
    }

    [Test]
    public void InnerExceptionWalk_AggregateException_SiblingOperationCanceled_ReturnsNull()
    {
        // Task.WhenAll-style AggregateException carrying a non-TCE OCE with a
        // cancelled token as a sibling: the top-level OCE short-circuit doesn't
        // fire (top is the AE), so the walk must repeat the cancellation check
        // on each node — otherwise the user's cancel signal is masked as
        // provider_error from the catch-all branch.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var siblingA = new InvalidOperationException("Background task failed");
        var siblingB = new OperationCanceledException("user cancel propagated", cts.Token);
        var outer = new AggregateException("One or more errors", siblingA, siblingB);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Null);
    }

    // Note: the EnqueueInner null-sibling guard in LlmErrorClassifier.cs is
    // defensive cover for deserialised AggregateException graphs from external
    // libraries — it is not reachable through the public AggregateException
    // API because the type's own get_Message iterator NREs first on a null
    // entry. Logged in deferred-work as a coverage gap.

    // --- Real-fixture tests for OpenAI + Azure OpenAI error shapes (Story 10.13, AC2) ---

    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "provider-errors");

    private static string LoadFixture(string filename)
    {
        var path = Path.Combine(FixtureRoot, filename);
        Assert.That(File.Exists(path), Is.True, $"Missing fixture: {path}");
        return File.ReadAllText(path);
    }

    [TestCase("openai-billing.json", HttpStatusCode.TooManyRequests, "billing_error")]
    [TestCase("openai-auth.json", HttpStatusCode.Unauthorized, "auth_error")]
    [TestCase("openai-rate-limit.json", HttpStatusCode.TooManyRequests, "rate_limit")]
    [TestCase("azure-quota.json", HttpStatusCode.TooManyRequests, "billing_error")]
    [TestCase("azure-auth.json", HttpStatusCode.Unauthorized, "auth_error")]
    [TestCase("azure-rate-limit.json", HttpStatusCode.TooManyRequests, "rate_limit")]
    public void Fixture_WithStatusCode_ClassifiesAsExpected(string filename, HttpStatusCode status, string expectedCode)
    {
        var body = LoadFixture(filename);
        var ex = new HttpRequestException(body, inner: null, statusCode: status);

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo(expectedCode));
    }

    // Edge case from AC2: provider SDK may strip the StatusCode despite a clear
    // signal in the body. Message-based fallback must catch billing/auth shapes.
    // Pure rate-limit fixtures need explicit "rate limit" in the body — both ours have it.
    [TestCase("openai-billing.json", "billing_error")]
    [TestCase("openai-auth.json", "auth_error")]
    [TestCase("openai-rate-limit.json", "rate_limit")]
    [TestCase("azure-quota.json", "billing_error")]
    [TestCase("azure-auth.json", "auth_error")]
    [TestCase("azure-rate-limit.json", "rate_limit")]
    public void Fixture_WithoutStatusCode_FallsBackToMessageClassification(string filename, string expectedCode)
    {
        var body = LoadFixture(filename);
        var ex = new HttpRequestException(body); // statusCode = null

        var result = LlmErrorClassifier.Classify(ex);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo(expectedCode));
    }

    // Combined coverage: SDK wraps the HttpRequestException in a generic ApiException.
    // This is the exact scenario the inner-exception walk closes for marketplace users
    // hitting OpenAI / Azure with credentials that fail in the SDK's "official" way.
    [TestCase("openai-billing.json", HttpStatusCode.TooManyRequests, "billing_error")]
    [TestCase("openai-auth.json", HttpStatusCode.Unauthorized, "auth_error")]
    [TestCase("openai-rate-limit.json", HttpStatusCode.TooManyRequests, "rate_limit")]
    [TestCase("azure-quota.json", HttpStatusCode.TooManyRequests, "billing_error")]
    [TestCase("azure-auth.json", HttpStatusCode.Unauthorized, "auth_error")]
    [TestCase("azure-rate-limit.json", HttpStatusCode.TooManyRequests, "rate_limit")]
    public void Fixture_WrappedInGenericSdkException_InnerWalkResolves(string filename, HttpStatusCode status, string expectedCode)
    {
        var body = LoadFixture(filename);
        var inner = new HttpRequestException(body, inner: null, statusCode: status);
        var outer = new InvalidOperationException("The request failed", inner);

        var result = LlmErrorClassifier.Classify(outer);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.ErrorCode, Is.EqualTo(expectedCode));
    }
}
