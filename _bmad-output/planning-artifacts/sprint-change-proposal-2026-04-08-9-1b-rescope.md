# Sprint Change Proposal — Story 9-1b Scope Reshape

**Date:** 2026-04-08
**Author:** Bob (Scrum Master) via `bmad-correct-course`
**Triggered by:** Same architectural finding that paused Story 9-1c (manual E2E discovery 2026-04-07 evening)
**Architect of record:** Winston (decisions locked in prior planning conversation 2026-04-08)
**Affected story:** 9-1b — Content Quality Audit Polish & Quality
**Affected epic:** Epic 9 — Beta Release Preparation (private beta gate)
**Change scope:** **Moderate** — story scope reshape; new dependency added; status preserved; no formal spec exists yet so no spec file is touched. Story 9-1b remains in scope for the private beta with its trustworthiness gate, 5-pass cap, and signoff artefact unchanged.
**Status:** Approved by Adam, ready for handoff: next ceremony is Amelia implementing Story 9.7.

---

## Section 1 — Issue Summary

### Why this Sprint Change Proposal exists alongside the 9-1c SCP

This is the **second of two** Sprint Change Proposals triggered by the architectural finding from Story 9-1c's manual E2E on 2026-04-07 evening. The first SCP ([sprint-change-proposal-2026-04-08-9-1c-pause.md](sprint-change-proposal-2026-04-08-9-1c-pause.md)) paused Story 9-1c on a new upstream beta-blocker (Story 9.7 — Tool Result Offloading for `fetch_url`) and committed the prompt-only work as a partial milestone. This second SCP reshapes Story 9-1b's scope to absorb the architect's locked Option 2 decision (server-side structured extraction via AngleSharp) on top of Story 9.7's offloading layer.

The two SCPs are kept as separate documents on purpose. They are two distinct course corrections triggered by the same finding but with different scopes, different ownership, and different timelines. Capturing them separately preserves clarity in the decision trail: anyone reading just the 9-1c SCP knows what happened to 9-1c without needing to understand the 9-1b reshape, and vice versa. Cross-references at the top of each SCP point to its sibling.

### What triggered this change

Same root cause as the 9-1c pause. The dev agent's full diagnosis is in [_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md](9-1c-architectural-finding-fetch-url-context-bloat.md) — TL;DR + Evidence + Root Causes + "The fix — proposed" sections are authoritative and are not relitigated here. This SCP references the finding report; it does not duplicate it.

For the 9-1b reshape specifically, the relevant section of the finding report is **"The fix — proposed → Option 2: Server-side extraction (more aggressive, scanner-specific)"** at lines 128–152. That section proposes adding server-side structured extraction so that the scanner agent stops re-parsing raw HTML into headings/word counts/image data — work that the model was already doing inconsistently and was destined to continue doing inconsistently regardless of how many polish iterations Story 9-1b put in.

### The "polish loop is the wrong lever" insight

Before today, Story 9-1b's scope was "iterate prompts until trustworthy." That framing assumed the polish lever was the prompts themselves — if Adam picked enough representative test inputs and the dev agent rewrote scanner.md / analyser.md / reporter.md enough times, the trustworthiness gate would eventually pass. The 2026-04-07 manual E2E proved this assumption wrong on two distinct counts:

1. **The scanner stalls before the polish loop can even start.** Until Story 9.7 ships the offloading layer, the multi-fetch flow that the scanner depends on is structurally broken at the tool-contract level. No amount of prompt iteration on a flaky tool produces a trustworthy output.

2. **Even after 9.7 ships, the model is still re-parsing raw HTML to extract headings/image alt counts/word counts.** That re-parsing is the *exact* class of work the model has been observed to do inconsistently — sometimes hallucinating headings, sometimes missing alt-text on images that are present in the source, sometimes producing word counts that don't match the actual body content. Polish-loop iteration on the prompt would chase those inconsistencies forever, because the bug is upstream of the prompt: the model is being asked to do parsing work that should be done by a real HTML parser.

The architect's insight (locked in his response to the finding report): **server-side structured extraction is the right lever for the trustworthiness gate, not prompt polish.** Move the structured extraction to a real HTML parser (AngleSharp), have the parser produce deterministic facts (title, headings, meta description, word count, image counts, link counts), feed those facts back to the analyser and reporter as a structured handle, and the polish loop's job collapses from "tune the model's parsing" to "tune the model's reasoning over deterministic facts." The latter is a much smaller, more honest tuning surface.

### Concrete failure modes that motivate Option 2

From the failing instance `caed201cbc5d4a9eb6a68f1ff6aafb06` and the broader 2026-04-07 evening session:

1. **Hallucinated headings.** When the scanner saw a 1.39 MB BBC News HTML payload in context, the model produced heading lists that did not match the actual `<h1>`/`<h2>` tags in the source. Some flagged headings were never present. This is the class of bug that AngleSharp eliminates entirely — if AngleSharp's `QuerySelectorAll("h1")` doesn't return it, the scanner doesn't see it.

2. **Inconsistent image alt-text counts across runs.** Even after Story 9.7 ships and the scanner reads the offloaded HTML via `read_file`, asking the model to count images with/without alt text from raw HTML is asking it to do deterministic work non-deterministically. AngleSharp does this in a single line: `images.Count(img => string.IsNullOrEmpty(img.GetAttribute("alt")))`.

