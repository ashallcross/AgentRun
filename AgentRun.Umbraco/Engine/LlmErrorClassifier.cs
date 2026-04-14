using System.Net;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Classifies LLM provider exceptions into user-friendly error codes and messages.
/// Returns null for OperationCanceledException with a cancelled token (user cancellation — rethrow path).
/// Callers must filter user cancellation before calling (StepExecutor's catch filter guarantees this).
/// </summary>
public static class LlmErrorClassifier
{
    public static (string ErrorCode, string UserMessage)? Classify(Exception ex)
    {
        // 1. Timeout — any TaskCanceledException reaching here is a timeout
        //    (StepExecutor already rethrows user cancellation via catch filter)
        if (ex is TaskCanceledException)
            return ("timeout", "The AI provider timed out. Check your provider configuration and retry.");

        // 2. User cancellation safety net — should not reach here, but defensive
        if (ex is OperationCanceledException oce && oce.CancellationToken.IsCancellationRequested)
            return null;

        // 3-5. HttpRequestException — check status code first, then fall through to message matching
        if (ex is HttpRequestException hre && hre.StatusCode.HasValue)
        {
            if (hre.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // 429 can be rate limit OR billing — check message for billing indicators
                if (IsBillingMessage(hre.Message))
                    return ("billing_error", "The AI provider rejected the request due to billing or quota limits. Check your account credit and plan.");
                return ("rate_limit", "The AI provider returned a rate limit error. Wait a moment and retry.");
            }

            if (hre.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ("auth_error", "The AI provider rejected the API key. Check your provider configuration.");

            // 402 Payment Required — billing/quota
            if (hre.StatusCode == (HttpStatusCode)402)
                return ("billing_error", "The AI provider rejected the request due to billing or quota limits. Check your account credit and plan.");

            return ("provider_error", "The AI provider returned an error. Check your provider configuration and retry.");
        }

        // 6. Message-based fallback for rate limit (catches provider SDK exceptions)
        var message = ex.Message;
        if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return ("rate_limit", "The AI provider returned a rate limit error. Wait a moment and retry.");

        // 6b. Message-based fallback for billing/quota errors
        if (IsBillingMessage(message))
            return ("billing_error", "The AI provider rejected the request due to billing or quota limits. Check your account credit and plan.");

        // 7. Message-based fallback for auth errors
        if (message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            return ("auth_error", "The AI provider rejected the API key. Check your provider configuration.");

        // 8. Default — unknown provider error
        return ("provider_error", "The AI provider returned an error. Check your provider configuration and retry.");
    }

    private static bool IsBillingMessage(string message)
        => message.Contains("billing", StringComparison.OrdinalIgnoreCase)
           || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
           || message.Contains("credit", StringComparison.OrdinalIgnoreCase)
           || message.Contains("insufficient_funds", StringComparison.OrdinalIgnoreCase)
           || message.Contains("budget", StringComparison.OrdinalIgnoreCase);
}
