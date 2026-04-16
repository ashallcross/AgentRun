# Story 10.13: Marketplace Polish & Provider Robustness

Status: done

**Depends on:** 10.12 (first-run experience, done — shipped billing patterns and `ProviderEmptyResponseException`), 10.7b (frontend refactor, done — shipped `run.finished` status preservation), 10.4 (Apache 2.0 license, done)

**Branch:** `feature/epic-10-continued` (all Epic 10 work; merge to `main` follows this story, then tag `v1.0.0-rc.1`)

> **Final Epic 10 story before v1.0.0-rc.1 cut and Umbraco Marketplace listing submission.** Three small-but-load-bearing polish items that close the gaps left by 10.7b and 10.12, plus one information-disclosure fix that matters once an Umbraco admin (not just a beta tester) is reading error responses. Deliberately scoped tight — anything else gets fast-followed in 1.0.1.

## Story

As a developer shipping AgentRun to the Umbraco Marketplace for public evaluation,
I want provider errors to classify correctly even when wrapped in SDK-specific exception types, cancels to produce chat confirmation on all paths (not just mid-run cancels), and endpoint error responses to surface sanitised messages to administrators while preserving full diagnostic detail in server logs,
So that the first impression for judges, early adopters, and real-world admins is "this handles failure modes like a mature package" rather than "this swallows errors silently or leaks paths."

## Context

**UX Mode: N/A — engine robustness + frontend polish + endpoint hygiene. No new UI affordances.**

**Verification done before scoping (see Dev Notes for verification trace):**

- Track C (chat cursor flash) — **already shipped in 10.7b Track F, commit c647486**. Removed from scope.
- Track A empty-completion guard — **already shipped in 10.12** (`ProviderEmptyResponseException` in `ToolLoop` first-turn guard). Remaining gap is inner-exception walking in `LlmErrorClassifier` plus real-fixture tests against OpenAI/Azure error shapes.
- Track B Case A (`run.finished` hardcoding "Completed") — **already shipped in 10.7b** (`instance-detail-sse-reducer.ts` preserves Failed/Cancelled, no longer appends misleading "Workflow complete."). Remaining gap is the *positive* confirmation message on terminal non-Completed transitions.
- Track B Case B (Pending-cancel no chat confirmation) — still broken. `_onCancelClick` never appends to chat.
- Track D (endpoint `ex.Message` leak) — confirmed two sites: `ExecutionEndpoints.cs:287` and `:489`. No prior work on this.

## Acceptance Criteria (BDD)

### AC1: LlmErrorClassifier walks InnerException chain

**Given** a provider SDK throws a `ProviderSdkException` whose `InnerException` is an `HttpRequestException` with status 402
**When** `LlmErrorClassifier.Classify(outerException)` runs
**Then** the classifier walks the `InnerException` chain (depth-first, up to 5 levels) and classifies based on the deepest recognised exception type
**And** returns `("billing_error", "The AI provider rejected the request due to billing or quota limits...")`
**And** a direct `HttpRequestException` at the top level continues to classify identically to current behaviour (no regression)

### AC2: LlmErrorClassifier unit-tested against real OpenAI and Azure OpenAI error shapes

**Given** captured real-JSON error responses from OpenAI (billing, auth, rate-limit) and Azure OpenAI (quota, auth, rate-limit) stored as test fixtures
**When** `LlmErrorClassifierTests` runs
**Then** each fixture is wrapped in an `HttpRequestException` matching the provider SDK's actual exception shape and fed through `Classify`
**And** each fixture produces the expected error code (`billing_error` / `auth_error` / `rate_limit`) — no "provider_error" catch-alls for these known shapes
**And** fixtures live under `AgentRun.Umbraco.Tests/Fixtures/provider-errors/` with filename + provenance comment in each test

### AC3: Pending-instance cancel produces chat confirmation

**Given** an instance in `Pending` status (never started, no SSE stream active)
**When** the user clicks Cancel and the endpoint returns 200
**Then** `"Run cancelled."` is appended to `_chatMessages` as a `system`-role message
**And** the input placeholder transitions to `"Run cancelled."` (pre-existing from 10.9)
**And** if the cancel POST fails (non-200), no chat append occurs — user sees the existing console.warn path

### AC4: Terminal non-Completed run.finished appends positive confirmation

**Given** an instance is running and the backend emits `run.finished` with `status === "Cancelled"` or `"Failed"` or `"Interrupted"`
**When** the SSE reducer's `applyRunFinished` processes the event
**Then** a `system`-role chat message is appended: `"Run cancelled."` / `"Run failed."` / `"Run interrupted."` respectively
**And** the existing `"Workflow complete."` append on Completed status is preserved unchanged
**And** an unrecognised status string falls through to the out-of-order-finalisation defence (preserves prior terminal state) — no chat append in that path

### AC5: Endpoint ErrorResponse messages are sanitised for client, full detail logged server-side

**Given** an exception reaches one of the two identified leak sites ([ExecutionEndpoints.cs:287](AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L287) — `retry_recovery_failed`; [ExecutionEndpoints.cs:489](AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L489) — SSE `execution_error`)
**When** the catch block runs
**Then** the full exception (including `ex.Message`, stack, and any inner exceptions) is logged via `ILogger.LogError(ex, ...)` with structured fields (`InstanceId`, `WorkflowAlias`, `StepId` where available)
**And** the `ErrorResponse.Message` / SSE payload message returned to the client is a fixed sanitised string:
  - 287: `"Failed to prepare conversation for retry. Check server logs for details."`
  - 489: `"Workflow execution failed. Check server logs for details."`
**And** `ErrorResponse.Error` (machine-readable code) is unchanged — the contract for programmatic handling stays intact
**And** all `LlmError`-path messages (already classified user-safe via `LlmErrorClassifier`) are unaffected — those pass through unchanged