3. **Word count drift.** The model's notion of "word count of body text" varies run-to-run because it's mentally extracting body text and counting words. AngleSharp's `document.Body.TextContent.Split(...).Length` is byte-deterministic for byte-identical input.

4. **Internal vs external link classification.** Asking the model to classify links by comparing each `href` to the source URL's host is parsing work, not reasoning work. AngleSharp does it cleanly in code.

The pattern in all four failure modes is the same: **parsing work that is non-deterministic when the model does it, becomes deterministic when a real parser does it.** The polish loop should be tuning *reasoning over deterministic facts*, not chasing parser hallucinations through prompt rewrites.

### Why this matters for the beta

The trustworthiness gate is the entire point of Story 9-1b. It is a signoff gate — the story doesn't ship until Adam can sign in writing that the audit output is good enough to send to a paying client without rewriting. Shipping a polish loop that is structurally incapable of converging on a trustworthy output (because the underlying parsing is non-deterministic) means shipping a story whose acceptance criteria can never be met. The reshape moves Story 9-1b from "iterate forever on a non-convergent loop" to "iterate on a convergent loop with a real parser doing the parsing."

This is not a cost; it is the *only* path to actually clearing the trustworthiness gate before the private beta ships.

---

## Section 2 — Impact Analysis

### Epic Impact

Epic 9 (Beta Release Preparation) is still completable as scoped. Goal unchanged. Dependency chain unchanged from where the 9-1c SCP left it (9.7 was already inserted there).

| Dimension | Before this SCP | After this SCP |
|---|---|---|
| Epic 9 stories | 9-6, 9-0, 9-1a, 9-7, 9-1c (paused), 9-1b (backlog), 9-4, 9-2, 9-3, 9-5 | _Unchanged structurally — only 9-1b's body outline is reshaped._ |
| 9-1b scope | "iterate prompts until trustworthy" | **"land Option 2 (server-side structured extraction via AngleSharp), then iterate prompts on the new contract until the trustworthiness gate passes against ≥3 representative real test inputs"** |
| 9-1b status | backlog | **backlog (preserved)** |
| 9-1b dependencies | 9.6 (done), 9.0 (done), 9.1a (done), 9-1c | 9.6 (done), 9.0 (done), 9.1a (done), **9.7 (ready-for-dev)**, 9-1c (paused — must reach `done`) |
| 9-1b new dependency added | — | **9.7** (explicit) |
| Trustworthiness gate criterion | _Adam would be willing to send the output to a paying client without rewriting_ | _Unchanged_ |
| 5-pass soft cap with architect escalation | Active | _Unchanged_ |
| Signoff artefact location | `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` (or equivalent) | _Unchanged_ |
| Beta release date | _Not date-pinned — gated on epic completion_ | _Unchanged_ |

**No other epics affected.** Epics 10, 11, 12 dependencies and content unchanged. No new epics added.

### Story Impact

**Story 9-1b:** Body outline in epics.md is reshaped. Status preserved as `backlog`. Dependencies expanded to include 9.7 explicitly. The user story / scope statement, AC group structure, and Process section are updated to reflect the two-phase scope. The trustworthiness gate, 5-pass cap, and signoff artefact references are preserved exactly. Two existing edge-case bullets (Sonnet 4.6 parallel-tool-call stall, retry-replay degeneration) are annotated rather than deleted — see Section 4 for details and Section 4 for the explicit promotion of the retry-replay finding to its own future story.

**Story 9-1c:** Untouched by this SCP. Already correctly paused by the 9-1c SCP.

**Story 9.7:** Untouched by this SCP. Already correctly created as `ready-for-dev` and authoritative.

**Story 9.4 (Accessibility Quick-Scan):** Implicitly benefits — when 9-1b ships AngleSharp-based extraction as a `extract: "structured"` mode on `fetch_url`, Story 9.4 inherits the same capability. 9.4's spec (when Bob writes it post-9-1b) should consume the same structured shape. **No 9.4 update is needed in this SCP.** This is recorded for future awareness, not action.

**Stories 9.2, 9.3, 9.5:** Unaffected. Their dependencies on 9-1c remain. They will pick up once 9-1c is `done` (post-9.7).

**Stories 9-6, 9-0, 9-1a:** Unaffected. They are done.

### A new story will need to be created (separately, not in this SCP)

The current 9-1b epic outline contains an edge-case bullet recording a **retry-replay degeneration** finding from the same 2026-04-07 evening session that triggered the context-bloat finding. The retry-replay finding is a *separate, latent bug* in the engine's retry path: when a step fails mid-tool-loop and the user retries, the conversation history is replayed including all completed `fetch_url` results from the failed attempt, leaving the model in a degenerate state where "all tools have run, only `write_file` is left" and it stalls again because the only thing left to do is produce text or call `write_file` (and it sometimes produces text).

**This is not a polish-loop bug.** It is an engine-state-machine bug that needs ToolLoop or ConversationStore changes. It does not get fixed by Story 9.7 (offloading), it does not get fixed by Story 9-1b's Option 2 reshape (structured extraction), and it does not get fixed by polish iteration on prompts. It needs its own story.

**Decision recorded by Adam 2026-04-08:** Promote the retry-replay finding to its own story. Adam will run `bmad-create-story` for it after this ceremony completes. Working name: **"Story 9.8 — Retry-Replay Degeneration Recovery"** (final name TBD by Bob). Story 9-1b's outline gets an annotation in the relevant edge-case bullet making it explicit that this finding is moving to its own story and is no longer 9-1b's responsibility.

