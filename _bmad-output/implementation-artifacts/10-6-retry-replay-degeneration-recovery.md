# Story 10.6: Retry-Replay Degeneration Recovery

Status: backlog

**Depends on:** 9.7 (Tool Result Offloading for `fetch_url` — must ship first; the recovery design depends on the `.fetch-cache/` handle pattern), 7.2 (Step Retry with Context Management — done; this story extends 7.2's retry path)
**Blocks:** Nothing in Epic 9 (deliberately scoped as a fast-follower — see "Epic placement" below). May be pulled forward into Epic 9 if private-beta telemetry shows the failure mode firing more than rarely; explicit escalation criteria are recorded under "Beta-Blocker Escalation Criteria".
**Cooperates with:** 9.0 (ToolLoop Stall Recovery — the upstream stall *detector*; this story is the *recovery layer underneath* it)

> **Fast-follower (Epic 10), with explicit escalation criteria.** This is an engine state-machine bug in the retry path. When a step fails mid-tool-loop and the user clicks retry, the existing Story 7.2 retry flow replays the JSONL conversation history including all completed tool results from the failed attempt. The model resumes in a degenerate state — its prior-self appears to have already finished all the tool work — and sometimes produces text instead of the next required tool call. Story 9.0's stall detector then (correctly) fires on the retry, and the user's recovery affordance fails silently in front of them.

## Story

As a developer who has just hit a step failure mid-tool-loop and clicked retry,
I want the engine to reshape the replayed conversation so the model does not see a degenerate "all my tools have already run, only `write_file` is left" state,
So that retries actually recover the workflow instead of stalling again on the same path that just failed.

## Context

**UX Mode: Both interactive and autonomous — but the failure surface is the retry path, which only exists in interactive mode for V1.** Autonomous mode currently has no retry affordance, so this story is interactive-only by virtue of where the bug lives.

**Authorisation:** This story was authorised on 2026-04-08 by [Sprint Change Proposal 2026-04-08 — Story 9-1b Rescope](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope.md). The retry-replay finding was originally captured as an edge case bullet in Story 9.1b's reshaped epic outline (see [epics.md](../planning-artifacts/epics.md) Story 9.1b Failure & Edge Cases section, "Retry-replay degeneration" bullet). During the 9.1b course correction it was promoted to its own story because:

1. It is **not** fixable by Story 9.7 (offloading reduces the *frequency* of the upstream stall but does not change the shape of the replayed conversation on retry).
2. It is **not** fixable by Story 9.1b's Option 2 (server-side AngleSharp extraction also reduces stall frequency but does not touch the retry path).
3. It is **not** fixable by Story 9.0 (which is the *detector*; this story is the *recovery layer* underneath it).
4. It is **not** prompt-fixable. No invariant in `scanner.md` can defend against the model seeing a state where its prior-self appeared to have completed all the fetches.

### The bug being fixed (in code terms)

[`StepExecutor.ExecuteStepAsync`](../../AgentRun.Umbraco/Engine/StepExecutor.cs) always loads the conversation history via `IConversationStore.GetHistoryAsync` and replays it through `ConvertHistoryToMessages` before calling `ToolLoop.RunAsync`. There is no "this is a retry, do something different" branch — every step execution is a "resume from where the JSONL left off" operation. The retry endpoint added in Story 7.2 differs from a normal start only in that it calls `ConversationStore.TruncateLastAssistantEntryAsync` first.

That truncate primitive is intentionally conservative. Per the Story 9.0 fix in [`ConversationStore.cs`](../../AgentRun.Umbraco/Instances/ConversationStore.cs) at line 136, **if the last assistant entry is followed by a tool_result entry (i.e. the conversation is already at a clean tool_use → tool_result boundary), the truncate is a no-op.** The reason it has to be a no-op is correct: removing the assistant entry would orphan the trailing `tool_result` and the next provider call would `400` ("tool_result with no matching tool_use in previous message").

**This is exactly the shape the failing instance produced.** In `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl`, the tail looks like:

```
... assistant tool_call: fetch_url("wikipedia")
    tool result:        HTTP 403: Forbidden
    (stall — empty assistant turn that StallDetector caught and threw on; the empty turn is NOT recorded to JSONL because StepExecutor's catch block fires before the recorder runs)
```

When the user retries:
1. `TruncateLastAssistantEntryAsync` walks backwards, finds the `fetch_url("wikipedia")` assistant entry, sees it is followed by a tool_result, and **correctly** declines to truncate (avoiding the 400).
2. `StepExecutor.ExecuteStepAsync` reloads the JSONL via `GetHistoryAsync` and replays the **entire** conversation including all five `fetch_url` tool_call/tool_result pairs.
3. The model now sees: "I have already issued five `fetch_url` calls; the last one returned a 403; what should I do next?" From the model's POV, the natural answer is *not* "issue write_file" — its conversation tail is a sequence of fetch operations, not a transition to writing. So it sometimes produces text ("Let me write the results now.") instead of calling `write_file`.
4. Story 9.0's `StallDetector` correctly classifies the text-only assistant turn as `StallNarration` and raises `StallDetectedException`.
5. The retry has now failed with the same stall the user just clicked retry to escape. The user's recovery affordance is broken.

**Why this isn't fixed by 9.7:** After 9.7 lands, the conversation context is small (handles instead of raw HTML, ~1 KB per fetch instead of ~400 KB), so context-bloat-induced stalls become rare. But the *shape* of the replayed conversation on retry is unchanged: five tool_call/tool_result pairs followed by "now, model, decide what to do next." The degenerate-state risk is shape-driven, not size-driven. A small but degenerately-shaped conversation can still produce the bug.

**Why this isn't fixed by 9.1b's Option 2:** Same reason. Structured extraction collapses the per-fetch context cost further but doesn't touch the retry path's replay shape.

**Why this isn't fixed by 9.0:** 9.0 is doing exactly the right thing — it correctly detects and classifies the second stall and raises the exception. 9.0 is the *detector*. This story is the *recovery design that prevents the model from being in a state where 9.0 has to fire on the retry in the first place*.

### Failing instance evidence

Same instance as the context-bloat finding: `caed201cbc5d4a9eb6a68f1ff6aafb06`. The existing `conversation-scanner.jsonl` in that instance contains the evidence — both the original failure and (per Adam's manual reproduction) the second-stall-on-retry. **Do not capture new fixtures.** Reuse the existing instance for fixture extraction during test work (Tasks 4 and 5).

## Epic placement — fast-follower vs beta blocker (LOCKED PENDING ADAM CONFIRMATION)

Bob's recommendation after reading [`StepExecutor.cs`](../../AgentRun.Umbraco/Engine/StepExecutor.cs), [`ConversationStore.cs`](../../AgentRun.Umbraco/Instances/ConversationStore.cs), [`ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs), and [`InstanceManager.cs`](../../AgentRun.Umbraco/Instances/InstanceManager.cs):

**Fast-follower (Epic 10), with explicit escalation criteria. Bob agrees with Adam's instinct.**

Reasoning grounded in the source:

1. **The dominant trigger for the upstream stall (context-bloat) is eliminated by 9.7.** Story 9.7 ships the offloaded handle pattern. Once 9.7 lands, the most common reason for a step to fail mid-tool-loop disappears. The retry path will be hit less often.
2. **The replayed-degenerate-state failure mode requires a specific shape:** several successful tool_call/tool_result pairs followed by a step failure. Post-9.7, the failures that produce that shape (network errors after several successful fetches, transient provider errors mid-batch, validation failures after several writes) are genuinely uncommon in normal use.
3. **The user has a manual workaround:** click retry again. Each retry is a fresh roll of the dice on whether the model produces text or `write_file`. Empirically (Adam's manual reproduction) it sometimes succeeds within 1–2 retries. The workaround is acceptable for a private beta whose explicit purpose is to surface bugs.
4. **The fix is non-trivial.** Three viable design alternatives exist (see Dev Notes), all of them touch engine state-machine territory, none of them is a one-line change. Doing the design work under beta-release pressure is the wrong forcing function.

**Counter-argument considered and rejected for Epic 9 placement:** the retry path is a primary recovery flow, and "click retry → it stalls again" is bad UX. True. But the failure mode is *recoverable* (re-clicking retry usually works) and the upstream cause is being substantially reduced by 9.7 in the same release. Compared to the cost of a fully-designed engine state-machine fix on the critical path to private beta, the fast-follower position is the better trade.

### Beta-Blocker Escalation Criteria

This story is fast-follower **only** as long as the following conditions hold. If any of them fail, pull this story forward into Epic 9:

- After 9.7 ships, **fewer than 1 in 10** retries against real-world workloads should hit the retry-replay degeneration. Measure during the 9-1c manual E2E resumption and the 9-1b polish loop. If the rate is higher, escalate.
- The manual workaround (retry up to 3 times) should succeed in **at least 90% of cases**. If the workaround fails more than that, escalate.
- The failure should not produce any **silent data loss** or **silent state corruption**. If retry-replay degeneration is found to leave the instance in an inconsistent state (orphaned tool results, stale step status, partial write_file output), escalate immediately — that's a different bug class and a beta blocker.
- No private beta tester reports the symptom **on a happy-path workflow** within the first week of beta. If they do, escalate.
- **If Story 9.7's manual E2E gate (Task 7) shows ANY retry-replay degeneration during the 5-batch test, escalate immediately.** Reasoning (Winston, 2026-04-08): 9.7's manual E2E is the first opportunity to observe post-9.7 retry behaviour empirically. A deliberate-test trigger is stronger signal than a wild rate measurement and warrants the more conservative response — a single occurrence on the 9.7 gate is enough to pull 10.6 forward into Epic 9.

The escalation path is `bmad-correct-course` to move this story from Epic 10 to Epic 9 with the same dependency chain.

## Files in Scope

| File | Change |
|---|---|
| `AgentRun.Umbraco/Tools/FetchUrlTool.cs` | **Task 0.5 prerequisite (added per Winston 2026-04-08):** add cache-on-hit short-circuit to `ExecuteAsync` so a re-issued `fetch_url` call for the same URL returns a handle indistinguishable from the cache-miss case without making an HTTP request. **This is the only `FetchUrlTool` change in 10.6** — Story 9.7's offloading shape is preserved exactly. |
| `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` | **Primary recovery call site.** The retry endpoint (added by Story 7.2) is where the new recovery primitive is invoked — at the same call site as the existing `TruncateLastAssistantEntryAsync` call. **`StepExecutor` is NOT modified.** Per Winston's 2026-04-08 design lock, recovery happens in the endpoint, not the executor — `StepExecutor` stays shape-agnostic to retry semantics. |
| `AgentRun.Umbraco/Instances/ConversationStore.cs` | Add the `WipeHistoryAsync` primitive: atomically rename the existing conversation file to `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl` (hyphenated timestamps for filesystem portability), then proceed. **Surface a clear error on rename failure — never silently delete or proceed with a stale conversation.** The existing `TruncateLastAssistantEntryAsync` stays as-is — Story 9.0's clean-boundary fix is correct and is not regressed. |
| `AgentRun.Umbraco/Instances/IConversationStore.cs` | Interface addition: `WipeHistoryAsync(workflowAlias, instanceId, stepId, ct)`. |
| `AgentRun.Umbraco.Tests/Engine/StepExecutorRetryTests.cs` (NEW or extend existing) | The integration test surface for the retry-replay recovery — fake `IChatClient` + captured fixtures from `caed201cbc5d4a9eb6a68f1ff6aafb06`. |
| `AgentRun.Umbraco.Tests/Instances/ConversationStoreTests.cs` | Extend with unit tests for the new primitive. |
| `AgentRun.Umbraco.Tests/Engine/ToolLoopRetryReplayTests.cs` (NEW) | End-to-end-shaped tests using a fake LLM that produces the degenerate-state symptom on retry; assert the recovery design prevents the stall. |

**Files explicitly NOT touched:**

- `AgentRun.Umbraco/Engine/StepExecutor.cs` — **per Winston's 2026-04-08 design lock, `StepExecutor` is NOT modified.** Recovery work lives in the retry endpoint, not the executor. `StepExecutor` stays shape-agnostic to retry semantics.
- `AgentRun.Umbraco/Engine/ToolLoop.cs` — the stall detector is correct; this story does not touch it.
- `AgentRun.Umbraco/Engine/StallDetector.cs` — same reason.
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` (Story 9.7's offloading shape) — the offloading contract is preserved exactly. The only addition is Task 0.5's cache-on-hit short-circuit, which produces a handle **indistinguishable** from the cache-miss case.
- `AgentRun.Umbraco.TestSite/.../scanner.md`, `analyser.md`, `reporter.md` — this story is engine state-machine territory, NOT prompt territory. **Hands off the agent prompts.**
- `_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md` — authoritative.
- `_bmad-output/planning-artifacts/v2-future-considerations.md` — Winston-owned.
- The Story 9.0 spec — upstream and authoritative.
- The Story 7.2 spec — upstream and authoritative; this story *extends* the retry path 7.2 built, it does not modify the 7.2 design.

## Acceptance Criteria

1. **Given** a step that has previously failed mid-tool-loop with the "retry-replay degenerate state" symptom (several completed `fetch_url` tool calls followed by a stall),
   **When** the user clicks retry via the existing Story 7.2 retry endpoint,
   **Then** the engine applies the chosen recovery design (see Dev Notes — Winston to lock) before re-entering `ToolLoop.RunAsync`,
   **And** the model does NOT see a degenerate "all my tools have already run" conversation state on the resumed first turn,
   **And** the retry completes successfully (the workflow proceeds to `write_file` and the step reaches `Complete`),
   **And** the existing Story 7.2 retry endpoint does not require any new public API surface — the recovery is internal to `StepExecutor` / `ConversationStore`.

2. **Given** a step that has previously failed with **zero** completed tool calls (e.g. failed before the first fetch even ran),
   **When** the user clicks retry,
   **Then** the existing Story 7.2 retry path runs unchanged — the new recovery logic must not regress the simple zero-tool-call retry case (this is the existing 7.2 baseline).

3. **Given** a step that has previously failed with completed tool calls **but the corresponding `.fetch-cache/{sha256}.html` files have been deleted** (e.g. the instance was partially cleaned up between attempts),
   **When** the user clicks retry,
   **Then** the recovery path does not crash — the recovery either re-runs the affected `fetch_url` calls (which will re-populate the cache via Story 9.7's offloading) or fails gracefully with a clear error pointing to the missing cached content. **No silent data corruption.**

4. **Given** the user retries the same step **multiple times in a row**, each retry hitting a different point in the tool loop,
   **When** the recovery logic runs each time,
   **Then** each retry is independent — the conversation history for the second retry reflects the second attempt's state, not a stale state from the first retry. The recovery primitive is **idempotent**.

5. **Given** a step has completed all its tool calls and was just about to call `write_file` when the upstream stall fired (this is the textbook degenerate case the bug is about),
   **When** the user clicks retry,
   **Then** the recovery design produces a conversation state in which the model's natural next action is `write_file` — verified by an end-to-end test using a fake `IChatClient` that produces the bug-symptom on the original turn but the correct action on the recovered turn.

6. **Given** the conversation log is corrupted or partially written (e.g. concurrent crash mid-write),
   **When** the user clicks retry,
   **Then** the recovery path **fails fast with a clear error** rather than crashing or silently producing wrong output. **(File consistency is a separate concern and not in scope for this story; the AC is graceful failure, not auto-repair.)**

7. **Given** `.fetch-cache/` files exist on disk but the conversation log does not reference them (dangling state),
   **When** the recovery path runs,
   **Then** the recovery path does NOT attempt to use the dangling cached files — they are treated as inert disk debris, not as conversation state. **Deny by default: anything not explicitly referenced by the conversation log is not part of the recovery's input.**

8. **Given** the model's first turn after the recovered retry produces text instead of a tool call (the symptom this story exists to fix),
   **When** Story 9.0's `StallDetector` evaluates that turn,
   **Then** detection still works as designed (this story does not regress 9.0). The expectation is that the recovery design **prevents the symptom from occurring**, not that it suppresses 9.0. If the symptom does occur post-recovery, 9.0 fires and the user sees the same error path as before — but the rate of post-recovery symptom occurrence must be **substantially lower** than the rate of pre-recovery symptom occurrence (measured by integration tests; see Tasks).

9. **Given** all backend changes,
   **When** `dotnet test AgentRun.Umbraco.slnx` runs,
   **Then** all existing tests pass and the new retry-replay recovery tests (~10 tests, see Tasks) pass.
   **(Note: always specify `.slnx`, never bare `dotnet test`.)**

10. **Given** the recovery design touches engine state-machine territory,
    **When** any new code path consumes `.fetch-cache/` paths, conversation log entries, or instance folder paths,
    **Then** **deny-by-default applies**: unrecognised, unspecified, or malformed inputs are rejected. The recovery primitive must not assume well-formed input from the conversation log or the file system.

## What NOT to Build

- **Do NOT modify Story 9.0's `StallDetector` or `ToolLoop.cs`.** The detector is correct. This story is the layer underneath.
- **Do NOT remove or modify Story 9.0's clean-boundary fix in `ConversationStore.TruncateLastAssistantEntryAsync` (line 136).** That fix prevents a different bug (orphaned tool_result → 400 from the provider). It is correct and stays. The new recovery primitive added by this story is **additional**, not a replacement.
- **Do NOT redesign the Story 7.2 retry endpoint shape.** The HTTP endpoint and chat-panel button stay as-is. This story changes what `StepExecutor` does internally on retry, not the user-facing API.
- **Do NOT add a new sandbox surface.** Re-use the existing `PathSandbox` for any disk reads of `.fetch-cache/` content. Story 9.7 already verified the dotted-directory path is sandbox-reachable.
- **Do NOT introduce auto-retry-with-nudge.** Story 9.0 explicitly rejected that pattern and locked the rationale. Recovery here is "user clicks retry, engine reshapes the resumed conversation to avoid the degenerate state" — it is NOT "engine retries internally and silently."
- **Do NOT modify scanner.md / analyser.md / reporter.md.** Engine territory only.
- **Do NOT add a configurable knob** for retry behaviour. The recovery design is one fixed strategy. Configurability is V2 if anyone asks.
- **Do NOT capture new test fixtures** — reuse the existing `caed201cbc5d4a9eb6a68f1ff6aafb06` instance.
- **Do NOT modify `StepExecutor.ExecuteStepAsync`.** Per Winston's 2026-04-08 design lock, the recovery work lives in the retry endpoint (`ExecutionEndpoints.cs`), not in the executor. `StepExecutor` stays shape-agnostic to retry semantics. If Amelia finds herself editing `StepExecutor`, stop and re-read this section.
- **Do NOT modify Story 9.7's offloading shape in `FetchUrlTool`.** Task 0.5's cache-on-hit addition must produce a handle **indistinguishable** from the cache-miss case (modulo timing fields). The 9.7 unit tests must continue to pass without modification.
- **Do NOT silently delete the original conversation file.** The wipe operation MUST first rename it to `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl` atomically. If the rename fails, surface a clear error — do not fall back to delete, do not proceed with a stale conversation.

## Failure & Edge Cases

(Some of these are also surfaced as ACs above; this section is the dev agent's checklist of failure modes the implementation MUST consider.)

- **Zero completed tool calls when retried** — must not regress the existing Story 7.2 baseline. (AC #2)
- **Cached `.fetch-cache/` files have been deleted between attempts** — recovery should not crash; either re-populate via re-fetch (Story 9.7's cache-on-miss) or fail gracefully with a clear error. (AC #3)
- **Multiple consecutive retries hitting different points in the tool loop** — each retry is independent; recovery primitive is idempotent. (AC #4)
- **Step had completed all tools and was about to call `write_file` when it stalled** — the textbook degenerate case; the primary AC for the recovery design. (AC #5)
- **Conversation log is corrupted or partially written** — recovery fails fast with a clear error; out of scope to auto-repair. (AC #6)
- **Cached files exist but conversation log does not reference them** — dangling state; deny-by-default treats them as inert debris. (AC #7)
- **The model's first post-recovery turn produces text** — Story 9.0's detector still fires; this story's success metric is *substantially lower* post-recovery symptom rate, not zero. (AC #8)
- **The user retries a step mid-stream** (e.g. while the workflow is somehow still running) — out of scope; instance concurrency locking is Story 10.1's surface. The recovery primitive assumes the step is in `Failed` or `Errored` status before retry begins. The retry endpoint already enforces this; verify it still does after this change.
- **The chosen design is Option 3 (restart from scratch) and `.fetch-cache/` does not exist** because Story 9.7 has not yet shipped or has been rolled back — the recovery degenerates to "re-fetch every URL from the network." Slow but correct. **The dependency on 9.7 is hard, not soft** — this story's spec assumes 9.7 is on `main`.
- **The chosen design is Option 1 (trim history) and there is no clean restart point** in the conversation (e.g. the conversation is one giant tool_call/tool_result chain with no user message in the middle) — the recovery falls back to Option 3 behaviour (full reset) or fails fast. Winston to decide during the design-lock pass.
- **`fetch_url`'s cache lookup is not cache-on-hit but only cache-on-miss** — if Story 9.7's offloading writes to `.fetch-cache/` but does NOT short-circuit a re-issued `fetch_url` call from the cache, then Option 3 will re-fetch from the network. This is functionally correct but has a perf cost. **Flag for Winston to confirm 9.7's cache lookup behaviour** before locking Option 3.
- **Deny-by-default statement (REQUIRED per project rules):** any conversation-log entry, `.fetch-cache/` path, or instance-folder content that does not match an explicitly recognised shape MUST be rejected, not silently consumed. The recovery primitive does not "best-effort" parse malformed or unfamiliar input.

## Tasks / Subtasks

_Numbered tasks; the dev agent works them in order. Each task has explicit subtasks with deny-by-default behaviour where applicable. Do NOT skip tasks. Do NOT collapse tasks into one PR commit — each task should be a clean logical unit._

- [ ] **Task 0: Pre-implementation gate.** Confirm Winston has ticked the Pre-Implementation Architect Review checkbox at the bottom of this spec. **Locked design: Option 3 (restart from scratch, leveraging 9.7's `.fetch-cache/`).** Do not start coding until this gate is passed AND Story 9.7 is `done` on `main`.

- [ ] **Task 0.5: Add cache-on-hit short-circuit to `FetchUrlTool`** (Option 3 prerequisite — added by Winston 2026-04-08; NOT in Story 9.7 because 9.7 was already approved and Winston refused to reopen it). The cache-on-hit handle MUST be indistinguishable from the cache-miss handle for the same URL (modulo timing fields).
  - [ ] 0.5.1 In `FetchUrlTool.ExecuteAsync`, **after** SSRF validation and **before** the `HttpClient.GetAsync` call, check if `Path.Combine(context.InstanceFolderPath, ".fetch-cache", $"{ComputeUrlHash(urlString)}.html")` exists. (Use the same hash function added by Story 9.7's Task 1.1.)
  - [ ] 0.5.2 If the file exists: read its metadata via `FileInfo` (`.Length` for `size_bytes`); detect truncation by checking for the `[Response truncated at` marker in the file's tail bytes; build a handle with `status: 200`, `content_type: "text/html"` (default — original `Content-Type` is **not** preserved across cache reuse; document this as a known limitation in Dev Notes), `saved_to: ".fetch-cache/{hash}.html"`, `size_bytes: fileInfo.Length`, `truncated: <marker check result>`; return the handle JSON immediately. **No HTTP request is made.**
  - [ ] 0.5.3 If the file does not exist: fall through to the existing 9.7 HTTP request path unchanged. The cache-miss path is the existing 9.7 implementation — do NOT modify it.
  - [ ] 0.5.4 Unit-test cache-on-hit: use a test double for `IHttpClientFactory` that **fails the test if `GetAsync` is called**. Pre-populate the cache file. Invoke `ExecuteAsync`. Assert the test double was never called and the returned handle is well-formed.
  - [ ] 0.5.5 Unit-test cache-on-miss (regression): the existing 9.7 cache-miss path still works exactly as before. Reuse one of 9.7's existing tests as the regression baseline if possible.
  - [ ] 0.5.6 Unit-test handle indistinguishability: invoke `ExecuteAsync` for the same URL twice. The first call is cache-miss (writes the file), the second is cache-on-hit (reads the file). Assert the two returned handles are equal modulo timing fields (`url`, `status`, `content_type`, `size_bytes`, `saved_to`, `truncated` all match).

- [ ] **Task 1: Add the `WipeHistoryAsync` primitive on `IConversationStore` / `ConversationStore`** (locked: Option 3):
  - [ ] 1.1 Add the interface method `Task WipeHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken)` to `IConversationStore`.
  - [ ] 1.2 Implement on `ConversationStore`. The implementation **MUST FIRST** atomically rename the existing conversation file to `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl` (with hyphenated timestamps for filesystem portability — colons replaced with hyphens, e.g. `conversation-scanner.failed-2026-04-08T19-47-23Z.jsonl`) **BEFORE** the new fresh conversation begins. Use the same atomic .tmp + move pattern as the other `ConversationStore` operations (or `File.Move(source, dest, overwrite: false)` if no temp staging is needed for a rename).
  - [ ] 1.3 Unit-test the primitive in isolation: missing file (no-op or graceful error — pick the semantic and document); single-entry file (rename succeeds, original is gone, archive exists with the right filename pattern); multi-entry file (same); rename collision (a `.failed-{timestamp}.jsonl` with the same timestamp already exists — surface a clear error, do NOT silently overwrite); rename failure due to permission (surface a clear `IOException` or wrapped `InvalidOperationException`, do NOT swallow).
  - [ ] 1.4 **Failure-mode contract:** the wipe operation MUST surface a clear error on rename failure. **It MUST NOT silently delete the original file as a fallback.** It MUST NOT proceed with a stale conversation. The caller (the retry endpoint, Task 2) treats a wipe failure as a hard failure of the retry attempt and surfaces it to the user.

- [ ] **Task 2: Invoke the recovery primitive from the retry endpoint** (`ExecutionEndpoints.cs`) — **NOT from `StepExecutor`**. Per Winston's 2026-04-08 design lock, `StepExecutor.ExecuteStepAsync` is **NOT modified** by this story. `StepExecutor` stays shape-agnostic to retry semantics.
  - [ ] 2.1 Locate the existing retry endpoint handler in `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — the same call site that currently invokes `IConversationStore.TruncateLastAssistantEntryAsync`.
  - [ ] 2.2 **Replace** (or sit alongside, depending on the existing handler shape — read it first) the `TruncateLastAssistantEntryAsync` call with a `WipeHistoryAsync` call. The wipe happens **before** the endpoint dispatches the step execution.
  - [ ] 2.3 Log the recovery action at `LogLevel.Information` with structured fields `WorkflowAlias`, `InstanceId`, `StepId`, `RecoveryStrategy: "wipe-and-restart"`, and `ArchivedTo` (the new `.failed-{timestamp}.jsonl` filename).
  - [ ] 2.4 If `WipeHistoryAsync` throws (rename failure per Subtask 1.4), the endpoint returns an HTTP error response with a clear message — do **not** dispatch the step execution against a stale conversation.
  - [ ] 2.5 **Verify (do NOT change) the existing endpoint precondition:** the retry endpoint already enforces that the step is in `Failed` or `Errored` status before retry begins. Confirm this still holds. If it does not, surface to Adam — do not silently change the precondition.

- [ ] **Task 4: Unit tests for the recovery primitive** (~3–4 tests):
  - File-shape tests: empty conversation, conversation with only user messages, conversation with only assistant messages, conversation with the failing-instance shape (extracted from `caed201cbc5d4a9eb6a68f1ff6aafb06`).
  - Atomicity test: ensure the .tmp + move pattern is used and a mid-write crash does not leave a corrupted file.
  - Idempotency test: invoking the primitive twice on the same conversation produces the same result.

- [ ] **Task 5: Integration tests using a fake `IChatClient`** (~3–4 tests):
  - Reproduce the degenerate-state symptom by feeding the fake `IChatClient` a captured conversation tail from `caed201cbc5d4a9eb6a68f1ff6aafb06` and asserting that without recovery, the fake client produces text-only output and `StallDetector` fires.
  - Apply the recovery primitive and assert that the fake client (now seeing the post-recovery shape) produces a `write_file` tool call instead of text.
  - Vary the symptom: model produces text, model produces an empty turn, model produces a duplicate `fetch_url` call (which is a different bug class — the recovery should not regress correctness here).
  - Pattern: follow the same fake-LLM pattern Story 9.0's stall-detector tests use.

- [ ] **Task 6: End-to-end-shaped tests against captured fixtures** (~2–3 tests):
  - Load the full `conversation-scanner.jsonl` from `caed201cbc5d4a9eb6a68f1ff6aafb06`, run it through `StepExecutor` with a fake `IChatClient`, simulate a retry, assert the recovery path runs and the workflow completes.
  - Test the dangling-cache case (AC #3): pre-populate `.fetch-cache/` with hashed files, then delete some of them, then trigger retry; assert the recovery either re-fetches or fails gracefully.
  - Test the multiple-consecutive-retries case (AC #4): retry, retry again, retry a third time; assert each retry is independent and the conversation history reflects the most recent attempt.

- [ ] **Task 7: Run the test suite:** `dotnet test AgentRun.Umbraco.slnx`. Expect green. **(Do NOT use bare `dotnet test`.)**

- [ ] **Task 8: Manual E2E gate (mandatory in DoD):**
  - 8.1: In the TestSite, manually trigger a step failure mid-tool-loop. Easiest reproduction: kill the dev server during a multi-fetch step in the Content Quality Audit workflow, restart, then click retry in the chat panel.
  - 8.2: Confirm the retry completes successfully — the workflow proceeds to `write_file` and the step reaches `Complete` status.
  - 8.3: Confirm the retry does NOT produce the degenerate stall (no second `StallDetectedException` in the logs).
  - 8.4: Inspect the conversation-scanner.jsonl after the retry — the recovery action should be visible (either the file was wiped, trimmed, or augmented with a system-reminder, depending on the locked design).
  - 8.5: Repeat the test 5 times to gather a rough success-rate signal. **At least 4 of 5 retries should succeed** (the deny-by-default acceptance bar; this is also an input to the Beta-Blocker Escalation Criteria above).

- [ ] **Task 9: Production smoke test (mandatory in DoD):**
  - Install the unmerged branch into a fresh TestSite.
  - Run Content Quality Audit against a real-world 5-URL batch (e.g. the same batch used for 9.7's smoke test, including a deliberately problematic URL).
  - Manually fail the step partway through (kill the dev server mid-fetch).
  - Restart the dev server.
  - Click retry.
  - Confirm the workflow recovers and the final `audit-report.md` artifact is produced.
  - Capture the conversation-scanner.jsonl and the recovery log entry as evidence in the Dev Agent Record.

## Dev Notes

### Three design alternatives (Winston to lock one before implementation begins)

These are the three viable shapes for the recovery primitive. They are not equally likely to succeed; Bob's read of the source code (see "Bob's read" below each option) flags the trade-offs. **The architect (Winston) is responsible for the final lock.** The dev agent (Amelia) does not start coding until Winston has ticked the Pre-Implementation Architect Review checkbox at the bottom of this spec and added a one-line note recording the locked option.

#### Option 1 — Trim the replayed history at the last clean restart point

**Idea:** When retrying, walk backwards through the JSONL conversation log to find the most recent "clean restart point" — defined as the last `user` role entry (or the start of the conversation if there is no user message) — and replay only the history up to and including that point. Everything after the restart point (assistant tool calls, tool results, prior model output) is discarded for the purposes of the resumed conversation. The on-disk JSONL is rewritten via the same atomic .tmp + move pattern used by the existing `TruncateLastAssistantEntryAsync`.

**Pros:**
- Conceptually closest to the existing 7.2 retry behaviour (truncate-and-replay) — the smallest mental model change.
- Preserves the user's original intent (their messages) while discarding the model's prior failed attempt.
- The cached `.fetch-cache/` files from Story 9.7 remain available; the model can re-discover them via `read_file` if it issues `fetch_url` for the same URLs and Story 9.7's cache-on-hit returns the existing handles.

**Cons:**
- Requires identifying a "clean restart point" which **may not always exist**. If the conversation is one long chain of tool calls with no intervening user message (which is the normal scanner shape after the user pastes the URL list), there is exactly one user message at the start, and Option 1 collapses to "wipe everything except the system prompt and the first user message" — which is functionally identical to Option 3.
- Risks breaking tool_use/tool_result pairing if the trim point is not chosen carefully. The existing `TruncateLastAssistantEntryAsync` is intentionally conservative for exactly this reason. Option 1 needs the same care.
- More complex to test than Option 3 because the test surface includes "find the right boundary in a JSONL file."

**Bob's read of the source:** [`StepExecutor.ConvertHistoryToMessages`](../../AgentRun.Umbraco/Engine/StepExecutor.cs) at lines 246–304 already groups consecutive tool-call assistant entries into one `ChatMessage` and pairs tool results with their tool_call_id. Trimming arbitrarily into the middle of a tool_call/tool_result chain would break this pairing and the next provider call would 400 (per the comment at [`ConversationStore.cs:127`](../../AgentRun.Umbraco/Instances/ConversationStore.cs#L127)). So Option 1 in practice means "trim back to a user message," which on the scanner step's conversation shape means "trim back to the very first user message" — i.e. effectively Option 3 with extra book-keeping. **Bob's view: Option 1 is dominated by Option 3 on this codebase.**

#### Option 2 — Inject a system reminder at retry time

**Idea:** Replay the full conversation history unchanged (no truncation, no wipe), but immediately before re-entering `ToolLoop.RunAsync`, append a synthetic system or user message to the in-memory `messages` list explaining the resumption context to the model: e.g. `"You previously fetched these N URLs successfully. Your remaining work is to write the results to scan-results.md. Begin by calling write_file."` The on-disk JSONL is not modified — the synthetic reminder lives only in the in-memory message list for this resumed turn.

**Pros:**
- Smallest engine surgery of the three. No new `IConversationStore` primitive. No conversation file rewrite. Just an in-memory message-list transform inside `StepExecutor.ExecuteStepAsync` immediately after `ConvertHistoryToMessages` returns.
- Reversible by definition — if the reminder turns out to be unhelpful, removing it is a one-line revert.
- Preserves the full conversation history for debugging/forensics. The on-disk JSONL is unchanged.

**Cons:**
- **Fragile to model behaviour.** The recovery depends on the model heeding a hint. Sonnet 4.6's known non-determinism (see `memory/project_sonnet_46_scanner_nondeterminism.md`) is exactly the kind of behaviour that ignores explicit hints when the conversation tail says otherwise. There is no guarantee the reminder is followed.
- Prompt engineering on top of the engine, which is exactly the layering Story 9.0 explicitly rejected ("retry-with-nudge").
- The reminder text needs to be authored, tested, and tuned — and tuning a prompt-shaped recovery is the same prompt-iteration loop that Story 9.1c proved has hit its ceiling for this failure mode.

**Bob's read of the source:** Implementing this is a 5-line change in `StepExecutor.ExecuteStepAsync` after the existing `messages.AddRange(ConvertHistoryToMessages(history))` call. Easy to write, easy to revert. **But the existing project memory entry on Sonnet 4.6 non-determinism explicitly warns that prompt-only mitigations have a ceiling in this exact failure family.** This option treats a prompt-engineering approach as a fix for a non-prompt-fixable bug. **Bob's view: Option 2 is the cheapest to ship and the most likely to fail in a way that produces "we shipped a recovery and it doesn't recover" telemetry.**

#### Option 3 — Restart the step from scratch, leveraging Story 9.7's `.fetch-cache/`

**Idea:** When retrying, **wipe the conversation log** (atomic delete via the new `WipeHistoryAsync` primitive in `IConversationStore`) and re-enter `StepExecutor.ExecuteStepAsync` with a fresh `messages` list containing only the system prompt. The agent sees the original step prompt, re-issues `fetch_url` calls for the same URLs, and Story 9.7's offloading writes responses to `.fetch-cache/{sha256(url)}.html` keyed on URL — so on the retry, **assuming `fetch_url` is implemented as cache-on-hit** (which Bob will flag for Winston to confirm before locking — see Open Questions), the second call returns the existing handle in milliseconds without re-hitting the network.

**Pros:**
- **No degenerate state can exist.** The conversation is genuinely fresh. The model has never seen the failed prior attempt. There is no shape-driven failure mode left to trigger.
- **Robust against model non-determinism.** The recovery does not depend on the model heeding any hint. It depends on the model re-issuing fetch calls it would naturally re-issue from a fresh start, and on the cache returning the same handles it returned the first time.
- **Composes naturally with Story 9.7.** The cache layer does double duty: original purpose was eliminating context bloat; now it also serves as the substrate that makes retry-restart cheap.
- **Simplest to test.** "Wipe + restart" is a single semantic with a single outcome to assert.

**Cons:**
- **Discards conversation history.** Forensic debugging of the original failure becomes harder. Mitigation: archive the original conversation file to `conversation-{stepId}.failed-{timestamp}.jsonl` instead of deleting it outright. Adds one line of code; preserves debug trail.
- **Assumes Story 9.7's offloading is cache-on-hit, not cache-on-miss-only.** **Open question for Winston.** If 9.7 only writes to the cache (and `fetch_url` always re-fetches from network), Option 3 still works but each retry costs N HTTP requests instead of N cache lookups. Functionally correct, perf-degraded.
- **Token cost on retry.** Re-issuing N tool calls means N more rounds of tool-call/tool-result tokens in the resumed conversation. Small in absolute terms (after 9.7, each fetch is ~1 KB instead of ~400 KB) but non-zero.
- **Hard dependency on 9.7 being on `main`.** Story 9.7 is `ready-for-dev` at the time of writing this spec. It must be `done` before this story's implementation can begin.

**Bob's read of the source:** This is the only option that **structurally** prevents the bug rather than detecting/papering over it. The new primitive (`WipeHistoryAsync`) is a 10-line method on `ConversationStore`. The retry-mode branch in `StepExecutor` is a 5-line addition. The total surgery is small and the semantic is clear: "retry means restart, not resume." **Bob's recommendation: Option 3, conditional on Winston confirming Story 9.7's cache lookup is cache-on-hit.**

### Bob's recommendation summary

**Recommended: Option 3 (restart from scratch, leveraging 9.7's cache)** because it is the only option that *prevents* the bug rather than *detecting* or *papering over* it, and the dependency on 9.7's cache lookup being cache-on-hit is checkable in one read of the 9.7 spec.

**Fall-back: Option 1**, but only if Winston decides Option 3 is unacceptable for forensic reasons. Option 1 with the "archive instead of delete" mitigation is functionally close to Option 3 but with more bookkeeping.

**Not recommended: Option 2.** Prompt-engineered recovery for a non-prompt-fixable bug. Cheap to ship, likely to fail, hard to debug when it does.

**Final lock is Winston's call. Amelia does not start until the gate is passed.**

### Open Questions — RESOLVED by Winston 2026-04-08

All four resolved at the design-lock pass. Recorded here for the paper trail.

1. **Story 9.7's cache lookup behaviour:** **ANSWERED.** 9.7 as currently scoped is cache-on-miss / write-through only — there is no cache-on-hit short-circuit. Winston refused to reopen 9.7 (it had already been approved). **The cache-on-hit short-circuit is added to 10.6 as a new prerequisite Task 0.5** in `FetchUrlTool` (NOT a 9.7 modification). The cache-on-hit handle MUST be indistinguishable from the cache-miss handle for the same URL (modulo timing fields). Six subtasks specified in Task 0.5. Known limitation: original `Content-Type` is not preserved across cache reuse — defaults to `text/html`.
2. **Forensic preservation:** **ANSWERED — archive, not delete.** Filename pattern: `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl` with hyphenated timestamps for filesystem portability (e.g. `2026-04-08T19-47-23Z` — colons replaced with hyphens). Use the same atomic .tmp + move pattern as other `ConversationStore` operations. **If the rename fails, surface a clear error — do NOT silently delete or proceed with a stale conversation.** Specified in Task 1.2 and the failure-mode contract in Task 1.4.
3. **Retry-mode signalling:** **ANSWERED — recovery happens in the retry endpoint (`ExecutionEndpoints.cs`), NOT inside `StepExecutor.ExecuteStepAsync`.** `StepExecutor` stays shape-agnostic to retry semantics. The new recovery primitive sits at the same call site as the existing `TruncateLastAssistantEntryAsync` call in the retry endpoint. Task 2 has been reframed accordingly. There is no `RetryMode` parameter, no `StepExecutionContext` flag, and no `StepExecutor` modification.
4. **Beta escalation telemetry:** **ANSWERED — deferred to Story 11.3 (Token Usage Logging)**, which will establish the engine-wide observability convention. 10.6 uses Task 8.5's manual-E2E success-rate gate (4 of 5 retries succeed) as the measurable bar. This is a forward reference, not a dependency — 10.6 ships without structured `recovery.outcome` logging; the manual gate is sufficient for the fast-follower bar.

### References

- Authorisation: [Sprint Change Proposal 2026-04-08 — Story 9-1b Rescope](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope.md)
- Broader context: [Sprint Change Proposal 2026-04-08 — Story 9-1c Pause](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md)
- Sibling architectural finding (related but separate bug): [9-1c Architectural Finding: fetch_url Context Bloat](../planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md)
- Upstream dependency: [Story 9.7 — Tool Result Offloading for fetch_url](./9-7-tool-result-offloading-fetch-url.md)
- Cooperating story (the upstream stall *detector*): [Story 9.0 — ToolLoop Stall Recovery](./9-0-toolloop-stall-recovery.md)
- Extended story (the existing retry path): [Story 7.2 — Step Retry with Context Management](./7-2-step-retry-with-context-management.md)
- Original edge case bullet: [epics.md — Story 9.1b Failure & Edge Cases section, "Retry-replay degeneration" bullet](../planning-artifacts/epics.md)
- Failing instance evidence: `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/caed201cbc5d4a9eb6a68f1ff6aafb06/conversation-scanner.jsonl`
- Engine source: [`AgentRun.Umbraco/Engine/StepExecutor.cs`](../../AgentRun.Umbraco/Engine/StepExecutor.cs), [`AgentRun.Umbraco/Engine/ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs), [`AgentRun.Umbraco/Instances/ConversationStore.cs`](../../AgentRun.Umbraco/Instances/ConversationStore.cs), [`AgentRun.Umbraco/Instances/InstanceManager.cs`](../../AgentRun.Umbraco/Instances/InstanceManager.cs)
- Project memory: `memory/project_sonnet_46_scanner_nondeterminism.md` — relevant because Option 2 is fragile against this exact non-determinism family.

## Definition of Done

- [ ] All Acceptance Criteria pass (verified by automated tests where possible, manual gate where not)
- [ ] All Tasks ticked
- [ ] `dotnet test AgentRun.Umbraco.slnx` is green (always specify `.slnx`, never bare `dotnet test`)
- [ ] Manual E2E gate passed (Task 8) — at least 4 of 5 manual retries succeed
- [ ] Production smoke test passed (Task 9) — fresh TestSite, real-world 5-URL batch, manually-induced failure, retry recovers, final artifact produced
- [ ] Code review complete via `bmad-code-review`
- [ ] Story status updated to `done` in `sprint-status.yaml`
- [ ] If post-implementation any of the Beta-Blocker Escalation Criteria are tripped, this story is escalated via `bmad-correct-course` to be moved into Epic 9 and re-prioritised before private beta ships

## Dev Agent Record

_To be filled in by Amelia during implementation._

- **Locked design alternative:** **Locked: Option 3 — Winston, 2026-04-08. Cache-on-hit added as 10.6 Task 0.5 (not 9.7 scope creep). Archive original conversation file as `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl`. Recovery work in retry endpoint (`ExecutionEndpoints.cs`), not `StepExecutor`. Telemetry deferred to Story 11.3.**
- **Implementation notes:** _(Amelia)_
- **Test results:** _(Amelia — paste `dotnet test AgentRun.Umbraco.slnx` output)_
- **Manual E2E results:** _(Amelia — 5 retry attempts, success/failure for each, conversation log evidence)_
- **Production smoke test results:** _(Amelia — fresh TestSite walkthrough)_
- **Beta-Blocker Escalation Criteria check:** _(Amelia — rates measured during testing; flag any that tripped)_

## Pre-Implementation Architect Review

> **GATE — Amelia does NOT start implementation until this checkbox is ticked by Winston.**

- [x] Architect (Winston) has reviewed the three design alternatives in the Dev Notes section, answered the four Open Questions, and locked one design alternative before any code is written.

**Locked 2026-04-08 — Winston: Option 3 (restart from scratch, leveraging 9.7's `.fetch-cache/`).** Cache-on-hit short-circuit added as a new 10.6 prerequisite Task 0.5 in `FetchUrlTool` (NOT a 9.7 modification — Winston refused to reopen 9.7). Original conversation file is archived to `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl` (hyphenated timestamps), not deleted. Recovery work lives in `ExecutionEndpoints.cs` retry endpoint, NOT in `StepExecutor` — `StepExecutor` stays shape-agnostic to retry semantics. Telemetry deferred to Story 11.3. New beta-blocker escalation tripwire added: any retry-replay degeneration during Story 9.7's manual E2E gate triggers immediate escalation. All six required spec updates have been applied. **Amelia is cleared to start once Story 9.7 is `done` on `main`.**

**Triggered by:** the design choice between Option 1 (trim replayed history), Option 2 (inject system reminder), and Option 3 (restart from scratch leveraging 9.7's cache) is non-trivial and the architect should make the call. Bob's recommendation is Option 3 conditional on Winston confirming 9.7's cache lookup is cache-on-hit, but the lock is Winston's.

**Process:**

1. Bob delivers this spec to Adam.
2. Adam hands the spec to Winston for review (either as a batch with Story 9.7 if Adam decides to escalate this story to beta-blocker status, or stand-alone after 9.7 is queued).
3. Winston reads the three design alternatives, answers the four Open Questions in the Dev Notes section, and ticks the checkbox above with a one-line note recording the locked option (e.g. `Locked: Option 3 — Winston, 2026-04-XX. Cache lookup confirmed cache-on-hit; archive original conversation file as .failed-{timestamp}.jsonl.`)
4. Once the checkbox is ticked, the spec is handed to Amelia and implementation begins.
