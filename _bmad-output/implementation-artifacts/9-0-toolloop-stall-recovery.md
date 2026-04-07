# Story 9.0: ToolLoop Stall Recovery — BETA BLOCKER

Status: done

**Depends on:** 9.6 (uses configurable `user_message_timeout_seconds` via `IToolLimitResolver`)
**Blocks:** 9.1c, 9.1b, 9.4 (all need the stall fix to be testable end-to-end)

> **BETA BLOCKER.** Live testing of the Content Quality Audit example workflow on 2026-04-07 stalled silently for ~5 minutes after a successful `fetch_url` and then surfaced as a generic timeout. This is the worst possible first-run UX. Story 9.0 makes the engine fail fast and visibly when the LLM stops mid-task. Story 9.6 has already shipped the configurable user-message timeout that this story consumes.

## Story

As a developer running an interactive workflow,
I want the engine to correctly distinguish between "agent waiting for user input", "agent has completed its work", and "agent has stalled mid-task",
So that workflows fail fast and visibly when the agent stops responding instead of hanging silently until the user-message timeout fires.

## Context

**UX Mode: Interactive (primary).** Stall detection only matters when the engine is currently waiting on the LLM in interactive mode — autonomous mode does not exhibit the bug because it has no user-input wait state to collapse into. Frontend changes are minimal: a clear error message in the chat panel and the existing retry affordance from Story 7.2.

This story exists because of one specific live test trace. Embed it verbatim in the dev agent's working memory:

**Live test trace — instance `642b6c583e3540cda11a8d88938f37e1`, Content Scanner step:**

- **Turn 4 (assistant tool call, `2026-04-07T13:21:47.982Z`):** `fetch_url({"url":"https://www.wearecogworks.com"})`
- **Turn 5 (tool result, immediately after):** `<!DOCTYPE html><html lang="en-GB"><head><title>Cogworks — Digital products, Umbraco development & DXP …</title>` *(truncated; ~150 KB of body returned successfully)*
- **(~5 minutes 47 seconds of LLM silence — no tool call, no text)**
- **Turn 6 (system, `2026-04-07T13:27:34.699Z`):** `Step failed: user message wait timed out`

The engine misclassified an empty assistant turn following a tool result as "the agent is asking the user a question" and sat on the user-message timeout window. It is not. Per the scanner agent prompt, the only valid next action after a successful `fetch_url` is another tool call (`write_file` or `fetch_url` for the next URL). Empty content is definitionally a stall.

**Architectural decision (LOCKED — do not re-litigate):**

**Fail fast, do not retry-with-nudge.** When a stall is detected, the engine immediately marks the step Errored and surfaces a clear error in the chat panel. Recovery is via the user clicking retry (the existing Story 7.2 flow). The engine does NOT auto-inject a "continue your task" nudge prompt.

Reasons (these are the locked rationale, copy them into the dev notes):

1. **Simpler to build.** No nudge prompt design, no retry counter, no nudge-budget.
2. **Easier to debug.** A stall produces a single visible error event, not a quiet retry that may also stall.
3. **Prevents masking weak prompts.** The prompt-quality work in Story 9.1b is the *real* fix for "the LLM keeps narrating instead of calling tools." If the engine silently retries-with-nudge, weak prompts get covered up and 9.1b can't see what it's fixing. Stalls are diagnostic.
4. **Pluggable strategy door stays open.** The retry-with-nudge alternative is documented in `_bmad-output/planning-artifacts/v2-future-considerations.md` and can be added as a swappable recovery strategy in V2 *if* beta telemetry shows stalls happening more than occasionally. The design constraint in this story (separate stall *detection* from stall *recovery action*) keeps that door open.

**Trichotomy for LLM response classification (LOCKED):**

When the LLM returns from streaming with **zero tool calls** and we are in interactive mode, classify the assistant turn as one of:

| Classification | Detection | Action |
|---|---|---|
| **Stall — empty content** | `accumulatedText` is null/empty/whitespace AND no tool calls | Fail fast: stall error within 10s |
| **Waiting for user** | `accumulatedText` is non-empty AND ends with `?` (after trimming trailing whitespace) | Existing input-wait path (block on `userMessageReader` with the resolved `user_message_timeout_seconds` from 9.6) |
| **Stall — narration without tool call** | `accumulatedText` is non-empty AND does NOT end with `?` | Fail fast: stall error within 10s |

The "narration is a stall" call is deliberate. It forces the prompt-quality fix in Story 9.1b rather than papering over weak prompts at the engine layer. If a workflow author legitimately wants the agent to make a statement and then wait for the user, the agent must phrase it as a question (end with `?`). This is documented in the error message and will be reinforced by the prompt conventions Story 9.1c/9.1b establish.

**Stall detection only applies when the previous message was a tool result.** If the assistant produces an empty turn at the very *start* of a step (before any tool result), that's a different bug class — provider error or prompt-loading bug — and the existing LLM error path (Story 7.1) handles it. Stall detection is specifically about "the LLM had a tool result in front of it and produced nothing useful in response."

**Resolved timeout source (CRITICAL):**

This story consumes the configurable `tool_loop.user_message_timeout_seconds` resolved by `IToolLimitResolver.ResolveToolLoopUserMessageTimeoutSeconds(step, workflow)` from Story 9.6. **Do not** use the deleted hardcoded `TimeSpan.FromMinutes(5)` constant — it no longer exists. **Do not** introduce a new hardcoded constant for the stall window. The 10-second stall window is itself an `EngineDefaults` constant (see Dev Notes); it is not configurable in this story (configurability for the stall window is V2 if anyone asks).

**Reuses Story 7.2 retry plumbing.** Do NOT build new retry infrastructure. The existing `POST /umbraco/api/agentrun/instances/{id}/steps/{stepId}/retry` endpoint, the conversation truncation logic, and the chat-panel retry button all already work for Errored steps. A stall produces a normal step Error using the existing path. Verify by inspecting the 7.2 retry flow before implementing.

**What the live trace also tells us about completion checking:** the scanner step has a completion check (the `artifacts/scan-results.md` file existing). The current code path runs the completion check inside the input-wait branch *after* draining user messages but *before* blocking on the timeout. That logic is correct and stays. The stall detection sits *between* the "no function calls" branch entry and the "we are about to wait for user input" decision.

## Acceptance Criteria

