# Story 10.7b: Frontend Instance-Detail Split + Chat Cursor Fix

Status: backlog (transitions to `ready-for-dev` when [Story 10.7a](./10-7a-backend-hotspot-refactors.md) is done)

**Depends on:** Story 10.7a (backend hotspot refactors — must land first so the backend baseline is stable before frontend work begins). Story 10.10 precedent (`instance-detail-helpers.ts` already extracted — reference pattern for this story's utility modules).
**Followed by:** Story 10.7c (content-tool DRY + comment hygiene). Split from the original [Story 10.7 parent spec](./10-7-code-shape-cleanup-hotspot-refactoring.md) on 2026-04-15 per Adam's quality-risk concern on single-PR delivery.
**Branch:** `feature/epic-10-continued`
**Priority:** 9th in Epic 10 (10.2 → 10.12 → 10.8 → 10.9 → 10.10 → 10.1 → 10.6 → 10.11 → 10.7a → **10.7b** → 10.7c → 10.4 → 10.5).

> **Two tracks shipped as one frontend-focused story.** Covers Track D (instance-detail 3-module split) and Track F (chat-panel cursor 1-line fix) from Winston's architect triage of the [Codex bloat review (2026-04-10)](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md). Track F is bundled here because it touches the same frontend mental model and shares the same E2E harness (chat UI streaming behaviour).
>
> **The goal is clearer responsibility boundaries with the fewest necessary moves — NOT maximum decomposition.** Every extraction must reduce reasoning load; thin one-method wrappers are explicitly rejected. When in doubt, leave the file alone.

## Story

As a maintainer of AgentRun.Umbraco,
I want the largest frontend accumulation point (`agentrun-instance-detail.element.ts`) refactored into clearer responsibility boundaries AND the chat cursor flash bug fixed,
So that future UI changes are lower-risk, the state / reducer / action concerns are testable without a DOM, and beta feedback about the cursor is resolved before public launch.

## Context

**UX Mode: Interactive (primary) + Autonomous (secondary).** Track D is a pure-shape refactor — UI behaviour unchanged. Track F improves interactive-mode UX only (Tom Madden beta feedback 2026-04-15): the block cursor currently flashes during tool calls / waiting states; after this story it only shows during active text streaming.

### Why this story was split from the parent

See [Story 10.7a §Why this story was split from the parent](./10-7a-backend-hotspot-refactors.md). This story (10.7b) is track-group 2 of 3 from the original parent spec. Running the backend refactor first (10.7a) lets the full test suite re-settle before frontend work begins — any weirdness in the ToolLoop / StepExecutor reshape surfaces first, not mingled with a 1073-line frontend reshape.

### The two tracks in this story

| Track | Target | Disposition | Why | Est. effort |
|---|---|---|---|---|
| **D** | [`Client/agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) (1073 lines) | Split | Strongest frontend hotspot; SSE event reducer + state store + action handlers out of the view | 1.5 days |
| **F** | [`Client/agentrun-chat-panel.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts) line 184 | 1-line fix + 1 test | Beta feedback (Tom Madden); cursor flashes during tool calls — wire existing message-level `isStreaming` flag | 0.1 day |

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
2. **Behaviour-preserving refactor — the sole behaviour change is Track F's cursor fix.** Track D must leave existing tests passing without modification to test *assertions* (setup may change to match new state shape; BDD intent must be preserved). Track F is the only observable-behaviour change: the cursor visibility expression is corrected to match the message-level flag.
3. **Engine boundary must stay clean (Story 10.11 invariant).** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` must remain at 0 matches. This story doesn't touch `Engine/` directly, but the invariant must be preserved across the repo at DoD.

**Track D (instance-detail):**

4. **Track D extracts exactly three modules — `instance-detail-sse-reducer.ts` + `instance-detail-store.ts` + `instance-detail-actions.ts`.** Do NOT extract sub-components (header/step-list/banner/conversation-panel). Component subdivision is a separate effort that would double the story; the state/action/reducer split alone reduces the file from 1073 lines to an estimated ~650 (render + wiring + existing helper calls), which is a worthwhile win on its own. The existing [`instance-detail-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) (152 lines — extracted during Story 10.10) is the reference pattern: small, focused, pure functions with their own test file.

