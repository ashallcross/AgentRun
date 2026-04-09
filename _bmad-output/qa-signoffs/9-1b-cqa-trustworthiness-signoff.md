# Story 9.1b — CQA Trustworthiness Signoff

**Date:** 2026-04-09
**Story:** 9-1b-content-quality-audit-polish-and-quality
**Signed by:** Adam Shallcross
**Status:** ✅ PASSED

## Gate definition (from story DoD)

> Trustworthiness signoff artefact exists, lists ≥3 representative real test inputs, and is signed by Adam.

The trustworthiness gate is subjective by design — it is Adam reading the produced audit-report.md and deciding whether he would send it to a paying client.

## Test inputs

Single representative real-world batch covering 5 distinct page types in one run:

| # | URL | Category | Why representative |
|---|---|---|---|
| 1 | `https://www.umbraco.com` | Marketing homepage | Long, image-heavy, navigation-rich enterprise CMS landing page (105 images, 17 H2s) |
| 2 | `https://www.wearecogworks.com` | Agency homepage | Short text-light page with heavy visual content (327 words, 23 images) — tests thin-content detection |
| 3 | `https://umbraco.com/products/umbraco-cms` | Product page | Long content marketing page (1401 words, 35 H2s, 118 images) — tests heading hierarchy and SEO depth |
| 4 | `https://www.bbc.co.uk/news` | News homepage | High-frequency content (2420 words, 95 H3-H6s, repeating "More top stories" H2s) — tests scale, freshness signals, accessibility duplicate-heading detection |
| 5 | `https://en.wikipedia.org/wiki/Content_management_system` | Reference article | No meta description (Wikipedia policy), high link density (276 internal / 90 external), formal encyclopaedic structure — tests missing-field handling, link classification, non-owned-domain awareness |

## Run details

- **Instance:** `64dc770a0d6b4ae2aea9a3f97454aa02`
- **Workflow:** `content-quality-audit`
- **Profile:** `anthropic-sonnet-4-6` (locked default)
- **Duration:** scanner step 09:22:37 → 09:23:41 (~1m); analyser 09:24:27 → 09:25:10 (~43s); reporter 09:25:29 → 09:26:22 (~53s); total wall-clock ~3m 45s including manual step advances
- **Tool calls:** scanner 5× `fetch_url` + 1× `write_file`; analyser 1× `read_file` + 1× `write_file`; reporter 2× `read_file` + 1× `write_file`
- **Stalls:** zero (every step completed cleanly with `FinishReason: stop` on the post-write narration turn)
- **Engine telemetry:** `engine.empty_turn.finish_reason = stop` on every step's final turn — clean voluntary completions, not truncations
- **Cumulative test suite at signoff time:** 442/442 passing

## Artefacts produced

All three artefacts are in `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/64dc770a0d6b4ae2aea9a3f97454aa02/artifacts/`:

1. **`scan-results.md`** — Deterministic parser facts for all 5 pages: titles, meta descriptions, word counts, headings (H1, H2, H3-H6 count), image counts (total, with-alt, missing-alt), link counts (internal/external). Two unprompted editorial notes added by the scanner agent (Cogworks low-word-count flag, BBC repeated-heading flag) demonstrate genuine reading rather than template-fill. Wikipedia null meta_description correctly recorded as "Not found." Spot-checked against engine logs — every numeric field matches the structured handle exactly.

2. **`quality-scores.md`** — Per-page scoring across 4 dimensions (SEO Completeness, Readability, Accessibility, Content Freshness) on a 1–10 scale with one-line justifications citing specific structured fields. Score range 6.0–7.5/10. Justifications cite exact character counts ("Title (52 chars)", "meta description (208 chars)") proving the analyser is doing arithmetic on the recorded fields rather than inventing reasoning. Cross-page summary correctly identifies missing freshness signals as the dominant pattern — emergent reasoning across the dataset, not per-page restatement.

3. **`audit-report.md`** — Three-section report (Executive Summary, Page-by-Page Findings, Prioritised Action Plan). Findings cite exact evidence ("Six identical H2 labels in a row are inaccessible for screen reader users and dilute SEO. Replace with distinct labels such as 'Top Stories — Politics' or 'Top Stories — World'"). Action plan is impact-ordered, not page-ordered. Wikipedia advice correctly hedged with "If you do not own this page, note it as an uncontrolled external asset" — domain judgement, not template-following. Executive Summary correctly sums 379 images across all pages with zero alt missing — emergent arithmetic, not stated in any input file.

## Trustworthiness assessment

**Would I send `audit-report.md` to a paying client?** Yes.

Specific reasons this passes:

- **Findings are evidence-cited and specific.** Every action item names the page it affects and gives a concrete example fix. No "consider improving accessibility" generic platitudes.
- **Cross-page reasoning is real.** The analyser identifies patterns the scanner did not (379 images / 0 missing alt is an emergent stat; "freshness signals are missing across all pages" is an emergent pattern).
- **Editorial judgement is sound.** The Wikipedia "uncontrolled external asset" hedge demonstrates the model recognised that audit recommendations need to be filtered through ownership/control.
- **Action plan ordering is impact-driven.** Items 1 and 7 both touch Umbraco pages but are ranked far apart because freshness fixes are higher leverage than heading-cleanup. Correct prioritisation reasoning.
- **Failure handling and fact accuracy are clean.** No hallucinated findings spot-checked. Every cited number matches the scan data.

## Known cosmetic gaps (NOT blockers, filed as deferred work)

These are real issues but they don't block beta launch and they don't require story 9.1b to stay open. They're filed in `deferred-work.md` for post-launch polish work:

1. **Date stamps in artefacts read `2025-07-10` (model training-cutoff default) instead of today's actual date.** The workflow does not currently pass a current-date variable into the agent prompts. Cosmetic only — does not affect any audit content. Trivial fix.

2. **The reporter occasionally over-scopes "remove navigation labels from H2 headings" findings on Umbraco pages.** The analyser/reporter conflates the site footer navigation with editorial content drift. Phase 2 polish item — give the prompt a hint that long flat H2 lists at the bottom of a page are likely navigation, not editorial drift.

3. **Wikipedia `Action #2` recommends "add a meta description to the Wikipedia CMS page" at #2 priority** — technically correct as a generic SEO finding but misranked because Wikipedia intentionally omits meta descriptions as a policy decision. The model partially saved itself with the "if you do not own this page" hedge but the action is still listed at #2 priority. Phase 2 polish item — teach the reporter to deprioritise actions for non-owned domains.

These are exactly the kind of items the Phase 2 polish loop exists to iterate on against real test inputs. With 3 of 5 polish passes still available, they can be addressed post-launch as the user feedback loop accumulates real evidence.

## Signoff

> The Content Quality Audit workflow produces genuinely useful, evidence-cited, actionable output on real public sites. The remaining issues are cosmetic and editorial-judgement polish, not correctness or quality failures. I would send `audit-report.md` to a paying client today and they would find it useful.
>
> Story 9.1b is approved for `done` status and ready for beta launch.
>
> — Adam Shallcross, 2026-04-09
