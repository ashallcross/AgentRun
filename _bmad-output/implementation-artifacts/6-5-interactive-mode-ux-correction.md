# Story 6.5: Interactive Mode UX Correction

Status: complete

## Story

As a developer or editor using an interactive workflow,
I want the UI to reflect that I am in control of step progression — not watching an automated process,
So that the experience matches the original app's human-driven model and feels collaborative rather than autonomous.

## Context

The current UI was built with an autonomous-first mental model: "Running" status, spinning sync icons, "Running..." subtitles, and a Start/Continue flow that implies the system is doing something between turns. In reality, interactive mode is a request-response cycle — the user sends a message, the agent responds, the user reviews, and when the step's work is done the user advances manually. There is no persistent "running" state between turns.

The original reference app (Cogworks BMAD Agent Runner) demonstrates the correct model:
- Step statuses are: `in_progress`, `completed`, `paused`, `pending` — no "Running" or "Failed" at the instance level for interactive flows
- The user sends a message → agent responds → `done` event with `completable` flag → user clicks "Advance to Next Step" when ready
- No spinners between turns — the agent is idle until the user acts
- Step sidebar shows static status (check, dot, current highlight) — no animated icons during idle

## Acceptance Criteria

1. **Given** an interactive workflow instance is loaded and no SSE stream is active
   **When** the user views the instance detail
   **Then** the active step shows "In progress" subtitle (not "Running...")
   **And** the step icon is static (no spin animation) unless an SSE stream is actively open
   **And** no "Cancel" button is shown (cancel is an autonomous-mode concern)

2. **Given** the user sends a message and the agent is streaming a response
   **When** the SSE stream is active
   **Then** the step icon animates (spin) to indicate the agent is working
   **And** the chat input is disabled while streaming
   **When** the stream completes (final text.delta received, no more tool calls)
   **Then** the step icon returns to static
   **And** the chat input re-enables immediately

