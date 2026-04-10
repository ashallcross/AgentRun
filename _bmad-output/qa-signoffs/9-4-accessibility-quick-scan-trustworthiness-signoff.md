# Story 9.4 — Accessibility Quick-Scan Trustworthiness Signoff

**Date:** 2026-04-10
**Story:** 9-4-accessibility-quick-scan-workflow
**Signed by:** Adam Shallcross
**Status:** ✅ PASSED
**Polish passes consumed:** 0 of 5

## Gate definition (from story DoD)

> Trustworthiness signoff artefact exists, lists representative real test inputs, and is signed by Adam. The same trustworthiness gate from Story 9.1b applies: Adam would be willing to send the report to a paying client without rewriting.

## Test inputs

Three runs across four instances, covering the AC checklist (4.A–4.E):

### Run 1 — Single URL (AC 4.C)

| # | URL | Category |
|---|---|---|
| 1 | Single test page (Adam's choice) | Known a11y issues |

- **Instance:** `e187460fbe8e48ecad9859b471a5e97b`
- Sequential fetch (1× `fetch_url`), `write_file` → reporter `read_file` + `write_file`. Completed cleanly.

### Run 2 — 3-URL batch (AC 4.D)

| # | URL | Category |
|---|---|---|
| 1–3 | Mix of clean and issue-laden pages (Adam's choice) | Sequential-fetch + severity bucketing validation |

- **Instance:** `3a054e366686459f911a79b9097b8a09`
- 3 sequential `fetch_url` calls (no parallel), `write_file` → reporter `read_file` + `write_file`. Completed cleanly.

### Run 3 — 5-URL batch including non-HTML + 404 (AC 4.E)

| # | URL | Category |
|---|---|---|
| 1 | `https://www.umbraco.com` | Marketing homepage |
| 2 | `https://www.wearecogworks.com` | Agency homepage |
| 3 | `https://umbraco.com/products/umbraco-cms` | Product page |
| 4 | `https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf` | Non-HTML (PDF) — tests Skipped bucket |
| 5 | `https://www.wearecogworks.com/aass` | HTTP 404 — tests Failed bucket |

- **Instance:** `fd8a5ab9efbc4d35a64be21c27a18f8a`
- 5 sequential `fetch_url` calls (no parallel, mixed delimiters handled), `write_file` → reporter `read_file` + `write_file`. Completed cleanly.
- PDF correctly recorded under **Skipped — non-HTML content** with content type `application/pdf`.
- 404 correctly recorded under **Failed URLs** with `HTTP 404: Not Found`.
- Reporter did not invent findings against skipped or failed URLs.

### CQA regression check

- **Instance:** `4f2f5580b95040bfaf003a20faa22448`
- Full 3-step CQA pipeline (scanner → analyser → reporter) completed cleanly against the same engine build. No regression.

## Additional checks

- **4.A (AC1):** Accessibility Quick-Scan appeared in the dashboard alongside Content Quality Audit. ✅
- **4.B (AC2):** Opening line fires on step start. Re-prompt fires on empty/non-URL messages and repeats correctly on subsequent non-URL messages. ✅
  - **Known cosmetic deviation:** Sonnet 4.6 drops `"from your own site or"` from the verbatim opening line. Not a blocker — the locked text is correct in scanner.md, this is a model prompt-following imperfection.
- **Sequential-fetch invariant:** Held on every run (1, 3, and 5 URL batches). No parallel `fetch_url` calls observed. ✅
- **Post-fetch → write_file invariant:** Held on every run. No text-only stall turns between final `fetch_url` and `write_file`. ✅
- **Engine telemetry:** `engine.empty_turn.finish_reason = stop` on every step's final turn — clean voluntary completions. Zero stalls.

## Run details

- **Workflow:** `accessibility-quick-scan`
- **Profile:** `anthropic-sonnet-4-6` (locked default)
- **Test suite at signoff time:** 465/465 passing
- **CQA backwards-compatibility:** canary test green; CQA run completed cleanly

## Known cosmetic gaps (NOT blockers)

1. **Opening line verbatim deviation:** Sonnet 4.6 drops `"from your own site or"` from the locked verbatim text. Prompt-following imperfection, not an engine bug. Polish-loop fixable but not blocking beta.
2. **Scanner post-`write_file` summary cites WCAG criteria:** The scanner's optional summary text after `write_file` included WCAG citations (including the non-locked `4.1.2`). This is cosmetic — the actual `scan-results.md` records facts correctly, and WCAG citation is the reporter's job. Polish-loop fixable.

## Trustworthiness assessment

**Would I send `accessibility-report.md` to a paying client?** Yes — as a quick-scan. This is a demo workflow to prove the engine generalises across two different task shapes (file-heavy CQA vs fetch-heavy accessibility). The reports are useful and evidence-cited. The remaining issues are cosmetic prompt-following deviations, not correctness failures.

## Signoff

> The Accessibility Quick-Scan workflow demonstrates that the AgentRun engine generalises beyond a single workflow shape. The scanner correctly records deterministic HTML structural facts, the reporter produces WCAG-anchored findings from the locked mapping, and the failure handling (PDF skip, 404 fail) works correctly. The remaining cosmetic gaps are prompt-following imperfections addressable in Phase 2 polish, not quality or correctness failures.
>
> Story 9.4 is approved for `done` status and ready for beta launch.
>
> — Adam Shallcross, 2026-04-10