This SCP does NOT create Story 9.8. It only annotates the existing 9-1b outline to record the decision and points the next-step handoff at `bmad-create-story`. Same pattern as the 9-1c SCP did with Story 9.7: capture the decision in the SCP, defer the story creation to the next ceremony.

### Artifact Conflicts

| Artifact | Conflict? | Action |
|---|---|---|
| PRD ([_bmad-output/planning-artifacts/prd.md](prd.md)) | No | No edit |
| Architecture ([_bmad-output/planning-artifacts/architecture.md](architecture.md)) | No | No edit |
| UX Design ([_bmad-output/planning-artifacts/ux-design-specification.md](ux-design-specification.md)) | No | No edit |
| Story 9-1b body outline in [epics.md](epics.md) | **Yes** — needs scope reshape, dependency update, provenance note, and four targeted AC/edge-case adjustments per Section 4 | **Updated by this proposal** |
| Story 9-1b formal spec file (`_bmad-output/implementation-artifacts/9-1b-...md`) | N/A — does not exist yet | **Do not create.** Bob writes the formal spec via `bmad-create-story` later, after 9.7 and 9-1c are both `done`. |
| sprint-status.yaml ([_bmad-output/implementation-artifacts/sprint-status.yaml](../implementation-artifacts/sprint-status.yaml)) | **Yes** — needs the inline comment on the 9-1b line updated and `last_updated` bumped | **Updated by this proposal** |
| Story 9.7 spec ([_bmad-output/implementation-artifacts/9-7-tool-result-offloading-fetch-url.md](../implementation-artifacts/9-7-tool-result-offloading-fetch-url.md)) | No — authoritative for the upstream tool fix | **Do not modify** per Adam's instruction |
| Story 9-1c spec ([_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md](../implementation-artifacts/9-1c-first-run-ux-url-input.md)) | No — already correctly paused by the 9-1c SCP | **Do not modify** |
| 9-1c SCP ([sprint-change-proposal-2026-04-08-9-1c-pause.md](sprint-change-proposal-2026-04-08-9-1c-pause.md)) | No | **Do not modify** |
| Architectural finding report ([9-1c-architectural-finding-fetch-url-context-bloat.md](9-1c-architectural-finding-fetch-url-context-bloat.md)) | No — authoritative as written | **Do not modify** per Adam's instruction |
| scanner.md ([scanner.md](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md)) | No — modified during 9-1b *implementation* (Amelia's job after Bob writes the formal spec) | **Do not modify** in this SCP |
| analyser.md, reporter.md | No — same as scanner.md | **Do not modify** in this SCP |
| v2-future-considerations.md | No — Winston-owned in a separate pass | **Do not modify** |

### Technical Impact

- **No code is changed by this Sprint Change Proposal.** Paperwork only.
- The reshape adds AngleSharp to the future dependency footprint of `AgentRun.Umbraco/Tools/FetchUrlTool.cs`. **AngleSharp licence has been verified as MIT** — permissive, NuGet-distributable, no concerns for an open-source package shipping to NuGet.org. Recorded here so the paper trail captures the licence check.
- The reshape commits the project to a `extract: "raw" | "structured"` parameter on the existing `fetch_url` tool (not a separate sibling tool). This is the architect's locked decision and is recorded in Section 3.
- Story 9-1b's actual implementation work (when Bob writes the formal spec and Amelia implements it) will touch `AgentRun.Umbraco/Tools/FetchUrlTool.cs`, `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs`, the agent prompt files (scanner.md, analyser.md, reporter.md), and the project's csproj for the AngleSharp NuGet reference. **None of that work is in scope for this SCP.**

---

## Section 3 — Recommended Approach

### Path selected: Reshape Story 9-1b to absorb Option 2

Winston explicitly approved Option 2 in his architect response to the dev agent's finding report. The architect's exact framing was:

> "Approved. Server-side structured extraction is the right answer for the audit pattern, and 9-1b is the right place for it to land."

The architect also explicitly considered and rejected adding Option 2 as a separate sibling tool (e.g. `fetch_url_structured`). His framing:

> "Don't add `fetch_url_structured` as a separate tool. Make it a **mode** on `fetch_url` via an optional parameter — `fetch_url(url, extract: "structured" | "raw")` defaulting to `"raw"` for backwards-compatible shape."

Three reasons the mode-parameter approach was chosen over a sibling tool:

1. **One tool, one concept, two return shapes.** Easier to document, easier for future workflow authors to discover, less surface area in the JSON Schema (Story 9.2).
2. **The scanner's workflow.yaml `tools: [fetch_url, write_file]` declaration does not need to change.** The scanner.md prompt explicitly tells the agent to use `extract: "structured"` for audit work. Less churn in Story 9.7's downstream consumers.
3. **The default stays `"raw"`** because that's the generic, conservative choice for any future workflow that doesn't know what it wants — preserves backwards compatibility with anything Story 9.7 ships.

### Old scope vs new scope (verbatim, side-by-side)

**OLD scope (current epics.md outline, line 1300):**

> _As a developer evaluating AgentRun, I want the Content Quality Audit example workflow to produce an output I would happily paste into a client deck, So that my first experience of the package proves its value, not just its plumbing._
>
> _The story is a "tuning-and-testing loop with a quality gate" — iterate prompts until trustworthy._

**NEW scope (this SCP):**

> _As a developer evaluating AgentRun, I want the Content Quality Audit example workflow to produce an output I would happily paste into a client deck, So that my first experience of the package proves its value, not just its plumbing._
>
> _The story is a **two-phase build-and-tune** sequence:_
>
> _**Phase 1 (build):** Implement Option 2 — server-side structured extraction. Add an optional `extract: "raw" | "structured"` parameter to the existing `fetch_url` tool (default: `"raw"`, preserving backwards compatibility with Story 9.7's offloading shape). When `extract: "structured"` is set, fetch the page as today (SSRF / truncation / cancellation / Story 9.6 ceiling all preserved), parse it server-side using AngleSharp (MIT-licensed), and return a structured handle with title, meta description, headings, word count, image counts, and link counts. Update scanner.md / analyser.md / reporter.md to consume the structured return shape and strip the prompt complexity that the new contract makes obsolete (specifically: the "defending against context bloat" sections of the current scanner.md, which are no longer needed because the parsing happens server-side and the model never sees raw HTML)._
>
> _**Phase 2 (tune):** Iterate prompts on top of the new contract until the trustworthiness gate passes against ≥3 representative real test inputs. The 5-pass soft cap with architect escalation applies to Phase 2 only — Phase 1's build work is bounded by the parser library, not by iteration cycles._
>
> _The trustworthiness gate criterion is unchanged: Adam, acting as the reviewer, would be willing to run this workflow against a paying client's live site without supervision and trust the output. The signoff artefact and 5-pass cap stay where they are._

### Implementation approach (high level — for the formal spec to expand later)

When Bob writes Story 9-1b's formal spec via `bmad-create-story` after 9.7 and 9-1c are both `done`, the spec should reflect:

1. **AngleSharp is locked.** MIT-licensed, actively maintained, modern .NET targeting, DOM-faithful API. Not currently in the project's dependency tree — adding the NuGet reference is part of Phase 1.
2. **The `extract` parameter is added to `fetch_url`'s schema.** Optional. Enum: `"raw" | "structured"`. Default: `"raw"`. The existing JSON Schema for the tool gets updated.
3. **The structured return shape** (locked from the finding report's Option 2 section, with the architect's added `truncated_during_parse` field):
   ```json
   {
     "url": "https://example.com/page",
     "status": 200,
     "title": "Example - Home",
     "meta_description": "...",
     "headings": { "h1": [...], "h2": [...], "h3_h6_count": 47 },
     "word_count": 2341,
     "images": { "total": 84, "with_alt": 79, "missing_alt": 5 },
     "links": { "internal": 312, "external": 89 },
     "truncated": false,
     "truncated_during_parse": false
   }
   ```
   - `truncated` cooperates with Story 9.6's `max_response_bytes` ceiling at the byte-stream layer (same field as in Story 9.7's `extract: "raw"` handle).
   - `truncated_during_parse` is NEW and `extract: "structured"`-specific. It is `true` if AngleSharp had to parse a document that hit the byte ceiling before parsing, in which case the structured fields may be incomplete and the agent needs to know.
4. **AngleSharp selectors map cleanly to every field:**
   - `title` → `document.Title`
   - `meta_description` → `document.QuerySelector("meta[name=description]")?.GetAttribute("content")`
   - `headings.h1` / `h2` → `document.QuerySelectorAll("h1").Select(e => e.TextContent.Trim())`
   - `headings.h3_h6_count` → `document.QuerySelectorAll("h3, h4, h5, h6").Length`
   - `word_count` → `document.Body.TextContent.Split(WhitespaceChars, RemoveEmpty).Length`
   - `images.total` / `with_alt` / `missing_alt` → `document.QuerySelectorAll("img")` then count by `string.IsNullOrEmpty(img.GetAttribute("alt"))`
   - `links.internal` / `external` → `document.QuerySelectorAll("a[href]")` then classify by parsing the source URL's host
5. **Test sizing for the parser layer is the same three sizes as Story 9.7:** ~100 KB / ~500 KB / ~1.5 MB. **Reuse the same captured fixtures** that Story 9.7 will check in from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl`. The fixtures will already exist by the time 9-1b begins implementation. Do NOT capture new fixtures.
6. **Prompt simplification opportunity (for Phase 1's prompt-rewrite work):** The current scanner.md spends significant verbiage defending against context bloat ("Critical: Interactive Mode Behaviour", the five hard invariants, the post-fetch → write_file invariant, the standalone-text invariant). After Phase 1 ships, several of these defences are no longer load-bearing because the new contract eliminates the failure mode upstream. Specifically: the five hard invariants stay as-is (they govern *when* the agent calls tools, not *what the tools return* — see [Story 9-1c spec](../implementation-artifacts/9-1c-first-run-ux-url-input.md) which Adam committed as the partial milestone), but the verbose framing around "do not narrate after fetch" can be tightened, and the `read_file`-cached-content-inspection prose from Story 9.7's contract bridge can be removed entirely once the scanner's parsing work is done by AngleSharp instead. The polish loop should produce *simpler* prompts than today, not more complex ones.

### Why this is the right path

There is no alternative path that ships a meaningful private beta. Continuing with the old "iterate prompts until trustworthy" scope would put the polish loop on a non-convergent target — the model's parsing work is structurally non-deterministic regardless of how the prompt is written. The reshape moves the parsing work to a real parser, leaving the polish loop with a tractable problem (tune reasoning over deterministic facts).

### Effort estimate

- **This Sprint Change Proposal (paperwork):** Already drafted. ~30 minutes of writing time, no code changes.
- **Story 9.8 (retry-replay degeneration) creation via `bmad-create-story`:** Adam's next-after-this ceremony. ~1–2 hours of spec writing.
- **Story 9-1b formal spec writing (Bob, after 9.7 and 9-1c are `done`):** ~2–3 hours given the additional implementation surface (AngleSharp integration, schema update, parser unit tests).
- **Story 9-1b implementation (Amelia, after spec):** Phase 1 build = AngleSharp NuGet add + ~200–300 lines C# (parser code path + tests) + scanner.md / analyser.md / reporter.md rewrite. Phase 2 tune = iterative polish loop bounded by the 5-pass cap. The polish loop is unbounded in cycles but bounded in passes; if 5 passes do not converge, the architect escalation kicks in.
- **Total epic 9 critical-path impact:** Negligible. 9-1b was already gated behind 9-1c being `done`, which was already gated behind 9.7 shipping. The reshape adds Phase 1 build work to 9-1b's body but does not change the position of 9-1b in the Epic 9 sequence.

### Risk assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| AngleSharp's API doesn't map as cleanly to one of the structured return fields as I expect | Low | Low | I mentally validated each field against AngleSharp's actual selector API before writing this SCP. All six fields have clean mappings (see Section 3). If a field turns out to be awkward in practice during implementation, the formal spec writing pass (Bob, post-9.7) is the right place to surface it; this SCP locks the *intent* of the shape, not the exact selectors. |
| The Phase 1 build work is bigger than the estimate | Medium | Low | Phase 1 is bounded by AngleSharp's API surface and the existing FetchUrlTool structure. There is no novel design work — the parser library does the parsing, the existing tool plumbing does the rest. If the build work spills, the polish loop in Phase 2 has 5 passes of slack before architect escalation. |
| The polish loop doesn't converge in 5 passes even with deterministic parsing | Low | Medium | The architect escalation path is the existing 5-pass soft cap mechanism. If it triggers, Winston rethinks the prompt structure. This is the same fallback as the original 9-1b scope, just on a more tractable problem now. |
| Test fixtures from instance `caed201cbc5d4a9eb6a68f1ff6aafb06` are not preserved by the time 9-1b implementation begins | Very low | Low | Story 9.7's spec already mandates extracting and committing those fixtures as part of 9.7's Definition of Done. They will be in the repo before 9-1b touches them. If somehow they're not, Bob's formal-spec pass is the right place to catch it. |
| Adding AngleSharp causes a NuGet dependency conflict with existing transitive dependencies (Umbraco / Microsoft.Extensions.AI / etc.) | Very low | Medium | AngleSharp is a self-contained HTML parser with no overlap with Umbraco or M.E.AI's dependency tree. Standard targeting netstandard2.0 and net8.0+. No realistic conflict surface. |
| AngleSharp's licence changes from MIT before 9-1b ships | Effectively zero | High | Locked at MIT as of the 2026-04-08 verification. Recorded in this SCP. If somehow the licence changes between now and implementation, Bob's formal-spec pass should re-verify and escalate to Adam. |

### Decisions confirmed by Adam during the course-correction conversation 2026-04-08

1. **Q1 — Sonnet 4.6 parallel-tool-call stall edge case:** Option **(c)** — preserve the bullet but annotate it inline to record that the failure mode is now defended in two upstream layers (the sequential-fetch invariant in scanner.md from 9-1c's partial milestone, and the offloaded handle pattern from Story 9.7 which eliminates the context-bloat trigger). The polish loop in Phase 2 only needs to spot-check that both defences hold across the trustworthiness gate test set; the two original escalation paths (switch to Opus, revisit fail-fast) are no longer needed.

2. **Q2 — Retry-replay degeneration finding:** Promote to its own story. The annotation goes into 9-1b's edge-case section as option **(c)** (annotate-and-flag). Adam will run `bmad-create-story` after this ceremony to create a new story (working name: Story 9.8 — Retry-Replay Degeneration Recovery, final name TBD by Bob). 9-1b is no longer responsible for evaluating the retry-replay finding.

3. **Q3 — scanner.md prompt strengthening AC:** Option **(b)** — rewrite the AC to capture the actual work the polish loop needs to do on the prompts post-Option-2. The new AC reads (paraphrased): "Given scanner.md is rewritten as part of the Option 2 implementation, when reviewed post-rewrite, then it correctly consumes the structured return shape from `fetch_url(extract: 'structured')`, the five hard invariants from 9-1c's partial milestone are preserved, AND the contract-defence verbosity is stripped." The stale instance reference (`642b6...`) is fixed inline.

4. **Q4 — "5 runs of the workflow against the same real URL" structural-consistency AC:** Option **(b)** — strengthen to acknowledge the determinism property that AngleSharp gives us. The new AC captures three layers: byte-identical extracted facts (AngleSharp determinism), consistent flagged issues (model reasoning over deterministic facts is stable), and predictably ordered structural sections in the reporter's output.

5. **AngleSharp is locked.** MIT licence verified. Selector API friction check passed. Not interrupted for confirmation.

---

## Section 4 — Detailed Change Proposals

Two artifacts are modified by this Sprint Change Proposal. One additional artifact (a new Story 9.8 spec) is **deferred to Adam's next ceremony** (`bmad-create-story` for the retry-replay finding). One additional artifact (Story 9-1b's formal spec file) is deferred to a much later ceremony (Bob writing the formal spec via `bmad-create-story` after 9.7 and 9-1c are both `done`).

### Change 4.1 — Reshape Story 9.1b body outline in epics.md

**File:** [_bmad-output/planning-artifacts/epics.md](epics.md)
**Section:** Story 9.1b body outline (lines 1295–1367 in the current state)

**Edits:**

1. **Add a "Scope reshaped 2026-04-08" provenance note** at the top of the section, immediately after the existing "_Added 2026-04-07_" provenance note. The new note points to this SCP.

2. **Add a "Depends on" / "Blocks" block** at the top of the section (the section currently has no explicit dependency block — only the existing 9-1b outline references dependencies inline). Add:
   ```
   **Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done), 9.1a (CQA working skeleton — done), 9.7 (Tool Result Offloading for fetch_url — ready-for-dev, must ship first), 9.1c (First-Run UX — paused, must reach `done` before 9.1b starts implementation work)
   **Blocks:** 9.4 (Accessibility Quick-Scan — inherits the structured extraction pattern established here)
   ```

3. **Replace the user story / scope statement** (currently lines 1299–1302) with the new two-phase scope statement quoted in Section 3 of this SCP.

4. **Add a new section "Implementation Approach (Phase 1 — Build)"** immediately after the new scope statement and before the existing trustworthiness gate paragraph. The section captures the AngleSharp choice, the `extract: "raw" | "structured"` parameter design, the structured return shape (with the locked field set), the AngleSharp selector mappings for each field, and the test fixture reuse from Story 9.7.

5. **Preserve the trustworthiness gate paragraph and its `_Decision recorded 2026-04-07_` annotation exactly as-is.** The gate criterion is unchanged.

6. **Update the AC group structure:**
   - **AC group "Three-to-five real test inputs" (currently lines 1309–1315):** Preserve verbatim. The trustworthiness gate criterion is unchanged.
   - **AC group "5 runs against the same real URL" (currently lines 1317–1321):** Replace with the strengthened version per Q4 decision option (b). New text captures three properties:
     - byte-identical extracted facts (AngleSharp determinism)
     - consistent flagged issues (model reasoning over deterministic facts is stable)
     - predictably ordered structural sections in the reporter output
   - **AC group "Non-technical reviewer reads `audit-report.md`" (currently lines 1323–1328):** Preserve verbatim. The reviewer-perspective gate is unchanged.
   - **AC group "scanner.md prompt strengthening" (currently lines 1330–1336):** Replace with the rewritten version per Q3 decision option (b). New text reads:
     ```
     **Given** scanner.md is rewritten as part of the Phase 1 Option 2 implementation
     **When** reviewed post-rewrite
     **Then** it correctly consumes the structured return shape from `fetch_url(extract: "structured")` (title, meta_description, headings, word_count, images, links, truncated, truncated_during_parse)
     **And** the five hard invariants from 9.1c's partial milestone are preserved verbatim (verbatim opening line, between-fetches rule, sequential-fetch invariant, post-fetch → write_file invariant, standalone-text invariant)
     **And** the contract-defence verbosity (the "defending against context bloat" framing of the current scanner.md) is stripped because the new contract eliminates the failure mode upstream — the polish loop produces simpler prompts than today, not more complex ones
     **And** the analyser.md and reporter.md prompts are updated to consume the structured fields directly rather than re-parsing the agent's text
     ```

7. **Update the Process section** (currently lines 1338–1346) to reflect the two-phase structure:
   - Insert Phase 1 build steps before the existing iterate-and-tune steps
   - The existing iterate-and-tune steps become Phase 2
   - The 5-pass cap is annotated as applying to Phase 2 only

8. **Update the "What NOT to Build" section** (currently lines 1348–1355):
   - Preserve all existing bullets except: the bullet "_Do NOT add new tools (scanner/analyser/reporter use only what exists today)_" needs to be updated to clarify that adding the `extract: "structured"` *mode* to the existing `fetch_url` tool is permitted (and required), and that what is forbidden is adding a sibling tool like `fetch_url_structured`.
   - Add a new bullet: "_Do NOT add new tools as siblings of `fetch_url`. The architect explicitly rejected adding a separate `fetch_url_structured` tool. Option 2 is implemented as a `mode` parameter on the existing `fetch_url` tool._"
   - Add a new bullet: "_Do NOT capture new test fixtures. Reuse the captured fixtures from Story 9.7 (extracted from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl`)._"

