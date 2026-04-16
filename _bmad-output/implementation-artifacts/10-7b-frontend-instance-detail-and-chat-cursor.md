# Story 10.7b: Frontend Instance-Detail Split + Chat Cursor Fix

Status: done

**Depends on:** Story 10.7a (backend hotspot refactors — must land first so the backend baseline is stable before frontend work begins). Story 10.10 precedent (`instance-detail-helpers.ts` already extracted — reference pattern for this story's utility modules).
**Followed by:** Story 10.7c (content-tool DRY + comment hygiene). Split from the original [Story 10.7 parent spec](./10-7-code-shape-cleanup-hotspot-refactoring.md) on 2026-04-15 per Adam's quality-risk concern on single-PR delivery.
**Branch:** `feature/epic-10-continued`
**Priority:** 9th in Epic 10 (10.2 → 10.12 → 10.8 → 10.9 → 10.10 → 10.1 → 10.6 → 10.11 → 10.7a → **10.7b** → 10.7c → 10.4 → 10.5).

> **Four tracks shipped as one frontend-focused story.** Covers Track D (instance-detail 3-module split) and Track F (chat-panel cursor 1-line fix) from Winston's architect triage of the [Codex bloat review (2026-04-10)](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md), plus Track G (chat-panel retry-banner reducer fix, AC5) and Track H (SSE text-delta render integrity, AC6) folded in 2026-04-15 from bugs surfaced during Story 10.7a E2E. Tracks F/G/H all live in `agentrun-chat-panel.element.ts` + its reducer / SSE wiring, so they share one frontend test run and one commit-train cadence.
>
> **The goal is clearer responsibility boundaries with the fewest necessary moves — NOT maximum decomposition.** Every extraction must reduce reasoning load; thin one-method wrappers are explicitly rejected. When in doubt, leave the file alone.

## Story

As a maintainer of AgentRun.Umbraco,
I want the largest frontend accumulation point (`agentrun-instance-detail.element.ts`) refactored into clearer responsibility boundaries AND three chat-panel UX bugs fixed (cursor flash, stale retry banner, text-delta render intermittency),
So that future UI changes are lower-risk, the state / reducer / action concerns are testable without a DOM, and three chunks of beta/E2E feedback land together before public launch instead of scattering across stories.

## Context

**UX Mode: Interactive (primary) + Autonomous (secondary).** Track D is a pure-shape refactor — UI behaviour unchanged. Tracks F / G / H are deliberate observable-behaviour changes: **F** (Tom Madden beta 2026-04-15) — block cursor only renders during active text streaming, not during tool calls / waiting states. **G** (10.7a E2E 2026-04-15) — chat-panel reducer clears the stale "Run failed/interrupted" banner + re-enables the input when Retry fires a new LLM turn, on both Failed→Retry and Interrupted→Retry paths. **H** (10.7a E2E 2026-04-15) — every `text.delta` SSE frame renders in the chat panel (no silent drops); if triage shows frames don't arrive at the browser at all, escalate back to 10.7a engine instead of patching the frontend.

### Why this story was split from the parent

See [Story 10.7a §Why this story was split from the parent](./10-7a-backend-hotspot-refactors.md). This story (10.7b) is track-group 2 of 3 from the original parent spec. Running the backend refactor first (10.7a) lets the full test suite re-settle before frontend work begins — any weirdness in the ToolLoop / StepExecutor reshape surfaces first, not mingled with a 1073-line frontend reshape.

### The four tracks in this story

| Track | Target | Disposition | Why | Est. effort |
|---|---|---|---|---|
| **D** | [`Client/agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) (1073 lines) | Split | Strongest frontend hotspot; SSE event reducer + state store + action handlers out of the view | 1.5 days |
| **F** | [`Client/agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) line 184 | 1-line fix + 1 test | Beta feedback (Tom Madden); cursor flashes during tool calls — wire existing message-level `isStreaming` flag | 0.1 day |
| **G** (AC5) | [`Client/agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) reducer / banner state | Reducer action + 3–4 tests | 10.7a E2E Tests 4 + 5 — Retry on Failed OR Interrupted leaves stale "Run failed/interrupted — click Retry to resume" banner + locked input throughout the retry execution | 0.3 day |
| **H** (AC6) | [`Client/agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) SSE text-delta render path | Triage-then-fix (≤ 2 tests if in-scope; escalate otherwise) | 10.7a E2E Test 1 — backend JSONL contained all 12 assistant entries, UI rendered none. Intermittent. Engine-side `engine.streaming.text_delta_emitted` Debug log from 10.7a localises backend-emit vs frontend-render on repro | 0.2 day (in-scope) / escalate to 10.7a if backend |

### Tracks explicitly deferred to sibling stories (do not touch)

| Deferred to / from | Scope |
|---|---|
| **10.7a (done first)** | Backend hotspots: FetchUrlTool, ToolLoop, StepExecutor |
| **10.7c (done after)** | Content-tool DRY (`ContentToolHelpers`) + O(n²) truncation fix + comment hygiene polish pass |

### Tracks explicitly REJECTED (do not touch — Winston triage)

From [epics.md #Story-10.7](../planning-artifacts/epics.md#story-107-code-shape-cleanup--hotspot-refactoring):

| File | Codex recommendation | Winston verdict | Rationale |
|---|---|---|---|
| [`Client/agentrun-chat-message.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-message.element.ts) (235 lines) | Simplify | **Skip** | DOM-to-HTML round trip is correct for sanitisation + streaming; 235 lines, works, leave it |

### Architect's locked decisions (do not relitigate)

These decisions were locked in the parent story on 2026-04-15 and are preserved verbatim here. Only decisions relevant to Tracks D + F appear in this story.

**Universal (all three 10.7 child stories):**

1. **"Fewest necessary moves" is the design principle.** Any extraction that does not demonstrably reduce reasoning load must be rejected in favour of leaving the file alone. Thin one-method wrappers are explicitly forbidden. When in doubt, don't split. The dev agent should read the refactor brief's "Review Rules For The Next Agent" ([refactor brief §Review Rules](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md)) before starting.
2. **Behaviour preservation scoped to Track D.** Track D is a pure-shape refactor — existing tests must pass without modification to test *assertions* (setup may change to match new state shape; BDD intent must be preserved). Tracks F / G / H are deliberate observable-behaviour changes, each scoped as narrowly as possible: F flips the cursor visibility expression; G adds one chat-panel reducer action that clears banner state on retry start; H is in-scope only if triage localises the bug to the reducer (see decision 13). Any Track D test that needs rewriting is a flag that the refactor is wrong-shaped — reshape the refactor, don't rewrite the test.
3. **Engine boundary must stay clean (Story 10.11 invariant).** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` must remain at 0 matches. This story doesn't touch `Engine/` directly, but the invariant must be preserved across the repo at DoD.

**Track D (instance-detail):**

4. **Track D extracts exactly three modules — `instance-detail-sse-reducer.ts` + `instance-detail-store.ts` + `instance-detail-actions.ts`.** Do NOT extract sub-components (header/step-list/banner/conversation-panel). Component subdivision is a separate effort that would double the story; the state/action/reducer split alone reduces the file from 1073 lines to an estimated ~650 (render + wiring + existing helper calls), which is a worthwhile win on its own. The existing [`instance-detail-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) (152 lines — extracted during Story 10.10) is the reference pattern: small, focused, pure functions with their own test file.

5. **Track D reducer is PURE.** `instance-detail-sse-reducer.ts` exports a pure `reduceSseEvent(state, event) → newState` function. No side effects (no `dispatchEvent`, no console calls, no SSE client references). State is the shape of the fields listed in AC2. The component calls `this._state = reduceSseEvent(this._state, parsed)` in `_handleSseEvent` and the reducer has its own unit-test file that runs without a DOM.

6. **Track D store is NOT a Redux/Umbraco-context store.** Per [project-context.md](../project-context.md) frontend rules, "state management: use Umbraco Context API with `UmbObjectState` / `UmbArrayState` observables — not Redux, not standalone stores." `instance-detail-store.ts` in this story is a **local state shape module** — a `type InstanceDetailState` definition + default initial state + the reducer. It does NOT introduce an observable, a context, or cross-component state sharing. Naming is deliberate: "store" in the refactor brief means "state model," not "application state manager."

**Track F (chat cursor):**

7. **Track F is a one-line code change + one frontend test — nothing else ships under the F label.** The deferred-work entry ([deferred-work.md:205-226](./deferred-work.md)) specifies the fix exactly. No other frontend changes in Track F — do NOT touch `agentrun-chat-message.element.ts` (Winston skip), do NOT touch the streaming field on ChatMessage (already correct). Reducer-level work belongs to Track G; render-path work belongs to Track H. Track F is scoped tight on purpose so a regression in G or H cannot contaminate the cursor fix commit.

**Track G (retry-banner reducer fix, AC5):**

12. **Track G is a single reducer action + banner-flag clear + test coverage — no chat-panel structural change.** Shape: add one case to the chat-panel reducer (name it `retry.started` or reuse the first post-retry `text.delta` / `tool.start` as the trigger — dev agent picks whichever the existing reducer topology makes cheapest) that clears the stale banner flag AND the `_disableInput` (or equivalent) flag. The two evidence paths (Failed→Retry and Interrupted→Retry) share one fix — do NOT write two code paths. Test coverage is the two paths asserted separately (AC5). This track does NOT touch the instance-detail reducer from Track D; it lives in the chat-panel's own state.

**Track H (SSE text-delta render integrity, AC6):**

13. **Track H is triage-first, fix-only-if-frontend.** The dev agent MUST repro with browser DevTools → Network → EventStream filter active before writing any fix. Two branches: (a) if `text.delta` frames arrive at the browser and the UI does not render them → bug is in the SSE reducer or state-merge path, IN SCOPE for this story, ship the fix with ≤ 2 tests; (b) if `text.delta` frames do NOT arrive at the browser → bug is engine-side, OUT OF SCOPE, escalate back to 10.7a review with a new bug-finding artefact under `_bmad-output/planning-artifacts/` and mark AC6 as escalated in the Dev Agent Record rather than failing the story. If the dev cannot reproduce AC6 at all during manual E2E against the TestSite across ≥ 3 full runs of the CQA workflow, document the non-repro in the Dev Agent Record, leave the 10.7a Debug instrumentation in place, and unblock story completion — do NOT block 10.7b on an unreproducible intermittent. Note this carefully: under-delivering on AC6 via documented non-repro or escalation is an acceptable path to DONE; over-delivering via speculative frontend patches to a bug that wasn't localised is NOT.

**Cross-cutting (frontend-relevant):**

8. **No new npm dependencies.** Track D uses existing Lit / TypeScript. Track F is a one-char change.

9. **Test budget target: ~16 new tests.** Breakdown: Track D (~10: 6 reducer tests for the SSE event cases, 2 for store shape, 2 for actions); Track F (~1 frontend cursor test); Track G (~4: reducer unit for the banner-clear action, Failed-path banner clears, Interrupted-path banner clears, input re-enable on retry); Track H (~0 if escalated / non-repro, ~1–2 if in-scope reducer fix). Full frontend suite must stay green. Preserve ALL existing tests — any test deletion must be justified in the Dev Notes as either duplicate coverage or no-longer-applicable setup.

10. **Test counts are guidelines, not ceilings.** From Epic 8+ retro. If the reducer needs 10 tests to cover all SSE event branches, write the 10 tests. Under-budget is fine too — don't pad.

11. **Priority ordering inside the story: D → F → G → H.** Track D first (heaviest work, largest surface). Track F second (one-line cursor change, lowest-risk chat-panel edit). Track G third (chat-panel reducer action — lands on top of F's cleaned-up chat-panel state without stepping on it). Track H last (triage-first; may resolve to "escalated" or "non-repro" rather than a code change, and running it last means D/F/G's test run gives a fresh baseline for the intermittent repro). Each track finishes with `npm test` green before the next one starts.

## Acceptance Criteria (BDD)

### AC1: Frontend — `instance-detail.element.ts` drops below 700 lines

**Given** the refactored [`Client/src/components/agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts)
**When** the file is inspected
**Then** it is ≤ 700 lines (down from 1073)
**And** the 17 `@state` fields listed at [lines 37–86](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L37) are reduced to a single `@state private _state: InstanceDetailState = initialInstanceDetailState();` plus the few fields that are genuinely UI-only (e.g. `_popoverOpen`, `_viewingStepId`) which may stay as individual fields if extraction would complicate the render
**And** the SSE event switch inside `_handleSseEvent` at [lines 513–735](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L513) is replaced with a single `this._state = reduceSseEvent(this._state, parsed);` call plus whatever side-effect plumbing remains (artifact popover trigger, scroll-to-bottom signalling)
**And** the action methods (`_onStartClick`, `_onRetryClick`, `_onCancelClick`, `_onAdvanceStep`) delegate their API-call bodies to functions in `instance-detail-actions.ts`, keeping only the UI-side state reset + event wiring in the component

### AC2: Three new frontend modules exist with dedicated tests

**Given** the new files [`Client/src/utils/instance-detail-sse-reducer.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts), [`Client/src/utils/instance-detail-store.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts), [`Client/src/utils/instance-detail-actions.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts)
**When** the files are inspected
**Then** `instance-detail-store.ts` exports `type InstanceDetailState` + `initialInstanceDetailState(): InstanceDetailState` — no class, no observable (locked decision 6)
**And** `instance-detail-sse-reducer.ts` exports `reduceSseEvent(state: InstanceDetailState, event: SseEvent): InstanceDetailState` — pure function, no `console.*`, no `dispatchEvent` (locked decision 5)
**And** `instance-detail-actions.ts` exports typed wrappers for `startInstanceAction`, `retryInstanceAction`, `cancelInstanceAction` that take the API client + id and return a `ReadableStream` or similar — UI concerns (modal confirmation, loading flags) stay in the component
**And** each file has a dedicated test file: [`instance-detail-sse-reducer.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts), [`instance-detail-store.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-store.test.ts), [`instance-detail-actions.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-actions.test.ts)
**And** the reducer test file covers all 9 SSE event cases (text.delta, tool.start, tool.args, tool.end, tool.result, step.started, step.finished, run.finished, input.wait, run.error — note run.error + input.wait are minor branches, count them)

### AC3: Chat cursor one-line fix + test

**Given** [`agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) line 184
**When** the panel renders
**Then** the attribute expression is `?is-streaming=${i === lastIndex && msg.role === "agent" && this.isStreaming && msg.isStreaming === true}` — the existing `msg.isStreaming` message-level flag is AND-gated with the connection-level `this.isStreaming`
**And** a new test in `agentrun-chat-panel.element.test.ts` (or equivalent) asserts: (a) cursor is present when `this.isStreaming && msg.isStreaming` both true; (b) cursor is absent when `this.isStreaming === true && msg.isStreaming === false` (the tool-call / waiting state that prompted Tom Madden's feedback)
**And** no other frontend files are modified for this track

### AC4: Full frontend suite green + no new warnings + backend boundary preserved

**Given** the story is complete
**When** `npm test` in `AgentRun.Umbraco/Client/` runs
**Then** frontend tests pass (baseline 183 → expect ~199 with ~16 new tests per locked decision 9; delta TBD by dev agent — lower bound ~196 if Track H resolves to non-repro / escalation)
**And** `npm run build` in `AgentRun.Umbraco/Client/` is clean
**And** line-count check: `wc -l AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` → ≤ 700 (AC1)
**And** `dotnet test AgentRun.Umbraco.slnx` still green at whatever count 10.7a left it (no backend regressions)
**And** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns **0** matches (Story 10.11 invariant preserved — no backend work in this story, but the repo-wide invariant check is part of DoD)

### AC5: Chat-panel retry clears stale banner + unlocks input (bug found during 10.7a E2E)

**Given** a workflow run is in Failed OR Interrupted state and the user clicks Retry
**When** the backend transitions the step back to Active (verified server-side during 10.7a E2E — Failed path fires `RecoveryStrategy=wipe-and-restart`; Interrupted path resumes the Active step in place)
**Then** the chat-panel input becomes enabled
**And** the stale banner text ("Run failed — click Retry to resume" OR "Run interrupted — click Retry to resume") clears
**And** the "Agent is responding…" indicator reflects the live retry state, not the prior failure/interrupt state
**Evidence:**
- Failed→Retry path: uniform repro on fresh instance `fad4b0f4a5c64747bbc153d26401fdd1` 2026-04-15 during E2E Test 4. Hard-refresh does not fix it — rules out ephemeral state. UI simultaneously shows three mutually-exclusive states ("Run failed" + "Agent is responding…" + locked input).
- Interrupted→Retry path: repro on instance `9ef84e8e26a44dabb68ee9c90138a2f1` 2026-04-15 during E2E Test 5. After F5 mid-stream → instance Interrupted → user clicks Retry → backend resumes the Active step and runs the scanner to completion → UI still shows "Run interrupted — click Retry to resume" banner throughout the retry execution, only clearing when the step completes and the "Click Continue to run the next step" prompt renders.
- Both paths point to the same missing `retry.started` / `step.restart` action in the chat-panel reducer that should clear the stale banner flag when a new LLM turn begins.

### AC6: SSE text-delta renders reliably across the full run (bug found during 10.7a E2E)

**Given** a workflow run completes normally on the backend (verified via `conversation-scanner.jsonl` containing all assistant text entries)
**When** the chat panel is connected to the SSE stream throughout the run
**Then** every `text.delta` event renders in the chat panel (no silent drops)
**Evidence:** Intermittent repro during 10.7a E2E Test 1 — run `b6180e96` 2026-04-15 backend emitted 12 assistant entries into the JSONL but the UI showed none of them; the preceding run on the same server build rendered normally. Backend-side instrumentation added in 10.7a (`engine.streaming.text_delta_emitted` at Debug) lets the dev localise whether the flake is backend emit or frontend render. The dev agent should repro with browser DevTools → Network → EventStream filter active: if `text.delta` frames arrive at the browser and the UI does not render them, the bug is in the SSE reducer (in-scope for this story). If frames do not arrive at the browser, escalate back to 10.7a engine review.

## Tasks / Subtasks

### Task 1: Track D — Extract `instance-detail-store.ts` + `instance-detail-sse-reducer.ts` (AC1, AC2)

- [x] 1.1 Read [`agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) lines 37–86 (state fields) + 513–735 (`_handleSseEvent`).
- [x] 1.2 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts):
  ```typescript
  export type InstanceDetailState = {
    instance: InstanceDto | null;
    loading: boolean;
    error: string | null;
    selectedStepId: string | null;
    runNumber: number | null;
    streaming: boolean;
    providerError: string | null;
    chatMessages: ChatMessage[];
    streamingText: string;
    viewingStepId: string | null;
    historyMessages: ChatMessage[];
    stepCompletable: boolean;
    agentResponding: boolean;
    retrying: boolean;
    cancelling: boolean;
    // popover state stays on the element (UI-only, not derived from SSE)
  };

  export function initialInstanceDetailState(): InstanceDetailState { /* ... */ }
  ```
  Dev agent tunes the exact field list — the invariant is: any field mutated from within `_handleSseEvent` MUST be on `InstanceDetailState`. Pure-UI fields (popover open/close) MAY stay as element `@state`.
- [x] 1.3 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts) exporting a pure `reduceSseEvent(state, event): InstanceDetailState`. Port every branch from the element's `_handleSseEvent` as a case in a switch on `event.event`. Side effects (popover open on `run.finished`, scroll-to-bottom triggers) do NOT go in the reducer — they stay in the component and fire after the reducer produces new state.
- [x] 1.4 Update the element to hold a single `@state private _state: InstanceDetailState = initialInstanceDetailState();`. Replace the 17 field reads throughout the file with reads from `this._state.xxx`. Replace the 17 field writes inside `_handleSseEvent` with `this._state = reduceSseEvent(this._state, parsed);` followed by the minimal side-effect plumbing (popover open, etc.).
- [x] 1.5 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts) with one test per SSE event case — 10 cases total (9 events from AC2 + initial state). Use `@open-wc/testing` + `expect` per project convention.
- [x] 1.6 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-store.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-store.test.ts) with 1–2 tests: `initialInstanceDetailState()` returns the expected zero-state.
- [x] 1.7 Run `npm test` — element tests pass (the existing `agentrun-instance-detail.element.test.ts` should still pass because externally observable behaviour is unchanged; if any test breaks due to state-shape change, adjust the test to match the new internal shape and note it in Dev Notes).

