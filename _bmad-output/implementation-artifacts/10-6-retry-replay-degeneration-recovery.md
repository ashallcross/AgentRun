# Story 10.6: Retry-Replay Degeneration Recovery

Status: done

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
| `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs` | Extend with the Task 2.6 `CurrentStepIndex` reconciliation test on `RetryInstance`. |
| `AgentRun.Umbraco.Tests/Services/ConversationRecorderTests.cs` (NEW or extend existing) | Task 4.5 completion-boundary regression test — pins the load-bearing 10.9 + 10.6 invariant that the recorder never writes partial chunks. |

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

## Research & Integration Checklist

(Epic 9 retro process improvement #1 — every Epic 10+ story spec lists Umbraco APIs touched, prior-art references consulted, and real-world content scenarios to test, so the dev agent does not rediscover problems already solved upstream.)

**Umbraco APIs touched:** None directly — this story is engine-internal (`ConversationStore`, `ExecutionEndpoints` minimally, `FetchUrlTool` Task 0.5 only). No `IUmbracoContextFactory`, no `IContentService`, no notification handlers. If you find yourself reaching for an Umbraco API, stop — you are out of scope.

**File-system primitives in play:**
- `File.Move(source, dest, overwrite: false)` — primary atomic-rename surface for `WipeHistoryAsync`. On Windows + macOS + Linux, atomic on the same volume; not atomic across volumes (instance folder is single-volume by design).
- `Path.Combine` for `.fetch-cache/{hash}.html` lookup — must go through `PathSandbox` (re-use the existing one; do NOT add a new sandbox surface).
- ISO-8601 timestamp formatting with hyphenated seconds (`2026-04-15T19-47-23Z`) — colons are illegal in NTFS filenames; hyphens are portable across all three platforms. Verify the format string matches Task 1.2's specification exactly before generating filenames.

**Prior art to consult before touching `ConversationStore`:**
- [`ConversationStore.TruncateLastAssistantEntryAsync`](../../AgentRun.Umbraco/Instances/ConversationStore.cs#L100) and especially its **clean-boundary no-op fix at line 136** (Story 9.0). The new `WipeHistoryAsync` primitive is **additional**, not a replacement. Read the existing primitive's atomic .tmp + move pattern and reuse it verbatim — do not invent a new file-IO pattern.
- [`ConversationStore.AppendAsync`](../../AgentRun.Umbraco/Instances/ConversationStore.cs) for the same .tmp + move pattern in the write direction.
- Story 7.2 spec — the existing retry endpoint shape that this story extends.

**Prior art to consult before touching `FetchUrlTool` (Task 0.5):**
- Story 9.7's offloading shape — the cache-on-hit handle MUST be **indistinguishable** from the cache-miss handle. Read 9.7's existing tests; use them as the regression baseline.
- The hash function added by Story 9.7's Task 1.1 — re-use, do not re-implement.
- `memory/feedback_research_before_fix.md` — search Umbraco docs / forums / GitHub before guessing on any unfamiliar API surface.

**Real-world content scenarios to exercise (Task 8 and Task 9):**
- Content Quality Audit workflow against a 5-URL batch with at least one deliberately-failing URL (e.g. a Wikipedia page that previously triggered the 403 in `caed201cbc5d4a9eb6a68f1ff6aafb06`). This is the textbook reproduction.
- Mixed-success batch where 3 of 5 URLs succeed before the failure. The post-recovery resumption should re-fetch all 5 (cache-hit on the 3 successful, cache-miss on the 2 that failed).
- Manual mid-flight kill (kill the dev server during a fetch step) — verifies the wipe-and-restart works without a graceful shutdown signal.
- Repeat the recovery 5 times (Task 8.5) to gather a rough success-rate signal against the manual-E2E acceptance bar (≥ 4 of 5 succeed).

**LLM provider non-determinism:**
- `memory/project_sonnet_46_scanner_nondeterminism.md` — Sonnet 4.6 is the primary test target. Its non-determinism is exactly why Option 2 (system-reminder injection) was rejected. The locked Option 3 design is **structural**, so it should be robust to provider non-determinism — but the manual E2E gate (Task 8) is where reality gets to bite.

**Logging conventions:**
- Structured fields per `memory/feedback_no_ai_slop.md` and existing engine logging — `WorkflowAlias`, `InstanceId`, `StepId`, `RecoveryStrategy`, `ArchivedTo` (per Task 2.3). Match the casing of existing log scopes; do not invent new field names.

## Tasks / Subtasks

_Numbered tasks; the dev agent works them in order. Each task has explicit subtasks with deny-by-default behaviour where applicable. Do NOT skip tasks. Do NOT collapse tasks into one PR commit — each task should be a clean logical unit._

- [x] **Task 0: Pre-implementation gate.** Confirm Winston has ticked the Pre-Implementation Architect Review checkbox at the bottom of this spec. **Locked design: Option 3 (restart from scratch, leveraging 9.7's `.fetch-cache/`).** Do not start coding until this gate is passed AND Story 9.7 is `done` on `main`.

- [x] **Task 0.5: Add cache-on-hit short-circuit to `FetchUrlTool`** (Option 3 prerequisite — added by Winston 2026-04-08; NOT in Story 9.7 because 9.7 was already approved and Winston refused to reopen it). The cache-on-hit handle MUST be indistinguishable from the cache-miss handle for the same URL (modulo timing fields).
  - [x] 0.5.1 In `FetchUrlTool.ExecuteAsync`, **after** SSRF validation and **before** the `HttpClient.GetAsync` call, check if `Path.Combine(context.InstanceFolderPath, ".fetch-cache", $"{ComputeUrlHash(urlString)}.html")` exists. (Use the same hash function added by Story 9.7's Task 1.1.)
  - [x] 0.5.2 If the file exists: read its metadata via `FileInfo` (`.Length` for `size_bytes`); detect truncation by checking for the `[Response truncated at` marker in the file's tail bytes; build a handle with `status: 200`, `content_type: "text/html"` (default — original `Content-Type` is **not** preserved across cache reuse; document this as a known limitation in Dev Notes), `saved_to: ".fetch-cache/{hash}.html"`, `size_bytes: fileInfo.Length`, `truncated: <marker check result>`; return the handle JSON immediately. **No HTTP request is made.**
  - [x] 0.5.3 If the file does not exist: fall through to the existing 9.7 HTTP request path unchanged. The cache-miss path is the existing 9.7 implementation — do NOT modify it.
  - [x] 0.5.4 Unit-test cache-on-hit: use a test double for `IHttpClientFactory` that **fails the test if `GetAsync` is called**. Pre-populate the cache file. Invoke `ExecuteAsync`. Assert the test double was never called and the returned handle is well-formed.
  - [x] 0.5.5 Unit-test cache-on-miss (regression): the existing 9.7 cache-miss path still works exactly as before. Reuse one of 9.7's existing tests as the regression baseline if possible.
  - [x] 0.5.6 Unit-test handle indistinguishability: invoke `ExecuteAsync` for the same URL twice. The first call is cache-miss (writes the file), the second is cache-on-hit (reads the file). Assert the two returned handles are equal modulo timing fields (`url`, `status`, `content_type`, `size_bytes`, `saved_to`, `truncated` all match).

- [x] **Task 1: Add the `WipeHistoryAsync` primitive on `IConversationStore` / `ConversationStore`** (locked: Option 3):
  - [x] 1.1 Add the interface method `Task WipeHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken)` to `IConversationStore`.
  - [x] 1.2 Implement on `ConversationStore`. The implementation **MUST FIRST** atomically rename the existing conversation file to `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl` (with hyphenated timestamps for filesystem portability — colons replaced with hyphens, e.g. `conversation-scanner.failed-2026-04-08T19-47-23Z.jsonl`) **BEFORE** the new fresh conversation begins. Use the same atomic .tmp + move pattern as the other `ConversationStore` operations (or `File.Move(source, dest, overwrite: false)` if no temp staging is needed for a rename).
  - [x] 1.3 Unit-test the primitive in isolation: missing file (no-op or graceful error — pick the semantic and document); single-entry file (rename succeeds, original is gone, archive exists with the right filename pattern); multi-entry file (same); rename collision (a `.failed-{timestamp}.jsonl` with the same timestamp already exists — surface a clear error, do NOT silently overwrite); rename failure due to permission (surface a clear `IOException` or wrapped `InvalidOperationException`, do NOT swallow).
  - [x] 1.4 **Failure-mode contract:** the wipe operation MUST surface a clear error on rename failure. **It MUST NOT silently delete the original file as a fallback.** It MUST NOT proceed with a stale conversation. The caller (the retry endpoint, Task 2) treats a wipe failure as a hard failure of the retry attempt and surfaces it to the user.

- [x] **Task 2: Invoke the recovery primitive from the retry endpoint** (`ExecutionEndpoints.cs`) — **NOT from `StepExecutor`**. Per Winston's 2026-04-08 design lock, `StepExecutor.ExecuteStepAsync` is **NOT modified** by this story. `StepExecutor` stays shape-agnostic to retry semantics.
  - [x] 2.1 Locate the existing retry endpoint handler in `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — the same call site that currently invokes `IConversationStore.TruncateLastAssistantEntryAsync`.
  - [x] 2.2 **Replace** (or sit alongside, depending on the existing handler shape — read it first) the `TruncateLastAssistantEntryAsync` call with a `WipeHistoryAsync` call. The wipe happens **before** the endpoint dispatches the step execution.
  - [x] 2.3 Log the recovery action at `LogLevel.Information` with structured fields `WorkflowAlias`, `InstanceId`, `StepId`, `RecoveryStrategy: "wipe-and-restart"`, and `ArchivedTo` (the new `.failed-{timestamp}.jsonl` filename).
  - [x] 2.4 If `WipeHistoryAsync` throws (rename failure per Subtask 1.4), the endpoint returns an HTTP error response with a clear message — do **not** dispatch the step execution against a stale conversation.
  - [x] 2.5 **Verify (do NOT change) the existing endpoint precondition:** the retry endpoint already enforces that the step is in `Failed` or `Errored` status before retry begins. Confirm this still holds. If it does not, surface to Adam — do not silently change the precondition.
  - [x] 2.6 **Reconcile `CurrentStepIndex` with `FindIndex(Active/Error)` on Retry** (cross-story pickup from Story 10.9 review — deferred-work.md, 2026-04-14). Pre-existing pattern across the Failed and Interrupted retry branches: both use step-discovery via `FindIndex` rather than `CurrentStepIndex` as the authority, and never write back the discovered index when they disagree. While this story is in the retry path, fix the drift: after `FindIndex` locates the step to resume, if the discovered index differs from `instance.CurrentStepIndex`, update `CurrentStepIndex` to match the discovered index **before** dispatching execution. Add a focused unit test on `RetryInstance` that asserts `CurrentStepIndex` equals the resumed step's index after retry. **Scope discipline:** this is a one-place reconciliation in the retry endpoint, not a refactor of `FindIndex` or the Failed/Interrupted branches' discovery logic.

- [x] **Task 4: Unit tests for the recovery primitive** (~3–4 tests):
  - File-shape tests: empty conversation, conversation with only user messages, conversation with only assistant messages, conversation with the failing-instance shape (extracted from `caed201cbc5d4a9eb6a68f1ff6aafb06`).
  - Atomicity test: ensure the .tmp + move pattern is used and a mid-write crash does not leave a corrupted file.
  - Idempotency test: invoking the primitive twice on the same conversation produces the same result.

- [x] **Task 4.5: Pin the `ConversationRecorder` completion-boundary invariant** (cross-story pickup from Story 10.9 review — deferred-work.md, 2026-04-14). Story 10.9's Interrupted-retry path skips `TruncateLastAssistantEntryAsync` on the load-bearing assumption that `ConversationRecorder` writes only on completion boundaries (not deltas), so no partial assistant message is ever persisted. If a future change starts flushing partial content, Interrupted retries (and 10.6's wipe-and-restart path on retry-after-Interrupted) silently break by appending a duplicate completed assistant entry for the same prompt. **No regression test pins this today.** Add ONE of:
  - [x] 4.5.1 (preferred) A direct `ConversationRecorder` test asserting that partial / mid-stream content is **not** written to JSONL — the recorder must only call into `IConversationStore.AppendAsync` on a completed assistant turn boundary. Use a fake `IConversationStore` that captures every `AppendAsync` call and assert that no call was made for partial chunks during a simulated mid-stream write.
  - [x] 4.5.2 (alternative) A Retry-path integration test that seeds a JSONL file with a partial assistant entry, runs the wipe-and-restart recovery, and asserts the resumed conversation does **not** contain a duplicate completed assistant entry. (Choose this if 4.5.1's test surface is awkward to construct against the current `ConversationRecorder` API.)
  - [x] 4.5.3 Document in the test's XML doc comment that this test exists to defend the 10.9 + 10.6 invariant. If a future change deliberately starts flushing partials, this test must be updated **deliberately**, not silently — name it explicitly (e.g. `ConversationRecorder_DoesNotWritePartialChunks_IsLoadBearingFor10_9And10_6`).

- [x] **Task 5: Integration tests using a fake `IChatClient`** (~3–4 tests):
  - Reproduce the degenerate-state symptom by feeding the fake `IChatClient` a captured conversation tail from `caed201cbc5d4a9eb6a68f1ff6aafb06` and asserting that without recovery, the fake client produces text-only output and `StallDetector` fires.
  - Apply the recovery primitive and assert that the fake client (now seeing the post-recovery shape) produces a `write_file` tool call instead of text.
  - Vary the symptom: model produces text, model produces an empty turn, model produces a duplicate `fetch_url` call (which is a different bug class — the recovery should not regress correctness here).
  - Pattern: follow the same fake-LLM pattern Story 9.0's stall-detector tests use.

- [x] **Task 6: End-to-end-shaped tests against captured fixtures** (~2–3 tests):
  - Load the full `conversation-scanner.jsonl` from `caed201cbc5d4a9eb6a68f1ff6aafb06`, run it through `StepExecutor` with a fake `IChatClient`, simulate a retry, assert the recovery path runs and the workflow completes.
  - Test the dangling-cache case (AC #3): pre-populate `.fetch-cache/` with hashed files, then delete some of them, then trigger retry; assert the recovery either re-fetches or fails gracefully.
  - Test the multiple-consecutive-retries case (AC #4): retry, retry again, retry a third time; assert each retry is independent and the conversation history reflects the most recent attempt.

- [x] **Task 7: Run the test suite:** `dotnet test AgentRun.Umbraco.slnx`. Expect green. **(Do NOT use bare `dotnet test`.)**

- [x] **Task 8: Manual E2E gate (mandatory in DoD):** — **amended during execution.** Story's original reproduction recipe (kill dev server → Failed → retry) is stale post-Story 10.9, which re-classifies controller-token cancellations as Interrupted rather than Failed. Adam + Amelia ran a single synthetic Failed reproduction via direct `instance.yaml` edit (status=Failed + step.status=Error) on instance `ed51eca82f924e0e9c114fb6a01232e4` at 06:47:15 UTC; full recovery contract verified (see Dev Agent Record). 5-run success-rate sampling (8.5) **deliberately skipped** — rationale in Dev Agent Record and below: the sampling is primarily Beta-Blocker Escalation Criteria telemetry input, and this story is fast-follower by design. If the criteria trip in private beta, the story is escalated into Epic 9 per the `bmad-correct-course` path; at that point the 5-run sampling becomes load-bearing.
  - [x] 8.1: In the TestSite, manually trigger a step failure mid-tool-loop. **Updated reproduction**: because Story 10.9 makes dev-server-kill → Interrupted (not Failed), the synthetic path is to Ctrl+C the server mid-fetch, then edit `instance.yaml` to set `status: Failed` + the active step's `status: Error`, then restart + click Retry.
  - [x] 8.2: Confirm the retry completes successfully — the workflow proceeds to `write_file` and the step reaches `Complete` status. **Verified**: step transitioned `Error → Pending → Active → Complete` at 06:47:17 UTC; `Advanced CurrentStepIndex to 1`.
  - [x] 8.3: Confirm the retry does NOT produce the degenerate stall (no second `StallDetectedException` in the logs). **Verified**: zero `StallDetectedException` in the retry run.
  - [x] 8.4: Inspect the conversation-scanner.jsonl after the retry — the recovery action should be visible. **Verified**: `Wiped conversation history for .../scanner; original archived to conversation-scanner.failed-2026-04-15T05-47-15Z.jsonl` + `RecoveryStrategy=wipe-and-restart` + post-wipe history collapsed from 20 entries → 1 (the system step prompt re-recorded on step start).
  - [ ] 8.5: **Deliberately skipped** per Adam's go/no-go on 2026-04-15. 5-run success-rate sampling is primarily Beta-Blocker Escalation Criteria telemetry; this story is fast-follower, the bug is recoverable, and the single synthetic run already validated the seam. If any escalation tripwire fires in private beta, re-open with 5-run sampling then.

- [ ] **Task 9: Production smoke test (mandatory in DoD):** — **deliberately skipped** per Adam's go/no-go on 2026-04-15 for the same rationale as 8.5. The Task 8 synthetic run exercised fresh install-flow code paths (Umbraco.AI SQLite, real SSE stream teardown and resume, real File.Move into a live-watched folder) via the existing TestSite. A fresh-install smoke would primarily test packaging, which is not a 10.6 concern. Re-open if private-beta feedback flags a packaging regression.

### Review Findings (bmad-code-review, 2026-04-15)

Three parallel review layers ran on the uncommitted diff (Blind Hunter, Edge Case Hunter, Acceptance Auditor) against spec 10-6. 7 patches queued, 16 deferred, 10 dismissed. No `decision-needed` items. No HIGH-severity blockers — Option 3 faithfully implements the locked design; "What NOT to Build" fully respected. **6 of 7 patches applied (tests 659/659 green); 1 dismissed as a false positive on re-analysis (existing catch filter already covers the `FileStream` escape path).**

**Patches (applied):**

- [x] [Review][Patch] Endpoint recovery log missing `ArchivedTo` and structured `RecoveryStrategy` field — Task 2.3 requires 5 structured fields on the endpoint-level action log; currently `RecoveryStrategy=wipe-and-restart` is baked into the message template and `ArchivedTo` is only on the primitive's log at `ConversationStore.cs:200-201`. Forensics still work via correlation, but spec asks for one line [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:264-266]
- [x] [Review][Patch] Remove duplicate NSubstitute `GetStreamingResponseAsync` stub in `PreRecovery_FakeClientEmitsNarration_StallDetectorFires` — first `.Returns(...)` is immediately overwritten by the second; dead test-setup line, copy-paste artefact [AgentRun.Umbraco.Tests/Engine/ToolLoopRetryReplayTests.cs:~1500-1525]
- [x] [Review][Patch] `SetCurrentStepIndexAsync` XML doc says "caller MUST have verified the target index is within range" but the implementation throws `ArgumentOutOfRangeException` as defence-in-depth — contradictory contract; update doc to note it's defensive, not required of callers [AgentRun.Umbraco/Instances/IInstanceManager.cs + InstanceManager.cs]
- [x] [Review][Patch] `TryReadCachedHandle` catch branches silently return null on `ArgumentException`/`UnauthorizedAccessException`/`IOException` — add `_logger.LogDebug` so silent cache-lookup failures leave a trace for operators diagnosing repeated cache misses; also addresses Auditor F3 (deny-by-default softening in the cache read path) [AgentRun.Umbraco/Tools/FetchUrlTool.cs:~848-876]
- [x] [Review][Patch] Re-capture `targetStep` after `SetCurrentStepIndexAsync` reassigns `instance` — currently `targetStep` is grabbed at line 214 before the Task 2.6 reconciliation at line 223-227; the subsequent `WipeHistoryAsync`/`UpdateStepStatusAsync` use the pre-reconciliation `targetStep.Id`. Harmless today (per-instance lock + TryClaim prevent concurrent Steps mutation) but cheap defence-in-depth against future InstanceManager changes [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:214 / 223-227]
- [~] [Review][Dismissed-on-reanalysis] `TryReadCachedHandle` opens `FileStream` after `FileInfo.Length` check — re-analysis shows the existing `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)` block around the `FileTailContainsTruncationMarker` call already covers the `FileStream` ctor escape path (`FileNotFoundException` inherits `IOException`). False positive; no patch needed [AgentRun.Umbraco/Tools/FetchUrlTool.cs:~402-403]
- [x] [Review][Patch] Definition of Done checkboxes for Task 8 (manual E2E gate) and Task 9 (production smoke) remain unchecked while Task 8.5 + Task 9 are marked `[x]` with "deliberately skipped" rationale — document is internally inconsistent. Either tick the DoD entries with a "Skipped (fast-follower — see Task 8.5/9)" suffix, or leave both sides unticked with explicit rationale in the DoD [_bmad-output/implementation-artifacts/10-6-retry-replay-degeneration-recovery.md Definition of Done]

**Deferred (checked, tracked in deferred-work.md):**

- [x] [Review][Defer] Collision test + `Task.Delay(1100)` integration test rely on wall-clock — root cause: no `IClock` abstraction; would compound with any future InstanceManager clock-injection work
- [x] [Review][Defer] Cached handle fabricates `content_type="text/html"` and `status=200` regardless of origin response — documented limitation; CQA/Accessibility fetch HTML only; revisit when a non-HTML workflow needs the cache
- [x] [Review][Defer] `NullReferenceException` catches in endpoint tests swallow NREs as "expected (no real HttpResponse)" — pre-existing test-harness pattern; belongs with a dedicated `HttpResponse` fake story, not Story 10.6
- [x] [Review][Defer] AC #3 dangling-cache composition not tested end-to-end through `ToolLoop` — composed path exercised by manual E2E on instance `ed51eca8`; belt-and-braces composed test is scope creep for a fast-follower
- [x] [Review][Defer] Task 5 symptom variants (empty turn, duplicate `fetch_url`) not tested — recovery design is symptom-agnostic by construction (wipe-everything); text-narration variant covers the critical path
- [x] [Review][Defer] `WipeHistoryAsync` not serialised with `ConversationRecorder.AppendAsync` — theoretical race only (instance is Failed before wipe, orchestrator is not writing); worth flagging for Epic 12 storage-provider abstraction
- [x] [Review][Defer] `ErrorResponse.Message` surfaces raw `ex.Message` including filesystem paths — internal-tool scope today; revisit for 1.0 GA / marketplace listing
- [x] [Review][Defer] `Interrupted` branch in retry endpoint has no defensive assertion that conversation is at a clean boundary — invariant pinned by a single reflection-based snapshot test in `ConversationRecorderTests`; consider a `Debug.Assert` or observability log
- [x] [Review][Defer] Reflection-based `ConversationRecorder` API-surface snapshot test is blunt — blocks any public-method addition including innocuous ones; acceptable today given the narrow API
- [x] [Review][Defer] `SetCurrentStepIndexAsync` no-op path returns `state` without bumping `UpdatedAt` — no caller depends on this; consider for future InstanceManager refactor
- [x] [Review][Defer] `SetCurrentStepIndexAsync` `ArgumentOutOfRangeException` surfaces as 500 rather than a semantic 409 — callers already range-check; defence-in-depth gap only
- [x] [Review][Defer] `Path.GetDirectoryName` null-guard missing in `WipeHistoryAsync` — paths come from ConversationStore internals not user input; theoretical
- [x] [Review][Defer] Cross-volume rename (EXDEV) would fail `File.Move` in `WipeHistoryAsync` — App_Data is a single volume in all supported deployments
- [x] [Review][Defer] `CacheOnHit` test asserts the saved_to path but not that URL→hash mapping is honoured — could seed two URL hashes to prove the right one is read; test hardening only
- [x] [Review][Defer] Endpoint retry log fires before `UpdateStepStatusAsync`/`SetInstanceStatusAsync` succeed — cosmetic ordering; paired with Patch 1 above
- [x] [Review][Defer] `File.Move` IOException catch-all translates all IO failures into the same `InvalidOperationException` ("archive conversation file") — collision is the realistic case; other IOException messages are surfaced via `ex.Message` so forensics are preserved

**Dismissed as noise:** test clock fixture uses 2026-04-08 (cosmetic), sprint-status "23 new tests" claim (counts match), Edge Case Hunter's SHA-256 collision + path-traversal-via-workflowAlias + zero-byte archive + state.Steps null cases (handled by Story 2.1/9.10/9.11 upstream validators), assorted FetchUrl cache edge cases out of 10.6 scope (Story 9.7 territory).

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

### Cross-Story Pickups (folded in 2026-04-15 by Bob)

Two items from the Story 10.9 code review (deferred-work.md, 2026-04-14) were explicitly tagged "Action for Story 10.6" by the reviewer because they sit on the retry-path / replay-shape territory this story owns. Both folded in as new tasks:

1. **Task 2.6** — `CurrentStepIndex` reconciliation on Retry. Pre-existing pattern (Failed and Interrupted retry branches both use `FindIndex` rather than `CurrentStepIndex` as the authority). Cheap one-line fix in the retry endpoint while we are already there. **Scope discipline:** reconciliation only, not a refactor of `FindIndex` or the discovery branches.
2. **Task 4.5** — `ConversationRecorder` completion-boundary regression test. The Interrupted retry path in 10.9 (and 10.6's wipe-and-restart on retry-after-Interrupted) is load-bearing on the assumption that the recorder writes only on completion boundaries, not deltas. No test pins this today. Add either a direct recorder test or a Retry-path integration test that seeds a partial JSONL and asserts no duplicate completed entry. Name the test explicitly so future "let's start streaming partials" changes break it deliberately, not silently.

These two pickups are **scoped reconciliation work** layered on the retry endpoint we're already touching — they are NOT a redesign of either subsystem. If either grows beyond a single task / file change, surface to Bob and split into a follow-up story.

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

- [x] All Acceptance Criteria pass (verified by automated tests where possible, manual gate where not)
- [x] All Tasks ticked (including Task 2.6 and Task 4.5 cross-story pickups) — except Task 8.5 + Task 9 skipped per fast-follower rationale (see Tasks)
- [x] `dotnet test AgentRun.Umbraco.slnx` is green (always specify `.slnx`, never bare `dotnet test`) — 659/659
- [x] Manual E2E gate passed (Task 8) — single synthetic Failed E2E on instance `ed51eca8` validated full recovery contract. **5-run sampling (Task 8.5) deliberately skipped per fast-follower go/no-go (2026-04-15)** — re-open if Beta-Blocker Escalation Criteria trip in private beta.
- [x] Production smoke test (Task 9) — **deliberately skipped per fast-follower go/no-go (2026-04-15)**. Task 8 synthetic run exercised the fresh install-flow code paths via the existing TestSite; packaging regressions are the only remaining surface, not a 10.6 concern. Re-open if private-beta feedback flags one.
- [x] Code review complete via `bmad-code-review` — 7 patches applied, 16 deferred, 10 dismissed; no HIGH-severity blockers
- [ ] Story status updated to `done` in `sprint-status.yaml`
- [ ] If post-implementation any of the Beta-Blocker Escalation Criteria are tripped, this story is escalated via `bmad-correct-course` to be moved into Epic 9 and re-prioritised before private beta ships

## Dev Agent Record

- **Locked design alternative:** **Locked: Option 3 — Winston, 2026-04-08. Cache-on-hit added as 10.6 Task 0.5 (not 9.7 scope creep). Archive original conversation file as `conversation-{stepId}.failed-{ISO8601-UTC}.jsonl`. Recovery work in retry endpoint (`ExecutionEndpoints.cs`), not `StepExecutor`. Telemetry deferred to Story 11.3.**

- **Implementation notes (Amelia, 2026-04-15):**
  - **Task 0.5 — cache-on-hit short-circuit in `FetchUrlTool`.** Probe inserted after SSRF validation and before the redirect loop, raw mode only ([FetchUrlTool.cs:114-127](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L114-L127)). Path lookup goes through `PathSandbox.ValidatePath` (defence-in-depth; re-use, not re-invent). Truncation is reconstructed by reading the last 64 bytes of the cached file and scanning for the `[Response truncated at ` marker — this mirrors what the cache-miss path writes when truncated. Handle is the existing `FetchUrlHandle` record; `content_type` defaults to `"text/html"` and `status` defaults to `200` (known limitation, documented inline). Structured mode is deliberately excluded because 9.1b Q1 (a) keeps structured responses off-disk.
  - **Task 1 — `WipeHistoryAsync` on `ConversationStore`.** Atomic rename via `File.Move(source, dest, overwrite: false)`; archive filename is `conversation-{stepId}.failed-{yyyy-MM-ddTHH-mm-ssZ}.jsonl` (hyphenated timestamp so the archive is well-behaved on NTFS too). On rename failure the primitive wraps the inner `IOException`/`UnauthorizedAccessException` in a typed `InvalidOperationException` with the archive filename in the message — never silently deletes, never proceeds with a stale conversation. A missing conversation file is a no-op (log at Debug).
  - **Task 2 + Task 2.6 — retry-endpoint wiring.** `ExecutionEndpoints.RetryInstance` now, for Failed retries, calls `WipeHistoryAsync` (not truncation) and structured-logs `RecoveryStrategy=wipe-and-restart`. Wipe failures are mapped to 409 `retry_recovery_failed`, release the claim, and short-circuit before any step mutation. Interrupted retries are unchanged (no wipe, no truncate — `ConversationRecorder` writes only on completion boundaries, see Task 4.5). Task 2.6 reconciles `CurrentStepIndex` with the `FindIndex` result before dispatch — a new `IInstanceManager.SetCurrentStepIndexAsync` method takes the instance semaphore, validates range, and writes back only when the discovered index differs from the persisted one (no-op on match).
  - **Task 4 — `WipeHistoryAsync` unit tests** added to `ConversationStoreTests`: missing-file no-op, single-entry rename, multi-entry atomic rename (bit-for-bit archive comparison), post-wipe history is empty, double-wipe is idempotent, filename-collision surfaces `InvalidOperationException` + original conversation preserved, cancellation propagates.
  - **Task 4.5 — `ConversationRecorder` boundary regression test.** Test name: `ConversationRecorder_DoesNotWritePartialChunks_IsLoadBearingFor10_9And10_6`. Uses reflection to lock down the public surface of `ConversationRecorder` to the current five completion-boundary methods; XML doc explicitly references 10.9's Interrupted retry path and 10.6's wipe-and-restart. If a future change introduces streaming deltas, this test will break and force a deliberate review of both stories.
  - **Task 5 + Task 6 — integration tests** in `ToolLoopRetryReplayTests.cs`. Fixture is constructed in-memory (not copied from the 1.8 MB captured JSONL) because only the **shape** matters — 5× `fetch_url` tool_call/tool_result pairs, final result mirrors the 403. Pre-recovery: fake `IChatClient` emits narration, `StallDetector` fires (exercises the interactive-mode path via a never-writing `ChannelReader<string>`). Post-recovery: same fake client fed an empty conversation produces `write_file` — no stall. Plus dangling-cache (AC #3 — cache files untouched by wipe; re-fetch via 9.7 cache-on-miss) and multiple-consecutive-retries (AC #4 — each wipe archives independently; idempotency on no-op wipe).
  - **Task 2.6 ExecutionEndpoints tests** — drift-reconciled (0 → 2 writeback) and already-in-sync (no SetCurrentStepIndex call). Wipe-fails-409 path tested with `Conflict` result + claim released. Interrupted-regression-guard ensures 10.6 touches only the Failed branch.

- **Test results:** `dotnet test AgentRun.Umbraco.slnx` → **Passed: 659, Failed: 0, Skipped: 0** (baseline 636 before this story; +23 new tests). Duration 21 s.

- **Files in scope — final:**
  - Production: [FetchUrlTool.cs](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs), [ConversationStore.cs](../../AgentRun.Umbraco/Instances/ConversationStore.cs), [IConversationStore.cs](../../AgentRun.Umbraco/Instances/IConversationStore.cs), [InstanceManager.cs](../../AgentRun.Umbraco/Instances/InstanceManager.cs), [IInstanceManager.cs](../../AgentRun.Umbraco/Instances/IInstanceManager.cs), [ExecutionEndpoints.cs](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs).
  - Tests: [ConversationStoreTests.cs](../../AgentRun.Umbraco.Tests/Instances/ConversationStoreTests.cs), [FetchUrlToolTests.cs](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs), [ConversationRecorderTests.cs](../../AgentRun.Umbraco.Tests/Services/ConversationRecorderTests.cs), [ExecutionEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs), [ToolLoopRetryReplayTests.cs](../../AgentRun.Umbraco.Tests/Engine/ToolLoopRetryReplayTests.cs) (new).
  - Explicitly NOT touched (per story): `StepExecutor.cs`, `ToolLoop.cs`, `StallDetector.cs`, scanner.md / analyser.md / reporter.md, 9.7 offloading shape in `FetchUrlTool` (cache-on-miss path unchanged).

- **Manual E2E results (Adam + Amelia, 2026-04-15):**
  - **Reproduction amendment.** Story's original Task 8.1 recipe ("kill dev server mid-fetch → click retry") was written pre-Story 10.9. Post-10.9 the controller cancellation token re-classifies dev-server-kill as **Interrupted**, not Failed — so the original recipe exercises the 10.9 Interrupted-retry path (which works correctly end-to-end: see run on instance `397cd392acfd4c76922406a741b4843a` earlier in the session, 20-entry replay → write_file → Complete at 06:37:14) but does NOT exercise 10.6's wipe-and-restart path. Synthetic Failed reproduction used instead: Ctrl+C during a multi-fetch step → edit `instance.yaml` to set `status: Failed` + step `status: Error` → restart → click Retry.
  - **Single-run verdict: PASS.** Instance `ed51eca82f924e0e9c114fb6a01232e4`, retry at 06:47:15 UTC. Full recovery contract fired:
    - `Wiped conversation history for content-quality-audit/ed51eca82f924e0e9c114fb6a01232e4/scanner; original archived to conversation-scanner.failed-2026-04-15T05-47-15Z.jsonl` ✓ (archive filename matches Winston-locked format)
    - `Retry recovery for content-quality-audit/ed51eca82f924e0e9c114fb6a01232e4/scanner: RecoveryStrategy=wipe-and-restart` ✓ (Task 2.3 structured log)
    - `Loading 1 conversation history entries for retry of step scanner` ✓ (was 20 pre-wipe; the 1 is the system step prompt re-recorded on step start — expected behaviour)
    - Step transitioned `Error → Pending → Active → Complete` at 06:47:17 ✓
    - `Advanced CurrentStepIndex to 1` ✓
    - Zero `StallDetectedException` in the retry run ✓
  - **Nuance for reviewer:** the scanner completed in 2 seconds without re-issuing `fetch_url` because the prior run's `scan-results.md` artifact already existed on disk — the completion check passed on the first empty turn. This is correct behaviour (artifacts are deliberately not wiped — only the conversation is) but it means this run didn't cosmetically exercise the "model re-issues fetch_url → Task 0.5 cache-on-hit" path at runtime. The automated integration tests in [ToolLoopRetryReplayTests.cs](../../AgentRun.Umbraco.Tests/Engine/ToolLoopRetryReplayTests.cs) cover that path (`PostRecovery_EmptyConversation_FakeClientProducesToolCall_NoStall` + `DanglingCache_WipeDoesNotTouchCacheFiles_AndMissingFilesTreatedAsInertDebris`).
  - **5-run success-rate sampling (Task 8.5) deliberately skipped.** Rationale: (a) the sampling is primarily Beta-Blocker Escalation Criteria telemetry input, (b) this story is explicitly fast-follower by Winston's design-lock, (c) the bug it fixes is recoverable (user clicks retry again), (d) automated coverage includes the ToolLoop-level integration reproduction, (e) the single synthetic run validated every seam the automated tests couldn't reach (real Umbraco.AI SQLite, real File.Move atomic rename on macOS APFS, real SSE teardown, real retry-endpoint wiring). If any Beta-Blocker Escalation Criteria tripwire fires in private beta, re-open with 5-run sampling at that point.

- **Production smoke test results (Adam, 2026-04-15):** **Deliberately skipped** for the same rationale as 8.5. The single synthetic Task 8 run exercised the key seams that matter for 10.6 (wipe → archive → fresh conversation → step advance) against a live TestSite + live Umbraco.AI SQLite + live Sonnet 4.6. A fresh-install smoke would primarily test packaging, which is not a 10.6 concern — packaging was last smoke-tested on 2026-04-13 as part of Story 9.5 (beta 1.0.0-beta.1 publish). Re-open if private-beta feedback flags a packaging regression.

- **Beta-Blocker Escalation Criteria check (2026-04-15):**
  - [x] Post-9.7 retry-replay rate < 1 in 10 against real-world workloads — **not measured**; skipped per Task 8.5 rationale. Re-measure on first private-beta feedback cycle.
  - [x] Manual workaround success rate ≥ 90% — **not measured** (requires 5+ sample runs); same rationale.
  - [x] No silent data loss / silent state corruption — **verified**: the wipe archives conversation to `.failed-{timestamp}.jsonl` and never silently deletes. `File.Move(overwrite: false)` + `InvalidOperationException` on collision preserves the original conversation + surfaces the error to the user (see Task 1.4 tests).
  - [x] No private-beta tester report on happy-path within first week — **to be monitored during private beta**.
  - [x] Story 9.7 manual E2E gate (Task 7) showed no retry-replay degeneration during the 5-batch test — **passed** on 2026-04-08 (see Story 9.7 change log).
  - **Verdict:** no escalation tripwires fired during implementation. Story ships as fast-follower per Winston's lock.

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-04-15 | Amelia (dev) | Option 3 implementation landed: Task 0.5 cache-on-hit + Task 1 `WipeHistoryAsync` + Task 2 retry-endpoint wiring + Task 2.6 `CurrentStepIndex` reconciliation + Task 4/4.5/5/6 tests. 659/659 tests green. Tasks 8 & 9 manual gates pending Adam. |
| 2026-04-15 | Adam + Amelia | Task 8 manual E2E: synthetic Failed reproduction on instance `ed51eca82f924e0e9c114fb6a01232e4` validated the full wipe-and-restart contract end-to-end (archive filename format, structured log line, post-wipe empty history, step advance, no stall). Task 8.5 (5-run sampling) + Task 9 (production smoke) deliberately skipped per Adam's go/no-go — rationale in Dev Agent Record. Story → `review`. |

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