9. **Update the Failure & Edge Cases section** (currently lines 1357–1365):
   - **Edge case "Sonnet 4.6 parallel-tool-call stall" (currently line 1364):** Preserve the bullet but add an inline annotation per Q1 decision option (c): _"[2026-04-08 annotation: This failure mode is now defended in two upstream layers — (i) the sequential-fetch invariant in scanner.md from Story 9-1c's partial milestone, and (ii) the offloaded handle pattern from Story 9.7 which eliminates the context-bloat trigger. The polish loop only needs to spot-check that both defences hold across the trustworthiness gate test set. The two original escalation paths (switch scanner to Opus, revisit Story 9.0's fail-fast decision) are no longer needed.]"_
   - **Edge case "retry-replay degeneration" (currently line 1365):** Preserve the bullet but add an inline annotation per Q2 decision option (c): _"[2026-04-08 annotation: This finding is being promoted to its own story and is no longer Story 9.1b's responsibility. Working name: Story 9.8 — Retry-Replay Degeneration Recovery (final name TBD by Bob). Adam will run `bmad-create-story` after the 9.1b course-correction ceremony completes. The retry-replay degeneration is an engine-state-machine bug in the ToolLoop / ConversationStore retry path; it is NOT fixable by Story 9.7 (offloading), Story 9.1b's Option 2 reshape (structured extraction), or polish-loop iteration on prompts. It needs its own story.]"_
   - All other edge case bullets preserved verbatim. **Fix the stale instance reference** in the existing "scanner.md prompt strengthening" AC group (Q3) — `642b6c583e3540cda11a8d88938f37e1` was the OLD failing instance from the original Story 9.0 stall era; the relevant instance for the 2026-04-07 finding is `caed201cbc5d4a9eb6a68f1ff6aafb06`. The Q3 AC rewrite handles this in passing.