5. **Track D reducer is PURE.** `instance-detail-sse-reducer.ts` exports a pure `reduceSseEvent(state, event) → newState` function. No side effects (no `dispatchEvent`, no console calls, no SSE client references). State is the shape of the fields listed in AC2. The component calls `this._state = reduceSseEvent(this._state, parsed)` in `_handleSseEvent` and the reducer has its own unit-test file that runs without a DOM.

6. **Track D store is NOT a Redux/Umbraco-context store.** Per [project-context.md](../project-context.md) frontend rules, "state management: use Umbraco Context API with `UmbObjectState` / `UmbArrayState` observables — not Redux, not standalone stores." `instance-detail-store.ts` in this story is a **local state shape module** — a `type InstanceDetailState` definition + default initial state + the reducer. It does NOT introduce an observable, a context, or cross-component state sharing. Naming is deliberate: "store" in the refactor brief means "state model," not "application state manager."

**Track F (chat cursor):**

7. **Track F is a one-line code change + one frontend test.** The deferred-work entry ([deferred-work.md:205-226](./deferred-work.md)) specifies the fix exactly. No other frontend changes in Track F — do NOT touch `agentrun-chat-message.element.ts` (Winston skip), do NOT touch the streaming field on ChatMessage (already correct).

**Cross-cutting (frontend-relevant):**

8. **No new npm dependencies.** Track D uses existing Lit / TypeScript. Track F is a one-char change.

9. **Test budget target: ~11 new tests.** Breakdown: Track D (~10: 6 reducer tests for the SSE event cases, 2 for store shape, 2 for actions), Track F (~1 frontend). Full frontend suite must stay green. Preserve ALL existing tests — any test deletion must be justified in the Dev Notes as either duplicate coverage or no-longer-applicable setup.

10. **Test counts are guidelines, not ceilings.** From Epic 8+ retro. If the reducer needs 10 tests to cover all SSE event branches, write the 10 tests. Under-budget is fine too — don't pad.

11. **Priority ordering inside the story: D → F.** Track D first (heavier work), Track F last (one-line change slotted into the same frontend test run). Track F could go first but bundling the test green-gate at the end is simpler.

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
**Then** frontend tests pass (baseline 183 → expect ~194 with ~11 new tests per locked decision 9; delta TBD by dev agent)
**And** `npm run build` in `AgentRun.Umbraco/Client/` is clean
**And** line-count check: `wc -l AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` → ≤ 700 (AC1)
**And** `dotnet test AgentRun.Umbraco.slnx` still green at whatever count 10.7a left it (no backend regressions)
**And** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns **0** matches (Story 10.11 invariant preserved — no backend work in this story, but the repo-wide invariant check is part of DoD)

## Tasks / Subtasks

### Task 1: Track D — Extract `instance-detail-store.ts` + `instance-detail-sse-reducer.ts` (AC1, AC2)