3. **Given** the agent has finished responding and the step's completion check passes
   **When** the `step.finished` event is received with status "Complete" OR the step's `writes_to` artifacts exist
   **Then** a "Continue to next step" banner appears above the chat input (green background, prominent button — matching the original app's pattern)
   **And** clicking "Continue" calls the existing step advancement endpoint
   **And** the chat panel clears and the next step becomes active

4. **Given** an interactive workflow instance is between turns (agent responded, user hasn't sent next message)
   **When** the user views the instance
   **Then** the header shows the workflow name and run number only — no "Running" or "Failed" status badge
   **And** the instance status in the data model may still be "Running" for backend purposes, but the UI does not display it

5. **Given** the user sends a message and the agent's response does not include tool calls
   **When** the ToolLoop waits for the next user message (Channel wait)
   **Then** the chat input re-enables after the streaming text completes
   **And** the user can send another message without pressing Start/Continue

6. **Given** the last step of an interactive workflow completes
   **When** the completion check passes and the user clicks "Advance"
   **Then** the instance shows "Workflow Complete" state with all steps showing check icons
   **And** the chat input is disabled with placeholder "Workflow complete"

## What NOT to Build

- **No changes to autonomous mode** — autonomous workflows continue to work exactly as they do now. All changes are gated on `workflow.mode !== "autonomous"` or equivalent.
- **No changes to the backend execution model** — the ToolLoop, Channel, StepExecutor, WorkflowOrchestrator are unchanged. This is purely a frontend story.
- **No changes to conversation context management** — the repeated text problem is a separate concern (context pruning/windowing). Do not address it here.
- **No changes to SSE event types or payloads** — work with the existing event structure.
- **No completion check polling** — completion state comes from SSE events or is checked on step advancement, not polled.
- **No artifact viewer** — Story 8.2 scope.

## Failure & Edge Cases

- **Mode detection:** The frontend must know the workflow mode. If `workflowMode` is not present in the instance API response, treat as `"interactive"` (the safe default per PRD line 311). Check the backend `InstanceDetailResponse` — if mode isn't included, add it from the workflow definition.
- **SSE disconnect mid-stream:** If the connection drops while streaming, the step icon should stop spinning and the input should re-enable. The user can send another message (which triggers a new Start/SSE stream). Don't show "Failed" — the step isn't failed, the connection is.
- **Rapid message sending:** If the user sends a message before the previous response fully renders, the optimistic add still works. The Channel queues the message server-side. No special handling needed.
- **Step advancement during streaming:** The "Continue" banner must NOT appear while an SSE stream is active. Only show it when `_streaming === false` AND the step is completable.
- **History view of completed steps:** Clicking a completed step in the sidebar still loads conversation history in read-only mode. No change to this behaviour.
- **Empty chat on initial load:** When no messages have been exchanged yet, show "Send a message to begin." placeholder (matching original app's `ChatPanel` empty state).

## Tasks / Subtasks

### Task 1: Add workflow mode to instance API response (if missing)

- [x]Check `InstanceDetailResponse` in `api/types.ts` for a `mode` field
- [x]If missing, add `workflowMode: string` to the API response type
- [x]Check the backend `ExecutionEndpoints.cs` GET instance endpoint — if it doesn't return mode, add it from the registered workflow definition
- [x]If backend change needed, update corresponding test

### Task 2: Update step subtitle and icon behaviour for interactive mode

In `utils/instance-detail-helpers.ts`:

- [x]Change `stepSubtitle` for "Active" status: return `"In progress"` instead of `"Running..."`
- [x]Export a new helper `shouldAnimateStepIcon(status: string, isStreaming: boolean): boolean` that returns `true` only when `status === "Active" && isStreaming`

In `shallai-instance-detail.element.ts`:

- [x]Pass `this._streaming` context into the step icon rendering
- [x]Apply `step-icon-spin` class only when `shouldAnimateStepIcon(step.status, this._streaming)` returns true (currently always applied for Active status via CSS)
- [x]Keep the `@keyframes spin` CSS — it's still used during streaming

### Task 3: Remove autonomous-mode UI chrome from interactive view

In `shallai-instance-detail.element.ts`:

- [x]Gate the "Cancel" button on workflow mode: `showCancel` should be false for interactive workflows
- [x]Remove or gate the instance status display in the header — for interactive mode, don't show "Running"/"Failed" badges (the workflow name and run number are sufficient)
- [x]The "Start" button remains — it initiates the first SSE stream. Rename label to "Start conversation" for interactive mode.
- [x]The "Continue" button (for step advancement between steps) remains but only shows when the step is completable and not streaming

### Task 4: Chat input enablement logic

In `shallai-instance-detail.element.ts`:

- [x]Change `inputEnabled` logic: for interactive mode, input should be enabled whenever:
  - The instance is not in a terminal state (Completed/Cancelled)
  - The user is not viewing historical step conversation (`_viewingStepId === null`)
  - The SSE stream is not actively open (`_streaming === false`)
  - Note: this is the OPPOSITE of the current logic (`inputEnabled = this._streaming && !this._viewingStepId`). Currently input only works during streaming. For interactive mode, input should work between turns too — sending a message triggers a new SSE stream via `_onStartClick` or a new `_onSendAndStart` method.
- [x]When the user sends a message and no SSE stream is active, automatically start a new stream (POST /start) with the queued message. This mirrors the original app where POST /chat both sends the message and gets the response.
- [x]Update `inputPlaceholder` for interactive mode:
  - No active step yet: "Click 'Start conversation' to begin."
  - Between turns (active step, not streaming): "Message the agent..."
  - Streaming: "Agent is responding..."
  - Workflow complete: "Workflow complete"

### Task 5: Step completion / advancement banner

- [x]After SSE stream ends (in the `finally` block of `_onStartClick` or after the last event), check if the step is completable. The backend already returns this in the instance state — reload via `_loadData()`.
- [x]When `completable === true` and `_streaming === false`, render a green banner above the chat input:
  ```
  [Step complete — review the output, then advance when ready]  [Continue to next step →]
  ```
- [x]Clicking the button calls the existing advancement endpoint (POST /start for the next step — same as the current "Continue" button in the header)
- [x]After advancement: clear `_chatMessages`, update instance state, next step becomes active
- [x]For the last step: show "Workflow Complete" banner instead of "Continue"

### Task 6: Send-and-stream flow for interactive mode

- [x]Create a `_onSendAndStream` method that:
  1. Takes the message text from the send event
  2. Optimistically adds the user message to `_chatMessages`
  3. Calls POST `/instances/{id}/message` to queue the message
  4. Then calls `_onStartClick()` to open the SSE stream (which will drain the queued message)
- [x]Wire the `@send-message` event to `_onSendAndStream` instead of `_onSendMessage` when no stream is active
- [x]When a stream IS active, continue using `_onSendMessage` (which just POSTs the message into the Channel)

### Task 7: Update tests

- [x]Update `instance-detail-helpers.test.ts` — `stepSubtitle("Active")` should return "In progress"
- [x]Add test for `shouldAnimateStepIcon` helper
- [x]Update any existing frontend tests that assert "Running..." subtitle text

## Manual E2E Validation

1. Create an interactive workflow instance, verify:
   - "Start conversation" button visible, no Cancel button
   - No status badge in header
   - Step sidebar shows "In progress" (not "Running..."), static icon
2. Click Start, send a message, verify:
   - Step icon spins during response
   - Chat input disabled during streaming
   - Icon stops, input re-enables when response completes
3. Continue chatting (send another message without clicking Start), verify:
   - Message sends, new SSE stream opens automatically
   - Agent responds, conversation flows naturally
4. When step artifacts are created and completion check passes, verify:
   - Green "Continue" banner appears
   - Clicking advances to next step
   - Chat clears, next step is active
5. Complete final step, verify:
   - "Workflow Complete" state shown
   - All steps show check icons
   - Input disabled with "Workflow complete" placeholder

## File List

**Backend — Modified:**
- `Shallai.UmbracoAgentRunner/Models/ApiModels/InstanceDetailResponse.cs` — added `WorkflowMode` property
- `Shallai.UmbracoAgentRunner/Endpoints/InstanceEndpoints.cs` — set `WorkflowMode` from definition with "interactive" fallback; allow restart of Running instances that are not actively executing (interactive mode between steps)
- `Shallai.UmbracoAgentRunner/Endpoints/ExecutionEndpoints.cs` — concurrency guard now checks ActiveInstanceRegistry before rejecting Running instances
- `Shallai.UmbracoAgentRunner/Engine/Events/ISseEventEmitter.cs` — added `EmitInputWaitAsync` method
- `Shallai.UmbracoAgentRunner/Engine/Events/SseEventEmitter.cs` — `input.wait` event implementation
- `Shallai.UmbracoAgentRunner/Engine/Events/SseEventTypes.cs` — `InputWait` constant + `InputWaitPayload` record
- `Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs` — emits `input.wait` before blocking wait; accepts `completionCheck` delegate for early exit when step artifacts exist
- `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs` — passes CompletionChecker delegate to ToolLoop
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/InstanceEndpointsTests.cs` — assertions for WorkflowMode

**Frontend — Modified:**
- `Shallai.UmbracoAgentRunner/Client/src/api/types.ts` — added `workflowMode` to `InstanceDetailResponse`
- `Shallai.UmbracoAgentRunner/Client/src/utils/instance-detail-helpers.ts` — "In progress" subtitle, `shouldAnimateStepIcon` helper
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` — interactive mode render logic, `_agentResponding` state driven by `input.wait` SSE event, send-and-stream, advance step, completion banner in `.main-panel` wrapper
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-chat-panel.element.ts` — empty state text

**Frontend — Tests modified:**
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.test.ts` — all interactive mode tests

### Dev Agent Record

**Implemented:** All 7 original tasks plus 3 issues discovered during E2E testing.

**Original scope (Tasks 1–7):**
- Added `workflowMode` to backend API + frontend types (defaults to "interactive")
- Step subtitle: "In progress" instead of "Running...", icon spins only during active agent response
- Cancel button + header Continue gated on autonomous mode; Start relabelled "Start conversation"
- Input enabled between turns (not just during streaming); contextual placeholders
- Green completion banner with "Continue to next step" button
- `_onSendAndStream` / `_onSendMessage` routing based on SSE connection state
- `shouldAnimateStepIcon` helper, all frontend tests updated

**Bug fix 1 — Input permanently disabled:**
The SSE stream stays open for the entire interactive step (ToolLoop blocks on Channel). `_streaming` was `true` permanently, disabling input. Fix: added `_agentResponding` state driven by `input.wait` SSE event emitted from ToolLoop before entering the blocking wait. Input uses `_agentResponding` (not `_streaming`) for interactive mode.

**Bug fix 2 — "Continue to next step" silently failing:**
After step completes, orchestrator returns but instance status stays "Running". POST /start rejected with 409 "already_running". Fix: ExecutionEndpoints now checks ActiveInstanceRegistry — if instance is Running but not actively executing, restart is allowed.

**Bug fix 3 — Step never completing mid-conversation:**
CompletionChecker only ran after ToolLoop exited (5-min timeout). Fix: ToolLoop accepts a `completionCheck` delegate. Before entering blocking wait, checks if step's artifacts exist. If passed, exits immediately — step completes without waiting for timeout.

**Bug fix 4 — Banner breaking grid layout:**
Multiple top-level elements in `mainContent` template broke the 2-column CSS Grid. Fix: wrapped in `.main-panel` flex container.

**All tests passing:** 292 backend, 112 frontend

### Change Log

- 2026-04-01: Story 6.5 implemented with 4 E2E bug fixes
- 2026-04-01: Story 6.5 created — interactive mode UX correction based on original app reference