10. **Preserve the trailing `_Full story spec to be created by SM before development begins._` line** verbatim. It correctly captures that the formal spec is still pending.

**Rationale:** The reshape captures every locked architectural decision from Winston's response without relitigating any of them. It preserves the trustworthiness gate, the 5-pass cap, the signoff artefact, and the existing edge case findings. The two annotation-style edits (Q1 and Q2) preserve the historical context that motivated the scope change without misleading future readers about the current state. The Q3 and Q4 rewrites convert ACs that were predicated on the old scope (prompt-only iteration on a flaky tool) into ACs that are predicated on the new scope (structured extraction with deterministic parsing). The dependency block at the top of the section makes the new 9.7 dependency explicit and correctly chains 9-1b behind 9-1c's resumption.

### Change 4.2 — Update sprint-status.yaml

**File:** [_bmad-output/implementation-artifacts/sprint-status.yaml](../implementation-artifacts/sprint-status.yaml)

**Edits:**

1. **Update the inline comment on the 9-1b line** — change from:
   ```
   9-1b-content-quality-audit-polish-and-quality: backlog        # depends on 9-1c (and on 9-0, 9-6 transitively)
   ```
   to:
   ```
   9-1b-content-quality-audit-polish-and-quality: backlog        # depends on 9-7 + 9-1c (and on 9-0, 9-6 transitively); scope reshaped 2026-04-08 — see sprint-change-proposal-2026-04-08-9-1b-rescope.md
   ```

