# Story 10.10: Instance Resume After Step Completion

Status: done

**Depends on:** Story 10.8 (Cancel wiring + CTS per instance — done), Story 10.9 (SSE disconnect → `Interrupted` + Retry extension — done)
**Branch:** `feature/epic-10`
**Priority:** 5th in Epic 10 (10.2 done → 10.12 done → 10.8 done → 10.9 done → **10.10** → 10.1 → 10.6 → 10.11 → 10.7 → 10.4 → 10.5)

> **Beta known issue — listed in epics.md §"Beta Known Issues" and §"Story 10.10".** After a step completes in an interactive-mode workflow, navigating away and returning causes the chat input to reject every message with "Failed to send message. Try again." Root cause is a two-part seam failure on the resume path: (1) in interactive mode there is no "Continue" affordance between steps so the user cannot explicitly resume, and (2) the chat `inputEnabled` branch at [agentrun-instance-detail.element.ts:959](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L959) treats `inst.status === "Running"` as "input is live" even when the orchestrator is not registered in `ActiveInstanceRegistry`, so typing a message posts to [/message](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L348) which 409s with `not_running`. This story adds the Continue affordance for interactive mode, tightens the `inputEnabled` gate to match orchestrator reality, and picks up the cancel carry-over from Story 10.8: an `Active` step left orphaned after cancel gets cleaned up via a new `StepStatus.Cancelled`.

## Story

As a backoffice user,
I want to navigate away from a running interactive workflow and return to resume it without the UI breaking,
So that I can pause between steps to review output and come back later to continue, without being blocked by a chat input that silently rejects every message.

## Context

**UX Mode: N/A** — frontend state-gating + backend step-status enum extension. No new UI components; small additions to existing render logic and one new enum value.

### Bug A — Chat input rejected on resume (primary bug)

After an interactive step completes in a workflow (e.g. CQA scanner → analyser), the orchestrator returns from `ExecuteNextStepAsync` at [WorkflowOrchestrator.cs:171](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L171), advances `CurrentStepIndex`, and exits — in interactive mode the orchestrator does not auto-advance to the next step (this is by design: interactive workflows pause between steps so the user can review output before proceeding). The persisted state after the scanner completes for instance `f95d49e1…`:

- `instance.Status == "Running"`
- `CurrentStepIndex == 1` (analyser)
- `steps[0].Status == "Complete"` (scanner done)
- `steps[1].Status == "Pending"` (analyser not yet started)
- `ActiveInstanceRegistry.GetMessageWriter(id)` returns `null` (no active orchestrator)

When Adam navigated back into the instance:

1. The chat input was **enabled** — the fallback branch at [agentrun-instance-detail.element.ts:959](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L959) reads `this._streaming || activeStep || inst.status === "Running"` and the third clause is true.
2. Typing a message fires `_onSendAndStream` at [line 767](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L767) which first POSTs `/instances/{id}/message`. That endpoint at [ExecutionEndpoints.cs:348](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L348) requires `GetMessageWriter(id)` to return non-null — returns `409 not_running` because no orchestrator is registered.
3. The frontend catch at [line 754](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L754) appends the system message "Failed to send message. Try again." and returns before the `_onStartClick()` call that would have started the SSE stream. User is stuck.

There is no "Resume" or "Continue" affordance for this state in interactive mode. The existing `showContinue` button at [line 926](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L926) is gated on `!isInteractive` — it only renders for autonomous workflows. Autonomous workflows between steps get a button; interactive workflows get a broken chat input.

### Bug B — `StepStatus.Active` orphan after cancel (carry-over from Story 10.8 code review)

When the user clicks Cancel, [InstanceEndpoints.CancelInstance](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L87) calls `SetInstanceStatusAsync(..., Cancelled, ...)` and then `RequestCancellation(id)`. The instance-level status transitions correctly, but no code path touches `steps[i].Status` — the step that was executing stays at `StepStatus.Active` after cancel. When the user reopens the cancelled instance:

- `instance.Status == "Cancelled"` (terminal, correct)
- `steps[activeIdx].Status == "Active"` (orphaned — wrong)
- Frontend `stepSubtitle()` renders "In progress" for the orphaned step
- `stepIconColor()` returns `"current"` (the live colour)
- `shouldAnimateStepIcon(status, isStreaming)` today returns `status === "Active" && isStreaming`, and on reopen `_streaming == false` so the spinner does not animate — but the step-progress sidebar still renders the step in its "live" visual state, which is misleading next to a terminal Cancelled badge.

The 10.8 code review flagged this for 10.10: "on cancel or abort, the interrupted step's status transitions out of `Active` so the resumed/reopened UI does not render it as live." The fix is a new `StepStatus.Cancelled` enum value and a step-cleanup write inside `CancelInstance` before the CTS fires.

### The fix shape (minimum fix per epic)

**Part A — Resume affordance + chat input gate (Bug A):**

