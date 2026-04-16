# Story 10.7c: Content-Tool DRY + Comment Hygiene + CSS Re-Format

Status: review

**Depends on:** Story 10.7a (backend hotspot refactors — must land first) AND Story 10.7b (frontend instance-detail + chat cursor — must land first). Track G (comment hygiene) explicitly requires 10.7a + 10.7b to have landed so the grep runs over the **final shape** of every edited backend + frontend file — stripping story archaeology from files that are about to be heavily modified is wasted work.
**Followed by:** Story 10.4 (open-source licence) → Story 10.5 (marketplace listing & community launch). Split from the original [Story 10.7 parent spec](./10-7-code-shape-cleanup-hotspot-refactoring.md) on 2026-04-15 per Adam's quality-risk concern on single-PR delivery.
**Branch:** `feature/epic-10-continued`
**Priority:** 10th in Epic 10 (10.2 → 10.12 → 10.8 → 10.9 → 10.10 → 10.1 → 10.6 → 10.11 → 10.7a → 10.7b → **10.7c** → 10.4 → 10.5).

> **Three tracks shipped as one cross-cutting polish story.** Covers Track E (content-tool DRY + O(n²) truncation fix) from the Epic 9 retrospective technical-debt list, Track G (comment hygiene polish pass) from Winston's architect triage, and Track H (CSS re-format closure) from the 10.7b review-defer entry. Track E is self-contained code; Track H is a narrow `.ts` style-format polish; Track G is a repo-wide `.cs` polish pass that must run LAST so it operates on the final post-refactor shape.
>
> **The goal is clearer responsibility boundaries with the fewest necessary moves — NOT maximum decomposition.** Every change must reduce reasoning load; thin one-method wrappers are explicitly rejected. Comment stripping preserves technical rationale — archaeology only. CSS re-format expands where expansion genuinely aids scanning, leaves cohesive state-matrix one-liner groups alone.

## Story

As a maintainer of AgentRun.Umbraco,
I want the content-tool parameter-parsing duplication eliminated, the O(n²) truncation loop fixed, story archaeology stripped from production code, and the 10.7b-minified CSS block in `agentrun-instance-detail.element.ts` reviewed and re-formatted where readability genuinely improves,
So that the content tools share one implementation of a load-bearing pattern, large result sets don't burn CPU on quadratic serialisation, the codebase ships to public launch without the shape of how it was built across 9 epics showing through, and the 10.7 trilogy closes with no open style-format debt carried into 10.4/10.5.

## Context

**UX Mode: N/A.** Pure-shape refactor + performance fix + comment polish + CSS whitespace polish. No observable behaviour changes. No new features, no new endpoints, no new config surface.

### Why this story was split from the parent

See [Story 10.7a §Why this story was split from the parent](./10-7a-backend-hotspot-refactors.md). This story (10.7c) is track-group 3 of 3 from the original parent spec. Track G (comment hygiene) explicitly runs AFTER 10.7a + 10.7b because:

- The comment-archaeology grep is most valuable when it runs over the **final** shape of every file. Stripping comments from `FetchUrlTool.cs` before 10.7a splits it would mean re-stripping after the split.
- Track G's commit-body bookkeeping ("ToolLoop.cs: -9 story refs, kept 5 technical") is the review point that Adam uses to sanity-check the strip wasn't over-aggressive — that signal is clearer when every file has reached its steady-state shape.
- 10.7c can land before 10.4 / 10.5 without blocking public launch; if it slips, launch can proceed with the polish delivered in a post-launch increment.

### The three tracks in this story

| Track | Target | Disposition | Why | Est. effort |
|---|---|---|---|---|
| **E** | [`Tools/ListContentTool.cs`](../../AgentRun.Umbraco/Tools/ListContentTool.cs), [`GetContentTool.cs`](../../AgentRun.Umbraco/Tools/GetContentTool.cs), [`ListContentTypesTool.cs`](../../AgentRun.Umbraco/Tools/ListContentTypesTool.cs) | DRY extract + O(n²) fix | 4 duplicated helpers; truncation loop re-serialises full JSON per iteration on responses > 256 KB | 0.5 day |
| **H** | [`Client/src/components/agentrun-instance-detail.element.ts`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts) CSS block (lines 58–173) | Review + re-format where it aids readability; close the deferred entry | 10.7b Track D minified rules to fit the ≤ 700 line budget; budget is not a persistent invariant; reformat multi-rule clusters where expansion helps, leave cohesive state-matrix one-liner groups alone | 0.2 day |
| **G** | Repo-wide `.cs` files | Strip story archaeology | ~69 story/epic refs across ~17 files; strip where the comment is pure archaeology, keep where it explains non-obvious technical rationale | 0.4 day |

### Tracks explicitly deferred to sibling stories (do not touch)

| Source / reference | Scope |
|---|---|
| **10.7a (already done)** | Backend hotspots: FetchUrlTool, ToolLoop, StepExecutor |
| **10.7b (already done)** | Frontend instance-detail + chat cursor fix |

### Architect's locked decisions (do not relitigate)

These decisions were locked in the parent story on 2026-04-15 and are preserved verbatim here. Only decisions relevant to Tracks E + G appear in this story.

**Universal (all three 10.7 child stories):**