### AC6: No regression on existing error classification

**Given** the existing suite of `LlmErrorClassifierTests` and `ToolLoopTests` covering billing/auth/rate-limit/timeout/stall paths
**When** the Track A changes land
**Then** every pre-existing test passes unchanged
**And** the `ProviderEmptyResponseException` first-turn empty-completion path (Story 10.12) is preserved — no regression in the `ToolLoop` empty-turn branch

## What NOT to Build

- Do NOT add a Delete button UI — proper home is a post-launch instance-list actions column revamp, not 10.13
- Do NOT wire `MaxOutputTokens` through `IToolLimitResolver` — that's Story 9.6.1, post-launch
- Do NOT fix the abrupt-shutdown stranded-`Running` bug — pre-existing, low occurrence, documented in beta known-issues
- Do NOT suppress Umbraco.AI middleware log noise — upstream issue filed, out of our control
- Do NOT rewrite the Retry-409 generic message — low-priority polish, ships in 1.0.1 if users complain
- Do NOT add Gemini-specific classifier patterns — Gemini is not an official Umbraco.AI provider; OpenAI + Azure OpenAI are the targets
- Do NOT touch Track C (chat cursor) — already done in 10.7b Track F, verify only
- Do NOT refactor `LlmErrorClassifier` into an instance class with DI — keep it as the existing pure static function (locked decision from Epic 7 retro)

## Failure & Edge Cases

- **InnerException chain is circular** — depth-first walk must cap at 5 levels to prevent infinite recursion if any SDK produces self-referential exception graphs. At cap, fall through to top-level message-based classification.
- **InnerException is null on a non-`HttpRequestException` top-level exception** — walk terminates, classifier falls through to message-based patterns on the top-level exception. No behavioural change from today.
- **OpenAI/Azure SDK wraps `HttpRequestException` but strips the `StatusCode`** — fixture tests must cover this: the wrapped exception may have `StatusCode = null` despite a clear billing/auth signal in the message. Message-based fallback must catch these.
- **Cancel POST race: user clicks Cancel, server returns 200, but by the time `_onCancelClick` resolves the instance has transitioned to Running (concurrent start)** — append the chat message anyway. The badge/placeholder will reflect the current state; the chat "Run cancelled." is an audit of the user's action, not a state assertion.
- **Cancel POST returns 409 `already_running` (user double-clicked)** — `cancelInstanceAction` returns `kind: "failed"`; no chat append per AC3. User already sees console.warn. Idempotency: double-click must not produce two "Run cancelled." chat entries.
- **`run.finished` arrives with `status === "Pending"` (impossible per protocol, but defensive)** — unrecognised status falls through to the out-of-order-finalisation defence; no chat append. Log at Warn for diagnostic trail.
- **`run.finished` arrives twice for the same instance (SSE replay after reconnect)** — the second fire sees the instance already terminal; reducer's existing preservation logic dominates; no duplicate chat append because the terminal transition already happened. Covered by existing 10.7b tests but re-assert.
- **`LogError` itself throws (logger configuration pathology)** — let it propagate; do not add a nested try/catch. The client-sanitised message path is the primary contract, and an `ILogger` that can't log is a hard failure worth surfacing.
- **Deny-by-default on sanitisation:** any new endpoint added post-10.13 that surfaces `ex.Message` to the client is a regression. Add a linter note or code-review checklist item, but don't build framework scaffolding for it — the project has 2 leak sites today and enumerating them by hand is fine.

## Tasks / Subtasks