2. **Update the `last_updated` line** — change from:
   ```
   last_updated: 2026-04-08 (story 9-7 created, 9-1c paused)
   ```
   to:
   ```
   last_updated: 2026-04-08 (story 9-1b scope reshaped)
   ```

3. **DO NOT change the `backlog` status.** The story is still backlog until 9.7 and 9-1c both reach `done`.

4. **DO NOT add or remove any other story lines.** Story 9.8 (retry-replay degeneration) will be added by Adam in the next `bmad-create-story` ceremony.

**Rationale:** Minimal change. Records the scope reshape in the inline comment so anyone reading sprint-status.yaml has a one-line pointer back to this SCP. The `backlog` status is preserved because nothing has changed about when work on 9-1b can begin.

### Change 4.3 — Write this Sprint Change Proposal

**File:** [_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope.md](sprint-change-proposal-2026-04-08-9-1b-rescope.md)

**Edit:** Create the file with the content of this entire document.

**Rationale:** Provides the canonical paper trail for the 9-1b reshape. Sibling document to the 9-1c SCP from earlier today. Anyone tracing the decision history will land on this file and have everything they need to understand what changed about 9-1b's scope, why, who decided it, and what happens next.

### Changes NOT made by this SCP (deferred to other ceremonies)

| Deferred change | Owner | Ceremony |
|---|---|---|
| Create Story 9.8 — Retry-Replay Degeneration Recovery (working name) | Adam | `bmad-create-story` immediately after this SCP's approval |
| Write Story 9-1b's formal spec file (`_bmad-output/implementation-artifacts/9-1b-content-quality-audit-polish-and-quality.md`) | Bob (SM) | `bmad-create-story` after 9.7 and 9-1c are both `done` |
| Implement Story 9-1b (AngleSharp integration, scanner.md / analyser.md / reporter.md rewrites, polish loop) | Amelia (Dev) | `bmad-dev-story` after Bob's spec is written |
| Architect-led updates to v2-future-considerations.md | Winston | Separate pass, not triggered by this SCP |
| Architect-led updates to architecture.md (recording the new "tool results are never just dumped into conversation context" safety principle) | Winston | Separate pass, not triggered by this SCP |