1. **"Fewest necessary moves" is the design principle.** Any extraction that does not demonstrably reduce reasoning load must be rejected in favour of leaving the file alone. Thin one-method wrappers are explicitly forbidden. When in doubt, don't split. The dev agent should read the refactor brief's "Review Rules For The Next Agent" ([refactor brief §Review Rules](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md)) before starting.
2. **Behaviour-preserving refactor — no functional changes in this story.** Track E is a DRY extraction + algorithmic performance fix with identical outputs. Track G is a comment strip — zero code changes. Existing tests must pass without modification to assertions.
3. **Engine boundary must stay clean (Story 10.11 invariant).** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` must remain at 0 matches. This story doesn't touch `Engine/` files structurally but Track G may strip comments from engine files — the boundary-grep check remains part of DoD.

**Track E (content-tool DRY + O(n²) fix):**

4. **Track E content-tool helpers live at [`AgentRun.Umbraco/Tools/ContentToolHelpers.cs`](../../AgentRun.Umbraco/Tools/ContentToolHelpers.cs).** A `public static class ContentToolHelpers` with the four deduplicated methods. All three content tools (`ListContentTool`, `GetContentTool`, `ListContentTypesTool`) delete their local copies and delegate. No new interface, no DI — these are pure argument-parsing utilities.

5. **Track E O(n²) truncation fix uses binary search over the items list.** Current pattern at [ListContentTool.cs:125-137](../../AgentRun.Umbraco/Tools/ListContentTool.cs#L125) removes one item per iteration and re-serialises the full list each time (O(n²) on the serialisation layer; worst case 256 KB × N items of re-serialisation). Replace with binary search: `lo=0, hi=items.Count`; each iteration serialises once; `O(n log n)` serialisation cost. Applies verbatim to `ListContentTypesTool` + `GetContentTool` (and extract to a helper on `ContentToolHelpers` since all three share the pattern).

**Track G (comment hygiene):**

6. **Track G strips comments matching narrow patterns — keeps everything else.** Strip: `// Story X.Y: did Z` where Z is archaeology (a hand-wave at a past fix), `// Per code review ...`, `// Post-review ...`, `// Added in Story X.Y` when the rest of the comment is empty. Preserve: any comment explaining WHY a specific constant is chosen (e.g. the `MaxOutputTokens=32768` rationale at [StepExecutor.cs:160-174](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L160) after 10.7a), any comment citing an RFC / spec / Umbraco docs link, any comment describing a concurrency or ordering constraint, any comment warning about a Umbraco/Umbraco.AI quirk. The test: if removing the comment would confuse a reader reading the code cold, the comment stays. If the comment is pure archaeology ("this was added in 9.6 — needed for tool limits"), strip it (the git log already knows).

**Track H (CSS re-format):**

7. **Track H is a narrow, judgement-led CSS-format polish — not a bulk reformatter.** The CSS block at [`agentrun-instance-detail.element.ts:58-173`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L58) was partly minified during 10.7b Track D to fit the AC1 ≤ 700 line budget. Review every one-line and multi-selector rule cluster; expand to conventional multi-line CSS format **only where expansion demonstrably aids scanning** (e.g. a multi-property rule flattened onto one line). **Leave cohesive state-matrix one-liner groups alone** — e.g. the four `.step-item.{pending,active,complete,error} .step-icon-wrapper` rules (lines 134–137) read better as a column of parallel one-liners than as 20 lines of expanded blocks; the same applies to `.step-icon` state-colour triples, `.step-text` family, and `@keyframes spin`. Single-property utility rules (`.step-item.clickable { cursor: pointer; }`) stay one-line.

8. **Track H explicitly does NOT re-introduce a persistent line-count budget.** The ≤ 700 line budget from 10.7b AC1 was a 10.7b-specific constraint to validate the refactor shape. After Track H re-formats, the file will likely land between 700 and 720 lines — that is expected and acceptable. Reasoning load is the goal, not line count.

9. **Track H is Track-G-orthogonal.** Track G operates on `.cs` only; Track H operates on one `.ts` file. Order between H and G does not matter functionally. Track H can land before or after Track G.

**Cross-cutting:**

10. **No new NuGet or npm dependencies.** Track E adds zero deps. Track G is a comment strip only. Track H is CSS whitespace only.

11. **Test budget target: ~5 new tests.** Breakdown: Track E (~5: 4 helper tests + 1 truncation binary-search characterisation test), Track G (0 — comment hygiene), Track H (0 — CSS whitespace only; existing frontend tests must stay green to confirm no behavioural drift). Full backend + frontend suites must stay green. Preserve ALL existing tests — any test deletion must be justified in the Dev Notes as either duplicate coverage or no-longer-applicable setup.

12. **Test counts are guidelines, not ceilings.** From Epic 8+ retro. Write what the change needs; don't pad.

13. **Priority ordering inside the story: E → H → G.** Track E first (code change + tests), Track H second (narrow `.ts` CSS polish — runs inside the same PR train but before Track G so the final grep pass in G is unambiguously the last thing Adam reviews), Track G last (repo-wide `.cs` polish over the final shape). Track G must run with 10.7a + 10.7b + 10.7c Track E + Track H all landed first — the grep operates on the post-refactor shape.

## Acceptance Criteria (BDD)

### AC1: Content-tool DRY extraction — `ContentToolHelpers.cs`

