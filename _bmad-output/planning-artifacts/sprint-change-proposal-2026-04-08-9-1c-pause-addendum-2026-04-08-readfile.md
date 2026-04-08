# Addendum to SCP 2026-04-08 — Story 9.1c Pause

**Date:** 2026-04-08 (evening)
**Type:** Addendum (additive paper trail — original SCP is untouched)
**Original SCP:** [sprint-change-proposal-2026-04-08-9-1c-pause.md](sprint-change-proposal-2026-04-08-9-1c-pause.md)
**Trigger:** Amelia's read_file context bloat finding — [9-7-architectural-finding-read-file-bloat.md](9-7-architectural-finding-read-file-bloat.md)
**Status:** Approved — Story 9.1c stays paused; dependency chain extended

---

## Why a separate addendum exists

Two distinct events sit on a single continuous trail and the historical record needs to keep them distinct:

1. **2026-04-07 evening** — original 9.1c manual E2E surfaced the `fetch_url` context-bloat finding. That event paused 9.1c and authorised Story 9.7. Captured in the original pause SCP.
2. **2026-04-08 evening** — Story 9.7's manual E2E surfaced a *second* context-bloat code path on `read_file`. The fix Story 9.7 shipped is correct (write-end of the offloading pattern is disciplined) but does not satisfy the symmetric read-end constraint. Story 9.7 moved to `done` via DoD amendment in [Ceremony 1's SCP](sprint-change-proposal-2026-04-08-9-7-dod-amendment.md). The workflow-completion gate that 9.1c was waiting for has not yet been met — it is now waiting on more than just 9.7.

Editing the original SCP would erase the chronology. This addendum extends it.

## The dependency chain change

**Before (original SCP):**

```
9.1c paused → 9.7 done → resume Task 7 manual E2E
```

**After (this addendum):**

```
9.1c paused → (9.7 done AND 9.1b done AND 9.9 done) → resume Task 7 manual E2E
```

The dependency is **conjunctive** — none of the three on its own is sufficient to unblock Task 7. All three must ship together (or in any order, as long as all three are on `main`) before 9.1c's manual E2E gate can be re-attempted.

### Why each of the three matters

- **Story 9.7 (write side — DONE 2026-04-08).** Disciplines `fetch_url` so it returns a JSON handle < 1 KB instead of dumping raw HTTP bodies into `tool_result` blocks. This eliminates the original 2026-04-07 failure mode end-to-end (verified against instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8` — every `fetch_url` `tool_result` measured at 211–224 bytes). 9.7 is half of a symmetric pair; its half is now complete.

- **Story 9.1b (read side — primary fix).** Lands Option A from the finding report — **server-side AngleSharp structured extraction**. The agent never reads raw HTML. Per-page context cost collapses to ~1–2 KB. This is the architectural lever that closes the failure mode at the contract level: the model's working memory stays bounded because the consuming side is *also* constrained, satisfying the symmetry principle. The locked design (AngleSharp, structured return shape) is unchanged from the original 9.1b reshape SCP — what changes is the *role*. See the parallel [9.1b reshape addendum](sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md).

- **Story 9.9 (read side — defence-in-depth).** Option D from the finding report — **a configurable byte cap on `read_file`** with truncation marker. To be created in Ceremony 3. Defence-in-depth catch for any future workflow that bypasses the structured tool and falls back to raw `read_file` against a cached payload. Cheap (~50 lines C# + tests), doesn't fix the failure mode by itself, but bounds the blast radius of any future mistake. Sibling to 9.1b — no order dependency, both must ship.

## Architectural reasoning — symmetry principle

Per the finding report's Root Causes #2 (quoted briefly):

> Tool result offloading … is a pattern that assumes the model never needs to read the offloaded content in full. … The model's working memory stays bounded because the consuming side is *also* constrained.

Story 9.7 disciplined the write end. The read end (`read_file`) is a transparent passthrough by design — correct for its original purpose (small artifact files), incorrect for cached multi-MB HTML payloads. Closing the symmetry pair is what 9.1b + 9.9 do together. Full reasoning lives in the [finding report](9-7-architectural-finding-read-file-bloat.md) — see the full Root Causes section.

## What does NOT change

- **Story 9.1c stays `paused`.** No status change.
- **The partial milestone stays committed to `main`.** Tasks 1–6 are not being reverted.
- **The AC annotations from the original SCP stay exactly as recorded.** ACs #3 and #6 remain BLOCKED; ACs #7 and #8 remain met-in-prompt-only-pending-the-tool-contract; all other ACs remain MET. This addendum does not re-annotate them — the [9-1c-first-run-ux-url-input.md](../implementation-artifacts/9-1c-first-run-ux-url-input.md) dev-agent-ready spec remains the source of truth for AC state.
- **The five hard invariants from the partial milestone stay verbatim.**
- **The pause rationale and the partial milestone scope from the original SCP stand unchanged.**
- **The two session-authorised amendments from earlier today** (workflow.yaml `read_file` registration and scanner.md Invariant #4 amendment) **stay in place** — both are correct precursors for 9.1b's structured-extraction lever and for 9.9's size guard, regardless of which lands first.

## Cross-references

- [Original 9.1c pause SCP](sprint-change-proposal-2026-04-08-9-1c-pause.md) — historical record, untouched
- [Original 9.1b reshape SCP](sprint-change-proposal-2026-04-08-9-1b-rescope.md) — historical record, untouched
- [9.1b reshape addendum (parallel ceremony)](sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md)
- [9.7 DoD amendment SCP (Ceremony 1)](sprint-change-proposal-2026-04-08-9-7-dod-amendment.md)
- [Architectural finding — read_file context bloat](9-7-architectural-finding-read-file-bloat.md)
- [9.1c dev-agent-ready spec](../implementation-artifacts/9-1c-first-run-ux-url-input.md)