- [ ] 1.1 Read [`agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) lines 37–86 (state fields) + 513–735 (`_handleSseEvent`).
- [ ] 1.2 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-store.ts):
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
- [ ] 1.3 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts) exporting a pure `reduceSseEvent(state, event): InstanceDetailState`. Port every branch from the element's `_handleSseEvent` as a case in a switch on `event.event`. Side effects (popover open on `run.finished`, scroll-to-bottom triggers) do NOT go in the reducer — they stay in the component and fire after the reducer produces new state.
- [ ] 1.4 Update the element to hold a single `@state private _state: InstanceDetailState = initialInstanceDetailState();`. Replace the 17 field reads throughout the file with reads from `this._state.xxx`. Replace the 17 field writes inside `_handleSseEvent` with `this._state = reduceSseEvent(this._state, parsed);` followed by the minimal side-effect plumbing (popover open, etc.).
- [ ] 1.5 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts) with one test per SSE event case — 10 cases total (9 events from AC2 + initial state). Use `@open-wc/testing` + `expect` per project convention.
- [ ] 1.6 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-store.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-store.test.ts) with 1–2 tests: `initialInstanceDetailState()` returns the expected zero-state.
- [ ] 1.7 Run `npm test` — element tests pass (the existing `agentrun-instance-detail.element.test.ts` should still pass because externally observable behaviour is unchanged; if any test breaks due to state-shape change, adjust the test to match the new internal shape and note it in Dev Notes).

### Task 2: Track D — Extract `instance-detail-actions.ts` (AC1, AC2)

- [ ] 2.1 Read the action methods at [`agentrun-instance-detail.element.ts:389-438`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L389) and [`810-843`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L810).
- [ ] 2.2 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-actions.ts). Export typed wrappers:
  ```typescript
  export async function startInstanceAction(apiClient: AgentRunApi, instanceId: string): Promise<Response> { ... }
  export async function retryInstanceAction(apiClient: AgentRunApi, instanceId: string, fromStepId: string): Promise<Response> { ... }
  export async function cancelInstanceAction(apiClient: AgentRunApi, instanceId: string): Promise<void> { ... }
  ```
  These are the API call + error-translation bodies ONLY. UI concerns (modal confirmation, loading flag toggling) stay in the component.
- [ ] 2.3 Component `_onStartClick` / `_onRetryClick` / `_onCancelClick` methods become: (a) UI-side state reset + flag toggles, (b) call the action function, (c) wire the returned `Response` / `ReadableStream` into `_handleSseEvent`.
- [ ] 2.4 Create [`AgentRun.Umbraco/Client/src/utils/instance-detail-actions.test.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-actions.test.ts) with 2–3 tests: happy-path start, happy-path retry, cancel surfaces API errors.
- [ ] 2.5 Run `npm test` — all tests pass. Verify `agentrun-instance-detail.element.ts` ≤ 700 lines (AC1).

### Task 3: Track F — Chat cursor one-line fix (AC3)

