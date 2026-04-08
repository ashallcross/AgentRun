# Sprint Change Proposal — Story 9-1c Pause & Partial Milestone

**Date:** 2026-04-08
**Author:** Bob (Scrum Master) via `bmad-correct-course`
**Triggered by:** Adam (manual E2E discovery 2026-04-07 evening)
**Architect of record:** Winston (decisions locked in prior planning conversation 2026-04-08)
**Affected story:** 9-1c — First-Run UX: User-Provided URL Input Handling
**Affected epic:** Epic 9 — Beta Release Preparation (private beta gate)
**Change scope:** **Moderate** — backlog reorganization (new upstream story inserted, existing story paused, dependency chain extended). Not a scope cut. Not a cancellation. Story 9-1c remains in scope for the private beta.
**Status:** Approved by Adam, ready for handoff to Bob (SM) for follow-up `bmad-create-story` invocation on Story 9.7

---

## Section 1 — Issue Summary

### What triggered this change

During Story 9-1c manual E2E (Task 7.4) on 2026-04-07 evening, the Content Quality Audit example workflow was tested against a real-world 5-URL batch (mix of small marketing pages, the BBC News homepage, and a Wikipedia article). The scanner step failed with `StallDetectedException` after the 5th fetch returned a small error string. Investigation by the dev agent established that the failure mode is **not** a prompt issue and **not** a regression in Story 9-0's stall detector — it is a **tool contract bug** in `FetchUrlTool` that has been latent in the codebase since Epic 5.

### Where the diagnosis lives

The full architectural finding report is at [_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md](_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md). The TL;DR + Evidence + Root Causes sections are authoritative and are not relitigated here. This Sprint Change Proposal references the finding report; it does not duplicate it.

### Concrete evidence (extracted from the finding report)

The failing instance is `caed201cbc5d4a9eb6a68f1ff6aafb06` (Content Quality Audit). The conversation-scanner.jsonl log shows the scanner correctly following every prompt invariant from Story 9-1c's strengthened scanner.md:

- Sequential fetches, one `fetch_url` call per assistant turn (Invariant #3 — sequential-fetch)
- One-line progress markers in the same turn as the next tool call (Invariant #2 — between-fetches)
- No parallel batching, no text-only narration mid-task

The scanner did exactly what the prompt told it to do. Despite this, after four successful `fetch_url` results accumulated **~1.81 MB of raw HTML** in the conversation context (159 KB + 258 KB + 1.39 MB across three pages), the model went empty on the 5th fetch attempt. Sonnet 4.6 produced an end_turn with empty content. Story 9-0's stall detector correctly fired and marked the step Failed.

**Two findings from the trace are non-negotiable:**

1. **The Story 9-1c prompt mitigations are working.** The conversation log proves it. No further prompt iteration will fix this — the prompt-only ceiling has been reached.
2. **The conversation context at the failure point contained ~1.8 MB of raw HTML.** The model going empty in this degenerate state is consistent with known Sonnet 4.6 behaviour patterns documented in `memory/project_sonnet_46_scanner_nondeterminism.md` and in Anthropic's general guidance about large prior tool results. The fix is upstream of the model: stop dumping raw HTTP bodies into conversation context.

### Why this matters for the beta

Story 9-1c's whole purpose is to validate the first-run experience — the moment a developer installs AgentRun, configures a profile, and pastes URLs into the chat for the first time. **The exact scenario the story's acceptance criteria describe (multi-URL handling, AC #3 and AC #6) is what the bug breaks.** Shipping a known-flaky example workflow to private beta invitees would undermine the entire trustworthiness positioning we have built the beta around.

The architect's evaluation: this is a beta blocker. It cannot be deferred to post-beta. It cannot be papered over with a "known limitation" note. It must be fixed before the example workflow is exercised by external testers.

---

## Section 2 — Impact Analysis

### Epic Impact

**Epic 9 (Beta Release Preparation):** Still completable as originally scoped. Goal is unchanged. Dependency chain extended by one new upstream story (9.7).

| Dimension | Before | After |
|---|---|---|
| Epic 9 goal | Two polished example workflows shipped to private beta via NuGet 1.0.0-beta.1 | _Unchanged_ |
| Epic 9 stories | 9-6, 9-0, 9-1a, 9-1c, 9-1b, 9-4, 9-2, 9-3, 9-5 | 9-6, 9-0, 9-1a, **9-7 (NEW)**, 9-1c, 9-1b, 9-4, 9-2, 9-3, 9-5 |
| 9-1c status | in-progress | **paused** (partial milestone committed; resumes after 9-7) |
| 9-1c dependencies | 9-6 (done), 9-0 (done), 9-1a (done) | 9-6 (done), 9-0 (done), 9-1a (done), **9-7 (new beta blocker)** |
| 9-1b dependencies | 9-1c | 9-1c, **9-7 transitively** (9-1b will be re-scoped in a separate course correction to land Option 2 / AngleSharp structured extraction) |
| Beta release date | _Not date-pinned_ — gated on epic completion | _Unchanged_ — gated on epic completion, just a slightly longer chain |

**No other epics are affected.** Epics 10, 11, 12 dependencies and content unchanged.

### Story Impact

**Story 9-1c:** Status changes from `in-progress` to `paused`. Tasks 1–6 are complete and the strengthened scanner.md is committed as the **partial milestone**. Task 7 (manual E2E) is annotated `PAUSED — resume after Story 9-7 ships`. ACs split into two groups (see Section 4 for the full annotation list).

**Story 9-7 (NEW) — Tool Result Offloading for `fetch_url`:** To be created by Bob via a separate `bmad-create-story` invocation immediately after this course correction completes. Inserted into Epic 9 between 9-0 and 9-1c in the dependency chain. Beta blocker. Implements the offloaded raw response pattern per Winston's locked architectural decision (Option 1 from the finding report, with the architect's tightened constraints around the scratch path location, hash function, handle shape, 3xx handling, and regression test sizing). **9-7 is NOT created in this Sprint Change Proposal.** Bob will create it next.

**Story 9-1b:** Will be course-corrected in a separate `bmad-correct-course` invocation immediately following this one. Scope shifts from "iterate prompts until trustworthy" to "land Option 2 (server-side structured extraction via AngleSharp), then iterate prompts on the new contract." Trustworthiness gate criterion, 5-pass cap, signoff artefact all unchanged. **9-1b is NOT touched in this Sprint Change Proposal.**

**Stories 9-4, 9-2, 9-3, 9-5:** Unaffected. Their dependencies on 9-1c remain. They will pick up the work once 9-1c resumes and signs off (post-9-7).

**Stories 9-6, 9-0, 9-1a:** Unaffected. They are done.

### Artifact Conflicts

| Artifact | Conflict? | Action |
|---|---|---|
| PRD ([_bmad-output/planning-artifacts/prd.md](_bmad-output/planning-artifacts/prd.md)) | No | No edit |
| Architecture ([_bmad-output/planning-artifacts/architecture.md](_bmad-output/planning-artifacts/architecture.md)) | Minor — should record the new "tool results are never just dumped into conversation context" safety principle | **Deferred** to architect (Winston) per his note that he is adding stub additions to v2-future-considerations.md in a separate pass |
| UX Design ([_bmad-output/planning-artifacts/ux-design-specification.md](_bmad-output/planning-artifacts/ux-design-specification.md)) | No | No edit |
| Story 9-1c spec ([_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md](_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md)) | **Yes** — needs status, partial milestone section, AC annotations, dependency on 9-7 | **Updated by this proposal** (see Section 4) |
| scanner.md ([AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md](AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md)) | No — already strengthened correctly | **Do not modify.** Acknowledged as the partial milestone artefact. |
| sprint-status.yaml ([_bmad-output/implementation-artifacts/sprint-status.yaml](_bmad-output/implementation-artifacts/sprint-status.yaml)) | **Yes** — needs 9-1c status update plus a new `paused` status definition | **Updated by this proposal** (see Section 4) |
| Architectural finding report | No — authoritative as written | **Do not modify** per Adam's instruction |
| v2-future-considerations.md | Pending architect-led updates in a separate pass | **Do not modify** in this proposal |
| epics.md ([_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md)) | Marginal — Epic 9 list will need a 9-7 entry once Bob creates it via `bmad-create-story` | **Deferred** to the `bmad-create-story` invocation that creates Story 9-7. Not edited in this proposal. |

### Technical Impact

- **No code is changed by this Sprint Change Proposal.** This is paperwork only.
- The strengthened scanner.md committed during the 9-1c prompt-only work is the only code-adjacent artefact, and it is preserved as-is.
- Story 9-7's implementation work (when Bob creates the spec and Amelia implements it) will touch `AgentRun.Umbraco/Tools/FetchUrlTool.cs`, `AgentRun.Umbraco/Engine/ToolLoop.cs`, `AgentRun.Umbraco/Security/PathSandbox.cs` (verification only — confirm `.fetch-cache/` is reachable via `read_file`), and add new unit tests with captured fixtures from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`. **None of that work is in scope for this Sprint Change Proposal.**

---

## Section 3 — Recommended Approach

### Path selected: Path B (architect's recommendation, locked in prior conversation)

**Path B = pause Story 9-1c on the upstream tool fix, do NOT ship with reduced acceptance criteria.**

The architect (Winston) explicitly considered and rejected:

- **Path A** — course-correct 9-1c to ship prompt-only work with reduced ACs. Rejected because AC #3 (multi-URL handling) and AC #6 (5+ URLs in sequence) are not peripheral to the story; they are the core trial-experience the example workflow exists to demonstrate. Marking them "deferred" while shipping the story would be a fiction that the private beta would expose immediately on first run.
- **Path C** — ship as-is and accept multi-URL flake. Rejected for the same reason: the trustworthiness gate is the entire point of the example workflow, and shipping a known-flaky example to private beta invitees undermines the trust positioning the beta is designed to validate.

### The amendment Winston added to Path B

Commit the prompt-only work (Tasks 1–6 of Story 9-1c) **as a partial milestone NOW**, before Story 9-7 begins. The strengthened scanner.md is correct work that is proven by the conversation log. It should not sit half-merged in a branch waiting for the upstream fix. Two reasons:

1. **Protects the work against drift.** If Story 9-7 takes longer than expected, the prompt invariants stay safely on `main` and don't risk being lost or contaminated.
2. **Keeps the dev agent's fail-fast feedback loop on the new contract clean.** When Story 9-1c resumes Task 7 manual E2E against the new tool, the only variable being tested is the tool. Not the prompt + the tool.

### Effort estimate

- **This Sprint Change Proposal (paperwork):** Already drafted. ~30 minutes of writing time, no code changes.
- **Story 9-7 spec writing (Bob, next step):** ~1–2 hours.
- **Story 9-7 implementation (Amelia, after spec):** Per the dev agent's estimate in the finding report — ~150–250 lines C#, ~50 lines test, scanner.md update. Plus the architect's added regression tests at 100 KB / 500 KB / 1.5 MB sizes using captured fixtures from the failing instance. Estimated as one focused day plus testing.
- **Story 9-1c resumption (after 9-7 ships):** Re-run Task 7 manual E2E against the new tool contract. If the trace looks clean, sign off and merge. If new problems surface, escalate to the architect.

### Risk assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Story 9-7 implementation reveals a deeper issue with the offloaded handle pattern | Low | High | Architect has tightened the constraints (path inside instance folder, SHA-256, no content-in-handle backup, 3xx returns `saved_to: null`). The pattern is well-trodden in Claude Code, Anthropic Computer Use demo, and the Claude Agent SDK. Risk is low. |
| Story 9-1c's prompt invariants need re-tuning against the new tool contract | Medium | Low | If they do, that's normal polish work and is captured in Task 7's resumption. The five hard invariants in scanner.md are largely independent of whether the tool returns inline content or a handle — they govern *when* the agent calls tools, not *what the tools return*. |
| Path sandbox does not allow `.fetch-cache/` reads via `read_file` | Low | Medium | Amelia must verify this explicitly in 9-7's implementation. Architect flagged it as a verification step. If `PathSandbox` rejects dotted directories, fix it before shipping 9-7. |
| 9-1b's separate course correction (next pass) reveals further coupling with 9-1c that this proposal didn't anticipate | Low | Low | 9-1b's course correction is the very next ceremony after this one. Any cross-impact is caught immediately. |
| Beta release slips beyond an external commitment date | N/A | N/A | No external date commitment exists. The beta is gated on epic completion, not a calendar. |

### Why this is the right path despite the cost

The cost is one extra story in Epic 9 and a pause on 9-1c. The benefit is that the example workflow actually works on the trial scenario it is built to demonstrate. There is no alternative path that ships a meaningful private beta. Path A would ship a story whose core ACs are knowingly broken. Path C would ship a workflow that fails on its trial use case. Path B is the only option that preserves the trust positioning the beta is designed around.

---

## Section 4 — Detailed Change Proposals

Three artifacts are modified by this Sprint Change Proposal. Two more are deferred to follow-up ceremonies (Story 9-7 spec, Story 9-1b course correction). Architect-owned updates to `v2-future-considerations.md` and `architecture.md` are deferred to Winston in a separate pass.

### Change 4.1 — Update Story 9-1c spec file

**File:** [_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md](_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md)

**Edits:**

1. **Status header** — change from `Status: in-progress` to `Status: paused`
2. **Depends on line** — append `, 9.7 (Tool Result Offloading for fetch_url — NEW BETA BLOCKER, not yet created)` to the existing dependency list
3. **Insert new section "Pause Reason & Partial Milestone"** at the very top of the file body (immediately after the Status / Depends on / Blocks block, before the existing `## Story` heading) — see full content in the actual file edit below
4. **Annotate Acceptance Criteria** with status markers per the locked architect decisions:
   - AC #1 (verbatim opening line) → `[MET — partial milestone, committed 2026-04-08]`
   - AC #2 (immediate fetch_url after URL) → `[MET — partial milestone, committed 2026-04-08]`
   - AC #3 (multi-URL extraction) → `[BLOCKED — see Sprint Change Proposal 2026-04-08; cannot be validated until Story 9.7 ships and the tool contract is fixed]`
   - AC #4 (protocol prepending) → `[MET — partial milestone, committed 2026-04-08]`
   - AC #5 (re-prompt on zero URLs) → `[MET — partial milestone, committed 2026-04-08]`
   - AC #6 (5+ URLs in sequence) → `[BLOCKED — see Sprint Change Proposal 2026-04-08; cannot be validated until Story 9.7 ships and the tool contract is fixed]`
   - AC #7 (truncated response handling) → `[MET in prompt — partial milestone, committed 2026-04-08; full E2E validation paused pending Story 9.7]`
   - AC #8 (failure case bucketing) → `[MET in prompt — partial milestone, committed 2026-04-08; full E2E validation paused pending Story 9.7]`
   - AC #9 (existing tests pass) → `[MET — partial milestone, committed 2026-04-08]`
5. **Annotate Task 7** (Manual E2E) — change the section header from `- [ ] **Task 7: Manual E2E validation**` to `- [ ] **Task 7: Manual E2E validation** [PAUSED — resume after Story 9.7 ships, see Sprint Change Proposal 2026-04-08]`. **Do not delete the validation steps.** They are still required for the story to be considered done; they just cannot run until 9-7 lands.
6. **Update Completion Notes List** — append a new bullet recording the pause and the partial milestone

**Rationale:** The story is paused, not cancelled. AC content remains unchanged. The status markers signal which ACs are validated by the partial milestone vs which are blocked. The Manual E2E task stays in place so when 9-1c resumes, the validation steps are intact and the dev agent does not need to reconstruct them.

### Change 4.2 — Update sprint-status.yaml

**File:** [_bmad-output/implementation-artifacts/sprint-status.yaml](_bmad-output/implementation-artifacts/sprint-status.yaml)

**Edits:**

1. **Add `paused` status to STATUS DEFINITIONS** — insert a new line in the Story Status block (between `done` and `moved`) defining the new status:
   ```
   #   - paused: Story started, partial work committed to main, blocked on a new
   #       upstream dependency. Differs from 'blocked' (which would mean "cannot
   #       start") — paused stories have meaningful work already shipped to main
   #       and resume from where they left off once the upstream dependency is
   #       satisfied. Inline comment must point to the Sprint Change Proposal
   #       that captured the pause.
   ```
2. **Update the 9-1c story line** — change from:
   ```
   9-1c-first-run-ux-url-input: in-progress                   # depends on 9-0 + 9-6
   ```
   to:
   ```
   9-1c-first-run-ux-url-input: paused                        # paused 2026-04-08 pending Story 9-7 (tool result offloading) — see sprint-change-proposal-2026-04-08-9-1c-pause.md
   ```
3. **Update the `last_updated` line** — change from `2026-04-07 (story 9-1c in-progress)` to `2026-04-08 (story 9-1c paused pending 9-7)`
4. **Do NOT add a 9-7 line in this pass.** Bob will add it via `bmad-create-story` in the next ceremony.

**Rationale:** Adds a future-proof status name (per the precedent set by `moved` for 10-3 → 9-0) rather than a comment-annotation hack. Inline comment provides the trail back to this Sprint Change Proposal.

### Change 4.3 — Write this Sprint Change Proposal

**File:** [_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md](_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md)

**Edit:** Create the file with the content of this entire document.

**Rationale:** Provides the canonical paper trail. Story 9-1c's spec, sprint-status.yaml's inline comment, and (eventually) Story 9-7's spec will all point back here. Anyone tracing the decision history will land on this file and have everything they need.

---

## Section 5 — Implementation Handoff

### Change scope classification

**Moderate** — backlog reorganization with a new upstream story to be created. Not Minor (this is more than a direct dev change) and not Major (no PRD revision, no architectural redesign, no fundamental replan).

### Handoff plan

| Step | Owner | Action | Artifact produced |
|---|---|---|---|
| 1. This Sprint Change Proposal | Bob (SM) via `bmad-correct-course` | Write SCP, update 9-1c spec, update sprint-status.yaml | `sprint-change-proposal-2026-04-08-9-1c-pause.md`, updated `9-1c-first-run-ux-url-input.md`, updated `sprint-status.yaml` |
| 2. Commit partial milestone | Adam | Commit the strengthened scanner.md to `main` with a clear message noting the pause and pointing to this SCP. Recommended commit message format: `Story 9-1c partial milestone: scanner.md prompt invariants (paused pending 9-7)` | Git commit on `main` |
| 3. Create Story 9-7 spec | Bob (SM) via `bmad-create-story` | Write the full Story 9-7 — Tool Result Offloading for `fetch_url` spec using the architect's locked decisions (offloaded raw response, scratch path inside instance folder at `.fetch-cache/{sha256(url)}.html`, no content-in-handle backup, 3xx returns `saved_to: null`, mandatory regression tests at 100 KB / 500 KB / 1.5 MB sizes from captured fixtures) | `_bmad-output/implementation-artifacts/9-7-tool-result-offloading-fetch-url.md` |
| 4. Add 9-7 to sprint-status.yaml | Bob (SM) (as part of step 3) | Insert a new `9-7-tool-result-offloading-fetch-url: ready-for-dev` line into Epic 9, positioned between 9-0 and 9-1c | Updated `sprint-status.yaml` |
| 5. Course-correct Story 9-1b | Bob (SM) via `bmad-correct-course` (separate invocation) | Re-scope 9-1b from "iterate prompts until trustworthy" to "land Option 2 (server-side structured extraction via `fetch_url(extract: structured)` using AngleSharp), then iterate prompts on the new contract." Trustworthiness gate, 5-pass cap, signoff artefact all unchanged. | `sprint-change-proposal-2026-04-08-9-1b-rescope.md` (or similar), updated 9-1b spec |
| 6. Implement Story 9-7 | Amelia (Dev) via `bmad-dev-story` | Build the offloaded raw response pattern in `FetchUrlTool` per the spec. Ship with regression tests. | Code change + tests on `main` |
| 7. Verify path sandbox accepts `.fetch-cache/` | Amelia (Dev), part of Story 9-7 | Confirm `read_file` can reach the new scratch directory; if `PathSandbox` rejects dotted directories, fix before shipping 9-7 | Test coverage in 9-7 |
| 8. Resume Story 9-1c Task 7 | Adam (manual E2E) | Re-run Task 7.1 through 7.10 against the new tool contract. If clean, sign off and merge 9-1c to `done`. If new problems surface, escalate to Winston. | Updated 9-1c spec status `done`, signoff artefact |
| 9. Story 9-1b begins | Amelia (Dev), after 9-1b spec is course-corrected and 9-7 + 9-1c are done | Implement Option 2 (AngleSharp), update agent prompts to consume structured tool returns, run trustworthiness gate loop | Story 9-1b done |

### Definition of "this Sprint Change Proposal is complete"

- ✅ This file exists at `_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md`
- ✅ Story 9-1c spec is updated with the pause status, partial milestone section, AC annotations, dependency on 9-7, and Task 7 PAUSED annotation
- ✅ sprint-status.yaml has the new `paused` status definition and the 9-1c line updated with the inline comment
- ✅ Adam has reviewed and approved the proposal
- ✅ Adam is ready to invoke `bmad-create-story` for Story 9-7 immediately after this ceremony

### What this Sprint Change Proposal does NOT do

- ❌ Does not create Story 9-7 (deferred to `bmad-create-story` next)
- ❌ Does not modify Story 9-1b (deferred to a separate `bmad-correct-course` invocation immediately following this one)
- ❌ Does not modify scanner.md (already correct, preserved as the partial milestone artefact)
- ❌ Does not modify the architectural finding report (authoritative, do not edit)
- ❌ Does not modify v2-future-considerations.md (architect-owned in a separate pass)
- ❌ Does not modify architecture.md or epics.md (deferred to follow-up ceremonies)
- ❌ Does not write any code

---

## Approval

**Approved by:** Adam (in advance of this ceremony, via the prompt that initiated `bmad-correct-course` on 2026-04-08)
**Architect concurrence:** Winston (decisions locked in prior planning conversation 2026-04-08, captured in the `## Architect's Response` section of the planning conversation log)
**Date:** 2026-04-08

---

_This Sprint Change Proposal is a point-in-time document. The architectural finding report ([_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md](_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md)) is the living technical reference. Story 9-7 (when created) is the source of truth for the implementation. This SCP captures the decision and the trail._