---

## Section 5 — Implementation Handoff

### Change scope classification

**Moderate** — story scope reshape; no formal spec exists yet so no spec file is touched; new dependency added (9.7) but it was already created by the 9-1c SCP; one new story (9.8) needs to be created in a follow-up ceremony but is not created by this SCP.

### Handoff plan

| Step | Owner | Action | Artifact produced |
|---|---|---|---|
| 1. This Sprint Change Proposal | Bob (SM) via `bmad-correct-course` | Write SCP, reshape 9-1b body in epics.md, update sprint-status.yaml | `sprint-change-proposal-2026-04-08-9-1b-rescope.md`, updated `epics.md`, updated `sprint-status.yaml` |
| 2. Create Story 9.8 — Retry-Replay Degeneration Recovery | Adam (PO) via `bmad-create-story` | Write the new story spec for the retry-replay finding. Final name TBD by Bob. Insert into Epic 9 (or push to Epic 10 if you decide the workaround "user retries up to N times" is acceptable for beta — that's a scope call you make during `bmad-create-story`). | `_bmad-output/implementation-artifacts/9-8-retry-replay-degeneration-recovery.md` (or whatever final name) |
| 3. Hand Story 9.7 to Winston for pre-implementation architect review | Adam | Send the Story 9.7 spec file to Winston for the PathSandbox interaction review gate (already in 9.7's spec at the bottom). Winston ticks the checkbox or escalates. | Architect signoff on 9.7 |
| 4. Implement Story 9.7 | Amelia (Dev) via `bmad-dev-story` | Build the offloaded raw response pattern in `FetchUrlTool`. Ship with regression tests. Capture and commit the test fixtures from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`. | Code change + tests on `main` |
| 5. Resume Story 9-1c Task 7 | Adam (manual E2E) | Re-run Task 7.1 through 7.10 against the new tool contract. Sign off if clean. Escalate to Winston if not. | Updated 9-1c spec status `done`, signoff artefact |
| 6. Write Story 9-1b's formal spec | Bob (SM) via `bmad-create-story` | Write the full Story 9-1b spec using the reshaped epic outline as input. Should include: AngleSharp integration tasks, the `extract` parameter schema update, the structured return shape, the parser unit tests reusing 9.7's fixtures, the scanner.md / analyser.md / reporter.md rewrite tasks, the Phase 2 polish loop with the 5-pass cap, the trustworthiness signoff artefact location and process. | `_bmad-output/implementation-artifacts/9-1b-content-quality-audit-polish-and-quality.md` |
| 7. Implement Story 9-1b Phase 1 (build) | Amelia (Dev) via `bmad-dev-story` | Add AngleSharp NuGet, implement the `extract: "structured"` mode in `FetchUrlTool`, rewrite the agent prompts to consume the structured return shape | Code change + tests + prompt updates on `main` |
| 8. Story 9-1b Phase 2 (tune) | Adam + Amelia | Run the polish loop against ≥3 real test inputs. Up to 5 passes. If the trustworthiness gate doesn't converge, architect escalation to Winston. | Trustworthiness signoff artefact, 9-1b status `done` |
| 9. Story 9.4 (Accessibility Quick-Scan) begins | Amelia (Dev) | After 9-1b is `done`, 9.4 inherits the `extract: "structured"` mode and builds the accessibility variant. | Story 9.4 done |

### Definition of "this Sprint Change Proposal is complete"

- ✅ This file exists at `_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope.md`
- ✅ Story 9.1b body outline in epics.md is reshaped per Section 4 (provenance note, dependency block, two-phase scope, implementation approach section, AC group updates, Process section update, What NOT to Build update, Failure & Edge Cases annotations)
- ✅ sprint-status.yaml has the 9-1b inline comment updated and `last_updated` bumped
- ✅ Adam has reviewed and approved the proposal
- ✅ Adam is ready to invoke `bmad-create-story` for Story 9.8 (retry-replay degeneration) immediately after this ceremony

### What this Sprint Change Proposal does NOT do

- ❌ Does not create Story 9.8 (deferred to `bmad-create-story` next, owned by Adam)
- ❌ Does not create Story 9-1b's formal spec file (deferred to `bmad-create-story` later, owned by Bob, after 9.7 and 9-1c are both `done`)
- ❌ Does not modify scanner.md, analyser.md, or reporter.md (preserved as-is — they get modified during 9-1b's implementation, not during the scope reshape paperwork)
- ❌ Does not modify the architectural finding report (authoritative)
- ❌ Does not modify the 9-1c SCP or the 9-1c spec (both finalised earlier today)
- ❌ Does not modify Story 9.7's spec (authoritative for the upstream tool fix)
- ❌ Does not modify v2-future-considerations.md or architecture.md (Winston-owned)
- ❌ Does not change Story 9-1b's status from `backlog`
- ❌ Does not add a pre-implementation architect review gate to Story 9-1b (the architect's locked decisions already cover everything; if the formal spec writing later surfaces design questions, that's the right time to involve Winston)
- ❌ Does not write any code

---

## Approval

**Approved by:** Adam (in advance of this ceremony, via the prompt that initiated `bmad-correct-course` on 2026-04-08, and confirmed via the four clarifying-question answers during the ceremony: Q1=c, Q2=hybrid annotate-and-flag with separate `bmad-create-story` follow-up, Q3=b, Q4=b)
**Architect concurrence:** Winston (decisions locked in prior planning conversation 2026-04-08, captured in the architect's response to the dev agent's finding report)
**AngleSharp licence verification:** MIT, confirmed during this ceremony
**Date:** 2026-04-08

---

_This Sprint Change Proposal is a point-in-time document. The architectural finding report ([9-1c-architectural-finding-fetch-url-context-bloat.md](9-1c-architectural-finding-fetch-url-context-bloat.md)) is the living technical reference for the underlying bug. The Story 9.7 spec ([9-7-tool-result-offloading-fetch-url.md](../implementation-artifacts/9-7-tool-result-offloading-fetch-url.md)) is the source of truth for the upstream offloading layer. Story 9-1b's formal spec (when written) is the source of truth for the AngleSharp implementation. This SCP captures the scope reshape decision and the trail._
