# Sprint Change Proposal — Story 9.7 DoD Amendment

**Date:** 2026-04-08
**Author:** Bob (SM), with Winston (Architect) approval
**Story affected:** 9-7-tool-result-offloading-fetch-url
**Type:** DoD wording amendment (NOT an implementation change)
**Status:** Approved — Story 9.7 moves to `done`

---

## Trigger

During the manual E2E for Story 9.7, Amelia surfaced an architectural finding documented in [9-7-architectural-finding-read-file-bloat.md](9-7-architectural-finding-read-file-bloat.md). Two manual instances tonight produced two distinct outcomes:

1. **Instance `d1ec618e0d884a5f9a83ea1468383b90`** — narration stall caused by `read_file` not being registered for the scanner step. **Fixed in-session** by adding `read_file` to the scanner step's `tools:` list in `workflow.yaml` and amending Invariant #4 in `scanner.md` from "post-fetch → write_file" to "post-fetch → read_file → write_file".
2. **Instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8`** — empty-turn stall after three sequential `read_file` calls accumulated ~1.34 MB of raw HTML in the conversation context. Same failure signature (`(empty)`) as the original `caed201cbc5d4a9eb6a68f1ff6aafb06` bug Story 9.7 was built to fix.

The conversation log for instance #2 confirmed that **every `fetch_url` `tool_result` block in the run was a JSON handle of 211–224 bytes** — well under the 1 KB ceiling. Story 9.7's offloading change is intact and working exactly as designed. The new failure path is `read_file` returning entire cached HTML payloads back into context, which is a separate architectural gap surfaced by — but not caused by — Story 9.7.

## Decision

**Story 9.7's implementation is correct and ships as-is. Only the Definition of Done wording is amended.**

The DoD over-promised by requiring full scanner workflow completion (write_file → step Complete) without `StallDetectedException`. Tonight's evidence proves that gate cannot be satisfied by 9.7 alone — it requires symmetric discipline at the `read_file` consumption end of the offloading pattern, which is out of scope for 9.7.

The DoD is being scoped down to **the fetch_url phase only**: every `fetch_url` `tool_result` block contains a JSON handle < 1 KB and zero raw HTML, and the fetch sequence completes without bloat-induced stalls. Tonight's instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8` already meets this scoped gate.

The full workflow-completion gate moves to a future combined release of **Story 9.7 + Story 9.1b (structured extraction lever) + a new Story 9.9 (read_file size guard)**. Those stories are handled in separate ceremonies, not this one.

## Architectural reasoning

Per the finding report's Root Causes #2 — **the symmetry principle**:

> Tool result offloading as practised by Claude Code, Anthropic Computer Use, and the Claude Agent SDK is a pattern that assumes the model never needs to read the offloaded content in full. The model's working memory stays bounded because the consuming side is *also* constrained.

Story 9.7 correctly disciplines the **write end** of the offloading pattern: `fetch_url` no longer dumps raw bodies into `tool_result` blocks. The **read end** — `read_file` — is a transparent passthrough by design. That trust is the gap. 9.7 is half of a symmetric pair; the other half belongs to subsequent stories.

This is not an implementation defect in 9.7. It is an architectural scope boundary that the DoD wording failed to honour. The "What this story does NOT solve" section of 9.7's spec already acknowledged that "this story does not eliminate the agent's need to parse HTML"; the original DoD line was internally inconsistent with that acknowledgement. This SCP closes that inconsistency.

## Strike-and-replace

**Existing DoD line (struck):**

> Manual E2E against the failing instance scenario `caed201cbc5d4a9eb6a68f1ff6aafb06` re-run end-to-end against the new tool contract — scanner step completes without `StallDetectedException`, conversation log contains handles (not raw HTML)

**Replaced verbatim with:**

> Manual E2E against the failing instance scenario caed201cbc5d4a9eb6a68f1ff6aafb06 re-run against the new tool contract — every fetch_url tool_result block in the conversation log contains a JSON handle < 1 KB and zero raw HTML; the fetch_url phase of the workflow completes without bloat-induced stalls. Full scanner workflow completion (write_file → step Complete) is gated on Story 9.1b shipping the structured extraction lever and Story 9.9 shipping the read_file size guard, and is explicitly NOT in 9.7's DoD. Tonight's evidence (instance 3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8) confirmed the fetch_url half of this gate is met; the workflow-completion half is deferred to the 9.7 + 9.1b + 9.9 combined release. See 9-7-architectural-finding-read-file-bloat.md for the trail.

## Session amendments that stay in place

The two amendments Adam authorised earlier this session under the "absorbed into 9.7" decision **stay in place** — they are correct precursors for any architectural lever 9.1b/9.9 will land, and are dead code without `read_file` being available to the scanner:

1. **`workflow.yaml`** — `read_file` added to the scanner step's `tools:` list.
2. **`scanner.md`** — Invariant #4 retitled "Post-fetch → read_file → write_file invariant" and amended to permit `read_file` calls (one per turn, no parallel) between the final `fetch_url` and the eventual `write_file`. The matching reminder paragraph in the "Writing Results" section also updated.

Neither amendment is reverted by this SCP. Both are preserved for the combined 9.7 + 9.1b + 9.9 release.

## Out of scope for this ceremony

- 9.7's tasks, ACs, or implementation code (untouched — only DoD wording, status, and provenance change)
- The architectural finding report (authoritative — untouched)
- The 9.1c pause SCP and the 9.1b reshape SCP (separate ceremonies)
- Creating Story 9.9 (separate ceremony)
- `scanner.md`, `workflow.yaml`, or any code changes
- `project-context.md` (Winston is updating it himself)

## Outcome

- Story 9.7 status → `done`
- DoD line struck and replaced as above
- Provenance note added to the spec immediately after the Status block, pointing to this SCP and the finding report
- `sprint-status.yaml` updated to reflect 9.7 done with an inline comment pointing to this SCP
- Workflow-completion gate explicitly deferred to the 9.7 + 9.1b + 9.9 combined release