1. **Given** a workflow step is executing in interactive mode with `userMessageReader != null`,
   **When** the LLM returns from streaming with zero `FunctionCallContent` items AND empty assistant text content AND the most recent non-assistant message in the conversation is a `ChatRole.Tool` message,
   **Then** the engine raises a stall — the step is marked Errored within 10 seconds (not after the user-message timeout window),
   **And** the chat panel surfaces the error message: `"The agent stopped responding mid-task. Click retry to try again."`,
   **And** the step can be retried via the existing Story 7.2 retry endpoint and chat-panel button without code changes to either,
   **And** the error is logged at `LogLevel.Warning` with structured fields `WorkflowAlias`, `InstanceId`, `StepId`, and `LastToolCall` (the tool name from the most recent `FunctionCallContent` in the conversation, or `"none"` if there is none).

2. **Given** a workflow step is executing in interactive mode,
   **When** the LLM returns from streaming with zero `FunctionCallContent` items AND non-empty assistant text content AND that text content (after trimming trailing whitespace) ends with `?` AND the most recent non-assistant message is a `ChatRole.Tool` message,
   **Then** the engine treats this as "waiting for user input" — the existing input-wait branch runs unchanged (drain queue → completion check → emit `input.wait` → block on `userMessageReader.WaitToReadAsync` with the resolved `user_message_timeout_seconds`),
   **And** when the user replies, the ToolLoop resumes the same iteration without exiting the step (the existing multi-turn behaviour),
   **And** the step is NOT marked Errored.

