# Upstream Issue Draft: Umbraco.AI middleware logs cancellation as ERR

**Target:** Umbraco.AI package maintainers (Warren Buckley / Niels Lyngsø / whoever is on point)
**Severity:** Cosmetic — log noise only, no functional impact
**Version observed:** Umbraco.AI 1.8.0
**Reporter context:** AgentRun.Umbraco — consumer of `IAIChatService` via a custom tool loop

---

## Summary

When a consumer cancels an in-flight chat streaming call by signalling the `CancellationToken`, Umbraco.AI's middleware chain logs the cancellation as an error, producing two ERR-level log entries per cancel with full stack traces. The cancellation itself succeeds — the HTTP connection to the provider is torn down, no further tokens are billed — but the log noise makes "user clicked Cancel" indistinguishable from "chat provider returned 5xx" when tailing server logs.

## Log output on every cancel

```
[ERR] Failed to record AI usage
System.Threading.Tasks.TaskCanceledException: A task was canceled.
   at Umbraco.AI.Core.Analytics.Usage.AIUsageRecordingService.QueueRecordUsageAsync(AIUsageRecord record, CancellationToken ct)
   at Umbraco.AI.Core.Analytics.Usage.Middleware.AIUsageRecordingChatClient.RecordUsageAsync(Int64 durationMs, Boolean succeeded, String errorMessage, CancellationToken ct)

[ERR] AuditLog <guid> failed with error: The operation was canceled. (Duration: 1398.226ms)
System.Threading.Tasks.TaskCanceledException: The operation was canceled.
 ---> System.Threading.Tasks.TaskCanceledException: The operation was canceled.
 ---> System.IO.IOException: Unable to read data from the transport connection: Operation canceled.
 ---> System.Net.Sockets.SocketException (89): Operation canceled
   [...full middleware chain stack: AIUsageRecordingChatClient → AIAuditingChatClient →
    AIContextInjectingChatClient → ScopedProfileChatClient → ScopedInlineChatClient → ...]
```

## Root cause

The middleware chain treats any exception from the downstream `IChatClient` as a failure:

- `AIUsageRecordingChatClient.RecordUsageAsync` / `QueueRecordUsageAsync` catches whatever bubbles up and logs ERR. It does not check whether the exception is `OperationCanceledException` matching the caller's token.
- `AIAuditingChatClient.WrapStreamWithErrorCapture` / `GetStreamingResponseAsync` records a failed-audit event with stack when the stream throws, without distinguishing cancellation from real failure.

From the middleware's perspective, "stream throws OCE" looks the same as "provider 500"s, so both surface at ERR.

## Expected behaviour

Both middleware layers should distinguish cancellation from failure. One common pattern:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // Caller cancelled deliberately — cold path, not an error.
    _logger.LogInformation("Chat stream cancelled by caller (recording skipped).");
    throw;
}
catch (Exception ex)
{
    _logger.LogError(ex, "...");
    throw;
}
```

Equivalent for the audit log: still record the audit entry, but with a dedicated `Cancelled` outcome rather than `Failed`, and log at Information rather than Error.

## Why it matters to consumers

The AgentRun package wires a per-instance `CancellationTokenSource` so the Cancel button in the backoffice actually stops an in-flight LLM run (stopping token burn on a run the user has abandoned). This is a normal, expected user action — it is not an error condition. Producing two ERR stacks per cancel makes real failures harder to spot and encourages users to silence or filter the logger, which then hides genuine issues.

## Reproduction

1. Register any `IChatClient` consumer that passes a `CancellationToken` into `GetStreamingResponseAsync`.
2. Start a streaming call against a real provider (Anthropic/OpenAI via Umbraco.AI).
3. Signal the token's `CancellationTokenSource.Cancel()` mid-stream.
4. Observe two ERR log lines from the Umbraco.AI middleware chain.

## Proposed fix location (from stack trace)

- `Umbraco.AI.Core.Analytics.Usage.Middleware.AIUsageRecordingChatClient.RecordUsageAsync` — downgrade OCE on the caller's token to Information.
- `Umbraco.AI.Core.AuditLog.Middleware.AIAuditingChatClient.WrapStreamWithErrorCapture` — add a dedicated `Cancelled` outcome and log at Information for caller-initiated cancellation.

Happy to test a pre-release or help validate a patch against the AgentRun cancel path if useful.
