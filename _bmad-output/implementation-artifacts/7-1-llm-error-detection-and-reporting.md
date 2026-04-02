# Story 7.1: LLM Error Detection & Reporting

Status: done

## Story

As a developer or editor,
I want clear plain-language error messages when an LLM provider fails,
So that I know what went wrong and what to do next without needing technical knowledge.

## Context

**UX Mode: Both interactive and autonomous.**

Error handling infrastructure already exists but is generic. The current flow:
1. ToolLoop catches `OperationCanceledException` (rethrows) but lets all other LLM exceptions propagate.
2. StepExecutor catches any exception, sets step status to `Error`, but does NOT emit events or classify the error.
3. WorkflowOrchestrator detects step error status, emits `run.error` with `"step_failed"` / `"Step '{name}' failed"` — a generic message regardless of cause.
4. Frontend receives `run.error`, displays `"Error: Step '{name}' failed"` as a system message.

This story replaces that generic pipeline with classified, user-friendly error messages. The error is caught and classified in the engine, emitted with a specific error code, and displayed in the chat panel using the UX-DR16 pattern: "[What happened]. [What to do]."

**Key constraint:** The engine layer (`Engine/`) has zero Umbraco dependencies. LLM calls go through `IChatClient` (Microsoft.Extensions.AI). Provider errors surface as standard .NET exceptions — `HttpRequestException`, `TaskCanceledException` (timeout), `OperationCanceledException`, or provider-specific exceptions. Classification must work from these base types.

## Acceptance Criteria

1. **Given** the step executor calls the LLM via ToolLoop
   **When** the LLM provider returns a rate limit error (HTTP 429 / "rate limit" in message)
   **Then** the error is classified as `"rate_limit"`
   **And** the run.error SSE event carries error code `"rate_limit"` and message `"The AI provider returned a rate limit error. Wait a moment and retry."`

2. **Given** the step executor calls the LLM via ToolLoop
   **When** the LLM provider times out (TaskCanceledException not from user cancellation)
   **Then** the error is classified as `"timeout"`
   **And** the run.error SSE event carries error code `"timeout"` and message `"The AI provider timed out. Check your provider configuration and retry."`

3. **Given** the step executor calls the LLM via ToolLoop
   **When** an authentication or authorization error occurs (HTTP 401/403 / "unauthorized"/"forbidden" in message)
   **Then** the error is classified as `"auth_error"`
   **And** the run.error SSE event carries error code `"auth_error"` and message `"The AI provider rejected the API key. Check your provider configuration."`

4. **Given** the step executor calls the LLM via ToolLoop
   **When** any other LLM API error occurs (HttpRequestException, other provider exceptions)
   **Then** the error is classified as `"provider_error"`
   **And** the run.error SSE event carries error code `"provider_error"` and message `"The AI provider returned an error. Check your provider configuration and retry."`

5. **Given** a step fails with any classified error
   **Then** the step status is set to `"Error"` atomically (existing behaviour preserved)
   **And** the instance status is set to `"Failed"` (existing behaviour preserved)
   **And** structured logging records the error at Error level with WorkflowAlias, InstanceId, StepId, and the original exception details
   **And** the error does not crash the Umbraco application (NFR23)
   **And** the instance remains in a recoverable state (NFR24)

6. **Given** the frontend receives a run.error SSE event with a classified error
   **When** the chat panel displays the error
   **Then** the system message shows the human-readable message from the event (not prefixed with "Error:")
   **And** no stack traces, HTTP status codes, or raw exception messages are shown
   **And** the step progress sidebar shows error status (red cross icon, --uui-color-danger) — existing behaviour, no change needed

7. **Given** a step fails due to a non-LLM error (validation failure, completion check failure, tool crash that terminates the step)
   **Then** the existing generic error path is preserved — error code `"step_failed"`, message `"Step '{name}' failed"`
   **And** the new classification logic does not interfere with these paths

8. **Given** all error classification scenarios
   **When** unit tests run
   **Then** tests verify error classification for: rate limit (429 status, "rate limit" message), timeout (TaskCanceledException), auth error (401/403, "unauthorized"), and generic provider error (other HttpRequestException)
   **And** tests verify the correct error code and user-facing message for each classification
   **And** tests verify that OperationCanceledException (user cancellation) is NOT classified — it is rethrown as before

## What NOT to Build

