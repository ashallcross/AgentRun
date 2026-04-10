# Story 9.4: Accessibility Quick-Scan Workflow

Status: done

**Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done), 9.1a (CQA working skeleton — done), 9.7 (Tool Result Offloading for `fetch_url` — done), 9.9 (read_file size guard — done), 9.1c (First-Run UX URL input — done), 9.1b (CQA Polish & Quality / structured extraction — done)
**Blocks:** 9.5 (Private Beta Distribution Plan — beta cannot ship until both example workflows are in)
**Inherits from:** 9.1c (verbatim opening line + zero-URL re-prompt pattern + interactive-mode invariants), 9.1b (server-side AngleSharp structured extraction pattern — the agent reasons over deterministic parser facts, never re-parses raw HTML)

> **BETA-SCOPED.** Second of two example workflows shipping in `1.0.0-beta.1`. The point of having two workflows is to prove the engine isn't a one-trick pony — accessibility deliberately exercises a different code path than CQA (less file munging, more fetch-based analysis). Trustworthiness gate from 9.1b applies verbatim: Adam, acting as the reviewer, would be willing to send the report to a paying client without rewriting it.

## Story

As a developer evaluating AgentRun,
I want a second pre-built example workflow that runs an accessibility quick-scan against URLs I provide,
So that I can see the engine handle a different kind of task than content quality audit, and I get a second piece of evidence that AgentRun can produce client-grade output on a real, salient agency concern.

## Context

**UX Mode: N/A — workflow files + agent prompts + minor `fetch_url` extension. No backoffice UI work, no engine state-machine changes.**

### Why two workflows for the beta