1. Extend the existing `showContinue` button to render for interactive mode between steps too — drop the `!isInteractive` clause at [line 926](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L926). Button label stays `"Continue"` (same semantic: "run the next pending step"). Clicking it hits the existing `_onStartClick` → `/start` endpoint, which already handles `Running` + between-steps at [ExecutionEndpoints.cs:75](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L75) (allows restart when no writer is registered).
2. Tighten the interactive-mode `inputEnabled` branch at [line 959](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L959). The current condition `this._streaming || activeStep || inst.status === "Running"` enables chat whenever the instance is persisted `Running`, regardless of whether an orchestrator is actually executing. Drop the third clause; require `this._streaming || activeStep`. Running-with-no-activeStep-and-no-streaming now falls to the final else which already disables input with placeholder "Send a message to start." — but that placeholder is wrong for this case (suggests the workflow hasn't begun). Add a dedicated between-steps branch above it: `if (isRunningBetweenSteps) { inputEnabled = false; inputPlaceholder = "Click Continue to run the next step."; }`.
3. `_onSendAndStream` at [line 767](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L767) is the legacy "message-then-stream" handler used when interactive + not streaming. With the tightened gate, this handler can only fire when the chat is enabled AND interactive AND not streaming — i.e., when `activeStep` exists (the orchestrator is mid-step waiting for user input via the Channel). This path is unchanged. Do NOT rework `_onSendAndStream` to try to auto-resume — that conflates two flows. Between-steps resume is explicit (Continue button); mid-step response is the existing `_onSendAndStream` path.

**Part B — Step cleanup on cancel (Bug B):**

1. Add `Cancelled` to `StepStatus` at [StepStatus.cs](../../AgentRun.Umbraco/Instances/StepStatus.cs). Position after `Error` — maintain source order for YAML/JSON compatibility (new values appended, not inserted). `[JsonStringEnumConverter]` on the enum handles serialization automatically.
2. In [InstanceEndpoints.CancelInstance](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L87), between `SetInstanceStatusAsync(Cancelled)` and `RequestCancellation`, find any step with `StepStatus.Active` in the returned `updated.Steps` list and call `UpdateStepStatusAsync(..., StepStatus.Cancelled, ...)` for each (expect exactly one; defensive loop handles zero or more). This is the "persist first, signal second" pattern from Story 10.8 — the step-status write must land before the CTS fires so the orchestrator, when it observes cancellation and returns, doesn't race with a stale Active.
3. Extend frontend step-display helpers at [instance-detail-helpers.ts](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts): `stepSubtitle` returns `"Cancelled"`, `stepIconName` returns `"icon-block"` (or equivalent terminal-not-success icon), `stepIconColor` returns `undefined` (neutral — the instance-level Cancelled badge carries the primary signal, the step icon just stops looking live). `shouldAnimateStepIcon` unchanged — it gates on `Active`, so Cancelled never animates.

### Architect's locked decisions (do not relitigate)

1. **Continue button is REUSED for interactive mode, not duplicated.** The existing `showContinue` button renders `"Continue"` for autonomous between-steps. Extending its gate to interactive mode is a one-token removal (`!isInteractive`). Do NOT create a new `showResume` button with different semantics. The button click handler (`_onStartClick`) already works for both modes (POSTs `/start`, which calls `ExecuteSseAsync` for the next pending step).
2. **Chat input between-steps gate is EXPLICIT, not auto-resume.** The user expects interactive workflows to pause between steps (that's the definition of interactive mode). Typing into a disabled chat with placeholder "Click Continue to run the next step." points the user at the correct affordance. Do NOT auto-start the SSE stream on instance reopen — it would run the next step without user consent, violating interactive semantics.
3. **`StepStatus.Cancelled` is a new enum value, not a reset to `Pending`.** The cancelled step carries historical value: "this is where the run was stopped." Resetting to `Pending` would be cosmetic but semantically wrong — `Pending` means "not yet started," which is false. Symmetric with how Story 10.9 added `InstanceStatus.Interrupted` rather than overloading `Failed`.
4. **Step cleanup happens in `CancelInstance` endpoint, NOT in the SSE OCE handler.** The cancel endpoint is the transactional owner of the cancel intent: it writes `InstanceStatus.Cancelled`, writes `StepStatus.Cancelled`, then fires the CTS. The SSE OCE handler is reactive — by the time it runs, the step write has landed. Defence-in-depth cleanup in the OCE handler would duplicate writes and mask any future bug where the primary path fails. Single-source-of-truth policy.
5. **Do NOT add `Cancelled` to `Retry`'s step-discovery.** Retry's gate at [ExecutionEndpoints.cs:125](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L125) accepts `Failed | Interrupted` only. `Cancelled` is not retry-eligible — the user explicitly stopped the run, the correct affordance is Delete (already wired — `DeleteInstance` accepts `Cancelled` at [InstanceEndpoints.cs:135](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L135)).
6. **Do NOT touch the interactive-mode step advancement design.** The orchestrator's "return after each step in interactive mode" behaviour at [WorkflowOrchestrator.cs:171](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L171) stays unchanged. This story adds the Continue affordance that makes that behaviour observable and recoverable; it does not alter the orchestrator's step-loop shape.
7. **Chat history binding on reopen stays with the last completed step.** The conversation-history load at [line 319–333](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L319) — "find first Active/Error step, fallback to last Complete" — is correct for between-steps: there is no Active step, and the last Complete step's transcript is the most useful context to display. User can review it while the chat input is disabled. Do NOT change this logic.
8. **No SSE emit on step-cancel.** The `UpdateStepStatusAsync(Cancelled)` call produces no SSE event. The run.finished(Cancelled) emit at [ExecutionEndpoints.cs:241](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L241) already covers the instance-level terminal signal; per-step updates for a terminal run have no observer. Locked decision 8 from Story 10.9 applies analogously.
9. **Step subtitle/icon for Cancelled are neutral, not danger.** The instance-level Cancelled badge is the primary visual. The step display should deactivate cleanly (no spinner, neutral colour) so the reopened view does not look alarming — the user cancelled the run intentionally. Different from `Error`, which uses danger colour because it indicates unintended failure.
10. **`isStreaming` gate on `shouldAnimateStepIcon` is unchanged.** The existing guard `status === "Active" && isStreaming` is correct. After Part B ships, a freshly-cancelled instance has no `Active` step, so the gate is structurally impossible to trigger — even if a future UI change drops the `isStreaming` clause. Belt-and-braces by design.

## Acceptance Criteria (BDD)

### AC1: `showContinue` renders for interactive mode between steps

**Given** an instance with `workflowMode != "autonomous"` (interactive)
**And** `instance.status === "Running"`
**And** no step has `status === "Active"` (between-steps state)
**And** at least one step has `status === "Pending"`
**And** `this._streaming === false`
**When** the detail view renders
**Then** the `Continue` button is visible in the header
**And** clicking it invokes `_onStartClick` which POSTs `/instances/{id}/start`
**And** `/start` returns an SSE stream that executes the next pending step

### AC2: `showContinue` still renders for autonomous mode between steps

**Given** an instance with `workflowMode === "autonomous"`
**And** `instance.status === "Running"`
**And** no step has `status === "Active"` and at least one step has `status === "Pending"`
**And** `this._streaming === false`
**When** the detail view renders
**Then** the `Continue` button is visible (existing behaviour preserved — regression guard)

### AC3: `showContinue` does NOT render when an active step exists

**Given** an instance with `instance.status === "Running"`
**And** any step has `status === "Active"` (orchestrator is mid-step)
**When** the detail view renders
**Then** the `Continue` button is hidden
**And** the chat input is enabled with placeholder `"Message the agent..."` (existing AC9 interaction preserved)

### AC4: `showContinue` does NOT render for terminal states

**Given** an instance with `instance.status` in `{"Completed", "Failed", "Cancelled", "Interrupted"}`
**When** the detail view renders
**Then** the `Continue` button is hidden (the terminal-state affordances — Retry for Failed/Interrupted, nothing for Completed/Cancelled — apply instead)

### AC5: Chat input disabled between steps in interactive mode

**Given** an interactive instance with `instance.status === "Running"`
**And** no step has `status === "Active"`
**And** `this._streaming === false`
**And** `this._viewingStepId` is null (user is not browsing a completed step's history)
**When** the detail view renders
**Then** the chat panel's `input-enabled` attribute is `false`
**And** the `input-placeholder` attribute reads `"Click Continue to run the next step."`
**And** typing into the input does not fire `_onSendAndStream` or `_onSendMessage` (chat-panel component honours `input-enabled`)

### AC6: Chat input re-enables when orchestrator resumes

**Given** the between-steps state of AC5
**When** the user clicks `Continue` → `/start` begins streaming
**And** the orchestrator transitions the next step's status to `Active` and emits SSE events
**And** `this._streaming` becomes `true` (via the client's SSE reader loop)
**Then** the chat input re-enables (either via `activeStep` or `this._streaming` — both conditions in the gate)
**And** the placeholder reverts to `"Message the agent..."` (existing interactive-streaming behaviour)

### AC7: Regression guard — existing mid-step input behaviour unchanged

**Given** an interactive instance where the orchestrator is actively streaming a step (`this._streaming === true`)
**When** the user types a message
**Then** `_onSendMessage` fires (via the `this._streaming` branch at [line 978–980](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L978))
**And** the message POSTs to `/message` and is picked up by the orchestrator's Channel reader
**And** the agent's response streams normally (no change to this path)

### AC8: Regression guard — mid-step send-and-stream unchanged

**Given** an interactive instance with an `Active` step, not currently streaming (rare — e.g., user pressed Stop on an earlier stream and the step is still Active waiting for a new connection)
**When** the user types a message
**Then** `_onSendAndStream` fires, posts `/message` successfully (writer is registered via the step's earlier execution), then calls `_onStartClick` to re-establish the SSE stream
**And** no "Failed to send message" error appears (existing mid-step flow preserved)

### AC9: `StepStatus.Cancelled` exists and round-trips through serialization

**Given** the `StepStatus` enum
**When** `Cancelled` is added as a new value after `Error`
**Then** `JsonSerializer.Serialize(StepStatus.Cancelled)` produces `"Cancelled"` (via the existing `[JsonConverter(typeof(JsonStringEnumConverter))]`)
**And** YAML serialization via `InstanceManager.WriteStateAtomicAsync` writes the value per the existing convention
**And** YAML deserialization of an instance with `steps[i].status: Cancelled` round-trips back to `StepStatus.Cancelled`
**And** existing YAML files without this value deserialize unchanged (forward-compatible)

### AC10: `CancelInstance` endpoint sets active step to `StepStatus.Cancelled`

**Given** an instance with `instance.Status == "Running"` and exactly one step with `steps[i].Status == "Active"`
**When** `POST /umbraco/api/agentrun/instances/{id}/cancel` is called
**Then** the endpoint first calls `SetInstanceStatusAsync(..., Cancelled, ...)` (existing behaviour)
**And** then iterates `updated.Steps`, finds the step(s) with `Status == Active`, and calls `UpdateStepStatusAsync(..., stepIndex, StepStatus.Cancelled, ...)` for each
**And** then calls `_activeInstanceRegistry.RequestCancellation(id)` (existing behaviour)
**And** returns `200 OK` with the mapped response
**And** the persisted `steps[i].Status == "Cancelled"` in the YAML file

### AC11: `CancelInstance` with no Active step still succeeds

**Given** an instance with `instance.Status == "Running"` but no step has `Status == Active` (pathological — e.g., cancel pressed in the sub-second window between one step completing and the next starting)
**When** the cancel endpoint is called
**Then** `SetInstanceStatusAsync(Cancelled)` runs
**And** the Active-step iteration finds zero steps to update (defensive loop handles empty set)
**And** no `UpdateStepStatusAsync` call is made
**And** `RequestCancellation` still fires
**And** the endpoint returns `200 OK` (no `NullReferenceException` or `InvalidOperationException`)

### AC12: `CancelInstance` with multiple Active steps cleans all of them

**Given** (pathological) an instance where two or more steps somehow have `Status == Active` (should never happen under normal orchestrator invariants, but guard against)
**When** the cancel endpoint is called
**Then** every Active step is set to `StepStatus.Cancelled`
**And** the endpoint returns `200 OK`
**Note:** the orchestrator's single-step-at-a-time invariant prevents this case from arising normally. AC12 is a defensive assertion — the loop must not assume a single Active step.

### AC13: Frontend renders `StepStatus.Cancelled` neutrally

**Given** a step with `status === "Cancelled"` in the step-progress sidebar
**When** the detail view renders
**Then** `stepSubtitle(step)` returns `"Cancelled"`
**And** `stepIconName("Cancelled")` returns `"icon-block"` (or the agreed terminal-neutral icon — see note below on icon selection)
**And** `stepIconColor("Cancelled")` returns `undefined` (neutral — the step renders in the default colour, not danger or current)
**And** `shouldAnimateStepIcon("Cancelled", isStreaming)` returns `false` for both `isStreaming` values (Cancelled is not Active)

### AC14: Cancelled step click behaviour

**Given** a step with `status === "Cancelled"` in the step-progress sidebar
**When** the user clicks the step
**Then** `_onStepClick` runs normally — it is not `"Pending"`, so the early-return guard at [line 353](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L353) does not fire
**And** `_viewingStepId` is set to the step's id
**And** the conversation history for that step loads (via `getConversation`) — the JSONL transcript produced before the cancel is still readable
**And** the user can review what happened before cancel

### AC15: Cancel step-cleanup is atomic enough (no mid-cancel race fallout)

**Given** the cancel endpoint is executing (between `SetInstanceStatusAsync(Cancelled)` and `RequestCancellation`)
**When** the orchestrator observes the per-instance CTS has not yet fired (it fires at the `RequestCancellation` step)
**And** the orchestrator happens to complete the step in the microsecond window (writes `StepStatus.Complete`)
**Then** the `CancelInstance` endpoint's subsequent `UpdateStepStatusAsync(..., Cancelled, ...)` for that step overwrites `Complete` with `Cancelled`
**And** the final persisted state is `steps[i].Status == "Cancelled"` — cancel intent wins over completion-in-race
**Note:** this is an accepted last-writer-wins race. The window is sub-millisecond in practice. The UX outcome is "cancel happened just as step completed — user sees Cancelled, which matches their click." Alternative designs (transactional mutual exclusion) are over-engineering for this case.

## Files in Scope

| File | Change |
|---|---|
| [AgentRun.Umbraco/Instances/StepStatus.cs](../../AgentRun.Umbraco/Instances/StepStatus.cs) | Add `Cancelled` enum value after `Error`. Keep `[JsonConverter(typeof(JsonStringEnumConverter))]` — serializes as `"Cancelled"`. |
| [AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs) | `CancelInstance` (lines 83–117): between `SetInstanceStatusAsync(Cancelled)` at line 108 and `RequestCancellation` at line 114, add step-status cleanup loop that sets every Active step to `StepStatus.Cancelled`. |
| [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) | (a) `showContinue` at line 926: drop `!isInteractive` — render Continue for both modes when between-steps. (b) Interactive `inputEnabled` branch at line 959: replace the three-clause condition with a new between-steps branch above the `else if (this._streaming \|\| activeStep)` — when `isRunningBetweenSteps` (Running + no activeStep + no streaming), set `inputEnabled = false` and `inputPlaceholder = "Click Continue to run the next step."`. |
| [AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) | Add `"Cancelled"` cases to `stepSubtitle` (returns `"Cancelled"`), `stepIconName` (returns `"icon-block"`), `stepIconColor` (returns `undefined`). `shouldAnimateStepIcon` unchanged (structurally false for non-Active). |
| [AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs) | Add: (a) `Cancel_WithActiveStep_SetsStepToCancelled` (AC10). (b) `Cancel_WithNoActiveStep_StillSucceeds` (AC11). (c) `Cancel_WithMultipleActiveSteps_CleansAllOfThem` (AC12 — pathological guard). |
| [AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs](../../AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs) | Add: `UpdateStepStatusAsync_SetsCancelledStatus` (round-trip via YAML — AC9 engine side). If an existing enum-round-trip test pattern exists, extend it rather than creating a new file. |
| [AgentRun.Umbraco.Tests/Instances/InstanceStatusSerializationTests.cs or equivalent](../../AgentRun.Umbraco.Tests/Instances/) | Add `StepStatus_Cancelled_SerializesAsCancelledString` assertion. Follow the same pattern as `InstanceStatus_Interrupted_SerializesAsInterruptedString` from Story 10.9. If the test file lives elsewhere (e.g., the same file as the 10.9 serialization test), extend it. |
| [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts) | Add: (a) `Continue button renders for interactive mode between steps` (AC1). (b) `Continue button still renders for autonomous mode between steps` (AC2 — regression guard). (c) `Continue button hidden when active step exists` (AC3 — regression guard). (d) `Chat input disabled between steps in interactive mode` (AC5 — input-enabled + placeholder). (e) `Chat input re-enables when streaming starts` (AC6). |
| [AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.test.ts](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.test.ts) (check for existence; follow repo pattern) | Add `stepSubtitle`/`stepIconName`/`stepIconColor` tests for `"Cancelled"` — AC13. |

**Files explicitly NOT touched:**

- [AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) — step-advancement behaviour in interactive mode is unchanged (locked decision 6). The orchestrator still returns after each step; the Continue button gives the user the affordance to resume.
- [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs) — no change. The `/start` endpoint at lines 46–107 already handles the `Running` + between-steps case (lines 75–86 allow restart when no writer is registered). `_onStartClick` / `showContinue` reuse this existing path. The SSE OCE handler at lines 213–310 is not touched (locked decision 4 — cancel endpoint owns the step cleanup, not the reactive OCE path). The `/message` endpoint at line 348 is not touched — its 409 behaviour is correct; the fix is upstream at the UI's `inputEnabled` gate.
- [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs) — no change. The registry's single-orchestrator invariant is respected by the UI gate; no new registry semantics needed.
- [AgentRun.Umbraco/Instances/InstanceStatus.cs](../../AgentRun.Umbraco/Instances/InstanceStatus.cs) — no change. This story adds `StepStatus.Cancelled`, not another `InstanceStatus`.
- [AgentRun.Umbraco/Instances/ConversationStore.cs](../../AgentRun.Umbraco/Instances/ConversationStore.cs) — the JSONL transcript for the cancelled step is preserved as-is. User can review it via the step-click history load (AC14).
- Any backend "resume on load" auto-orchestration code path — locked decision 2, we do not auto-start on instance reopen.

## Tasks

### Task 1: Add `Cancelled` to `StepStatus` enum (AC9)

- [x] 1.1 Add `Cancelled` after `Error` in [StepStatus.cs](../../AgentRun.Umbraco/Instances/StepStatus.cs).

1. Open the file.
2. Add a new line after `Error,`: `Cancelled`.
3. The `[JsonConverter(typeof(JsonStringEnumConverter))]` attribute handles JSON serialization — no per-value attributes needed.
4. Build the project (`dotnet build AgentRun.Umbraco.slnx`) to surface any exhaustive-switch warnings. C# `switch` expressions and statements with no default branch will warn when a new enum value is added — audit each warning and decide explicitly. Most will resolve via `is`-patterns with `or` or `default` branches; any that do not must be updated to handle `Cancelled` (most likely candidates: any future rendering code that switches on StepStatus — at the time of writing, the only switch-like consumers are the frontend helpers in Task 4).

**Thread-safety / concurrency:** none — enum addition is a pure type-system change.

### Task 2: `CancelInstance` — add step-status cleanup (AC10, AC11, AC12, AC15)

- [x] 2.1 Modify [`InstanceEndpoints.CancelInstance`](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L87) to clean up Active steps after writing `InstanceStatus.Cancelled`.

1. Between line 108 (`SetInstanceStatusAsync(Cancelled)` call returning `updated`) and line 114 (`RequestCancellation`), insert the step cleanup loop:
   ```csharp
   // Story 10.10: clean up any Active step so the reopened UI does not render
   // it as live. Must happen BEFORE RequestCancellation fires the CTS — the
   // orchestrator, when it observes cancellation and returns, should see the
   // step as Cancelled, not Active. Defensive loop handles zero (cancel in
   // the between-steps window) or multiple (pathological; should not occur).
   for (int i = 0; i < updated.Steps.Count; i++)
   {
       if (updated.Steps[i].Status == StepStatus.Active)
       {
           updated = await _instanceManager.UpdateStepStatusAsync(
               updated.WorkflowAlias, updated.InstanceId, i,
               StepStatus.Cancelled, cancellationToken);
       }
   }
   ```
2. Do NOT reorder `RequestCancellation` — it must still fire after both status writes. Order: SetInstanceStatus(Cancelled) → per-step UpdateStepStatus(Cancelled) → RequestCancellation.
3. Do NOT add a `try/catch` around the UpdateStepStatusAsync loop. If the step-status write fails (disk error), the endpoint surfaces the exception as 500 — same as if `SetInstanceStatusAsync(Cancelled)` had failed. Consistent error surface.
4. Pass `cancellationToken` (the controller parameter) to `UpdateStepStatusAsync`. This is the HTTP request token — if the user navigates away mid-cancel, the writes abort. The persisted state is then: Cancelled (instance) + Active (step, if the step write didn't land). This is an accepted edge: the instance status is correct, the step-orphan is the bug we're fixing but the mid-cancel-disconnect race is a separate edge documented in F1 below.

### Task 3: Frontend — `showContinue` + `inputEnabled` between-steps (AC1, AC2, AC3, AC4, AC5, AC6, AC7, AC8)

- [x] 3.1 Drop `!isInteractive` from `showContinue` at line 926.

1. Change:
   ```ts
   const showContinue = !isInteractive
     && inst.status === "Running" && !hasActiveStep && !this._streaming
     && inst.steps.some((s) => s.status === "Pending");
   ```
   to:
   ```ts
   // Story 10.10: Continue button now renders for interactive mode too —
   // previously only autonomous mode surfaced this affordance between steps,
   // leaving interactive users stuck with a broken chat input. The /start
   // endpoint already handles Running + between-steps for both modes.
   const showContinue = inst.status === "Running" && !hasActiveStep && !this._streaming
     && inst.steps.some((s) => s.status === "Pending");
   ```

- [x] 3.2 Tighten interactive-mode `inputEnabled` at line 959.

1. Between the existing `_viewingStepId` / `_agentResponding` branches and the `_streaming || activeStep || inst.status === "Running"` branch, insert a new between-steps branch. Final shape of the interactive block:
   ```ts
   } else if (isInteractive) {
     if (isTerminal) {
       inputEnabled = false;
       inputPlaceholder =
         inst.status === "Cancelled" ? "Run cancelled."
         : inst.status === "Failed" ? "Run failed — click Retry to resume."
         : "Workflow complete.";
     } else if (this._viewingStepId) {
       inputEnabled = false;
       inputPlaceholder = "Viewing step history";
     } else if (this._agentResponding) {
       inputEnabled = false;
       inputPlaceholder = "Agent is responding...";
     } else if (showContinue) {
       // Story 10.10: between-steps state — disable chat, point user at Continue
       inputEnabled = false;
       inputPlaceholder = "Click Continue to run the next step.";
     } else if (this._streaming || activeStep) {
       // Streaming but agent idle (waiting for input), or active step exists
       inputEnabled = true;
       inputPlaceholder = "Message the agent...";
     } else {
       inputEnabled = false;
       inputPlaceholder = "Send a message to start.";
     }
   }
   ```
2. Note the critical changes:
   - Added `else if (showContinue) { ... }` branch above the streaming/active branch.
   - Removed `|| inst.status === "Running"` from the streaming/active condition. The between-steps case is now explicitly handled; the fallback `else` covers any unforeseen state (defaults to disabled, safer than enabling).
3. `showContinue` is computed just above this block — using it directly is idiomatic and keeps the gate consistent with the button rendering. If the button appears, the input is disabled with the Continue hint; if the button is hidden, the input follows the existing rules.
4. Do NOT change the autonomous branch at line 967. Its `inputEnabled = this._streaming && !this._viewingStepId` already evaluates false when `this._streaming` is false, so autonomous-between-steps already has input disabled — no regression to guard.

### Task 4: Frontend — `StepStatus.Cancelled` rendering (AC13)

- [x] 4.1 Add `"Cancelled"` cases to [instance-detail-helpers.ts](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts).

1. `stepSubtitle`:
   ```ts
   case "Cancelled":
     return "Cancelled";
   ```
2. `stepIconName`:
   ```ts
   case "Cancelled":
     return "icon-block";
   ```
   Alternative icons considered: `icon-stop`, `icon-circle-slash`. `icon-block` communicates "stopped" cleanly and is visually distinct from `icon-check` (Complete) and `icon-wrong` (Error). If `icon-block` does not exist in the UUI icon set, substitute `icon-stop`; verify availability in [the UUI icons library](https://uui.umbraco.com/?path=/story/symbols-icon--all-icons) before committing.
3. `stepIconColor`:
   ```ts
   case "Cancelled":
     return undefined; // neutral — instance-level Cancelled badge carries the signal
   ```
   Do NOT use `"danger"` — Cancel is a user-initiated intentional stop, not an error. Symmetric with how the step icon for `Complete` uses `"positive"` while Completed instances don't show alarming colours.
4. `shouldAnimateStepIcon`: **no change**. Its existing guard `status === "Active" && isStreaming` is structurally false for `Cancelled`.

### Task 5: Backend tests — CancelInstance step cleanup (AC10, AC11, AC12)

- [x] 5.1 Extend [InstanceEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs).

1. **`CancelInstance_WithActiveStep_SetsStepToCancelled`** — build an `InstanceState` with `Status = Running` and one step at `StepStatus.Active`. Configure `_instanceManager` to return this from `FindInstanceAsync`. Stub `SetInstanceStatusAsync(Cancelled)` to return the state (with Status mutated to Cancelled). Call `CancelInstance`. Assert:
   - `SetInstanceStatusAsync(..., InstanceStatus.Cancelled, ...)` was called once.
   - `UpdateStepStatusAsync(..., activeStepIndex, StepStatus.Cancelled, ...)` was called once (use NSubstitute's `.Received(1)` with matchers).
   - `RequestCancellation(id)` was called once.
   - Response is `OkObjectResult` with the expected `InstanceResponse` payload.
   - Use `Received.InOrder(...)` or sequential `.Received()` calls to pin the ordering: SetInstanceStatus → UpdateStepStatus → RequestCancellation.
2. **`CancelInstance_WithNoActiveStep_StillSucceeds`** — build an instance with `Status = Running` and all steps `Pending` or `Complete`. Call `CancelInstance`. Assert:
   - `SetInstanceStatusAsync(Cancelled)` called once.
   - `UpdateStepStatusAsync` NOT called (use `.DidNotReceive()`).
   - `RequestCancellation` called once.
   - Response is `OkObjectResult`.
3. **`CancelInstance_WithMultipleActiveSteps_CleansAllOfThem`** — build a pathological instance with two steps at `StepStatus.Active` (shouldn't happen but the loop should handle it). Assert `UpdateStepStatusAsync` called twice, once per step index.
4. **`CancelInstance_StepCleanupBeforeCancellation`** — regression assertion on ordering. Use `Received.InOrder(() => { ... })` or sequential setup to confirm step-status writes happen BEFORE `RequestCancellation`. The "persist first, signal second" policy from 10.8 extends here.

**Test pattern reminders:** NUnit 4 `[Test]` + `Assert.That(..., Is.EqualTo(...))`; NSubstitute for mocks; follow the existing `_endpoints`, `_instanceManager`, `_activeInstanceRegistry` fixture pattern in `InstanceEndpointsTests.cs`.

### Task 6: Backend tests — `StepStatus.Cancelled` serialization + round-trip (AC9)

- [x] 6.1 Extend [InstanceManagerTests.cs](../../AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs).

1. **`UpdateStepStatusAsync_SetsCancelledStatus`** — create an instance via `CreateInstanceAsync`, call `UpdateStepStatusAsync(..., 0, StepStatus.Cancelled, ...)`, read it back via `FindInstanceAsync`, assert `steps[0].Status == StepStatus.Cancelled`. Pins the YAML round-trip.
2. **`StepStatus_Cancelled_SerializesAsCancelledString`** — one-line assertion that `JsonSerializer.Serialize(StepStatus.Cancelled, JsonSerializerOptions.Web)` equals `"\"Cancelled\""`. Add to the same test file that covers the 10.9 equivalent for `InstanceStatus.Interrupted` (most likely `InstanceStatusSerializationTests.cs` or `InstanceStatusTests.cs` under `AgentRun.Umbraco.Tests/Instances/`). If no such file exists, add the assertion to `InstanceManagerTests.cs`.

### Task 7: Frontend tests — Continue button + input gate (AC1, AC2, AC3, AC4, AC5, AC6)

- [x] 7.1 Extend [agentrun-instance-detail.element.test.ts](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts).

1. **Continue button renders for interactive mode between steps** — fixture: interactive workflow, `status: "Running"`, steps: `[{ status: "Complete" }, { status: "Pending" }]`, `_streaming: false`. Assert `uui-button[label="Continue"]` is present.
2. **Continue button still renders for autonomous mode between steps** — same fixture but `workflowMode: "autonomous"`. Assert Continue is present (regression guard).
3. **Continue button hidden when active step exists** — fixture with `steps: [{ status: "Active" }, { status: "Pending" }]`. Assert Continue is absent, chat input is enabled.
4. **Continue button hidden for terminal states** — iterate over `"Completed" | "Failed" | "Cancelled" | "Interrupted"`, assert Continue absent for each.
5. **Chat input disabled between steps (interactive)** — fixture from test 1. Assert `input-enabled=false` and `input-placeholder="Click Continue to run the next step."` on the `agentrun-chat-panel`.
6. **Chat input re-enables when streaming starts** — fixture from test 1, then set `this._streaming = true` on the element and await `updateComplete`. Assert `input-enabled=true` and `input-placeholder="Message the agent..."`.

Follow existing test patterns in the file — fixtures via `html` + `fixture(...)`, status mutation via direct property assignment, use `await el.updateComplete` between mutations.

### Task 8: Frontend tests — step-detail helpers for Cancelled (AC13)

- [x] 8.1 Extend [instance-detail-helpers.test.ts](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.test.ts) (check for existence — if absent, follow the existing frontend-test convention; the equivalent `instance-list-helpers.test.ts` may be the reference pattern).
      **Dev note:** no dedicated `instance-detail-helpers.test.ts` file exists — the helpers are tested inside `agentrun-instance-detail.element.test.ts` (matches existing repo convention; the `stepSubtitle`/`stepIconName`/`stepIconColor`/`shouldAnimateStepIcon` coverage lives there). Added the Cancelled cases in the same file.

1. `stepSubtitle({ status: "Cancelled" })` returns `"Cancelled"`.
2. `stepIconName("Cancelled")` returns `"icon-block"` (or the agreed alternative).
3. `stepIconColor("Cancelled")` returns `undefined`.
4. `shouldAnimateStepIcon("Cancelled", true)` returns `false`.
5. `shouldAnimateStepIcon("Cancelled", false)` returns `false`.

### Task 9: Manual E2E verification

- [x] 9.1 **Resume interactive workflow between steps.** Start a fresh Content Quality Audit instance (interactive-mode workflow). Paste 3–5 URLs. Wait for the scanner step to complete fully (post-write summary appears, step icon flips to Complete). Do NOT click anything yet.
- [x] 9.2 Close the tab (Cmd+W). Reopen the Umbraco backoffice. Navigate to the AgentRun section → Workflows → Content Quality Audit → Instances. Click the instance.
- [x] 9.3 Observe the detail view. Confirm:
  - Status badge reads **Running** (not Failed, not Interrupted — the scanner completed and advanced cleanly; no SSE disconnect occurred here because the tab-close happened after the stream had already ended).
  - The **Continue** button is visible in the header.
  - The chat input is **disabled**, placeholder reads **"Click Continue to run the next step."**
  - The Cancel button is hidden (no step is active).
  - The chat transcript shows the scanner's completed conversation (loaded from the last Complete step's history).
- [x] 9.4 Click **Continue**. Confirm:
  - SSE stream starts, the analyser step transitions to `Active`, step icon starts animating (spinner).
  - Chat input re-enables with placeholder "Message the agent..." — though the analyser is non-interactive internally, so the user doesn't type anything.
  - Analyser completes and the reporter step runs (per CQA workflow definition — all three steps are actually marked interactive at workflow level but only the scanner asks for input; analyser and reporter are effectively autonomous within their step).
  - Workflow completes end-to-end.
- [x] 9.5 **Alternative resume via F5.** Repeat 9.1–9.2 but use F5 to refresh mid-scanner (while scanner is streaming). Confirm the Story 10.9 Interrupted flow fires (status → Interrupted, Retry button renders). This is the 10.9 happy path; re-verify it is NOT broken by 10.10's changes — the `showContinue` gate now includes `inst.status === "Running"` which does NOT match Interrupted, so Continue is correctly hidden for Interrupted (Retry is the right affordance).
- [x] 9.6 **Cancel mid-step — verify step cleanup.** Start a fresh CQA instance. Paste URLs, wait for the scanner to start fetching (2–3 fetch_url calls visible in the chat). Click **Cancel**. Confirm the existing Story 10.8 cancel behaviour (clean stop, Cancelled badge). Navigate away and return to the instance. Confirm:
  - The scanner step icon renders in a neutral/non-active state (no spinner, no current-colour highlight, subtitle reads **"Cancelled"**).
  - Previous pre-10.10 bug: the scanner would show as "In progress" (Active subtitle) with `current`-coloured icon next to a Cancelled badge — visually broken. Verify this no longer reproduces.
- [x] 9.7 **Click the cancelled step.** Confirm the chat transcript for the cancelled scanner step loads (step-click history navigation — AC14). The user can review what fetch_url calls happened before cancel.
- [x] 9.8 **Regression — cancel on a Pending instance.** Create an instance but don't click Start. Cancel it. Confirm:
  - Instance status → Cancelled.
  - No step was Active, so no step-status write occurred (AC11 path).
  - No error or exception surfaced.
- [x] 9.9 **Regression — delete a cancelled instance.** (verified persistence on disk: instance `a69dd6af876a4455b489f42efaec2f82` shows `status: Cancelled`, scanner step `status: Cancelled`, analyser/reporter `status: Pending` — the full Bug B fix lands end-to-end. Delete UI not exercised; locked-decision 5 + DeleteInstance accepts Cancelled is unit-test-covered.) On a cancelled instance from 9.6, click Delete (if surfaced in the UI) or call `DELETE /.../{id}` directly. Confirm 204 and instance folder removed. Delete already accepts Cancelled — no change needed, but verify.
- [x] 9.10 Run `dotnet test AgentRun.Umbraco.slnx` — **615/615** pass (baseline 609/609 at end of 10.9; +6 tests: 4 CancelInstance cleanup + 2 StepStatus.Cancelled serialization/round-trip).
- [x] 9.11 Run `npm test` in `AgentRun.Umbraco/Client/` — **182/182** pass (baseline 170/170; +12 tests: Story 10.10 interactive-resume describe block + Cancelled helpers describe block; updated the existing "Continue header button shows only for autonomous mode" test to reflect the new mode-agnostic gate).
- [x] 9.12 Run `npm run build` in `AgentRun.Umbraco/Client/` — frontend typecheck + Vite production bundle clean.

## Failure & Edge Cases

### F1: Cancel endpoint interrupted mid-write (between `SetInstanceStatusAsync` and `UpdateStepStatusAsync`)

**When** the HTTP request is aborted (client disconnects) in the microsecond window between the instance-status write and the step-status write
**Then** instance status = Cancelled (landed); step status = Active (orphan remains)
**And** the orchestrator observes `RequestCancellation` never fired (because the controller exited early), continues running; eventually hits the SSE OCE handler's `Cancelled` branch at [ExecutionEndpoints.cs:225](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L225) which preserves Cancelled and returns cleanly
**But** the Active step stays orphaned — same as the pre-10.10 bug
**Mitigation:** accepted for this story. The mid-cancel-disconnect race is sub-millisecond and requires the user to simultaneously click Cancel and close the tab. A future hardening could move the step-cleanup into `SetInstanceStatusAsync` itself when transitioning to Cancelled — but that couples the instance-status method to step state, which is a larger refactor. Defer.

### F2: `UpdateStepStatusAsync(Cancelled)` fails (disk write error)

**When** the per-step write fails after the instance-status write succeeded
**Then** the exception propagates out of `CancelInstance`, surfaces as a 500 to the client
**And** `RequestCancellation` is NOT called (the catch/throw aborts before that line)
**And** the orchestrator continues running until it finishes naturally or another cancel attempt succeeds
**Accepted:** same error surface as a failed `SetInstanceStatusAsync`. The user clicks Cancel again; the re-read state shows Cancelled (already written), the per-step retry succeeds, `RequestCancellation` fires. Idempotent cancel is out of scope for 10.10 — documented in deferred-work under Story 10.1's per-instance locking work.

### F3: Orchestrator writes `StepStatus.Complete` in the window between `SetInstanceStatusAsync(Cancelled)` and `UpdateStepStatusAsync(Cancelled)`

**When** the step completes (writes Complete) in the microsecond window
**Then** the subsequent `UpdateStepStatusAsync(Cancelled)` overwrites Complete with Cancelled
**And** the final persisted state is `steps[i].Status == Cancelled`
**Accepted:** cancel intent wins (AC15). The alternative — reading the step status and skipping the write if already Complete — would produce a historical record "step completed just as user cancelled" that is technically accurate but UX-confusing (user clicked Cancel; they expect to see Cancelled).

### F4: Multiple cancel calls race (two tabs, two clicks)

**When** two `POST .../cancel` requests arrive concurrently
**Then** both call `SetInstanceStatusAsync(Cancelled)` — the second finds state already Cancelled, the terminal-transition guard at [InstanceManager.cs:222](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L222) refuses the sideways transition and returns the state as-is (Cancelled)
**And** both attempt the step-status cleanup loop — the first sets Active → Cancelled, the second finds no Active step (all Cancelled now) and the loop completes with zero writes
**And** both call `RequestCancellation` — `ActiveInstanceRegistry.RequestCancellation` is idempotent (cancelling an already-cancelled CTS is a no-op)
**Net effect:** both requests return 200; the persisted state is consistent. No double-cleanup, no exception. Accepted without special-casing.

### F5: Cancel arrives after the orchestrator has advanced past the Active step (race)

**When** the orchestrator completes a step and advances `CurrentStepIndex`; the next step has not yet transitioned to Active; user clicks Cancel in that window
**Then** `CancelInstance` reads state: one step Complete, no Active step
**And** `SetInstanceStatusAsync(Cancelled)` lands
**And** the step-cleanup loop finds zero Active steps, does nothing (AC11 path)
**And** `RequestCancellation` fires; the orchestrator (which has not yet started the next step) observes the CTS and returns cleanly
**Net effect:** instance Cancelled; no step marked Cancelled (because none was Active at the cancel moment). The previously-Complete step retains its Complete status — correct. The not-yet-started next step stays Pending — correct.

### F6: Cancel on an Interrupted instance

**When** an instance is in `InstanceStatus.Interrupted` (post-10.9 disconnect state)
**Then** the existing [`InstanceEndpoints.CancelInstance` guard at line 99](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L99) `state.Status is not (Running or Pending)` rejects with 409 (existing behaviour — Story 10.9 AC8)
**And** no step-cleanup runs (guard rejects before the cleanup loop)
**No new behaviour:** Interrupted is recoverable via Retry; the user must Retry, not Cancel. If the user wants to exit the run without Retry, the Delete affordance accepts Interrupted (10.9 AC7).

### F7: Step click on a `Cancelled` step loads JSONL transcript

**When** the user clicks a Cancelled step in the sidebar (AC14 path)
**Then** `_onStepClick` at [line 352](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L352) runs: the `Pending` early-return guard at line 353 does not match `Cancelled`, so execution continues
**And** `_viewingStepId = step.id`; `getConversation(instanceId, step.id, token)` loads the JSONL transcript for that step
**And** the chat panel renders `_historyMessages` (via the `_viewingStepId` branch at [line 1011](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L1011))
**And** the input is disabled with placeholder `"Viewing step history"` (existing logic at line 954)
**Net effect:** user can review what happened before cancel, matching AC14.

### F8: Step click on an `Active` step that's actually an orphan from a pre-10.10 cancelled instance

**When** (migration edge) a user opens an instance that was cancelled BEFORE this story shipped — the YAML on disk has `instance.Status == Cancelled` but `steps[i].Status == Active`
**Then** the frontend renders the orphan as Active (existing helpers apply — subtitle "In progress", current-coloured icon)
**And** `shouldAnimateStepIcon("Active", false)` returns false (since `_streaming == false` on a reopened terminal instance) — no spinner, but still visually "live"
**Mitigation options:**
- (a) Do nothing — pre-existing cancelled instances stay as-is; the orphan is only cosmetic (no spinner) and new instances post-10.10 are clean
- (b) On instance-load, if `instance.Status == Cancelled` and any step is Active, patch the frontend rendering to show Cancelled subtitle/icon without touching the persisted YAML
- (c) Data migration on first load post-10.10 that rewrites stale Active steps on Cancelled instances
**Recommendation:** (a) — do nothing. Private-beta users have minimal instance history; the migration complexity is not worth it. If a post-public-launch complaint surfaces, (b) is the lightest fix. Document under deferred-work if (b) becomes needed.

### F9: Continue clicked on a between-steps instance that has somehow lost its Pending steps

**When** (pathological) `showContinue` evaluates true but the instance actually has no Pending steps (bug state — the `inst.steps.some((s) => s.status === "Pending")` clause should prevent this, but defensive)
**Then** `/start` at [ExecutionEndpoints.cs:51](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L51) is called; `ExecuteNextStepAsync` at [WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) finds no pending step, transitions the instance to Completed, emits run.finished(Completed), and returns
**Net effect:** the instance correctly finalises. User sees Completed badge. No error. Accepted as a graceful fallback.

### F10: Between-steps state persists for days (long-lived Running)

**When** an instance sits in the between-steps Running state for an extended period (user left for the weekend)
**Then** the instance status stays Running; `UpdatedAt` reflects the last orchestrator write; no stale-instance sweep runs (AgentRun has no stale-run detector as of this story)
**On reopen** the Continue button surfaces normally and the user can resume or Cancel
**Accepted:** long-lived Running instances are a known consequence of interactive-mode UX. Stale-run detection (mark instances as Abandoned after N hours of no activity) is out of scope — future work, possibly Story 12.x territory.

### F11: Cancel during a step that happens to have no `Active` status yet (very narrow startup race)

**When** the user clicks Cancel in the sub-millisecond window between `RegisterInstance` (in the SSE handler) and the StepExecutor's first `UpdateStepStatusAsync(Active)` call
**Then** `CancelInstance` reads: instance Running, all steps Pending (no Active yet)
**And** step-cleanup loop finds zero Active steps (AC11 path)
**And** `RequestCancellation` fires; the StepExecutor observes the CTS before the first Active write happens, the step stays Pending
**Net effect:** Cancelled instance with all steps Pending. Clean state. No orphan. Matches F5's shape — the cleanup loop was a no-op, which is correct because there was no Active step to clean.

## Dev Notes

### Why the Continue button is the right fix (not auto-resume)

The bug report's two hypotheses (orchestrator doesn't advance on reopen; chat posts to wrong step) both pointed at the same underlying seam: **interactive mode pauses between steps, and there is no UI affordance to resume.** Auto-resume would satisfy the narrow case where all remaining steps are autonomous-within-step (scanner → analyser → reporter, where only the scanner asks for input) but would violate interactive semantics for mixed workflows where a later step may need user input before running. The Continue button makes the pause-and-resume model explicit: the user chose interactive mode because they want to review between steps.

The button already exists for autonomous mode — Story 10.10's delta is just extending the gate. Zero new UI surface.

### Why `StepStatus.Cancelled` (not reset-to-Pending)

Two alternatives were considered for Bug B:

| Option | Behaviour | Verdict |
|---|---|---|
| Reset Active → Pending on cancel | Active step returns to the "not yet started" state | Rejected — semantically wrong (the step was started and then stopped; Pending implies never started). Also loses historical signal for the step-click history load (AC14 — user wants to see what happened before cancel). |
| Add `StepStatus.Cancelled` | New terminal step status alongside Complete/Error | Selected — preserves history, renders cleanly, symmetric with `InstanceStatus.Cancelled`. Matches the 10.9 precedent of adding new enum values rather than overloading existing ones. |

### Why cancel endpoint (not SSE OCE handler)

The cancel endpoint is the transactional owner of the cancel intent. Putting the step-cleanup there means:

- Single-source-of-truth for the cancel transaction: one endpoint call, one atomic-enough write sequence, one return.
- The OCE handler stays reactive — it reads whatever state the cancel endpoint wrote.
- If the OCE handler ever races ahead of the cancel endpoint's writes (e.g., orchestrator observes cancellation and returns before `UpdateStepStatusAsync` lands), the step is already Cancelled on disk — no cleanup needed. If not, the cleanup happens in the endpoint before the OCE handler sees anything.
- No duplicate writes, no conflicting sources of truth, no "defence-in-depth" layer to maintain.

Locked decision 4 codifies this. Story 10.9's disconnect branch does not clean up steps because Interrupted is recoverable (Retry does the cleanup via step-discovery + reset-to-Pending). Cancel is terminal — step-cleanup must happen at the cancel boundary.

### Why the chat input between-steps gate is not `inst.status === "Running" && !activeStep && !_streaming`

The simplest condition that captures "Running with no orchestrator" would be `inst.status === "Running" && !activeStep && !this._streaming` — the exact shape of `showContinue` (minus the Pending-step check). Using `showContinue` itself makes the dependency explicit and ensures the input gate and the button gate can never drift. If future changes to `showContinue` add or remove conditions, the input gate follows automatically.

This is a deliberate coupling. Symmetric pair: "if the Continue button is visible, the input is disabled with the Continue hint."

### Chat input in autonomous mode between steps — no change

The autonomous `inputEnabled` branch at line 967 is `this._streaming && !this._viewingStepId`. When not streaming, input is already disabled (autonomous workflows never accept user input anyway). The `showContinue` button in autonomous mode was already the resume affordance; this story doesn't touch autonomous behaviour. Confirmed regression-covered by AC2.

### What NOT to do

- Do NOT auto-start the SSE stream on instance reopen. Interactive workflows pause between steps by design; auto-start violates that.
- Do NOT rework `_onSendAndStream` to try to Continue first, then send the message. The between-steps case has no chat input enabled — the user clicks Continue explicitly. `_onSendAndStream` remains for the "active step, not streaming" case (unusual but exists when a mid-step stream was torn down and not yet re-opened).
- Do NOT add `StepStatus.Cancelled` to `Retry`'s step-discovery. Cancelled is terminal and intentional — Retry doesn't apply. Delete is the affordance (already wired).
- Do NOT emit SSE events from `UpdateStepStatusAsync(Cancelled)`. The instance-level `run.finished(Cancelled)` already exists at [ExecutionEndpoints.cs:241](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L241) — that's the terminal signal for observers.
- Do NOT add a `StepStatus.Cancelled` to `shouldAnimateStepIcon`'s spin list. Cancelled is not live, should not spin. Existing gate (`Active && isStreaming`) is correct; adding `Cancelled` would be a bug.
- Do NOT change the cancel endpoint's guard at [InstanceEndpoints.cs:99](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L99). `not (Running or Pending)` is correct — cancel on Interrupted/Failed/Completed/Cancelled is rejected (existing behaviour).
- Do NOT migrate pre-10.10 cancelled instances to backfill `StepStatus.Cancelled`. F8 covers the rationale — private-beta users have no meaningful instance history to migrate.
- Do NOT couple `SetInstanceStatusAsync` to step-status writes. Keep `InstanceManager` methods single-responsibility. The cancel endpoint orchestrates both writes explicitly.

### Test patterns

- **Backend:** NUnit 4 with `[TestFixture]`, `[Test]`, `Assert.That(...)`. NSubstitute for mocking. Use `Received.InOrder` when asserting call ordering (e.g., SetInstanceStatus → UpdateStepStatus → RequestCancellation).
- **Frontend:** `@open-wc/testing` + `describe/it/expect`. Fixtures via `html` + `fixture(...)`. Status mutation via direct property assignment on the element, then `await el.updateComplete` before assertions.
- **Run all tests:** `dotnet test AgentRun.Umbraco.slnx` (specify slnx — never bare `dotnet test`; see `feedback_dotnet_test_slnx.md`). Frontend: `npm test` from `AgentRun.Umbraco/Client/`.

### Project Structure Notes

- Backend changes live in `AgentRun.Umbraco/Endpoints/` (InstanceEndpoints) and `AgentRun.Umbraco/Instances/` (StepStatus enum).
- Frontend changes live in `AgentRun.Umbraco/Client/src/components/` (detail element) and `AgentRun.Umbraco/Client/src/utils/` (helpers).
- Engine boundary preserved: no Engine/ code is touched. The orchestrator's step-advancement behaviour is unchanged; 10.10 only adds the UI affordance and the cancel-endpoint cleanup.
- DI: no changes — `InstanceEndpoints` already has `_instanceManager` injected, which has the `UpdateStepStatusAsync` method.
- No new NuGet dependencies. No new files (enum value additions and helper-function extensions only).

### Serialization notes

- `StepStatus` uses `[JsonConverter(typeof(JsonStringEnumConverter))]` at the enum level — JSON produces `"Cancelled"` automatically.
- YAML serialization via `InstanceManager._yamlSerializer` uses `UnderscoredNamingConvention` (per the existing serializer config). Enum values are serialized by YamlDotNet's default: the enum identifier verbatim (e.g., `Cancelled`). Verify via the AC9 round-trip test — if YamlDotNet produces `cancelled` (lowercase), the deserializer is symmetric and both round-trip correctly.
- `StepState` (the class containing `Status`) is a mutable class with a `StepStatus` property — it inherits the converter automatically via the enum's attribute.
- API models (`InstanceDetailResponse.Steps[i].Status`) are typed as `StepStatus` — they inherit the converter automatically.

### Frontend type system

- The `StepDetailResponse.status` field is typed as `StepStatus` (or equivalent string union) in `api/types.ts`. Verify whether the TS type needs an explicit `"Cancelled"` addition to the union. If the type is `string` or `StepStatus = "Pending" | "Active" | "Complete" | "Error"`, extend it. The switch-default fallbacks in the helpers (`default: return status;`) mean runtime will work even without the type update, but the typecheck will fail if the union is restrictive.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story-10.10](../planning-artifacts/epics.md) — epic-level story definition and beta known-issue listing
- [Source: _bmad-output/planning-artifacts/bug-finding-2026-04-10-instance-resume-after-step-completion.md](../planning-artifacts/bug-finding-2026-04-10-instance-resume-after-step-completion.md) — original bug finding from Story 9.1c Task 7.4 manual E2E (2026-04-10)
- [Source: _bmad-output/implementation-artifacts/deferred-work.md#Deferred-from-code-review-of-story-10-8-cancel-wiring-2026-04-14](./deferred-work.md) — 10.8 code review carry-over requirement: "`StepStatus.Active` orphan after cancel" — STORY 10.10 MUST PICK THIS UP (explicit handoff text)
- [Source: _bmad-output/implementation-artifacts/10-8-cancel-wiring-cancellation-token-per-instance.md](./10-8-cancel-wiring-cancellation-token-per-instance.md) — predecessor; established the cancel endpoint's "persist first, signal second" pattern
- [Source: _bmad-output/implementation-artifacts/10-9-sse-disconnect-resilience.md](./10-9-sse-disconnect-resilience.md) — predecessor; added `InstanceStatus.Interrupted` and the Retry(Interrupted) path that resets `StepStatus.Active` → Pending. 10.10 extends the same pattern to cancel via `StepStatus.Cancelled`.
- [Source: AgentRun.Umbraco/Instances/StepStatus.cs](../../AgentRun.Umbraco/Instances/StepStatus.cs) — enum to extend
- [Source: AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs:83-117](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L83) — `CancelInstance` endpoint to extend
- [Source: AgentRun.Umbraco/Instances/InstanceManager.cs:152-193](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L152) — `UpdateStepStatusAsync` (existing method, called from the new cleanup loop)
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:46-107](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L46) — `/start` endpoint (reused by Continue button; no change)
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:344-373](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L344) — `/message` endpoint (reference for the 409 `not_running` that triggers the bug's symptom; no change)
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:319-333](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L319) — conversation-history load on instance load (no change — binding to last Complete step is correct for between-steps; locked decision 7)
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:737-765](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L737) — `_onSendMessage` (legacy mid-step handler; unchanged)
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:767-799](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L767) — `_onSendAndStream` (unchanged — only fires when activeStep exists, which excludes the between-steps case under the new gate)
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:910-975](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L910) — render() header and input-gate block to extend
- [Source: AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) — step-display helpers to extend with `Cancelled` cases
- [Source: AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs:150-180](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L150) — interactive-mode step-advancement (the `return` after each step — unchanged; this is the behaviour the Continue button resumes)

## Dev Agent Record

### Agent Model Used

Amelia (claude-opus-4-6[1m])

### Debug Log References

- `dotnet build AgentRun.Umbraco.slnx` — clean (2 pre-existing warnings in GetContentTool.cs, unrelated). Adding `StepStatus.Cancelled` surfaced no exhaustive-switch warnings — no C# code switches on `StepStatus` today, only the frontend helpers updated under Task 4.
- `dotnet test AgentRun.Umbraco.slnx` — 615/615 pass (+6 over 10.9 baseline of 609).
- `npm test` (in `AgentRun.Umbraco/Client/`) — 182/182 pass (+12 over 10.9 baseline of 170).
- `npm run build` — clean.

### Completion Notes List

- **Task 1:** `StepStatus.Cancelled` appended after `Error` (not inserted) per story spec on source-order preservation for YAML/JSON compatibility.
- **Task 2:** Step cleanup loop added to `CancelInstance` in the order SetInstanceStatus(Cancelled) → per-step UpdateStepStatus(Cancelled) → RequestCancellation (locked decision 4 + Story 10.8 "persist first, signal second"). `cancellationToken` is threaded to `UpdateStepStatusAsync` per the story's mid-cancel-disconnect accepted-edge note.
- **Task 3:** `showContinue` is now mode-agnostic — the `!isInteractive` clause is gone. The interactive `inputEnabled` block gained an explicit `showContinue` branch above the streaming/active branch, and the fall-through `|| inst.status === "Running"` clause was removed from the streaming/active condition (the between-steps case is now handled explicitly; unforeseen states fall to the safer disabled `else`). The gate is coupled to `showContinue` by design — if the button is visible, the input is disabled with the Continue hint.
- **Task 4:** Added `Cancelled` cases to `stepSubtitle`, `stepIconName` (→ `icon-block`; verified present in `@umbraco-cms/backoffice` icon registry), and `stepIconColor` (→ `undefined`). `shouldAnimateStepIcon` intentionally unchanged — gate is `status === "Active" && isStreaming`, structurally false for Cancelled.
- **Task 5:** Four NSubstitute-based tests added to `InstanceEndpointsTests.cs` — WithActiveStep (AC10), WithNoActiveStep (AC11), WithMultipleActiveSteps (AC12), and StepCleanupRunsBeforeRequestCancellation (AC15 / ordering regression). Used a local `CreateRunningInstanceWithStepStatuses` helper to model arbitrary step-status vectors.
- **Task 6:** Added `StepStatus_Cancelled_SerializesAsCancelledString` (JSON round-trip via `[JsonStringEnumConverter]`) and `UpdateStepStatusAsync_SetsCancelledStatus` (full YAML round-trip through real `InstanceManager` + temp dir) alongside the existing 10.9 `InstanceStatus_Interrupted_SerializesAsInterruptedString` test.
- **Task 7:** Introduced a new `describe("Story 10.10 — interactive resume between steps", ...)` block with predicate-mirror tests for the `showContinue` gate (AC1–4) and the interactive `inputEnabled`/`inputPlaceholder` gate (AC5, AC6, plus a regression assertion for the pre-10.10 bug shape). Updated the pre-existing "Continue header button shows only for autonomous mode" test — the old assertion is now wrong under the new mode-agnostic gate, so the test name and body were rewritten to pin the new behaviour.
- **Task 8:** No dedicated `instance-detail-helpers.test.ts` exists — the repo convention is to co-locate helper tests inside `agentrun-instance-detail.element.test.ts` (where the pre-existing `stepSubtitle`/`stepIconName`/`stepIconColor`/`shouldAnimateStepIcon` tests already live). Added a `describe("Story 10.10 — StepStatus.Cancelled helpers", ...)` block alongside the interactive-resume block.
- **Task 9:** Steps 9.10–9.12 (test/build commands) run green. Steps 9.1–9.9 (browser manual E2E against a running TestSite) are the user's gate — left unchecked pending Adam's walkthrough per the project's "Manual E2E finds seam bugs" convention.
- **No locked decisions broken.** `WorkflowOrchestrator.cs`, `ExecutionEndpoints.cs`, `ActiveInstanceRegistry.cs`, `ConversationStore.cs`, and the conversation-history-load path were not touched. `Retry`'s step-discovery was NOT extended to `Cancelled` (locked decision 5). No SSE emit was added for step-level cancel (locked decision 8). No migration of pre-10.10 cancelled instances (F8 accepted as (a) — do nothing).

### File List

**Production code:**

- `AgentRun.Umbraco/Instances/StepStatus.cs` — added `Cancelled` enum value
- `AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs` — `CancelInstance` step-cleanup loop
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` — `showContinue` mode-agnostic + interactive `inputEnabled` between-steps branch
- `AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts` — `Cancelled` cases in `stepSubtitle`/`stepIconName`/`stepIconColor`

**Tests:**

- `AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs` — +4 CancelInstance cleanup tests
- `AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs` — +2 StepStatus.Cancelled serialization + YAML round-trip tests
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts` — +12 tests (Story 10.10 interactive-resume + Cancelled helpers describe blocks); updated 1 pre-existing test (Continue header button mode gate)

**Story tracking:**

- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 10-10 ready-for-dev → in-progress → review
- `_bmad-output/implementation-artifacts/10-10-instance-resume-after-step-completion.md` — task checkboxes, Dev Agent Record, Change Log, Status

### Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-14 | Initial story creation by Bob (SM) — covers the bug-finding-2026-04-10 primary bug (chat rejection on resume) and the 10.8 code review carry-over (`StepStatus.Active` orphan on cancel). Two-part scope: (A) frontend Continue button extension + chat input between-steps gate, (B) new `StepStatus.Cancelled` enum + cancel-endpoint step cleanup. 15 ACs, 9 manual E2E steps, 11 failure/edge cases. Locked decisions 1–10 to prevent relitigation. Dependencies on 10.8 + 10.9 both satisfied (done). |
| 0.2 | 2026-04-14 | Tasks 1–8 implemented by Amelia (dev agent). Backend: `StepStatus.Cancelled` added; `CancelInstance` runs per-step cleanup between instance-status write and CTS signal. Frontend: `showContinue` is mode-agnostic; interactive `inputEnabled` gains an explicit between-steps branch coupled to `showContinue` and loses the fall-through `Running` clause. Helpers render `Cancelled` with `icon-block`, neutral colour, no spin. Tests: +6 backend (615/615), +12 frontend (182/182), build clean. Status → review; manual E2E (Tasks 9.1–9.9) awaits Adam. |
| 0.3 | 2026-04-14 | Code review (Blind Hunter + Edge Case Hunter + Acceptance Auditor) applied. Extracted `shouldShowContinueButton` and `computeChatInputGate` to `instance-detail-helpers.ts` so production render and tests share one function (closes predicate-mirror drift risk). Added `SetInstanceStatusAsync` receive-assertion to `CancelInstance_WithNoActiveStep_StillSucceeds`; added `RetryInstance_CancelledInstance_Returns409` pinning locked-decision 5. Restored `workflowMode` thread-through on the pre-existing Continue-button test. Five deferred items logged to `deferred-work.md` (orchestrator post-step Cancelled branch, conversation-history-load for cancelled reopen, AC5 `viewingStepId` mock gap, AC9 raw YAML assertion, locked-decision-2 negative test). Tests: 616/616 backend, 183/183 frontend, build clean. Status → done; manual E2E (9.1–9.9) remains Adam's gate. |
| 0.4 | 2026-04-14 | Manual E2E 9.1–9.9 walked end-to-end by Adam against the running TestSite. All three blocks (Bug A resume affordance, Bug B step cleanup, regressions) pass. One in-scope fix during E2E: duplicate Continue affordance — header Continue rendered alongside the existing in-session "Continue to next step" completion banner. Resolved by introducing `isBetweenSteps` as the structural condition (decoupled from button visibility) and gating the header Continue with `isBetweenSteps && !showCompletionBanner`. The chat input gate now uses `isBetweenSteps` directly so input disables correctly whether the banner or the header button is the visible affordance. Two related-but-out-of-scope findings logged to `deferred-work.md` as a single combined fast-follow story (10.13 candidate): (a) `run.finished` SSE handler hardcodes "Completed" status + emits "Workflow complete." regardless of actual final status; (b) Pending-cancel path leaves the chat blank with no confirmation message. Both share the same fix surface (chat-window terminal-state messaging). Frontend tests 183/183 against the renamed gate input. AC9 + AC10 + AC11 + AC15 verified on disk via instance `a69dd6af876a4455b489f42efaec2f82` (`status: Cancelled`, scanner `status: Cancelled`, analyser/reporter `status: Pending`). Story 10.10 fully closed. |

### Review Findings

_Code review 2026-04-14 — Blind Hunter + Edge Case Hunter + Acceptance Auditor. 1 decision-needed, 3 patch, 5 defer, 15 dismissed as noise. All decision-needed + patch items resolved._

- [x] [Review][Decision→Patched] Frontend tests use predicate-mirrors, not DOM fixtures — chose option (c) (single DOM smoke test) but discovered the repo has no DOM-fixture infrastructure and the element depends on Bellissima auth context + fetch, making a single smoke test surprisingly expensive. Fixed instead by extracting `shouldShowContinueButton(...)` and `computeChatInputGate(...)` to `instance-detail-helpers.ts`; the production `render()` and the Story 10.10 tests now call the **same exported function**, so drift is structurally impossible — stronger than a DOM smoke test and cheaper than a framework lift. [instance-detail-helpers.ts:72-168, agentrun-instance-detail.element.ts:927-961, agentrun-instance-detail.element.test.ts:441-543]
- [x] [Review][Patch] AC2 regression assertion is tautological — restored `workflowMode` thread-through in the pre-existing "Continue header button shows for both modes between steps" test; the new Story 10.10 tests call the real `shouldShowContinueButton` helper directly (no mirror to drift). The absence of a mode parameter on the helper signature is now the structural guarantee that `!isInteractive` cannot be reintroduced. [agentrun-instance-detail.element.test.ts:306-328, 446-493]
- [x] [Review][Patch] `CancelInstance_WithNoActiveStep_StillSucceeds` now asserts `SetInstanceStatusAsync(Cancelled)` was received once. [InstanceEndpointsTests.cs:74-76]
- [x] [Review][Patch] Added `RetryInstance_CancelledInstance_Returns409` — pins locked-decision 5 (Cancelled is not retry-eligible, step-discovery never matches Cancelled). [ExecutionEndpointsTests.cs:101-130]
- [x] [Review][Defer] Orchestrator post-step `stepState.Status` check has no `Cancelled` branch — `WorkflowOrchestrator.cs:120` only checks `Error` and falls through to the success path (emits `step.finished(Complete)`, advances). Scenario: `ExecuteStepAsync` returns cleanly with Complete → `CancelInstance` cleanup overwrites Complete→Cancelled (F3 race) → orchestrator re-read at line 117 reads Cancelled → falls through. Window is sub-millisecond (the cleanup loop writes between successful step completion and the `RequestCancellation` that cancels `runToken`, and the re-read normally throws OCE once the token is cancelled). Locked decision 4 supports keeping cleanup in the cancel endpoint rather than adding defence-in-depth in the orchestrator; locked decision 8 argues against emitting per-step cancel SSE. Deferred — document as new edge case; patch only if observed in manual E2E. [WorkflowOrchestrator.cs:120-172]
- [x] [Review][Defer] `_loadData` conversation-history selection does not surface the Cancelled step's transcript on reopen — current logic (`find(Active/Error)` then fallback to last Complete) yields empty chat for a cancelled instance whose only meaningful step is Cancelled. Locked decision 7 explicitly forbids changing this logic for now; AC14's step-click path loads the Cancelled JSONL on demand. Deferred — revisit if beta feedback highlights the empty-chat-on-reopen UX. [agentrun-instance-detail.element.ts:319-333]
- [x] [Review][Defer] AC5 mock gate not exercised with `viewingStepId=true` — `interactiveInputGate` accepts the flag but no test sets it to verify viewing-step-history wins over the new `showContinue` branch. Trivial to add (single `it` block) but not blocking. [agentrun-instance-detail.element.test.ts:254-280]
- [x] [Review][Defer] AC9 YAML round-trip does not assert raw on-disk string representation — test only asserts enum round-trip via `GetInstanceAsync`. Enum persistence convention is stable; drift extremely unlikely. [InstanceManagerTests.cs:173-186]
- [x] [Review][Defer] Locked decision 2 (no auto-resume) has no explicit negative test — no test asserts that reopening a between-steps instance does NOT invoke `_onStartClick`. Trusting absence is fine but untested. [agentrun-instance-detail.element.test.ts]
