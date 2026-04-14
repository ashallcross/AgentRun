# Story 10.9: SSE Disconnect Resilience

Status: done

**Depends on:** Story 10.8 (CTS per instance + SSE OCE handler branching — already done)
**Branch:** `feature/epic-10`
**Priority:** 4th in Epic 10 (10.2 done → 10.12 done → 10.8 done → **10.9** → 10.10 → 10.1 → 10.6 → 10.11 → 10.7 → 10.4 → 10.5)

> **Beta known issue — listed in epics.md §"Story 10.9".** When the SSE connection drops (browser tab close, network blip, F5 refresh, proxy idle timeout), `OperationCanceledException` propagates to [`ExecutionEndpoints.ExecuteSseAsync`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174) and the Running-branch writes `InstanceStatus.Failed`. A run that was progressing normally is persisted as a failure. The user sees a `Failed` badge and a Retry button for a run that was never actually broken. This story distinguishes **client disconnect** from **execution failure** so dropped connections mark runs `Interrupted` (recoverable) rather than `Failed` (broken).

## Story

As a backoffice user,
I want a dropped SSE connection to not kill my running workflow,
So that closing a browser tab or a network interruption doesn't destroy a run that was progressing normally.

## Context

**UX Mode: N/A** — engine + endpoint plumbing change with minor frontend surfacing (new status label + colour + Retry-gate extension). No new UI affordances.

**The bug being fixed:**

The OCE catch in [`ExecutionEndpoints.ExecuteSseAsync`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174) currently has two branches (after Story 10.8):

1. **Cancelled branch** (persisted status == `Cancelled`) → clean return, emits `run.finished(Cancelled)`.
2. **All-other-OCE branch** → writes `InstanceStatus.Failed` and rethrows.