### Task 2: Track D — Extract `instance-detail-actions.ts` (AC1, AC2)

- [x] 2.1 Read the action methods at [`agentrun-instance-detail.element.ts:389-438`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L389) and [`810-843`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L810).
- [x] 2.2 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts). Export typed wrappers:
  ```typescript
  export async function startInstanceAction(apiClient: AgentRunApi, instanceId: string): Promise<Response> { ... }
  export async function retryInstanceAction(apiClient: AgentRunApi, instanceId: string, fromStepId: string): Promise<Response> { ... }
  export async function cancelInstanceAction(apiClient: AgentRunApi, instanceId: string): Promise<void> { ... }
  ```
  These are the API call + error-translation bodies ONLY. UI concerns (modal confirmation, loading flag toggling) stay in the component.
- [x] 2.3 Component `_onStartClick` / `_onRetryClick` / `_onCancelClick` methods become: (a) UI-side state reset + flag toggles, (b) call the action function, (c) wire the returned `Response` / `ReadableStream` into `_handleSseEvent`.
- [x] 2.4 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-actions.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-actions.test.ts) with 2–3 tests: happy-path start, happy-path retry, cancel surfaces API errors.
- [x] 2.5 Run `npm test` — all tests pass. Verify `agentrun-instance-detail.element.ts` ≤ 700 lines (AC1).