**Given** the new file [`AgentRun.Umbraco/Tools/ContentToolHelpers.cs`](../../AgentRun.Umbraco/Tools/ContentToolHelpers.cs)
**When** the file is inspected
**Then** it contains a `public static class ContentToolHelpers` with the four deduplicated methods: `RejectUnknownParameters`, `ExtractOptionalStringArgument`, `ExtractOptionalIntArgument`, `ExtractRequiredIntArgument` (each method's signature must match the most-capable variant across the three source files — e.g. `ExtractOptionalIntArgument` keeps the range-check parameters from `ListContentTool`)
**And** `ListContentTool.cs`, `GetContentTool.cs`, and `ListContentTypesTool.cs` delete their local copies and call `ContentToolHelpers.XXX(...)` instead
**And** each tool retains its own `KnownParameters` HashSet (the set is tool-specific; only the *method* is shared)
**And** new test file [`ContentToolHelpersTests.cs`](../../AgentRun.Umbraco.Tests/Tools/ContentToolHelpersTests.cs) has at minimum 4 tests covering the four methods' happy + edge cases

### AC2: Content-tool O(n²) truncation fixed to O(n log n) via binary search

**Given** the refactored `ListContentTool.cs`, `ListContentTypesTool.cs`, `GetContentTool.cs` truncation paths
**When** any of the three tools produces a result > 256 KB
**Then** the truncation is computed via binary search (`lo=0, hi=items.Count`; each iteration serialises once)
**And** the binary-search implementation is extracted as a helper method on `ContentToolHelpers` (e.g. `TruncateToByteLimit<T>(IList<T> items, int limitBytes, Func<int, string> markerFor)`) so all three tools share one implementation
**And** existing content-tool tests still pass (truncation shape unchanged; only performance improved)
**And** a new test in `ContentToolHelpersTests` asserts the binary-search variant produces the same final item count as the linear variant for a representative input (characterisation test)

### AC3: Comment hygiene polish — story/epic archaeology stripped

**Given** the production `.cs` files in `AgentRun.Umbraco/` (excluding test files and `Migrations/`)
**When** `grep -rnE "^\s*//\s*(Story\s+[0-9]+\.[0-9]+[^:]*:|Per (code )?review|Post-review|Added in Story)" AgentRun.Umbraco/ --include="*.cs"` runs before and after the story
**Then** the count drops by at least 50% (baseline ~69 hits → target ≤ 35)
**And** comments explaining WHY a specific constant / invariant / quirk is chosen are preserved (e.g. `MaxOutputTokens=32768` rationale at `StepExecutor.cs` (post-10.7a line numbers) stays)
**And** the Story 10.6 + 10.8 + 10.9 + 10.11 retention/keepalive/cancellation comments in `ExecutionEndpoints.cs` are preserved where they cite RFC/spec links or explain locked-decision rationale, but pure "added in Story X" annotations with no further substance are stripped
**And** the dev agent's commit on Track G includes a 1-line note per file in the commit body summarising what kind of comments were removed (e.g. "ToolLoop.cs: stripped 9 story refs; kept 5 technical rationale comments")

### AC4: Full suite green + no new warnings + boundary preserved

**Given** the story is complete
**When** `dotnet test AgentRun.Umbraco.slnx` runs
**Then** backend tests pass (baseline whatever 10.7a + 10.7b left it → expect +5 with ~5 new tests per locked decision 11; delta TBD by dev agent)
**And** `npm test` in `AgentRun.Umbraco/Client/` passes (no frontend-logic changes in this story; count unchanged — baseline 235/235 post-10.7b per sprint-status; Track H is whitespace-only CSS so no new frontend tests expected)
**And** `dotnet build AgentRun.Umbraco.slnx` is clean — zero new warnings introduced
**And** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns **0** matches (Story 10.11 invariant preserved)

### AC5: Track H — CSS re-format closure on `agentrun-instance-detail.element.ts`

**Given** the CSS block at [`agentrun-instance-detail.element.ts:58-173`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L58) flagged by [deferred-work.md:374](./deferred-work.md) + [10-7b Review-Defer](./10-7b-frontend-instance-detail-and-chat-cursor.md) as a 10.7c candidate
**When** the Track H commit lands
**Then** every multi-property / multi-rule one-liner in the CSS block that a reader cannot scan at a glance is expanded to conventional multi-line CSS
**And** cohesive state-matrix groups (the `.step-item.{pending,active,complete,error} .step-icon-wrapper` four-liner, the `.step-icon` state-colour triple, the `.step-text` / `.step-name` / `.step-subtitle` family, `@keyframes spin`, single-property utility rules like `.step-item.clickable { cursor: pointer; }`) are preserved in their current compact form — expansion there hurts scanability, not helps
**And** manual browser verification confirms the rendered instance-detail view is visually identical pre- and post-reformat (side-by-side screenshot check is sufficient; no new frontend automated test required per locked decision 11)
**And** the Track H commit body includes a 1-line note summarising the judgement call for each cluster group touched vs. preserved (e.g. "Expanded: .completion-banner, .step-sidebar ul. Preserved: step-item state matrix, @keyframes, single-prop utilities.")
**And** the file's final line count is allowed to exceed the 10.7b AC1 ≤ 700 budget — that budget does not persist past 10.7b (locked decision 8); realistic landing is ~700–720 lines
**And** the deferred-work entry at line 374 is struck through and marked **RESOLVED in Story 10.7c Track H** with the date, mirroring the pattern used for other resolved deferred entries

## Tasks / Subtasks

### Task 1: Track E — Extract `ContentToolHelpers` + fix O(n²) truncation (AC1, AC2)

- [x] 1.1 Read the four duplicated helpers in [`ListContentTool.cs:250-299`](../../AgentRun.Umbraco/Tools/ListContentTool.cs#L250), compare against the copies in [`GetContentTool.cs:250-299`](../../AgentRun.Umbraco/Tools/GetContentTool.cs#L250) and [`ListContentTypesTool.cs:123-144`](../../AgentRun.Umbraco/Tools/ListContentTypesTool.cs#L123). Pick the most-capable variant of each method (range checks present, edge cases handled) as the version that lives in the shared helper.
- [x] 1.2 Create [`AgentRun.Umbraco/Tools/ContentToolHelpers.cs`](../../AgentRun.Umbraco/Tools/ContentToolHelpers.cs) as `public static class ContentToolHelpers`. Add the four methods: `RejectUnknownParameters`, `ExtractOptionalStringArgument`, `ExtractOptionalIntArgument`, `ExtractRequiredIntArgument`.
- [x] 1.3 Delete the local copies in the three tool files and update the call sites to `ContentToolHelpers.XXX(...)`.
- [x] 1.4 Add a `public static string TruncateToByteLimit<T>(IList<T> items, int limitBytes, Func<int, string> markerFor, JsonSerializerOptions? options = null)` method to `ContentToolHelpers`. Implement via binary search:
  1. `lo = 0, hi = items.Count`
  2. Serialise full list + marker — if ≤ limit, return.
  3. Binary-search: at each step, serialise `items.Take(mid).ToList()` + `markerFor(mid)`, compare vs limit, move `lo` or `hi`.
  4. Return the largest prefix that fits.

  **Signature deviation from spec, applied during implementation:** the method returns `(string Json, int IncludedCount)` (tuple) and takes an extra `Func<IList<T>, string> serialise` delegate. `GetContentTool` wraps its property-subset inside a larger result object, so the caller — not the helper — owns the outer JSON shape. Returning the included count lets `GetContentTool` populate its handle's `propertyCount` field and compute the `truncated` flag without a second pass. `List*` tools discard the count with `var (json, _) = ...`. The binary-search algorithm itself is unchanged.
- [x] 1.5 Replace the O(n²) loops at [`ListContentTool.cs:125-137`](../../AgentRun.Umbraco/Tools/ListContentTool.cs#L125), [`ListContentTypesTool.cs:104-115`](../../AgentRun.Umbraco/Tools/ListContentTypesTool.cs#L104), [`GetContentTool.cs:137-151`](../../AgentRun.Umbraco/Tools/GetContentTool.cs#L137) with calls to `ContentToolHelpers.TruncateToByteLimit(...)`.
- [x] 1.6 Create [`AgentRun.Umbraco.Tests/Tools/ContentToolHelpersTests.cs`](../../AgentRun.Umbraco.Tests/Tools/ContentToolHelpersTests.cs) with at least 5 tests: happy path for each of the 4 argument helpers + binary-search characterisation test (same final count as the old linear variant for representative input). Landed 8 tests (5 arg-helper coverage + 3 truncation: full-fits, binary-vs-linear characterisation, limit-smaller-than-empty-marker fallback).
- [x] 1.7 Run content-tool tests — all existing tests still pass (no behavioural change; only performance). Backend 713/713 (baseline 705 + 8 new).

### Task 2: Track H — CSS re-format closure on `agentrun-instance-detail.element.ts` (AC5)

- [x] 2.1 Read the CSS block at [`agentrun-instance-detail.element.ts:58-173`](../../AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L58) in full.
- [x] 2.2 For each rule / rule cluster, apply locked decision 7:
  - **Expanded** (3 rules): `.sidebar-divider`, `.error-state`, `.main-panel` — each had 3 distinct properties on one line; expansion makes the property list scannable without breaking a preserved group.
  - **Preserved**: state-matrix `.step-item.{pending,active,complete,error} .step-icon-wrapper` 4-liner, `.step-icon` state-colour triple, `.step-text` / `.step-name` / `.step-subtitle` / `.step-item.pending .step-name` family, `@keyframes spin`, all single-property utilities (`.step-item.clickable`, `.step-item.clickable:hover`, `.step-icon`, `.step-icon-spin`, `.completion-banner uui-button`), and the 2-prop `:host` utility.
  - **Judgement call threshold honoured:** borderline rules (e.g. the 4-prop `.step-text`) left alone per locked decision 7's explicit "`.step-text` family" preservation clause.
- [x] 2.3 Save the file. Run `npm run build` inside `AgentRun.Umbraco/Client/` — bundle rebuilds clean (20 modules, `agentrun-instance-detail.element-B1z9OojA.js` 44.31 KB).
- [ ] 2.4 Manual visual check: start TestSite (`dotnet run` from `AgentRun.Umbraco.TestSite/`), load a running instance, verify the instance-detail view renders visually identically to the pre-reformat version. Hard-refresh the browser (Cmd+Shift+R) to bypass the 10-second Umbraco manifest cache. _— Adam's gate (Task 4.6 step 5 scheduled)._
- [x] 2.5 Run `npm test` in `AgentRun.Umbraco/Client/` — 235/235 green (count unchanged from 10.7b baseline).
- [x] 2.6 Strike through the deferred-work entry at [deferred-work.md:374](./deferred-work.md) with the pattern `~~...~~ **RESOLVED in Story 10.7c Track H** (2026-04-16)`. Also strike through the 10-7b review-defer entry at [10-7b:321](./10-7b-frontend-instance-detail-and-chat-cursor.md#L321) with the same mark.

### Task 3: Track G — Comment hygiene pass (AC3)

- [x] 3.1 Run the baseline grep: `grep -rnE "^\s*//\s*(Story\s+[0-9]+\.[0-9]+[^:]*:|Per (code )?review|Post-review|Added in Story)" AgentRun.Umbraco/ --include="*.cs" | wc -l`. Baseline **61** hits across 17 files (lower than the story-spec estimate of ~69 because 10.7a/b already stripped some en-route).
- [x] 3.2 For each file with ≥ 3 hits, review each comment. Apply locked decision 6:
  - **Strip** if the comment is pure archaeology ("// Story 9.6: added tool limit resolution" on a method that's already named `ResolveToolLimit`).
  - **Keep** if the comment explains WHY a specific choice was made and a reader would be lost without it (RFC references, concurrency constraints, Umbraco.AI quirks, MaxOutputTokens=32768 rationale, AgentRunException bypassing LlmErrorClassifier).
  - **Rewrite** if the comment mixes archaeology with technical rationale — keep the rationale, drop the story ref.
- [x] 3.3 Re-run the grep — **0** hits. Well below the ≤ 35 target (50% reduction minimum per AC3). Heavy comments (MaxOutputTokens rationale, AgentRunException bypass invariant, stall-recovery post-nudge decision tree, per-instance SemaphoreSlim lifecycle) were rewritten in place to remove the story-ref lead while preserving the full technical body. Single-line archaeology comments were deleted outright.
- [x] 3.4 In the Track G commit body, include a terse per-file note: file name + count removed + what kind. See commit message.
- [x] 3.5 Run `dotnet test AgentRun.Umbraco.slnx` one final time — 713/713 green. Run `npm test` in `Client/` — 235/235 green (verified pre-Track-G, CSS whitespace doesn't affect behaviour).

### Task 4: DoD verification + commit train

- [x] 4.1 Full backend test: `dotnet test AgentRun.Umbraco.slnx` → 713/713 green (AC4).
- [x] 4.2 Full frontend test: `cd AgentRun.Umbraco/Client && npm test` → 235/235 green (count unchanged from 10.7b baseline 235).
- [x] 4.3 Build clean: `dotnet build AgentRun.Umbraco.slnx` → 0 new warnings (2 pre-existing only — CS8604 HtmlDecode nullability + CS0618 IFileService.GetTemplate obsolete).
- [x] 4.4 Engine boundary check: `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → 0 matches.
- [x] 4.5 Commit train landed:
  - Commit 1 (Track E — `cff8c1a`): `ContentToolHelpers` + binary-search truncation + tests + content-tool call-site updates.
  - Commit 2 (Track H — `51a93e0`): CSS re-format on `agentrun-instance-detail.element.ts` — three expansions, state-matrix / family / utility preserved.
  - Commit 3 (Track G — this commit): comment hygiene polish — per-file note in commit body.

  Each commit leaves the full test suite green. Each commit body ends with the standard Co-Authored-By trailer.
- [ ] 4.6 Manual E2E — _Adam's gate._ Steps retained verbatim below for the code-review / E2E pass:
  1. Start TestSite: `dotnet run` from `AgentRun.Umbraco.TestSite/`.
  2. Run the **Content Audit** workflow (Umbraco-native) end-to-end on Sonnet 4.6 against a realistic content tree (26+ nodes; use the Clean Starter Kit baseline from 9.13 + add content as needed to exceed the 256 KB truncation threshold on `list_content` or `list_content_types`).
  3. Verify: `list_content` / `get_content` / `list_content_types` tools all succeed, truncation marker appears when result exceeds 256 KB, final `run.finished: Completed`, audit-report references real content.
  4. Verify from logs that the content-tool calls completed in comparable or better time than baseline (the binary-search fix reduces CPU; it should never be slower).
  5. **Track H visual gate:** load an instance-detail view in the browser; verify step sidebar, completion banner, step icons, and main panel layout render identically to the pre-10.7c form (side-by-side screenshot comparison against a pre-story screenshot is sufficient).
- [x] 4.7 Story status set to `review` (not `done` — per standard dev workflow the dev agent sets `review` and the code reviewer / Adam sets `done` after the manual E2E pass at 4.6). This completes the 10.7 split trilogy code deliverables; next story in Epic 10 is 10.4 (open-source licence decision).

## Failure & Edge Cases

### F1: Content-tool binary-search truncation produces a different item count than the old linear variant

**When** the new `TruncateToByteLimit<T>` returns N items and the old linear variant would have returned N-1 or N+1 (boundary drift due to marker-string length differences)
**Then** the count may differ by 1 for edge-case inputs where the item-at-boundary itself changes the marker length
**Mitigation:** `markerFor(int count)` is passed as a delegate specifically so the marker length is recomputed for every candidate count. The characterisation test at Task 1.6 asserts the binary-search result equals the linear result for representative inputs; edge cases where results differ by 1 are acceptable because the item selection is still maximally packed — the "which item to exclude at the boundary" is a tie-break without any functional impact.
**Net effect:** a cosmetic drift of ±1 item is tolerable; a drift of >1 is a bug and the test must fail in that case.

### F2: Comment hygiene pass accidentally strips a load-bearing comment

**When** a comment is removed in Track G that turns out to explain a non-obvious invariant (e.g. MaxOutputTokens rationale, an RFC link, an ordering constraint)
**Then** the next dev to read the file loses context that was shipped in the diff
**Mitigation:** locked decision 6 gives the explicit test ("If removing the comment would confuse a reader reading the code cold, the comment stays"). The Track G commit body note per file gives Adam a review point — if a file had 9 comments stripped and none preserved, that's a flag to check for over-stripping.
**Net effect:** Track G is the lowest-risk track but has the highest "false confidence" risk. The commit-body bookkeeping is the mitigation.

### F3: Dev agent attempts to split a file Winston said "Skip"

**When** mid-refactor the dev is tempted to simplify `PromptAssembler.cs` or `WorkflowOrchestrator.cs` or `agentrun-chat-message.element.ts` (Codex suggested simplifications)
**Then** the dev STOPS and logs the temptation as a deferred-work candidate — does NOT refactor those files in this story
**Net effect:** scope discipline. Winston's triage was deliberate; overriding it requires a conversation, not a commit. If the dev spots a Real Issue in one of these files, file it as deferred-work and move on. This F-case applies to all three 10.7 child stories; listed here because 10.7c is the last of the trilogy — the temptation to "also clean up X while I'm in here" peaks when the story scope feels polish-shaped.

### F4: Track H over-expands a state-matrix group and loses at-a-glance scanability

**When** the dev blindly expands every one-line CSS rule in the block — including the `.step-item.{pending,active,complete,error} .step-icon-wrapper` four-liner, the `.step-icon` state-colour triple, or single-property utility rules
**Then** the file grows by 40–60 lines without adding clarity; worse, the state→style mapping that the compact form communicates at a glance becomes harder to scan because the reader has to parse 4 full rule blocks to see the pattern
**Mitigation:** locked decision 7 is explicit on what gets preserved. The Task 2.2 commit-body note per cluster gives Adam a review point — if a state-matrix group was expanded, that's a flag to revert that cluster. The rule of thumb: **a column of parallel one-line rules with the same shape is a table; reading across a table is faster than reading down a list of blocks. Don't break the table.**
**Net effect:** Track H is the lowest-code-risk track but has the highest "over-zealous reformat" risk. The per-cluster commit note is the mitigation.

## Dev Notes

### Why Track G runs last

The comment-hygiene grep is most valuable when it runs over the final shape of every file. If Track G ran before 10.7a's FetchUrlTool split, the dev would strip story refs from `FetchUrlTool.cs` and then find that 10.7a moves half that code to `HtmlStructureExtractor.cs` + `FetchCacheWriter.cs` — the strip would need repeating on the new files, and the per-file bookkeeping note from the original strip would be stale. Running Track G at the end means every file has stabilised; the grep sees the final surface; the bookkeeping note is correct.

### Why Track E is self-contained

The content-tool DRY extraction + O(n²) fix has zero overlap with any other 10.7 track — no file touched by Track E is touched by 10.7a (which touches `FetchUrlTool`, `ToolLoop`, `StepExecutor`) or 10.7b (which touches frontend only). Track E could have landed in 10.7a without conflict, but bundling it with Track G is better for review focus — 10.7a is heavy backend shape-refactor; keeping the content-tool perf fix separate means its review is about "does the binary search produce the same truncation shape as the linear loop?" without getting mixed into the bigger ToolLoop / FetchUrlTool scope conversation.

### Why behaviour preservation is paramount

This story is the third and smallest slice of the 10.7 cleanup. Track E changes algorithm, not output — the characterisation test at AC2 codifies that. Track G changes comments, not code — the grep count is the DoD signal. If a test needs assertions rewritten, something's wrong with the refactor shape; reshape the refactor, don't rewrite the test.

### Why the commit train matters

Two commits per the Task 3.5 plan — each commit leaves the full test suite green. The pattern serves three goals:
1. **Bisectability** — if a regression shows up later, `git bisect` lands on a small, track-scoped commit.
2. **Review-ability** — each commit is a reviewable unit; the comment-strip commit in particular is reviewable as "do these strips look right?" in isolation.
3. **Partial rollback** — if either track turns out to be wrong-shaped, revert the commit in isolation.

### What NOT to do

- Do NOT split files Winston flagged "Skip" or "Defer" (`PromptAssembler`, `WorkflowOrchestrator`, `agentrun-chat-message`). If you spot a real issue, log it as deferred-work.
- Do NOT change the shape / signature of the `_knownParameters` HashSets per-tool (locked decision 4 — the *set* is tool-specific, only the *method* is shared).
- Do NOT introduce new NuGet or npm deps.
- Do NOT strip comments containing RFC references, spec citations, or concurrency/ordering constraints during Track G. Archaeology only (locked decision 6).
- Do NOT strip the `MaxOutputTokens=32768` rationale comment in `StepExecutor.cs` — it's load-bearing technical context from Story 9.1b. (10.7a preserves this; 10.7c must also preserve it.)
- Do NOT strip the `AgentRunException bypasses LlmErrorClassifier` rationale wherever it appears (user memory item — past incident).
- Do NOT rewrite comments in bulk with an LLM prompt ("rewrite all comments"). The rule is targeted grep-match stripping + manual review per file. A sweep-rewrite is exactly the "AI slop" Track G is supposed to remove.
- Do NOT touch any backend files from 10.7a scope structurally — only comment-strip them.
- Do NOT touch any frontend files from 10.7b scope structurally — only comment-strip them if any frontend `.cs` file exists (there are none; frontend is `.ts` + tests).
- Do NOT strip comments from `.ts` files in Track G — the grep pattern is `.cs` only. Frontend comment cleanup is a separate effort if needed.
- Do NOT bulk-expand every one-line CSS rule in Track H — locked decision 7 is explicit about preserving state-matrix groups and single-property utility rules. Over-expansion is the F4 failure mode.
- Do NOT introduce a persistent CSS-block line budget in Track H. The ≤ 700 line budget from 10.7b AC1 was a 10.7b-specific refactor-validation gate; it does not carry forward past that story. Post-Track H, the file landing between 700 and 720 lines is expected and acceptable (locked decision 8).
- Do NOT add new frontend tests in Track H — CSS whitespace-only changes do not warrant new automated coverage. The manual visual check at Task 2.4 is the verification gate.
- Do NOT rename existing test methods; preserve BDD intent across setup changes.

### Test patterns

- **Backend:** NUnit 4 (`[TestFixture]`, `[Test]`, `Assert.That(..., Is.EqualTo(...))`). NSubstitute for mocks.
- **Characterisation tests:** test both old and new paths produce the same output on representative input. For Track E's binary-search truncation, construct a list + marker delegate + byte limit, run both the linear pseudo-code + the real binary-search helper, assert same final count. The old linear code doesn't ship (it's removed by Task 1.5), but the test can reimplement it locally as a reference.
- **Run all tests:** `dotnet test AgentRun.Umbraco.slnx` (always specify the slnx — never bare `dotnet test`; per [`feedback_dotnet_test_slnx.md`](../../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_dotnet_test_slnx.md)).

### Project Structure Notes

- **Backend new files:**
  - `AgentRun.Umbraco/Tools/ContentToolHelpers.cs`
  - `AgentRun.Umbraco.Tests/Tools/ContentToolHelpersTests.cs`
- **Backend modified files:**
  - `AgentRun.Umbraco/Tools/ListContentTool.cs` (helpers deleted; binary-search truncation)
  - `AgentRun.Umbraco/Tools/GetContentTool.cs` (helpers deleted; binary-search truncation)
  - `AgentRun.Umbraco/Tools/ListContentTypesTool.cs` (helpers deleted; binary-search truncation)
  - ~17 additional `.cs` files for Track G (comment hygiene) — each with story refs removed
- **Frontend modified files:**
  - `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` (Track H: CSS re-format in the static-styles block, lines 58–173; whitespace-only change)
  - `AgentRun.Umbraco/wwwroot/App_Plugins/AgentRunUmbraco/` bundle rebuild via `npm run build` in `Client/` — the bundled JS output must be committed alongside the `.ts` change (repo convention from Stories 6.4 / 10.7b)
- **Engine boundary preserved:** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/` must return 0 (locked decision 3).
- **No new NuGet dependencies. No new npm dependencies.**
- **No DI changes** — `ContentToolHelpers` is a static class.
- **No frontend logic changes** — Track H is CSS whitespace only; no `@state`, no `@property`, no handler, no template changes.

### Research & Integration Checklist (per Epic 9 retro Process Improvement #1)

- **Umbraco APIs touched:** none directly. `ContentToolHelpers` methods operate on `IDictionary<string, object?>` argument dictionaries (the Umbraco.AI tool-call argument shape, but no Umbraco types involved) and `IList<T>` of tool result items (generic — no Umbraco types).
- **Community resources consulted:**
  - [Epic 9 retrospective, 2026-04-13](./epic-9-retro-2026-04-13.md) — Technical Debt table identifies content-tool DRY + O(n²) truncation as 10.7 scope
  - [deferred-work.md #9-12-umbraco-content-tools code review](./deferred-work.md) — exact enumeration of the four duplicated helpers + O(n²) truncation finding
  - [Codex codebase bloat review, 2026-04-10](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md) — Phase 4 Comment Hygiene section
  - [Codex refactor brief, 2026-04-10](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md) — §Cross-Cutting Cleanup Recommendations §1 Comment Hygiene
- **Real-world content scenarios to test (Task 3.6):**
  - Content Audit workflow (Umbraco-native tools) — exercises `ContentToolHelpers` dedup + binary-search truncation on a real content tree
  - Representative content tree exceeding 256 KB truncation threshold on `list_content` or `list_content_types` — exercises the binary-search fast path

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story-10.7`](../planning-artifacts/epics.md) — epic-level definition + Winston's architect triage table
- [Source: `_bmad-output/implementation-artifacts/10-7-code-shape-cleanup-hotspot-refactoring.md`](./10-7-code-shape-cleanup-hotspot-refactoring.md) — parent story spec (now a redirect note); canonical source of the 18 locked decisions
- [Source: `_bmad-output/implementation-artifacts/10-7a-backend-hotspot-refactors.md`](./10-7a-backend-hotspot-refactors.md) — sibling story (must land first)
- [Source: `_bmad-output/implementation-artifacts/10-7b-frontend-instance-detail-and-chat-cursor.md`](./10-7b-frontend-instance-detail-and-chat-cursor.md) — sibling story (must land before Track G so comment strip runs over final shape)
- [Source: `_bmad-output/implementation-artifacts/epic-9-retro-2026-04-13.md`](./epic-9-retro-2026-04-13.md) — Technical Debt from Epic 9 table identifying content-tool DRY + O(n²) truncation as 10.7 scope
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md#deferred-from-code-review-of-story-9-12-umbraco-content-tools-2026-04-12`](./deferred-work.md) — deferred entry enumerating the 4 duplicated helpers + O(n²) truncation + BFS full-tree load
- [Source: `_bmad-output/planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md`](../planning-artifacts/codebase-bloat-review-agentrun-umbraco-2026-04-10.md) — §Phase 4 Comment Hygiene
- [Source: `_bmad-output/planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md`](../planning-artifacts/codebase-bloat-refactor-brief-agentrun-umbraco-2026-04-10.md) — §Cross-Cutting Cleanup Recommendations
- [Source: `AgentRun.Umbraco/Tools/ListContentTool.cs`, `GetContentTool.cs`, `ListContentTypesTool.cs`](../../AgentRun.Umbraco/Tools/) — Track E targets
- [Source: project-context.md](../project-context.md) — commit-per-story discipline, "only add comments when the WHY is non-obvious" rule

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context) — `claude-opus-4-6[1m]`.

### Debug Log References

- Track E build verification: `dotnet build AgentRun.Umbraco.slnx` → clean, same 2 pre-existing warnings (GetContentTool.cs:286 CS8604, GetContentTool.cs:486 CS0618).
- Track E test run: `dotnet test AgentRun.Umbraco.slnx --filter "FullyQualifiedName~ContentToolHelpersTests"` → 8/8 passed.
- Track E full-suite: `dotnet test AgentRun.Umbraco.slnx` → 713/713 passed.
- Track H bundle: `npm run build` in `Client/` → 20 modules transformed clean, `agentrun-instance-detail.element-B1z9OojA.js` 44.31 KB.
- Track H frontend: `npm test` in `Client/` → 235/235 passed.
- Track G baseline grep: `61` hits across 17 .cs files (lower than the spec's ~69 estimate because 10.7a/b already stripped some en-route).
- Track G post-grep: `0` hits — well below the ≤35 target. The rewrite preserved every substantive technical comment (MaxOutputTokens=32768 rationale, AgentRunException-bypass invariant, per-instance semaphore lifecycle, SSE keepalive cancel-then-dispose, defence-in-depth sandbox invariant, stall-recovery decision tree, etc.) by stripping only the story-ref lead while keeping the full body.
- Engine boundary grep (post Track G): `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → 0.

### Completion Notes List

- Track E deviation from Task 1.4 signature: `TruncateToByteLimit<T>` returns `(string Json, int IncludedCount)` (not just `string`) and takes an extra `Func<IList<T>, string> serialise` delegate. Two reasons: (1) GetContentTool wraps the truncated property-subset inside a larger `result` object (id, name, contentType, url, etc.); letting the helper own the serialiser would lock every caller into the bare-list shape. (2) GetContentTool's handle needs the post-truncation property count for the `propertyCount` field and the `truncated` flag — returning the count avoids a second pass. Binary-search algorithm itself is unchanged; the signature is the minimum deviation required to make the helper actually shared across all three tools. Rationale recorded at Task 1.4 checkbox.
- F1 (binary-search vs linear drift) characterisation test written against a representative 200-item synthetic input; asserts the included count differs from the linear reference by ≤ 1.
- Track H: locked decision 7 respected. Expansions were limited to 3 rules (`.sidebar-divider`, `.error-state`, `.main-panel`) — each ≥ 3 distinct properties and NOT part of a preserved state-matrix or family group. Preserved: state-matrix 4-liner (`.step-item.{pending,active,complete,error} .step-icon-wrapper` lines 134–137), `.step-icon` state-colour triple (139–142), `.step-text` family (143–146), `@keyframes spin` (147), `:host` 2-prop utility, five single-property utility rules. Final line count 688 (still under the legacy 10.7b budget though the budget explicitly doesn't persist past 10.7b per locked decision 8).
- Track G: the rewrite was surgical per locked decision 6 — "strip story ref, keep technical rationale" drove every edit. Single-line archaeology comments (e.g. `// Story 10.2: track assistant turns for compaction age calculation` where the variable is literally named `assistantTurnCount`) were deleted entirely. Multi-line comments with load-bearing content were rewritten in place to strip the story-ref lead while preserving the full body — MaxOutputTokens=32768 rationale retained in full, AgentRunException bypass invariant retained in full, stall-recovery decision tree retained in full. Zero technical rationale was discarded in Track G. No bulk-rewrite — every comment was individually reviewed against the locked-decision-6 test.
- Manual E2E (Task 4.6) is Adam's gate; spec steps preserved verbatim above. Track H visual-identity check is step 5 of the same gate.

### File List

**Track E (Commit 1 — cff8c1a):**

New files:
- `AgentRun.Umbraco/Tools/ContentToolHelpers.cs` — shared static helper class (4 arg-parsing methods + `TruncateToByteLimit<T>`).
- `AgentRun.Umbraco.Tests/Tools/ContentToolHelpersTests.cs` — 8 tests.

Modified files:
- `AgentRun.Umbraco/Tools/ListContentTool.cs` — delegates to `ContentToolHelpers`; truncation loop replaced with `TruncateToByteLimit`.
- `AgentRun.Umbraco/Tools/GetContentTool.cs` — delegates to `ContentToolHelpers`; truncation loop replaced with `TruncateToByteLimit` (property-subset variant via serialise delegate); `includedCount` feeds the handle's `propertyCount` + `truncated`.
- `AgentRun.Umbraco/Tools/ListContentTypesTool.cs` — delegates to `ContentToolHelpers`; truncation loop replaced with `TruncateToByteLimit`.
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — status set to in-progress.
- `_bmad-output/implementation-artifacts/10-7c-content-tool-dry-and-comment-hygiene.md` — Task 1 checkboxes.

**Track H (Commit 2 — 51a93e0):**

Modified files:
- `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts` — CSS block re-format (3 rules expanded).
- `AgentRun.Umbraco/wwwroot/App_Plugins/AgentRunUmbraco/*.js` + `*.js.map` — bundle rebuild (hash-renamed modules).
- `_bmad-output/implementation-artifacts/deferred-work.md` — strike-through on the 10.7c-candidate entry.
- `_bmad-output/implementation-artifacts/10-7b-frontend-instance-detail-and-chat-cursor.md` — strike-through on the Review-Defer entry.
- `_bmad-output/implementation-artifacts/10-7c-content-tool-dry-and-comment-hygiene.md` — Task 2 checkboxes.

**Track G (Commit 3 — this commit):**

Modified files (17 .cs files — comment-strip only, zero code changes):
- `AgentRun.Umbraco/Engine/ToolLoop.cs` (−10 hits; rewrote 6 multi-line comments, deleted 3 single-line archaeology comments, retained 1 rewritten).
- `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` (−9; rewrote 8 multi-line, stripped 1 prefix).
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` (−8; rewrote 6 multi-line, stripped 2 prefixes).
- `AgentRun.Umbraco/Engine/StallRecoveryPolicy.cs` (−6; rewrote all 6 multi-line, full technical body preserved in every one).
- `AgentRun.Umbraco/Instances/InstanceManager.cs` (−5; rewrote 1 multi-line, deleted 4 repeated `// Story 10.1: serialise read-modify-write on this instance.` one-liners — pure archaeology, the semaphore call site is self-explanatory).
- `AgentRun.Umbraco/Composers/AgentRunComposer.cs` (−4; rewrote 3 multi-line, stripped 1 prefix).
- `AgentRun.Umbraco/Tools/HtmlStructureExtractor.cs` (−3; rewrote 3, all technical content preserved).
- `AgentRun.Umbraco/Engine/StepExecutor.cs` (−3; rewrote MaxOutputTokens=32768 rationale full-body, rewrote classifier-bypass invariant full-body, deleted 1 single-line archaeology).
- `AgentRun.Umbraco/Tools/ReadFileTool.cs` (−2; rewrote both multi-line, full technical body preserved).
- `AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs` (−2; rewrote both prefixes).
- `AgentRun.Umbraco/Engine/StreamingResponseAccumulator.cs` (−2; rewrote class-comment + emit-boundary instrumentation comment).
- `AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs` (−2; rewrote both prefixes).
- `AgentRun.Umbraco/Workflows/WorkflowRegistry.cs` (−1; deleted single-line archaeology `// Story 9.6: enforce site-level ceilings` — the method call `EnforceCeilings(definition)` is self-describing).
- `AgentRun.Umbraco/Tools/FetchCacheWriter.cs` (−1; rewrote class-comment prefix).
- `AgentRun.Umbraco/Instances/ConversationStore.cs` (−1; rewrote prefix, full tool_use/tool_result boundary rationale preserved).
- `AgentRun.Umbraco/Engine/ToolDeclaration.cs` (−1; rewrote class-comment prefix).
- `AgentRun.Umbraco/Engine/StepExecutionFailureHandler.cs` (−1; rewrote class-comment prefix, kept full engine-domain-exception rationale).

Also modified: `_bmad-output/implementation-artifacts/10-7c-content-tool-dry-and-comment-hygiene.md` (Task 3 + Task 4 checkboxes + Dev Agent Record + File List + Change Log + Status → review) and `_bmad-output/implementation-artifacts/sprint-status.yaml` (status → review).

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-15 | Story spec created by splitting parent Story 10.7 into 10.7a / 10.7b / 10.7c per Adam's quality-risk concern on 4-day single-PR delivery. This story (10.7c) covers Track E (ContentToolHelpers + binary-search truncation) + Track G (comment hygiene polish) — cross-cutting cleanup only. Depends on 10.7a + 10.7b landing first so Track G grep runs over the final shape. | Bob (SM) |
| 2026-04-16 | Added **Track H — CSS re-format closure** on `agentrun-instance-detail.element.ts` to pick up the deferred-work entry explicitly marked as a 10.7c candidate ([deferred-work.md:374](./deferred-work.md), [10-7b:321](./10-7b-frontend-instance-detail-and-chat-cursor.md#L321)). Title + Story + Context + track table + locked decisions 7-13 + AC5 + Task 2 + F4 + What-NOT-to-do + Project Structure Notes + Change Log all updated. Three locked decisions added for Track H specifically: (7) judgement-led not bulk-reformat, (8) no persistent line budget past 10.7b, (9) Track-G-orthogonal. Priority ordering updated to E → H → G. Commit train updated from 2 commits to 3. No impact on Track E or Track G scope; test budget unchanged (~5 new backend tests, 0 frontend). | Bob (SM) |
| 2026-04-16 | **Story 10.7c → REVIEW.** Amelia shipped all three tracks across 3 commits — Track E (cff8c1a): `ContentToolHelpers` + binary-search truncation + 8 tests (total 713/713 backend, baseline 705 + 8). Track H (51a93e0): CSS re-format — 3 multi-property rules expanded, state-matrix + family + utility one-liners preserved per locked decision 7; bundle rebuilt clean; frontend 235/235 (unchanged); final line count 688. Track G (this commit): comment archaeology strip — 61 → **0** hits across 17 files (target ≤ 35; well below the 50% floor). Every substantive technical comment (MaxOutputTokens=32768 rationale, AgentRunException bypass invariant, stall-recovery decision tree, per-instance semaphore lifecycle, SSE keepalive cancel-then-dispose, defence-in-depth sandbox invariant) was rewritten in place with the story-ref lead stripped — zero technical content was lost. DoD: backend 713/713, frontend 235/235, build clean (only the two pre-existing GetContentTool warnings remain), engine boundary 0. Task 4.6 manual E2E remains Adam's gate; Track H visual-identity is step 5 of that gate. Test-budget plan (~5 new backend, 0 frontend) landed as 8 backend / 0 frontend — the extra 3 tests are edge-case coverage (full-fits fast path + empty-marker fallback + the F1 characterisation test), still within decision 12's "budget guideline, not ceiling". | Amelia (Dev) |