The catch-all treats client disconnect (the OCE that fires when `HttpContext.RequestAborted` cancels) as a hard failure. This is the main bug. A browser tab close, network hiccup, or proxy idle timeout while a run is happily streaming causes the persisted status to flip to `Failed`. The Retry flow is designed for genuine failures (it truncates the last assistant message, assumes there's a step with `StepStatus.Error`) — retrying a disconnect-induced `Failed` state is semantically wrong.

**The fix shape (minimum fix per epic):**

- Add a new `InstanceStatus.Interrupted` enum value. Semantics: "run was progressing when the SSE stream dropped; can be retried."
- In the SSE OCE handler, between the existing `Cancelled` branch and the `Failed` fallback, add a **disconnect branch**: if `cancellationToken.IsCancellationRequested` is `true` (the HTTP request token fired — the only disconnect signal available to this handler), persist `Interrupted` instead of `Failed`, return `EmptyResult` cleanly (do not rethrow — same reasoning as the post-10.8 Cancelled branch).
- Extend [`RetryInstance`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L109) to accept `Interrupted` alongside `Failed`, with step-discovery that finds `StepStatus.Active` (the orphan left by a mid-stream disconnect) in addition to `StepStatus.Error`, and **does not truncate the JSONL** for the `Interrupted` path (there was no failed assistant message — the stream was torn down mid-response, not after a recorded error).
- Surface `Interrupted` in the frontend status helpers and the detail-view Retry gate.

**The full fix (v2) is explicitly out of scope.** The code review (Codex) recommended "move execution to a background service so the SSE stream observes execution rather than owning it." That's a significant architectural change — an `IHostedService` background runner, an event bus between orchestrator and SSE, durable queue semantics. Per epic placement, this is deferred to post-beta. Story 10.9 gets only the minimum-fix delta.

### Architect's locked decisions (do not relitigate)

1. **`Interrupted` is a new `InstanceStatus` enum value, not a boolean flag on `InstanceState`.** The enum is the surface both APIs and UI consume. Adding a value is additive — existing YAML files (which don't use it) deserialize unchanged; new writes serialize as `"Interrupted"`. Do NOT add a sidecar `bool WasInterrupted`.
2. **Disconnect discrimination uses `cancellationToken.IsCancellationRequested` on the controller parameter.** The controller token comes from `HttpContext.RequestAborted`. When the client disconnects, this fires. When the cancel endpoint is called, this does NOT fire (the per-instance CTS fires instead, and the persisted status becomes `Cancelled` which the earlier branch catches). When a provider-internal timeout fires (e.g., an `HttpClient` operation timeout deep inside M.E.AI), the controller token is NOT cancelled — those OCEs are correctly classified as `Failed`. Do NOT inspect the exception's inner `CancellationToken` field; the controller token is the single source of truth for "did the client go away."
3. **`Interrupted` is NOT a terminal state in the `SetInstanceStatusAsync` guard.** The Story 10.8 terminal-transition guard refuses moves out of `Completed|Failed|Cancelled`. `Interrupted` is a **recoverable paused** state — Retry transitions it to `Running`, which must be allowed. Do NOT add `Interrupted` to the terminal list at [InstanceManager.cs:218](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L218).
4. **`Interrupted` IS eligible for delete.** The `DeleteInstance` endpoint currently accepts `Completed|Failed|Cancelled`. Add `Interrupted` — users need an exit other than Retry. The terminal-status guard for delete is separate from the transition guard.
5. **Cancel endpoint rejects `Interrupted` via existing 409 guard** ([InstanceEndpoints.cs:99](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L99)). The guard is already `not (Running or Pending)` — `Interrupted` falls into the reject path. No change needed; this is correct behaviour (you can't cancel a run that isn't running).
6. **Retry handles `Interrupted` without JSONL truncation.** The existing [`RetryInstance`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L148) path calls `TruncateLastAssistantEntryAsync` to remove the failed assistant message before replay. For `Interrupted`, there is no failed assistant message — the stream was torn down mid-transmission, and the in-progress content was never committed to JSONL (conversation recording happens at final boundaries, not on stream increments). Branch the logic: `if (status == Failed) { truncate }`, skip truncate for `Interrupted`. Story 10.6 owns deeper replay-shape refinement — 10.9 should not pre-empt it.
7. **Step-discovery for Retry(Interrupted) finds `StepStatus.Active`, not `StepStatus.Error`.** Mid-stream disconnect leaves the in-flight step with `Active` status. The existing `Steps.FindIndex(s => s.Status == StepStatus.Error)` returns -1. Change the discovery branch: for `Failed`, find `Error`; for `Interrupted`, find `Active`. If neither is found, 409 with a clear error code (existing pattern preserved).
8. **Do NOT emit `run.finished(Interrupted)` as an SSE event.** The Cancelled branch emits `run.finished(Cancelled)` because non-initiating observers (e.g., a second browser tab watching the same run via a future observer endpoint) need to distinguish cancel from drop. The disconnect branch has no such requirement — the disconnect IS the drop, there is no one left on the stream to receive it, and attempting to emit on a half-closed stream produces log noise (see the `EmitRunFinishedAsync` try/catch in the Cancelled branch — keep that pattern but do not add it here). If a future observer model lands, revisit.
9. **No new `Resume` button, no new `/resume` endpoint.** The Retry button already exists, works, and maps to the correct user intent for both `Failed` and `Interrupted`. The label stays "Retry" — do not introduce "Resume" for this release. UX polish (conditional label/help text) is out of scope.
10. **Story 10.9 does NOT refactor the orphaned `StepStatus.Active` on cancel.** That's Story 10.10's explicit pickup from the 10.8 code review. 10.9's Retry(Interrupted) branch resets the Active step to Pending as a side effect of its recovery — this is correct for the Retry path, but the general "clean up Active on terminal transition" concern stays with 10.10.

## Acceptance Criteria (BDD)

### AC1: Disconnect-path OCE persists `Interrupted` not `Failed`

**Given** an instance is running a step (status `Running`, active in `ActiveInstanceRegistry`)
**And** no cancel endpoint has been called for this instance
**When** the SSE client disconnects (browser close / network drop / F5), causing `cancellationToken` (the controller `HttpContext.RequestAborted` token) to fire
**And** `OperationCanceledException` propagates into [`ExecutionEndpoints.ExecuteSseAsync`'s catch block](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174)
**Then** the OCE handler reads the current persisted status via a fresh `FindInstanceAsync`
**And** sees the status is not `Cancelled` (existing 10.8 branch is skipped)
**And** observes `cancellationToken.IsCancellationRequested == true`
**And** persists `InstanceStatus.Interrupted` (not `Failed`)
**And** logs at Information level: `"SSE client disconnected for instance {InstanceId}; marking Interrupted"`
**And** returns `new EmptyResult()` without rethrowing (no unhandled-exception log for a non-failure)

### AC2: Internal OCE (not disconnect) still writes `Failed`

**Given** an instance is running
**And** no cancel endpoint has been called
**When** an `OperationCanceledException` propagates into the OCE handler **but the controller `cancellationToken` is NOT cancelled** (e.g., a provider-internal timeout from a nested `HttpClient` operation, or any future engine-domain cancellation source not derived from the HTTP or per-instance tokens)
**Then** the handler persists `InstanceStatus.Failed` (existing behaviour preserved)
**And** rethrows the OCE (existing behaviour preserved — this is a genuine failure path)

### AC3: Cancelled branch is unchanged

**Given** the cancel endpoint has persisted `Cancelled` and signalled the per-instance CTS (Story 10.8 flow)
**When** OCE propagates into the handler
**Then** the Cancelled branch runs unchanged — emit `run.finished(Cancelled)`, log the "Cancellation observed…" Information entry, return `EmptyResult`
**And** the disconnect discrimination (AC1) is NOT reached

### AC4: `InstanceStatus.Interrupted` exists and round-trips through serialization

**Given** the `InstanceStatus` enum
**When** `Interrupted` is added as a new value (after `Cancelled`)
**Then** `JsonSerializer.Serialize` produces `"Interrupted"` (via the existing `JsonStringEnumConverter`)
**And** YAML serialization writes `interrupted` (lower-case via `UnderscoredNamingConvention`) or `Interrupted` per the existing serializer's default behaviour — pinned by a unit test that round-trips state through `InstanceManager.WriteStateAtomicAsync` / `ReadStateAsync`
**And** API response models (`InstanceResponse`, `InstanceDetailResponse`) carry the new value unchanged
**And** existing YAML files without the new value deserialize unchanged (forward-compatible)

### AC5: Retry accepts `Interrupted`

**Given** an instance with status `Interrupted` (and at least one step with `StepStatus.Active`)
**When** `POST /umbraco/api/agentrun/instances/{id}/retry` is called
**Then** the existing `instance.Status != Failed` guard at [`RetryInstance`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L125) is extended to `not (Failed or Interrupted)` — the Interrupted call passes the gate
**And** step-discovery branches: if status is `Failed`, find `StepStatus.Error`; if status is `Interrupted`, find `StepStatus.Active`
**And** when step is `Interrupted`, **do NOT call** `TruncateLastAssistantEntryAsync` (there is no recorded failed assistant message to remove)
**And** reset the Active step to `StepStatus.Pending` (same pattern as the existing Failed path)
**And** transition the instance to `Running` and stream a fresh SSE response
**And** the client observes `run.started` on the new stream exactly as a Retry(Failed) would

### AC6: Retry on `Interrupted` with no Active step returns 409

**Given** an instance with status `Interrupted` but no step has `StepStatus.Active` (a pathological state — e.g., the Interrupted was persisted after all steps had already finished)
**When** Retry is called
**Then** the endpoint returns 409 with `Error = "invalid_state"` and a message pointing to the missing Active step
**And** no status mutation or SSE stream is started

### AC7: Delete accepts `Interrupted`

**Given** an instance with status `Interrupted`
**When** `DELETE /umbraco/api/agentrun/instances/{id}` is called
**Then** the existing terminal-status check at [`InstanceEndpoints.cs:135`](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L135) is extended to include `Interrupted`
**And** the corresponding guard in [`InstanceManager.DeleteInstanceAsync`](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L258) is also extended
**And** the delete completes as today (204 No Content, instance folder removed)

### AC8: Cancel rejects `Interrupted` via existing 409

**Given** an instance with status `Interrupted`
**When** the cancel endpoint is called
**Then** the existing [`InstanceEndpoints.CancelInstance`](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L99) guard `not (Running or Pending)` rejects with 409
**And** no CTS signal is attempted
**And** the error message indicates Interrupted is not a running/pending state
**Note:** this AC is defensive — it documents that the existing guard already does the right thing. No code change is required for this AC; it's validated by a unit test.

### AC9: StartInstance rejects `Interrupted`

**Given** an instance with status `Interrupted`
**When** `POST /.../start` is called
**Then** the existing [`StartInstance`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L64) terminal-status guard is extended from `Completed|Failed|Cancelled` to `Completed|Failed|Cancelled|Interrupted`
**And** the endpoint returns 409 with the existing `invalid_status` error code
**And** the user must use Retry, not Start, to resume the run

### AC10: `SetInstanceStatusAsync` terminal guard does NOT include `Interrupted`

**Given** the terminal-transition guard at [`InstanceManager.cs:218`](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L218)
**When** Retry transitions the instance from `Interrupted` to `Running`
**Then** the guard must allow the transition (Interrupted is NOT in the refusal set)
**And** the test covering this case explicitly asserts Interrupted→Running is allowed
**And** the guard still refuses transitions out of `Completed|Failed|Cancelled` (existing 10.8 behaviour preserved)

### AC11: Frontend status helpers render `Interrupted`

**Given** the frontend instance-status helper functions in [`instance-list-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts)
**When** an instance with `status === "Interrupted"` is rendered
**Then** `displayStatus("Interrupted", mode)` returns the string `"Interrupted"` (both modes — same label for interactive and autonomous)
**And** `statusColor("Interrupted", mode)` returns a distinguishable colour (`"warning"` — amber/orange, semantically "attention needed but not broken")
**And** `isTerminalStatus("Interrupted")` returns `false` (Interrupted is recoverable, not terminal — the UI uses this to decide whether the chat input and other controls go into a "completed" state)

### AC12: Frontend Retry gate includes `Interrupted`

**Given** the Retry button render logic in [`agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L916)
**When** `inst.status === "Interrupted"` and `!this._streaming`
**Then** `showRetry` evaluates true (the gate is extended from `status === "Failed"` to `(status === "Failed" || status === "Interrupted")`)
**And** the button label remains `"Retry"` (no new "Resume" label — locked decision 9)

### AC13: Frontend input placeholder for `Interrupted`

**Given** the input-enablement logic in [`agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L931)
**When** `inst.status === "Interrupted"` and the client is not currently streaming
**Then** the chat input is disabled (`inputEnabled = false`)
**And** the placeholder reads: `"Run interrupted — click Retry to resume."` (not `"Workflow complete"` — that's the terminal-state placeholder and would mislead)
**And** the cancel button is hidden (`showCancel` stays `Running || Pending`, which excludes Interrupted — locked decision 5, no change needed here, just validate via test)

### AC14: Client `_streamSseResponse` catch does not corrupt state on disconnect

**Given** the client SSE reader loop in [`_streamSseResponse`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L433)
**When** the reader errors because the server closed the stream (disconnect flow)
**Then** the existing catch block runs (adds `"Connection lost."` system message, finally-block calls `_loadData`)
**And** `_loadData` fetches the instance and sees `status === "Interrupted"`
**And** the re-render surfaces Retry (AC12), disables the input with the new placeholder (AC13), and hides the cancel button
**And** no further client-side changes are required to this flow

## Files in Scope

| File | Change |
|---|---|
| [AgentRun.Umbraco/Instances/InstanceStatus.cs](../../AgentRun.Umbraco/Instances/InstanceStatus.cs) | Add `Interrupted` enum value after `Cancelled`. Keep `[JsonStringEnumConverter]` attribute — serializes as `"Interrupted"`. |
| [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs) | (a) `ExecuteSseAsync` OCE handler: insert the disconnect branch between the existing Cancelled branch and the Failed fallback. (b) `StartInstance`: extend the terminal-status guard at line 64 to include `Interrupted`. (c) `RetryInstance`: extend the gate at line 125 to accept `Interrupted`; branch step-discovery between `Error` and `Active`; skip JSONL truncation for the Interrupted path. |
| [AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs) | `DeleteInstance` guard at line 135: include `Interrupted` in the deletable set. Cancel guard at line 99 is already correct (no change needed, covered by AC8 test). |
| [AgentRun.Umbraco/Instances/InstanceManager.cs](../../AgentRun.Umbraco/Instances/InstanceManager.cs) | `DeleteInstanceAsync` guard at line 258: include `Interrupted` in the deletable set. **Do NOT** add Interrupted to the terminal-transition guard at line 218 (locked decision 3). |
| [AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts](../../AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts) | Extend `displayStatus` (new `case "Interrupted": return "Interrupted";`), `statusColor` (new `case "Interrupted": return "warning";`), and `isTerminalStatus` (do NOT add — returns `false` by default). |
| [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) | (a) `showRetry` gate: extend from `status === "Failed"` to `(status === "Failed" \|\| status === "Interrupted")`. (b) `inputEnabled`/`inputPlaceholder` block: add an `Interrupted` branch with the new placeholder string. Keep `showCancel` unchanged (Interrupted is not in `Running \|\| Pending`). Keep `isTerminal` unchanged — the three-state terminal predicate stays as is; Interrupted-specific behaviour lives in the new branches. |
| [AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs) | Add: (a) `Start_OceWithRunningStatus_ClientDisconnect_WritesInterruptedAndReturnsEmpty` (pre-cancelled token, expect Interrupted + EmptyResult, no rethrow). (b) Update / re-examine the existing `Start_OceWithRunningStatus_WritesFailedAndRethrows` to ensure its `cancellationToken` parameter is NOT cancelled (it's already `CancellationToken.None`, so the existing assertion stands — but add an inline comment clarifying the Story 10.9 discrimination). (c) `Start_OceWithCancelledStatus_…` stays unchanged (10.8 contract preserved). (d) `Retry_OnInterrupted_ResetsActiveStepAndStreams`. (e) `Retry_OnInterrupted_NoActiveStep_Returns409`. (f) `Retry_OnInterrupted_DoesNotTruncateConversation`. |
| [AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs) | Add: (a) `Cancel_OnInterrupted_Returns409` (defensive AC8 test). (b) `Delete_OnInterrupted_Returns204` (AC7). |
| [AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs](../../AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs) | Add: (a) `SetInstanceStatusAsync_InterruptedToRunning_Allowed` (AC10 — the transition guard must NOT include Interrupted). (b) `RoundTrip_Interrupted_ThroughYaml` (AC4 — serialization sanity). (c) `DeleteInstanceAsync_OnInterrupted_Succeeds` (AC7 engine-side). |
| [AgentRun.Umbraco.Tests/Instances/InstanceStatusSerializationTests.cs](../../AgentRun.Umbraco.Tests/Instances/) (NEW if not present; check first — may live in an existing test file) | Assert `JsonSerializer.Serialize(InstanceStatus.Interrupted)` → `"Interrupted"` and round-trips back. If an analogous test file already exists for other enum values, extend it rather than creating a new file. |
| [AgentRun.Umbraco/Client/src/utils/instance-list-helpers.test.ts](../../AgentRun.Umbraco/Client/src/utils/) (check pattern) | Add `displayStatus` / `statusColor` / `isTerminalStatus` tests for `"Interrupted"` (AC11). If test file doesn't exist yet, follow the frontend-test pattern already in the repo — otherwise extend. |
| [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts) | Add: (a) Retry button renders when status is Interrupted (AC12). (b) Cancel button does NOT render when status is Interrupted (AC13 defensive). (c) Input placeholder reads the new copy when status is Interrupted (AC13). |

**Files explicitly NOT touched:**

- [AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) — the linked-token plumbing is already correct (Story 10.8). 10.9 reacts to the OCE at the SSE boundary; it does not change how the token is built, passed, or disposed.
- [AgentRun.Umbraco/Engine/ToolLoop.cs](../../AgentRun.Umbraco/Engine/ToolLoop.cs) — no tool-layer changes. 10.9 is purely about status classification at the catch site.
- [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs) — registry contract unchanged.
- [AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs:99 (Cancel guard)](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L99) — the existing `not (Running or Pending)` already rejects Interrupted correctly (AC8).
- `AgentRun.Umbraco/Instances/StepStatus.cs` — no new step statuses. 10.9 uses the existing `Active` as the discriminator for Retry(Interrupted).
- `AgentRun.Umbraco/Instances/ConversationStore.cs` — JSONL is untouched. Cancelled/interrupted runs leave the log in whatever shape execution reached.
- `AgentRun.Umbraco/Engine/Events/SseEventTypes.cs` — no new event types. The disconnect path is intentionally silent (locked decision 8 — the client is gone, there is no one to receive an event).
- Any background-execution scaffolding — the full fix (hosted service + observer model) is post-beta. Do NOT introduce `IHostedService` infrastructure in this story.

## Tasks

### Task 1: Add `Interrupted` to `InstanceStatus` enum (AC4)

- [x] Add `Interrupted` after `Cancelled` in [InstanceStatus.cs](../../AgentRun.Umbraco/Instances/InstanceStatus.cs).

1. Open [`AgentRun.Umbraco/Instances/InstanceStatus.cs`](../../AgentRun.Umbraco/Instances/InstanceStatus.cs).
2. Add a new line after `Cancelled,`: `Interrupted`.
3. The `[JsonStringEnumConverter]` attribute on the enum handles JSON serialization automatically — no per-value attributes needed.
4. Build the project (`dotnet build AgentRun.Umbraco.slnx`) to surface any exhaustive-switch warnings from the compiler. C# `switch` expressions with no default branch will warn when a new enum value is added — audit each warning and decide explicitly (most will resolve by letting the warning pass because they use `is`-patterns or `default` branches; any that do not must be updated to handle `Interrupted`).

**Thread-safety / concurrency:** none — enum addition is a pure type-system change.

### Task 2: SSE OCE handler — disconnect branch (AC1, AC2, AC3)

- [x] Modify [`ExecutionEndpoints.ExecuteSseAsync`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174) to insert the disconnect branch between the existing Cancelled branch and the Failed fallback.

1. Leave the Cancelled branch at lines 186–213 unchanged (it handles AC3).
2. After the Cancelled branch's `return new EmptyResult();`, add:
   ```csharp
   if (current is not null && cancellationToken.IsCancellationRequested)
   {
       _logger.LogInformation(
           "SSE client disconnected for instance {InstanceId}; marking Interrupted",
           instance.InstanceId);

       try
       {
           await _instanceManager.SetInstanceStatusAsync(
               instance.WorkflowAlias, instance.InstanceId,
               InstanceStatus.Interrupted, CancellationToken.None);
       }
       catch (Exception statusEx)
       {
           _logger.LogCritical(statusEx,
               "Failed to set instance {InstanceId} status to Interrupted after disconnect",
               instance.InstanceId);
       }

       return new EmptyResult();
   }
   ```
3. Leave the existing Failed fallback (lines 215–228) unchanged — it now handles AC2 (internal OCE where the controller token did not fire).
4. **Do NOT emit an SSE event in the disconnect branch** (locked decision 8). The client is gone; the emitter call would throw on a closed stream and the log noise is pure cost.
5. **Do NOT rethrow** from the disconnect branch — same reasoning as the post-10.8 Cancelled branch (amendment 1.2 in the 10.8 change log): rethrowing a deliberately-handled OCE surfaces as an unhandled controller exception with a 100-line stack trace per disconnect. The minimum-noise policy applies.

### Task 3: Extend `StartInstance` terminal-status guard (AC9)

- [x] Modify [`StartInstance`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L64) terminal check.

1. Change line 64 from:
   ```csharp
   if (instance.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled)
   ```
   to:
   ```csharp
   if (instance.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled or InstanceStatus.Interrupted)
   ```
2. The existing 409 response body is fine (error code `invalid_status`) — Interrupted maps naturally.

### Task 4: Extend `RetryInstance` to accept `Interrupted` (AC5, AC6)

- [x] Modify [`RetryInstance`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L113) to branch on status.

1. Change the gate at line 125 from:
   ```csharp
   if (instance.Status != InstanceStatus.Failed)
   ```
   to:
   ```csharp
   if (instance.Status is not (InstanceStatus.Failed or InstanceStatus.Interrupted))
   ```
2. Replace the step-discovery block at lines 134–143 with a status-conditional version:
   ```csharp
   var stepStatusToFind = instance.Status == InstanceStatus.Failed
       ? StepStatus.Error
       : StepStatus.Active;

   var stepIndex = instance.Steps.FindIndex(s => s.Status == stepStatusToFind);
   if (stepIndex == -1)
   {
       return Conflict(new ErrorResponse
       {
           Error = "invalid_state",
           Message = instance.Status == InstanceStatus.Failed
               ? "No step in error state found"
               : "No active step found to resume"
       });
   }

   var targetStep = instance.Steps[stepIndex];
   ```
3. Conditionally truncate the JSONL — only for `Failed`:
   ```csharp
   if (instance.Status == InstanceStatus.Failed)
   {
       await _conversationStore.TruncateLastAssistantEntryAsync(
           instance.WorkflowAlias, instance.InstanceId, targetStep.Id, cancellationToken);
   }
   ```
4. The remaining reset-and-stream steps (lines 152–159) stay unchanged — reset step status to Pending, set instance to Running, return `ExecuteSseAsync(instance, cancellationToken)`. These apply identically to both Failed and Interrupted paths.

**Why no JSONL truncation for Interrupted:** the disconnect happened during streaming or tool execution; the `ConversationRecorder` writes on completion boundaries (`RecordAssistantMessageAsync` is called AFTER a full message completes, not on each delta). So there is no "last assistant message" recording a failure to remove. Truncation would either be a no-op or — in a race — accidentally remove a legitimate prior assistant message. Skip the truncate.

### Task 5: Extend delete guards (AC7)

- [x] Add `Interrupted` to both the endpoint and engine delete guards.

1. [`InstanceEndpoints.DeleteInstance`](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L135), change:
   ```csharp
   if (state.Status is not (InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled))
   ```
   to:
   ```csharp
   if (state.Status is not (InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled or InstanceStatus.Interrupted))
   ```
2. [`InstanceManager.DeleteInstanceAsync`](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L258), apply the same change.
3. **Do NOT** change the terminal-transition guard at [`InstanceManager.cs:218`](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L218) — locked decision 3. Interrupted must be transitionable-out-of (Retry needs Interrupted→Running).

### Task 6: Frontend status helpers (AC11)

- [x] Modify [`instance-list-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts).

1. `displayStatus`: add `case "Interrupted": return "Interrupted";` — both modes show the same label.
2. `statusColor`: add `case "Interrupted": return "warning";` — amber/orange, distinct from the `"danger"` used for Failed (autonomous mode).
3. `isTerminalStatus`: **do NOT** add `Interrupted` — the function returns `false` for unknown values by default, which is the correct answer. Interrupted is a recoverable state, not a terminal state, from the UI's perspective. Adding it here would be a bug — `isTerminalStatus` is used to gate the `"Workflow complete"` input state, which is wrong for Interrupted.

### Task 7: Frontend Retry gate + input placeholder (AC12, AC13)

- [x] Modify [`agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts).

1. Change the `showRetry` computation at line 916:
   ```ts
   const showRetry = (inst.status === "Failed" || inst.status === "Interrupted") && !this._streaming;
   ```
2. In the input-enablement block (lines 933–956), add an Interrupted branch inside the interactive case, **before** the `_viewingStepId` / `_agentResponding` / `_streaming` branches:
   ```ts
   if (inst.status === "Interrupted") {
       inputEnabled = false;
       inputPlaceholder = "Run interrupted — click Retry to resume.";
   } else if (isTerminal) {
       // existing branch
   }
   ```
3. The autonomous branch (line 951–956) should also handle Interrupted — override the current `inputPlaceholder` ternary to surface the new copy when status is Interrupted. Simplest option: hoist the Interrupted check above the isInteractive switch so it applies to both modes:
   ```ts
   let inputEnabled: boolean;
   let inputPlaceholder: string;
   if (inst.status === "Interrupted") {
       inputEnabled = false;
       inputPlaceholder = "Run interrupted — click Retry to resume.";
   } else if (isInteractive) {
       // existing interactive block
   } else {
       // existing autonomous block
   }
   ```
4. `showCancel` at line 928 stays unchanged — Interrupted is not `Running || Pending`, so cancel is correctly hidden (locked decision 5). Add a test to pin this, but no code change.
5. `isTerminal` at line 907 stays unchanged — the current three-state definition (`Completed || Failed || Cancelled`) is used only for the "Workflow complete" placeholder and a couple of `showWorkflowComplete` gates. Surfacing Interrupted via the dedicated branch in step 3 above is cleaner than extending `isTerminal`.

### Task 8: Backend tests — OCE discrimination and Retry(Interrupted) (AC1, AC2, AC5, AC6)

- [x] Extend [`ExecutionEndpointsTests.cs`](../../AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs).

1. **`Start_OceWithRunningStatus_ClientDisconnect_WritesInterruptedAndReturnsEmpty`** — mirror the existing `Start_OceWithRunningStatus_WritesFailedAndRethrows` test but pass a pre-cancelled token to `StartInstance`. Use:
   ```csharp
   using var cts = new CancellationTokenSource();
   cts.Cancel();
   var result = await _endpoints.StartInstance("inst-001", cts.Token);
   ```
   Assert: result is `EmptyResult`; `SetInstanceStatusAsync(..., Interrupted, ...)` was called once; `SetInstanceStatusAsync(..., Failed, ...)` was NOT called; no OCE rethrow (use `Assert.DoesNotThrow`-equivalent — the test itself completes normally).
2. **`Start_OceWithRunningStatus_InternalOceNotDisconnect_WritesFailedAndRethrows`** — the existing `Start_OceWithRunningStatus_WritesFailedAndRethrows` test already covers this case (its `cancellationToken` parameter is `CancellationToken.None`, which is never cancelled). Add an inline comment linking to Story 10.9 AC2 and assert explicitly that `SetInstanceStatusAsync(..., Interrupted, ...)` was NOT called in this path.
3. **`Start_OceWithCancelledStatus_…`** — existing 10.8 test stays green as-is. Add an inline assertion: `SetInstanceStatusAsync(..., Interrupted, ...)` was not called (guard against branch ordering regression).
4. **`Retry_OnInterrupted_ResetsActiveStepAndStreams`** — build an `InstanceState` with `Status = Interrupted` and one step with `StepStatus.Active`. Call `RetryInstance`. Assert: `UpdateStepStatusAsync(..., Pending, ...)` was called for the Active step's index; `SetInstanceStatusAsync(..., Running, ...)` was called; `ExecuteNextStepAsync` was reached (SSE stream started); `TruncateLastAssistantEntryAsync` was NOT called.
5. **`Retry_OnInterrupted_NoActiveStep_Returns409`** — build an instance with `Status = Interrupted` and all steps `Pending` or `Complete` (no `Active`). Call `RetryInstance`. Assert: 409 with error code `invalid_state`; no mutation; no SSE stream.
6. **`Retry_OnInterrupted_DoesNotTruncateConversation`** — variant of test 4 that specifically asserts `IConversationStore.TruncateLastAssistantEntryAsync` was NOT called (use `.DidNotReceive()` on the mock).
7. **`Retry_OnFailed_StillTruncatesConversation`** — regression guard: the existing Failed path continues to call `TruncateLastAssistantEntryAsync`. If a test already covers this, add an assertion checking it wasn't accidentally broken by the branching.

**Test pattern reminders:** use NUnit 4 `[Test]` + `Assert.That(..., Is.EqualTo(...))`; use NSubstitute for mocking; follow the existing `_endpoints`, `_instanceManager`, `_orchestrator` fixture pattern in `ExecutionEndpointsTests.cs`.

### Task 9: Backend tests — endpoint and manager guards (AC7, AC8, AC9, AC10)

- [x] Extend [`InstanceEndpointsTests.cs`](../../AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs) and [`InstanceManagerTests.cs`](../../AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs).

1. **`Cancel_OnInterrupted_Returns409`** (InstanceEndpointsTests) — build an instance with `Status = Interrupted`; call `CancelInstance`; assert 409 with `Error = "invalid_status"`; assert `RequestCancellation` was NOT called on the registry.
2. **`Delete_OnInterrupted_Returns204`** (InstanceEndpointsTests) — build an instance with `Status = Interrupted`; call `DeleteInstance`; assert 204; assert `DeleteInstanceAsync` was called.
3. **`Start_OnInterrupted_Returns409`** (ExecutionEndpointsTests or InstanceEndpointsTests — wherever StartInstance tests live) — instance with `Status = Interrupted`; assert 409 with `invalid_status`; assert no status mutation; assert no SSE stream.
4. **`SetInstanceStatusAsync_InterruptedToRunning_Allowed`** (InstanceManagerTests) — write an instance with status Interrupted to disk; call `SetInstanceStatusAsync(..., Running, ...)`; assert the returned state has `Status = Running` and the file was updated; this pins locked decision 3.
5. **`SetInstanceStatusAsync_TerminalToAnything_Refused`** (InstanceManagerTests) — regression guard: existing 10.8 behaviour for `Completed|Failed|Cancelled` still blocks transitions out. If a test already covers this for other terminals, extend it; don't duplicate.
6. **`DeleteInstanceAsync_OnInterrupted_Succeeds`** (InstanceManagerTests) — integration-style engine test: create an instance folder, set status to Interrupted on disk, call `DeleteInstanceAsync`, assert folder removed.
7. **`RoundTrip_Interrupted_ThroughYaml`** (InstanceManagerTests) — create an instance, `SetInstanceStatusAsync(..., Interrupted, ...)`, read it back with `FindInstanceAsync`, assert `Status == Interrupted`. Pins AC4 end-to-end through the YAML serializer.
8. **Enum serialization sanity** — one-line assertion that `JsonSerializer.Serialize(InstanceStatus.Interrupted, JsonSerializerOptions.Web)` equals `"\"Interrupted\""`. Add to whichever test file already covers `InstanceStatus` JSON if one exists; otherwise add a new tiny test class `InstanceStatusTests.cs` under `AgentRun.Umbraco.Tests/Instances/`.

### Task 10: Frontend tests (AC11, AC12, AC13)

- [x] Extend the relevant frontend test files.

1. **`instance-list-helpers.test.ts`** (check for existence first — if absent, follow the existing frontend-test convention in the repo, likely under `AgentRun.Umbraco/Client/src/utils/`). Add:
   - `displayStatus("Interrupted")` returns `"Interrupted"` in both interactive and autonomous modes.
   - `statusColor("Interrupted")` returns `"warning"` in both modes.
   - `isTerminalStatus("Interrupted")` returns `false`.
2. **`agentrun-instance-detail.element.test.ts`**:
   - **Retry button renders for Interrupted** — set the instance fixture's status to `"Interrupted"`, confirm `uui-button[label="Retry"]` is present.
   - **Cancel button hidden for Interrupted** — same fixture, confirm no `uui-button[label="Cancel"]`.
   - **Input placeholder for Interrupted** — same fixture, confirm the chat panel's `input-placeholder` attribute is `"Run interrupted — click Retry to resume."`.
   - **Input disabled for Interrupted** — same fixture, confirm `input-enabled` is `false`.
3. Follow the existing `@open-wc/testing` + `describe/it/expect` pattern — do not introduce new test infrastructure.

### Task 11: Manual E2E verification

- [x] 11.1. Start a fresh Content Audit instance against the Clean Starter Kit. Wait for the scanner step to be mid-batch (3–5 `get_content` calls completed, more in flight).
- [x] 11.2. **Close the browser tab** (Cmd+W on macOS). Do not click Cancel.
- [x] 11.3. Re-open the Umbraco backoffice, navigate to the instance list. Observe the instance's badge — it must read **Interrupted** (amber/warning colour), NOT Failed (red).
- [x] 11.4. Open the instance detail view. Confirm: no Cancel button; Retry button is visible; chat input is disabled with placeholder "Run interrupted — click Retry to resume."; the "Connection lost." system message (or similar) appears in the chat transcript (from the client-side SSE catch).
- [x] 11.5. Inspect server logs: expect `[INF] SSE client disconnected for instance {InstanceId}; marking Interrupted` (Information level). **No** `[CRT] Failed to set instance ... status to Failed` entries should appear. No 100-line unhandled-exception stack traces.
- [x] 11.6. Click Retry. Confirm a fresh SSE stream starts, `run.started` fires, and the interrupted step resumes (in practice: the orchestrator re-executes from the start of the `Active` step — the JSONL has whatever completed tool pairs were recorded; replay-shape quality is Story 10.6's concern, not 10.9's).
- [~] 11.7. **Variant — network drop mid-stream.** _[SKIPPED — redundant with F5 (11.8); same RequestAborted code path]_ Original instruction: With a run in progress, use macOS `ifconfig en0 down` (or equivalent) for 5 seconds to force the SSE stream to error without closing the tab. Restore the network. Confirm: status = Interrupted; the client shows the Retry path; behaviour matches 11.3–11.6.
- [x] 11.8. **Variant — F5 refresh mid-stream.** With a run in progress, press F5. The browser cancels the in-flight SSE request and re-navigates to the detail page. Confirm the Running → Interrupted transition happens, the refresh lands on the Retry UI (not on a Failed-looking UI).
- [x] 11.9. **Variant — cancel still works.** Start a fresh run. Click Cancel (10.8 behaviour). Confirm status → Cancelled (not Interrupted). Confirms AC3 — the branch ordering is correct.
- [x] 11.10. **Variant — Retry on Failed still works.** If practical, trigger a genuine failure (e.g., break a workflow agent file temporarily, start a run, watch it fail), click Retry, confirm the existing Failed path still truncates and re-streams. Skip if reproducing a Failed state is impractical — the automated test coverage for this path is already strong.
- [x] 11.11. Run `dotnet test AgentRun.Umbraco.slnx` — all tests green (target: existing baseline + the new tests from Tasks 8/9/10).
- [x] 11.12. Run `npm test` in `AgentRun.Umbraco/Client/` — frontend tests green.

## Failure & Edge Cases

### F1: OCE fires but `FindInstanceAsync` returns null (instance vanished)

**When** the OCE handler calls `FindInstanceAsync` and it returns `null` (the on-disk YAML was deleted or moved concurrently — pathological state)
**Then** both the Cancelled branch (`current is not null && current.Status == Cancelled`) and the disconnect branch (`current is not null && cancellationToken.IsCancellationRequested`) evaluate false
**And** execution falls through to the Failed fallback
**And** the Failed fallback's `SetInstanceStatusAsync` call throws because the instance doesn't exist, which is caught and logged at Critical
**And** the OCE rethrows to ASP.NET which closes the response
**This is correct** — a missing instance is a genuine hard failure; no change needed.

### F2: `SetInstanceStatusAsync(..., Interrupted, ...)` fails (write error)

**When** the disconnect branch attempts to persist Interrupted and the write fails (disk full, permission error, concurrent mutation)
**Then** the existing try/catch logs at Critical level with a specific "Failed to set … status to Interrupted after disconnect" message
**And** the handler still returns `EmptyResult` (no rethrow — the client is gone anyway)
**And** the persisted status remains whatever it was (likely `Running`), which is a stale flag but no worse than the current post-10.8 state on a Failed-write failure
**Mitigation:** same as today — stale `Running` after an OCE is a known pre-existing condition that a future background-execution refactor (Story 10.5+ territory) will eliminate by making the orchestrator authoritative.

### F3: Disconnect fires between `run.finished(Completed)` emit and controller return

**When** the orchestrator emits `run.finished(Completed)` and persists `Completed` at lines 160–163 of `WorkflowOrchestrator.cs`, then the client disconnects before ASP.NET returns the response (rare but possible on the last write)
**Then** the OCE propagates up into the handler
**And** `FindInstanceAsync` returns the instance with `Status == Completed`
**And** the Cancelled branch evaluates false (status != Cancelled)
**And** the disconnect branch evaluates true (`current is not null && cancellationToken.IsCancellationRequested`) → attempts `SetInstanceStatusAsync(..., Interrupted, ...)`
**And** the terminal-transition guard at `InstanceManager.cs:218` **refuses** the transition (Completed is terminal; refuses move out)
**And** logs at Information: `"Ignored status transition for instance {InstanceId}: already in terminal state Completed, refused transition to Interrupted"`
**And** the handler returns `EmptyResult`
**Net effect:** status stays Completed — correct. The terminal guard is the safety net here, and this is exactly the race the guard was built for.

### F4: Disconnect fires while the orchestrator is in the Error branch, mid-write of `Failed`

**When** the orchestrator's Error branch at [`WorkflowOrchestrator.cs:122–147`](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L122) is mid-way through `SetInstanceStatusAsync(..., Failed, ...)` when the HTTP request aborts
**Then** the Failed write either completes (status = Failed) or fails (status = Running if the write never landed)
**And** the orchestrator then emits `run.error` (may fail on a closed stream — caught and logged)
**And** control returns to the SSE handler; if OCE was thrown from somewhere later, the disconnect branch attempts Interrupted
**And** the terminal-transition guard refuses if status is already Failed → status stays Failed (correct)
**Edge: status is still Running (Failed write failed):** disconnect branch writes Interrupted; the run is semantically neither Failed nor Interrupted but since the user can't tell and Retry works on both, the UX outcome is indistinguishable. Accepted trade-off.

### F5: Disconnect on a step at `StepStatus.Pending` (pre-start)

**When** the disconnect fires before the orchestrator has transitioned the step from Pending to Active (a very narrow window — essentially between `RegisterInstance` at line 47 and `StepExecutor.ExecuteStepAsync` first mutation)
**Then** the persisted state: instance status = Running, all steps still Pending
**And** the disconnect branch writes Interrupted
**And** the user clicks Retry → `RetryInstance` step-discovery for `StepStatus.Active` returns -1
**And** AC6's 409 fires
**Resolution:** the user sees "No active step found to resume" and can either Delete the instance and start fresh, or inspect the state manually. Not elegant, but the scenario is narrow (sub-second race window during orchestrator startup) and the 409 message is clear.

### F6: Multiple steps are `Active` simultaneously (shouldn't happen — but)

**When** for any reason (bug, crash-recovery, manual state edit) the instance has more than one step with `StepStatus.Active`
**Then** `Steps.FindIndex(s => s.Status == Active)` returns the first occurrence by index
**And** Retry resets only that step to Pending
**And** the remaining Active steps stay Active
**Mitigation:** the orchestrator's invariants prevent this by design — StepExecutor transitions exactly one step at a time via `UpdateStepStatusAsync`. If this case arises, it's a bug in a different story (potentially 10.1 or 10.10). 10.9 does not take on multi-Active cleanup — it handles the single-Active case which is the real-world disconnect outcome.

### F7: User clicks Retry twice rapidly on an Interrupted instance

**When** two `POST .../retry` requests arrive within milliseconds for an Interrupted instance
**Then** the first request: passes the gate, resets step to Pending, transitions Interrupted → Running, starts SSE
**And** the second request: reads state (now Running), the gate `not (Failed or Interrupted)` refuses with 409
**And** this is the correct outcome — identical to the current double-Retry behaviour on Failed
**No change needed:** the existing Retry path is idempotent-enough under rapid repeat.

### F8: Disconnect branch fires when `cancellationToken.IsCancellationRequested` is true but the linked token source had additional reasons

**When** (theoretical) the HTTP request aborts AND simultaneously the per-instance CTS was also signalled but the Cancelled status was not yet persisted
**Then** the Cancelled branch evaluates false (status not yet Cancelled) → falls through
**And** the disconnect branch evaluates true → writes Interrupted
**And** the subsequent cancel-endpoint write attempts to persist Cancelled; the terminal-transition guard does NOT block (Interrupted is not terminal per locked decision 3) → status flips Interrupted → Cancelled
**Net effect:** the final persisted state is Cancelled, which is the user's intent. Branch ordering matters less than terminal-state convergence; accepted.

### F9: Non-disconnect OCE from a non-HTTP source (e.g., provider `HttpClient` timeout)

**When** an OCE originates deep inside the provider stack (e.g., `HttpClient` timeout firing at the M.E.AI layer) but NOT from the HTTP request token
**Then** the handler's disconnect check (`cancellationToken.IsCancellationRequested`) evaluates false
**And** the Failed fallback runs (AC2)
**And** status → Failed; Retry (the Failed path) is the correct user affordance
**Verification:** the existing `Start_OceWithRunningStatus_WritesFailedAndRethrows` test explicitly exercises this case — its `cancellationToken` parameter is `CancellationToken.None` (never cancelled), so disconnect-check is false. Green = no regression.

### F10: Retry on Interrupted races with a concurrent Delete

**When** User A clicks Retry on an Interrupted instance, User B clicks Delete on the same instance within milliseconds
**Then** (timing A): Retry reads state (Interrupted), transitions to Running, starts SSE. Delete reads state (now Running), sees the guard rejects delete of non-terminal, returns 409. Correct.
**Then** (timing B): Delete reads state (Interrupted), deletes the folder. Retry reads state after delete — `FindInstanceAsync` returns null → 404. Correct.
**Accepted:** this is the same last-writer-wins race that exists across all endpoints. No new mitigation needed.

### F11: Disconnect when the orchestrator is inside the autonomous-mode `Task.Delay`

**When** an autonomous-mode run is paused in `await Task.Delay(1000, runToken)` between steps and the HTTP client disconnects
**Then** the linked token fires, `Task.Delay` throws OCE, propagates out of the orchestrator into the SSE handler
**And** the disconnect branch runs → status = Interrupted
**And** Retry will find the next step with `StepStatus.Pending` or a mid-step `Active` (depending on where exactly the delay fired)
**Edge case:** after the delay but before the next iteration's `AdvanceStepAsync` call, the CurrentStepIndex is already advanced. Retry step-discovery for Active finds nothing (all next steps are Pending, current step is Complete). AC6's 409 fires.
**Mitigation:** for autonomous-mode instances that interrupt between steps, the user sees the same "no active step found to resume" 409 as F5. Pragmatic outcome for a narrow window; not worth special-casing in 10.9.

### F12: Client-side `_streamSseResponse` catch races with `_loadData`

**When** the server closes the stream (disconnect branch returned EmptyResult), the client's `reader.read()` eventually returns `{ done: true }` OR throws depending on timing
**Then** if `done: true` → clean exit, finally-block runs `_loadData`, status = Interrupted, UI updates correctly
**And** if throw → catch-block adds "Connection lost." system message, finally-block runs `_loadData`, status = Interrupted, UI updates correctly
**Both paths converge on the same UI state.** Do NOT attempt to differentiate; the `_loadData` is the source of truth.

## Dev Notes

### Discrimination decision rationale

The single most important design call in this story is: **how do we distinguish client disconnect from provider-internal OCE?**

Options considered:

| Option | How | Verdict |
|---|---|---|
| Check exception's inner `CancellationToken` field | `ex.CancellationToken` | Rejected — not reliably populated across providers and M.E.AI layers; brittle. |
| Add a sidecar "disconnect observed" bool captured from a request-abort callback | `HttpContext.RequestAborted.Register(() => _wasDisconnected = true)` | Rejected — adds state to the controller, complicates testing, race-prone. |
| Check `HttpContext.RequestAborted.IsCancellationRequested` after catch | Direct field access | Works, but controller `cancellationToken` parameter is identical and semantically clearer. |
| **Check the controller `cancellationToken` parameter's `IsCancellationRequested`** | Selected | Minimum surgery; ASP.NET model binder guarantees this token comes from `HttpContext.RequestAborted`; no additional state. |

The controller's `CancellationToken` parameter is the canonical signal for "did the client go away." When the cancel endpoint runs, it triggers the per-instance CTS, not the HTTP token — so the Cancelled branch catches that case before we reach the disconnect check. When a provider-internal timeout fires, the HTTP token is not cancelled — the OCE originates from a separate timer CTS inside `HttpClient`, and our controller token stays un-fired. This gives clean discrimination.

### Existing patterns to follow

- **Branch ordering in the OCE handler:** Cancelled first, then disconnect, then Failed fallback. This is a precedence hierarchy: deliberate-user-intent > client-went-away > everything-else.
- **Terminal-transition guard as safety net:** the `SetInstanceStatusAsync` guard (10.8) already refuses writes out of terminal states. 10.9's disconnect branch relies on this to handle F3 cleanly — do not duplicate the check at the call site.
- **Retry-as-idempotent recovery:** the existing `RetryInstance` shape (reset step, transition to Running, re-stream SSE) applies cleanly to both Failed and Interrupted. The only divergence is JSONL truncation and step-status discovery.
- **Log-noise minimisation:** the 10.8 amendment 1.2 decision — return EmptyResult rather than rethrow in a handled OCE branch — applies identically here. Same pattern, same reason, same test-assertion style.

### What NOT to do

- Do NOT introduce a new `Resume` button, endpoint, or HTTP verb. Retry handles both cases — locked decision 9.
- Do NOT add a `Cancelling` or `Disconnecting` interim state. Transitions are sub-second; users do not see the gap (same reasoning as 10.8 locked decision 8).
- Do NOT emit `run.finished(Interrupted)` SSE event. The stream is already gone; the emit would fail silently and the event would have no receiver — locked decision 8.
- Do NOT pre-empt Story 10.10 (resume-after-navigate-away UX) or Story 10.6 (retry-replay-degeneration). They are adjacent but distinct concerns.
- Do NOT refactor the orchestrator to run in a background `IHostedService`. That's the v2 full fix; post-beta.
- Do NOT add `Interrupted` to the `InstanceManager.SetInstanceStatusAsync` terminal guard. Retry depends on Interrupted→Running being allowed — locked decision 3.
- Do NOT change `IConversationStore.TruncateLastAssistantEntryAsync` — the existing method is correct for the Failed path; Interrupted simply doesn't call it.
- Do NOT add cancellation-rollback semantics to tools, JSONL, or artifacts. Same rule as 10.8 — workflows are not transactional.
- Do NOT introduce frontend state machine abstractions for this status transition. A two-line check in the Lit template's `inputPlaceholder` branch is sufficient.

### Test patterns

- **Backend:** NUnit 4 with `[TestFixture]`, `[Test]`, `Assert.That(...)`. NSubstitute for mocking. Follow `ExecutionEndpointsTests.cs` fixture shape — reuse `AttachHttpContext()` helper for SSE tests, `MakeInstance(InstanceStatus.X)` for fixture instance states.
- **Pre-cancelled token pattern for disconnect tests:**
   ```csharp
   using var cts = new CancellationTokenSource();
   cts.Cancel();
   var result = await _endpoints.StartInstance("inst-001", cts.Token);
   ```
- **Frontend:** `@open-wc/testing` + `describe/it/expect`. Fixtures via `html` + `fixture(...)`. Status mutation via direct property assignment on the element.
- **Run all tests:** `dotnet test AgentRun.Umbraco.slnx` (specify slnx — never bare `dotnet test`; see `feedback_dotnet_test_slnx.md`). Frontend: `npm test` from `AgentRun.Umbraco/Client/`.

### Project Structure Notes

- All backend changes are in `AgentRun.Umbraco/Endpoints/`, `AgentRun.Umbraco/Instances/`, and corresponding test folders.
- All frontend changes are in `AgentRun.Umbraco/Client/src/utils/` and `AgentRun.Umbraco/Client/src/components/`.
- Engine boundary preserved: no Engine/ code is touched. The SSE handler (Endpoints/) reacts to OCE at the API boundary, which is the correct layer.
- DI: no changes — `ExecutionEndpoints` already gets everything it needs via constructor injection. Interrupted is an enum value, not a service.
- No new NuGet dependencies. No new files (unless a test file needs creating — see Files in Scope).

### Serialization notes

- `InstanceStatus` uses `[JsonStringEnumConverter]` at the enum level — JSON serialization produces `"Interrupted"` automatically. No per-value annotations needed.
- YAML serialization via `InstanceManager._yamlSerializer` uses `UnderscoredNamingConvention` — enum values are serialized in whatever form YamlDotNet chooses (default is the enum's identifier verbatim, which for a single-word `Interrupted` means `Interrupted`). Verify via the AC4 round-trip test; if YamlDotNet produces `interrupted` (lower-case) that's fine — the deserializer is symmetric.
- API models (`InstanceResponse`, `InstanceDetailResponse`) are `record`/`class` types with `InstanceStatus` properties — they inherit the converter automatically, no changes.

### Frontend considerations

- The `InstanceResponse.status` field is typed as `InstanceStatus` in `api/types.ts` — add `"Interrupted"` to the union (check `AgentRun.Umbraco/Client/src/api/types.ts` and extend if needed).
- The status-helper switch statements in `instance-list-helpers.ts` have `default: return status;` branches — so adding `Interrupted` is additive; the default would already return `"Interrupted"` as a fallback label. The explicit case is about the colour (Warning) and future intent.
- Lit template re-renders happen automatically on `@state()` and `@property()` changes — no manual invalidation needed for the new branches.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story-10.9](../planning-artifacts/epics.md) — epic-level story definition and beta known-issue listing
- [Source: _bmad-output/planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md#High-1](../planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md) — Codex pre-launch review that surfaced this finding (High #1: "Execution is tied to the SSE request lifetime") and its pairing with #2 (Cancel does not stop work, which became Story 10.8)
- [Source: _bmad-output/implementation-artifacts/10-8-cancel-wiring-cancellation-token-per-instance.md](./10-8-cancel-wiring-cancellation-token-per-instance.md) — predecessor story; established the SSE OCE handler's branch structure and the "don't rethrow on handled OCE" precedent (amendment 1.2)
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:174-228](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174) — OCE handler to extend
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:109-160](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L109) — RetryInstance endpoint to extend
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:46-107](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L46) — StartInstance guard to extend
- [Source: AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs:83-117](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L83) — CancelInstance guard (unchanged but test-validated)
- [Source: AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs:119-155](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L119) — DeleteInstance guard to extend
- [Source: AgentRun.Umbraco/Instances/InstanceStatus.cs](../../AgentRun.Umbraco/Instances/InstanceStatus.cs) — enum to extend
- [Source: AgentRun.Umbraco/Instances/InstanceManager.cs:195-237](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L195) — terminal-transition guard (must NOT include Interrupted)
- [Source: AgentRun.Umbraco/Instances/InstanceManager.cs:239-268](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L239) — DeleteInstanceAsync guard to extend
- [Source: AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts](../../AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts) — status helpers to extend
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:900-1054](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L900) — Retry gate and input-enablement block to extend
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:433-478](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L433) — SSE reader loop (unchanged — its catch+finally handles the disconnect flow correctly today)
- [Source: _bmad-output/planning-artifacts/architecture.md#NFR5](../planning-artifacts/architecture.md) — "SSE reconnection with replay" accepted partial-gap; 10.9 does not attempt replay, only status discrimination

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context) — Amelia (dev agent)

### Debug Log References

- `dotnet build AgentRun.Umbraco.slnx` → clean build, no new exhaustive-switch warnings from the enum addition (existing switches use `is`-patterns with `or` or have `default` fallbacks).
- `dotnet test AgentRun.Umbraco.slnx` → 603/603 passing (was 592/592 pre-story; +11 new tests for Story 10.9: 5 in `ExecutionEndpointsTests`, 2 in `InstanceEndpointsTests`, 5 in `InstanceManagerTests` — plus 2 negative assertions added to the existing 10.8 OCE tests, counted within unchanged test numbers).
- `npm test` in `AgentRun.Umbraco/Client/` → 170/170 passing (was 162/162 pre-story; +8 new tests: 3 helper tests + 5 detail-element behaviour tests for Interrupted).
- `npm run build` → frontend typecheck + Vite production bundle clean.

### Completion Notes List

- **Core delta shipped:** new `InstanceStatus.Interrupted` value + OCE handler disconnect branch + Retry extension with Active-step discovery and no-truncation + delete/start guard extensions + frontend status surfacing. All 14 ACs satisfied in code and unit tests.
- **Locked decisions honoured:** Interrupted is an enum value (not a sidecar bool — decision 1); disconnect discrimination uses `cancellationToken.IsCancellationRequested` on the controller parameter (decision 2); Interrupted is NOT added to the terminal-transition guard at `InstanceManager.cs:218` (decision 3 — Retry depends on Interrupted→Running being allowed); Interrupted IS added to both delete guards (decision 4); Cancel guard untouched (decision 5 — `not (Running or Pending)` already rejects Interrupted); Retry skips `TruncateLastAssistantEntryAsync` for Interrupted and resets `StepStatus.Active` to Pending (decisions 6 + 7); no `run.finished(Interrupted)` SSE event (decision 8 — stream is gone); no new Resume button/endpoint (decision 9); `StepStatus.Active` orphan cleanup stays with Story 10.10 (decision 10).
- **Branch ordering preserved:** Cancelled → disconnect → Failed. AC3 + F3 + F8 regression guards wired via the existing terminal-transition guard.
- **Log noise:** disconnect branch does NOT rethrow (same pattern as 10.8 amendment 1.2) and does NOT attempt an SSE emit on a closed stream (locked decision 8). Information-level log: `"SSE client disconnected for instance {InstanceId}; marking Interrupted"`. Critical-level log only if the Interrupted write itself fails.
- **Existing 10.8 tests:** `Start_OceWithRunningStatus_WritesFailedAndRethrows` unchanged at the assertion level (its `cancellationToken` is `CancellationToken.None`, so disconnect branch evaluates false → Failed fallback runs = AC2). Added inline commentary + a `DidNotReceive` Interrupted assertion to pin the discrimination.
- **Frontend test style:** the repo's existing pattern uses pure-function mirrors of component logic (not full `@open-wc/testing` fixtures for gate computations). I mirrored that style — `shouldShowRetry`, `shouldShowCancel`, `placeholderFor` — to stay consistent with the surrounding detail-element tests. A future shift to fixture-based tests for the detail element would be a cross-cutting refactor, not scoped to this story.
- **Manual E2E deferred to Adam (Task 11):** the browser-based walkthrough (tab close, network drop, F5, cancel-still-works, retry-on-failed-still-works) requires real backoffice interaction. All automated gates green; awaiting Adam's manual validation against Task 11.1–11.12.

### File List

**Backend (production):**
- `AgentRun.Umbraco/Instances/InstanceStatus.cs` — added `Interrupted` enum value
- `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — StartInstance terminal guard includes Interrupted; RetryInstance gate accepts Failed|Interrupted with status-conditional step discovery + conditional truncation; ExecuteSseAsync OCE handler disconnect branch between Cancelled and Failed fallback
- `AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs` — DeleteInstance guard includes Interrupted
- `AgentRun.Umbraco/Instances/InstanceManager.cs` — DeleteInstanceAsync guard includes Interrupted; terminal-transition guard intentionally unchanged (locked decision 3)

**Frontend (production):**
- `AgentRun.Umbraco/Client/src/utils/instance-list-helpers.ts` — displayStatus + statusColor Interrupted cases; isTerminalStatus intentionally unchanged (locked decision 3)
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` — showRetry extended to Failed|Interrupted; inputEnabled/inputPlaceholder Interrupted branch hoisted above isInteractive switch

**Tests:**
- `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs` — added Story 10.9 tests: `Start_OceWithRunningStatus_ClientDisconnect_WritesInterruptedAndReturnsEmpty`, `Retry_OnInterrupted_ResetsActiveStepAndStreams`, `Retry_OnInterrupted_NoActiveStep_Returns409`, `Start_OnInterrupted_Returns409`; added negative Interrupted assertions + inline AC commentary to existing `Start_OceWithCancelledStatus_…` and `Start_OceWithRunningStatus_WritesFailedAndRethrows`
- `AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs` — added `CancelInstance_Returns409_WhenInterrupted` (AC8), `DeleteInstance_Returns204_WhenInterrupted` (AC7)
- `AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs` — added `SetInstanceStatusAsync_InterruptedToRunning_Allowed` (AC10), `SetInstanceStatusAsync_Interrupted_RoundTripsThroughYaml` (AC4), `DeleteInstanceAsync_OnInterrupted_Succeeds` (AC7 engine side), `InstanceStatus_Interrupted_SerializesAsInterruptedString` (AC4 JSON), `SetInstanceStatusAsync_TerminalToAnything_Refused` (regression guard for terminal set)
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-list.element.test.ts` — added Story 10.9 describe block: displayStatus returns "Interrupted" both modes, statusColor returns "warning" both modes, isTerminalStatus returns false
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts` — added Story 10.9 describe block: Retry renders for Interrupted, Cancel hidden for Interrupted, placeholder text + enabled-state checks, distinct-from-terminal placeholder regression guard

**Story + sprint tracking:**
- `_bmad-output/implementation-artifacts/10-9-sse-disconnect-resilience.md` — this file (tasks checked, Dev Agent Record filled, status review)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 10-9 transitioned ready-for-dev → in-progress → review

### Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-14 | Initial story creation by Bob (SM) — comprehensive context for SSE disconnect discrimination: new `Interrupted` enum value, OCE handler disconnect branch, Retry extension with Active-step discovery + no-truncation, delete/start guard extensions, frontend status surfacing. |
| 0.2 | 2026-04-14 | Tasks 1–10 implemented by Amelia (dev). InstanceStatus.Interrupted added; ExecuteSseAsync OCE disconnect branch (controller-token discrimination, no rethrow, no SSE emit); RetryInstance branches on Failed vs Interrupted (Error vs Active step discovery, conditional JSONL truncation); StartInstance + DeleteInstance guards include Interrupted; InstanceManager.DeleteInstanceAsync guard includes Interrupted, terminal-transition guard intentionally unchanged per locked decision 3; frontend helpers + detail element surface Interrupted (warning colour, disabled input, distinct placeholder, Retry gate). Backend 603/603 tests (+11), frontend 170/170 tests (+8), full-stack build clean. Manual E2E (Task 11) deferred to Adam. Status → review. |
| 0.3 | 2026-04-14 | Code review applied 7 patches (Amelia). P1: disconnect branch now gated on `current.Status == Running` — prevents misleading "marking Interrupted" log on terminal states + silent `UpdatedAt` refresh on non-Running. P2: `RetryInstance` `SetInstanceStatusAsync(Running)` wrapped in try/catch → concurrent-retry race returns 409 `already_running` instead of unhandled 500. P3: `RetryInstance` gains `ActiveInstanceRegistry.GetMessageWriter` guard mirroring `StartInstance` — rejects retry while prior orchestrator is still draining. P5: retry 409 message aligned ("No errored step found to resume" / "No active step found to resume"). P6: disconnect-branch write-failure log downgraded `Critical` → `Error` (recoverable). P7: frontend `_onRetryClick` rejects stale click when status is no longer Failed/Interrupted. P4: added `Retry_OnFailed_StillTruncatesConversation` regression test (Task 8.7). +4 backend tests (`Start_OceWithCompletedStatus_DoesNotWriteInterruptedEvenOnDisconnect`, `Retry_OnFailed_StillTruncatesConversation`, `Retry_OnInterrupted_ConcurrentRetry_Returns409NotUnhandled`, `Retry_OnInterrupted_WithActiveRegistryWriter_Returns409`). Backend 607/607, frontend 170/170. One `decision_needed` resolved: kept `statusColor("Interrupted")` unconditional `warning` (distinct from Failed's suppressed-interactive badge — Interrupted has no in-chat signal so the badge carries the full user cue). Six items deferred (see deferred-work.md). Manual E2E (Task 11) still the remaining gate. |
| 0.4 | 2026-04-14 | Manual E2E (Task 11) completed by Adam with Amelia co-driver. Verified AC1 (tab close → Interrupted badge), AC3+AC8+F8 (Cancel still preserves Cancelled + no cross-contamination with disconnect branch), AC5 (Retry from Interrupted resumes correctly — scanner picked up at node 7 of 27 after 6 completed nodes, no duplicate work, JSONL preserved), AC6 extended (Retry on Failed instance with no Error step returns 409), AC11 (amber warning badge), AC12+AC13 (no Cancel button, Retry visible, input disabled with "Run interrupted — click Retry to resume." placeholder), AC14 (F5 refresh mid-stream routes through disconnect branch cleanly). Network-drop variant (Task 11.7) skipped as redundant with F5 (same RequestAborted code path). **Two additional defects surfaced and fixed in-session:** (a) **Terminal-transition guard was too broad** (pre-existing 10.8 regression) — `InstanceManager.SetInstanceStatusAsync` refused `Failed → Running` silently, so Retry(Failed) called `ExecuteSseAsync` with a still-Failed instance, orchestrator ran the step to completion but instance status never flipped, frontend stayed stuck on Retry button. Fix: narrowed the guard to refuse *sideways* terminal transitions (e.g., Cancel overwriting Completed) while permitting transitions INTO Running (Retry is the only path that writes Running to a terminal, gated by `RetryInstance`'s Failed|Interrupted admission check). +2 tests: `SetInstanceStatusAsync_FailedToRunning_Allowed`, `SetInstanceStatusAsync_CancelledToRunning_Allowed`. (b) **Terminal-state input placeholders misleading** — all three terminals (`Completed`, `Failed`, `Cancelled`) rendered "Workflow complete" as the disabled-input placeholder, wrong for Failed (shown next to Retry button) and Cancelled (workflow wasn't complete, it was cancelled). Fix: status-aware placeholder — `Cancelled` → "Run cancelled.", `Failed` → "Run failed — click Retry to resume.", `Completed` → "Workflow complete." unchanged. Applied in both interactive and autonomous branches. Backend 609/609 tests (+2), frontend 170/170. All 14 ACs verified end-to-end. Umbraco.AI middleware noise (`Failed to record AI usage`, `AuditLog ... canceled`) confirmed as pre-existing upstream issue (already filed in `upstream-issue-umbraco-ai-cancellation-logging.md`). Sonnet 4.6 used throughout. Status → done. |

### Review Findings

- [ ] [Review][Patch] Disconnect branch ignores `current.Status` — was clobbering terminal states + producing misleading INFO logs [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:238] — **APPLIED** (0.3 P1)
- [ ] [Review][Patch] `RetryInstance` concurrent retry surfaces as 500 instead of 409 [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:169] — **APPLIED** (0.3 P2)
- [ ] [Review][Patch] `RetryInstance` missing `ActiveInstanceRegistry` guard — parallel orchestrator race [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:125] — **APPLIED** (0.3 P3)
- [ ] [Review][Patch] Task 8.7 regression test `Retry_OnFailed_StillTruncatesConversation` was missing — **APPLIED** (0.3 P4)
- [ ] [Review][Patch] Retry 409 messages inconsistent phrasing under same `invalid_state` code [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:147-150] — **APPLIED** (0.3 P5)
- [ ] [Review][Patch] `LogCritical` over-severity on disconnect-branch write failure [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:252] — **APPLIED** (0.3 P6)
- [ ] [Review][Patch] Frontend `_onRetryClick` guard lacks status check — stale click fires 409 [agentrun-instance-detail.element.ts:406] — **APPLIED** (0.3 P7)
- [x] [Review][Decision] `statusColor("Interrupted", "interactive")` returns `"warning"` not `undefined` — **RESOLVED**: keep unconditional warning. Rationale: Interrupted has no in-chat signal (unlike Failed's error message), so the badge carries the full user cue; mirroring Failed's suppressed-interactive pattern would make Interrupted invisible inside the chat.
- [x] [Review][Defer] `CurrentStepIndex` not reconciled with `FindIndex(Active)` result [ExecutionEndpoints.cs:141] — deferred, pre-existing Failed-path pattern
- [x] [Review][Defer] ConversationRecorder completion-boundary invariant load-bearing but unverified — deferred to Story 10.6 (replay-shape refinement owns this)
- [x] [Review][Defer] No test for Interrupted + Error step coexistence (rare pathological state) — deferred, narrow window
- [x] [Review][Defer] `InstanceManager.SetInstanceStatusAsync` engine-level has no Interrupted-protection guard — deferred, architectural concern
- [x] [Review][Defer] Disconnect race: all steps Complete + status not yet Completed → Retry button renders, click 409s — deferred, narrow race window
- [x] [Review][Defer] Manual E2E Task 11 (browser-based verification) — deferred to Adam before merge (story-level gate per project convention)
