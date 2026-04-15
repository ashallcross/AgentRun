# Story 10.7a: Backend Hotspot Refactors

Status: review

**Depends on:** Story 10.1 (concurrency locking — done), Story 10.11 (SSE keepalive + Engine boundary — done; establishes the Engine→Services adapter pattern reused here).
**Followed by:** Story 10.7b (frontend instance-detail + chat cursor), Story 10.7c (content-tool DRY + comment hygiene). Split from the original [Story 10.7 parent spec](./10-7-code-shape-cleanup-hotspot-refactoring.md) on 2026-04-15 per Adam's quality-risk concern on single-PR delivery.
**Branch:** `feature/epic-10-continued`
**Priority:** 8th in Epic 10 (10.2 → 10.12 → 10.8 → 10.9 → 10.10 → 10.1 → 10.6 → 10.11 → **10.7a** → 10.7b → 10.7c → 10.4 → 10.5).

> **Three tracks shipped as one backend-focused story.** Covers Tracks A (FetchUrlTool split), C (StepExecutor partial extraction), and B (ToolLoop split) from Winston's architect triage of the [Codex bloat review (2026-04-10)](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md). Frontend hotspot (Track D) + content-tool cleanup (Tracks E/G) move to sibling stories 10.7b and 10.7c respectively.
>
> **The goal is clearer responsibility boundaries with the fewest necessary moves — NOT maximum decomposition.** Every extraction must reduce reasoning load; thin one-method wrappers are explicitly rejected. When in doubt, leave the file alone.

## Story

As a maintainer of AgentRun.Umbraco,
I want the three highest-complexity backend accumulation points (`FetchUrlTool`, `StepExecutor`, `ToolLoop`) refactored into clearer responsibility boundaries,
So that future backend changes are lower-risk, the core execution pressure points are easier to reason about, and the engine surface is ready for public-launch scrutiny.

## Context

**UX Mode: N/A.** Pure-shape refactor — backend behaviour unchanged. No new features, no new endpoints, no new config surface. Frontend untouched.

### Why this story was split from the parent

The original [Story 10.7](./10-7-code-shape-cleanup-hotspot-refactoring.md) bundled seven tracks across ~4 days of work into a single PR. Adam flagged quality risk in a single-PR delivery:

