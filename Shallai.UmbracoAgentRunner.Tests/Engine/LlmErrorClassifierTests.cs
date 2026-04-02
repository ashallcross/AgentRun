using System.Net;
using Shallai.UmbracoAgentRunner.Engine;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

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
}