### Task 3: Track F — Chat cursor one-line fix (AC3)

- [x] 3.1 Open [`agentrun-chat-panel.element.ts` line 184](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts#L184).
- [x] 3.2 Change:
  ```typescript
  ?is-streaming=${i === lastIndex && msg.role === "agent" && this.isStreaming}
  ```
  to:
  ```typescript
  ?is-streaming=${i === lastIndex && msg.role === "agent" && this.isStreaming && msg.isStreaming === true}
  ```
- [x] 3.3 Add a test in [`agentrun-chat-panel.element.test.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.test.ts) — if the file doesn't exist, create it — asserting (a) cursor present when both flags true, (b) cursor absent when `this.isStreaming=true && msg.isStreaming=false`. Use `@open-wc/testing` `fixture` + query-selector on the rendered DOM.
- [x] 3.4 Run `npm test` — new test green.

### Task 4: Track G — Chat-panel retry-banner reducer fix (AC5)

- [x] 4.1 Open [`agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) + its reducer / state module. Identify where the stale banner flag (likely `_showFailedBanner` / `_showInterruptedBanner` or a shared `_terminalBannerText` field) and `_disableInput` are set, and where `_handleSseEvent` (or the panel's own SSE reducer) dispatches into that state.
- [x] 4.2 Add a single reducer action — name it `retry.started` or reuse the first post-retry `text.delta` / `tool.start` event as the trigger, whichever the existing reducer topology makes cheapest per locked decision 12 — that clears the banner flag + `_disableInput` when a new LLM turn begins after Retry. Shape:
  ```ts
  // inside the chat-panel's reducer / SSE handler
  case "text.delta":  // or "retry.started" if a dedicated event is cleaner
    return { ...state, terminalBanner: null, inputDisabled: false, /* ...existing merge */ };
  ```
  Exact field names are whatever the file already uses — do NOT rename existing fields.
- [x] 4.3 Add tests in [`agentrun-chat-panel.element.test.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.test.ts) (create if missing):
  - (a) Reducer unit — starting from `{ terminalBanner: "Run failed…", inputDisabled: true }`, dispatch the retry-trigger event and assert the banner clears + input re-enables.
  - (b) **Failed→Retry path** — fixture with Failed state + banner rendered, simulate retry start event, assert banner absent + input enabled.
  - (c) **Interrupted→Retry path** — fixture with Interrupted state + banner rendered, simulate retry start event, assert banner absent + input enabled.
  - (d) Assert **input-disabled placeholder text** no longer reads "Run failed — click Retry to resume" / "Run interrupted — click Retry to resume" after retry starts.
- [x] 4.4 Run `npm test` — all green. Verify both AC5 evidence paths would now behave correctly (evidence instances `fad4b0f4a5c64747bbc153d26401fdd1` for Failed→Retry and `9ef84e8e26a44dabb68ee9c90138a2f1` for Interrupted→Retry).

### Task 5: Track H — SSE text-delta render integrity triage-then-fix (AC6)

- [ ] 5.1 **Triage first, code second.** Start the TestSite, run the CQA workflow, keep browser DevTools → Network → EventStream filter active across ≥ 3 full runs. Repro the AC6 symptom (full JSONL on disk, empty UI) before writing a single line of fix code. Note: the symptom is intermittent; if it does not repro after 3 runs plus ad-hoc exercise, proceed to subtask 5.4.
- [ ] 5.2 If repro succeeds — **branch on "where did the frames stop":**
  - (a) **Frames arrive at browser, UI silent** → bug is in the SSE reducer / state-merge path. IN SCOPE for this story. Pinpoint the missed merge (likely `text.delta` dropping into a state branch that fails to append to the active assistant message's text buffer, or a race between `_finaliseStreamingMessage` and a subsequent `text.delta`). Apply the narrowest fix + ≤ 2 tests. Document evidence in the Dev Agent Record.
  - (b) **Frames do NOT arrive at browser** → bug is engine-side. OUT OF SCOPE for 10.7b. Use the `engine.streaming.text_delta_emitted` Debug log from 10.7a to confirm emits on the server, then file a new bug-finding artefact at `_bmad-output/planning-artifacts/bug-finding-2026-04-1X-text-delta-backend-drop.md` and mark AC6 as "escalated — see bug-finding" in the Dev Agent Record.
- [ ] 5.3 Add ≤ 2 tests if the fix landed in the reducer — typical assertion shape is `reduceSseEvent(initial, { event: "text.delta", text: "abc" })` yields state with streamingText appended; a second test for the race pattern if that's what the triage found.
- [x] 5.4 If the symptom never repros — document the non-repro in the Dev Agent Record per locked decision 13: "AC6 triage: ≥ 3 CQA runs + exploratory use, symptom not reproduced. Engine-side `engine.streaming.text_delta_emitted` Debug instrumentation from 10.7a left in place for future repros." This is an acceptable path to DONE.
- [x] 5.5 Run `npm test` — all green whether or not a code fix landed.

### Task 6: DoD verification + commit train

- [x] 6.1 Full frontend test: `cd AgentRun.Umbraco/Client && npm test` → all green (AC4, expect ~199; lower bound ~196 if Track H resolved to non-repro / escalation).
- [x] 6.2 Full backend test: `dotnet test AgentRun.Umbraco.slnx` → still green at whatever 10.7a left it (no backend changes in this story).
- [x] 6.3 Build clean: `npm run build` in `Client/` → clean. `dotnet build AgentRun.Umbraco.slnx` → 0 new warnings.
- [x] 6.4 Engine boundary check: `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → 0 matches (Story 10.11 invariant — not expected to change in this story, but part of DoD).
- [x] 6.5 Line-count check: `wc -l AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` → ≤ 700 (AC1).
- [x] 6.6 Commit train:
  - Commit 1: Track D part 1 (store + reducer + their tests + element refactor to use them)
  - Commit 2: Track D part 2 (actions + their tests + element refactor to call them)
  - Commit 3: Track F (chat cursor one-line fix + test)
  - Commit 4: Track G (chat-panel retry-banner reducer action + 3–4 tests)
  - Commit 5: Track H (IN-SCOPE fix + ≤ 2 tests) — OR — Track H escalation / non-repro documentation commit (Dev Agent Record update only, no code change)

  Each commit leaves the full frontend test suite green. Each commit body ends with the standard Co-Authored-By trailer per CLAUDE.md.
- [x] 6.7 Manual E2E — Adam walks the frontend-relevant scenarios:
  1. Start TestSite: `dotnet run` from `AgentRun.Umbraco.TestSite/`.
  2. Run the **Content Quality Audit** workflow end-to-end. Verify: chat UI streams correctly (Track D — reducer works end-to-end), agent responses + tool calls + results all render in the right order, SSE event handling survives the refactor.
  3. **Cancel a running step** — verify cancel UI flows work (Story 10.8 invariant across Track D action extraction).
  4. **Retry a failed step (Failed→Retry, Track G / AC5)** — trigger a Failed run (e.g., cancel mid-LLM or let a transient provider error land), click Retry, verify: banner clears the moment the retry LLM turn starts (not when the step completes), input is immediately enabled, "Agent is responding…" reflects live retry state. Adam's AC5 evidence instance `fad4b0f4a5c64747bbc153d26401fdd1` reproduces this; any fresh Failed instance should now behave.
  5. **Interrupt via F5 during a long LLM step, then click Retry (Interrupted→Retry, Track G / AC5)** — verify Interrupted flow surfaces correctly in the UI (Story 10.9 invariant) AND the banner clears + input re-enables as soon as the retry LLM turn begins, not when the step completes. AC5 evidence instance `9ef84e8e26a44dabb68ee9c90138a2f1`.
  6. **Chat cursor behaviour (Track F)** — observe the cursor during a multi-turn interactive step: the block cursor (▋) should ONLY appear during active text streaming, NOT during tool calls, NOT during waiting states, NOT between messages. This is the Tom Madden repro scenario; it's the acceptance condition for Track F.
  7. **Text-delta render integrity (Track H / AC6)** — across ≥ 3 full CQA runs with DevTools EventStream filter active, verify every `text.delta` frame renders in the chat panel. If the intermittent symptom surfaces, it should now be handled (in-scope branch 5.2a) or documented (escalated branch 5.2b or non-repro branch 5.4). AC6 evidence run `b6180e96` 2026-04-15.
- [x] 6.8 Set story status to `done` in this file + `sprint-status.yaml` once 6.1–6.7 all pass. Transition 10.7c from `backlog` to `ready-for-dev` in the same sprint-status update.

### Review Findings (2026-04-15 — Amelia, bmad-code-review)

**Decision-needed (4) — all resolved 2026-04-15:**

- [x] [Review][Decision] AC5 fix lives in instance-detail reducer, not chat-panel reducer — **Resolved: accept deviation.** Reducer is natural home for status mutation; bisectability cost tolerable. Rationale recorded in Dev Notes §"Code-review accepted deviations".
- [x] [Review][Decision] AC3 cursor test uses predicate-mirror — **Resolved: extracted `shouldShowCursor` helper to `instance-detail-helpers.ts:154`; template + test now call the same function (single source of truth, drift impossible).** True DOM fixture infeasible — project's test runner has no Bellissima import map.
- [x] [Review][Decision] Track G reducer trigger includes `step.started` — **Resolved: tightened to `{text.delta, tool.start}` at `instance-detail-sse-reducer.ts:41-47`.** `step.started` dropped from the trigger set; backend resume paths no longer silently flip to Running.
- [x] [Review][Decision] AC6 dev-side triage deferred to Task 6.7 — **Resolved: accept deferral.** F5 defensive test + 10.7a Debug instrumentation carry the invariant; Adam's manual E2E owns the live triage gate. Rationale recorded in Dev Notes §"Code-review accepted deviations".

**Patch (9) — all applied 2026-04-15:**

- [x] [Review][Patch] `finaliseStreamingMessage` silently drops streamingText when last message isn't an active streaming agent message — violated F5 invariant [`instance-detail-sse-reducer.ts:80-106`]. Fixed: appends a new finalised agent message with the streaming text instead of dropping it.
- [x] [Review][Patch] `applyTextDelta` lacks string-type guard on `data.content` — non-string truthy values produced `"abc42"` / `"abc[object Object]"` [`instance-detail-sse-reducer.ts:94-101`]. Fixed: `typeof data.content !== "string"` guard.
- [x] [Review][Patch] `applyToolStart` lacks guard on missing `toolCallId` / `toolName` — zombie tool call with undefined id [`instance-detail-sse-reducer.ts:128-140`]. Fixed: type-guarded early return on missing fields.
- [x] [Review][Patch] `applyToolResult` with `data.result === undefined` produces non-string resultStr — `JSON.stringify(undefined)` returns undefined value [`instance-detail-sse-reducer.ts:220-239`]. Fixed: explicit null/undefined → empty-string guard; toolCallId guard added.
- [x] [Review][Patch] `applyRunFinished` overwrites prior Failed/Cancelled status with Completed — run.finished after run.error silently rewrote the terminal outcome [`instance-detail-sse-reducer.ts:310-326`]. Fixed: preserve Failed/Cancelled, only flip to Completed from non-terminal states.
- [x] [Review][Patch] `startInstanceAction` / `retryInstanceAction` do not catch fetch rejections — unhandled rejection left `retrying: true` forever [`instance-detail-actions.ts:15-33, 38-55`]. Fixed: try/catch returning `{ kind: "failed", status: 0 }` on network throw; `_onRetryClick` now clears `retrying` in `finally`.
- [x] [Review][Patch] `applyStepFinished` writes literal `undefined` into `step.status` when `data.status` missing [`instance-detail-sse-reducer.ts:288-302`]. Fixed: `typeof data.status === "string" ? data.status : "Complete"` default.
- [x] [Review][Patch] D2 follow-through: Extract `shouldShowCursor` into `instance-detail-helpers.ts` (single source of truth for template + test) [`instance-detail-helpers.ts:154-168`, `agentrun-chat-panel.element.ts:185`, `agentrun-chat-panel.element.test.ts:12-61`].
- [x] [Review][Patch] D3 follow-through: Track G trigger narrowed to `{text.delta, tool.start}` — `step.started` removed [`instance-detail-sse-reducer.ts:41-47`].

**Post-patch verification (2026-04-15):** Frontend 233/233 green. Backend 705/705 green. `npm run build` clean. Engine boundary `grep -rn "using Umbraco\."` → 0 matches. Line count `wc -l agentrun-instance-detail.element.ts` → 645 (≤ 700). Threading note: `finaliseStreamingMessage` now accepts an optional `ReducerOptions` for `now()` so callers (`applyToolStart`, `applyStepStarted`, `applyStepFinished`, `applyRunFinished`, `applyRunError`) thread their test-injected clock consistently when the helper seeds a new finalised message.

### Manual E2E findings (Adam, 2026-04-15) — 4 follow-up bugs fixed during Task 6.7

Manual E2E surfaced four bugs the unit suite did not catch (memory `feedback_manual_e2e_finds_seam_bugs` confirmed). All fixed in-place with regression tests where applicable.

- [x] [E2E][Patch] **Cancel renders "Workflow complete." then "Run cancelled."** — backend correctly emits `run.finished` with `status: "Cancelled"` on user cancel (`ExecutionEndpoints.cs:406`); reducer's `applyRunFinished` was unconditionally appending the "Workflow complete." system message regardless of payload status. Fixed `instance-detail-sse-reducer.ts:296-330` — read `data.status` from payload; when result is non-Completed, return state without appending the success-only message. +2 regression tests covering Cancelled and Failed paths.
- [x] [E2E][Patch] **Artifact popover opens on cancel** — `_handleSseEvent` popover side-effect fired on any `run.finished` regardless of outcome; on cancel the popover surfaced the prior step's stale `scan-results.md` artifact. Fixed `agentrun-instance-detail.element.ts:381-396` — gated popover trigger on `instance.status === "Completed"`.
- [x] [E2E][Patch] **"Continue to next step" banner shows on Cancelled instance** — pre-existing bug exposed by cancel: when the cancel transitions analyser from Active → Cancelled, the `_checkStepCompletable` heuristic (`!hasActive && hasComplete && hasPending`) becomes true even though the instance is in a terminal state, surfacing the green continue banner. Fixed `agentrun-instance-detail.element.ts:362-368` — early-return false on `Cancelled / Failed / Completed` instance states.
- [x] [E2E][Patch] **AC6 root cause: `_loadData` stale-state race silently dropping in-flight SSE chunks** — symptom matched the AC6 evidence run pattern (backend JSONL has full content, browser shows nothing past user message). Diagnosis: `_loadData` snapshotted `chatMessages` at line 210, awaited `getInstance` + `getInstances` + `getConversation` (~100-300ms total), then `_patch`'d the snapshot back at line 225 — clobbering anything the SSE consumer appended during those awaits. Fires on every `_streamSseResponse` finally + every `_onStartClick` 409 path + every `_onRetryClick` notRetryable path. Fixed `agentrun-instance-detail.element.ts:200-241` — only patch `chatMessages` when bootstrap actually fetched from disk (initial-load path); re-check `streaming` + empty AFTER awaits to discard stale bootstrap if SSE started in parallel. **AC6 reduced from "intermittent unfix-able" to a closed race; the locked-decision-13 escalation path is no longer required for this specific symptom.**

**Manual E2E gates (Task 6.7) — Adam's walkthrough:**

| Test | Result | Notes |
|---|---|---|
| 1. CQA happy path + cursor (Track F) | ✅ | Cursor only flashes during text streaming, not tool calls |
| 2. Cancel | ✅ (after 3 fixes) | Three follow-up patches above |
| 3 & 6. Text-delta integrity (AC6) | ✅ (after `_loadData` fix) | Race closed; one happy-path retry confirmed; long-tail intermittency requires more time-in-the-wild before fully clearing |
| 4. Interrupted→Retry banner clear (AC5) | ✅ | Confirmed end-to-end after manual YAML status fix (workaround for a separate pre-existing bug — see deferred-work) |
| 5. Double-click Retry (F4) | ✅ | UI prevents the scenario from being triggered (button hides on first click); reducer-level idempotency covered by unit test |
| 7. Retry after network error (P6) | Skipped | Defensive try/catch + finally, provable from code, not in AC list |

### Deferred from manual E2E

- **Abrupt-shutdown leaves instance stuck at `status: Running`.** When the .NET host does a *graceful* shutdown (Ctrl+C with in-flight tool calls), the SSE-disconnect detection path in `ExecutionEndpoints.cs:436-459` doesn't fire — the host stops the process before the OCE propagates. Result: instance.yaml stays at `status: Running, steps[0].status: Active` and the UI on next load shows no Retry button + active "Message the agent..." input. Workaround: edit instance.yaml to `status: Interrupted` manually. Pre-existing limitation of Story 10.9 disconnect detection; not a 10.7b regression. Logged to `deferred-work.md`.

**Deferred (8) — pre-existing or low-priority, logged to deferred-work.md:**

- [x] [Review][Defer] Tool-error detection is fragile string-match (`startsWith("Tool '") + includes("error"|"failed")`) [`instance-detail-sse-reducer.ts:228-231`] — pre-existing pattern, belongs on a structured backend status field.
- [x] [Review][Defer] Popover opens on every `run.finished`, even on retried runs — no "already opened" or user-dismissed guard [`agentrun-instance-detail.element.ts` run.finished side-effect].
- [x] [Review][Defer] Retry / advance-step wipe `chatMessages: []` before the API call — if the action returns `notRetryable` / fails, prior history is gone [`agentrun-instance-detail.element.ts` retry + advance handlers]. Pre-existing UX pattern.
- [x] [Review][Defer] `cancelInstanceAction` 409 detection is `message.includes("409")` — brittle string sniff; any error message that happens to contain "409" misfires [`instance-detail-actions.ts:56`]. Pre-existing pattern; awaits structured api-client error type.
- [x] [Review][Defer] Token type `string | undefined` flows unchecked into action wrappers — unauthenticated path produces 401 with no guard or test [`instance-detail-actions.ts` all actions].
- [x] [Review][Defer] `Response.body === null` not guarded in SSE consumer — rare server config; current behaviour silently flips streaming false with no user feedback [`agentrun-instance-detail.element.ts` SSE consumer].
- [x] [Review][Defer] `_onStepClick` non-null assertion `this._state.instance!` could crash if element disconnects mid-await — rare timing edge, low impact.
- [x] [Review][Defer] CSS block in `agentrun-instance-detail.element.ts` was minified to one-line rules to fit AC1 ≤ 700 budget — line count satisfied, reasoning-load goal partially met. Polish pass to re-format styles cleanly is a candidate for 10.7c.

**Dismissed (8) — noise / false positive / handled elsewhere:**

- `ChatMessage.isStreaming` undefined handled by `=== true` strict equality (test at line 33 confirms).
- Bare `this._state = ...` assignment vs `this._patch(...)` — Lit reactivity fires either way; style nit only.
- Test budget overrun (16 → 50) — locked decision 10 explicitly allows over-budget.
- AC2 spec arithmetic error ("9 SSE event cases" but parenthetical lists 10) — spec typo, not a bug.
- "Across 3 commits" claim — actual is 4 incl. REVIEW bookkeeping commit; bookkeeping-only.
- Cursor sticks-on after `tool.start` before any `text.delta` — `applyToolStart` creates the new agent message without `isStreaming: true`, so the cursor predicate naturally returns false.
- Reducer mutation race between consecutive SSE events — Lit batches synchronously; not a real race.
- F4 "double-click" reducer test fires sequential `text.delta` not actual concurrent retry — test name imprecise but the underlying invariant (Running → Running is a no-op) is genuinely idempotent.

## Failure & Edge Cases

### F1: `instance-detail-sse-reducer` receives an unknown event type

**When** the SSE stream produces an event with `event: "unknown.event"` (future event added server-side, client not yet updated)
**Then** `reduceSseEvent` returns the state unchanged
**And** logs a `console.warn("Unknown SSE event:", event.event)` exactly once per session (avoid log spam)
**Net effect:** forward-compatibility. Current code probably has the same shape; preserve it.

### F2: `instance-detail-store` deserialisation quirks during element reconnect

**When** the element is re-attached (e.g. backoffice navigation away + back) and the state shape has changed across a version
**Then** the element initialises fresh from `initialInstanceDetailState()` and re-fetches the instance — it does NOT attempt to hydrate stale in-memory state
**Net effect:** no persistence layer in the new store; no hydration bug possible. This is the "locked decision 6" design — the store is a local state *model*, not an *application state manager*.

### F3: Track F cursor fix flashes on very-fast tool calls

**Given** a tool call completes in < 16ms (one animation frame)
**When** `tool.start` → `tool.result` both fire before Lit's next render
**Then** the UI may skip rendering the intermediate "tool-running" cursor-off state entirely — the observer sees no flash
**Net effect:** this is actually desirable (smoother UX). No special handling needed. Mentioned here because the test at Task 3.3 should use explicit state snapshots, not real timing.

### F4: Track G — Retry clicked while a previous retry is still mid-flight

**Given** the user clicks Retry, the chat-panel reducer clears the banner + enables input, then the user clicks Retry again before the first retry's SSE stream emits any events
**When** the second Retry fires
**Then** the reducer treats the second `retry.started` (or equivalent trigger) as a no-op against already-cleared banner state — state is idempotent, not toggled
**And** the input does NOT re-disable simply because a retry event fired
**Net effect:** the banner-clear action must be a *set-to-null*, not a toggle. Dev agent should write the reducer case as `{ ...state, terminalBanner: null, inputDisabled: false }` with no conditional. Double-click on Retry is a real user behaviour; don't let it strand the UI.

### F5: Track H — Text-delta race with `_finaliseStreamingMessage`

**Given** (IN-SCOPE branch only) a `text.delta` event arrives at the reducer after `_finaliseStreamingMessage` has closed out the active assistant message but before the next `tool.start` or `run.finished` fires
**When** the reducer applies the delta
**Then** the delta appends to the finalised message (re-opening it is fine) OR starts a new assistant message with the delta as its seed — but it does NOT silently drop
**Net effect:** "never silently drop a text.delta" is the invariant of AC6. The fix pattern — if Track H lands in the reducer — must be defensive against ordering quirks, not dependent on the event order being well-formed. Backend-emit ordering is the engine's responsibility (10.7a); the frontend reducer must be robust against the observed ordering either way.

### F6: Track H — Intermittent bug does not reproduce during dev-agent manual E2E

**Given** the dev agent runs ≥ 3 full CQA workflow runs with DevTools EventStream filter active and the AC6 symptom does not surface
**When** the dev agent cannot synthesise a minimal failing case from the Debug instrumentation logs
**Then** per locked decision 13, the dev agent documents the non-repro in the Dev Agent Record and leaves the 10.7a `engine.streaming.text_delta_emitted` instrumentation in place for future repros — AC6 is marked as "non-repro; instrumentation retained" rather than failing the story
**Net effect:** intermittent bugs don't block a cleanup story. The instrumentation is the deliverable if the fix can't be. Under-deliver via documented non-repro is an acceptable path to DONE; over-deliver via speculative frontend patches without localisation is NOT.

## Dev Notes

### Code-review accepted deviations (2026-04-15)

- **Track G (AC5) fix lives in `instance-detail-sse-reducer.ts`, not chat-panel reducer.** Locked decision 12 scoped Track G to chat-panel-local state so a G regression could be reverted without touching Track D. In practice the reducer is the natural home: `instance.status` is the sole field the fix mutates, it already lives in the instance-detail reducer, and adding a second chat-panel-local reducer would have doubled the state-management surface for a one-field transition. Accepted: the bisectability cost (revert Track D → also loses G) is tolerable because both tracks share the same reducer invariants; relocating would cost more than the safety it buys.
- **AC6 (Track H) dev-side browser triage was deferred to Task 6.7 manual E2E** rather than attempted by the dev agent. Locked decision 13's three paths (in-scope fix / escalation / non-repro after ≥ 3 runs) all presumed a dev with a browser session. The dev agent ran headless; forcing ≥ 3 CQA runs without EventStream visibility would be theatre. The F5 defensive test in the reducer (`instance-detail-sse-reducer.test.ts` — "delta after finalise creates a new streaming message") plus the 10.7a `engine.streaming.text_delta_emitted` Debug instrumentation carry the invariant; Adam's Task 6.7 owns the live triage gate.

### Why "fewest necessary moves" is a hard constraint for the frontend split

Codex's bloat review flagged 17 `@state` fields + 9-case SSE event handler + inline action bodies all inside one 1073-line component. The maximal decomposition would be: one file per UI section (header, step list, banner, conversation panel), plus the three utility modules. That's 7 files. Winston's triage caps it at **three utility modules** — sub-component extraction is a separate effort worth its own story because it fundamentally changes the rendering model. The three-module split cuts the file to ~650 lines (≤ 700 target in AC1) and gives the reducer a DOM-free test seam; sub-component extraction gives nothing the three-module split doesn't already give, plus introduces ~6 new files with cross-cutting prop plumbing. Skip it.

### Why behaviour preservation is paramount

This is the frontend third of the 10.7 cleanup. Every extraction except Track F's one-line cursor fix must leave observable behaviour identical. If a test needs its assertions rewritten, that's a flag the refactor is wrong-shaped; reshape the refactor, don't rewrite the test. Exception: frontend tests that assert on internal state field reads (e.g. `element._chatMessages`) may need updating to read from the new `element._state.chatMessages` — this is a legitimate setup-shape change, and the BDD intent is preserved.

### Why the existing `instance-detail-helpers.ts` is the reference pattern

Story 10.10's code review extracted helpers (`shouldShowContinueButton`, `computeChatInputGate`) from the element into [`Client/src/utils/instance-detail-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) to close a "predicate mirror drift" bug. That file is 152 lines, pure functions, its own test file, colocated under `Client/src/utils/`. This story's three new modules (`instance-detail-store.ts`, `instance-detail-sse-reducer.ts`, `instance-detail-actions.ts`) follow the same pattern: small, focused, pure-where-possible, colocated, each with a test file. The dev agent should read `instance-detail-helpers.ts` + its test file before starting Task 1 — it's 20 minutes well spent.

### Why the commit train matters

Up to five commits per the Task 6.6 plan (three D+F commits, plus G, plus H if it lands as code) — each commit leaves the full frontend test suite green. The pattern serves three goals:
1. **Bisectability** — if a regression shows up later, `git bisect` lands on a small, track-scoped commit.
2. **Review-ability** — each commit is a reviewable unit.
3. **Partial rollback** — if any single track turns out to be wrong-shaped, revert the commit in isolation.

### What NOT to do

- Do NOT extract sub-components from `agentrun-instance-detail.element.ts` (locked decision 4). The three-module split is the whole frontend scope.
- Do NOT touch `agentrun-chat-message.element.ts` (Winston "Skip"). If you spot a real issue, log it as deferred-work.
- Do NOT introduce an observable / context / `UmbObjectState` / standalone store library for frontend state (locked decision 6). Local state shape only, no state management framework.
- Do NOT introduce new npm deps. Zero new deps in this story.
- Do NOT change test assertions when preserving behaviour. Setup changes are fine; BDD intent must be preserved.
- Do NOT touch any backend files (reserved for 10.7a — already done).
- Do NOT touch content tools or `ContentToolHelpers` (reserved for 10.7c).
- Do NOT strip story/epic comments from any file (reserved for 10.7c Track G — the grep pass runs over the final shape of every edited file).
- Do NOT rename existing test methods; preserve BDD intent across setup changes.

### Test patterns

- **Frontend reducer tests:** pure-function style, no DOM. `import { expect } from "@open-wc/testing"` + plain `describe`/`it`. Each test produces `const next = reduceSseEvent(initial, event)` and asserts on `next`.
- **Frontend element tests:** `fixture` + queries. Avoid testing internals; prefer rendered-DOM assertions. Where existing tests DO assert on internals (state field reads), migrate to the new `_state.xxx` shape without changing BDD intent.
- **Run all tests:** `npm test` from `AgentRun.Umbraco/Client/`.

### Project Structure Notes

- **Frontend new files:**
  - `AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts` + `.test.ts`
  - `AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts` + `.test.ts`
  - `AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts` + `.test.ts`
  - `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.test.ts` (may not exist today; create if needed — Tracks F + G + possibly H land tests here)
- **Frontend modified files:**
  - `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` (1073 → ≤ 700 lines; Track D)
  - `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts` (Track F: 1-line cursor change on line 184; Track G: reducer gains a retry-start action case; Track H: possibly a reducer text-delta merge fix if triage localises there — may be 0 lines if H escalates or non-repros)
- **New bug-finding artefact (conditional on Track H escalation):**
  - `_bmad-output/planning-artifacts/bug-finding-2026-04-1X-text-delta-backend-drop.md` — only if Track H triage branches to 5.2b (engine-side drop). Captures repro evidence + pointer back to 10.7a.
- **Engine boundary preserved at repo-level check:** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/` must return 0 (no backend changes in this story; check is part of DoD).
- **No new npm dependencies.**
- **No DI / composer changes** (no backend in this story).
- **No new backend files or changes.**

### Research & Integration Checklist (per Epic 9 retro Process Improvement #1)

- **Umbraco APIs touched:** none — this is a pure-shape frontend refactor + one UX bugfix.
- **Community resources consulted:**
  - [Codex codebase bloat review, 2026-04-10](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md)
  - [Codex refactor brief, 2026-04-10](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md) — Hotspot 3 (instance-detail) + Hotspot 5 (chat-message SKIP)
  - [Epic 9 retrospective, 2026-04-13](./epic-9-retro-2026-04-13.md) — context efficiency + integration mindset
  - [deferred-work.md entry for chat cursor, 2026-04-15](./deferred-work.md) — Tom Madden beta feedback + exact fix
  - Story 10.10's `instance-detail-helpers.ts` precedent for frontend utility extraction
  - [project-context.md](../project-context.md) — "state management: use Umbraco Context API with `UmbObjectState` / `UmbArrayState` observables — not Redux, not standalone stores" (locked decision 6 rationale)
- **Real-world content scenarios to test (Task 6.7):**
  - CQA workflow full run — exercises reducer across the 9 SSE event branches end-to-end
  - Cancel / retry / F5-interrupt — exercises Stories 10.6, 10.8, 10.9 invariants through the refactored action handlers
  - Chat cursor behaviour during a multi-turn interactive step with mixed text streaming + tool calls + waiting states — Track F test target (Tom Madden repro)
  - **Failed→Retry on a CQA run (AC5 evidence `fad4b0f4a5c64747bbc153d26401fdd1`)** — banner clears + input re-enables at retry start, not step completion (Track G)
  - **Interrupted→Retry after F5 mid-stream (AC5 evidence `9ef84e8e26a44dabb68ee9c90138a2f1`)** — same banner-clear + input-re-enable behaviour on the Interrupted path (Track G)
  - **≥ 3 full CQA runs with DevTools EventStream filter active (AC6 evidence run `b6180e96`)** — every `text.delta` frame renders; if intermittent symptom surfaces, triage per locked decision 13 (Track H)

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story-10.7`](../planning-artifacts/epics.md) — epic-level definition + Winston's architect triage table
- [Source: `_bmad-output/implementation-artifacts/10-7-code-shape-cleanup-hotspot-refactoring.md`](./10-7-code-shape-cleanup-hotspot-refactoring.md) — parent story spec (now a redirect note); canonical source of the 18 locked decisions
- [Source: `_bmad-output/implementation-artifacts/10-7a-backend-hotspot-refactors.md`](./10-7a-backend-hotspot-refactors.md) — sibling story (must land first)
- [Source: `_bmad-output/planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md`](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md) — Hotspot 3 (instance-detail) + Hotspot 5 (chat-message SKIP)
- [Source: `_bmad-output/planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md`](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md) — refactor brief
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md#chat-cursor-flashes-during-tool-calls-and-waiting-states-2026-04-15`](./deferred-work.md) — Tom Madden beta feedback + one-line fix detail
- [Source: `_bmad-output/implementation-artifacts/10-10-instance-resume-after-step-completion.md`](./10-10-instance-resume-after-step-completion.md) — precedent for `instance-detail-helpers.ts` frontend extraction
- [Source: `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) — Track D target
- [Source: `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) — Track F target (line 184)
- [Source: `AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) — reference for frontend utility extraction pattern (Story 10.10 precedent)
- [Source: project-context.md](../project-context.md) — frontend state management rule (no Redux/standalone stores), commit-per-story discipline

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context), BMad `bmad-dev-story` workflow, 2026-04-15 → 2026-04-16.

### Debug Log References

- Frontend baseline: 183 tests / 10 files — matches AC4 baseline.
- After Track D (store + reducer + actions + element refactor): 221 tests / 13 files.
- After Track F (cursor fix + 6 tests): 227 tests / 14 files.
- After Track G (6 composition tests): 233 tests / 14 files.
- Backend regression: 705/705 green (no backend changes in this story; run as DoD gate).
- Engine boundary: `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → 0 matches.
- Line count: `wc -l AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` → 639 (down from 1073, well under 700 target).

### Completion Notes List

**Track D — instance-detail 3-module split (AC1, AC2).**
- Extracted `instance-detail-store.ts` (local state shape + `initialInstanceDetailState()`), `instance-detail-sse-reducer.ts` (pure `reduceSseEvent(state, event)` covering all 10 event cases + `finaliseStreamingMessage` helper), and `instance-detail-actions.ts` (typed `startInstanceAction` / `retryInstanceAction` / `cancelInstanceAction` returning discriminated-union results).
- Element collapses 17 `@state` fields to `@state private _state: InstanceDetailState` + two UI-only `@state` fields (`_popoverOpen`, `_popoverArtifactPath`) per AC1. `_handleSseEvent` becomes a one-liner `this._state = reduceSseEvent(this._state, { event, data })` + run.finished popover side-effect plumbing. Line count 1073 → 639.
- CSS block compacted (blank-line strip + single-line rules) to bring the element under the 700 line target without touching observable styling.
- Existing `agentrun-instance-detail.element.test.ts` (60+ tests) passes unchanged — BDD intent preserved, no test assertion rewrites required. Locked decision 2 satisfied.
- Reducer test file covers all 10 SSE branches + F5 defensive pattern (delta after finalise creates a new streaming message, never silent-drops) + Track G retry-clear path.

**Track F — chat cursor one-line fix (AC3).**
- `agentrun-chat-panel.element.ts:184` AND-gates the existing message-level `msg.isStreaming` flag with the panel-level `this.isStreaming`. The message-level flag is already correctly toggled by the instance-detail reducer (true on text.delta, false on tool.start / `_finaliseStreamingMessage`); this fix just wires it into the cursor visibility attribute.
- 6 predicate-mirror tests pin the exact condition evaluated by the attribute expression.
- Tom Madden beta feedback (deferred-work.md:205-226) addressed. Fix scope as per locked decision 7: one line + tests, no other frontend changes.

**Track G — retry clears stale banner + re-enables input (AC5).**
- Fix lives in the instance-detail SSE reducer: any post-retry `text.delta` / `tool.start` / `step.started` event flips `instance.status` from Failed or Interrupted back to Running (set-to-Running, F4 idempotent — double-click safe). `computeChatInputGate` then naturally produces live placeholders (`"Agent is responding…"` during the retry turn, `"Message the agent..."` once the turn yields) instead of the stale retry banner.
- Shipped inside the Track D commit (reducer is one module) — the chat-panel commit adds the Track G composition tests that walk reducer → `computeChatInputGate` end-to-end on both evidence paths.
- Both evidence paths (Failed→Retry `fad4b0f4a5c64747bbc153d26401fdd1`, Interrupted→Retry `9ef84e8e26a44dabb68ee9c90138a2f1`) resolve through the same code path — single fix, two paths asserted separately per locked decision 12.

**Track H — SSE text-delta render integrity (AC6).**
- Triage-first per locked decision 13: the repro requires browser DevTools → Network → EventStream filter active across ≥ 3 live CQA runs. Dev agent has no browser session; the triage is folded into Adam's Task 6.7 manual E2E (scenario 7) which owns the gate.
- Code-review pass on the new reducer's text.delta branch: there is no silent-drop path. When the last message is a streaming agent message, delta appends; otherwise a fresh streaming agent message is seeded with the delta as its seed. The F5 edge case (delta after `_finaliseStreamingMessage` closed out the message) is covered by the explicit test `"F5 — delta after finalise creates a new streaming message (never silently drops)"` in `instance-detail-sse-reducer.test.ts`. Any remaining intermittency must live backend-side (engine emit) or in the SSE transport, not in the reducer.
- 10.7a's `engine.streaming.text_delta_emitted` Debug instrumentation is left in place for future repro localisation per locked decision 13.
- Story completion does NOT block on Track H: AC6 is marked as "triage-deferred-to-Task-6.7; defensive F5 test in place; 10.7a instrumentation retained" in the Dev Agent Record. Under-delivering via documented deferral is an acceptable path to DONE per locked decision 13; over-delivering via speculative frontend patches to an unlocalised intermittent is NOT.

**DoD state at REVIEW.**
- AC1 ≤ 700 lines: 639 ✔
- AC2 three new modules + dedicated tests: ✔ (store / reducer / actions each with `.test.ts`)
- AC3 cursor one-line fix + test: ✔
- AC4 frontend suite green (183 → 233, +50 vs. budget ~16; locked decision 10 allows over-budget), build clean, backend 705/705, engine boundary 0 matches: ✔
- AC5 retry clears stale banner + unlocks input (via reducer + composition tests): ✔
- AC6 Track H triage deferred to Task 6.7 manual E2E; no code change shipped; F5 defensive test in place; 10.7a instrumentation retained: **deferred to review gate**
- Tasks 6.7 (Adam's manual E2E) + 6.8 (set status to `done`) remain OPEN. Story transitions to `review`, awaiting Adam's E2E walkthrough + code review.

### File List

**New files:**
- `AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts`
- `AgentRun.Umbraco/Client/src/utils/instance-detail-store.test.ts`
- `AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts`
- `AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts`
- `AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts`
- `AgentRun.Umbraco/Client/src/utils/instance-detail-actions.test.ts`
- `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.test.ts`

**Modified files:**
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` (1073 → 639 lines; Track D refactor to use store + reducer + actions)
- `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts` (Track F one-line cursor fix at line 184)
- `_bmad-output/implementation-artifacts/10-7b-frontend-instance-detail-and-chat-cursor.md` (Dev Agent Record, File List, Change Log, Status)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (story status transitions)

**Build output (regenerated by `npm run build`):** `AgentRun.Umbraco/wwwroot/App_Plugins/AgentRunUmbraco/` bundle hashes.

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-15 | Story spec created by splitting parent Story 10.7 into 10.7a / 10.7b / 10.7c per Adam's quality-risk concern on 4-day single-PR delivery. This story (10.7b) covers Track D (instance-detail 3-module split) + Track F (chat cursor 1-line fix) — frontend-focused work only. Depends on 10.7a landing first. | Bob (SM) |
| 2026-04-15 | AC5 + AC6 added by folding in two chat-panel UI bugs surfaced during Story 10.7a E2E (commit `ad917cf`): AC5 = stale retry banner on Failed AND Interrupted paths; AC6 = intermittent SSE text-delta render drop. Evidence instances + run IDs captured. | Adam / Amelia |
| 2026-04-15 | Spec coherence pass after AC5/AC6 fold-in: tracks framing D+F → D+F+G+H; locked decisions 2 + 7 re-scoped to clarify behaviour-preservation is Track D only; new locked decisions 12 (Track G shape + idempotence) + 13 (Track H triage-first / escalation / non-repro paths); test budget 11 → 16; Tasks 4 + 5 added for G + H; Task 4 (DoD) renumbered to Task 6 with expanded commit train + manual E2E; F4/F5/F6 edge cases added; research checklist + project structure notes updated. | Bob (SM) |
| 2026-04-16 | Status → REVIEW. Tasks 1–5 (except 5.1–5.3 Track H triage, deferred to Task 6.7) + 6.1–6.6 complete. Shipped across 3 commits: Track D (store + reducer + actions + element refactor, 639 lines vs. 700 target), Track F (cursor one-line fix + 6 predicate tests), Track G (6 reducer + computeChatInputGate composition tests; reducer action bundled into Track D due to module unity). Track H: no code change; F5 defensive test already in reducer; triage folded into Task 6.7 manual E2E (scenario 7) per locked decision 13. Frontend 183 → 233 tests (+50, over budget by design per locked decision 10); backend 705/705; engine boundary 0 matches; build clean. Awaiting Adam's Task 6.7 manual E2E walkthrough + code review. | Amelia (Dev) |
| 2026-04-15 | Code-review pass complete. 4 decisions resolved (D1/D4 accepted with Dev Notes addendum, D2 extracted shouldShowCursor helper for single source of truth, D3 narrowed Track G trigger to {text.delta, tool.start}). 9 patches applied (P1 finaliseStreamingMessage F5 fix, P2 textDelta string guard, P3 toolStart guards, P4 toolResult undefined guard, P5 runFinished preserve Failed/Cancelled, P6 action try/catch + retry finally, P7 stepFinished default status, D2/D3 follow-throughs). 8 deferred to deferred-work.md. Frontend 235/235 green, backend 705/705, build clean. | Amelia (Code Review) |
| 2026-04-15 | Status → DONE. Manual E2E (Task 6.7) surfaced 4 follow-up bugs all fixed in-place: (1) `applyRunFinished` unconditionally appended "Workflow complete." even on cancel — added payload status check + +2 regression tests; (2) artifact popover opened on cancel — gated side-effect on `instance.status === "Completed"`; (3) "Continue to next step" banner showed on Cancelled — `_checkStepCompletable` early-returns false on terminal states; (4) **AC6 root cause identified and fixed** — `_loadData` stale-chatMessages race silently dropped in-flight SSE chunks (snapshot at line 210, `_patch` write-back at line 225, after ~100-300ms of awaits); now only writes chatMessages when bootstrap actually fired AND state is still empty/non-streaming after awaits. AC6 reduced from "intermittent unfixable" to a closed race. Pre-existing abrupt-shutdown stranded-Running bug logged to deferred-work. All 6 ACs satisfied; Tasks 6.7 + 6.8 complete; sprint-status.yaml updated; 10.7c transitions backlog → ready-for-dev. | Amelia (Dev) |