- [x] Task 1: Track A — `LlmErrorClassifier` inner-exception walk (AC: #1, #6)
  - [x] 1.1: Extract current `Classify` logic into a private `ClassifySingle(Exception ex)` method that returns the same tuple type.
  - [x] 1.2: Rewrite public `Classify` to walk `InnerException` depth-first up to 5 levels, returning the first `ClassifySingle` result that is NOT the generic catch-all `("provider_error", ...)`. If every level returns the catch-all, return the top-level result (preserves current behaviour for unknown shapes).
  - [x] 1.3: Preserve the `OperationCanceledException` null-return short-circuit at the top level — cancellation safety net must not be masked by inner exceptions.
  - [x] 1.4: Unit-test the walk: 2-level wrap with inner `HttpRequestException` 402 → `billing_error`; 5-level wrap (cap edge) → inner resolved; 6-level wrap (cap exceeded) → top-level classification; circular reference → terminates at cap.
  - [x] 1.5: Regression-assert: all existing `LlmErrorClassifierTests` pass unchanged.

- [x] Task 2: Track A — Real-fixture tests (AC: #2, #6)
  - [x] 2.1: Create `AgentRun.Umbraco.Tests/Fixtures/provider-errors/` folder.
  - [x] 2.2: Capture error fixtures. Use `dotnet run` against a real OpenAI key with expired credit / invalid key / aggressive rate-limit to capture the raw HTTP response body. Same for Azure OpenAI. Save as `.json` files with a sibling `.txt` provenance note (date, provider, scenario).
    - **Alternative if Adam can't burn credits:** use documented OpenAI/Azure error response examples from their public API docs — acceptable for classifier testing since we're matching on shape, not live behaviour. **Used this path** — 6 fixtures sourced from public docs, single `PROVENANCE.md` documents source + per-file mapping (cleaner than per-file `.txt`).
  - [x] 2.3: Add fixture-driven tests to `LlmErrorClassifierTests.cs`: load each `.json`, wrap in `HttpRequestException` matching the SDK's actual shape (`Message` contains the JSON body per Umbraco.AI provider pattern), assert classification.
  - [x] 2.4: Minimum coverage: OpenAI billing + auth + rate-limit; Azure OpenAI quota + auth + rate-limit. Anthropic fixtures optional (already well-covered by existing unit tests). Three test variants per fixture: with-status, without-status (SDK strips StatusCode), wrapped in generic SDK exception (inner-walk path).

- [x] Task 3: Track B — Pending-cancel chat append (AC: #3)
  - [x] 3.1: In `_onCancelClick`, after the successful `cancelInstanceAction` call and before `_loadData()`, check `result.kind === "ok"` (corrected from spec's `"success"` — actual discriminant per `instance-detail-actions.ts`) and append `{ role: "system", content: "Run cancelled.", timestamp }` via `_appendMessage`. Conflict (409 — already terminal) is intentionally silent so we don't lie about state.
  - [x] 3.2: Idempotency guard via shared `shouldAppendTerminalLine` helper in `instance-detail-helpers.ts` — same predicate the reducer uses, so a race between the AC3 handler path and the AC4 reducer path produces exactly one chat line.
  - [x] 3.3: Test: chat-panel test infrastructure does not mount the component (Bellissima import-map dependency, see existing comment at chat-panel test line 14-16). Followed established pattern: tested the chat-append decision via `shouldAppendTerminalLine` helper (5 cases) + the dedupe interplay via reducer integration test "run.finished after _onCancelClick AC3 append does NOT duplicate". Wiring is mechanical (`if (guard) _appendMessage(...)`).

- [x] Task 4: Track B — Reducer terminal message append (AC: #4, #6)
  - [x] 4.1: `applyRunFinished` now consults shared `terminalChatLine(status)` helper for the chat text. Returns `"Run cancelled."` / `"Run failed."` / `"Run interrupted."` / `"Workflow complete."`. Unrecognised statuses return `null` and the reducer skips append (out-of-order-finalisation defence preserved).
  - [x] 4.2: Completed branch unchanged in observable behaviour — `terminalChatLine("Completed")` still returns "Workflow complete.", append still happens.
  - [x] 4.3: Idempotency via `shouldAppendTerminalLine` — last-message dedupe protects against SSE replay AND against the AC3/AC4 race (AC3 appends optimistically; AC4 sees the same string and skips).
  - [x] 4.4: Tests added — `run.finished with Cancelled appends 'Run cancelled.'`, same for Failed/Interrupted, replay does not double-append, post-AC3-append does not duplicate. Existing Completed/Cancelled/Failed tests pass unchanged.

- [x] Task 5: Track D — Endpoint error sanitisation (AC: #5, #6)
  - [x] 5.1: `ExecutionEndpoints.cs:287` — replaced `$"Failed to prepare conversation for retry: {ex.Message}"` with the fixed sanitised string `"Failed to prepare conversation for retry. Check server logs for details."`. Existing `_logger.LogError(ex, ...)` already includes structured `WorkflowAlias`/`InstanceId`/`StepId` fields (extended logger format string from `{InstanceId}/{StepId}` to `{WorkflowAlias}/{InstanceId}/{StepId}` for consistency with the AC5 spec).
  - [x] 5.2: `ExecutionEndpoints.cs:489` — `_logger.LogError(ex, "Execution failed for instance {InstanceId} of workflow {WorkflowAlias}", ...)` already existed at the catch-block top; the leak was only on the `EmitRunErrorAsync` payload. Replaced `ex.Message` with `"Workflow execution failed. Check server logs for details."`. Comment in source explains why classified-LLM messages flow through their own emit path with safe text.
  - [x] 5.3: `grep "ex\.Message|exception\.Message" AgentRun.Umbraco/Endpoints/` post-fix returns only the in-source comment reference — confirmed the two leak sites were the only ones.
  - [x] 5.4: `WorkflowOrchestrator.EmitRunErrorAsync` call passes `errorMessage = context.LlmError?.UserMessage ?? "Step '{step.Name}' failed"` — `UserMessage` originates from `LlmErrorClassifier` and is safe by construction. No change to that call.
  - [x] 5.5: Updated `Retry_OnFailed_WipeFails_Returns409AndReleasesClaim` to assert sanitised message + verify `_logger.Received(1).Log(LogLevel.Error, ..., Arg.Is<Exception>(e => e.Message.Contains("archive conversation file collision")), ...)`. Added new `Start_ExecutionFailure_EmitsSanitisedSseErrorAndLogsFullException` that drives a real SSE flow via `AttachHttpContext()`, throws `InvalidOperationException("/private/var/folders/secret-path/...")` from the orchestrator, reads back the MemoryStream-backed Response.Body, and asserts the sentinel leak does NOT appear while the sanitised message DOES.

- [x] Task 6: Build verification
  - [x] 6.1: `dotnet test AgentRun.Umbraco.slnx` — **739/739** (baseline 715, +24 tests).
  - [x] 6.2: `cd AgentRun.Umbraco/Client && npm test` — **243/243** (baseline 235, +8 tests).
  - [x] 6.3: Build clean — `npm run build` produced bundles without errors; `dotnet test` build passed with only the two pre-existing GetContentTool warnings (CS8604 line 286, CS0618 line 486 — flagged in 10.7c).
  - [x] 6.4: Engine boundary grep — `rg "^using Umbraco\." AgentRun.Umbraco/Engine/` returns zero matches.

- [x] Task 7: Code review
  - [x] 7.1: Invoke `bmad-code-review` with the three review layers (Blind Hunter, Edge Case Hunter, Acceptance Auditor). Findings triaged: 4 patches, 5 deferred, ~28 dismissed.
  - [x] 7.2 (extension, 2026-04-16): **Manual E2E Task 8.4 surfaced an AC5-adjacent path leak.** `AgentFileNotFoundException` carried the full canonical filesystem path (e.g. `/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco.TestSite/...`) into the `run.error` SSE payload via the `AgentRunException` engine-domain bypass at `StepExecutionFailureHandler.cs:17`. Spec AC5 explicitly named only the two `ExecutionEndpoints` leak sites; this one is the same class of leak through a different path. Audited every `AgentRunException` subclass message for similar leaks — only `AgentFileNotFoundException` was leaking. Patched: exception now carries the workflow-relative path (`agents/scanner.md`); full canonical path stays in the `PromptAssembler` ILogger.LogError. Added a leak-guard assertion in `PromptAssemblerTests.ThrowsAgentFileNotFoundException_WhenAgentFileMissing` that fails if the workflow folder absolute path appears in the message.
    - P1 — `LlmErrorClassifier.Classify` now enumerates `AggregateException.InnerExceptions` siblings in a BFS walk (queue + shared depth cap + shared `seen` set). Task-based async failures no longer misclassify when index 0 is a generic wrapper and the billing/auth HRE is a later sibling.
    - P2 — `applyRunFinished` now gates chat-append on `payloadStatus` being recognised (not `nextStatus`). Unrecognised statuses log `console.warn` and return with prior terminal badge preserved but no lying "Run cancelled." stamped. Test renamed + rewritten to assert the silent + warn contract.
    - P3 — `Fixture_WrappedInGenericSdkException_InnerWalkResolves` matrix extended to 6/6: `openai-rate-limit.json` + `azure-rate-limit.json` now covered for the inner-walk path.
    - P4 — Added `_onCancelClick` dispatch-table test documenting the per-`kind` (`ok` / `conflict` / `failed`) chat-append and console.warn decisions. Conflict(409) silent-branch now pinned.
  - [x] 7.3: Re-run tests; build clean. Backend 744/744 (baseline 739, +5 from AggregateException + 2 new SDK-wrap fixture cases). Frontend 244/244 (baseline 243, +1 from P4 dispatch-table). Only pre-existing GetContentTool warnings remain.

- [x] Task 8: Manual E2E
  - [x] 8.1: **Bad-credential test per provider.** Validated for Anthropic via real-world credit exhaustion (account ran out mid-test 2026-04-16). Anthropic returned `{"error":{"message":"Your credit balance is too low to access the Anthropic API..."}}`; classifier matched on "credit" → `billing_error`; canonical sanitised message `"The AI provider rejected the request due to billing or quota limits..."` reached the chat panel. No blank screen, no raw stack trace, full exception in server log via Umbraco.AI.Core.AuditLog middleware. OpenAI/Azure variants not exercised (cost vs. value: classifier is fixture-tested for both providers; 4o-mini call succeeded end-to-end on real key, demonstrating the happy path).
  - [x] 8.2: **Pending-cancel UX test.** Pass — `"Run cancelled."` chat-panel system message + identical input-field placeholder both visible (AC3 spec mandates both). Status badge → `Cancelled`. No errors in server log.
  - [x] 8.3: **Mid-run cancel UX test.** Pass — Cancel mid-stream killed three concurrent in-flight `fetch_url` calls cleanly. Server log shows `Cancellation observed for instance ...; preserving Cancelled status`. Chat panel ends with `"Run cancelled."`, no stale "Workflow complete." leak. Badge → `Cancelled`.
  - [x] 8.4: **Failed-run UX test.** Pass after fast-follow patch. Initial run (renamed `agents/scanner.md`) leaked the absolute filesystem path `/Users/adamshallcross/Documents/Umbraco AI/...` into the chat via the `AgentRunException` engine-domain message bypass — fixed in v0.5 (see Task 7.2 extension). Re-test with patched code shows `"Agent file not found: 'agents/scanner.md'"` (workflow-relative path only); status → `Failed`; Retry button visible; input placeholder reads `"Run failed — click Retry to resume."`. Note: AC4's `"Run failed."` chat line is NOT fired for engine failures — orchestrator emits `run.error` (which renders the classified message verbatim), not `run.finished(Failed)`. Spec accepts either outcome — both prove the contract that no raw stack trace reaches the user.
  - [x] 8.5: **Endpoint sanitisation test.** Covered by unit tests with byte-level guarantees stronger than a manual click could provide — `Retry_OnFailed_WipeFails_Returns409AndReleasesClaim` (line 287 leak site: sanitised body + logger receives full exception) and `Start_ExecutionFailure_EmitsSanitisedSseErrorAndLogsFullException` (line 489 leak site: real MemoryStream Response.Body readback proves byte-level absence of sentinel + presence of sanitised message). Manual repro requires a synthetic timestamp-collision or chmod-w fault that doesn't occur naturally; skip rationale agreed with Adam 2026-04-16.

## Dev Notes

### Verification trace for scope shrink

Before writing this spec, Winston verified the state of each originally-scoped track against actual code:

**Track C (chat cursor)** — confirmed done in [agentrun-chat-panel.element.ts:185](AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts#L185): `?is-streaming=${shouldShowCursor(i, lastIndex, msg, this.isStreaming)}`. Extracted helper `shouldShowCursor` correctly gates on message-level `msg.isStreaming`. Commit c647486 ("Story 10.7b Track F: chat cursor only flashes during active text streaming"). **Dropped from 10.13.**

**Track A empty-completion guard** — confirmed done in [ProviderEmptyResponseException.cs](AgentRun.Umbraco/Engine/ProviderEmptyResponseException.cs) + `ToolLoop` first-turn detection (Story 10.12 Task 3.2). Billing patterns also in [LlmErrorClassifier.cs:28-51](AgentRun.Umbraco/Engine/LlmErrorClassifier.cs#L28) (402, 429+billing, message-based). **Remaining gap: inner-exception walking + real-fixture tests.**

**Track B Case A (run.finished hardcoding)** — confirmed done in [instance-detail-sse-reducer.ts:338-357](AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts#L338): reducer preserves prior Failed/Cancelled status, only appends "Workflow complete." when `nextStatus === "Completed"`. **Remaining gap: positive terminal message for non-Completed transitions (currently silent).**

**Track B Case B (Pending-cancel)** — confirmed broken in [agentrun-instance-detail.element.ts:462-490](AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L462): `_onCancelClick` has no chat append. **Fix scoped.**

**Track D (endpoint leaks)** — confirmed two leak sites via grep. No prior work. **Fix scoped.**

### LlmErrorClassifier inner-exception walk — architectural lock

The walk must happen *inside* `Classify` and *before* the message-based fallback, not after. Rationale: the wrapped `HttpRequestException` carries the authoritative status code; the outer wrapper's message may be provider-specific prose that lies about the status. Example: OpenAI SDK may throw `OpenAI.Core.ApiException: The request failed` wrapping `HttpRequestException (StatusCode=402)`. Message-based fallback would see "The request failed" and miss the billing signal; status-code walk sees the 402 and classifies correctly.

Keep the function a pure static — no DI, no logger injection. The `StepExecutor` catch block already logs at the callsite with full context. (Locked decision from Epic 7 retro: "LlmErrorClassifier as pure static function — right call.")

### Endpoint sanitisation — what the `Error` code means vs what `Message` means

`ErrorResponse.Error` is machine-readable (`retry_recovery_failed`, `execution_error`, `already_running`, etc.) — programmatic callers switch on it and it must not change shape. `ErrorResponse.Message` is human-facing prose — historically we've treated it as debugging detail for internal beta users, which is why `ex.Message` interpolation crept in. For the marketplace audience, the message is admin-visible: flip it to a stable sanitised string and put the debugging detail in the log.

Do NOT introduce a new "debug detail" field on `ErrorResponse` to carry sanitised-but-detailed text. YAGNI — the log is the right destination for detail.

### Test fixture pragma

If capturing real provider errors costs real API credit and Adam doesn't want to burn it, the OpenAI and Azure OpenAI docs both publish example error response JSON in their reference pages. Those are sufficient for classifier-shape testing because we're matching JSON shape, not validating provider behaviour. Do document the provenance either way — future maintainers need to know whether a fixture is "captured from live" or "copied from docs" to update correctly.

### Reducer idempotency — why the guard matters

The frontend has two paths that can both append `"Run cancelled."`:
1. User clicks Cancel on a mid-run instance → backend emits `run.finished(Cancelled)` → reducer AC4 path fires.
2. User clicks Cancel on a Pending instance → `_onCancelClick` AC3 path fires directly.

For mid-run, the `_onCancelClick` AC3 path also fires because `result.kind === "success"` on the 200 response — and `_loadData()` may or may not race with the SSE `run.finished` event. The `lastMessage?.content === "Run cancelled."` guard ensures that even if both paths fire in quick succession, only one chat line appears.

Same principle for `run.finished` replay after SSE reconnect — the reducer must not double-append.

### Branch strategy post-10.13

Once 10.13 is green:
1. `dotnet test AgentRun.Umbraco.slnx` + `npm test` + build clean
2. Squash-or-merge of `feature/epic-10-continued` → `main` (keep story commits; merge commit, not squash — per architect's branch strategy guidance)
3. Tag merge commit `v1.0.0-rc.1`
4. `dotnet pack` + `dotnet nuget push` with `-rc.1` suffix
5. Spec + execute Story 10.5 (Marketplace Listing & Community Launch)
6. Submit Umbraco Package Awards nomination (deadline Sunday May 3rd 23:59 CET)

### References

- [Source: AgentRun.Umbraco/Engine/LlmErrorClassifier.cs — current classification logic, 69 lines]
- [Source: AgentRun.Umbraco/Engine/ProviderEmptyResponseException.cs — pattern for engine-domain exceptions to be preserved]
- [Source: AgentRun.Umbraco/Engine/ToolLoop.cs — first-turn empty-completion guard (Story 10.12 Task 3.2), assistantTurnCount tracking]
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:287 — retry_recovery_failed leak site]
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:489 — execution_error SSE leak site]
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:462-490 — _onCancelClick]
- [Source: AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts:337-367 — applyRunFinished]
- [Source: AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts:185 — shouldShowCursor (Track C verify-only)]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:128 — LlmErrorClassifier inner-exception gap]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:227-243 — Warren Buckley silent-failure report]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:307-313 — Terminal-state chat messaging gaps]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:338 — ex.Message leak in ErrorResponse]
- [Source: _bmad-output/implementation-artifacts/10-12-first-run-experience-migration-profile-provider.md — prior work on provider error surfacing]
- [Source: _bmad-output/implementation-artifacts/10-7b-frontend-instance-detail-and-chat-cursor.md — prior work on terminal status preservation + cursor fix]

## Dev Agent Record

### Implementation Plan

Three tracks, each landed in a single bounded change:

- **Track A (Engine):** Refactored `LlmErrorClassifier.Classify` to walk `InnerException` depth-first up to 5 levels, returning the first specific (non-`provider_error`) classification or falling back to top-level if every level is the catch-all. Top-level `OperationCanceledException` short-circuit preserved (cancellation safety net runs before the walk). Inner walk uses a reference-equality `seen` set as a belt-and-braces defence against circular exception graphs that some SDKs produce. Reordered message-based fallback so `IsBillingMessage` runs BEFORE the `"rate limit"` substring check — the Azure quota error body cheerfully ends with "...the default rate limit." and the old order misclassified that as a transient. Added `IsAuthMessage` helper covering OpenAI's "Incorrect API key" and Azure's "Access denied"/"invalid subscription key" patterns so stripped-StatusCode shapes still classify correctly.

- **Track B (Frontend):** Extracted two pure helpers into `instance-detail-helpers.ts`: `terminalChatLine(status)` maps run statuses to canonical chat text (Completed → "Workflow complete.", Cancelled → "Run cancelled.", Failed → "Run failed.", Interrupted → "Run interrupted.", anything else → null), and `shouldAppendTerminalLine(last, candidate)` is the single dedupe predicate. The reducer's `applyRunFinished` now consults both — preserves the existing Completed branch behaviour, adds positive confirmation for non-Completed terminal transitions, and protects against SSE replay double-fire. The element's `_onCancelClick` consults the same predicate so the Pending-cancel path optimistically appends "Run cancelled." while the AC4 reducer path can't double-stamp on the racing `run.finished(Cancelled)` event. Conflict (409) is intentionally silent — the instance was already terminal; "Run cancelled." would be a lie.

- **Track D (Endpoints):** Both leak sites now return fixed sanitised prose. The structured `_logger.LogError(ex, ...)` call already existed at the SSE catch-all (line 471-473) and just needed the message/log split; the retry catch needed both the format-string update (added `{WorkflowAlias}`) and the sanitised return. Used `Substitute.For<ILogger<ExecutionEndpoints>>()` in the test setup (replacing `NullLogger`) so the existing test that previously asserted the leak-message could be inverted to assert the sanitised-message + the logger received the full exception. Added a new SSE-flow test that drives a real MemoryStream-backed Response.Body through `AttachHttpContext` and reads back the bytes to prove the sentinel leak string never reaches the wire.

### Decisions / Notable Discoveries

1. **Spec discriminant correction.** Story Task 3.1 said `result.kind === "success"` — actual `cancelInstanceAction` returns `"ok" | "conflict" | "failed"`. Used `"ok"` and added explicit silence on `"conflict"`. Captured in Task 3.1 checkbox note.
2. **Azure quota fixture surfaced fallback ordering bug.** First test run failed because `"...the default rate limit."` triggered the `"rate limit"` substring before the `"quota"` billing check. Reordered priorities — billing now wins over rate-limit on message-based fallback. This is the correct semantic anyway (billing errors are persistent, rate-limit is transient — false-classifying as transient delays the real fix).
3. **Stripped-StatusCode case needed broader auth message coverage.** OpenAI's "Incorrect API key provided: sk-..." doesn't contain "unauthorized" or "forbidden". Added explicit pattern set covering documented OpenAI + Azure auth phrasing. Spec edge case explicitly required this ("Message-based fallback must catch these.").
4. **Frontend test fixture pattern: helpers, not DOM.** The chat-panel test file (line 14-16) already documents that DOM fixtures aren't viable in this test environment because of the Bellissima import-map dependency. Followed the established pattern: extracted the AC3 chat-append decision into a pure helper with explicit dedupe-table tests, plus a reducer integration test that simulates the AC3-then-AC4 race. Wiring (`if (guard) _appendMessage(...)`) is mechanical glue.
5. **SSE leak test uses real MemoryStream Response.Body readback.** Existing `AttachHttpContext` helper was already set up with a MemoryStream — repurposed it to read back what the SSE emitter actually wrote and assert byte-level absence of the sentinel leak string. Stronger than a logger assertion alone because it proves the bytes themselves don't contain the leak.

### Completion Notes

- Backend: 739/739 tests (baseline 715, +24 — 10 inner-walk tests, 18 fixture-driven tests minus some overlap, 1 new SSE leak test, 1 modified existing test).
- Frontend: 243/243 tests (baseline 235, +8 — terminalChatLine table, dedupe guard table, 4 new reducer terminal-transition tests, 2 idempotency tests).
- Build clean; only the two pre-existing GetContentTool warnings (CS8604 line 286, CS0618 line 486) remain — already flagged in 10.7c.
- Engine boundary grep clean (zero `using Umbraco.*` in `Engine/`).
- Manual E2E (Task 8) remains Adam's gate.

## File List

### Modified (backend)

- `AgentRun.Umbraco/Engine/LlmErrorClassifier.cs` — inner-exception walk, reordered fallback priority, IsAuthMessage helper; v0.4 added BFS+AggregateException sibling enumeration; v0.7 OCE xmldoc documents TCE exclusion, dropped dead `seen.Add(ex)` pre-seed, EnqueueInner null-sibling guard, repeated cancellation safety net inside walk
- `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — sanitised messages at lines 287 and 496 (was 489); structured-log format extended for retry path
- `AgentRun.Umbraco/Engine/AgentFileNotFoundException.cs` — v0.5 carries the workflow-relative path, not the canonical absolute path; v0.7 xmldoc points at PromptAssembler (actual log site)
- `AgentRun.Umbraco/Engine/PromptAssembler.cs` — v0.5 logs the full canonical path before throwing (ops triage retains detail)

### Modified (frontend)

- `AgentRun.Umbraco/Client/src/utils/instance-detail-helpers.ts` — `terminalChatLine`, `shouldAppendTerminalLine` helpers
- `AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts` — `applyRunFinished` consumes shared helpers; positive terminal confirmation + dedupe; v0.7 unrecognised-payload guard runs before status mutation so prior NON-terminal status is also preserved (out-of-order defence's whole point)
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` — `_onCancelClick` appends `"Run cancelled."` on `kind: "ok"` via shared dedupe guard; v0.7 conflict (409) branch logs `console.warn` for diagnostic symmetry with the failed branch (chat stays silent per spec)

### Modified (tests)

- `AgentRun.Umbraco.Tests/Engine/LlmErrorClassifierTests.cs` — 7 inner-walk tests + 18 fixture-driven test cases; v0.4 +3 AggregateException tests + 2 rate-limit SDK-wrap fixture cases; v0.7 +`MessageBased_BillingBeatsRateLimit_OrderingIsLocked` ordering pin + `InnerExceptionWalk_AggregateException_SiblingOperationCanceled_ReturnsNull` cancellation safety net inside walk; cap-edge test commentary corrected
- `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs` — `_logger` substituted (was NullLogger); existing retry-failure test now asserts sanitised message + verifies logger; new SSE-flow leak test; v0.7 `_orchestrator.Received(1)` assertion pinned so sanitisation assertions can't pass vacuously
- `AgentRun.Umbraco.Tests/Engine/PromptAssemblerTests.cs` — v0.5 leak-guard assertion in `ThrowsAgentFileNotFoundException_WhenAgentFileMissing` ensures the workflow folder absolute path never appears in the exception message
- `AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.test.ts` — 5 new tests covering terminal transitions, replay idempotency, AC3/AC4 race; v0.7 +`run.finished with unrecognised status preserves prior NON-terminal status` (out-of-order defence regardless of terminal state)
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.test.ts` — `terminalChatLine` table test + `shouldAppendTerminalLine` table test; v0.7 dispatch-table conflict-branch now asserts `logWarn: true`
- `AgentRun.Umbraco.Tests/AgentRun.Umbraco.Tests.csproj` — added `Fixtures\provider-errors\*.json` copy-to-output

### Created (test fixtures)

- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/PROVENANCE.md`
- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/openai-billing.json`
- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/openai-auth.json`
- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/openai-rate-limit.json`
- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/azure-quota.json`
- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/azure-auth.json`
- `AgentRun.Umbraco.Tests/Fixtures/provider-errors/azure-rate-limit.json`

### Review Findings

Triaged output of parallel Blind Hunter / Edge Case Hunter / Acceptance Auditor pass (2026-04-16). 4 patches, 5 deferred, ~28 dismissed.

**Patches (applied and verified green):**

- [x] [Review][Patch] `LlmErrorClassifier.Classify` ignores `AggregateException.InnerExceptions` — only walks `.InnerException` (first). Task-based async code (e.g. `Task.WhenAll`) produces `AggregateException` with multiple siblings; if index 0 is a generic wrapper and index 1 is the `HttpRequestException` with status 402, we misclassify as `provider_error`. [AgentRun.Umbraco/Engine/LlmErrorClassifier.cs:33-43] — FIXED: BFS walk with shared `seen` + depth cap enumerates siblings.
- [x] [Review][Patch] `applyRunFinished` emits prior terminal chat line when payload status is unrecognised, and never logs Warn — violates AC4 ("no chat append in that path") and Failure/Edge line 99 ("Log at Warn for diagnostic trail"). Test `run.finished with unrecognised status is silent` is self-contradictory: title says silent, body asserts `chatMessages.length === before + 1`. [AgentRun.Umbraco/Client/src/utils/instance-detail-sse-reducer.ts:335-378, instance-detail-sse-reducer.test.ts:319-332] — FIXED: gate chat-append on payloadStatus recognition, warn on unrecognised; test rewritten to match contract.
- [x] [Review][Patch] Task 2.4 matrix incomplete: `Fixture_WrappedInGenericSdkException_InnerWalkResolves` tests only 4 of 6 fixtures (missing `openai-rate-limit.json` + `azure-rate-limit.json`). Task 2.4 checkbox claims "Three test variants per fixture". [AgentRun.Umbraco.Tests/Engine/LlmErrorClassifierTests.cs:346-360] — FIXED: both rate-limit fixtures added to the `[TestCase]` attribute set.
- [x] [Review][Patch] `_onCancelClick` conflict(409) branch is not test-enforced. Dedupe guard prevents double-append in practice, but no test asserts the conflict path leaves chat untouched (AC3 idempotency / Failure line 98). [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:482-500] — FIXED: dispatch-table test covers all three `kind` branches and the AC4-race dedupe interaction.

**Deferred (logged to `deferred-work.md`):**

- [x] [Review][Defer] Marketplace UX: sanitised `ErrorResponse.Message` gives Umbraco admins no actionable signal when they can't reach server logs (shared hosting, restricted Cloud UIs). Add correlation ID tying UI message → log line. — deferred to 1.0.1 polish; not story-scoped.
- [x] [Review][Defer] `catch (InvalidOperationException ex)` at retry-recovery (ExecutionEndpoints.cs:278) is too broad — LINQ/state-guard bugs ("Sequence contains no elements") now surface as "Failed to prepare conversation for retry". Narrow to `IOException | UnauthorizedAccessException | InvalidOperationException` or use a marker type. — deferred; narrow escape vector, retry path well-covered by tests.
- [x] [Review][Defer] `LlmErrorClassifier` inner-walk cap (5) truncates silently. Deeper wrapping (Refit → Polly → HttpClient → SocketsHandler → SocketException) can exceed 5. Log once at Warn when cap hit. — deferred; add diagnostic only, not a correctness issue.
- [x] [Review][Defer] `InnerExceptionWalk_CircularChain_TerminatesAtCap` test uses reflection on private CLR field `Exception._innerException` — brittle across .NET minor versions. — deferred; asserts field presence up front, acceptable risk.
- [x] [Review][Defer] AC1 spec wording ("classifies based on the **deepest recognised** exception type") contradicts Task 1.2 + Dev Notes ("first recognised wins"). Implementation follows Task 1.2 — defensible; spec needs reword. — deferred; doc correction for 0.3 bump, not code change.

**Dismissed (not logged):** 28 findings judged noise / handled / out-of-scope: i18n concerns (no i18n infra), logger-assertion fragility (works in practice), `_loadData` wiping optimistic chat (explicitly guarded in code per 2026-04-15 E2E fix), hypothetical Exception.Message null, word-boundary `'quota'` in `'quotation'`, status-case drift, single-threaded Lit race concerns, etc.

## Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-16 | Story spec created by Winston post-verification of originally-proposed four-track scope against actual code. Track C dropped (done in 10.7b F). Track A reduced to inner-exception walk + fixture tests (empty-completion + billing patterns already done in 10.12). Track B reduced to Pending-cancel chat append + terminal message polish (Case A hardcoding already done in 10.7b). Track D scoped to two confirmed leak sites. Status: ready-for-dev. |
| 0.2 | 2026-04-16 | Amelia shipped Tracks A/B/D + Build verification. Backend 739/739 (+24), frontend 243/243 (+8). Status: review. Tasks 1-6 closed; Tasks 7 (code review) + 8 (manual E2E) remain. Two in-flight discoveries documented: (a) spec's `result.kind === "success"` corrected to `"ok"` per actual `cancelInstanceAction` discriminant; (b) message-based fallback ordering surfaced via Azure quota fixture — billing now beats rate-limit (Azure quota body trails with "...the default rate limit."), correct semantic anyway. Added `IsAuthMessage` to cover OpenAI "Incorrect API key" + Azure "Access denied"/"invalid subscription key" stripped-status shapes per AC2 edge case. |
| 0.3 | 2026-04-16 | Code review (Blind Hunter + Edge Case Hunter + Acceptance Auditor) complete. 4 patches identified, 5 deferred items logged to deferred-work.md, ~28 dismissed as noise. Tasks 7.1 done; Tasks 7.2 (apply patches) + 7.3 (re-verify) pending Adam's decision. Status unchanged: review. |
| 0.4 | 2026-04-16 | Review patches P1–P4 applied and verified. Backend 744/744 (+5), frontend 244/244 (+1). Task 7 complete. Status stays `review` — Task 8 (manual E2E) remains Adam's gate before the story can close and the `v1.0.0-rc.1` tag + marketplace listing submission can proceed. |
| 0.5 | 2026-04-16 | Manual E2E Task 8.4 (Failed-run UX) surfaced an AC5-adjacent path leak: `AgentFileNotFoundException` was emitting the absolute canonical filesystem path through the `run.error` SSE payload via the engine-domain bypass at `StepExecutionFailureHandler.cs:17`. Audit of all `AgentRunException` subclasses isolated this as the only leak. Fix: exception now carries the workflow-relative agent path; full canonical path logged via `PromptAssembler` ILogger.LogError. Test added: leak-guard assertion in `PromptAssemblerTests`. Backend 744/744 still green (existing tests covered the relative-path case). Manual E2E 8.1 (Anthropic via accidental credit exhaustion), 8.2 (Pending-cancel), 8.3 (mid-run cancel) all passed cleanly. 8.4 re-test pending Adam. 8.5 (endpoint sanitisation) remaining. |
| 0.6 | 2026-04-16 | Story → DONE. Manual E2E Task 8 closed: 8.1 validated for Anthropic (accidental credit exhaustion proved the classifier+sanitisation contract end-to-end), 8.2/8.3/8.4 passed with patched code, 8.5 covered by unit tests with byte-level guarantees (decision agreed with Adam). v1.0.0-rc.1 cut + marketplace listing submission now unblocked. Next: branch strategy per Dev Notes line 198 (squash-or-merge → main, tag, dotnet pack + push, then Story 10.5). |
| 0.8 | 2026-04-16 | Story → DONE. 2nd code-review patches verified green; Adam confirmed commit. v1.0.0-rc.1 cut + marketplace listing submission unblocked. |
| 0.7 | 2026-04-16 | Story reverted DONE → REVIEW by Adam. 2nd code-review pass run via Amelia (3 layers: Blind Hunter / Edge Case Hunter / Acceptance Auditor). Layers surfaced 9 patches / 1 decision-needed / 8 defer / 11 dismissed. Three Blind Hunter HIGH findings verified false-positive (PromptAssemblerTests assertion correct; ExecutionEndpoints OCE handled by earlier catch with `throw;`; azure-quota wrapped fixture passes via `IsBillingMessage("quota")` inside the 429 branch). Patches applied: P1 reducer `applyRunFinished` now preserves prior status on unrecognised payload regardless of whether prior was terminal (out-of-order defence's whole point); P2 `_orchestrator.Received(1)` pinned in SSE leak test so sanitisation assertion can't pass vacuously; P3 cap-edge test commentary corrected to match `>=` semantics; P4 OCE xmldoc on `LlmErrorClassifier` documents the TCE exclusion + StepExecutor upstream filter rationale; P5 `AgentFileNotFoundException` xmldoc points at `PromptAssembler.AssemblePromptAsync` (actual log site); P6 `seen.Add(ex)` dead pre-seed dropped; P7 `EnqueueInner` skips null siblings (defensive guard for deserialised AggregateException graphs); P8 ordering-pin test `MessageBased_BillingBeatsRateLimit_OrderingIsLocked` names the billing-beats-rate-limit invariant explicitly; P9 cancellation safety net repeated inside the BFS walk so non-TCE OCE inside an AggregateException sibling returns null instead of being misclassified as provider_error. P10 Decision: conflict-409 cancel branch now logs `console.warn` (chat stays silent per spec — diagnostic symmetry with the failed branch). New tests: `MessageBased_BillingBeatsRateLimit_OrderingIsLocked`, `InnerExceptionWalk_AggregateException_SiblingOperationCanceled_ReturnsNull`, reducer `run.finished with unrecognised status preserves prior NON-terminal status`, dispatch-table conflict-warn assertions. Backend 746/746 (+2 vs 744 baseline), frontend 245/245 (+1 vs 244). 8 deferred items appended to deferred-work.md. Status: review — re-test of manual E2E touchpoints (cancel UX) recommended before flipping to DONE. |