- **No retry logic** — retry is Story 7.2. This story only detects, classifies, and reports the error.
- **No new SSE event types** — use the existing `run.error` event with the existing `RunErrorPayload(Error, Message)` record. Just change the values sent.
- **No changes to tool execution errors** — ToolExecutionException handling in ToolLoop is unchanged. Those errors are returned to the LLM, not surfaced to the user.
- **No provider-specific SDK dependencies** — classify from standard .NET exception types only (HttpRequestException, TaskCanceledException). Do NOT import Anthropic or OpenAI SDK types.
- **No new frontend components** — modify the existing run.error handler in shallai-instance-detail.
- **No changes to step status values** — StepStatus.Error and InstanceStatus.Failed remain unchanged.
- **No changes to conversation persistence** — JSONL recording is unaffected.
- **No error toast notifications or modal dialogs** — errors display as system messages in the chat panel only (UX-DR16).

## Tasks / Subtasks

- [x] Task 1: Create LlmErrorClassifier in Engine (AC: #1, #2, #3, #4)
  - [x] Create `Engine/LlmErrorClassifier.cs` — a static class with a single method:
    ```
    public static (string ErrorCode, string UserMessage) Classify(Exception ex)
    ```
  - [x] Classification logic (check in order — first match wins):
    1. `OperationCanceledException` where `CancellationToken.IsCancellationRequested` is true → return `null` (not an LLM error, rethrow path)
    2. `TaskCanceledException` (not user cancellation) → `("timeout", "The AI provider timed out. Check your provider configuration and retry.")`
    3. `HttpRequestException` with StatusCode 429, OR exception message contains "rate limit" (case-insensitive) → `("rate_limit", "The AI provider returned a rate limit error. Wait a moment and retry.")`
    4. `HttpRequestException` with StatusCode 401 or 403, OR exception message contains "unauthorized" or "forbidden" (case-insensitive) → `("auth_error", "The AI provider rejected the API key. Check your provider configuration.")`
    5. `HttpRequestException` (any other status) → `("provider_error", "The AI provider returned an error. Check your provider configuration and retry.")`
    6. Exception message contains "rate limit" (case-insensitive, catches provider SDK exceptions) → `("rate_limit", ...)`
    7. Exception message contains "unauthorized" or "forbidden" (case-insensitive) → `("auth_error", ...)`
    8. Default → `("provider_error", "The AI provider returned an error. Check your provider configuration and retry.")`
  - [x] Return type: `(string ErrorCode, string UserMessage)?` — nullable to signal "not an LLM error, rethrow" for OperationCanceledException
  - [x] No dependencies — pure static method, no interfaces, no DI

- [x] Task 2: Integrate classifier into StepExecutor (AC: #5, #7)
  - [x] In `StepExecutor.ExecuteStepAsync`, modify the catch block at line 192:
    - Before setting step status to Error, call `LlmErrorClassifier.Classify(ex)`
    - Store the classification result on the `StepExecutionContext` so the orchestrator can read it
  - [x] Add a property to `StepExecutionContext`: `public (string ErrorCode, string UserMessage)? LlmError { get; set; }`
  - [x] If classification returns null (OperationCanceledException), rethrow as before (existing line 188-190 already handles this — no change needed)
  - [x] If classification returns a result, set `context.LlmError = result` before returning
  - [x] The existing step status Error update (line 200-208) remains unchanged — classification is additional metadata, not a replacement
  - [x] Preserve existing structured logging (line 194-196) — the original exception is still logged

- [x] Task 3: Update WorkflowOrchestrator to use classified error (AC: #1, #2, #3, #4, #7)
  - [x] In `WorkflowOrchestrator`, after detecting step error status, check `context.LlmError`:
    - If `LlmError` has a value → emit `run.error` with `LlmError.Value.ErrorCode` and `LlmError.Value.UserMessage`
    - If `LlmError` is null → emit `run.error` with `"step_failed"` and `"Step '{step.Name}' failed"` (existing behaviour for non-LLM errors)
  - [x] The system message recorded to conversation (currently `"{step.Name} failed"`) should also use the classified message when available

- [x] Task 4: Update frontend error display (AC: #6)
  - [x] In `shallai-instance-detail.element.ts`, update the `run.error` handler (line 632-646):
    - Change the system message content from `` `Error: ${data.message || "Workflow failed"}` `` to `` `${data.message || "An error occurred."}` ``
    - Remove the `"Error: "` prefix — the message itself is already user-friendly and self-contained
    - The fallback changes from `"Workflow failed"` to `"An error occurred."` — more neutral, doesn't imply unrecoverable failure

- [x] Task 5: Add unit tests for LlmErrorClassifier (AC: #8)
  - [x] Create `Shallai.UmbracoAgentRunner.Tests/Engine/LlmErrorClassifierTests.cs`
  - [x] Test cases:
    - `HttpRequestException` with StatusCode 429 → `("rate_limit", ...)`
    - `HttpRequestException` with StatusCode 401 → `("auth_error", ...)`
    - `HttpRequestException` with StatusCode 403 → `("auth_error", ...)`
    - `HttpRequestException` with StatusCode 500 → `("provider_error", ...)`
    - `HttpRequestException` with no status code but message containing "rate limit" → `("rate_limit", ...)`
    - `TaskCanceledException` with non-cancelled token → `("timeout", ...)`
    - `OperationCanceledException` with cancelled token → returns null
    - Generic `Exception` with message "unauthorized" → `("auth_error", ...)`
    - Generic `Exception` with unrelated message → `("provider_error", ...)`
  - [x] Target: ~9 tests

- [x] Task 6: Add/update integration tests for StepExecutor error classification (AC: #5, #8)
  - [x] In `StepExecutorTests.cs`, add tests:
    - Profile resolver throws `HttpRequestException(429)` → step status Error AND context.LlmError set with rate_limit
    - Profile resolver throws `TaskCanceledException` → step status Error AND context.LlmError set with timeout
    - Profile resolver throws generic exception → step status Error AND context.LlmError set with provider_error
  - [x] Existing test `ExceptionDuringExecution_SetsStepStatusToError` should still pass — existing assertions unchanged
  - [x] Target: ~3 new tests

- [x] Task 7: Add/update orchestrator tests for classified error emission (AC: #1, #7)
  - [x] In `WorkflowOrchestratorTests.cs`, add tests:
    - Step fails with LlmError set → `run.error` emitted with classified error code and message
    - Step fails with LlmError null (non-LLM error) → `run.error` emitted with `"step_failed"` (existing behaviour preserved)
  - [x] Existing test `StepFails_InstanceSetToFailed_RunErrorEmitted` should still pass
  - [x] Target: ~2 new tests

- [x] Task 8: Run all tests and manual E2E validation
  - [x] `dotnet test Shallai.UmbracoAgentRunner.slnx` — all backend tests pass (306 passed, 0 failed)
  - [x] `npm run build` from `Client/` — frontend builds cleanly, `wwwroot/` output updated
  - [ ] Start TestSite with `dotnet run` — application starts without errors
  - [ ] Manual E2E: Start an interactive workflow with a valid Anthropic provider → runs normally, no regressions
  - [ ] Manual E2E: Temporarily misconfigure the API key (wrong key) → trigger an auth error → verify chat panel shows "The AI provider rejected the API key. Check your provider configuration." without stack traces
  - [ ] Manual E2E: Verify step sidebar shows red cross icon on error
  - [ ] Manual E2E: Verify the instance can be loaded again after error (recoverable state)

> **Note:** Manual E2E tests and `dotnet run` validation require browser testing by Adam.

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

| Component | File | Action |
|-----------|------|--------|
| `LlmErrorClassifier` | `Engine/LlmErrorClassifier.cs` | **CREATE** — static classifier, pure .NET, no dependencies |
| `StepExecutionContext` | `Engine/StepExecutionContext.cs` | **MODIFY** — add `LlmError` property |
| `StepExecutor` | `Engine/StepExecutor.cs` | **MODIFY** — call classifier in catch block |
| `WorkflowOrchestrator` | `Engine/WorkflowOrchestrator.cs` | **MODIFY** — use classified error in run.error emission |
| `shallai-instance-detail` | `Client/src/components/shallai-instance-detail.element.ts` | **MODIFY** — remove "Error:" prefix from system message |
| `LlmErrorClassifierTests` | `Tests/Engine/LlmErrorClassifierTests.cs` | **CREATE** — ~9 classification tests |
| `StepExecutorTests` | `Tests/Engine/StepExecutorTests.cs` | **MODIFY** — add ~3 error classification tests |
| `WorkflowOrchestratorTests` | `Tests/Engine/WorkflowOrchestratorTests.cs` | **MODIFY** — add ~2 classified error tests |
| `SseEventTypes` | `Engine/Events/SseEventTypes.cs` | **DO NOT MODIFY** — existing RunErrorPayload is sufficient |
| `ToolLoop` | `Engine/ToolLoop.cs` | **DO NOT MODIFY** — ToolLoop rethrows LLM errors, StepExecutor catches them |
| `InstanceManager` | `Instances/InstanceManager.cs` | **DO NOT MODIFY** — atomic write behaviour unchanged |

### Architecture Compliance

- **Engine has ZERO Umbraco dependencies** — LlmErrorClassifier is pure .NET, classifies from base exception types only
- **No provider SDK imports** — classification uses HttpRequestException.StatusCode, TaskCanceledException, and exception message string matching. Never import Anthropic or OpenAI types.
- **Atomic writes preserved** — step status updates use existing InstanceManager path (write to .tmp, File.Move)
- **Structured logging preserved** — existing Error-level logging with WorkflowAlias, InstanceId, StepId remains. The original exception is logged, not the user-facing message.
- **Tool errors unchanged** — ToolExecutionException in ToolLoop is returned to the LLM as an error result. This story only classifies errors that terminate the step (propagate up from ToolLoop to StepExecutor).

### Key Design Decisions

**Why a static classifier instead of an interface:**
- Classification is a pure function — takes an exception, returns a code + message. No state, no dependencies, no DI needed.
- Adding an `ILlmErrorClassifier` interface for a single static method would be unnecessary abstraction (project-context.md: "Three similar lines of code is better than a premature abstraction").
- Easy to test — pass exception, assert result.

**Why classify in StepExecutor, not ToolLoop:**
- ToolLoop's responsibility is streaming and tool execution. It rethrows LLM errors — that's the right boundary.
- StepExecutor is the catch boundary for step-level failures. Classification at this level captures ALL LLM errors regardless of which ToolLoop iteration they occurred in.
- Separating classification from emission (StepExecutor classifies, Orchestrator emits) keeps each layer's responsibility clean.

**Why store classification on StepExecutionContext:**
- StepExecutionContext is already the shared state object passed between StepExecutor and Orchestrator.
- Adding a nullable property is the simplest way to communicate the classification without changing method signatures or return types.
- The orchestrator already reads context state — this follows the existing pattern.

**Why string matching for rate limit / auth errors:**
- `HttpRequestException.StatusCode` is nullable and may not be set by all providers.
- Provider SDKs (Anthropic, OpenAI) may wrap HTTP errors in their own exception types. The message often contains "rate limit", "unauthorized", etc.
- String matching on the message is a pragmatic fallback that works across providers without importing their SDKs.
- Order matters: check StatusCode first (reliable), then message (fallback).

**Why TaskCanceledException for timeout vs OperationCanceledException for user cancel:**
- `TaskCanceledException` inherits from `OperationCanceledException`. HTTP timeouts throw `TaskCanceledException`.
- User cancellation sets `CancellationToken.IsCancellationRequested = true`.
- Classification checks: if the token is cancelled → not an LLM error (rethrow). If not cancelled → timeout.
- The existing OperationCanceledException catch in StepExecutor (line 188) fires first — the classifier only sees exceptions that pass that guard.

### Error Code Reference Table

| Error Code | Trigger | User Message |
|------------|---------|-------------|
| `rate_limit` | HTTP 429 or "rate limit" in message | "The AI provider returned a rate limit error. Wait a moment and retry." |
| `timeout` | TaskCanceledException (not user cancel) | "The AI provider timed out. Check your provider configuration and retry." |
| `auth_error` | HTTP 401/403 or "unauthorized"/"forbidden" in message | "The AI provider rejected the API key. Check your provider configuration." |
| `provider_error` | Any other HttpRequestException or unrecognised exception | "The AI provider returned an error. Check your provider configuration and retry." |
| `step_failed` | Non-LLM error (validation, completion check) | "Step '{name}' failed" |

### Failure & Edge Cases

1. **LLM provider throws an exception type we haven't seen:** The default classification is `"provider_error"` with a generic user message. No crash, no unhandled exception. The original exception is logged at Error level for debugging. **Handled by default case.**

2. **HttpRequestException with no StatusCode:** Some HttpClient configurations don't set StatusCode. The classifier falls through to message string matching, then to the default `"provider_error"`. **Handled by fallback chain.**

3. **TaskCanceledException from user cancellation:** The existing `catch (OperationCanceledException)` at line 188 in StepExecutor fires before the general catch. The classifier never sees user cancellations. **Already handled by existing code — no change needed.**

4. **Exception during step status update after classification:** The try-catch around `UpdateStepStatusAsync` (lines 198-208) already handles this — logs at Critical level. Classification metadata on context is still set even if the status update fails. The orchestrator may not see the error status in instance state, but it checks the step status independently. **Already handled.**

5. **Concurrent step execution race on error:** Instance concurrency prevention (one step per instance at a time) is enforced at the orchestrator level. No race condition on error classification. **Already handled by existing guard.**

6. **Exception message in a non-English locale:** Provider SDKs may return localised error messages. String matching for "rate limit", "unauthorized" etc. will fail. Classification falls to `"provider_error"` default — still a reasonable user message. The original exception is logged for debugging. **Acceptable degradation.**

7. **ToolLoop tool error escalates to step failure:** If a tool execution itself throws an exception that isn't ToolExecutionException (e.g., unhandled error), ToolLoop rethrows it. StepExecutor catches it, classifier classifies it as `"provider_error"` (default). This is acceptable — the original exception is logged. **Handled by default case.**

8. **Multiple errors in a single step (e.g., retry within ToolLoop):** ToolLoop does not retry internally. Each step execution has exactly one error path. If the LLM call fails, the exception propagates immediately. **Not applicable.**

### Review Findings

- [x] [Review][Decision] TaskCanceledException with HttpClient-internal cancelled token may misclassify timeout as user cancellation — Fixed: reordered classifier to check TCE before OCE, removed token condition from TCE check since StepExecutor already filters user cancellation. Added test for TCE with cancelled internal token. [LlmErrorClassifier.cs]
- [x] [Review][Patch] Missing regression test for OCE rethrow after catch filter narrowing — Fixed: added `OperationCanceledException_WithCancelledToken_Rethrows` test. [StepExecutorTests.cs]
- [x] [Review][Patch] Unnecessary template literal wrapper in frontend error display — Fixed: removed backtick wrapper. [shallai-instance-detail.element.ts:642]
- [x] [Review][Defer] No inner exception inspection in classifier — provider SDKs may wrap HttpRequestException in their own types. Walking `InnerException` would improve classification but is a pre-existing gap, not introduced by this change. [LlmErrorClassifier.cs:17-41] — deferred, pre-existing

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- TaskCanceledException test initially failed because existing `catch (OperationCanceledException) { throw; }` caught it before the general catch. Fixed by adding `when (cancellationToken.IsCancellationRequested)` filter to only rethrow user cancellations, letting timeout exceptions fall through to classification.

### Completion Notes List

- Created `LlmErrorClassifier` as a pure static class — classifies exceptions into rate_limit, timeout, auth_error, or provider_error with user-friendly messages. Returns null for user cancellation (rethrow path).
- Integrated classifier into StepExecutor catch block via `context.LlmError` property on `StepExecutionContext`.
- Narrowed `OperationCanceledException` catch to `when (cancellationToken.IsCancellationRequested)` so timeout `TaskCanceledException` reaches the classifier.
- Updated WorkflowOrchestrator to emit classified error code/message in `run.error` event, falling back to `"step_failed"` for non-LLM errors.
- Removed `"Error: "` prefix from frontend system message display — messages are now self-contained.
- All 308 backend tests pass (16 new: 10 classifier, 4 StepExecutor, 2 orchestrator). Frontend builds cleanly.
- Manual E2E validation (TestSite startup, error scenarios) deferred to Adam for browser testing.

### Change Log

- 2026-04-02: Implemented LLM error classification and reporting — Tasks 1-8 complete (automated tests all pass, manual E2E deferred)
- 2026-04-02: Code review fixes — reordered TCE/OCE checks in classifier (timeout misclassification bug), added OCE rethrow regression test, removed unnecessary template literal, added TCE-with-cancelled-token test. 308 tests pass.

### File List

- `Shallai.UmbracoAgentRunner/Engine/LlmErrorClassifier.cs` — **CREATED** — static error classifier
- `Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs` — **MODIFIED** — added `LlmError` property
- `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs` — **MODIFIED** — call classifier in catch, narrowed OCE catch filter
- `Shallai.UmbracoAgentRunner/Engine/WorkflowOrchestrator.cs` — **MODIFIED** — use classified error in run.error emission
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` — **MODIFIED** — removed "Error:" prefix, removed unnecessary template literal
- `Shallai.UmbracoAgentRunner.Tests/Engine/LlmErrorClassifierTests.cs` — **CREATED** — 10 classification unit tests
- `Shallai.UmbracoAgentRunner.Tests/Engine/StepExecutorTests.cs` — **MODIFIED** — added 4 tests (3 error classification + 1 OCE rethrow regression)
- `Shallai.UmbracoAgentRunner.Tests/Engine/WorkflowOrchestratorTests.cs` — **MODIFIED** — added 2 classified error emission tests