Locked by product-owner decision recorded in [epics.md Story 9.4 preamble (line 1666)](../planning-artifacts/epics.md#L1666). Reasons re-stated for the dev agent:

1. **Engine credibility.** A single workflow can be dismissed as a fluke. Two workflows that exercise different code paths (file-heavy CQA vs fetch-heavy accessibility) demonstrate the engine generalises.
2. **Agency salience.** Accessibility is a high-priority, regulated, and increasingly litigated concern in 2026. A working accessibility scanner is something agency reviewers will actually try against their own sites.
3. **Achievable in the beta window.** The prompt complexity is moderate; the structured-extraction pattern from 9.1b makes deterministic accessibility facts trivially available; no new tools are required.

### The architectural decision that unblocks this story

**Story 9.1b's `fetch_url(extract: "structured")` is reused, but the `StructuredFetchHandle` is extended additively with accessibility-relevant fields.** This is the same pattern 9.1b established for CQA — server-side AngleSharp parsing, deterministic facts handed to the agent, no raw HTML re-parsing in the prompt. Any temptation to fall back on raw HTML for "richer signals" is the exact failure mode 9.1b spent two reliability gates and a polish loop fixing. **Do not regress it.**

The additive-extension approach (rather than a new `extract: "accessibility"` mode) is locked because:

- The CQA scanner only consumes the existing fields (title, meta_description, headings, word_count, images, links). New fields appended to the handle do not change the bytes CQA reads — additive JSON is forward-compatible.
- One tool, one concept, multiple shapes was the explicit pattern Winston locked in 9.1b decision #2. Adding a third enum value re-opens that decision; adding fields does not.
- The trustworthiness gate evidence-citation discipline (reporter only flags issues it can directly cite from the structured fields) carries over verbatim. The accessibility reporter cites `semantic_elements.main`, the same way the CQA analyser cites `images.missing_alt`.

### Authorisation

This spec is authorised by:

- [epics.md Story 9.4](../planning-artifacts/epics.md#L1664) — story statement, ACs, "what NOT to build", failure & edge cases.
- [epics.md Epic 9 preamble (line 291–292)](../planning-artifacts/epics.md#L291) — second example workflow scope confirmation.
- Story 9.1b's locked decisions (parser=AngleSharp, additive structured handle, no raw-HTML re-parsing, trustworthiness gate) — all carry over byte-for-byte.

## Acceptance Criteria

**AC1 — Workflow appears in dashboard**
Given the package is installed and a profile is configured
When the developer opens the Agent Workflows dashboard
Then an "Accessibility Quick-Scan" workflow appears alongside Content Quality Audit
And it has 2 steps (scanner + reporter — see Task 2 for the two-step rationale)
And it runs in interactive mode

**AC2 — Welcoming first-run prompt (verbatim, mirroring 9.1c)**
Given the developer runs Accessibility Quick-Scan
When the scanner step begins
Then the agent's first message is **exactly**:
`Paste one or more URLs you'd like me to scan for accessibility issues, one per line. You can paste URLs from your own site or any public site you'd like a quick accessibility check on.`
And the agent does not greet, introduce itself, or narrate before this message

**AC3 — Sequential fetch + structured extraction**
Given the user pastes one or more URLs
When the scanner runs
Then the agent calls `fetch_url(url, extract: "structured")` exactly once per URL, **one URL per assistant turn** (never parallel — the sequential-fetch invariant from Story 9.1c carries over verbatim)
And the agent does not stall between fetches (Story 9.0 / 9.1c invariants enforced)
And after the **last** `fetch_url` returns, the agent's next turn calls `write_file` targeting `artifacts/scan-results.md` (Story 9.1c Invariant #4)

**AC4 — Deterministic HTML structural facts in scan-results.md (consumed by the agent for accessibility interpretation)**
Given each URL has been fetched
When the scanner writes `artifacts/scan-results.md`
Then for each successfully scanned page the file records the following **generic HTML structural facts** taken **directly** from the structured handle (no agent re-parsing). The runner exposes generic primitives; the accessibility scanner agent prompt is the layer that interprets them as WCAG-relevant findings:
- Total images, images with alt, images missing alt (existing 9.1b fields — `images.total`, `images.with_alt`, `images.missing_alt`)
- Heading sequence in document order (`headings.sequence`) — the agent computes "skipped heading levels" itself by walking the sequence in the prompt
- Anchor text samples (`links.anchor_texts`) capped at 100 entries in document order, plus `links.anchor_texts_truncated` — the agent applies the non-descriptive-link allowlist (the WCAG community-consensus list `"click here"`, `"read more"`, etc.) inside the **scanner prompt**, not in the runner
- Semantic HTML element presence (`semantic_elements { main, nav, header, footer }`) — booleans for either the semantic element OR the matching ARIA role. The "landmark" framing belongs in the agent prompt; the runner just reports HTML/ARIA structure.
- Form field count + label association (`forms { field_count, fields_with_label, fields_missing_label }`) — generic HTML structural fact, not a WCAG judgement
- Page language (`lang`) — `<html lang="...">` value, top-level scalar, `null` when absent or empty
- The same `truncated` flag from 9.1b

**AC5 — Skipped and failed buckets**
Given some URLs are non-HTML or fail
When the scanner writes `scan-results.md`
Then non-HTML URLs are recorded under a `## Skipped — non-HTML content` section with the content type from the structured-extraction error message (do **not** retry with `extract: "raw"` — Story 9.1b carry-over)
And HTTP errors / SSRF rejections / connection failures are recorded under a `## Failed URLs` section with the error or security reason

**AC6 — Reporter produces a prioritised, WCAG-anchored fix list**
Given `scan-results.md` is complete
When the reporter step runs
Then it reads `scan-results.md` (no other inputs needed) and writes `artifacts/accessibility-report.md`
And the report contains: an Executive Summary, a Page-by-Page Findings section, and a Prioritised Action Plan grouped by severity (Critical / Major / Minor)
And **every WCAG 2.1 AA criterion cited is one the agent can directly evidence from the facts recorded in `scan-results.md`** — for example, missing alt text → WCAG 1.1.1, skipped heading level (agent-computed from `headings.sequence`) → WCAG 1.3.1, missing form labels → WCAG 1.3.1/3.3.2, missing `<main>` semantic element → WCAG 1.3.1/2.4.1, non-descriptive link text (scanner-computed via case-insensitive allowlist match against `links.anchor_texts`) → WCAG 2.4.4, missing `lang` attribute → 3.1.1. The reporter MUST NOT cite contrast criteria (1.4.3, 1.4.6, 1.4.11) — the scanner does not measure contrast and the report does not address it. The reporter MUST NOT invent ARIA findings the scanner did not capture
And the report is written for a non-technical content manager — concrete, actionable, free of jargon
And the report stays under 200 lines

**AC7 — Trustworthiness gate (carries over from 9.1b)**
Given Adam runs the workflow against 3-5 representative real test inputs of his own choice (mix of his own sites + arbitrary public pages, including at least one page with known a11y issues and at least one page that is largely clean)
When he reviews the output
Then the same trustworthiness gate from Story 9.1b applies: **he would be willing to send the report to a paying client without rewriting**
And the agent flags real issues specific to the actual fetched content, not generic WCAG advice
And the agent does not hallucinate WCAG criteria references
And no hallucinated findings (no "missing alt text on 5 images" when the structured handle says zero)
And the trustworthiness signoff artefact `_bmad-output/qa-signoffs/9-4-accessibility-quick-scan-trustworthiness-signoff.md` exists with Adam's signoff once the gate passes

**AC8 — Tests green; no CQA regression**
Given the accessibility extension is added to `FetchUrlTool` and `StructuredFetchHandle`
When `dotnet test AgentRun.Umbraco.slnx` is run
Then all tests pass (currently 442 — must remain 442+)
And **no existing CQA scanner / analyser / reporter test changes are required** (additive extension — backwards compatible)
And new parser-layer regression tests cover the new accessibility fields against the existing checked-in fixtures (`fetch-url-100kb.html`, `fetch-url-500kb.html`, `fetch-url-1500kb.html`) — do not capture new fixtures

## Tasks / Subtasks

> **Phase ordering: Task 1 (engine) → Task 2 (workflow.yaml) → Task 3 (agent prompts) → Task 4 (manual E2E + trustworthiness gate).** Phase 1/2/3 are the equivalent of Story 9.1b's "build" phase and have no soft cap. Phase 4 is the equivalent of 9.1b's "polish" phase and inherits the same **5-pass soft cap with architect escalation** convention.

### Task 1 — Extend `StructuredFetchHandle` with accessibility fields (engine, AC4, AC8)

> **CRITICAL — POST-ARCHITECT-RE-REVIEW PATCH INSTRUCTIONS FOR AMELIA.** The original Task 1 implementation landed in working tree on 2026-04-10 against a spec that baked workflow-domain fields into the generic `FetchUrlTool` (see [feedback memory: Runner stays workflow-generic](../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_runner_stays_workflow_generic.md) and Change Log row 0.2). **Working tree is NOT being rolled back.** Adam's call: a lot of the existing code is correct under the new rules — keep it; patch the parts that aren't. The patch has been scoped precisely below so you don't have to mentally diff old-vs-new shape from scratch. Re-read Tasks 1.A → 3.B.i in this file before touching code; the corrected `StructuredFetchHandle` shape in Task 1.B and the rewritten Task 3 prompts are the source of truth.
>
> ### Patch list (FetchUrlTool.cs)
>
> **What stays untouched (Amelia got this right under the new rules):**
> - `IsFieldLabelled` helper — all four label-association mechanisms, multi-IDREF dereference, unresolved-IDREF non-throwing path. **Verbatim.**
> - `StructuredForms` record — already correctly named, no namespace prefix needed. Just promotes to a top-level property on `StructuredFetchHandle`.
> - `HasLandmark` helper logic — semantic element OR `[role~="..." i]` token match. Logic is correct; only the **name** changes.
> - `HeadingSequenceCap = 200` constant + the early-break walk pattern. Constant + algorithm survive; only the property it lives under changes.
> - Empty-body short-circuit pattern — the precedent is correct; only the field list inside changes.
>
> **What to delete:**
> 1. **`StructuredAccessibility` record** — DELETE entirely.
> 2. **`NonDescriptiveLinkAllowlist` private `HashSet<string>`** — DELETE. The allowlist moves into [`accessibility-quick-scan/agents/scanner.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/agents/scanner.md) as the source of truth (see Task 3.A point 7).
> 3. **`NormaliseLinkText` private helper** — DELETE. The corrected `links.anchor_texts` field preserves original case; the parser does NOT pre-lowercase.
> 4. **`BuildAccessibility(IDocument)` helper** — DELETE entirely. Its surviving pieces redistribute as follows:
>    - **Heading-sequence walk** → fold into the existing heading collection in `ParseStructured` so one walk produces `h1`, `h2`, `h3_h6_count`, AND `sequence` together. Apply the `HeadingSequenceCap = 200` early break inside that walk.
>    - **Form-fields/label walk** → extract to a new `BuildForms(IDocument)` helper. Body is the existing accessibility-block code minus the namespace.
>    - **Semantic-element checks** → extract to a new `BuildSemanticElements(IDocument)` helper that calls the renamed `HasSemanticElement` method.
>    - **Lang attribute lookup** → fold into `ParseStructured` directly as a one-liner.
>    - **DROP ENTIRELY:** the inline-style colour count loop and the non-descriptive link counting loop. Both gone, no replacement in the runner.
>
> **What to rename:**
> 5. **`HasLandmark` → `HasSemanticElement`.** Body unchanged.
> 6. **`StructuredLandmarks` → `StructuredSemanticElements`** with property + JsonPropertyName renames: `HasMain → Main` (`"main"`), `HasNav → Nav` (`"nav"`), `HasHeader → Header` (`"header"`), `HasFooter → Footer` (`"footer"`).
>
> **What to extend (additive — do not reorder existing fields, do not change existing JsonPropertyNames):**
> 7. **`StructuredHeadings`** — add one new property: `[JsonPropertyName("sequence")] IReadOnlyList<string> Sequence`. Existing `H1` / `H2` / `H3H6Count` stay byte-for-byte (CQA contract preserved).
> 8. **`StructuredLinks`** — add two new properties: `[JsonPropertyName("anchor_texts")] IReadOnlyList<string> AnchorTexts` and `[JsonPropertyName("anchor_texts_truncated")] bool AnchorTextsTruncated`. Existing `Internal` / `External` stay byte-for-byte.
> 9. **`StructuredFetchHandle`** —
>    - DROP the `Accessibility` property entirely.
>    - ADD three top-level properties: `[JsonPropertyName("forms")] StructuredForms Forms`, `[JsonPropertyName("semantic_elements")] StructuredSemanticElements SemanticElements`, `[JsonPropertyName("lang")] string? Lang`.
>    - All existing properties (`Url`, `Status`, `Title`, `MetaDescription`, `Headings`, `WordCount`, `Images`, `Links`, `Truncated`) stay in their current order with their current JsonPropertyName values. **Do not reorder existing fields.**
>
> **What to modify in `ParseStructured` (the existing parsing flow):**
> 10. **Anchor-walking loop** (currently around lines 422–443) — inside the same loop, additionally collect anchor text into a new `List<string> anchorTexts` with cap 100:
>     - For each anchor, compute `trimmed-and-whitespace-collapsed` text **preserving original case**. Do NOT call `.ToLowerInvariant()` — that's a deliberate divergence from the deleted `NormaliseLinkText` and is locked by Q2'.
>     - If the resulting string is empty (icon-only / image-only links), skip — do NOT add to `anchorTexts`. Still count it in `internal` / `external` (the existing 9.1b invariant `internal + external == document.QuerySelectorAll("a[href]").Length` MUST keep holding).
>     - If `anchorTexts.Count < 100`, add it. If `anchorTexts.Count == 100`, set `anchorTextsTruncated = true` and **stop adding** — but keep walking the rest of the loop for `internal` / `external` counts.
>     - **No deduplication.** Document-order repetitions preserved — frequency is signal.
> 11. **Heading-walking logic** — currently builds `h1` and `h2` lists separately. Refactor to one walk that produces `h1`, `h2`, `h3_h6_count`, AND `sequence`. Apply the 200-entry cap to `sequence` collection via early break (the deleted `BuildAccessibility` has the right pattern — copy it). The existing `h1`/`h2`/`h3_h6_count` collection is uncapped; only `sequence` is capped.
> 12. **`extract` parameter schema description** (around line 22) — replace the accessibility-block sentence with a generic one. **Do NOT use the word "accessibility" in the description** — these are generic HTML primitives. Suggested wording: _"...returns a small structured handle (title, meta description, headings including ordered tag sequence, word count, image and link counts, anchor text samples, form field counts with label associations, semantic HTML element presence, and page language)..."_
> 13. **`emptyStructured` constructor call** in the empty-body short-circuit (around line 187):
>     - DROP the `Accessibility:` named argument.
>     - ADD `Forms: new StructuredForms(0, 0, 0)`, `SemanticElements: new StructuredSemanticElements(false, false, false, false)`, `Lang: null`.
>     - Update the existing `Headings:` argument to provide an empty `Sequence`.
>     - Update the existing `Links:` argument to provide an empty `AnchorTexts` and `false` for `AnchorTextsTruncated`.
>
> ### Patch list (FetchUrlToolTests.cs)
>
> **What to delete:**
> - Any test asserting `accessibility.*` JSON paths — the namespace is gone.
> - Skipped-heading-levels detection tests (H1→H3 positive + monotonic negative) — engine no longer computes this; moved to scanner prompt.
> - Non-descriptive link allowlist case-insensitivity tests, trailing-punctuation test, anything asserting `non_descriptive_link_count` or `non_descriptive_link_samples`.
> - Inline-style colour count tests (case-insensitive match, ignoring other style properties).
>
> **What to rename (logic stays, only field paths and property names change):**
> - Landmark tests: `accessibility.landmarks.has_main` → `semantic_elements.main` (and the other three).
> - Form tests: `accessibility.forms.field_count` → `forms.field_count` (and the rest).
> - Lang tests: `accessibility.lang_attribute` → `lang`.
> - Heading-sequence tests (including the 200-entry cap test): `accessibility.heading_sequence` → `headings.sequence`.
>
> **What to add (the five anchor-text tests + one extra labelledby case):**
> - Cap enforcement: synthetic fixture with 150 anchors, assert `anchor_texts.Count == 100` AND `anchor_texts_truncated == true` AND `internal + external == 150`.
> - Empty-text exclusion: fixture with one `<a href="...">` wrapping only an `<img>` (no visible text), assert the anchor IS counted in `links.internal`/`external` but is NOT in `anchor_texts`.
> - Whitespace collapse: fixture with `<a> Read\n\tour guide </a>`, assert `anchor_texts` contains exactly `"Read our guide"`.
> - Case preservation: fixture with `<a>Click Here</a>`, assert `anchor_texts` contains `"Click Here"` (NOT lowercased).
> - Determinism: parse the same fixture twice, assert identical `anchor_texts` lists.
> - **`aria-labelledby` unresolved-IDREF non-throwing path** (Q3' addition): one resolved non-empty IDREF + one unresolved IDREF → field counts as labelled, no throw.
>
> **What to modify (existing tests that need their assertions updated):**
> - `HandleShape_HasExactlyExpectedFields_AndIsUnder1KB` — replace `accessibility` in the expected top-level field set with `forms`, `semantic_elements`, `lang`. The under-1KB property should still hold (the new shape is smaller — dropped a nested namespace plus the inline-colour and non-descriptive-link counts).
> - `Extract_Structured_NotInvokedOn_EmptyBody` — assert the new top-level zero-valued blocks instead of the zero-valued accessibility block.
> - The three fixture-backed regression tests (`fetch-url-100kb.html` / `500kb.html` / `1500kb.html`) — assert presence of the new field paths. The determinism assertion now applies to `headings.sequence` AND `links.anchor_texts`.
>
> **What stays untouched:**
> - The CQA backwards-compatibility shape assertion test — still asserts pre-9.4 fields are unchanged. That's still the right test.
> - All four label-association mechanism tests (`for`/`id`, wrapping `<label>`, `aria-label`, `aria-labelledby` with multi-IDREF + empty-IDREF cases). Logic unchanged; only the path renames apply.
> - All four `semantic_elements` toggle tests (semantic element + role attribute fixtures). Logic unchanged.
> - All `lang` present/absent tests. Logic unchanged.
>
> ### Patch list (workflow files)
>
> - **`accessibility-quick-scan/workflow.yaml`** — keep as-is. It's correct under both old and new shapes.
> - **`accessibility-quick-scan/agents/scanner.md`** — rewrite per the corrected Task 3.A in this file. The five hard invariants stay verbatim; the verbatim opening line stays; the structured-handle JSON example needs the new shape; the non-descriptive-link allowlist + skipped-heading-levels detection logic move INTO the prompt (Task 3.A points 7 + 8); the output template references the new flat field paths.
> - **`accessibility-quick-scan/agents/reporter.md`** — rewrite per the corrected Task 3.B. **Strike the entire `## Manual Review Notes` section** and the inline-colour prohibition block — both defended a field that no longer exists. WCAG 2.4.4 mapping now references "scanner-computed via case-insensitive allowlist match against `links.anchor_texts`" instead of the deleted parser count.
>
> ### Stale-reference safety check (run after patching, before running tests)
>
> Search the entire `AgentRun.Umbraco/Tools/FetchUrlTool.cs` and `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` files for these strings — **any surviving match is a stale rename and must be fixed before commit:**
>
> - `accessibility` (the namespace, the helper, anything)
> - `landmarks` / `Landmarks`
> - `lang_attribute`
> - `non_descriptive` / `NonDescriptive`
> - `inline_style_colour` / `InlineStyleColour`
> - `skipped_heading` / `SkippedHeading`
> - `BuildAccessibility`
> - `NormaliseLinkText`
> - `NonDescriptiveLinkAllowlist`
> - `StructuredAccessibility`
>
> Same sweep across `accessibility-quick-scan/agents/*.md` (the prompts) — the `accessibility.*` JSON paths in the old scanner.md output template and the JSON example block must all be gone.
>
> Then run `dotnet test AgentRun.Umbraco.slnx` per the locked memory. Tests must be green; the CQA backwards-compatibility assertion test is the canary — if it fails, the additive-only invariant has been broken and the patch needs revision.

- [x] **1.A** — Read [`AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) end-to-end. The relevant section is `ParseStructured` (around line 358) and the `StructuredFetchHandle` record (around line 463). The new fields are **additive and generic** — do not rename, reorder, or alter any existing field names, types, or JsonPropertyName values. CQA's scanner.md / analyser.md / reporter.md must continue to consume the existing shape unchanged. **Architectural rule (locked, non-negotiable):** every field added must be a generic HTML/web primitive that any future workflow could reuse — no workflow-domain vocabulary, no workflow-domain allowlists, no workflow-domain derived judgements. See Dev Notes → "Architectural inheritance from 9.1b" and the post-architect-re-review Pre-Implementation Architect Re-Review Gate at the bottom of this file.
- [x] **1.B** — Append the following **generic-only** fields to `StructuredFetchHandle`. The corrected canonical JSON shape (locked by architect re-review on 2026-04-10):

  ```json
  {
    "url": "...",
    "status": 200,
    "title": "...",
    "meta_description": "...",
    "headings": {
      "h1": [...],
      "h2": [...],
      "h3_h6_count": 47,
      "sequence": ["h1", "h2", "h2", "h3"]
    },
    "word_count": 2341,
    "images": { "total": 84, "with_alt": 79, "missing_alt": 5 },
    "links": {
      "internal": 312,
      "external": 89,
      "anchor_texts": ["Read our guide", "click here", "..."],
      "anchor_texts_truncated": false
    },
    "forms": {
      "field_count": 12,
      "fields_with_label": 9,
      "fields_missing_label": 3
    },
    "semantic_elements": {
      "main": true,
      "nav": true,
      "header": true,
      "footer": false
    },
    "lang": "en",
    "truncated": false
  }
  ```

  **There is NO `accessibility { }` namespace.** There is no `non_descriptive_link_count`, no `non_descriptive_link_samples`, no `skipped_heading_levels`, no `inline_style_colour_count`, no `landmarks`, no `lang_attribute`, no `NonDescriptiveLinkAllowlist` constant. All workflow-domain interpretation lives in the agent prompts under [`accessibility-quick-scan/agents/`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/agents/).

  **Field semantics (canonical — AC-bearing, locked by Winston 2026-04-10):**

  - **`headings.sequence`** — flat ordered list of heading tags (`"h1"`..`"h6"`) in document order, taken via a single `document.QuerySelectorAll("h1, h2, h3, h4, h5, h6")` walk. **Capped at 200 entries via early break during the walk** — do NOT materialise the full list and then `.Take(200)`. A pathological page with 50k headings must not allocate the full list. Append to the existing `StructuredHeadings` record (additive — `h1`, `h2`, `h3_h6_count` are unchanged). The agent computes "skipped heading levels" itself in the prompt by walking the sequence — that derivation is workflow-domain interpretation and does not belong in the runner.
  - **`links.anchor_texts`** — list of anchor text strings from `<a href>` elements, in document order. **Capped at 100 entries via early break during the existing anchor-walking loop** (currently `FetchUrlTool.cs:422-443`). When the cap is hit, **stop collecting into `anchor_texts` but keep walking** so the existing `internal` / `external` tallies remain uncapped — the invariant `internal + external == document.QuerySelectorAll("a[href]").Length` MUST continue to hold (existing 9.1b AC, do not break it).
    - **Normalisation:** trim + whitespace-collapse (collapse runs of any Unicode whitespace to a single space). **Preserve original case** — the parser does NOT pre-lowercase. The scanner agent prompt does the case-insensitive allowlist match itself; preserving case lets future workflows do case-sensitive analysis if they need it.
    - **Empty-text exclusion:** after trim+collapse, if the resulting string is empty (icon-only links, image-only links, `&nbsp;`-only links), the anchor is **not added** to `anchor_texts`. It is still counted in `internal` / `external`. Empty-text anchors are a real but distinct failure mode — the parser cannot disambiguate "icon link with aria-label" from "broken empty link" without ARIA name computation, which is domain logic.
    - **No deduplication.** Keep document-order repetitions. Frequency is signal — a page with `"click here"` appearing 40 times reads very differently from one with it appearing once.
  - **`links.anchor_texts_truncated`** — `bool`, `true` when the 100-entry cap was hit, `false` otherwise. Mirrors the top-level `truncated` flag's role: the agent's signal to caveat findings ("scanner examined the first 100 links on this page"). Without this flag, the scanner cannot tell "page has no non-descriptive links" from "page has 200 links and the problematic ones might be past the cap."
  - **`forms`** — top-level new block (NOT inside any namespace).
    - **`forms.field_count`** — count of `<input>`, `<textarea>`, `<select>` elements, **excluding** `input[type="hidden"]`, `input[type="submit"]`, `input[type="button"]`, `input[type="reset"]`, `input[type="image"]`.
    - **`forms.fields_with_label`** — count of those fields where one of the following is true:
      1. the element has an `id` and there exists a `<label for="<id>">` somewhere in the document
      2. the element is a descendant of a `<label>` element
      3. the element has a non-empty `aria-label`
      4. the element has an `aria-labelledby` attribute, and at least one of its space-separated IDREFs resolves to an element whose trimmed `TextContent` is non-empty. Concatenate text across all referenced elements; any single non-empty referenced element is sufficient. If an IDREF fails to resolve (no matching element in the document), it contributes empty text — it does NOT throw and does NOT disqualify other IDREFs in the list. This matches browser/AT behaviour.
    - **`forms.fields_missing_label`** — `field_count - fields_with_label`. Always non-negative.
  - **`semantic_elements`** — top-level new block. Booleans for the presence of either the semantic HTML5 element OR a matching ARIA role attribute. The role-attribute fallback stays because role attributes are generic HTML metadata, not a WCAG concept — ARIA is a web platform standard, "landmark" is the WCAG-flavoured lens over it. The field name is the tell; the helper checks both.
    - **`semantic_elements.main`** — `<main>` OR `[role~="main" i]`
    - **`semantic_elements.nav`** — `<nav>` OR `[role~="navigation" i]`
    - **`semantic_elements.header`** — `<header>` OR `[role~="banner" i]`
    - **`semantic_elements.footer`** — `<footer>` OR `[role~="contentinfo" i]`
    - The role-attribute selector uses `~=` (token match, case-insensitive) because the ARIA `role` attribute is a space-separated token list per spec.
  - **`lang`** — top-level scalar. Value of `<html lang="...">` if present and non-empty; otherwise `null`.
- [x] **1.C** — Truncation interaction: if AngleSharp parsed bytes that hit the byte ceiling (`truncated == true`), all the new fields (`headings.sequence`, `links.anchor_texts`, `forms.*`, `semantic_elements.*`, `lang`) are best-effort against partial content. Set them as you would for the existing fields — do not null them out, do not refuse to populate them. The agent's caveat signal is the existing top-level `truncated` flag. (`links.anchor_texts_truncated` is a separate concept — it specifically signals that the 100-anchor cap was hit, not that the byte stream was truncated. Both can be true independently.)
- [x] **1.D** — Empty-body short-circuit (lines ~187 in FetchUrlTool.cs): the existing `emptyStructured` `StructuredFetchHandle` constructor must be extended to provide zero-valued versions of all the new fields. Do not skip this — Story 9.1b's P5 code-review fix is the precedent (structured mode must always return the structured shape, even on empty body). On an empty body the new fields are: `headings.sequence: []`, `links.anchor_texts: []`, `links.anchor_texts_truncated: false`, `forms { 0, 0, 0 }`, `semantic_elements { false, false, false, false }`, `lang: null`.
- [x] **1.E** — Add parser-layer unit tests in [`AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs) covering:
  - **`headings.sequence` is present** in the JSON for `extract: "structured"` against a small synthetic HTML fixture, in correct document order (write the fixture inline in the test, do not check in a new file).
  - **`headings.sequence` cap enforcement:** synthetic fixture with 250 headings, assert `headings.sequence.Count == 200`.
  - **`links.anchor_texts` cap enforcement:** synthetic fixture with 150 anchors, assert `anchor_texts.Count == 100` AND `anchor_texts_truncated == true` AND `internal + external == 150` (the cap must not break the existing internal/external invariant).
  - **`links.anchor_texts` empty-text exclusion:** fixture with one `<a href="...">` wrapping only an `<img>` (no visible text content), assert the anchor is counted in `links.internal`/`external` but is NOT in `anchor_texts`.
  - **`links.anchor_texts` whitespace collapse:** fixture with `<a> Read\n\tour guide </a>`, assert `anchor_texts` contains exactly `"Read our guide"`.
  - **`links.anchor_texts` case preservation:** fixture with `<a>Click Here</a>`, assert `anchor_texts` contains `"Click Here"` (NOT lowercased).
  - **`links.anchor_texts` determinism:** parse the same fixture twice, assert identical `anchor_texts` lists.
  - **All four `semantic_elements` booleans toggle independently** on both semantic-element fixtures (`<main>`, `<nav>`, `<header>`, `<footer>`) AND `role`-attribute fixtures (`role="main"`, `role="navigation"`, `role="banner"`, `role="contentinfo"`).
  - **`forms.fields_with_label` correctly handles all four label-association mechanisms** (for/id, wrapping label, aria-label, aria-labelledby) and excludes `hidden`/`submit`/`button`/`reset`/`image` input types from `field_count`.
  - **`aria-labelledby` with two IDREFs**, one referenced element empty and one non-empty → field counts as labelled.
  - **`aria-labelledby` with a single IDREF** pointing to an empty `<span>` → field counts as missing label.
  - **`aria-labelledby` with an unresolved IDREF** (no matching element) plus a second resolved non-empty IDREF → field counts as labelled (unresolved IDREFs contribute empty text, do not throw, do not disqualify).
  - **`lang` is `null`** when `<html>` has no `lang` attribute, and contains the trimmed value otherwise.
  - **Regression coverage against the three existing fixtures** (`fetch-url-100kb.html`, `fetch-url-500kb.html`, `fetch-url-1500kb.html`): assert that all the new fields are present (non-null) on each, and that `headings.sequence` and `links.anchor_texts` are byte-identical across two consecutive parses of the same fixture (determinism — same guarantee 9.1b made for the existing fields).
  - **CQA backwards-compatibility test:** parse a fixture, assert that all existing field names (`title`, `meta_description`, `headings.h1`, `headings.h2`, `headings.h3_h6_count`, `word_count`, `images.total`, `images.with_alt`, `images.missing_alt`, `links.internal`, `links.external`, `truncated`) are still present and unchanged in shape. This test exists explicitly to fail loudly if anyone renames an existing field.
- [x] **1.F** — Update the `extract` parameter description in the `fetch_url` tool schema (around line 22 in FetchUrlTool.cs). Single-sentence amendment to mention the new generic fields (`headings.sequence`, `links.anchor_texts`, `forms`, `semantic_elements`, `lang`). **Do NOT use the word "accessibility" in the description** — these are generic HTML primitives, the description must reflect that.
- [x] **1.G** — Run `dotnet test AgentRun.Umbraco.slnx` (per the locked memory: never bare `dotnet test`). All tests must remain green. CQA-related tests must NOT require modification — if they do, the additive-only invariant has been broken and Task 1 needs revision.

### Task 2 — Create the Accessibility Quick-Scan workflow.yaml (AC1, AC3, AC5)

- [x] **2.A** — Folder `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/` with subfolder `agents/` already exists in working tree from the original implementation. **No action needed** — the folder layout was correct. Patch happens in place against the existing files.
- [x] **2.B** — `accessibility-quick-scan/workflow.yaml` already exists in working tree with the correct content. **No action needed** — `workflow.yaml` itself was correct under both old and new shapes; only the agent prompts and the runner-side fields were wrong. The reference content below is preserved for spec completeness; **do not overwrite the existing file**:

  ```yaml
  name: Accessibility Quick-Scan
  description: Scans pages for accessibility issues and produces a prioritised, WCAG 2.1 AA-anchored fix list
  mode: interactive
  # Change this to match your configured Umbraco.AI profile alias
  default_profile: anthropic-sonnet-4-6
  # Story 9.6 / 9.1b: marketing pages routinely exceed the 1 MB engine default
  # in size only marginally — bump to 2 MB and extend the per-request fetch_url
  # timeout to 30s to absorb slow CDNs. Same defaults as Content Quality Audit.
  tool_defaults:
    fetch_url:
      max_response_bytes: 2097152
      timeout_seconds: 30
  steps:
    - id: scanner
      name: Accessibility Scanner
      agent: agents/scanner.md
      tools:
        - fetch_url
        - write_file
      writes_to:
        - artifacts/scan-results.md
      completion_check:
        files_exist:
          - artifacts/scan-results.md
    - id: reporter
      name: Accessibility Report Generator
      agent: agents/reporter.md
      tools:
        - read_file
        - write_file
      reads_from:
        - artifacts/scan-results.md
      writes_to:
        - artifacts/accessibility-report.md
      completion_check:
        files_exist:
          - artifacts/accessibility-report.md
  ```

  **Why two steps and not three (CQA has scanner → analyser → reporter):** the analyser tier in CQA exists to score pages on four orthogonal quality dimensions before the reporter narrates them. Accessibility findings are not scored — they are categorised by severity and cited against WCAG criteria, work the reporter does directly from the structured facts. Adding an analyser tier here would be ceremony, not value. The two-step shape is also a deliberate pedagogical contrast for the beta reviewer: it shows that workflow length is not fixed.

  **Note the deliberate omission of `read_file` from the scanner step's tools list.** The scanner has nothing to read — `fetch_url(extract: "structured")` returns the structured handle directly into context, no cache file needs reading. Removing `read_file` is a small but real demonstration that workflow tool lists are scoped per step (a thing the documentation in Story 9.3 will lean on).

### Task 3 — Author the agent prompts (AC2, AC3, AC4, AC5, AC6)

> **Inheritance discipline.** The five hard invariants in Story 9.1c's scanner.md (post-9.1b) are the lock pattern. The accessibility scanner inherits them **byte-for-byte** with one substitution: the verbatim opening line and zero-URL re-prompt are accessibility-flavoured (see AC2). Everything else — interactive-mode rules, sequential-fetch invariant, post-fetch → write_file invariant, standalone-text invariant — is identical. Copy the structure from [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md). Do not improvise the invariants.

- [x] **3.A** — Create `agents/scanner.md` with:
  1. Role statement: "You are an accessibility scanner. Your job is to fetch web pages and record deterministic HTML structural facts that inform a WCAG 2.1 AA quick-scan."
  2. The five hard invariants verbatim (post → fetch_url, between-fetches, sequential-fetch, post-fetch → write_file, standalone-text). Copy from CQA scanner.md and substitute `scan-results.md` / `audit` → `scan-results.md` / `scan` only where the substitution is grammatical.
  3. The verbatim opening line from AC2 (locked).
  4. The verbatim zero-URL re-prompt: `I didn't catch any URLs in that message. Paste one or more URLs you'd like me to scan for accessibility issues, one per line — for example: https://example.com/about` (locked).
  5. URL-extraction rules: identical to CQA scanner.md (handle every separator, prepend `https://` if no scheme, dedupe permissively).
  6. Fetching rules: always `extract: "structured"`, one URL per turn, sequentially. Show the **corrected** structured handle JSON shape (the one from Task 1.B above — flat-shape, no `accessibility` namespace) as the source-of-truth example.
  7. **The non-descriptive-link allowlist is locked here in the prompt** as the source of truth — this is where WCAG community consensus belongs. The scanner reasons over `links.anchor_texts` (which preserves original case), applies a **case-insensitive exact match** against the locked allowlist `"click here"`, `"read more"`, `"here"`, `"more"`, `"link"`, `"learn more"`, `"this"` (after trimming + whitespace-collapsing the anchor text), counts the matches, and records the matched samples (up to 5 distinct, in document order). The allowlist lives **only here** — it is NOT in the runner. If the trustworthiness gate later wants the allowlist expanded, that decision is a prompt change in Phase 2 polish, not a runner change. Locked against engine drift.
  8. **The skipped-heading-levels detection is locked here in the prompt as the source of truth.** The scanner walks `headings.sequence` once, detects any forward jump greater than 1 (e.g. h1 → h3 with no h2 in between) **ignoring direction-down jumps** (h3 → h1 sectioning back to a top level is fine), and records `Skipped: Yes/No`. The agent computes this; the runner does not. The judgement of what constitutes a "skip" is the workflow's, not the parser's.
  9. **The anchor-texts truncation caveat:** when the recorded `links.anchor_texts_truncated == true`, the scanner adds the note `"Scanner examined the first 100 links on this page; non-descriptive link findings are scoped to that prefix."` to the page entry's Notes section. Without this caveat the report would silently overstate the completeness of its WCAG 2.4.4 coverage on link-farm / megamenu pages.
  10. Failure handling buckets: identical to CQA scanner.md (Failed URLs / Skipped — non-HTML / Truncation). Do **not** retry skipped pages with `extract: "raw"`.
  11. Output template — see Task 3.A.i below.
  12. The reminder paragraph at the bottom about Invariant #4 (post-fetch → write_file). Verbatim from CQA scanner.md.
  - [x] **3.A.i** — Output template for `artifacts/scan-results.md` (corrected — references the flat-shape fields, no `accessibility.*` paths):

    ````markdown
    # Accessibility Scan Results

    Scanned: [number] pages | Date: [today's date]

    ## Page: [page title]

    - **URL:** [url]
    - **Title:** [title from structured handle, or "Not found" if null]
    - **Lang:** [lang or "Not set"]
    - **Heading sequence:** [headings.sequence joined with " → "]
    - **Skipped heading levels:** [Yes / No — agent-computed from headings.sequence per Task 3.A point 8]
    - **Images:** [images.total] total | [images.with_alt] with alt text | [images.missing_alt] missing alt text
    - **Links:** [links.internal] internal | [links.external] external | [N] with non-descriptive text — agent-computed from links.anchor_texts via the locked allowlist [include matched samples in parentheses if any]
    - **Semantic elements:** main=[semantic_elements.main] · nav=[semantic_elements.nav] · header=[semantic_elements.header] · footer=[semantic_elements.footer]
    - **Forms:** [forms.field_count] fields | [forms.fields_with_label] labelled | [forms.fields_missing_label] missing labels
    - **Notes:** [e.g. "Response truncated at configured limit — partial content processed." OR "Scanner examined the first 100 links on this page; non-descriptive link findings are scoped to that prefix." Omit if not applicable. Both notes can apply.]

    [Repeat for each page]

    ## Skipped — non-HTML content

    - [url] — [content type]

    ## Failed URLs

    - [url] — [error message or security reason]
    ````

- [x] **3.B** — Create `agents/reporter.md` with:
  1. Role statement.
  2. Interactive-mode behaviour rules (copy CQA reporter.md verbatim — `read_file` first, then `write_file`, no text-only turns until done, summary only after `write_file`).
  3. **WCAG citation discipline (AC6 — this is the trustworthiness lever).** A locked instruction block stating: _"Only cite a WCAG 2.1 AA criterion you can directly evidence from the facts recorded in `scan-results.md`. The locked mappings are: missing alt text → 1.1.1 Non-text Content; skipped heading levels → 1.3.1 Info and Relationships and 2.4.6 Headings and Labels; missing form labels → 1.3.1 Info and Relationships and 3.3.2 Labels or Instructions; missing `<main>` semantic element (or `role="main"`) → 1.3.1 Info and Relationships and 2.4.1 Bypass Blocks; non-descriptive link text (detected by the scanner via case-insensitive allowlist match against `links.anchor_texts`) → 2.4.4 Link Purpose (In Context); missing `lang` attribute → 3.1.1 Language of Page. **You MUST NOT cite any other criterion.** Do not invent ARIA findings the scanner did not capture. Do not cite contrast criteria (1.4.3, 1.4.6, 1.4.11) — the scanner does not measure colour contrast and the report does not address it. When the scanner notes that anchor text examination was scoped to the first 100 links on a page, the WCAG 2.4.4 finding for that page must include a one-line caveat reflecting that scope."_
  4. Severity rubric (locked):
     - **Critical** — missing form labels, missing `<main>` semantic element, missing `lang` attribute on `<html>`.
     - **Major** — missing alt text on >10% of images, skipped heading levels, non-descriptive link text on >3 links.
     - **Minor** — missing alt text on ≤10% of images, missing `<nav>`/`<header>`/`<footer>`, non-descriptive link text on ≤3 links.
  5. Three-section output template (Executive Summary, Page-by-Page Findings, Prioritised Action Plan).
  6. ≤200 line cap on the report.
  - [x] **3.B.i** — Output template for `artifacts/accessibility-report.md`:

    ````markdown
    # Accessibility Quick-Scan Report

    Date: [today's date] | Pages scanned: [number]

    ## Executive Summary

    [3-5 sentences covering: overall accessibility posture, the single biggest concern, the single biggest strength, and a one-sentence recommendation. Anchor at least one sentence to a concrete count from scan-results.md.]

    ## Page-by-Page Findings

    ### [Page title]

    **Critical:**
    - [specific finding with WCAG citation, e.g. "3 of 12 form fields have no associated label (WCAG 1.3.1, 3.3.2)"]

    **Major:**
    - [...]

    **Minor:**
    - [...]

    [Repeat for each page. Omit severity headings with no findings.]

    ## Prioritised Action Plan

    Actions are ordered by impact — fix the top items first for the biggest accessibility gain.

    1. **[What to do]** — [Why it matters, which page(s) it affects, which WCAG criterion it addresses]
    2. ...
    ````

    **There is no `## Manual Review Notes` section.** The original spec carried one to defend against `inline_style_colour_count` being dressed up as a WCAG 1.4.3 finding. The corrected runner does not expose that field at all, so the defence is moot — there is nothing to dress up. If a future workflow or 9.4 polish pass surfaces a class of finding that the scanner can detect but cannot WCAG-cite, that pattern can be reintroduced **then**, against an actual concrete need, not pre-emptively.

### Task 4 — Manual end-to-end + trustworthiness gate (AC7)

> **Soft cap: 5 polish passes on the prompts**, mirroring 9.1b Phase 2. If the trustworthiness gate is not met after 5 passes, **stop and escalate to architect (Winston)**. Do not silently iterate forever. Track each pass in the Change Log section at the bottom of this file with a short note on what changed and why.

- [x] **4.A** — Restart the TestSite. Confirm the new "Accessibility Quick-Scan" workflow appears in the dashboard alongside Content Quality Audit (AC1).
- [x] **4.B** — Start an instance. Confirm the verbatim opening line (AC2). Reply with a deliberately empty / URL-less message and confirm the verbatim re-prompt fires.
- [x] **4.C** — Reply with a single URL — one of Adam's chosen test pages with known a11y issues. Confirm sequential fetching, structured-handle reasoning, `write_file` to `scan-results.md`, then reporter step runs and writes `accessibility-report.md`. Spot-check the report against AC6 (WCAG citations only from the locked mapping; severity rubric respected; non-developer language).
- [x] **4.D** — Repeat with a 3-URL batch — mix of clean and broken pages. Confirm the sequential-fetch invariant holds (no parallel `fetch_url` calls), no stalls, all three pages appear in the report with correct severity bucketing.
- [x] **4.E** — Repeat with a 5-URL batch including: at least one non-HTML URL (PDF, image), at least one HTTP-error URL (404), and at least one page known to be largely accessible. Confirm the Skipped and Failed sections of `scan-results.md` are populated correctly and the report does not invent findings against the failed/skipped URLs.
- [x] **4.F** — **Trustworthiness gate.** Adam reviews the audit-report against the gate criterion (AC7): would he send this to a paying client without rewriting? If yes — sign the gate artefact (Task 4.G). If no — note the failure mode in the Change Log, update the prompts (or, if a parser-side gap is found, escalate to architect — do not modify the engine inside Phase 4 without architect signoff), and re-run from 4.C. Track passes against the 5-pass soft cap.
- [x] **4.G** — Create [`_bmad-output/qa-signoffs/9-4-accessibility-quick-scan-trustworthiness-signoff.md`](../qa-signoffs/9-4-accessibility-quick-scan-trustworthiness-signoff.md) with: the URLs tested, the date, the pass number from the 5-pass cap, and Adam's signoff line. Mirror the 9.1b signoff artefact format.

### Task 5 — Definition of Done

- [x] All ACs met.
- [x] `dotnet test AgentRun.Umbraco.slnx` green; tests count ≥ 442; CQA tests untouched.
- [x] Trustworthiness signoff artefact exists at `_bmad-output/qa-signoffs/9-4-accessibility-quick-scan-trustworthiness-signoff.md`.
- [x] Sprint status updated: `9-4-accessibility-quick-scan-workflow: done`.
- [x] Change Log section in this file updated with phase notes (Phase 1 build, Phase 2 polish passes used).

## Failure & Edge Cases

- **User provides URLs with no accessibility issues** → the report still produces value: confirm what's done well, with a brief "no critical issues found" summary in the Executive Summary. Severity sections that have no entries are omitted, not filled with filler.
- **Agent conflates issues across pages** → the per-page output template structure prevents this if followed. Reporter prompt explicitly forbids cross-page aggregation in the Page-by-Page section (Action Plan section is the only place pages are grouped).
- **LLM hallucinates WCAG criteria references** → the locked WCAG mapping in the reporter prompt (Task 3.B.3) is the defence. Polish loop must catch and correct any hallucination by tightening the mapping wording, NOT by adding more criteria.
- **User pastes a URL whose page is too large for the configured fetch limit** → `truncated == true`. The structured handle still returns best-effort accessibility fields against the partial bytes. Scanner notes the truncation in the page entry; reporter caveats findings against truncated pages.
- **`lang` missing on a single-page application that injects lang via JS** → we cannot detect this — the structured handle reflects raw HTML only, no JS execution (locked: epics.md "do NOT add browser rendering or JavaScript execution"). The report should treat missing `lang` as a finding regardless. Document this honestly in the report wording — frame as "the rendered HTML did not include a `lang` attribute" rather than "the page is missing a lang attribute", so the user can disambiguate against their own JS framework.
- **Anchor-text cap hit on a megamenu / link-farm page** → `links.anchor_texts_truncated == true`. The scanner records the caveat note (Task 3.A point 9). The reporter's WCAG 2.4.4 finding for that page MUST include the one-line caveat reflecting the 100-link scope. Without the caveat, the report would silently overstate completeness on the most link-heavy pages — exactly the pages most likely to *have* non-descriptive links.
- **Parallel-fetch regression** (Sonnet issuing all `fetch_url` calls in one turn) → Story 9.0's stall detector + Story 9.1c's sequential-fetch invariant in scanner.md are the upstream defences. If this regresses during Task 4, the polish loop adjusts the scanner prompt's invariant wording — it does NOT modify the engine.
- **Non-HTML URL (PDF) provided** → `fetch_url(extract: "structured")` raises the locked content-type error. Scanner records under "Skipped — non-HTML content" and continues. Do not retry with `extract: "raw"` (carries over from CQA scanner.md decision).
- **`fetch_url` returns an empty body** → Story 9.1b P5 fix returns the structured shape with zero-valued fields. Task 1.D extends this to the new accessibility block. Reporter sees the page with zero counts and treats it as a failed scan in the report (one-line entry, no findings).
- **Reporter cites a WCAG criterion outside the locked mapping** → manual catch in Task 4.C / 4.F. Polish-loop fix is to tighten the prompt; do **not** expand the locked mapping during Phase 2 without architect signoff.

## Files in Scope

**Modified (engine source — Phase 1):**

- [`AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) — additive extension to `StructuredFetchHandle` and `ParseStructured`. Existing fields, the `extract` parameter schema enum, the SSRF protection, the byte-stream layer, the truncation marker behaviour, the empty-body short-circuit (extended to populate the new block), and the `ToolContextMissingException` are all preserved verbatim.

**Modified (tests — Phase 1):**

- [`AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs) — new parser-layer regression tests for the accessibility fields, plus a backwards-compatibility assertion test for the existing CQA fields.

**Created (workflow — Phase 1/2):**

- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/workflow.yaml`
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/agents/scanner.md`
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/agents/reporter.md`

**Created (Phase 4):**

- `_bmad-output/qa-signoffs/9-4-accessibility-quick-scan-trustworthiness-signoff.md`

**Explicitly NOT touched (out of scope — fail loudly if you find yourself reaching for these):**

- Any file under `workflows/content-quality-audit/` — CQA is done. Touching its prompts in this story would risk regressing the trustworthiness gate signed off in 9.1b.
- [`AgentRun.Umbraco/Tools/ReadFileTool.cs`](../../AgentRun.Umbraco/Tools/ReadFileTool.cs) — Story 9.9 territory. Not modified.
- [`AgentRun.Umbraco/Tools/WriteFileTool.cs`](../../AgentRun.Umbraco/Tools/WriteFileTool.cs) — out of scope.
- [`AgentRun.Umbraco/Engine/ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs) — Story 9.0's stall detector is correct.
- [`AgentRun.Umbraco/Engine/StepExecutor.cs`](../../AgentRun.Umbraco/Engine/StepExecutor.cs) — out of scope. The hardcoded `MaxOutputTokens=32768` from 9.1b stays as-is; Story 9.6.1 will revisit it.
- [`AgentRun.Umbraco/Security/SsrfProtection.cs`](../../AgentRun.Umbraco/Security/SsrfProtection.cs) and [`AgentRun.Umbraco/Security/PathSandbox.cs`](../../AgentRun.Umbraco/Security/PathSandbox.cs) — security perimeter, untouched.
- [`AgentRun.Umbraco/Engine/ToolLimitResolver.cs`](../../AgentRun.Umbraco/Engine/ToolLimitResolver.cs) — Story 9.6's resolution chain reused unchanged. **No new tunable is added** for the accessibility fields — they are always-on additions to the structured handle, not a workflow-author opt-in.
- The JSON Schema for `workflow.yaml` — forward dependency for Story 9.2. 9.2 picks up the accessibility-quick-scan workflow file as part of its sweep. **Do not block 9.4 on Story 9.2.**

## Dev Notes

### Architectural inheritance from 9.1b (read this before writing code)

Story 9.1b spent two reliability gates and a polish loop teaching the engine that **the agent must reason over deterministic parser facts, not raw HTML.** Every architectural choice in this story flows from that lesson:

- **`extract: "structured"` only.** The accessibility scanner never asks for `extract: "raw"`. If you find yourself wanting raw HTML to extract a signal that isn't in the structured handle, the answer is to **add the field to the structured handle** (which is what Task 1 already does for the seven accessibility-relevant fields). Re-parsing raw HTML in the prompt is exactly the failure mode 9.1b fixed.
- **Additive extension, not a new extract mode.** Locked rationale: see "The architectural decision that unblocks this story" in the Context section above. CQA's existing tests assert the existing field shape; appending fields is forward-compatible; renaming or restructuring is not.
- **Determinism over richness.** Every accessibility field in Task 1.B is a count, a boolean, an ordered enum sequence, or a small bounded list. None of them require LLM judgement to compute. The polish loop in Task 4 tunes how the agent **reasons** about these facts, never how it **extracts** them.
- **The five hard invariants from 9.1c carry over byte-for-byte.** Sonnet 4.6 still has the parallel-fetch failure mode; Story 9.0's stall detector is the engine-side defence; the scanner.md invariants are the prompt-side defence. Do not weaken either.

### CQA scanner.md is the canonical pattern

Read `content-quality-audit/agents/scanner.md` end-to-end before writing the accessibility scanner.md. The structure (role → invariants → opening line → URL extraction → fetching → failure handling → writing → output template → reminder) is locked. Substitute the verbatim opening line and re-prompt; copy everything else.

### Test fixture reuse

Reuse the captured fixtures already checked in by Story 9.7 / 9.1b at `AgentRun.Umbraco.Tests/Tools/Fixtures/`: `fetch-url-100kb.html`, `fetch-url-500kb.html`, `fetch-url-1500kb.html`. **Do NOT capture new fixtures.** Inline-write small synthetic HTML strings inside the new tests for targeted assertions (skipped heading detection, label association mechanisms, etc.).

### `dotnet test` invocation discipline

Locked memory: always invoke as `dotnet test AgentRun.Umbraco.slnx` (or the current slnx file in the repo root) — never bare `dotnet test`. Bare invocations have caused incorrect project resolution before.

### Engine-side exception types

If FetchUrlTool needs to surface a missing context error, use `ToolContextMissingException` (added in Story 9.9 D2), **not** raw `InvalidOperationException`. This is required for the LLM error classifier path to work correctly.

### Open questions for the architect (Winston) — RESOLVED

> **All four questions below were resolved by Winston on 2026-04-10. See the Pre-Implementation Architect Review Gate further down for the locked decisions and any spec edits flowing from them. The question text is preserved for traceability — do not read these as still-open.**

1. **[RESOLVED — see gate Q1]** **Heading sequence cap of 200 entries** — is 200 the right ceiling, or should it match an existing cap convention in the codebase? (CQA uses `h3_h6_count` rather than a list specifically to avoid this question. The accessibility scanner needs the actual sequence to detect skipped levels deterministically.)
2. **[RESOLVED — see gate Q2]** **Non-descriptive link text allowlist** — the locked list in Task 1.B (`"click here"`, `"read more"`, `"here"`, `"more"`, `"link"`, `"learn more"`, `"this"`) is from rough WCAG guidance and a11y community consensus, not a formal spec. Confirm the allowlist is final before implementation, or specify a different one. **Do not let the polish loop expand it** — if it expands, that is a parser-side change requiring architect signoff.
3. **[RESOLVED — see gate Q3]** **Form-label fourth mechanism (`aria-labelledby`)** — should the parser dereference the `aria-labelledby` IDREF and verify the referenced element has non-empty text, or just check the attribute exists? The story locks "dereference and verify non-empty" — confirm or override.
4. **[RESOLVED — see gate Q4]** **Inline-style colour count phrasing** — the field is intentionally framed as a hint, not a finding, because we cannot compute contrast without rendered CSS. Confirm the reporter prompt's "manual review recommended" framing is sufficient defence against the LLM dressing this up as a 1.4.3 violation, or specify stricter wording.

These questions are written to be resolved by Winston in a single pre-implementation review block, **mirroring the 9.1b pre-implementation architect review gate.** Amelia does not start Task 1 until this block is ticked.

### Pre-Implementation Architect Review Gate (OBSOLETE — superseded by Re-Review Gate below)

> **OBSOLETE 2026-04-10.** This gate was signed by Winston on 2026-04-10 and Amelia executed against it. Adam caught a runner-genericity violation during in-flight QA review the same day — the spec the gate signed off baked workflow-domain fields (`accessibility { non_descriptive_link_count, landmarks, inline_style_colour_count, ... }` plus a hardcoded WCAG-community allowlist) into a generic tool. **Winston re-reviewed and overrode his own 9.1b AC #9 hedge** (see [9-1b spec line 212 erratum](9-1b-content-quality-audit-polish-and-quality.md), 2026-04-10), confirmed Adam's stricter rule that nothing in `AgentRun.Umbraco/Tools/` may carry workflow-domain code, and signed off the corrected shape in the **Pre-Implementation Architect Re-Review Gate** at the very bottom of this file. The Q1–Q4 history below is preserved verbatim for audit trail. **Do not implement against this block — implement against the Re-Review Gate below.**

- [x] **Q1 (heading sequence cap): 200 confirmed.** No existing cap convention to match — `headings.h1` and `headings.h2` in the current `StructuredFetchHandle` are uncapped lists, so 200 is a *new* defensive ceiling rather than an inconsistency with prior art. Each entry is a 2-char tag (`"h1"`..`"h6"`), so 200 entries is trivial in payload terms and I'd happily go higher, but 200 covers >99% of real pages and the truncation failure mode is benign: `skipped_heading_levels` is computed by walking the (capped) sequence, so any skip that exists within the first 200 headings is still caught — only skips that occur *after* the 200th heading are missed, and a page with 200+ headings has bigger problems than a single missed skip detection. Lock 200. Implementation note: the cap must be applied during the `QuerySelectorAll` walk, not after — do **not** materialise the full list and then `.Take(200)` on a pathological 50k-heading page. Use a `for` loop with an early break.

- [x] **Q2 (non-descriptive link allowlist): locked verbatim — final.** The list `"click here"`, `"read more"`, `"here"`, `"more"`, `"link"`, `"learn more"`, `"this"` is the canonical community-consensus set and matches what every major a11y auditing tool ships out of the box. Case-insensitive, exact-match (after trim + whitespace-collapse), no fuzzy. **Locked against polish-loop expansion**: if Phase 2 produces a trustworthiness-gate failure that "would be fixed by adding X to the allowlist," that is a parser-side change and comes back through this gate. Polish loop fixes prompt wording, never the allowlist. (The right call when the LLM wants to flag a borderline phrase the parser didn't catch is for the reporter to *not* flag it — false-negative on the parser side is acceptable, parser-driven false-positives in the report are not.)

- [x] **Q3 (`aria-labelledby` dereference): confirmed — dereference and verify non-empty.** Existence-only is the wrong trade. The whole point of counting `forms.fields_with_label` is to give the analyser a signal that survives the trustworthiness gate, and an empty `<span id="label"></span>` is a real failure mode (especially in CMS-rendered forms where a label element is templated in but never populated). The cost is one `document.GetElementById` lookup per labelledby IDREF — negligible against the AngleSharp parse cost. **Two implementation clarifications** Amelia must follow:
  1. `aria-labelledby` accepts a **space-separated list of IDREFs**. Resolve each, concatenate their `TextContent` (trimmed), and treat the field as labelled if the concatenated result is non-empty. A single non-empty referenced element is sufficient.
  2. If any IDREF fails to resolve (no matching element in the document), that IDREF contributes empty text — it does not throw, it does not disqualify the other IDREFs in the list. This matches how browsers and AT actually behave.
  - Add an explicit unit test in 1.E for the multi-IDREF case (one empty, one non-empty → labelled) and the all-empty case (one IDREF, empty referenced span → not labelled).

- [x] **Q4 (inline-style colour reporter framing): "manual review recommended" is NOT strong enough — stricter guard required.** Sonnet 4.6 will dress this up as a 1.4.3 finding under polish-loop pressure to "flag more issues" — I've watched it happen on similar locked-mapping prompts. The current spec has a contradiction the LLM will exploit: Task 3.B.3 explicitly forbids citing 1.4.3 from this field, but Task 3.B.4 lists "presence of inline-style colour declarations" under the **Minor** severity bucket. Once it's in a severity bucket — even labelled "hint only" — the model treats it as a finding. **Two changes to lock:**

  **(a) Remove `inline_style_colour_count` from the severity rubric entirely.** Strike the bullet `"presence of inline-style colour declarations (hint only — phrase as "manual review recommended", not as a defect)"` from Task 3.B.4 Minor. It does not belong in Critical / Major / Minor at all — putting it there is the contradiction.

  **(b) Add a new, separate output-template section** to `accessibility-report.md` (Task 3.B.i) called `## Manual Review Notes` placed *after* the Prioritised Action Plan, with this exact locked introductory sentence:

  > The following items were detected by the scanner but cannot be automatically assessed against any WCAG criterion. They are listed here for the human reviewer's awareness only and are **not** findings.

  Inline-style colour declarations are reported under this section as a single line per affected page: `[Page title]: [N] inline-style colour declaration(s) detected — manual contrast check recommended.` Pages with `inline_style_colour_count == 0` are omitted. If no page has any, the entire `## Manual Review Notes` section is omitted.

  **(c) Add an explicit prohibition to the locked WCAG citation block in Task 3.B.3**, replacing the existing sentence "do not cite 1.4.3 Contrast (Minimum) on the basis of `inline_style_colour_count` — that is a hint for the human reviewer, not a finding" with this **stronger, locked** wording:

  > **`accessibility.inline_style_colour_count` is NOT a finding under any circumstances.** It MUST NOT appear in the Page-by-Page Findings section under Critical, Major, or Minor. It MUST NOT be cited against WCAG 1.4.3 Contrast (Minimum), 1.4.6 Contrast (Enhanced), 1.4.11 Non-text Contrast, or any other criterion. Its only valid destination in the report is the `## Manual Review Notes` section, using the exact line format specified in the output template. If you find yourself wanting to write the words "contrast" or "1.4.3" anywhere in this report, stop — the parser does not give you grounds to do so.

  This is now the strongest-worded prohibition in the reporter prompt and that is intentional. It is also the only paragraph in the prompt that uses the word "stop" — that scarcity is part of the lock; do not let polish passes weaken or duplicate it.

- [x] **Architect sign-off to proceed to Task 1:** Gate is open. The four decisions above are byte-for-byte locked against Phase 2 polish — any drift comes back through me. Amelia is unblocked to start Task 1. Ship it. — Winston, 2026-04-10

## References

- [epics.md Story 9.4 (line 1664)](../planning-artifacts/epics.md#L1664) — story statement, ACs, what NOT to build, failure & edge cases
- [epics.md Epic 9 preamble (line 287–292)](../planning-artifacts/epics.md#L287) — beta scope, two example workflows
- [Story 9.1b spec](9-1b-content-quality-audit-polish-and-quality.md) — the structured-extraction pattern this story inherits
- [Story 9.1c spec](9-1c-first-run-ux-url-input.md) — the verbatim opening line + invariants pattern this story inherits
- [`AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) — the file Task 1 extends
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md) — canonical scanner pattern to clone
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md) — canonical reporter pattern to clone

## Pre-Implementation Architect Re-Review Gate (CURRENT — implement against this)

> **This is the live gate.** The OBSOLETE block in Dev Notes above is preserved for audit trail only. Amelia executes against this gate. Bob (SM) wrote the corrected spec on 2026-04-10 after Winston's 2026-04-10 re-review confirmed Adam's stricter runner-genericity rule and locked the corrected `StructuredFetchHandle` shape and the `links.anchor_texts` parameters. Q3' was originally signed off by Winston as a working-tree rollback procedure; **Adam overrode that decision the same day in favour of patch-in-place** to preserve the parts of Amelia's implementation that are correct under the new rules. Q1' (shape) and Q2' (anchor parameters) are unchanged — only the execution path moved from rollback-and-redo to patch-in-place. The precise patch list lives in the Task 1 callout at the top of this file.

**Q1' (corrected `StructuredFetchHandle` shape):** Locked verbatim per Task 1.B above. Generic-only. No `accessibility { }` namespace. The five new field paths are: `headings.sequence` (additive, capped 200, early break); top-level `forms { field_count, fields_with_label, fields_missing_label }`; top-level `semantic_elements { main, nav, header, footer }` (semantic element OR matching ARIA role token); top-level `lang` (scalar, `null` when absent); plus `links.anchor_texts` and `links.anchor_texts_truncated` covered by Q2'. The eight dropped fields/concepts are: `accessibility { }` namespace, `skipped_heading_levels` (agent computes), `non_descriptive_link_count`, `non_descriptive_link_samples`, `NonDescriptiveLinkAllowlist` constant (moves to scanner prompt as the source of truth), `inline_style_colour_count`, `landmarks` (renamed `semantic_elements`), `lang_attribute` (renamed `lang`).

- [x] **Q1' confirmed.** The corrected `StructuredFetchHandle` shape in Task 1.B is locked as specified. Generic-only, no `accessibility { }` namespace, no workflow-domain vocabulary in field names. The eight dropped concepts stay dropped. The five new field paths (`headings.sequence`, top-level `forms`, top-level `semantic_elements`, top-level `lang`, `links.anchor_texts` + `links.anchor_texts_truncated`) are each neutral HTML/web primitives with obvious reuse across future workflows (SEO, link audit, content-tone, schema checks, CMS migration). The `semantic_elements` role-token fallback stays — `role` is a generic platform attribute, "landmark" was the WCAG lens over it. Additive against the existing CQA field shape; CQA backwards-compat test in Task 1.E is the tripwire. — Winston, 2026-04-10

**Q2' (`links.anchor_texts` parameters):** Locked verbatim per Task 1.B above:
- Field name `links.anchor_texts` (not `anchor_text_samples`).
- **Cap: 100 entries** via early break during the existing anchor-walking loop. Stop collecting into `anchor_texts` at 100, but **keep walking** so the existing `internal + external == document.QuerySelectorAll("a[href]").Length` invariant continues to hold.
- **Normalisation:** trim + whitespace-collapse (collapse runs of any Unicode whitespace to a single space). **Preserve original case** — parser does not pre-lowercase.
- **Empty-text exclusion:** after trim+collapse, empty strings are not added to `anchor_texts`. Empty anchors are still counted in `internal`/`external`.
- **No deduplication.** Document-order repetitions preserved — frequency is signal.
- **`links.anchor_texts_truncated`** — `bool`, mirrors top-level `truncated`'s role for the cap rather than the byte-stream limit. Both flags can be true independently.
- Five locked unit tests in Task 1.E (cap enforcement, empty-text exclusion, whitespace collapse, case preservation, determinism).

- [x] **Q2' confirmed.** All seven `links.anchor_texts` parameters locked as specified: field name `anchor_texts` (not `samples` — honest about the semantics), cap 100 via early-break during the existing anchor-walking loop, `internal + external == anchors.Length` invariant preserved by continuing to walk past the cap for tally purposes, trim + whitespace-collapse normalisation, original case preserved (agent does case-insensitive allowlist match), empty-text exclusion from `anchor_texts` but not from counts, no deduplication (frequency is signal), `anchor_texts_truncated` as the independent cap-hit flag. The five unit tests in Task 1.E are the acceptance surface. — Winston, 2026-04-10

**Q3' (patch-in-place procedure):** Amelia patches the existing working-tree code in place — **no `git restore`, no rollback.** A lot of the original implementation is correct under the new rules (the four label-association mechanisms, the early-break heading walk pattern, the role-attribute token match for landmarks, the empty-body short-circuit precedent, the fixture-backed regression tests) and rolling back would throw away that work. The precise change list is locked in the post-architect-re-review patch instructions at the top of Task 1 above — every delete, rename, extension, modification, and the post-patch stale-reference safety check is enumerated there. Re-read Tasks 1.A → 3.B.i end-to-end before touching code; the corrected Task 1.B JSON shape, Task 1.E test list, and Task 3 prompts are the source of truth. The CQA backwards-compatibility shape assertion test is the canary — if it fails after the patch, the additive-only invariant is broken and the patch needs revision.

- [x] **Q3' confirmed (Winston originally signed off rollback; Adam overrode to patch-in-place 2026-04-10).** Winston's original Q3' confirmation read: _"Rollback procedure is the only correct path forward — the working-tree artefacts are faithful executions of a wrong spec and must not inform the re-implementation."_ Adam reviewed that decision the same day and overrode it: a lot of Amelia's existing code is correct under the new rules (`IsFieldLabelled` and its four mechanisms verbatim, `HasLandmark` body unchanged but renamed, `StructuredForms` already correctly named, the `HeadingSequenceCap = 200` early-break pattern, the empty-body short-circuit precedent, the fixture-backed regression tests), and rolling back via `git restore` would throw that work away. Bob authored a precise patch checklist (delete / rename / extend / modify, plus a stale-reference safety sweep across both source files and the prompts) and committed it to the Task 1 callout at the top of this file. **The sharp edge Winston flagged — workflow-domain framing sneaking back through variable names and comments — still applies**, and is enforced by the post-patch stale-reference grep in the Task 1 callout (search the patched files for `accessibility`, `landmarks`, `lang_attribute`, `non_descriptive`, `inline_style_colour`, `skipped_heading`, `BuildAccessibility`, `NormaliseLinkText`, `NonDescriptiveLinkAllowlist`, `StructuredAccessibility` — any surviving match is a stale rename and must be fixed before commit). The CQA backwards-compatibility shape assertion test is the canary — if it fails after the patch, the additive-only invariant is broken and the patch needs revision. — Adam override 2026-04-10, applied by Bob (SM) the same day.

**Architect re-review sign-off to proceed to corrected Task 1:** _Winston tick + signoff line. Once this checkbox is ticked, Amelia is unblocked to start the corrected Task 1._

- [x] **Architect signoff: Winston, 2026-04-10 (with Q3' execution path overridden by Adam to patch-in-place the same day).** Re-Review Gate open. Amelia is unblocked to execute the Task 1 patch checklist (no rollback) and re-implement against the corrected Task 1.B / 1.E / 3.A / 3.B. Trustworthiness gate runs once, against the right architecture.

---

## Dev Agent Record

> **PATCH APPLIED 2026-04-10.** Working tree now matches the corrected Re-Review Gate spec. The pre-patch notes that previously occupied this section have been replaced with the post-patch state below. See Change Log row 2.0 for the patch summary.

### Agent Model Used

claude-opus-4-6 (1M context) — Amelia persona, 2026-04-10. Patch-in-place pass against the corrected Re-Review Gate.

### Debug Log References

- `dotnet test AgentRun.Umbraco.slnx` (full regression after the Phase 1 patch + Phase 3 prompt rewrite) — **465/465 passed**, 0 failed. CQA backwards-compatibility shape canary green (the additive-only invariant held through the patch).

### Completion Notes List

**Phase 1 patch-in-place against the Re-Review Gate (Tasks 1.A–1.G) — DONE 2026-04-10**

Applied the precise patch list from the Task 1 callout. No rollback. Per Adam's override of Winston's original rollback signoff, the parts of the original Phase 1 that were already correct under the new generic-runner rules were preserved verbatim.

- **Deleted from `FetchUrlTool.cs`:** `StructuredAccessibility` record, `NonDescriptiveLinkAllowlist` constant, `NormaliseLinkText` helper, `BuildAccessibility` helper. All workflow-domain vocabulary is now out of the runner.
- **Renamed:** `HasLandmark` → `HasSemanticElement`; `StructuredLandmarks` → `StructuredSemanticElements` with property + JsonPropertyName renames `HasMain → Main` (`"main"`), `HasNav → Nav` (`"nav"`), `HasHeader → Header` (`"header"`), `HasFooter → Footer` (`"footer"`).
- **Extended additively (no reorder, no rename of existing fields):**
  - `StructuredHeadings` gained `Sequence` (`"sequence"`).
  - `StructuredLinks` gained `AnchorTexts` (`"anchor_texts"`) and `AnchorTextsTruncated` (`"anchor_texts_truncated"`).
  - `StructuredFetchHandle` gained three top-level properties — `Forms` (`"forms"`), `SemanticElements` (`"semantic_elements"`), `Lang` (`"lang"`) — and dropped the `Accessibility` namespace property entirely. Existing pre-9.4 fields and JsonPropertyName values are byte-for-byte unchanged.
- **`ParseStructured` modifications:**
  - The previously separate `h1` / `h2` / `h3_h6_count` queries are now produced by a **single** heading walk that simultaneously builds the four outputs `h1`, `h2`, `h3_h6_count`, and `sequence`. The `sequence` collection is bounded by the `HeadingSequenceCap = 200` early-break check inside the walk; the `h1` / `h2` / `h3_h6_count` tallies remain uncapped to preserve the pre-9.4 CQA contract.
  - The existing anchor-walking loop was extended in place: each anchor's text is run through a new `CollapseWhitespace` helper (trim + Unicode whitespace collapse, **case preserved** — the scanner prompt does case-insensitive matching itself), empty-text anchors are skipped from the sample list but still counted in `internal` / `external`, the cap is `AnchorTextsCap = 100` via early break on the **collection only** (`internal` / `external` keep going so the 9.1b invariant `internal + external == document.QuerySelectorAll("a[href]").Length` continues to hold), and `anchor_texts_truncated` is set when the cap is hit.
  - New `BuildForms(IDocument)` and `BuildSemanticElements(IDocument)` helpers were extracted from the deleted `BuildAccessibility` body. `IsFieldLabelled` is unchanged — Q3' multi-IDREF dereference + unresolved-IDREF non-throwing path is correct under both spec versions.
  - `lang` is read inline from `document.DocumentElement.GetAttribute("lang")` with empty-string normalisation to `null`.
- **Empty-body short-circuit (Task 1.D):** `emptyStructured` constructor call updated to drop the `Accessibility:` argument and add zero-valued versions of `Forms`, `SemanticElements`, `Lang`, plus an empty `Sequence` on `Headings:` and an empty `AnchorTexts` + `false` `AnchorTextsTruncated` on `Links:`.
- **`extract` schema description (Task 1.F):** rewrote without the word "accessibility" — _"...returns a small structured handle (title, meta description, headings including ordered tag sequence, word count, image and link counts, anchor text samples, form field counts with label associations, semantic HTML element presence, and page language)..."_

**Test patch summary:**

- **Deleted:** the `A11y*` projection records and helpers; the `A11y_SkippedHeadingLevels_*` tests (engine no longer computes this); the `A11y_NonDescriptiveLinkCount_*` tests (allowlist no longer in engine); the `A11y_InlineStyleColourCount_*` test (field gone). The `Story 9.4 Task 1.E` block is now a `Generic_*` test family using a new `StructuredHandleExt` projection.
- **Renamed assertions:** all landmark / form / lang / heading-sequence tests now assert the flat-shape paths (`semantic_elements.main`, `forms.field_count`, `lang`, `headings.sequence`).
- **Added (six new tests required by the patch list):**
  - `Generic_AnchorTexts_CapEnforcedAtOneHundred_InternalExternalUncapped` — synthetic 150-anchor fixture, cap holds, truncated flag set, invariant preserved.
  - `Generic_AnchorTexts_EmptyTextAnchorIsExcludedButStillCountedInLinks`.
  - `Generic_AnchorTexts_WhitespaceCollapsedTrimmed`.
  - `Generic_AnchorTexts_OriginalCasePreserved` — explicit no-lowercasing assertion.
  - `Generic_AnchorTexts_DeterministicAcrossTwoParses`.
  - `Generic_AriaLabelledBy_UnresolvedAndResolvedIdref_CountsAsLabelled` (Q3' addition: one resolved non-empty + one unresolved → labelled, no throw).
- **Updated existing assertions:** `HandleShape_HasExactlyExpectedFields_AndIsUnder1KB` now lists `forms`, `semantic_elements`, `lang` instead of `accessibility`; the empty-body test now asserts the new top-level zero-valued blocks; the three fixture-backed regression tests now assert `headings.sequence` and `links.anchor_texts` presence + determinism.
- **Untouched:** the CQA backwards-compatibility shape assertion test — same canary, still asserts pre-9.4 fields are unchanged. **It passes**, confirming the additive-only invariant held through the patch.

**Stale-reference safety sweep:** ran the locked sweep list (`accessibility`, `landmarks`, `lang_attribute`, `non_descriptive`, `inline_style_colour`, `skipped_heading`, `BuildAccessibility`, `NormaliseLinkText`, `NonDescriptiveLinkAllowlist`, `StructuredAccessibility`) across both `FetchUrlTool.cs` and `FetchUrlToolTests.cs`. Surviving matches: two non-functional comments (one referring to "accessibility-correct decorative-image marker" — semantically correct in context, generic HTML vocab — and the helper comment pointing to the workflow agent prompts as the interpretation layer). Both intentional. Same sweep across `accessibility-quick-scan/agents/*.md` for `accessibility.*` JSON paths and `Manual Review Notes` — zero matches.

**Test result:** `dotnet test AgentRun.Umbraco.slnx` → **465/465 passed**, 0 failed. The CQA backwards-compatibility shape assertion canary is green.

**Phase 2 workflow.yaml (Task 2) — UNCHANGED 2026-04-10**

`accessibility-quick-scan/workflow.yaml` was correct under both old and new shapes. No edit required.

**Phase 3 agent prompts (Task 3) — REWRITTEN 2026-04-10**

- `accessibility-quick-scan/agents/scanner.md` rewritten in place against the corrected Task 3.A:
  - The structured-handle JSON example now shows the corrected flat shape — no `accessibility` namespace, top-level `forms` / `semantic_elements` / `lang`, `headings.sequence`, `links.anchor_texts` + `links.anchor_texts_truncated`.
  - **The locked WCAG community-consensus non-descriptive link allowlist** (`click here`, `read more`, `here`, `more`, `link`, `learn more`, `this`) lives in this prompt as the source of truth, with case-insensitive exact-match rules and an explicit "do not expand the allowlist during a single run" lock.
  - **Skipped-heading-level detection** is now an agent-side rule: walk `headings.sequence`, flag forward jumps > 1, ignore direction-down. The runner does not compute it.
  - The verbatim anchor-texts truncation caveat note (`Scanner examined the first 100 links on this page; non-descriptive link findings are scoped to that prefix.`) is added with explicit conditions for when to emit it.
  - The output template references the flat field paths.
  - The five hard invariants and the verbatim opening line + zero-URL re-prompt are byte-for-byte preserved from the previous scanner.md.
- `accessibility-quick-scan/agents/reporter.md` rewritten in place against the corrected Task 3.B:
  - The entire `## Manual Review Notes` section is **gone** — it defended a field that no longer exists.
  - The inline-colour `accessibility.inline_style_colour_count` prohibition block (the verbatim "stop" wording paragraph) is **gone** — same reason. Both the offending field and its defence are deleted together.
  - The WCAG citation discipline now references "non-descriptive link text (detected by the scanner via case-insensitive allowlist match against `links.anchor_texts`)" and "missing `<main>` semantic element (or `role="main"`)".
  - The severity rubric uses the corrected language ("missing `<main>` semantic element", "missing `<nav>`/`<header>`/`<footer>` semantic elements") with no inline-colour bullet anywhere. The contrast-criteria prohibition (1.4.3 / 1.4.6 / 1.4.11) is preserved as a one-liner — the scanner does not measure contrast and the report must not address it.

**Phase 4 (Tasks 4.A–4.G) — NOT YET STARTED**

Phase 4 is the manual end-to-end + trustworthiness gate. It must be driven by Adam against real test inputs of his choice on a running TestSite — Amelia cannot execute it from code. **Halting here for manual handover.** Trustworthiness gate now runs **once**, against the corrected architecture. The 5-pass soft-cap polish budget mirrors 9.1b Phase 2; pass-by-pass tracking will be appended to the Change Log table at the bottom of this file.

### File List

**Modified (engine source — Task 1 patch):**

- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` — top-level generic primitives appended additively to `StructuredFetchHandle`: `Forms` (`"forms"`), `SemanticElements` (`"semantic_elements"`), `Lang` (`"lang"`); `StructuredHeadings` extended with `Sequence` (`"sequence"`); `StructuredLinks` extended with `AnchorTexts` (`"anchor_texts"`) and `AnchorTextsTruncated` (`"anchor_texts_truncated"`). Renamed: `HasLandmark` → `HasSemanticElement`, `StructuredLandmarks` → `StructuredSemanticElements` (with `HasMain → Main` etc property + JsonPropertyName renames). Deleted: `StructuredAccessibility` record, `NonDescriptiveLinkAllowlist` constant, `NormaliseLinkText` helper, `BuildAccessibility` helper. New helpers extracted from the deleted `BuildAccessibility` body: `BuildForms`, `BuildSemanticElements`, `CollapseWhitespace`. New constant: `AnchorTextsCap = 100`. `ParseStructured` reworked: single heading walk produces `h1`/`h2`/`h3_h6_count`/`sequence` together (cap 200 on `sequence` only); existing anchor walk extended to collect `anchor_texts` (cap 100, preserve case, skip empty, keep `internal`/`external` invariant). Empty-body short-circuit extended. `extract` schema description rewritten without the word "accessibility". `IsFieldLabelled` (Q3' four-mechanism + multi-IDREF + unresolved-IDREF non-throwing path) preserved verbatim.

**Modified (tests — Task 1 patch):**

- `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` — `A11y*` projection records and helpers replaced with `StructuredHandleExt` family; `Story 9.4 Task 1.E` block rewritten as a `Generic_*` test family asserting flat-shape paths; six new tests added per the patch list (anchor-texts cap, empty-text exclusion, whitespace collapse, case preservation, determinism, unresolved-IDREF + resolved-IDREF labelled case); `HandleShape_HasExactlyExpectedFields_AndIsUnder1KB` updated to list `forms`/`semantic_elements`/`lang`; empty-body test updated to assert the new top-level zero-valued blocks; the three fixture-backed regression tests now assert `headings.sequence` and `links.anchor_texts`. The CQA backwards-compatibility shape canary test is unchanged and green.

**Modified (workflow agent prompts — Task 3 patch):**

- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/agents/scanner.md` — rewritten with the corrected flat-shape JSON example, the locked non-descriptive link allowlist as the source of truth, the agent-side skipped-heading detection rule, and the verbatim anchor-texts truncation caveat. Five hard invariants and verbatim opening line / zero-URL re-prompt preserved byte-for-byte.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/agents/reporter.md` — rewritten with the corrected WCAG citation discipline (no inline-style colour prohibition, no Manual Review Notes section — both defended fields that no longer exist), the corrected severity rubric, and the contrast-criteria prohibition preserved as a one-liner.

**Unchanged from the original Phase 1/2 build:**

- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/workflow.yaml` — correct under both old and new shapes.

**Modified (tracking):**

- `_bmad-output/implementation-artifacts/sprint-status.yaml` — to be updated to `9-4-accessibility-quick-scan-workflow: in-progress` (patch applied, awaiting Phase 4).

## Change Log

| Pass | Date | Phase | Notes |
|------|------|-------|-------|
| 0 | 2026-04-10 | Story spec | Initial spec written by Bob (SM) — ready for Winston pre-implementation review on the four open questions before Amelia starts Task 1 |
| 2.0 | 2026-04-10 | Phase 1 patch-in-place + Phase 3 prompt rewrite | Amelia: applied the precise patch list from the Re-Review Gate Task 1 callout. `FetchUrlTool.cs` rewritten in place — deleted `StructuredAccessibility`/`NonDescriptiveLinkAllowlist`/`NormaliseLinkText`/`BuildAccessibility`; renamed `HasLandmark`→`HasSemanticElement` and `StructuredLandmarks`→`StructuredSemanticElements`; appended `Forms`/`SemanticElements`/`Lang` top-level + `headings.sequence` + `links.anchor_texts`/`links.anchor_texts_truncated`; collapsed heading walks into one; extended anchor walk in place (cap 100, preserve case, skip empty, internal/external invariant preserved); empty-body short-circuit + schema description rewritten without "accessibility". `IsFieldLabelled` (Q3' multi-IDREF semantics) preserved verbatim. Tests rewritten as `Generic_*` family with the six new tests required by the patch list. Stale-reference safety sweep clean. Scanner.md and reporter.md rewritten — non-descriptive link allowlist + skipped-heading detection moved into the scanner prompt (source of truth); the entire `## Manual Review Notes` section and inline-colour prohibition block were struck from the reporter (both defended a field that no longer exists). `dotnet test AgentRun.Umbraco.slnx` → 465/465 green; CQA backwards-compatibility canary green. Trustworthiness gate (Phase 4) handed back to Adam against the corrected architecture. |
| 1.0 | 2026-04-10 | Phase 1/2/3 build | Amelia: Tasks 1.A–1.G + 2.A–2.B + 3.A–3.B all complete in a single pass. `dotnet test AgentRun.Umbraco.slnx` → 462/462 green (444 baseline + 18 new accessibility-block tests). No CQA test or prompt files touched. Phase 4 (manual E2E + trustworthiness gate) handed back to Adam — he runs against his own URL set, then signs `_bmad-output/qa-signoffs/9-4-accessibility-quick-scan-trustworthiness-signoff.md`. 5-pass polish budget unconsumed. |
| 0.1 | 2026-04-10 | Post-gate spec edits | Winston signed the gate (Q1–Q4 locked). Bob applied the flowing edits: (a) erratum struck `truncated_during_parse` from AC4, Task 1.B JSON, Task 1.C, and Task 1.E CQA backwards-compat list — field was dropped in 9.1b P1 code review and no longer exists; (b) Q1 — added early-break loop implementation note to `heading_sequence` cap; (c) Q2 — appended polish-loop expansion lock language to `non_descriptive_link_count`; (d) Q3 — replaced `aria-labelledby` (d) clause with multi-IDREF dereference + non-empty `TextContent` semantics, plus two new unit-test bullets in Task 1.E; (e) Q4 — struck inline-style colour from severity rubric Minor, added `## Manual Review Notes` section to reporter output template with locked verbatim intro sentence, replaced WCAG citation discipline paragraph in Task 3.B.3 with the stronger "stop" wording. Open Questions block in Dev Notes marked RESOLVED for traceability. Status remains `ready-for-dev`. Amelia unblocked. |
| 0.2 | 2026-04-10 | Architect re-review + spec rewrite + patch-in-place plan | **Adam caught a runner-genericity violation in the Phase 1/2/3 implementation during the in-flight QA review window** — the spec Bob shipped baked workflow-domain fields (`accessibility { non_descriptive_link_count, landmarks, inline_style_colour_count, skipped_heading_levels, ... }` plus a hardcoded WCAG-community allowlist as a private `HashSet<string>`) into the generic `FetchUrlTool` / `StructuredFetchHandle`. This violates FR23, NFR25, [project-context.md:122 + 150–151](../project-context.md), and the principle locked in the [Runner stays workflow-generic](../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_runner_stays_workflow_generic.md) memory entry. Root cause: Bob skipped reading project-context.md, architecture.md, and the second half of 9.1b before writing the original spec, despite the create-story workflow.md explicitly requiring it. **Winston re-reviewed and overrode his own 9.1b AC #9 hedge** — see [9-1b spec line 212 erratum](9-1b-content-quality-audit-polish-and-quality.md). The corrected `StructuredFetchHandle` shape and `links.anchor_texts` parameters are locked in the new **Pre-Implementation Architect Re-Review Gate** at the bottom of this file (the original gate is preserved as OBSOLETE for audit trail). Bob rewrote Task 1.B + 1.C + 1.D + 1.E + 1.F + 1.G, Task 3.A scanner.md content + Task 3.A.i output template, Task 3.B reporter.md content + Task 3.B.i output template (struck the entire `## Manual Review Notes` machinery — defended a field that no longer exists), AC4 (corrected field references), Failure & Edge Cases (replaced inline-colour-SPA bullet with anchor-text-cap caveat). **Adam's call (2026-04-10): patch in place, no rollback.** A lot of Amelia's existing code is correct under the new rules — the four label-association mechanisms, the early-break heading walk, the role-attribute token match, the empty-body short-circuit, the fixture-backed regression tests. Throwing it away via `git restore` would waste that work. Bob authored a precise patch checklist (delete / rename / extend / modify, plus a stale-reference safety sweep) and committed it to the Task 1 callout at the top of this file. Dev Agent Record marked PATCH PENDING with banner; pre-patch notes preserved as audit trail with explicit warning not to use them as guidance. Trustworthiness gate now runs **once**, against the corrected architecture, after the patch lands. Re-Review Gate Q1' / Q2' / Q3' (with Adam override) + architect signoff all ticked the same day. Amelia is unblocked to apply the patch. |