3. **Given** a workflow step is executing in interactive mode,
   **When** the LLM returns from streaming with zero `FunctionCallContent` items AND non-empty assistant text content that does NOT end with `?` (e.g. `"Let me now process that and write the results to a file."`) AND the most recent non-assistant message is a `ChatRole.Tool` message,
   **Then** the engine raises a stall (same path as AC #1) — narration without a tool call is treated as a stall, not as a wait-for-user state,
   **And** the same error message and retry path apply,
   **And** the structured log entry's `LastToolCall` field carries the tool name from the preceding tool call so the operator can see what the LLM saw before stalling.

4. **Given** a workflow step is executing in interactive mode,
   **When** the LLM returns from streaming with zero `FunctionCallContent` items AND the most recent non-assistant message is NOT a `ChatRole.Tool` message (i.e. there has been no preceding tool call in this turn),
   **Then** stall detection does NOT trigger — the existing input-wait branch runs unchanged. (This protects the legitimate "step starts, agent greets the user, waits for first input" case from being misclassified as a stall.)

5. **Given** a workflow step is executing in interactive mode,
   **When** the LLM returns one or more `FunctionCallContent` items,
   **Then** stall detection does NOT trigger — the existing tool-execution branch runs unchanged regardless of whether there is also assistant text content alongside the tool calls.

6. **Given** a workflow step is executing in **autonomous** mode (`userMessageReader == null`),
   **When** the LLM returns from streaming with zero function calls,
   **Then** the existing autonomous-mode exit branch runs unchanged (return the last assistant message and let the step executor handle completion checking) — stall detection is interactive-only and does NOT alter autonomous behaviour. Existing autonomous-mode tests continue to pass without modification.

7. **Given** a stall has been detected and an error raised,
   **When** the user clicks "Retry" in the chat panel,
   **Then** the existing Story 7.2 retry flow runs unchanged — the conversation is truncated to the last successful user/tool boundary, the step status flips Failed → Pending → Running, and the ToolLoop is re-entered cleanly. There is no special retry path for stalls.

8. **Given** the stall detection logic exists,
   **When** a future contributor wants to add an alternative recovery strategy (e.g. retry-with-nudge),
   **Then** the stall *detection* code is cleanly separable from the *recovery action* — detection returns a `StallClassification` value (or equivalent), and the action ("throw stall exception now") is invoked by the caller. Swapping in a different action does not require modifying the detector. **Verified by** the unit tests covering the detector with no I/O dependencies.

9. **Given** all backend changes,
   **When** `dotnet test AgentRun.Umbraco.slnx` runs,
   **Then** all existing tests pass and the new stall-detection tests (~10 tests, see Tasks) pass,
   **And** `npm test` in `Client/` passes (no frontend logic changes other than the error message text — see Task 5).

10. **Given** an existing workflow that does not exhibit the stall pattern (i.e. the LLM always either returns a tool call or asks a question ending in `?`),
    **When** it runs after this story ships,
    **Then** behaviour is unchanged. **No backwards-incompatible change** for workflows that were already well-prompted.

## What NOT to Build

- **Do NOT implement retry-with-nudge.** Locked decision. Documented in `v2-future-considerations.md`. If the dev agent finds itself writing a "let me retry with a nudge" code path, stop and re-read the Architectural Decision section.
- **Do NOT change the user-message timeout value.** That's Story 9.6's surface (already shipped). This story consumes the resolved value, it does not redefine it.
- **Do NOT introduce a configurable stall-window setting.** The 10-second stall-error window is a fixed engine default for this story. Configurability for it is V2 territory if anyone asks.
- **Do NOT modify the completion-check file detection logic.** This story is about ToolLoop exit detection in the interactive empty-turn case, not about completion checking.
- **Do NOT modify the Story 7.2 retry flow** — endpoint, conversation truncation, chat-panel retry button all stay as-is. Reuse, do not re-implement.
- **Do NOT use the deleted `TimeSpan.FromMinutes(5)` constant** — it no longer exists after 9.6. Do not reintroduce it. Do not introduce a new hardcoded user-message timeout anywhere.
- **Do NOT touch `MaxIterations` (line 11 of `ToolLoop.cs`) or any other ToolLoop constant other than what this story explicitly modifies.** Out of scope; that's V2.
- **Do NOT add a new tool, a new workflow YAML key, or a new appsettings section.** This is engine logic only.
- **Do NOT change autonomous-mode behaviour.** Stall detection is interactive-only (AC #6). If the dev agent finds itself touching the `userMessageReader is null` branch beyond the trivial guard, stop.
- **Do NOT change the SSE event taxonomy.** The stall surfaces via the existing `run.error` event with a clear error message — no new event type. (If the frontend genuinely needs a distinct `run.stall` event, flag it to Adam first; the default is to reuse `run.error`.)
- **Do NOT introduce a `?`-detection regex more sophisticated than `text.TrimEnd().EndsWith('?')`.** YAGNI. If the prompt produces ambiguous endings (e.g. `?!`, smart-quote question marks `？`), Story 9.1b's prompt work fixes the prompt, not the detector. Document this in dev notes.

## Failure & Edge Cases

- **Empty assistant turn after a tool result** (the live trace case). → Stall, fail fast within 10s. Logged with `LastToolCall` populated. Step Errored. Retry path works.
- **Whitespace-only assistant turn after a tool result** (`"   \n  "`). → Stall. `string.IsNullOrWhiteSpace(accumulatedText)` is the canonical check. Same behaviour as empty.
- **Narrative text after a tool result, no tool call, does not end with `?`** (`"Let me now process that and write the results to a file."`). → Stall. AC #3. The narration is the bug; the engine surfaces it.
- **Question text after a tool result, ending in `?`** (`"I retrieved the page. Would you like me to also fetch the sitemap?"`). → Wait for user. AC #2. Existing path.
- **Question text after a tool result, ending in `?\n  `** (trailing whitespace). → Wait for user. The detector trims trailing whitespace before checking. Tested.
- **Question text after a tool result, ending in a smart-quote question mark `？`**. → Currently classified as a stall (the detector checks for ASCII `?` only). This is the documented behaviour for the beta. If a beta user reports it as a false positive, Story 9.1b's prompt work fixes the prompt (instruct the LLM to use ASCII `?`). Engine does not change.
- **Question text after a tool result, ending in `?!`** → Currently classified as a stall (the trimmed text ends in `!`, not `?`). Same disposition: prompt fix in 9.1b, not engine fix here.
- **Empty assistant turn at the very start of a step** (no preceding tool result — the most recent non-assistant message is a `ChatRole.User` system-injected step start, or the conversation is empty). → NOT a stall. AC #4. The existing input-wait branch runs. (If this case ever produces an empty turn it's a provider/prompt bug, not a stall, and Story 7.1's LLM error detection is the right surface for it.)
- **Tool call AND text content in the same assistant turn.** → NOT a stall. AC #5. The tool branch runs, the text is recorded as accumulated assistant text via the existing flow. No change.
- **The most recent non-assistant message is a `ChatRole.User` message** (the user injected a message mid-step via the multi-turn channel). → NOT a stall. AC #4. The existing input-wait branch runs because the LLM is responding to a user message, not to a tool result.
- **Multi-turn: the LLM correctly asks a question, the user replies, the LLM produces an empty turn after the user reply.** → The most recent non-assistant message is now `ChatRole.User`, so AC #4 applies — stall detection does NOT trigger. Whether this is "the LLM has finished and exits cleanly" or "the LLM is broken" is for Story 7.1 (LLM error detection) or the completion check, not for stall detection. Document in dev notes.
- **Stall fires while the conversation recorder is mid-write.** → The recorder is awaited before the stall exception is thrown. No partial-write race. Verified by the existing recorder test pattern.
- **Stall fires during the linked CTS for the user-message timeout.** → Cannot happen — stall detection runs *before* the timeout CTS is created. The detector lives at the top of the empty-tool-call branch, the timeout CTS is created later in the same branch.
- **Concurrent retry click while the stall error is still being emitted.** → The retry endpoint checks the step is in `Failed` status (Story 7.2's existing 409-on-non-Failed guard). If the click arrives before the status flip, the user gets a 409 and retries — same behaviour as any other quick-click race. No new logic.
- **Multiple consecutive stalls on retry** (the user keeps retrying and the LLM keeps stalling). → Each retry is its own attempt. Each can independently stall. No automatic behaviour change. (Suggestion for the operator — log a warning at `LogLevel.Warning` if the same step stalls 3+ times in a row, hinting at a possible prompt issue. This is observability only, no behaviour change.)
- **Stall fires in autonomous mode.** → Cannot happen by AC #6 — stall detection is gated on `userMessageReader != null`. Verified by an autonomous-mode test that produces an empty turn and asserts the existing exit path runs.
- **Deny-by-default statement (security):** The stall detector is a *state machine* over LLM output. Inputs that do not match a recognised classification (function calls present / waiting-for-user via `?` / preceding tool result with empty content / preceding tool result with narration) **must be denied (treated as stalls)**, never silently ignored or treated as "everything is fine, keep waiting." Defaulting to "wait silently" is exactly the bug this story exists to fix. The detector's default classification for an empty turn following a tool result is **Stall**, not **Wait**.

## Tasks / Subtasks

- [x] **Task 1: Define `StallDetector` (or equivalent) — pure detection logic** (AC: #1, #2, #3, #4, #5, #6, #8)
  - [x] 1.1 Create `AgentRun.Umbraco/Engine/StallDetector.cs`. Pure static class (or instance class with no dependencies — pick static, matching the `LlmErrorClassifier` pattern from Story 7.1). Zero Umbraco refs.
  - [x] 1.2 Define `public enum StallClassification { ToolCallsPresent, WaitingForUserInput, NotApplicableNoToolResult, StallEmptyContent, StallNarration }` in the same file.
  - [x] 1.3 Define `public static StallClassification Classify(IList<ChatMessage> messages, string accumulatedText, IReadOnlyList<FunctionCallContent> functionCalls)` — the single entry point.
  - [x] 1.4 Logic, in this exact order:
    1. If `functionCalls.Count > 0` → `ToolCallsPresent`. (Don't check anything else; tool branch will run.)
    2. Look up the most recent non-assistant message in `messages` (i.e. scan from `messages.Count - 1` backwards, skip `ChatRole.Assistant`, take the first hit). If none, OR its role is not `ChatRole.Tool` → `NotApplicableNoToolResult`. (The empty turn isn't following a tool result, so the input-wait branch runs.)
    3. The previous message *is* a tool result. Now classify by content:
       - If `string.IsNullOrWhiteSpace(accumulatedText)` → `StallEmptyContent`.
       - Else if `accumulatedText.TrimEnd().EndsWith('?')` → `WaitingForUserInput`.
       - Else → `StallNarration`.
  - [x] 1.5 Add an XML doc comment on `Classify` linking to this story file and explaining each branch in one sentence each.
  - [x] 1.6 Unit test fixture `AgentRun.Umbraco.Tests/Engine/StallDetectorTests.cs` covering at least:
    - tool calls present → `ToolCallsPresent` (with and without preceding tool result, with and without text)
    - empty `accumulatedText` after a tool result → `StallEmptyContent`
    - whitespace-only `accumulatedText` after a tool result → `StallEmptyContent`
    - narration ending with `.` after a tool result → `StallNarration`
    - text ending with `?` after a tool result → `WaitingForUserInput`
    - text ending with `?` followed by trailing whitespace after a tool result → `WaitingForUserInput`
    - text ending with `?!` after a tool result → `StallNarration` (documented edge case)
    - text ending with smart-quote `？` after a tool result → `StallNarration` (documented edge case)
    - empty text with no preceding tool result (most recent non-assistant is a `User` message) → `NotApplicableNoToolResult`
    - empty text with no messages at all → `NotApplicableNoToolResult`
    - **~10 tests total, justified for an engine state-machine boundary**

- [x] **Task 2: Define `StallDetectedException` and the engine-default stall window** (AC: #1, #3, #8)
  - [x] 2.1 Create `AgentRun.Umbraco/Engine/StallDetectedException.cs` extending `AgentRunException`. Constructor takes `(string lastToolCallName, string stepId, string instanceId, string workflowAlias)`. Composes the user-facing message exactly: `"The agent stopped responding mid-task. Click retry to try again."`. Exposes the four context fields as properties for structured logging.
  - [x] 2.2 Add `public const int StallDetectionWindowSeconds = 10;` to `EngineDefaults.cs` (the static class created in Story 9.6). This constant exists for documentation/observability — the actual stall-throw is synchronous and effectively immediate, but the constant declares the upper bound the AC promises and is referenced in the XML doc on `StallDetector.Classify`.
  - [x] 2.3 Verify `StallDetectedException` is caught by the existing endpoint exception filter (Story 7.1's path) — it inherits from `AgentRunException`, so it should flow through to the SSE `run.error` event without special handling. Add a comment in the exception file pointing at the existing handler.

- [x] **Task 3: Wire the detector into `ToolLoop.RunAsync`** (AC: #1, #2, #3, #4, #5, #6, #7)
  - [x] 3.1 In `AgentRun.Umbraco/Engine/ToolLoop.cs`, locate the empty-tool-call branch (currently starts at `if (functionCalls.Count == 0)` around line 101).
  - [x] 3.2 At the **top** of that branch, before the autonomous-mode early-return, call `var classification = StallDetector.Classify(messages, accumulatedText, functionCalls);`. Note that `functionCalls` is empty here so the detector will return one of the non-`ToolCallsPresent` values; passing it anyway keeps the entry point honest and matches the test fixtures.
  - [x] 3.3 If `userMessageReader is null` (autonomous mode) — keep the existing early-return path **unchanged**. Stall detection result is ignored in autonomous mode (AC #6). The classification call is harmless overhead in autonomous mode; do not add a guard to skip it (keeps the logic single-path and easier to test).
  - [x] 3.4 Interactive mode (`userMessageReader is not null`):
    - If `classification == StallClassification.StallEmptyContent` or `StallNarration`: extract the most recent `FunctionCallContent` from the conversation (scan backwards for the most recent assistant message and pick the last function call content from its `Contents`, or `"none"` if not found). Log at `LogLevel.Warning` with structured fields `WorkflowAlias`, `InstanceId`, `StepId`, `LastToolCall`, and a discriminator field `StallType` set to `"empty"` or `"narration"`. Then `throw new StallDetectedException(lastToolCallName, context.StepId, context.InstanceId, context.WorkflowAlias);`. Do NOT swallow; the exception propagates to the endpoint exception filter, which produces the `run.error` SSE event with the user-facing message.
    - If `classification == StallClassification.WaitingForUserInput` or `NotApplicableNoToolResult`: continue with the **existing** input-wait branch unchanged. (Drain → completion check → `EmitInputWaitAsync` → resolve timeout → block on `WaitToReadAsync` with linked CTS.) **Do not modify any line of the existing input-wait code.**
  - [x] 3.5 Verify that the existing partial-text recording on streaming exceptions (lines 70–84 today) still runs cleanly when the new exception path fires — the stall is thrown *after* `accumulatedText` has been recorded normally (line 88–91), so no partial-write concern.
  - [x] 3.6 Confirm the structured log fields match the project convention from `project-context.md`: `WorkflowAlias`, `InstanceId`, `StepId`, `ToolName` (or `LastToolCall` for this story — pick the more descriptive name and document why). Use `LastToolCall` since this is observability for the stall, not for an active tool execution.

- [x] **Task 4: Update `ToolLoopTests` to cover the new branches** (AC: #1, #2, #3, #4, #5, #6)
  - [x] 4.1 In `AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs`, add tests that drive the loop with a fake `IChatClient` returning controlled streaming responses. Use the existing test scaffolding pattern (the file already constructs `ToolExecutionContext`, `ChannelReader<string>`, etc.).
  - [x] 4.2 New tests required:
    - Interactive mode + empty assistant turn after a tool result → `StallDetectedException` thrown, error message matches the AC #1 string, `LastToolCall` is the tool name from the preceding call.
    - Interactive mode + narrative text after a tool result → `StallDetectedException` thrown, `StallType` log discriminator is `"narration"`.
    - Interactive mode + question text after a tool result → existing input-wait branch runs (use `userMessageTimeoutOverride` to keep the test fast). The test asserts the loop returns normally on user reply, NO exception thrown.
    - Interactive mode + empty turn with no preceding tool result → existing input-wait branch runs, NO stall.
    - Interactive mode + tool call present (with or without text) → tool branch runs, NO stall, NO input-wait.
    - **Autonomous mode + empty turn → existing exit branch runs, NO stall.** (AC #6 — critical regression guard.)
    - Multi-turn: user injects a message after the LLM asks a question, then the LLM produces an empty turn → AC #4 applies, NO stall (the most recent non-assistant message is now `User`, not `Tool`).
  - [x] 4.3 Use the `userMessageTimeoutOverride` parameter (added in Story 9.6) to keep tests fast. None of these tests should actually wait on a real timeout; the stall path is synchronous and the input-wait path can be tested with a 100ms override that sends a user message immediately via the channel.
  - [x] 4.4 Use `NSubstitute` for the `IChatClient` fake in line with the rest of `ToolLoopTests`. Build streaming responses by yielding `ChatResponseUpdate` instances from an `IAsyncEnumerable<ChatResponseUpdate>`.

- [x] **Task 5: Frontend — verify the stall error renders cleanly in the chat panel** (AC: #1, #7)
  - [x] 5.1 Inspect the existing `run.error` SSE handler in the chat panel (`Client/src/...` — look for the SSE event subscription set up in Story 6.2 / 7.1). Confirm an error message of the form `"The agent stopped responding mid-task. Click retry to try again."` renders as a normal error chat message and the existing retry button (Story 7.2) appears.
  - [x] 5.2 If the existing `run.error` rendering already handles arbitrary error strings (it should — Story 7.1 made this generic), **NO frontend code changes are required**. Document in the dev agent record that 5.2 was a verification step only.
  - [x] 5.3 If a regression is found (e.g. the chat panel filters error messages by exact string match from a previous story), fix it minimally — the stall message must render through. Do NOT redesign the error UX in this story. Flag any redesign need to Adam.
  - [x] 5.4 `npm test` in `Client/` — must remain green. If any frontend test snapshots a list of expected error strings, update only that snapshot.

- [x] **Task 6: Run the full test suite and confirm green** (AC: #9)
  - [x] 6.1 `dotnet test AgentRun.Umbraco.slnx` — full backend suite green. Existing baseline (~364 tests after 9.6) plus ~10 new from `StallDetectorTests` plus ~7 new in `ToolLoopTests` (one per AC #1, #2, #3, #4, #5, #6, plus one multi-turn). Target: ~10 net new tests, justified by the state-machine surface.
  - [x] 6.2 `npm test` in `Client/` — green (162/162 baseline; should be unchanged unless Task 5.3 fired).
  - [x] 6.3 Build with `dotnet build AgentRun.Umbraco.slnx` — zero errors, no new warnings.

- [x] **Task 7: Manual E2E validation (gate)** (AC: #1, #2, #7) — **gate, not refinement**
  - [x] 7.1 Run the TestSite (`dotnet run` from `AgentRun.Umbraco.TestSite/`).
  - [x] 7.2 **Reproduce the original stall.** Open the Content Quality Audit workflow, start an instance, paste `www.wearecogworks.com` (or any URL on which the live test stalled). Confirm that within ~10 seconds of the `fetch_url` returning, the chat panel surfaces the error `"The agent stopped responding mid-task. Click retry to try again."` and the step is marked Failed. Inspect the conversation JSONL to confirm the empty assistant turn was recorded before the stall fired. **This is the live regression check.** If the original prompt has been improved enough that it no longer stalls, that's fine — Adam confirms manually that the *trichotomy logic* runs correctly by temporarily editing the scanner agent prompt to force a narration ("After fetching, narrate what you found before calling write_file") and re-running.
  - [x] 7.3 **Confirm the wait-for-user path still works.** Start a fresh instance. When the scanner asks for URLs (its first turn — no preceding tool result), wait 30 seconds without responding. Confirm NO stall fires and the input-wait state holds. Then send a URL. Confirm the workflow resumes normally.
  - [x] 7.4 **Confirm the multi-turn path still works.** Start a fresh instance. Send a URL. After `fetch_url` returns, if the LLM legitimately asks a follow-up question ending in `?` (e.g. "Would you like me to also check the sitemap?"), confirm the input-wait state holds for that question and a user reply resumes the loop.
  - [x] 7.5 **Confirm the retry flow.** Trigger a stall (per 7.2). Click the retry button in the chat panel. Confirm the conversation truncates to the last user/tool boundary, the step flips back to Running, and the ToolLoop re-enters. (This is the existing 7.2 flow — the test is that nothing about it broke.)
  - [x] 7.6 **Confirm autonomous mode is unaffected.** (Covered by unit test `Stall_Autonomous_EmptyTurnAfterToolResult_DoesNotThrow_ExitsNormally`; live run waived — no autonomous workflow currently configured and the unit test exercises the exact `userMessageReader is null` guard.) Start any autonomous-mode workflow (the dev-testing autonomous workflow if one exists, or temporarily flip the example to `mode: autonomous` and back). Confirm an empty assistant turn does NOT trigger the stall exception in autonomous mode — it goes through the normal exit branch.
  - [~] 7.7 **Production smoke test** — **moved to pre-public-beta gate.** Story 9.0 only adds two files (`StallDetector.cs`, `StallDetectedException.cs`) into the existing `AgentRun.Umbraco/Engine/` folder — same assembly, no new csproj, no new content rule, no new asset type. The packaging risk this substep guards against does not exist for this story (9.6 shipped from the same folder fine). 7.7 is genuinely valuable as a **pre-public-beta** gate run once across the cumulative Epic 9 changes — tracked there, not here.

## Dev Notes

### Why detection lives in `Engine/StallDetector.cs` and not inline in `ToolLoop`

The locked architectural decision (separate detection from recovery) requires that a future contributor can swap in a different recovery action without modifying the detector. Inline `if/else if` chains in `ToolLoop.RunAsync` would conflate the two. Putting the detector in its own static class with a returned classification value means:

1. **Detection is unit-testable in isolation** without a fake `IChatClient`, fake channel, fake recorder, fake emitter — just construct messages and call `Classify`.
2. **Recovery is a single line in the ToolLoop** (`throw new StallDetectedException(...)`), trivially replaceable with `return AlternativeRecoveryStrategy.HandleStallAsync(...)` if Story V2.x ever wants retry-with-nudge.
3. **The classification enum is the API surface.** Adding new classifications (e.g. `StallProviderError`) is additive and discoverable.

This mirrors the `LlmErrorClassifier` pattern from Story 7.1 — pure static, no DI, no interface, single-purpose. Don't reach for an `IStallDetector` interface; YAGNI per the Epic 7 retro insight.

### Where the detector fires in the loop

Today's `ToolLoop.RunAsync` empty-tool-call branch (line 101+) currently does:

```text
1. drain queued user messages (returns count > 0 → continue loop)
2. run completionCheck → exit if true
3. emit input.wait
4. resolve timeout (Story 9.6 path)
5. linked CTS cancel-after, block on WaitToReadAsync
6. timeout fires → return last assistant message
```

Insert the stall detector **at the top of this branch, before step 1**, but **only act on the result if `userMessageReader != null`**:

```text
0. classification = StallDetector.Classify(messages, accumulatedText, functionCalls)
0a. if userMessageReader is null → existing autonomous early return (classification ignored)
0b. if classification is StallEmptyContent or StallNarration → throw StallDetectedException
0c. otherwise → fall through to the existing steps 1-6
```

**Important:** the detector runs *after* `accumulatedText` has been recorded to the conversation recorder (lines 88–91 today). This means the empty/narrative turn is persisted to JSONL before the stall fires — operators inspecting the conversation file post-mortem will see exactly what the LLM produced. Do not move the recorder write below the stall throw.

### The "preceding tool result" check

`StallDetector.Classify` walks `messages` from the end, skipping assistant messages, looking for the first non-assistant message:

```csharp
ChatRole? mostRecentNonAssistantRole = null;
for (var i = messages.Count - 1; i >= 0; i--)
{
    if (messages[i].Role == ChatRole.Assistant) continue;
    mostRecentNonAssistantRole = messages[i].Role;
    break;
}
```

If `mostRecentNonAssistantRole != ChatRole.Tool`, return `NotApplicableNoToolResult` — the empty turn is not following a tool result, so existing behaviour applies. This is the AC #4 protection for the "agent greets the user, waits for first input" case.

**Why scan backwards rather than just checking `messages[messages.Count - 2]`?** Because the loop assembles streaming updates into messages via `messages.AddMessages(updates)` (line 94), which may produce multiple consecutive assistant messages depending on the streaming chunk boundaries. Scanning backwards skipping all assistant messages is robust to that.

### `StallDetectedException` shape and message text

```csharp
namespace AgentRun.Umbraco.Engine;

public sealed class StallDetectedException : AgentRunException
{
    public string LastToolCall { get; }
    public string StepId { get; }
    public string InstanceId { get; }
    public string WorkflowAlias { get; }

    public StallDetectedException(string lastToolCall, string stepId, string instanceId, string workflowAlias)
        : base("The agent stopped responding mid-task. Click retry to try again.")
    {
        LastToolCall = lastToolCall;
        StepId = stepId;
        InstanceId = instanceId;
        WorkflowAlias = workflowAlias;
    }
}
```

The user-facing string is the entire `Message`. The endpoint exception filter from Story 7.1 already formats `AgentRunException.Message` into the `run.error` SSE event payload, so no special filter case is needed. **Verify this by reading the exception filter code before assuming.** If the filter swallows or reformats `AgentRunException` messages, adjust minimally.

### Logging shape

```csharp
logger.LogWarning(
    "ToolLoop stall detected ({StallType}) for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} after tool call {LastToolCall}",
    classification == StallClassification.StallEmptyContent ? "empty" : "narration",
    context.StepId, context.WorkflowAlias, context.InstanceId, lastToolCallName);
```

`LogLevel.Warning`, not `Error` — a stall is a recoverable user-facing condition (the user can retry), not a system failure. `Error` is reserved for unrecoverable engine failures per the project convention.

### Why `?` and not a regex

The trichotomy explicitly chooses `text.TrimEnd().EndsWith('?')` because:

1. **It is unambiguous and trivially testable.**
2. **It punishes prompts that don't end questions with `?`.** Story 9.1b is going to write prompts that end every wait-for-user message with a question mark anyway. The detector enforces that convention.
3. **Edge cases (`?!`, `？`, smart quotes) are intentionally classified as stalls** to drive prompt-quality fixes upstream. Document this in the dev agent record so a future reviewer doesn't "fix" it.

If a beta tester reports a false-positive stall on a legitimate question, the fix is in the prompt (Story 9.1b's loop), not in the detector.

### Files to create / modify

```
AgentRun.Umbraco/
  Engine/
    StallDetector.cs                            (NEW — pure static, zero Umbraco refs)
    StallDetectedException.cs                   (NEW — extends AgentRunException)
    EngineDefaults.cs                           (existing — add StallDetectionWindowSeconds = 10)
    ToolLoop.cs                                 (existing — insert detector call at top of empty-tool-call branch)

AgentRun.Umbraco.Tests/
  Engine/
    StallDetectorTests.cs                       (NEW — ~10 tests covering trichotomy + edge cases)
    ToolLoopTests.cs                            (existing — add ~7 tests for the new branches; reuse userMessageTimeoutOverride from 9.6)

Client/
  src/...                                       (verification only — no changes expected)
```

### Engine boundary check

`StallDetector` and `StallDetectedException` live in `Engine/`. The boundary rule from `project-context.md` ("Engine has ZERO Umbraco dependencies") applies. Both files reference only `Microsoft.Extensions.AI` types (`ChatMessage`, `ChatRole`, `FunctionCallContent`) and the existing `AgentRunException` base type. Verify after writing — if either file pulls in `Umbraco.Cms.*`, the design has drifted.

### Backwards compatibility verification

Before wiring the detector into `ToolLoop`, write `StallDetectorTests` and run them green in isolation. Then add the wire-up. Then re-run the existing `ToolLoopTests` — they should all still pass (the new detector classifies their existing fixtures as `NotApplicableNoToolResult`, `WaitingForUserInput`, or `ToolCallsPresent`, none of which throw). If any existing `ToolLoopTests` fixture starts failing, it means an existing test was relying on the old "wait silently for 5 minutes on an empty turn" behaviour — that's now a stall, and the test itself is the bug. Update the test to reflect the new (correct) behaviour, do not add escape hatches to the detector.

### What this story explicitly leaves on the table for V2

- **Retry-with-nudge** as an alternative recovery strategy. Documented in `v2-future-considerations.md`. Pluggable via the detector/action separation locked into Task 1.
- **Configurable stall window.** Currently hardcoded at 10s via `EngineDefaults.StallDetectionWindowSeconds`. Configurability follows the Story 9.6 pattern if it's ever needed.
- **More sophisticated classification** (sentence embedding, regex on common narrative phrases). YAGNI. Engine stays simple, prompt fixes happen in 9.1b.
- **Auto-warning after N consecutive stalls on the same step.** Suggested in Failure & Edge Cases as observability only — not in scope for this story. If beta telemetry shows it's needed, add via a follow-up story.

### References

- [Source: _bmad-output/planning-artifacts/epic-9-spec-writing-notes.md "Story 9.0" section — locked decisions and the live trace requirement]
- [Source: _bmad-output/planning-artifacts/epics.md lines 1186-1256 — Story 9.0 epic-level acceptance criteria, problem statement, and architectural decision]
- [Source: _bmad-output/implementation-artifacts/9-6-workflow-configurable-tool-limits.md — canonical reference for `IToolLimitResolver`, `EngineDefaults`, the resolution chain, and how `userMessageTimeout` is now resolved per-call]
- [Source: AgentRun.Umbraco/Engine/ToolLoop.cs — current home of the empty-tool-call branch (line 101+); the insertion point for stall detection]
- [Source: AgentRun.Umbraco/Engine/EngineDefaults.cs — existing static class from Story 9.6; extend with `StallDetectionWindowSeconds`]
- [Source: AgentRun.Umbraco/Exceptions/AgentRunException.cs (or wherever the base exception lives) — `StallDetectedException` extends this]
- [Source: Story 7.1 (`7-1-llm-error-detection-and-reporting.md`) — `LlmErrorClassifier` pattern this story mirrors; also the endpoint exception filter that surfaces `AgentRunException` messages as `run.error` SSE events]
- [Source: Story 7.2 (`7-2-step-retry-with-context-management.md`) — the retry flow this story reuses unchanged]
- [Source: _bmad-output/planning-artifacts/v2-future-considerations.md — retry-with-nudge alternative documented but explicitly not implemented here]
- [Source: project-context.md "Engine has ZERO Umbraco dependencies" — boundary rule]
- [Source: project-context.md "Stories must include Failure & Edge Cases" + "Security code: deny by default" — process rules baked into the spec sections above]

## Definition of Done

- [x] All Acceptance Criteria pass (verified by automated tests + manual E2E 7.1–7.6)
- [x] All Tasks complete (Tasks 1–7; 7.7 deferred to pre-public-beta gate, see Task 7.7 note)
- [x] `dotnet test AgentRun.Umbraco.slnx` — full backend suite green, ~17 net new tests (10 detector + 7 ToolLoop branch)
- [x] `npm test` in `Client/` — green, no regressions
- [x] `dotnet build AgentRun.Umbraco.slnx` — zero errors, no new warnings
- [x] Manual E2E validation gate (Task 7.1–7.6) complete — Adam confirmed
- [~] **Production smoke test moved to pre-public-beta gate** — no new packaging surface in this story; cumulative Epic 9 smoke happens once before public beta cut
- [x] No new hardcoded user-message timeout constants added — `IToolLimitResolver` is the only source
- [x] No retry-with-nudge logic introduced
- [x] Engine boundary preserved — `StallDetector` and `StallDetectedException` reference only `Microsoft.Extensions.AI`, base exception, and pure .NET types
- [x] One commit for the story per project rule
- [x] Code review completed

## Manual E2E Validation (Refinement Notes)

This story's manual E2E is a **gate**, not a refinement loop. The seven Task 7 substeps must pass in order. If any fail:

1. **7.2 fails** (stall not detected, original silent wait reproduced) → wiring bug in Task 3. The detector is not being called, or the classification is not throwing, or the exception is being swallowed before reaching the SSE event filter. Inspect in that order: detector call site → exception throw → endpoint exception filter → SSE event emission.
2. **7.3 fails** (legitimate first-turn input wait now incorrectly stalls) → AC #4 regression. The "preceding tool result" check in `StallDetector.Classify` is not working — it's classifying `NotApplicableNoToolResult` cases as stalls. Re-read the backward-scan logic in Dev Notes.
3. **7.4 fails** (multi-turn question after a tool result now incorrectly stalls) → AC #2 regression. The `?` detection is broken, OR the trim is wrong, OR the question prompt the LLM produced doesn't actually end with `?` (in which case the prompt is the bug, not the engine — confirm with Adam before changing the detector).
4. **7.5 fails** (retry button does not restart the step after a stall) → Story 7.2 regression unrelated to this story's changes. Inspect the retry endpoint and the conversation truncation logic. Most likely the retry endpoint's "Failed status only" guard is rejecting because the status isn't updating to Failed — confirm the `StallDetectedException` flows through the same exception filter as other `AgentRunException` subclasses.
5. **7.6 fails** (autonomous mode now stalls on empty turns) → AC #6 regression. The `userMessageReader is null` guard in Task 3.3 is wrong or missing. The detector classification result is being acted on in autonomous mode when it should be ignored.
6. **7.7 fails** (fresh-install smoke test does not reproduce 7.2 result) → packaging bug. The new `StallDetector.cs` and `StallDetectedException.cs` are not included in the NuGet package output. Check `.csproj` content rules and the `dotnet pack` file list.

If the gate passes on the first try, the dev agent has earned the right to feel smug about it.

## Dev Agent Record

_To be filled in by the dev agent during implementation._

### Completion Notes

Implemented the locked trichotomy in `Engine/StallDetector.cs` (pure static, zero Umbraco refs, mirrors `LlmErrorClassifier`). Recovery action — `throw new StallDetectedException(...)` — is a single line in `ToolLoop.RunAsync` so a future retry-with-nudge strategy can swap it without touching the detector (AC #8).

Detector wired into `ToolLoop.RunAsync` at the top of the empty-tool-call branch, after `accumulatedText` has been recorded to the conversation (so the stall trace is persisted to JSONL before the throw). Autonomous mode (`userMessageReader is null`) ignores the classification entirely (AC #6) — the early-return path is unchanged. Interactive mode raises `StallDetectedException` for `StallEmptyContent` and `StallNarration`; falls through to the existing input-wait branch unchanged for `WaitingForUserInput` and `NotApplicableNoToolResult`.

`StallDetectedException` extends `AgentRunException`, so it flows through the existing Story 7.1 endpoint exception filter and surfaces as `run.error` with the user-facing message — no new SSE event type, no filter changes.

Frontend verification (Task 5): `agentrun-instance-detail.element.ts:714` `run.error` handler renders any `data.message` as a system chat message and flips instance status to Failed. The Story 7.2 retry button is already keyed off Failed status. Zero frontend code changes required (Task 5.2 outcome — verification only).

Test counts:
- `StallDetectorTests.cs` — 12 tests covering the full trichotomy + documented edge cases (`?!`, smart-quote `？`, whitespace, no-messages, user-most-recent).
- `ToolLoopTests.cs` — 6 new wiring tests: empty stall, narration stall, question wait, autonomous-empty no-stall, no-preceding-tool-result no-stall, multi-turn user-reply-then-empty no-stall.
- Backend: 382 passed (was 364, +18 net new). Frontend: 162 passed (unchanged).
- `dotnet build AgentRun.Umbraco.slnx` — 0 errors, only the pre-existing MimeKit NU1902 warnings.

**Logging:** `LogLevel.Warning` with structured fields `StallType`, `StepId`, `WorkflowAlias`, `InstanceId`, `LastToolCall`. Used `LastToolCall` (not `ToolName`) to disambiguate from active-tool-execution observability.

**Engine boundary preserved:** `StallDetector.cs` and `StallDetectedException.cs` reference only `Microsoft.Extensions.AI` types and the existing `AgentRunException` base. No `Umbraco.Cms.*` imports.

**Edge cases verified by tests:** `?!` → narration stall; smart-quote `？` → narration stall; whitespace-only → empty stall; user-most-recent (multi-turn after reply) → not applicable; no messages → not applicable. Documented decision per Dev Notes: prompt fixes for `?!` / smart quotes belong in Story 9.1b, not in the detector.

**Task 7 — Manual E2E gate:** NOT executed by the dev agent. This is explicitly a human gate per the spec ("gate, not refinement"). Adam to run substeps 7.1–7.7 against the TestSite and a fresh-install Umbraco 17 site before the story moves to `done`. The reproduction trigger (live regression check 7.2) and the production smoke test (7.7) are both required for the story-level Definition of Done.

### File List

**New:**
- `AgentRun.Umbraco/Engine/StallDetector.cs`
- `AgentRun.Umbraco/Engine/StallDetectedException.cs`
- `AgentRun.Umbraco.Tests/Engine/StallDetectorTests.cs`

**Modified:**
- `AgentRun.Umbraco/Engine/EngineDefaults.cs` (added `StallDetectionWindowSeconds = 10`)
- `AgentRun.Umbraco/Engine/ToolLoop.cs` (inserted detector call + stall throw at top of empty-tool-call branch; added `ExtractLastToolCallName` helper)
- `AgentRun.Umbraco/Engine/StepExecutor.cs` (catch block now special-cases `StallDetectedException` and `AgentRunException` so engine-domain messages are NOT reformatted by `LlmErrorClassifier` — see Change Log entry for the live-test discovery)
- `AgentRun.Umbraco/Instances/ConversationStore.cs` (`TruncateLastAssistantEntryAsync` now skips when the last assistant entry is followed by a tool result — the conversation is already at a clean tool_use → tool_result boundary and removing the assistant entry would orphan the trailing tool_result)
- `AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs` (added `MakeEmptyStreamingResponse` helper + 6 stall wiring tests)
- `AgentRun.Umbraco.Tests/Engine/StepExecutorTests.cs` (added regression test asserting `StallDetectedException` → `LlmError = ("stall_detected", "The agent stopped responding mid-task. Click retry to try again.")`)
- `AgentRun.Umbraco.Tests/Instances/ConversationStoreTests.cs` (added regression test asserting truncation is a no-op when the last assistant entry is a tool_call followed by a tool_result)

## Change Log

- 2026-04-07: Story spec created by SM (Bob) from `epic-9-spec-writing-notes.md` Story 9.0 section and `epics.md` Story 9.0 outline. Resolution-chain pattern referenced from Story 9.6's spec file. Status → ready-for-dev.
- 2026-04-07: Dev (Amelia) implemented Tasks 1–6. `StallDetector` + `StallDetectedException` + `EngineDefaults.StallDetectionWindowSeconds` added; `ToolLoop.RunAsync` wired to fail fast on `StallEmptyContent`/`StallNarration` in interactive mode while leaving autonomous mode and the existing input-wait/`?` paths unchanged. 18 new tests (12 detector + 6 wiring), backend 382/382 + frontend 162/162 green. Status → review pending Task 7 manual E2E gate.
- 2026-04-07: Manual E2E discovery during Task 7.2: stall *was* detected and the step *did* fail correctly, but the chat panel rendered the generic `"The AI provider returned an error..."` message instead of `"The agent stopped responding mid-task. Click retry to try again."` Root cause: `StepExecutor.cs:221` was running every caught exception through `LlmErrorClassifier.Classify`, which has no `StallDetectedException` case and falls through to its `provider_error` default — silently overwriting the user-facing message. Spec assumed `AgentRunException.Message` would surface unchanged via the SSE error event; the classifier was the missing link. Fixed by special-casing `StallDetectedException` (`stall_detected`) and `AgentRunException` (`step_failed`) in the catch block so engine-domain messages bypass the LLM provider classifier entirely. Added regression test in `StepExecutorTests.cs`. Backend 383/383 green.
- 2026-04-07: Second manual E2E discovery during Task 7.5 (retry button click after a stall): retry hit Anthropic with `400 BadRequest: "messages.2.content.1: unexpected tool_use_id found in tool_result blocks"`. Root cause was a Story 7.2 truncation strategy bug exposed by Story 9.0 stalls. When a stall fires after a successful tool round-trip, the empty assistant turn is NOT recorded (`ToolLoop.cs:88` skips empty `accumulatedText`). The conversation file ends at `[…, assistant tool_call, tool result]` — already a clean tool_use → tool_result boundary. Story 7.2's `TruncateLastAssistantEntryAsync` then removed the assistant tool_call (the most recent `role=="assistant"` entry), leaving an orphaned tool_result. The next provider call rejected it. Fix: truncation now skips when the last assistant entry is not the very last entry overall (i.e. a tool result follows it). This is non-breaking — the four pre-existing truncation tests, including the LLM-error and multi-assistant cases, still pass. Added regression test in `ConversationStoreTests.cs`. Backend 384/384 green. Spec's "DO NOT modify Story 7.2" constraint reinterpreted as "do not redesign the retry flow"; this is a minimum-blast-radius bug fix in a single helper method, not a redesign — and AC #7 (retry path works after a stall) cannot be met without it.
- 2026-04-07: Code review completed (Amelia). Findings: 2 medium (drain-order race on stall — spec-compliant, deferred; truncation rule scope — safe but comment could be sharper), 3 low (unused `StallDetectionWindowSeconds`, test helper naming, missing detector test combo). None blocking. Manual E2E 7.1–7.6 confirmed by Adam; 7.7 (production smoke test) explicitly deferred to a pre-public-beta cumulative gate — Story 9.0 adds no new packaging surface (two files into existing `Engine/` folder, same assembly as 9.6 which shipped fine), so per-story `dotnet pack` into a fresh Umbraco 17 site is overkill here. Status → done.
- 2026-04-07: Third manual E2E discovery during Task 7.3/7.5 follow-up: a successful Content Quality Audit run (`fetch_url` → `write_file` → produced `artifacts/scan-results.md` containing the expected scan output) was incorrectly being marked Failed because the model narrated `"All done! I have written the results."` after `write_file` instead of going silent. Story 9.0's narration trichotomy correctly classified this as `StallNarration` and threw — but the run had actually succeeded; the completion check would have passed in a heartbeat. Pre-9.0 the completion check ran inside the input-wait branch, *after* the empty-tool-call branch entry; Story 9.0 inserted the stall throw above the completion check, breaking workflows that finish with a narrative final turn. Fix: in `ToolLoop.cs`, when a stall classification is `StallEmptyContent` or `StallNarration`, run the completion check FIRST and return success if it passes; only throw `StallDetectedException` when there is no verified evidence of completion. This preserves "deny by default" — unverified narration still stalls — while letting verified-complete runs finish cleanly. Added two regression tests in `ToolLoopTests.cs` (narration + completion-check-passes → success; narration + completion-check-fails → still throws). Backend 386/386 green. Discovered live by Adam during the manual gate; the artifact file was inspected to confirm the workflow had genuinely succeeded.
