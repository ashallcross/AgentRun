# Addendum to SCP 2026-04-08 — Story 9.1b Rescope

**Date:** 2026-04-08 (evening)
**Type:** Addendum (additive paper trail — original SCP is untouched)
**Original SCP:** [sprint-change-proposal-2026-04-08-9-1b-rescope.md](sprint-change-proposal-2026-04-08-9-1b-rescope.md)
**Trigger:** Amelia's read_file context bloat finding — [9-7-architectural-finding-read-file-bloat.md](9-7-architectural-finding-read-file-bloat.md)
**Status:** Approved — Story 9.1b stays `backlog`; locked design unchanged; framing reframed

---

## Why this addendum exists

Story 9.1b's locked design from the original reshape SCP is **completely unchanged**: AngleSharp parser, two-phase build-and-tune sequence, structured return shape, trustworthiness gate, signoff artefact location, 5-pass soft cap on Phase 2. None of that moves.

What changes is the **weight of that lock**. Tonight's finding (read_file passing entire cached HTML payloads back into context after Story 9.7's offloading change shipped) proves that 9.1b is no longer a "polish on top of an already-working scanner" — it is the second of two reliability gates without which the scanner cannot complete a single end-to-end run. The original reshape SCP framed the structured extraction as a quality improvement; the manual evidence says otherwise.

This addendum reframes 9.1b's role and its position in the dependency chain. The implementation work itself does not change.

## The reframing

**Before (original reshape SCP framing):**

> Quality improvement on top of an already-working scanner. The 9.1b polish loop iterates prompts on top of 9.7's offloaded handle contract until the trustworthiness gate passes against representative real test inputs.

**After (this addendum):**

> Second reliability gate without which the scanner cannot complete a single end-to-end run. Story 9.7 disciplines the write end of tool-result offloading; Story 9.1b lands the structured extraction that disciplines the read end. Without the structured return shape, the scanner is forced to fall back to raw `read_file` against multi-MB cached HTML payloads, which deterministically reproduces the same context-bloat failure mode 9.7 was built to fix. The polish phase remains polish; the build phase is now beta-blocking.

The mental model shift: from "Phase 1 makes the scanner nicer; Phase 2 makes it trustworthy" to "Phase 1 makes the scanner *work*; Phase 2 makes it trustworthy."

## Operational implications

- **5-pass soft cap applies to the polish phase only (Phase 2).** Phase 1 (the build phase — AngleSharp integration, structured return shape, agents updated to consume it) has **no soft cap**. It ships when AngleSharp is integrated and the agents (scanner, analyser, reporter) consume the structured return shape correctly. This is unchanged from the original reshape SCP — that SCP already said "Phase 1's build work is bounded by the parser library, not by iteration cycles" — but it is worth restating because the reframing makes Phase 1 strictly beta-blocking and the cap distinction matters more now.

- **Story 9.1b is now strictly beta-blocking.** Previously implied by being upstream of the trustworthiness gate; now explicit. Without 9.1b's Phase 1 on `main`, the scanner workflow does not complete an end-to-end run and the private beta cannot ship.

- **The polish phase (Phase 2) inherits its existing 5-pass soft cap with architect escalation.** Unchanged.

## What does NOT change

- **AngleSharp is locked** (MIT-licensed, .NET-native, mature).
- **Structured return shape is locked** — title, meta description, headings, word count, image counts, link counts, status, content_type, truncated.
- **Two-phase build-and-tune scope is locked** — Phase 1 builds, Phase 2 tunes.
- **Trustworthiness gate criterion is unchanged** — Adam picks ≥3 representative real test inputs and judges output directly.
- **5-pass soft cap on Phase 2 is unchanged.**
- **Signoff artefact location is unchanged.**
- **Dependency on Story 9.1c reaching `done` before the polish phase begins is unchanged.**
- **Story 9.1b stays in `backlog`** — no status change in this ceremony. Status change happens when its upstream chain (9.7 done — already met, 9.1c done — pending, 9.9 created and shipped — pending) is resolved enough for spec authoring to begin.

## New dependency: Story 9.9

**Story 9.9 (read_file size guard — Option D from the finding report)** is added as a **parallel sibling** to Story 9.1b. Both must ship; both can be implemented in parallel. There is **no order dependency between 9.1b and 9.9** because they touch different files:

- **9.1b modifies** `AgentRun.Umbraco/Tools/FetchUrlTool.cs` (adds the structured-extraction code path; the existing handle contract from 9.7 is preserved as the default)
- **9.9 modifies** `AgentRun.Umbraco/Tools/ReadFileTool.cs` (adds a configurable byte-cap with truncation marker)

The two stories are functionally independent at the implementation level. They are coupled at the *outcome* level: the scanner workflow needs both to ship before its end-to-end completion gate is met (per the [9.1c pause addendum](sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md)).

Story 9.9 is created in **Ceremony 3** of this finding cycle. Reference it as "Story 9.9 — to be created."

## Note on formal spec authoring

When Bob writes the formal 9.1b spec via `bmad-create-story` (after Story 9.7 — done — and Story 9.1c — pending — both reach `done`), that spec must reference **both** of the architectural finding reports as part of the Authorisation / Context section:

- [9-1c-architectural-finding-fetch-url-context-bloat.md](9-1c-architectural-finding-fetch-url-context-bloat.md) — the original 2026-04-07 finding that motivated the reshape.
- [9-7-architectural-finding-read-file-bloat.md](9-7-architectural-finding-read-file-bloat.md) — tonight's finding that escalated the reshape's framing from quality improvement to second reliability gate, and added Story 9.9 as a sibling.

Both reports together are the architectural justification for 9.1b's locked design. Either alone is incomplete.

## Cross-references

- [Original 9.1b reshape SCP](sprint-change-proposal-2026-04-08-9-1b-rescope.md) — historical record, untouched
- [Original 9.1c pause SCP](sprint-change-proposal-2026-04-08-9-1c-pause.md) — historical record, untouched
- [9.1c pause addendum (parallel ceremony)](sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md)
- [9.7 DoD amendment SCP (Ceremony 1)](sprint-change-proposal-2026-04-08-9-7-dod-amendment.md)
- [Architectural finding — read_file context bloat](9-7-architectural-finding-read-file-bloat.md)
- [Original architectural finding — fetch_url context bloat](9-1c-architectural-finding-fetch-url-context-bloat.md)