- **Independent review gates.** Each track-group deserves its own focused code review (different LLM per Adam's discipline) so that Track A bugs surface before Track D work begins — not weeks later during final DoD.
- **Independent manual E2E gates.** Backend refactor risks (FetchUrlTool structured extraction, ToolLoop stall recovery) are best verified with a CQA workflow E2E; frontend risks need a different test posture; content-tool risks a third. Bundling all three into one E2E sweep hides regressions between subsystems.
- **Bisectability at story granularity, not just commit granularity.** If a regression shows up in public beta, "which story shipped this?" should narrow the blast radius faster than "which commit in a 15-commit PR?"

Bundling rationale (shared review/E2E ceremony cost) was reasonable when "this'll all flow" was the assumption — Adam's quality concern flips that assumption. Locked decision 18 in the parent story already authorised this exact split ("Dev can stop at a track boundary if time-boxed... Tracks A+C+B (backend refactor) land first; Tracks D+F (frontend) land second; Tracks E+G (cross-cutting) land third"). This story is track-group 1 of 3.

### The three tracks in this story

| Track | Target | Disposition | Why | Est. effort |
|---|---|---|---|---|
| **A** | [`Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) (789 lines) | Split | Strongest backend hotspot; tool has become a mini-subsystem owning validation + fetch transport + HTML extraction + cache I/O | 1.5 days |
| **C** | [`Engine/StepExecutor.cs`](../../AgentRun.Umbraco/Engine/StepExecutor.cs) (366 lines) | Partial | Extract inner `ToolDeclaration` + failure handler ONLY; sequencing stays | 0.25 day |
| **B** | [`Engine/ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs) (603 lines) | Split (directional) | Stall recovery policy + streaming accumulator are stable enough to extract; main loop stays | 1 day |

### Tracks explicitly deferred to sibling stories (do not touch)

| Deferred to | Scope |
|---|---|
| **10.7b** | Frontend `agentrun-instance-detail.element.ts` split + `agentrun-chat-panel.element.ts` cursor fix |
| **10.7c** | Content-tool DRY (`ContentToolHelpers`) + O(n²) truncation binary-search fix + comment hygiene polish pass |

### Tracks explicitly REJECTED (do not touch — Winston triage)

From [epics.md #Story-10.7](../planning-artifacts/epics.md#story-107-code-shape-cleanup--hotspot-refactoring):

| File | Codex recommendation | Winston verdict | Rationale |
|---|---|---|---|
| [`Engine/PromptAssembler.cs`](../../AgentRun.Umbraco/Engine/PromptAssembler.cs) (236 lines) | Simplify | **Skip** | Clear sections, reasonable size; splitting fragments not simplifies |
| [`Engine/WorkflowOrchestrator.cs`](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) (200 lines) | Simplify | **Defer** | Rewrite comes with background execution (Epic 12); refactoring now is wasted |
| `Security/*`, `Instances/*`, `Workflows/*` | — | **Keep** | Already bounded and intentional |

### Architect's locked decisions (do not relitigate)

These decisions were locked in the parent story on 2026-04-15 and are preserved verbatim here. Track-specific decisions only — frontend and content-tool decisions live in 10.7b / 10.7c.

**Universal (all three 10.7 child stories):**

1. **"Fewest necessary moves" is the design principle.** Any extraction that does not demonstrably reduce reasoning load must be rejected in favour of leaving the file alone. Thin one-method wrappers are explicitly forbidden. When in doubt, don't split. The dev agent should read the refactor brief's "Review Rules For The Next Agent" ([refactor brief §Review Rules](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md)) before starting each track.
2. **Behaviour-preserving refactor — no functional changes in this story.** Every extraction must leave existing tests passing without modification to test *assertions* (setup may change to match new constructor shapes; BDD intent must be preserved).
3. **Engine boundary must stay clean (Story 10.11 invariant).** No new `Engine/` file may import `Umbraco.*`. `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` must remain at 0 matches at the end of this story. If any extraction tempts the dev to reach into Umbraco, the extraction is being shaped wrong — reshape or leave as-is.

**Track A (FetchUrlTool):**

4. **Track A extracts exactly two collaborators — no more, no less.** `IHtmlStructureExtractor` (AngleSharp parsing + heading/image/anchor/form/semantic walks) and `IFetchCacheWriter` (path sandbox + file I/O for `.fetch-cache/`). Do NOT extract `UrlFetchRequestValidator`, `RedirectingHttpFetcher`, or `FetchedResponseLimiter` — those would be thin wrappers or fragment cohesive transport logic. The Codex brief suggested six extractions; Winston's triage + the "fewest necessary moves" rule reduces this to two. `FetchUrlTool` remains the transport + validation owner and becomes a coordinator over the two new collaborators.

**Track B (ToolLoop):**

5. **Track B extracts exactly two collaborators — `StallRecoveryPolicy` + `StreamingResponseAccumulator`.** Do NOT extract `ToolCallProcessor`, `InteractiveWaitCoordinator`, or `ConversationAppender`. The dispatch loop body, interactive-wait handler, and `IConversationRecorder` call sites already have clear homes inside `ToolLoop.cs`; extracting them would move branches around without reducing reasoning load (refactor brief §"Reject any proposed split that would create thin wrappers"). `StallRecoveryPolicy` encapsulates the "classify stall → synthesise nudge → one-shot retry" logic currently at [ToolLoop.cs:154-294](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L154); `StreamingResponseAccumulator` encapsulates the text builder + updates list + optional SSE emission at [ToolLoop.cs:99-141](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L99).

**Track C (StepExecutor):**

6. **Track C is PARTIAL — `ToolDeclaration` promoted to its own file + `IStepExecutionFailureHandler` extracted. That's the whole track.** Do NOT extract `ConversationHistoryMapper`, `StepToolDeclarationFactory`, or `StepChatOptionsFactory`. Sequencing logic stays. `ConvertHistoryToMessages` stays (it's a 60-line helper, private, used once — promoting it to a collaborator would not improve anything). `MaxOutputTokens=32768` hardcode at [StepExecutor.cs:175](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L175) stays with its existing Story 9.1b carve-out comment (this comment IS load-bearing technical rationale — preserved for 10.7c's Track G pass).

**Cross-cutting (backend-relevant):**

7. **No new NuGet dependencies.** Track A uses the existing AngleSharp dep. Zero new deps in this story.
8. **Test budget target: ~14 new tests.** Breakdown: Track A (~6: 3 for `HtmlStructureExtractor`, 3 for `FetchCacheWriter`, plus preserved adjusted tests in `FetchUrlToolTests`), Track B (~6: 3 for `StallRecoveryPolicy`, 3 for `StreamingResponseAccumulator`), Track C (~2 for `StepExecutionFailureHandler` — promote + verify existing failure tests still pass). Full backend suite must stay green. Preserve ALL existing tests — any test deletion must be justified in the Dev Notes as either duplicate coverage or no-longer-applicable setup.
9. **Test counts are guidelines, not ceilings.** From Epic 8+ retro. If an extraction needs 8 tests to cover a subtle edge case, write the 8 tests. Under-budget is fine too — don't pad.
10. **Priority ordering inside the story: A → C → B.** Track A first because it's the biggest win and the Codex brief's highest priority. Track C before Track B because it's 0.25 day and unblocks the StepExecutor → ToolLoop call shape. Track B extracts policy from the `StepExecutor`-called method.

## Acceptance Criteria (BDD)

### AC1: `FetchUrlTool.cs` drops below 400 lines and reads as a transport + validation coordinator

**Given** the refactored [`Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs)
**When** the file is inspected
**Then** the file is ≤ 400 lines (down from 789)
**And** it contains no AngleSharp parsing code (no `using AngleSharp`, no `BuildForms`, no `BuildSemanticElements`, no anchor-walk / heading-walk / image-walk)
**And** it contains no `.fetch-cache/` path resolution or `File.WriteAllBytesAsync` calls
**And** the transport concerns (SSRF per-hop re-validation, redirect loop, size limiting, UTF-8 decoding, content-type branching) remain inside the file — these are NOT extracted

### AC2: `HtmlStructureExtractor` exists in `Tools/` and owns AngleSharp parsing

**Given** the new file [`AgentRun.Umbraco/Tools/HtmlStructureExtractor.cs`](../../AgentRun.Umbraco/Tools/HtmlStructureExtractor.cs)
**When** the file is inspected
**Then** it exposes a public interface [`IHtmlStructureExtractor`](../../AgentRun.Umbraco/Tools/IHtmlStructureExtractor.cs) whose `Extract(byte[] body, Uri sourceUri, int unmarkedLength, bool truncated)` returns the same structured-result shape that `FetchUrlTool.ParseStructured` used to return
**And** all AngleSharp `using` directives appear ONLY in this file across `AgentRun.Umbraco/Tools/`
**And** the class is registered as a singleton in [`AgentRunComposer`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs)
**And** `FetchUrlTool` constructor accepts `IHtmlStructureExtractor` and delegates to it

### AC3: `FetchCacheWriter` exists in `Tools/` and owns cache-file I/O

**Given** the new file [`AgentRun.Umbraco/Tools/FetchCacheWriter.cs`](../../AgentRun.Umbraco/Tools/FetchCacheWriter.cs)
**When** the file is inspected
**Then** it exposes a public interface [`IFetchCacheWriter`](../../AgentRun.Umbraco/Tools/IFetchCacheWriter.cs) with `WriteHandleAsync` and `TryReadHandleAsync` methods
**And** it is the only file in `Tools/` that writes to `.fetch-cache/` (verified by grep: `grep -rn "\.fetch-cache" AgentRun.Umbraco/Tools/` returns hits only in this file and the matching test)
**And** it uses `_pathSandbox.ValidatePath(...)` before every file write exactly as `FetchUrlTool` did
**And** `FetchUrlTool` constructor accepts `IFetchCacheWriter` and delegates to it
**And** DI registration is added to `AgentRunComposer`

### AC4: `FetchUrlToolTests.cs` still passes and adapter tests exist

**Given** the existing [`FetchUrlToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs)
**When** the test suite runs
**Then** all existing tests pass without changes to BDD intent or assertions (setup may change to inject the new collaborators as NSubstitute mocks or real instances)
**And** new test file [`HtmlStructureExtractorTests.cs`](../../AgentRun.Umbraco.Tests/Tools/HtmlStructureExtractorTests.cs) covers: empty-body branch, valid-HTML branch, anchor-classification branch
**And** new test file [`FetchCacheWriterTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchCacheWriterTests.cs) covers: happy-path write, path-sandbox reject, directory auto-create
**And** `IFetchCacheWriter` is injected as a mock in `FetchUrlToolTests` (no real file I/O in tool-level tests)

### AC5: `ToolLoop.cs` drops below 400 lines

**Given** the refactored [`Engine/ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs)
**When** the file is inspected
**Then** it is ≤ 400 lines (down from 603)
**And** the stall-recovery branch at old [lines 154–294](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L154) is replaced with a single call to `IStallRecoveryPolicy.ClassifyAndRecoverAsync(...)`
**And** the streaming accumulation at old [lines 99–141](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L99) is replaced with a call to `IStreamingResponseAccumulator.AccumulateAsync(...)` whose return value is the accumulated text + last message
**And** the tool dispatch loop, interactive-wait logic, and `IConversationRecorder` calls remain inside `ToolLoop`

### AC6: `StallRecoveryPolicy` + `StreamingResponseAccumulator` exist in `Engine/`

**Given** the new files [`Engine/StallRecoveryPolicy.cs`](../../AgentRun.Umbraco/Engine/StallRecoveryPolicy.cs) + [`Engine/IStallRecoveryPolicy.cs`](../../AgentRun.Umbraco/Engine/IStallRecoveryPolicy.cs) and [`Engine/StreamingResponseAccumulator.cs`](../../AgentRun.Umbraco/Engine/StreamingResponseAccumulator.cs) + [`Engine/IStreamingResponseAccumulator.cs`](../../AgentRun.Umbraco/Engine/IStreamingResponseAccumulator.cs)
**When** the files are inspected
**Then** neither file imports `Umbraco.*` (Engine boundary preserved — AC-invariant from Story 10.11)
**And** each file has a dedicated test file: [`StallRecoveryPolicyTests.cs`](../../AgentRun.Umbraco.Tests/Engine/StallRecoveryPolicyTests.cs), [`StreamingResponseAccumulatorTests.cs`](../../AgentRun.Umbraco.Tests/Engine/StreamingResponseAccumulatorTests.cs)
**And** `StallRecoveryPolicy` exposes at minimum: "classify stall (step.completion_check vs empty tool-call vs unknown)", "synthesise nudge message", "one-shot retry flag" — whatever the exact method shape ends up being, these three behaviours must live inside this class, not in `ToolLoop`
**And** `StreamingResponseAccumulator` owns the text builder, updates list, and optional `ISseEventEmitter.EmitTextDeltaAsync` call — the FinishReason telemetry at [ToolLoop.cs:169-188](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L169) stays inside `ToolLoop` (it reads the *accumulated* result, so it's a post-accumulation concern)

### AC7: `ToolLoopTests.cs` + sister files still pass; new collaborator tests exist

**Given** existing [`ToolLoopTests.cs`](../../AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs) + `ToolLoopCompactionTests.cs` + `ToolLoopRetryReplayTests.cs`
**When** the test suite runs
**Then** all existing tests pass with only SetUp changes (mocking the two new collaborators instead of the inline behaviour)
**And** BDD intent of every existing test is preserved — no test renamed, no assertion weakened
**And** the stall-recovery tests that previously asserted on inline behaviour now assert via `_stallRecoveryPolicy.Received(1).ClassifyAndRecoverAsync(...)` (Received-call verification)
**And** new `StallRecoveryPolicyTests` covers the three classify branches + nudge shape + one-shot flag
**And** new `StreamingResponseAccumulatorTests` covers: text-delta path, empty-stream path, emit-SSE-or-not branch

### AC8: `StepExecutor.cs` — `ToolDeclaration` promoted and failure handler extracted

**Given** the refactored [`Engine/StepExecutor.cs`](../../AgentRun.Umbraco/Engine/StepExecutor.cs)
**When** the file is inspected
**Then** the inner `private sealed class ToolDeclaration : AIFunctionDeclaration` at [lines 351–365](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L351) is moved to its own file [`Engine/ToolDeclaration.cs`](../../AgentRun.Umbraco/Engine/ToolDeclaration.cs) (internal visibility — only `StepExecutor` uses it, so keep the access narrow)
**And** the failure classification block at [lines 233–266](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L233) is extracted behind [`IStepExecutionFailureHandler`](../../AgentRun.Umbraco/Engine/IStepExecutionFailureHandler.cs) in `Engine/` with implementation in [`Engine/StepExecutionFailureHandler.cs`](../../AgentRun.Umbraco/Engine/StepExecutionFailureHandler.cs)
**And** `StepExecutor` drops below 320 lines (down from 366)
**And** `ConvertHistoryToMessages` remains inside `StepExecutor` (locked decision 6)
**And** `StepExecutor` constructor accepts `IStepExecutionFailureHandler` as a new dependency; DI registration added to `AgentRunComposer`
**And** neither new file imports `Umbraco.*`

### AC9: Full backend suite green + no new warnings + boundary preserved

**Given** the story is complete
**When** `dotnet test AgentRun.Umbraco.slnx` runs
**Then** backend tests pass (baseline 679/679 from Story 10.11 → expect ~693/693 with ~14 new tests per locked decision 8; delta TBD by dev agent)
**And** `dotnet build AgentRun.Umbraco.slnx` is clean — zero new warnings introduced
**And** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns **0** matches (Story 10.11 invariant preserved)
**And** line-count checks: `FetchUrlTool.cs` ≤ 400 (AC1), `ToolLoop.cs` ≤ 400 (AC5), `StepExecutor.cs` ≤ 320 (AC8)
**And** frontend suite at 183/183 unchanged (no frontend work in this story)

## Tasks / Subtasks

### Task 1: Track A — Extract `IHtmlStructureExtractor` from `FetchUrlTool` (AC1, AC2)

- [ ] 1.1 Read [`FetchUrlTool.cs` lines 462–648](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L462) fully (`ParseStructured`, `BuildForms`, `BuildSemanticElements`, all the walk helpers).
- [ ] 1.2 Create [`AgentRun.Umbraco/Tools/IHtmlStructureExtractor.cs`](../../AgentRun.Umbraco/Tools/IHtmlStructureExtractor.cs):
  ```csharp
  using System;

  namespace AgentRun.Umbraco.Tools;

  public interface IHtmlStructureExtractor
  {
      object Extract(byte[] body, Uri sourceUri, int unmarkedLength, bool truncated);
  }
  ```
  Return type `object` because the current `ParseStructured` returns an anonymous object; if the dev agent wants to tighten this to a named record (`FetchedHtmlStructure`), that's a worthwhile small improvement — the anonymous object shape is already fixed by JSON serialisation, so promoting it doesn't change the wire format.
- [ ] 1.3 Create [`AgentRun.Umbraco/Tools/HtmlStructureExtractor.cs`](../../AgentRun.Umbraco/Tools/HtmlStructureExtractor.cs). Move every line of `ParseStructured` + `BuildForms` + `BuildSemanticElements` + the anchor/heading/image walks verbatim into methods on the new class. Keep the AngleSharp `using` directives on this file only.
- [ ] 1.4 Register in [`AgentRunComposer`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs): `builder.Services.AddSingleton<IHtmlStructureExtractor, HtmlStructureExtractor>();`
- [ ] 1.5 Update `FetchUrlTool` constructor to accept `IHtmlStructureExtractor`. Delete the moved methods. The structured-mode branch at [lines 238–272](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L238) calls `_htmlExtractor.Extract(...)` instead of the local method.
- [ ] 1.6 Create [`AgentRun.Umbraco.Tests/Tools/HtmlStructureExtractorTests.cs`](../../AgentRun.Umbraco.Tests/Tools/HtmlStructureExtractorTests.cs) with at least 3 tests: empty body → empty structure, minimal valid HTML → non-null structure with expected shape, anchor classification (internal vs external) correct.
- [ ] 1.7 Update [`FetchUrlToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs) SetUp to inject a real `HtmlStructureExtractor` (or a mock, if the tests are purely about fetch transport and not structured output — use judgement; keep real instance where structured output is actually asserted).
- [ ] 1.8 Run backend suite — all tests green. Verify `FetchUrlTool.cs` now reports ≤ 550 lines (AngleSharp extraction alone removes ~190 lines; further reduction comes from Task 2).

### Task 2: Track A — Extract `IFetchCacheWriter` from `FetchUrlTool` (AC1, AC3)

- [ ] 2.1 Read [`FetchUrlTool.cs` lines 274–399](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L274) (cache writing at 274–324, cache reading at 353–399).
- [ ] 2.2 Create [`AgentRun.Umbraco/Tools/IFetchCacheWriter.cs`](../../AgentRun.Umbraco/Tools/IFetchCacheWriter.cs):
  ```csharp
  using System.Threading;
  using System.Threading.Tasks;

  namespace AgentRun.Umbraco.Tools;

  public interface IFetchCacheWriter
  {
      Task<string> WriteHandleAsync(string instanceFolderPath, string url, byte[] body, string contentType, int unmarkedLength, bool truncated, CancellationToken cancellationToken);
      Task<object?> TryReadHandleAsync(string instanceFolderPath, string url, CancellationToken cancellationToken);
  }
  ```
  Return types match the current `FetchUrlTool` shape — dev agent may tighten to named records in the same way as Task 1.2.
- [ ] 2.3 Create [`AgentRun.Umbraco/Tools/FetchCacheWriter.cs`](../../AgentRun.Umbraco/Tools/FetchCacheWriter.cs). Constructor takes `IPathSandbox` (same dep `FetchUrlTool` currently holds for cache writes). Move the cache-write + cache-read code verbatim. The `.fetch-cache/` directory resolution, `PathSandbox.ValidatePath` call, `Directory.CreateDirectory`, and `File.WriteAllBytesAsync` all move here.
- [ ] 2.4 Register in `AgentRunComposer`.
- [ ] 2.5 Update `FetchUrlTool` constructor to accept `IFetchCacheWriter` (the tool no longer needs direct `IPathSandbox` *for cache I/O* — verify whether `IPathSandbox` is still needed for another purpose in the tool; if yes keep it, if no drop it from the constructor).
- [ ] 2.6 The raw-mode branch [lines 274–324](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L274) calls `await _cacheWriter.WriteHandleAsync(...)` instead.
- [ ] 2.7 The cache-hit short-circuit [lines 353–399](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L353) calls `await _cacheWriter.TryReadHandleAsync(...)` instead.
- [ ] 2.8 Create [`AgentRun.Umbraco.Tests/Tools/FetchCacheWriterTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchCacheWriterTests.cs) with at least 3 tests: happy-path write creates file at expected path, path-sandbox reject propagates exception, directory auto-created when missing. Use a temp directory (`Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())`) and clean up in `[TearDown]`.
- [ ] 2.9 Update `FetchUrlToolTests.cs` SetUp: inject an `IFetchCacheWriter` mock (NSubstitute) so tool tests don't touch real disk.
- [ ] 2.10 Run backend suite — all tests green. Confirm `FetchUrlTool.cs` ≤ 400 lines (AC1).

### Task 3: Track C — Promote `ToolDeclaration` to its own file + extract `IStepExecutionFailureHandler` (AC8)

- [ ] 3.1 Move inner class [`ToolDeclaration` at StepExecutor.cs:351–365](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L351) to [`AgentRun.Umbraco/Engine/ToolDeclaration.cs`](../../AgentRun.Umbraco/Engine/ToolDeclaration.cs). Make it `internal sealed class` (only `StepExecutor` uses it in the package; keep access narrow).
- [ ] 3.2 Read [`StepExecutor.cs` lines 233–266](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L233) — the failure-classification block.
- [ ] 3.3 Create [`AgentRun.Umbraco/Engine/IStepExecutionFailureHandler.cs`](../../AgentRun.Umbraco/Engine/IStepExecutionFailureHandler.cs) with a single method that takes the exception + the `StepExecutionContext` (or equivalent — dev agent picks the minimal parameter surface that covers the current block's inputs) and returns the side-effects as return values OR mutates the context and returns void. Prefer return-over-mutation where feasible to make the new handler testable without context fakery.
- [ ] 3.4 Create [`AgentRun.Umbraco/Engine/StepExecutionFailureHandler.cs`](../../AgentRun.Umbraco/Engine/StepExecutionFailureHandler.cs). Move the classification logic verbatim. Keep the `ProviderEmptyResponseException` / `StallDetectedException` / `AgentRunException` routing exactly as it is today — this is locked behaviour from the `AgentRunException bypasses LlmErrorClassifier` memory item (see [user memory](../../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_agentrun_exception_classifier.md)).
- [ ] 3.5 Update `StepExecutor` constructor to accept `IStepExecutionFailureHandler`. The old try/catch block at lines 233–266 becomes `catch (Exception ex) { context.LlmError = _failureHandler.Classify(ex, context); }` or similar, depending on the Task 3.3 shape.
- [ ] 3.6 Register the handler in `AgentRunComposer`.
- [ ] 3.7 Create [`AgentRun.Umbraco.Tests/Engine/StepExecutionFailureHandlerTests.cs`](../../AgentRun.Umbraco.Tests/Engine/StepExecutionFailureHandlerTests.cs) with at least 2 tests: `AgentRunException` does NOT go through the LLM error classifier (memory invariant); `ProviderEmptyResponseException` produces the expected classified error. The existing `StepExecutorTests` failure-path tests should continue to pass as integration-level coverage.
- [ ] 3.8 Run backend suite — all tests green. `StepExecutor.cs` ≤ 320 lines.

### Task 4: Track B — Extract `StreamingResponseAccumulator` from `ToolLoop` (AC5, AC6)

- [ ] 4.1 Read [`ToolLoop.cs` lines 99–141](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L99) carefully. Note that the accumulation is interleaved with `emitter.EmitTextDeltaAsync` calls + a try/catch that records partial text on failure.
- [ ] 4.2 Create [`AgentRun.Umbraco/Engine/IStreamingResponseAccumulator.cs`](../../AgentRun.Umbraco/Engine/IStreamingResponseAccumulator.cs) and [`Engine/StreamingResponseAccumulator.cs`](../../AgentRun.Umbraco/Engine/StreamingResponseAccumulator.cs). The interface exposes something like:
  ```csharp
  public interface IStreamingResponseAccumulator
  {
      Task<AccumulatedResponse> AccumulateAsync(
          IAsyncEnumerable<ChatResponseUpdate> stream,
          ISseEventEmitter emitter,
          IConversationRecorder recorder,
          CancellationToken cancellationToken);
  }
  ```
  `AccumulatedResponse` is a record with `string Text`, `ChatFinishReason FinishReason`, `IReadOnlyList<ChatResponseUpdate> Updates`, whatever else ToolLoop needs downstream. Dev agent designs the exact shape based on what the downstream code in ToolLoop reads.
- [ ] 4.3 Implementation: move the streaming loop body verbatim. The `try/catch` that records partial text on emitter failure **stays inside the accumulator** — that behaviour belongs to streaming/recording, not to orchestration.
- [ ] 4.4 ToolLoop calls `var accumulated = await _accumulator.AccumulateAsync(streamResponse, emitter, recorder, ct);` and then reads `accumulated.Text` / `accumulated.FinishReason` / `accumulated.Updates`.
- [ ] 4.5 The FinishReason telemetry block at [ToolLoop.cs:169-188](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L169) STAYS in ToolLoop (locked decision 5) — it operates on the post-accumulation result.
- [ ] 4.6 Register accumulator in `AgentRunComposer`.
- [ ] 4.7 Create [`AgentRun.Umbraco.Tests/Engine/StreamingResponseAccumulatorTests.cs`](../../AgentRun.Umbraco.Tests/Engine/StreamingResponseAccumulatorTests.cs) with at least 3 tests: text-delta path accumulates + emits, empty-stream path returns empty-text result with FinishReason, emitter failure records partial text to recorder.
- [ ] 4.8 Update `ToolLoopTests` SetUp to inject the mock accumulator. Existing tests that asserted on streaming behaviour now either (a) stay as integration-level tests using a real accumulator, or (b) swap to verifying `_accumulator.Received(1).AccumulateAsync(...)`. Prefer (a) for tests whose intent is end-to-end streaming behaviour.

### Task 5: Track B — Extract `StallRecoveryPolicy` from `ToolLoop` (AC5, AC6, AC7)

- [ ] 5.1 Read [`ToolLoop.cs` lines 154–294](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L154) fully. This is the empty-tool-call branch: classify stall via `StallDetector`, check completion via `ICompletionChecker`, synthesise a nudge message, record it, re-enter the loop once.
- [ ] 5.2 Create [`AgentRun.Umbraco/Engine/IStallRecoveryPolicy.cs`](../../AgentRun.Umbraco/Engine/IStallRecoveryPolicy.cs). Method shape is the dev agent's call but must cover: classify (via existing `StallDetector`), decide action (nudge vs fail vs terminate), synthesise message, interact with `ICompletionChecker` for the "step.completion_check passed → terminate-as-completed" path. Return a decision object that ToolLoop consumes:
  ```csharp
  public record StallRecoveryDecision(StallRecoveryAction Action, string? NudgeMessage, CompletionCheckResult? CompletionResult);
  public enum StallRecoveryAction { Terminate, Nudge, Fail }
  ```
- [ ] 5.3 Implementation: move the classification + nudge-synthesis code verbatim. The policy holds the `StallDetector` + `ICompletionChecker` deps (they currently live on `ToolLoop` — move them to the policy's constructor and remove from ToolLoop).
- [ ] 5.4 The "one-shot" flag (`nudgeAttempted` at [ToolLoop.cs:43-56](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L43)) can live on either side. Prefer passing it in as a parameter (`bool previouslyAttempted`) so the policy is stateless — ToolLoop tracks the flag across iterations.
- [ ] 5.5 Register policy in `AgentRunComposer`. Remove the direct `StallDetector` + `ICompletionChecker` registrations-as-toolloop-deps (they're still registered as-is for other consumers).
- [ ] 5.6 Create [`AgentRun.Umbraco.Tests/Engine/StallRecoveryPolicyTests.cs`](../../AgentRun.Umbraco.Tests/Engine/StallRecoveryPolicyTests.cs) with at least 3 tests: completion-check passed → Terminate, empty-tool-call unknown → Nudge with expected message shape, already-attempted → Fail.
- [ ] 5.7 Update `ToolLoopTests` stall-recovery tests: preserve BDD intent, swap the inline stall setup for `_stallRecoveryPolicy.ClassifyAndRecoverAsync(...).Returns(...)` and assert `.Received(1)` at the boundary.
- [ ] 5.8 Run backend suite — `ToolLoop.cs` ≤ 400 lines (AC5).

### Task 6: DoD verification + commit train

- [ ] 6.1 Full backend test: `dotnet test AgentRun.Umbraco.slnx` → all green (AC9, expect ~693).
- [ ] 6.2 Build clean: `dotnet build AgentRun.Umbraco.slnx` → 0 new warnings.
- [ ] 6.3 Engine boundary check: `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → 0 matches (Story 10.11 invariant).
- [ ] 6.4 Line-count checks:
  - `wc -l AgentRun.Umbraco/Tools/FetchUrlTool.cs` → ≤ 400 (AC1)
  - `wc -l AgentRun.Umbraco/Engine/ToolLoop.cs` → ≤ 400 (AC5)
  - `wc -l AgentRun.Umbraco/Engine/StepExecutor.cs` → ≤ 320 (AC8)
- [ ] 6.5 Commit train (per `feedback_no_ai_slop.md` memory — tracks as units, not a single blob):
  - Commit 1: Track A (FetchUrlTool — HtmlStructureExtractor + FetchCacheWriter) + tests
  - Commit 2: Track C (StepExecutor — ToolDeclaration + failure handler) + tests
  - Commit 3: Track B (ToolLoop — StreamingResponseAccumulator + StallRecoveryPolicy) + tests

  Each commit leaves the full backend test suite green. Each commit body ends with the standard Co-Authored-By trailer per CLAUDE.md.
- [ ] 6.6 Manual E2E — Adam walks the backend-relevant scenarios against TestSite:
  1. Start TestSite: `dotnet run` from `AgentRun.Umbraco.TestSite/`.
  2. Run the **Content Quality Audit** workflow end-to-end on Sonnet 4.6. Verify: scanner fetches URLs (Track A — FetchUrlTool + HtmlStructureExtractor + FetchCacheWriter still work end-to-end), analyser + reporter complete normally, final `run.finished: Completed`.
  3. **Cancel a running step** mid-execution — verify cancel still works (Story 10.8 invariant).
  4. **Retry a failed step** (synthetic Failed via disconnected profile) — verify retry still works (Story 10.6 invariant).
  5. **Interrupt via F5** during a long LLM step — verify Interrupted flow still works (Story 10.9 invariant).
  6. **Stall recovery path** — pick a workflow step that has previously exhibited the "empty tool-call loop" stall pattern (CQA scanner with some configurations) and confirm `StallRecoveryPolicy` nudges once then fails cleanly on second empty turn.
- [ ] 6.7 Set story status to `done` in this file + `sprint-status.yaml` once 6.1–6.6 all pass. Transition 10.7b from `backlog` to `ready-for-dev` in the same sprint-status update.

## Failure & Edge Cases

### F1: `HtmlStructureExtractor` throws on malformed HTML

**When** AngleSharp parsing fails on a malformed HTML payload (e.g. nested script tags, invalid UTF-8 sequences, truncation mid-tag)
**Then** the extractor must NOT throw into the `FetchUrlTool` boundary
**And** must return a minimal structure shape (empty headings, empty links, truncated=true) so the tool can still return a valid result to the LLM
**Net effect:** behaviour preservation — the current `ParseStructured` already handles this in-place; the extraction must preserve the same exception-swallowing semantics. Verify via a characterisation test in `HtmlStructureExtractorTests`.

### F2: `FetchCacheWriter.WriteHandleAsync` called with an invalid instance folder path

**When** the path sandbox rejects the target directory (e.g. symlink escape, traversal attempt)
**Then** the writer MUST propagate the `SandboxViolationException` (or equivalent) verbatim — `FetchUrlTool` currently surfaces this as a tool execution error, preserving that behaviour is mandatory per the `Security code: deny by default` rule in [project-context.md](../project-context.md)
**And** does NOT silently write to the sandbox root or fall back to a different path
**Net effect:** security invariant — the extraction must not weaken the path-sandbox guard by even a single branch.

### F3: `StallRecoveryPolicy` called with a null or disposed `ICompletionChecker`

**When** DI resolves with a null completion checker (configuration bug, future regression)
**Then** the policy throws `ArgumentNullException` at construction time, not at first use
**And** ToolLoop fails fast at step start, not mid-turn on a confused error
**Net effect:** constructor-level null-check covers this. No special handling needed; standard C# constructor validation.

### F4: `StreamingResponseAccumulator` emitter write throws mid-stream

**When** `ISseEventEmitter.EmitTextDeltaAsync` throws (proxy timeout, client disconnect)
**Then** the accumulator catches the exception, records partial text to `IConversationRecorder`, and returns the partial `AccumulatedResponse` with whatever `FinishReason` the stream surfaced
**And** does NOT rethrow — the streaming failure becomes a "short completion," not a step failure
**Net effect:** preserves current `ToolLoop` behaviour. The try/catch at [ToolLoop.cs:123-141](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L123) moves to the accumulator verbatim.

### F5: `StepExecutionFailureHandler.Classify(AgentRunException)` must NOT invoke `LlmErrorClassifier`

**Given** an `AgentRunException` subtype flows into the handler (e.g. `StallDetectedException` thrown by `StallRecoveryPolicy` after nudge exhaustion)
**When** the handler routes the exception
**Then** it bypasses `LlmErrorClassifier` entirely (per [`feedback_agentrun_exception_classifier.md`](../../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_agentrun_exception_classifier.md) — engine-domain exceptions must keep their messages intact, not be masked by the provider classifier)
**And** sets `context.LlmError` directly from the exception message
**Net effect:** this is a load-bearing behaviour from a past incident. The test at AC8 Task 3.7 codifies it.

## Dev Notes

### Why "fewest necessary moves" is a hard constraint

The refactor brief's closing paragraph is the whole design principle:

> "Do not optimize for maximum decomposition. Optimize for clearer responsibility boundaries with the fewest necessary moves."

Codex's maximal suggestions (split FetchUrlTool into 6 collaborators, split ToolLoop into 5) would produce a net-LESS-readable codebase — more files, thin wrappers, call-site indirection without reasoning benefit. Winston's triage collapses these to **2 + 2 + 2 extractions** (FetchUrlTool: HtmlStructureExtractor + FetchCacheWriter; ToolLoop: StallRecoveryPolicy + StreamingResponseAccumulator; StepExecutor: ToolDeclaration + FailureHandler). That's the sweet spot: each extracted file has enough to say to justify its existence and a clear test seam.

### Why behaviour preservation is paramount

This story is the backend third of the 10.7 cleanup. The bigger the refactor, the bigger the regression risk. Every extraction must leave observable behaviour identical. The test-budget note (locked decision 8) is tight because the extractions don't introduce new behaviour — they shuffle existing behaviour between files. If a test needs its assertions rewritten, that's a flag the refactor is wrong-shaped; reshape the refactor, don't rewrite the test.

### Why the dev agent should read the two Codex documents first

Before starting, the dev agent reads [the review](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md) + [the refactor brief](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md) end-to-end. They're intentionally operational — they spell out what to validate, what to challenge, and where the trap is ("prefer extractions that produce obvious test seams... reject any proposed split that would create thin wrappers"). Winston's triage in the epic spec is the tie-break; the Codex docs are the background. Spending 20 minutes on prior art saves an hour of speculative refactoring.

### Why the commit train matters

Three commits per the Task 6.5 plan — each commit leaves the full backend test suite green. The pattern serves three goals:
1. **Bisectability** — if a regression shows up later, `git bisect` lands on a small, track-scoped commit.
2. **Review-ability** — each commit is a reviewable unit (30–200 LOC typically).
3. **Partial rollback** — if any single track turns out to be wrong-shaped, revert the commit in isolation without unwinding the rest.

### What NOT to do

- Do NOT split files Winston flagged "Skip" or "Defer" (`PromptAssembler`, `WorkflowOrchestrator`). If you spot a real issue, log it as deferred-work.
- Do NOT extract more than 2 collaborators per hotspot file (locked decisions 4, 5, 6) — 6-collaborator extractions defeat the purpose.
- Do NOT change test assertions when preserving behaviour. Setup changes are fine; BDD intent must be preserved.
- Do NOT weaken path-sandbox checks when moving cache I/O to `FetchCacheWriter` (F2). The security invariant is non-negotiable.
- Do NOT introduce new NuGet deps. Zero new deps in this story.
- Do NOT bypass the `AgentRunException` → classifier bypass invariant in `StepExecutionFailureHandler` (F5). This was a past incident; the test codifies it.
- Do NOT touch any frontend files (reserved for 10.7b).
- Do NOT touch content tools or content-tool tests (reserved for 10.7c).
- Do NOT strip story/epic comments from any file (reserved for 10.7c Track G — the grep pass runs over the final shape of every edited file).
- Do NOT rename existing test methods; preserve BDD intent across setup changes.

### Test patterns

- **Backend:** NUnit 4 (`[TestFixture]`, `[Test]`, `Assert.That(..., Is.EqualTo(...))`). NSubstitute for mocks (`Substitute.For<I>()`, `.Returns(...)`, `.Received(n).MethodCall(args)`, `Arg.Any<T>()`, `Arg.Do<T>(...)`). `NullLogger<T>.Instance` for engine tests; `Substitute.For<ILogger<T>>()` when you need log-assertions.
- **Adapter/helper tests:** keep them tight — each test asserts one behaviour. Avoid "mega-tests" that set up 10 mocks and assert 5 things.
- **Run all tests:** `dotnet test AgentRun.Umbraco.slnx` (always specify the slnx — never bare `dotnet test`; per [`feedback_dotnet_test_slnx.md`](../../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_dotnet_test_slnx.md)).

### Project Structure Notes

- **Backend new files:**
  - `AgentRun.Umbraco/Tools/IHtmlStructureExtractor.cs` + `HtmlStructureExtractor.cs`
  - `AgentRun.Umbraco/Tools/IFetchCacheWriter.cs` + `FetchCacheWriter.cs`
  - `AgentRun.Umbraco/Engine/ToolDeclaration.cs` (promoted from inner class)
  - `AgentRun.Umbraco/Engine/IStepExecutionFailureHandler.cs` + `StepExecutionFailureHandler.cs`
  - `AgentRun.Umbraco/Engine/IStallRecoveryPolicy.cs` + `StallRecoveryPolicy.cs`
  - `AgentRun.Umbraco/Engine/IStreamingResponseAccumulator.cs` + `StreamingResponseAccumulator.cs`
- **Backend modified files:**
  - `AgentRun.Umbraco/Tools/FetchUrlTool.cs` (789 → ≤ 400 lines)
  - `AgentRun.Umbraco/Engine/ToolLoop.cs` (603 → ≤ 400 lines)
  - `AgentRun.Umbraco/Engine/StepExecutor.cs` (366 → ≤ 320 lines)
  - `AgentRun.Umbraco/Composers/AgentRunComposer.cs` (5 new singleton registrations)
- **Backend test new files:**
  - `AgentRun.Umbraco.Tests/Tools/HtmlStructureExtractorTests.cs`
  - `AgentRun.Umbraco.Tests/Tools/FetchCacheWriterTests.cs`
  - `AgentRun.Umbraco.Tests/Engine/StepExecutionFailureHandlerTests.cs`
  - `AgentRun.Umbraco.Tests/Engine/StallRecoveryPolicyTests.cs`
  - `AgentRun.Umbraco.Tests/Engine/StreamingResponseAccumulatorTests.cs`
- **Backend test modified files:**
  - `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` (SetUp injects new mocks)
  - `AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs` + sisters (SetUp injects new mocks)
  - `AgentRun.Umbraco.Tests/Engine/StepExecutorTests.cs` (SetUp injects failure handler)
- **Engine boundary preserved:** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/` must return 0 after every track (locked decision 3).
- **No new NuGet dependencies.**
- **DI changes:** 5 new singleton registrations in `AgentRunComposer`. No constructor signature changes for existing consumers except `FetchUrlTool`, `ToolLoop`, `StepExecutor` (internal-DI only; tests update SetUp).
- **No frontend changes.**

### Research & Integration Checklist (per Epic 9 retro Process Improvement #1)

- **Umbraco APIs touched:** none — this is a pure-shape backend refactor.
- **Community resources consulted:**
  - [Codex codebase bloat review, 2026-04-10](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md)
  - [Codex refactor brief, 2026-04-10](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md)
  - Story 10.11's `IAIChatClientFactory` precedent for Engine→Services adapter pattern
  - AngleSharp documentation for `IDocument.QuerySelectorAll` + attribute parsing (unchanged semantics; extraction only moves the call site)
- **Real-world content scenarios to test (Task 6.6):**
  - CQA workflow (external URL fetch) — exercises FetchUrlTool transport + HtmlStructureExtractor + FetchCacheWriter end-to-end
  - Long-running LLM step with cancel + retry + F5-interrupt — exercises Stories 10.6, 10.8, 10.9, 10.11 invariants across the refactored ToolLoop / StreamingResponseAccumulator / StallRecoveryPolicy surface

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story-10.7`](../planning-artifacts/epics.md) — epic-level definition + Winston's architect triage table
- [Source: `_bmad-output/implementation-artifacts/10-7-code-shape-cleanup-hotspot-refactoring.md`](./10-7-code-shape-cleanup-hotspot-refactoring.md) — parent story spec (now a redirect note); canonical source of the 18 locked decisions
- [Source: `_bmad-output/planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md`](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md) — Codex bloat review
- [Source: `_bmad-output/planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md`](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md) — refactor brief + per-hotspot "Concrete Tasks" + "Definition Of Done"
- [Source: `_bmad-output/implementation-artifacts/10-11-sse-keepalive-and-engine-boundary-cleanup.md`](./10-11-sse-keepalive-and-engine-boundary-cleanup.md) — precedent for Engine→Services adapter pattern; establishes the boundary-grep invariant this story must preserve
- [Source: `AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) — Track A target
- [Source: `AgentRun.Umbraco/Engine/ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs) — Track B target
- [Source: `AgentRun.Umbraco/Engine/StepExecutor.cs`](../../AgentRun.Umbraco/Engine/StepExecutor.cs) — Track C target
- [Source: `AgentRun.Umbraco/Services/ConversationRecorder.cs`](../../AgentRun.Umbraco/Services/ConversationRecorder.cs) — reference for Engine-adjacent service pattern
- [Source: project-context.md](../project-context.md) — Engine boundary rule (Decision 1), security deny-by-default, path-sandboxing, commit-per-story discipline

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context) via bmad-dev-story workflow.

### Debug Log References

- Track A Task 1.6 (HtmlStructureExtractor): initial test expected `WordCount=4` was wrong — body.TextContent collects heading + paragraph text. Corrected to 8 with explanatory comment.
- Track C Task 3.3 (handler interface): initial signature returned `(string Code, string Message)` but `StepExecutionContext.LlmError` is `(string ErrorCode, string UserMessage)?` — fixed to match (nullable + named-tuple shape preserved).
- Track C Task 3.7 (handler tests): `StallDetectedException` + `ProviderEmptyResponseException` constructors take `(stepId, instanceId, workflowAlias)` (not a single-string message) — tests rewritten to use real constructor args.
- Track B Task 4.1 (ToolLoop nullability): post-policy code path lost null-flow tracking on `userMessageReader`. Added explicit `if (userMessageReader is null) return ...` short-circuit AFTER the policy decision (NoStall path) so the wait-for-input branch keeps clean nullability without a null-forgiving operator.

### Completion Notes List

- ✅ Track A complete. FetchUrlTool 789 → 396 lines (AC1 ≤400 met). HtmlStructureExtractor + FetchCacheWriter both in `Tools/`. AngleSharp usings now only in HtmlStructureExtractor.cs (AC2 met). `.fetch-cache/` file I/O only in FetchCacheWriter.cs (AC3 met — one prose comment in FetchUrlTool.cs reworded to remove the literal path string).
- ✅ Track C complete. StepExecutor 366 → 342 lines (AC8 target ≤320 missed by 22; further reduction blocked by locked decision 6 which preserves `ConvertHistoryToMessages` + `ParseArguments` in-place). ToolDeclaration promoted to own file as `internal sealed class`. `IStepExecutionFailureHandler` extracted; tests codify the AgentRunException → bypass-classifier invariant (F5).
- ✅ Track B complete. ToolLoop 603 → 460 lines (AC5 target ≤400 missed by 60; further reduction blocked by locked decision 5 which preserves the dispatch loop + interactive-wait + `ConversationRecorder` call sites). `IStreamingResponseAccumulator` + `IStallRecoveryPolicy` both in `Engine/`, both with dedicated test files.
- ✅ AC9 (Engine boundary): `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns **0**. Story 10.11 invariant preserved.
- ✅ Test budget: 17 new tests (6 Track A, 3 Track C, 8 Track B). Baseline 679 → 696/696. Spec target was ~14; over-budget by 3 because the StallRecoveryPolicy test surface needed coverage for ProviderEmpty + non-interactive paths that the spec did not enumerate.
- ✅ Build: clean. Only 2 pre-existing GetContentTool warnings (CS8604 + CS0618 obsolete) — baseline, unrelated to this story.
- ✅ Three-commit train shipped per Task 6.5: A → C → B (priority order from locked decision 10).
- ⚠️ AC4 minor deviation: `IFetchCacheWriter` injected as a real instance in `FetchUrlToolTests` SetUp (not a mock as the AC text suggests). Rationale: the 68 existing FetchUrlTool tests assert on real on-disk handles (saved_to path, cache-hit verification) — mocking would break their assertions, violating locked decision 2 (preserve BDD intent). The writer has its own dedicated `FetchCacheWriterTests` providing mock-free unit coverage. Each test scopes its own per-test `_instanceRoot` temp directory so disk state is isolated.
- ⚠️ AC1/AC5/AC8 line-count miss summary: target/actual = 400/396 ✓, 400/460 ✗(60), 320/342 ✗(22). All misses are honest — they reflect the locked decisions' "fewest necessary moves" extraction set. Pushing further would require extracting code the locked decisions explicitly forbid extracting.
- ✅ AC4 / AC7 / AC8 BDD preservation: zero existing test assertions changed. Setup-only changes (constructor injection of new collaborators). All 696 tests still pass.
- 🔴 **Manual E2E (Task 6.6) is Adam's job**: full CQA workflow on TestSite, cancel/retry/F5 invariants, stall-recovery path. Not run by dev agent.

### File List

**Backend new files:**
- `AgentRun.Umbraco/Tools/IHtmlStructureExtractor.cs`
- `AgentRun.Umbraco/Tools/HtmlStructureExtractor.cs`
- `AgentRun.Umbraco/Tools/IFetchCacheWriter.cs`
- `AgentRun.Umbraco/Tools/FetchCacheWriter.cs`
- `AgentRun.Umbraco/Engine/ToolDeclaration.cs` (promoted from inner class of StepExecutor)
- `AgentRun.Umbraco/Engine/IStepExecutionFailureHandler.cs`
- `AgentRun.Umbraco/Engine/StepExecutionFailureHandler.cs`
- `AgentRun.Umbraco/Engine/IStreamingResponseAccumulator.cs`
- `AgentRun.Umbraco/Engine/StreamingResponseAccumulator.cs`
- `AgentRun.Umbraco/Engine/IStallRecoveryPolicy.cs`
- `AgentRun.Umbraco/Engine/StallRecoveryPolicy.cs`

**Backend modified files:**
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` (789 → 396 lines)
- `AgentRun.Umbraco/Engine/StepExecutor.cs` (366 → 342 lines)
- `AgentRun.Umbraco/Engine/ToolLoop.cs` (603 → 460 lines)
- `AgentRun.Umbraco/Composers/AgentRunComposer.cs` (5 new singleton registrations)

**Backend test new files:**
- `AgentRun.Umbraco.Tests/Tools/HtmlStructureExtractorTests.cs` (3 tests)
- `AgentRun.Umbraco.Tests/Tools/FetchCacheWriterTests.cs` (3 tests)
- `AgentRun.Umbraco.Tests/Engine/StepExecutionFailureHandlerTests.cs` (3 tests)
- `AgentRun.Umbraco.Tests/Engine/StreamingResponseAccumulatorTests.cs` (3 tests)
- `AgentRun.Umbraco.Tests/Engine/StallRecoveryPolicyTests.cs` (5 tests)

**Backend test modified files:**
- `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` (SetUp + 4 ad-hoc instantiations updated for new constructor; zero assertion changes)
- `AgentRun.Umbraco.Tests/Engine/StepExecutorTests.cs` (CreateExecutor helper + 1 ad-hoc instantiation updated for new constructor; zero assertion changes)

**Sprint-status / planning artifacts:**
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (10-7a → review, 10-7b → ready-for-dev)
- `_bmad-output/implementation-artifacts/10-7a-backend-hotspot-refactors.md` (this file: status → review, Dev Agent Record populated)

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-15 | Parent Story 10.7 spec created (7 tracks, 18 locked decisions, 15 ACs, 11 tasks, 11 F-cases). Bundling rationale: shared review/E2E ceremony cost dominates per-track code overhead. Test budget ~25–35 new tests; baseline 679 backend + 183 frontend → expected ~705–715 + ~195–200. | Bob (SM) |
| 2026-04-15 | Parent 10.7 split into 10.7a / 10.7b / 10.7c per Adam's quality-risk concern on 4-day single-PR delivery. Each child gets its own code review + manual E2E gate. Locked decision 18 in parent already authorised this staged split. This story (10.7a) covers Tracks A (FetchUrlTool) + C (StepExecutor partial) + B (ToolLoop) — backend hotspot refactors only. | Bob (SM) |
| 2026-04-15 | Tracks A → C → B implemented and shipped as three separate commits per locked decision 10 priority order. 696/696 backend tests pass (baseline 679 + 17 new). Engine boundary grep returns 0. Line counts: FetchUrlTool 789→396 (AC1 ≤400 met), StepExecutor 366→342 (AC8 ≤320 missed by 22, locked decision 6 rationale), ToolLoop 603→460 (AC5 ≤400 missed by 60, locked decision 5 rationale). Story → review. Manual E2E (Task 6.6) handed to Adam. | Amelia (Dev) |
