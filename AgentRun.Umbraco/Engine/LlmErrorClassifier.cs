using System.Net;

namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Classifies LLM provider exceptions into user-friendly error codes and messages.
/// Returns null for non-TaskCanceledException OperationCanceledException with a cancelled
/// token (user cancellation — rethrow path). TaskCanceledException always classifies as
/// "timeout" because StepExecutor's upstream catch filter rethrows real user cancellation
/// before this method runs — a TCE reaching here is provider-side timeout, not user intent.
/// </summary>
public static class LlmErrorClassifier
{
    private const int InnerExceptionWalkCap = 5;
    private const string ProviderErrorCode = "provider_error";

    public static (string ErrorCode, string UserMessage)? Classify(Exception ex)
    {
        // User cancellation safety net runs at the top level only — inner exceptions
        // must not mask a real cancellation token signalled by the caller.
        if (ex is OperationCanceledException oce
            && oce is not TaskCanceledException
            && oce.CancellationToken.IsCancellationRequested)
            return null;

        // Provider SDKs commonly wrap the authoritative HttpRequestException inside
        // a generic "ApiException: The request failed" — message-based fallback on
        // the outer would miss the status-code signal. Walk the chain and return
        // the first specific (non-catch-all) classification; if every level is the
        // catch-all, fall back to the top-level result. AggregateException siblings
        // are enumerated because Task-based async (Task.WhenAll, Parallel.For) can
        // surface a generic wrapper at index 0 while the billing/auth HRE lives at
        // a later index.
        var topLevel = ClassifySingle(ex);
        if (topLevel is { } first && first.ErrorCode != ProviderErrorCode)
            return topLevel;

        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(Exception Node, int Depth)>();
        EnqueueInner(queue, ex, 0);
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= InnerExceptionWalkCap || !seen.Add(current))
                continue;
            // Cancellation safety net repeats inside the walk: a Task.WhenAll-style
            // AggregateException can carry a non-TCE OperationCanceledException as a
            // sibling; without this re-check the walk would classify it as the
            // generic provider catch-all, masking the user's cancel signal.
            if (current is OperationCanceledException innerOce
                && innerOce is not TaskCanceledException
                && innerOce.CancellationToken.IsCancellationRequested)
                return null;
            var inner = ClassifySingle(current);
            if (inner is { } resolved && resolved.ErrorCode != ProviderErrorCode)
                return resolved;
            EnqueueInner(queue, current, depth + 1);
        }
        return topLevel;

        static void EnqueueInner(Queue<(Exception Node, int Depth)> queue, Exception node, int depth)
        {
            if (node is AggregateException agg)
            {
                foreach (var sibling in agg.InnerExceptions)
                {
                    // Custom AggregateException subclasses / serialised graphs can
                    // surface a null entry; skipping silently keeps ClassifySingle
                    // safe from a NullReferenceException on pattern-match.
                    if (sibling is not null)
                        queue.Enqueue((sibling, depth));
                }
                return;
            }
            if (node.InnerException is { } child)
                queue.Enqueue((child, depth));
        }
    }

    private static (string ErrorCode, string UserMessage)? ClassifySingle(Exception ex)
    {
        // 1. Timeout — any TaskCanceledException reaching here is a timeout
        //    (StepExecutor already rethrows user cancellation via catch filter)
        if (ex is TaskCanceledException)
            return ("timeout", "The AI provider timed out. Check your provider configuration and retry.");

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

            return (ProviderErrorCode, "The AI provider returned an error. Check your provider configuration and retry.");
        }

        // 6. Message-based fallback. Order matters: billing/quota beats rate-limit
        //    because Azure's quota error body cheerfully includes a help-link
        //    sentence ending "...the default rate limit." — that incidental
        //    substring would mis-classify a billing failure as a rate_limit
        //    transient otherwise. Auth runs last; it's the lowest-confidence
        //    pattern set (English-prose phrasing across providers).
        var message = ex.Message;
        if (IsBillingMessage(message))
            return ("billing_error", "The AI provider rejected the request due to billing or quota limits. Check your account credit and plan.");

        if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return ("rate_limit", "The AI provider returned a rate limit error. Wait a moment and retry.");

        // 7. Message-based fallback for auth errors. Covers both stripped-status
        //    HttpRequestException shapes and provider SDK exceptions whose body
        //    leaks through .Message. OpenAI publishes "Incorrect API key" /
        //    "invalid_api_key"; Azure publishes "invalid subscription key" /
        //    "Access denied"; legacy providers use "Unauthorized"/"Forbidden".
        if (IsAuthMessage(message))
            return ("auth_error", "The AI provider rejected the API key. Check your provider configuration.");

        // 8. Default — unknown provider error
        return (ProviderErrorCode, "The AI provider returned an error. Check your provider configuration and retry.");
    }

    private static bool IsBillingMessage(string message)
        => message.Contains("billing", StringComparison.OrdinalIgnoreCase)
           || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
           || message.Contains("credit", StringComparison.OrdinalIgnoreCase)
           || message.Contains("insufficient_funds", StringComparison.OrdinalIgnoreCase)
           || message.Contains("budget", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthMessage(string message)
        => message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
           || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
           || message.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase)
           || message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase)
           || message.Contains("incorrect api key", StringComparison.OrdinalIgnoreCase)
           || message.Contains("invalid subscription key", StringComparison.OrdinalIgnoreCase)
           || message.Contains("access denied", StringComparison.OrdinalIgnoreCase);
}