- [ ] 3.1 Open [`agentrun-chat-panel.element.ts` line 184](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts#L184).
- [ ] 3.2 Change:
  ```typescript
  ?is-streaming=${i === lastIndex && msg.role === "agent" && this.isStreaming}
  ```
  to:
  ```typescript
  ?is-streaming=${i === lastIndex && msg.role === "agent" && this.isStreaming && msg.isStreaming === true}
  ```
- [ ] 3.3 Add a test in [`agentrun-chat-panel.element.test.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.test.ts) — if the file doesn't exist, create it — asserting (a) cursor present when both flags true, (b) cursor absent when `this.isStreaming=true && msg.isStreaming=false`. Use `@open-wc/testing` `fixture` + query-selector on the rendered DOM.
- [ ] 3.4 Run `npm test` — new test green.

### Task 4: DoD verification + commit train

- [ ] 4.1 Full frontend test: `cd AgentRun.Umbraco/Client && npm test` → all green (AC4, expect ~194).
- [ ] 4.2 Full backend test: `dotnet test AgentRun.Umbraco.slnx` → still green at whatever 10.7a left it (no backend changes in this story).
- [ ] 4.3 Build clean: `npm run build` in `Client/` → clean. `dotnet build AgentRun.Umbraco.slnx` → 0 new warnings.
- [ ] 4.4 Engine boundary check: `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → 0 matches (Story 10.11 invariant — not expected to change in this story, but part of DoD).
- [ ] 4.5 Line-count check: `wc -l AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` → ≤ 700 (AC1).
- [ ] 4.6 Commit train:
  - Commit 1: Track D part 1 (store + reducer + their tests + element refactor to use them)
  - Commit 2: Track D part 2 (actions + their tests + element refactor to call them)
  - Commit 3: Track F (chat cursor one-line fix + test)

  Each commit leaves the full frontend test suite green. Each commit body ends with the standard Co-Authored-By trailer per CLAUDE.md.
- [ ] 4.7 Manual E2E — Adam walks the frontend-relevant scenarios:
  1. Start TestSite: `dotnet run` from `AgentRun.Umbraco.TestSite/`.
  2. Run the **Content Quality Audit** workflow end-to-end. Verify: chat UI streams correctly (Track D — reducer works end-to-end), agent responses + tool calls + results all render in the right order, SSE event handling survives the refactor.
  3. **Cancel a running step** — verify cancel UI flows work (Story 10.8 invariant across Track D action extraction).
  4. **Retry a failed step** — verify retry UI flows work (Story 10.6 invariant).
  5. **Interrupt via F5** during a long LLM step — verify Interrupted flow surfaces correctly in the UI (Story 10.9 invariant).
  6. **Chat cursor behaviour (Track F)** — observe the cursor during a multi-turn interactive step: the block cursor (▋) should ONLY appear during active text streaming, NOT during tool calls, NOT during waiting states, NOT between messages. This is the Tom Madden repro scenario; it's the acceptance condition for Track F.
- [ ] 4.8 Set story status to `done` in this file + `sprint-status.yaml` once 4.1–4.7 all pass. Transition 10.7c from `backlog` to `ready-for-dev` in the same sprint-status update.

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

## Dev Notes

### Why "fewest necessary moves" is a hard constraint for the frontend split

Codex's bloat review flagged 17 `@state` fields + 9-case SSE event handler + inline action bodies all inside one 1073-line component. The maximal decomposition would be: one file per UI section (header, step list, banner, conversation panel), plus the three utility modules. That's 7 files. Winston's triage caps it at **three utility modules** — sub-component extraction is a separate effort worth its own story because it fundamentally changes the rendering model. The three-module split cuts the file to ~650 lines (≤ 700 target in AC1) and gives the reducer a DOM-free test seam; sub-component extraction gives nothing the three-module split doesn't already give, plus introduces ~6 new files with cross-cutting prop plumbing. Skip it.

### Why behaviour preservation is paramount

This is the frontend third of the 10.7 cleanup. Every extraction except Track F's one-line cursor fix must leave observable behaviour identical. If a test needs its assertions rewritten, that's a flag the refactor is wrong-shaped; reshape the refactor, don't rewrite the test. Exception: frontend tests that assert on internal state field reads (e.g. `element._chatMessages`) may need updating to read from the new `element._state.chatMessages` — this is a legitimate setup-shape change, and the BDD intent is preserved.

### Why the existing `instance-detail-helpers.ts` is the reference pattern

Story 10.10's code review extracted helpers (`shouldShowContinueButton`, `computeChatInputGate`) from the element into [`Client/src/utils/instance-detail-helpers.ts`](../../AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts) to close a "predicate mirror drift" bug. That file is 152 lines, pure functions, its own test file, colocated under `Client/src/utils/`. This story's three new modules (`instance-detail-store.ts`, `instance-detail-sse-reducer.ts`, `instance-detail-actions.ts`) follow the same pattern: small, focused, pure-where-possible, colocated, each with a test file. The dev agent should read `instance-detail-helpers.ts` + its test file before starting Task 1 — it's 20 minutes well spent.

### Why the commit train matters

Three commits per the Task 4.6 plan — each commit leaves the full frontend test suite green. The pattern serves three goals:
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
  - `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.test.ts` (may not exist today; create if needed)
- **Frontend modified files:**
  - `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` (1073 → ≤ 700 lines)
  - `AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts` (1-line change)
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
- **Real-world content scenarios to test (Task 4.7):**
  - CQA workflow full run — exercises reducer across the 9 SSE event branches end-to-end
  - Cancel / retry / F5-interrupt — exercises Stories 10.6, 10.8, 10.9 invariants through the refactored action handlers
  - Chat cursor behaviour during a multi-turn interactive step with mixed text streaming + tool calls + waiting states — Track F test target (Tom Madden repro)

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

_To be filled by dev agent._

### Debug Log References

_To be filled by dev agent._

### Completion Notes List

_To be filled by dev agent._

### File List

_To be filled by dev agent._

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-15 | Story spec created by splitting parent Story 10.7 into 10.7a / 10.7b / 10.7c per Adam's quality-risk concern on 4-day single-PR delivery. This story (10.7b) covers Track D (instance-detail 3-module split) + Track F (chat cursor 1-line fix) — frontend-focused work only. Depends on 10.7a landing first. | Bob (SM) |
