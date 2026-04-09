# Story 9.1b: Content Quality Audit — Polish & Quality (Phase 1 Build + Phase 2 Polish) — BETA BLOCKER

Status: review (Phase 1 code complete; Phase 1 manual E2E gate Task 8 + Phase 2 polish Tasks 9–10 still owned by Adam)

> **Pre-Implementation Architect Review required before Amelia starts.** Winston eyeballs the structured return shape against AngleSharp's actual API and resolves the open questions in the Pre-Implementation Architect Review section at the bottom of this file. Spec is otherwise ready.

**Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done), 9.1a (CQA working skeleton — done), 9.7 (Tool Result Offloading for `fetch_url` — done), 9.9 (read_file size guard — done), 9.1c (First-Run UX — paused; must reach `done` before Phase 2 polish loop begins, but Phase 1 build does **not** block on 9.1c)
**Blocks:** 9.4 (Accessibility Quick-Scan — inherits the structured extraction pattern established here)
**Cooperates with:** 9.9 (defence-in-depth safety net on the read end; 9.1b is the primary fix that makes raw `read_file` against cached HTML unnecessary in the first place)

> **BETA BLOCKER.** This is the **second of two reliability gates** for the Content Quality Audit scanner workflow. Story 9.7 disciplined the **write end** of tool-result offloading; Story 9.9 added a defence-in-depth byte cap on the **read end**. **Story 9.1b lands the structured-extraction lever that means the agent never has to read raw HTML at all.** Without 9.1b's Phase 1 build on `main`, the scanner workflow does not complete a single end-to-end run against real-world pages — it deterministically reproduces the same context-bloat failure mode 9.7 was built to fix, just via the `read_file` code path instead of the `fetch_url` code path. The 5-pass soft cap with architect escalation applies to **Phase 2 (polish) only** — Phase 1 has no soft cap, it ships when the build is done.

## Story

As a developer evaluating AgentRun,
I want the Content Quality Audit example workflow to produce an output I would happily paste into a client deck,
So that my first experience of the package proves its value, not just its plumbing — and so that the trustworthiness gate I sign off on is grounded in deterministic parser facts, not on hopeful prompt iteration over a non-deterministic parsing surface.

This is a **two-phase build-and-tune** sequence:

- **Phase 1 (build)** — Add an optional `extract: "raw" | "structured"` parameter to the existing `fetch_url` tool. Default `"raw"`, preserving Story 9.7's offloading shape verbatim. When `extract: "structured"` is set, AngleSharp parses the response server-side and the tool returns a small structured handle (title, meta description, headings, word count, image counts, link counts, truncation flags). Rewrite scanner.md / analyser.md / reporter.md to consume the new structured shape and strip the contract-defence verbosity that the new contract makes obsolete. **Phase 1 has no soft cap.** It ships when the build is done.
- **Phase 2 (polish)** — Iterate prompts on top of the new contract until the trustworthiness gate passes against ≥3 representative real test inputs. **5-pass soft cap with architect escalation.**

## Context

**UX Mode: N/A — engine + tool contract change followed by prompt iteration. No UI work.**

### The two-phase mental model

> **Phase 1 makes the scanner *work*. Phase 2 makes it *trustworthy*.**

That reframing is from the [9.1b reshape addendum](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md), not the original reshape SCP. Before the read_file bloat finding, 9.1b's Phase 1 was framed as "quality improvement on top of an already-working scanner." Tonight's evidence (instances `d1ec618e0d884a5f9a83ea1468383b90` and `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8`) proved the scanner cannot complete a single end-to-end run without Phase 1. The polish phase is still polish; the build phase is now beta-blocking.

### The symmetry argument (why 9.1b cooperates with 9.9 instead of duplicating it)

Tool-result offloading as practised by Claude Code, Anthropic Computer Use, and the Claude Agent SDK assumes the model never needs to read offloaded content in full. The model's working memory stays bounded because the **consuming side is also constrained**. Story 9.7 disciplined the producer (`fetch_url`); Story 9.9 added a hard byte cap on the consumer (`read_file`) so any future workflow that falls back to raw `read_file` against cached HTML cannot silently re-introduce the failure mode. **Story 9.1b removes the agent's reason to call raw `read_file` against cached HTML at all.** With structured extraction, the scanner reads deterministic facts (title, headings, word count, image counts, link counts) directly from the `fetch_url` handle and never invokes `read_file` against a multi-MB cached HTML payload during the audit pattern. 9.9 is the safety net; 9.1b is the primary fix; both ship before private beta.

### Why the polish loop on its own was the wrong lever

Before this reshape, Story 9.1b's scope was "iterate prompts until trustworthy." That assumed the polish lever was the prompts themselves. The 2026-04-07 manual E2E proved the assumption wrong: even after Story 9.7 ships, the model is still re-parsing raw HTML to extract headings, alt-text counts, and word counts. That re-parsing is the *exact* class of work the model has been observed to do non-deterministically (hallucinated headings, missing alt-text on present images, drifting word counts run-to-run). Polish-loop iteration on the prompt would chase those inconsistencies forever, because the bug is upstream of the prompt: the model is being asked to do parsing work that should be done by a real HTML parser. **Server-side structured extraction moves the parsing to AngleSharp, leaving the polish loop with a tractable problem (tune *reasoning over deterministic facts*, not parsing accuracy).**

### Authorisation

This spec is authorised by **two architectural finding reports** plus **two sprint change proposals**:

- [9-1c-architectural-finding-fetch-url-context-bloat.md](../planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md) — the original 2026-04-07 finding that motivated the reshape (Option 2 = server-side extraction via AngleSharp). Sections "TL;DR", "Evidence", "Root Causes", and "The fix — proposed → Option 2" are authoritative.
- [9-7-architectural-finding-read-file-bloat.md](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md) — the 2026-04-08 evening finding that escalated 9.1b's framing from "quality improvement" to "second reliability gate" and added Story 9.9 as a parallel sibling. Read alongside the original finding, not after.
- [Sprint Change Proposal 2026-04-08 — Story 9.1b Rescope](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope.md) — locks AngleSharp, the structured return shape, the two-phase scope, the 5-pass cap on Phase 2, the trustworthiness gate, and the signoff artefact location.
- [Sprint Change Proposal 2026-04-08 — Story 9.1b Rescope Addendum (read_file finding)](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md) — reframes 9.1b as the second reliability gate without which the scanner cannot complete a single end-to-end run; introduces Story 9.9 as a parallel sibling; restates that Phase 1 has no soft cap.

Both finding reports are required reading. Either alone is incomplete.

### Architect's locked decisions (do not relitigate)

Locked by Winston in his 2026-04-08 architect responses. If the dev agent finds a real ambiguity (not a relitigation), stop and ask Adam.

1. **Parser: AngleSharp.** MIT-licensed (verified 2026-04-08). Actively maintained, modern .NET targeting, DOM-faithful API. Not currently in the project's dependency tree — adding the NuGet reference is part of Phase 1. **Do not substitute.** If implementation surfaces a real reason to revisit, escalate to Adam — do not silently swap parsers.
2. **Implementation shape: optional `extract` parameter on the existing `fetch_url` tool.** Enum `"raw" | "structured"`. Default `"raw"` preserves Story 9.7's offloading handle shape verbatim. The architect explicitly **rejected** adding a sibling tool like `fetch_url_structured`. One tool, one concept, two return shapes.
3. **Structured return shape (locked verbatim — minus architect review on AngleSharp mappability):**

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

   - `truncated` cooperates with Story 9.6's `max_response_bytes` ceiling at the byte-stream layer (same field as Story 9.7's `extract: "raw"` handle).
   - `truncated_during_parse` is NEW and `extract: "structured"`-specific. It is `true` if AngleSharp had to parse a document that hit the byte ceiling before parsing, in which case the structured fields may be incomplete and the agent needs to know.
4. **Two-phase scope.** Phase 1 = build. Phase 2 = polish. The 5-pass soft cap with architect escalation applies to **Phase 2 only**. Phase 1 has no cap and ships when AngleSharp is integrated, the agents consume the structured return shape, and the unit/regression tests are green.
5. **Trustworthiness gate criterion (unchanged):** Adam, acting as the reviewer, would be willing to run this workflow against a paying client's live site without supervision and trust the output.
6. **Signoff artefact location (unchanged):** `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` (or equivalent — confirm or create during Phase 2 kick-off). Its existence is the AC.
7. **Test fixture reuse.** Reuse the captured fixtures already checked in by Story 9.7 at `AgentRun.Umbraco.Tests/Tools/Fixtures/`: `fetch-url-100kb.html` (~107 KB), `fetch-url-500kb.html` (~207 KB), `fetch-url-1500kb.html` (~1 MB). **Do NOT capture new fixtures.**
8. **The scanner's `tools: [fetch_url, read_file, write_file]` declaration in workflow.yaml does not change.** Verified 2026-04-08 — the scanner step already declares all three after Story 9.7's session amendments. Phase 1 does not modify workflow.yaml's tools list. (See Dev Notes for the verification path if Amelia needs to re-confirm.)
9. **The five hard invariants from Story 9.1c's partial milestone are preserved verbatim** in scanner.md after the rewrite. The contract-defence verbosity (the "defending against context bloat" framing) is what gets stripped — the new contract eliminates the failure mode upstream so the prompts get *simpler*, not more complex.
10. **Pre-implementation architect review gate.** Winston explicitly asked to eyeball the structured return shape against AngleSharp's actual API before code is written. See the gate at the bottom of this file. Amelia does not start implementation until that checkbox is ticked.

## Files in Scope

**Modified (engine source — Phase 1):**

- [`AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) — add the `extract` parameter to the schema; add the `extract: "structured"` code path that feeds the byte buffer to AngleSharp before (or instead of, depending on architect-review answer Q1 below) writing to `.fetch-cache/`. The existing `extract: "raw"` (default) path is preserved byte-for-byte — Story 9.7's handle shape, SHA-256 file path, SSRF protection, timeout, cancellation, the Story 9.6 byte ceiling, the truncation marker behaviour, the empty-body short-circuit, and the typed `ToolContextMissingException` for missing Step/Workflow context (see Dev Notes — must use the same exception type added in Story 9.9 D2, **not** raw `InvalidOperationException`).
- [`AgentRun.Umbraco.csproj`](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj) — add the AngleSharp NuGet reference. Pin to a specific version current on NuGet.org at implementation time; record the version in the Dev Agent Record.

**Modified (tests — Phase 1):**

- [`AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs) — add the parser-layer regression tests against the three captured fixtures plus contract tests for the new return shape (every locked field present, correct types, deterministic across two parses of the same fixture, `truncated_during_parse` correctly set when the input bytes carry the truncation marker, correct interaction with Story 9.6's byte ceiling). Existing `extract: "raw"` tests stay green unchanged.

**Modified (agent prompts — Phase 1, prompt rewrite step):**

- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md) — switch the scanner to `fetch_url(url, extract: "structured")` for audit work. Strip the "defending against context bloat" framing. Preserve the verbatim opening line and the five hard invariants. Preserve the failure-bucketing logic for non-HTML content types and HTTP errors.
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/analyser.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/analyser.md) — consume the structured fields from `scan-results.md` directly rather than re-parsing the scanner's narrative text. Update the "only flag issues you can directly cite" instruction to reference the structured fields.
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md) — same treatment: consume structured facts via the analyser's output, drop verbosity that defended against now-impossible failure modes.

**Modified (Phase 2 polish loop only — iterative):**

- The same three agent prompt files above. Phase 2 modifies them iteratively against the trustworthiness gate. No source changes during Phase 2.

**Created (Phase 2):**

- `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` — the trustworthiness gate signoff artefact. Created at the start of Phase 2; Adam signs it once the gate passes against ≥3 representative real test inputs.

**Explicitly NOT touched (out of scope):**

- [`AgentRun.Umbraco/Tools/ReadFileTool.cs`](../../AgentRun.Umbraco/Tools/ReadFileTool.cs) — Story 9.9 territory; do not modify. The structured extraction is done in `FetchUrlTool`, not by reading from cache.
- [`AgentRun.Umbraco/Tools/WriteFileTool.cs`](../../AgentRun.Umbraco/Tools/WriteFileTool.cs) / `ListFilesTool.cs` — out of scope.
- [`AgentRun.Umbraco/Engine/ToolLoop.cs`](../../AgentRun.Umbraco/Engine/ToolLoop.cs) — out of scope. Story 9.0's stall detector is correct.
- [`AgentRun.Umbraco/Security/SsrfProtection.cs`](../../AgentRun.Umbraco/Security/SsrfProtection.cs) — out of scope. SSRF behaviour preserved exactly.
- [`AgentRun.Umbraco/Security/PathSandbox.cs`](../../AgentRun.Umbraco/Security/PathSandbox.cs) — out of scope. The cache write path (when used) continues to go through `PathSandbox.ValidatePath` exactly as in Story 9.7.
- [`AgentRun.Umbraco/Engine/ToolLimitResolver.cs`](../../AgentRun.Umbraco/Engine/ToolLimitResolver.cs) — out of scope. Story 9.6's resolution chain and Story 9.9's `ResolveReadFileMaxResponseBytes` extension are reused unchanged. **No new tunable is added** for `extract` — it is an agent-supplied argument, not a workflow-author tunable.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml` — no change. The scanner step's `tools: [fetch_url, read_file, write_file]` declaration was already updated by Story 9.7's session amendments and is correct as-is for 9.1b.
- The JSON Schema for `workflow.yaml` — **forward dependency for Story 9.2.** Story 9.2 picks up `extract` as part of its sweep of post-9.6 keys. **Do not block 9.1b on Story 9.2.**
- The Story 9.7, 9.9, 9.1c, 9.6, 9.0 spec files — authoritative as written; do not modify.
- The architectural finding reports — authoritative as written; do not modify.
- `_bmad-output/planning-artifacts/v2-future-considerations.md` and `_bmad-output/project-context.md` — Winston-owned.

## Acceptance Criteria

### Phase 1 — Build (no soft cap)

**AC #1 — AngleSharp NuGet reference is added.**

**Given** the `AgentRun.Umbraco.csproj` project file
**When** the project is restored
**Then** AngleSharp resolves from NuGet.org at a specific pinned version
**And** the resolved version is recorded in the Dev Agent Record
**And** no transitive dependency conflict is reported by `dotnet restore` against the existing dependency tree (Umbraco, Microsoft.Extensions.AI, etc.).

**AC #2 — `extract` parameter is added to `fetch_url`'s schema.**

**Given** the `fetch_url` tool's `ParameterSchema` JSON
**When** the schema is inspected
**Then** it includes a new optional property `extract` of type `string`
**And** the allowed values are exactly `"raw"` and `"structured"`
**And** the default (when omitted) is `"raw"`
**And** the existing required `url` parameter is unchanged
**And** the schema validates against itself (well-formed JSON Schema).

**AC #3 — `extract: "raw"` is byte-for-byte identical to today's Story 9.7 behaviour.**

**Given** any URL whose response is a successful HTTP 200 with a non-empty body
**When** `fetch_url(url, extract: "raw")` is called
**And** also when `fetch_url(url)` is called (parameter omitted)
**Then** in both cases the tool writes the response body to `{InstanceFolderPath}/.fetch-cache/{sha256(url)}.html`
**And** returns the existing Story 9.7 JSON handle shape `{ url, status, content_type, size_bytes, saved_to, truncated }`
**And** the SSRF / timeout / cancellation / Story 9.6 byte ceiling / empty-body / HTTP-error string contracts are all preserved exactly
**And** every existing `FetchUrlToolTests` test passes unchanged.

**AC #4 — `extract: "structured"` returns the locked structured handle shape against the 100 KB fixture.**

**Given** the captured fixture `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-100kb.html`
**When** `fetch_url(url, extract: "structured")` is called against a stub HTTP handler that serves the fixture as the response body with `Content-Type: text/html; charset=utf-8`
**Then** the tool returns a JSON handle whose **deserialised shape** contains exactly these top-level fields: `url`, `status`, `title`, `meta_description`, `headings` (object with `h1` array, `h2` array, `h3_h6_count` int), `word_count` int, `images` (object with `total`, `with_alt`, `missing_alt` ints), `links` (object with `internal`, `external` ints), `truncated` bool, `truncated_during_parse` bool
**And** `title` matches the fixture's `<title>` element text content (whitespace-trimmed)
**And** `headings.h1` contains the text content of every `<h1>` element in the fixture
**And** `images.total == images.with_alt + images.missing_alt`
**And** `links.internal + links.external == document.QuerySelectorAll("a[href]").Length`
**And** `truncated == false` and `truncated_during_parse == false`
**And** the same call run twice over the same fixture returns deserialised values that are byte-identical (AngleSharp determinism).

**AC #5 — `extract: "structured"` against the 207 KB fixture populates non-empty meta_description and word_count.**

**Given** the captured fixture `fetch-url-500kb.html` (~207 KB)
**When** `fetch_url(url, extract: "structured")` is called
**Then** the returned handle has a non-null `meta_description` (or, if the fixture genuinely has none, has `meta_description: null` and the test asserts that the fixture HTML has no `<meta name="description">`)
**And** `word_count > 0`
**And** every locked field is present.

**AC #6 — `extract: "structured"` against the 1 MB fixture exercises the parser layer at production-scale and reports `truncated_during_parse: false` when no truncation occurred.**

**Given** the captured fixture `fetch-url-1500kb.html` (~1 MB)
**And** the workflow's `fetch_url.max_response_bytes` is set to 2097152 (the existing CQA workflow default — fixture fits under the ceiling)
**When** `fetch_url(url, extract: "structured")` is called
**Then** the parsed structured fields are populated
**And** `truncated == false`
**And** `truncated_during_parse == false`
**And** memory profiling (or a code review of the implementation) confirms no allocation proportional to repeated parses (single parser pass per call).

**AC #7 — `truncated_during_parse: true` is set correctly when the byte-stream layer truncated before AngleSharp parsed.**

**Given** the 1 MB fixture
**And** a workflow-level override pushing `fetch_url.max_response_bytes` down to 65536 (64 KB) so the fixture is forced through the existing Story 9.6 truncation
**When** `fetch_url(url, extract: "structured")` is called
**Then** the existing byte-stream truncation marker behaviour is preserved (the `extract: "raw"` cache write is unaffected, see open question Q1 below)
**And** the structured handle reports `truncated == true` and `truncated_during_parse == true`
**And** the structured fields are still populated from whatever AngleSharp could parse out of the truncated bytes (best-effort; partial fields are acceptable when `truncated_during_parse == true`)
**And** AngleSharp does **not** try to parse the literal truncation marker text as HTML (see Dev Notes — Phase 1 implementation must strip or avoid feeding the marker to the parser).

**AC #8 — Empty body and HTTP error responses preserve today's behaviour irrespective of `extract` value.**

**Given** an HTTP 204 / 3xx / 200-with-Content-Length-0 response
**When** `fetch_url(url, extract: "structured")` is called
**Then** the tool returns the existing string error contract for HTTP error / redirect responses (unchanged from Story 9.7) **OR** the existing empty-body handle (`saved_to: null, size_bytes: 0`) for the 204 / 200-empty case
**And** AngleSharp is **not** invoked
**And** there is no attempt to populate structured fields from a missing body.

**AC #9 — Non-HTML content type with `extract: "structured"` raises `ToolExecutionException` (Q5 resolved).**

**Given** an HTTP 200 with `Content-Type: application/pdf` (or `image/png`, `application/json`, etc.)
**When** `fetch_url(url, extract: "structured")` is called
**Then** the tool raises `ToolExecutionException` with the verbatim message `"Cannot extract structured fields from content type '<type>'. Use extract: 'raw' instead."`
**And** AngleSharp is **not** invoked
**And** the content-type check happens after the existing HTTP-status / empty-body / content sniffing checks but before parser invocation
**And** Story 9.4 (Accessibility Quick-Scan) may add a sibling `extract` mode later for its own needs — that is **out of scope** for 9.1b. _Locked by architect review 2026-04-09._

**AC #10 — Scanner.md is rewritten and consumes the structured shape.**

**Given** scanner.md after the Phase 1 prompt rewrite
**When** reviewed by Adam against the locked invariants
**Then** every audit fetch uses `fetch_url(url, extract: "structured")` (not raw)
**And** the verbatim opening line is preserved
**And** the five hard invariants from Story 9.1c's partial milestone are preserved verbatim:
  1. Verbatim opening line
  2. Between-fetches rule (one short progress update between fetches; no narration after the final fetch in a batch)
  3. Sequential-fetch invariant (one fetch per assistant turn; no parallel tool calls)
  4. Post-fetch → write_file invariant (after the final fetch, the next assistant turn must call write_file, not produce text)
  5. Standalone-text invariant (no standalone text turns once tools are available)
**And** the failure-bucketing logic for non-HTML content types and HTTP errors is preserved
**And** the "defending against context bloat" framing (the verbose contract-defence prose, the read_file-cached-content-inspection prose carried over from Story 9.7's session amendments) is **stripped** because the new contract eliminates the failure mode upstream
**And** the resulting prompt is **shorter** than today's, not longer.

**AC #11 — analyser.md and reporter.md are rewritten to consume structured fields.**

**Given** the new scanner.md writes `scan-results.md` whose facts are sourced directly from the structured handle
**When** analyser.md and reporter.md run against `scan-results.md`
**Then** they reference the structured fields by name (e.g. "based on the 5 missing alt-text images reported in the scanner's images.missing_alt field") rather than re-parsing the scanner's narrative
**And** the prompts include the explicit instruction "only flag issues you can directly cite from the structured fields returned by `fetch_url(extract: 'structured')`"
**And** the prompts handle null/empty fields gracefully (e.g. AngleSharp recovered nothing from a malformed page → `title: null, headings.h1: []`).

**AC #12 — Existing test suite stays green.**

`dotnet test AgentRun.Umbraco.slnx` reports **0 failures across all projects** after Phase 1 ships. The pre-9.1b suite (421 passing post-9.9) plus the new parser-layer tests must all pass.

**AC #13 — Phase 1 manual E2E gate (the gate that closes Phase 1 and opens Phase 2).**

**Given** Phase 1's code, prompt rewrites, and unit tests are committed
**When** Adam runs the Content Quality Audit workflow against the same 5-URL batch from instance `caed201cbc5d4a9eb6a68f1ff6aafb06` (the canonical failing instance)
**Then** the scanner agent completes every step without stalling
**And** every `tool_result` block from `fetch_url` in `conversation-scanner.jsonl` is a structured JSON handle (small — under ~2 KB per call regardless of source page size)
**And** `read_file` is **not** called against any cached HTML payload during the audit pattern (the structured fields make it unnecessary)
**And** the workflow reaches step `Complete`
**And** `audit-report.md` is produced and is structurally well-formed (executive summary, top findings, page-by-page breakdown, prioritised recommendations).

### Phase 2 — Polish (5-pass soft cap, architect escalation on overrun)

**AC #14 — Trustworthiness gate signoff artefact exists.**

**Given** Phase 2 has begun
**When** the signoff artefact at `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` is inspected
**Then** the file exists
**And** it lists every test input Adam used (the ≥3 representative real test inputs — mix of his own pages, public pages with known issues, and at least one deliberately problematic page)
**And** for each input it records: the URL set, the polish-pass number on which it passed, the resulting `audit-report.md` excerpt, and Adam's signed verdict ("trustworthy / would send to a paying client without rewriting").

**AC #15 — Trustworthiness gate passes against ≥3 real test inputs within the 5-pass soft cap.**

**Given** ≥3 representative real test inputs of Adam's choice
**When** the workflow is run against each
**Then** every run produces a report Adam would be willing to send to a paying client without rewriting
**And** the report identifies real issues specific to the actual fetched content — not generic advice that could apply to any page
**And** the recommendations are tied to evidence the agent observed in the structured fields (not invented, not hallucinated)
**And** the report is non-trivially useful (length within ~500–2000 words, structurally complete, top 3 findings identifiable on first read)
**And** convergence is reached within **5 polish passes** — or, if not, architect escalation is triggered to Winston for prompt structure rethink. The cap is 5 *passes*, not 5 minor edits within a pass.

**AC #16 — Determinism property holds across repeat runs against the same URL.**

**Given** five runs of the workflow against the same real URL after Phase 2 lock
**When** the outputs are compared
**Then** the extracted *facts* in `scan-results.md` are byte-identical (AngleSharp determinism: identical input HTML produces identical extracted fields)
**And** the analyser's flagged issues are consistent across runs (the model's reasoning over deterministic facts is stable, even if phrasing varies)
**And** the reporter's structural sections are present and predictably ordered (executive summary → top findings → page-by-page breakdown → prioritised recommendations)
**And** the report length stays within a reasonable band (~500–2000 words)
**And** no run produces a structurally broken document.

**AC #17 — Non-technical reviewer test.**

**Given** Adam reading `audit-report.md` as if he were a marketing director receiving the report
**When** he evaluates it
**Then** he can understand it without technical context
**And** he can identify the top 3 issues without re-reading
**And** the recommendations read as credible and worth acting on
**And** he would forward the report to a colleague without embarrassment.

**AC #18 — Phase 2 manual E2E (production smoke).**

Final lock-pass: run the workflow against a clean fresh TestSite build with the locked prompts and confirm end-to-end completion plus signoff. Recorded as the closing entry in the signoff artefact.

## What NOT to Build

- **Do NOT add a sibling tool** (e.g. `fetch_url_structured`). The architect explicitly rejected this in the reshape SCP. Option 2 is implemented as the `extract` mode parameter on the existing `fetch_url` tool.
- **Do NOT change the workflow.yaml step structure.** Keep three steps (scanner, analyser, reporter). Keep the scanner's `tools: [fetch_url, read_file, write_file]` declaration unchanged.
- **Do NOT add a new workflow tunable for `extract`.** It is an agent-supplied argument, not a workflow-author option. The resolution chain (Story 9.6) is not extended.
- **Do NOT add agent-side `max_bytes` arguments, chunking, sliced reads, or any new parsing controls.** The byte ceiling stays where Story 9.6 left it.
- **Do NOT include a `content` backup field anywhere in the structured return shape.** Trust the parser. If parsing fails, the tool errors — it does not silently fall back to inline raw HTML.
- **Do NOT add retry logic at the engine level** — that's Story 9.0 / Story 10.6 territory.
- **Do NOT generalise the prompts across multiple content types.** Content Quality Audit is a single fixed use case for V1.
- **Do NOT bundle sample content or pre-fetched HTML.** Story 9.1c removed that approach.
- **Do NOT capture new test fixtures.** Reuse the three fixtures already in `AgentRun.Umbraco.Tests/Tools/Fixtures/`.
- **Do NOT re-evaluate the parser choice.** AngleSharp is locked. If implementation surfaces a real reason to revisit, escalate to Adam — do not silently substitute.
- **Do NOT regress the five hard invariants in scanner.md.** They are preserved verbatim. The contract-defence verbosity is what gets stripped.
- **Do NOT touch `ReadFileTool.cs`, `WriteFileTool.cs`, `ListFilesTool.cs`, `ToolLoop.cs`, `SsrfProtection.cs`, `PathSandbox.cs`, or `ToolLimitResolver.cs`.** Out of scope.
- **Do NOT modify the Story 9.7, 9.9, 9.1c, 9.6, 9.0 spec files, the architectural finding reports, the SCPs, or `v2-future-considerations.md` / `project-context.md`.**
- **Do NOT raise raw `InvalidOperationException` for missing Step/Workflow context.** Use the existing `ToolContextMissingException : AgentRunException` added in Story 9.9 D2 (already in [`AgentRun.Umbraco/Engine/ToolContextMissingException.cs`](../../AgentRun.Umbraco/Engine/ToolContextMissingException.cs)). The current `FetchUrlTool.cs` already does this correctly post-9.9 — preserve it. See feedback memory `feedback_agentrun_exception_classifier.md` for why.
- **Do NOT add a JSON Schema entry for `extract` in `workflow.yaml`'s schema.** That is a Story 9.2 forward dependency. Flag it; do not block on it.
- **Do NOT split Phase 1 across multiple commits.** The prompt rewrites are tightly coupled to the new tool contract — see Dev Notes Q4. Single commit unless architect review surfaces a reason to split.

## Failure & Edge Cases

**Deny-by-default statement:** Unrecognised or invalid `extract` values MUST be rejected at the parameter-extraction layer with a clear `ToolExecutionException` — never silently coerced to `"raw"` or `"structured"`. SSRF protection (`SsrfProtection.ValidateUrlAsync`) continues to gate every fetch regardless of `extract` value. The structured-extraction code path receives **untrusted HTML from arbitrary URLs**; AngleSharp's HTML5 parser is the trust boundary at the parser layer (it is designed for hostile input), and the JSON serialisation of the structured handle is the trust boundary at the return layer. No raw HTML, raw script content, or unsanitised attribute values are ever returned in the structured handle — only the extracted facts (text content, counts, strings).

| # | Case | Expected behaviour |
|---|---|---|
| 1 | `extract` argument is missing / null | Default to `"raw"`. Existing Story 9.7 behaviour. Regression guard for AC #3. |
| 2 | `extract` argument is `"raw"` | Existing Story 9.7 behaviour, byte-for-byte. AC #3. |
| 3 | `extract` argument is `"structured"` and response is well-formed `text/html` | AngleSharp parses, structured handle returned. AC #4 / #5 / #6. |
| 4 | `extract` argument is any other string (`"json"`, `"markdown"`, `""`, etc.) | `ToolExecutionException("Invalid extract value: '<value>'. Must be 'raw' or 'structured'.")`. Same shape as the existing invalid-URL error. |
| 5 | `extract` argument is not a string (e.g. integer, boolean, object) | `ToolExecutionException` per the existing argument-extraction pattern. |
| 6 | `extract: "structured"` against an HTTP error (4xx/5xx) or redirect (3xx) | Return the existing string error contract. AngleSharp not invoked. AC #8. |
| 7 | `extract: "structured"` against an empty body (204, 200 with `Content-Length: 0`) | Return the existing empty-body handle (`saved_to: null, size_bytes: 0`). AngleSharp not invoked. AC #8. |
| 8 | `extract: "structured"` against non-HTML content type (`application/pdf`, `image/png`, `application/json`, etc.) | **Q5 resolved (architect review 2026-04-09):** raise `ToolExecutionException("Cannot extract structured fields from content type '<type>'. Use extract: 'raw' instead.")`. The content-type check happens after the existing content sniffing and before AngleSharp is invoked. See AC #9. |
| 9 | `extract: "structured"` and the byte-stream layer hit the Story 9.6 ceiling | AngleSharp parses whatever bytes were captured, with the truncation marker text **stripped or avoided** so the parser does not see it as HTML (see Dev Notes Q2). `truncated == true`, `truncated_during_parse == true`, structured fields are best-effort against the partial document. AC #7. |
| 10 | AngleSharp encounters genuinely malformed HTML | AngleSharp's HTML5 parsing algorithm recovers the way browsers do. Structured fields may be sparse (`title: null`, empty heading lists). The handle is still returned successfully. The analyser/reporter prompts handle null/empty fields gracefully (AC #11). |
| 11 | AngleSharp throws on parse (out-of-memory, internal error, anything unexpected) | Wrap in `ToolExecutionException("Failed to parse response as HTML: {underlying message}")`. **Do NOT silently fall back to `extract: "raw"`** — the agent asked for structured, the agent gets either structured or an error. |
| 12 | Same URL fetched twice in a single instance, once `extract: "raw"` then `extract: "structured"` | Both calls succeed. `extract: "raw"` writes/uses the cache file as today; `extract: "structured"` resolves per Q1 below (cache or skip). No interference. |
| 13 | `context.Step` or `context.Workflow` is null (engine wiring bug) | `ToolContextMissingException` (existing post-9.9 behaviour). Preserved. |
| 14 | `context.InstanceFolderPath` is empty or not a valid directory and `extract: "structured"` is requested | **Acceptable** — Q1 resolved (structured mode skips the cache, so no write happens). For `extract: "raw"`, the existing 9.7 behaviour is preserved (the cache write fails loud). The `InstanceFolderPath` field is non-nullable on `ToolExecutionContext` at the type level, so the realistic failure mode is a mis-constructed context with an empty path string, not an actual null reference. Locked by architect review 2026-04-09. |
| 15 | Phase 2 trustworthiness gate fails after 5 polish passes | Architect escalation to Winston for prompt structure rethink. The cap is 5 *passes*, not 5 minor edits within a pass. |
| 16 | Phase 2 reports are inconsistent between models or providers | Lock to Anthropic Sonnet for the beta (already the default in workflow.yaml). Document the recommendation in the workflow authoring guide. |
| 17 | A test input is so large it pushes against `fetch_url.max_response_bytes` | Byte ceiling still truncates at the byte-stream layer. AngleSharp parses the truncated document. `truncated_during_parse: true` is surfaced; the analyser must check this flag and mark its findings as "based on partial content" when set. The polish loop must verify this behaviour against at least one such test input. |
| 18 | Sonnet 4.6 parallel-tool-call stall | Defended in two upstream layers per the original 9.1b reshape SCP — (i) the sequential-fetch invariant in scanner.md from Story 9.1c's partial milestone, (ii) the offloaded handle pattern from Story 9.7. With Phase 1's structured handles further reducing per-call context cost, this should not recur. The Phase 2 polish loop only needs to spot-check that both defences hold. |
| 19 | Retry-replay degeneration | **Out of scope for 9.1b.** Promoted to Story 10.6 — Retry-Replay Degeneration Recovery via the original reshape SCP. Do not attempt to fix in 9.1b. |
| 20 | The captured fixtures get deleted between Story 9.7 and Story 9.1b implementation | Verified present 2026-04-08 in `AgentRun.Umbraco.Tests/Tools/Fixtures/` — `fetch-url-100kb.html`, `fetch-url-500kb.html`, `fetch-url-1500kb.html`, `README.md`. If somehow they are missing at implementation time, stop and ask Adam — do not capture new ones silently. |
| 21 | Redirect chain to a private/internal IP (e.g. server returns 302 → `http://169.254.169.254/`) | Each redirect target is re-validated through `SsrfProtection.ValidateUrlAsync` before the next `SendAsync`. SSRF rejection at any hop returns the existing security error contract. The chain is capped at 5 redirects (6 total requests); exceeding the cap throws `ToolExecutionException("Too many redirects (max 5)")`. Custom headers and cookies are NOT forwarded across hops. The pre-existing `timeoutCts` linked-token bound applies to the entire chain wall-clock, not per-hop. _Added during Phase 1 code-review fix pass 2026-04-09 — see Locked Decision #11._ |
| 22 | Bot-protection 403 against bare-UA clients (Cloudflare, Fastly, AWS WAF, Wikipedia, GitHub, news sites) | `FetchUrlTool` sets a generic `User-Agent: AgentRun/1.0` on every outgoing `HttpRequestMessage` (initial fetch and every redirect hop). The default is a property of the engine, not workflow-specific — any workflow that fetches arbitrary public URLs benefits. If a future workflow needs a custom UA, that becomes a Story 9.6-style `ToolLimitResolver` / workflow-YAML tunable, not a hardcoded change. _Added during Phase 1 manual E2E gate carve-out 2026-04-09 — Wikipedia 403 was the canary._ |

## Tasks / Subtasks

### Task 1 — Add the AngleSharp NuGet reference (Phase 1)

- [x] 1.1 In `AgentRun.Umbraco.csproj`, add a `<PackageReference Include="AngleSharp" Version="..."/>` pinned to a specific version current on NuGet.org at implementation time.
- [x] 1.2 Run `dotnet restore` and confirm no transitive dependency conflicts against Umbraco / Microsoft.Extensions.AI / the existing tree.
- [x] 1.3 Record the resolved AngleSharp version in the Dev Agent Record.
- [x] 1.4 If the architect-review pass surfaced any version-pinning guidance (e.g. avoid pre-release), follow it.

### Task 2 — Extend `fetch_url`'s schema with the `extract` parameter (Phase 1)

- [x] 2.1 Update the `Schema` JSON literal in `FetchUrlTool.cs` to add the optional `extract` property with enum `["raw", "structured"]` and default `"raw"`.
- [x] 2.2 Confirm the schema validates against itself (well-formed JSON Schema). AC #2.
- [x] 2.3 Add a parameter-extraction helper that pulls `extract` from `arguments`, defaults to `"raw"` when missing/null, and throws `ToolExecutionException("Invalid extract value: '<value>'. Must be 'raw' or 'structured'.")` for any other value or non-string type. Edge Cases #4 / #5.

### Task 3 — Implement the `extract: "structured"` code path (Phase 1)

- [x] 3.1 In `ExecuteAsync`, after the existing argument extraction and the `extract` value parse, branch on the `extract` value. The `extract: "raw"` branch is the existing post-9.7 code path **byte-for-byte unchanged**.
- [x] 3.2 In the `extract: "structured"` branch, preserve every existing safety / context check up to and including the `bytesToWrite` computation: SSRF validation, the `ToolContextMissingException` guard for missing Step/Workflow, the timeout CTS, the byte-stream read loop with the Story 9.6 ceiling, the empty-body short-circuit, and the HTTP-error string contract. **Do not duplicate any of these checks** — the `extract` branch happens *after* them.
- [x] 3.3 Resolve the cache-write decision per architect review Q1 below: either (a) write the bytes to `.fetch-cache/` exactly as the raw branch does and then parse from disk, or (b) parse the in-memory bytes directly without writing to disk for structured mode. Implement whichever Q1 resolves to. Document the choice inline in the code with a comment pointing at this spec.
- [x] 3.4 Strip or avoid feeding the truncation marker text to AngleSharp when `truncated == true`. The current code (lines 121–129 of `FetchUrlTool.cs`) appends the marker as bytes to `bytesToWrite` *before* the cache write. For structured mode, parse the **un-marked** bytes (the original `buffer[0..maxBytes]` slice without the marker) so AngleSharp does not see `[Response truncated at N bytes]` as HTML content. Set `truncated_during_parse: true` in the structured handle whenever `truncated == true`. Edge Case #9 / AC #7. (See Dev Notes Q2 for why.)
- [x] 3.5 Feed the bytes to `new HtmlParser().ParseDocument(stream-or-string)` (use AngleSharp's recommended entry point — `BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(...))` or `new HtmlParser().ParseDocument(stream)` — whichever the architect-review surfaces as cleanest).
- [x] 3.6 Build the structured handle from the AngleSharp document using the locked selector mappings:
  - `title` ← `string.IsNullOrWhiteSpace(document.Title) ? null : document.Title.Trim()` (AngleSharp returns `""` for both missing and empty `<title>`; normalise to `null` for the agent's null-handling pattern — locked by architect review 2026-04-09)
  - `meta_description` ← `document.QuerySelector("meta[name=description]")?.GetAttribute("content")`
  - `headings.h1` ← `document.QuerySelectorAll("h1").Select(e => e.TextContent.Trim()).ToList()`
  - `headings.h2` ← `document.QuerySelectorAll("h2").Select(e => e.TextContent.Trim()).ToList()`
  - `headings.h3_h6_count` ← `document.QuerySelectorAll("h3, h4, h5, h6").Length`
  - `word_count` ← `document.Body?.TextContent?.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries).Length ?? 0`
  - `images.total` ← `document.QuerySelectorAll("img").Length`
  - `images.with_alt` ← count where `img.GetAttribute("alt") != null` (architect-locked 2026-04-09: attribute *existence* is the rule, not non-emptiness — `<img alt="">` is the accessibility-correct decorative-image marker and counts as "with alt"; see Dev Notes)
  - `images.missing_alt` ← `total - with_alt`
  - `links.internal` / `links.external` ← classify each `a[href]` by parsing the href against the source URL's host (relative hrefs and same-host hrefs are internal; everything else is external; ignore non-http(s) schemes)
  - `truncated` ← from the existing byte-stream layer
  - `truncated_during_parse` ← `truncated` value (parser saw a truncated document iff the byte stream truncated)
- [x] 3.7 Serialise the handle as JSON via `JsonSerializer.Serialize` with the existing `HandleJsonOptions`. Return the string.
- [x] 3.8 Wrap any unexpected AngleSharp exception in `ToolExecutionException("Failed to parse response as HTML: {underlying message}")`. Do **not** silently fall back to `extract: "raw"`. Edge Case #11.

### Task 4 — Parser-layer regression and contract tests (Phase 1)

Add to `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs`. Aim for ~8–10 new tests:

- [x] 4.1 `Extract_Raw_DefaultBehaviour_PreservesStory97Handle` — AC #3 regression guard. Calls without the parameter; asserts the existing Story 9.7 handle shape and the cache write byte-for-byte.
- [x] 4.2 `Extract_Raw_ExplicitParameter_PreservesStory97Handle` — AC #3 with the parameter explicitly set to `"raw"`.
- [x] 4.3 `Extract_Structured_100kb_ProducesLockedShape` — AC #4. Asserts every locked top-level field is present, types are correct, deserialised values match the fixture.
- [x] 4.4 `Extract_Structured_207kb_PopulatesMetaAndWordCount` — AC #5.
- [x] 4.5 `Extract_Structured_1mb_ParsesAtProductionScale` — AC #6.
- [x] 4.6 `Extract_Structured_DeterministicAcrossTwoParses` — AC #4 byte-identical determinism. Two calls against the same fixture, asserts deserialised structured fields are equal.
- [x] 4.7 `Extract_Structured_TruncatedDuringParse_FlagSetCorrectly` — AC #7. Workflow override pushes `max_response_bytes` to 65536; fixture is the 1 MB; assert `truncated == true`, `truncated_during_parse == true`, structured fields populated best-effort, and (critically) AngleSharp did not see the truncation marker text.
- [x] 4.8 `Extract_Structured_NotInvokedOn_HttpError` — AC #8 regression. Stub HTTP handler returns 404; assert the existing string error contract is returned and AngleSharp was not invoked.
- [x] 4.9 `Extract_Structured_NotInvokedOn_EmptyBody` — AC #8 regression. 204 / Content-Length 0 returns the existing empty-body handle; AngleSharp not invoked.
- [x] 4.10 `Extract_Structured_NonHtmlContentType_ThrowsToolExecutionException` — AC #9. Stub HTTP handler returns 200 with `Content-Type: application/pdf` (or `application/json`); assert the call raises `ToolExecutionException` whose message is verbatim `"Cannot extract structured fields from content type 'application/pdf'. Use extract: 'raw' instead."` and that AngleSharp was not invoked. Locked per Q5 architect review 2026-04-09.
- [x] 4.11 `Extract_InvalidValue_ThrowsToolExecutionException` — Edge Case #4. `extract: "json"` → throws with the locked error message.
- [x] 4.12 `Extract_NonStringType_ThrowsToolExecutionException` — Edge Case #5.
- [x] 4.13 `Extract_Structured_ImagesAccountingHolds` — AC #4 invariant: `images.total == images.with_alt + images.missing_alt`.
- [x] 4.14 `Extract_Structured_LinksClassificationCorrect` — internal vs external classification against a hand-crafted fixture or the captured 100 KB fixture.

(Task numbering is a guideline. Aim for ~10 well-targeted tests; do not pad.)

### Task 5 — Rewrite scanner.md to consume the structured shape (Phase 1)

- [x] 5.1 Read the current `scanner.md` end-to-end. Identify every section that defends against context bloat (the "Critical: Interactive Mode Behaviour" framing, the verbose post-fetch narration warnings, the read_file-cached-content-inspection prose carried over from Story 9.7's session amendments).
- [x] 5.2 Identify and preserve verbatim:
  - The opening line.
  - The five hard invariants from Story 9.1c's partial milestone (between-fetches rule, sequential-fetch, post-fetch → write_file, standalone-text, opening line).
  - The failure-bucketing logic for non-HTML content types and HTTP errors.
- [x] 5.3 Switch every audit fetch instruction from `fetch_url(url)` to `fetch_url(url, extract: "structured")`. Document the structured handle's shape in the prompt so the agent knows what fields it gets back (this is the smallest possible "what the tool returns" reference — do **not** re-explain context bloat; the contract eliminates it).
- [x] 5.4 Rewrite the post-fetch instructions: instead of "use read_file to inspect the cached page", say "the structured handle is the source of truth; write the audit notes from those fields directly to scan-results.md".
- [x] 5.5 Strip the contract-defence verbosity. The new prompt is **shorter** than today's, not longer. AC #10.
- [x] 5.6 Verify the failing-instance reference (`642b6c583e3540cda11a8d88938f37e1` was the OLD pre-9.0 stall instance; the relevant 2026-04-07 finding instance is `caed201cbc5d4a9eb6a68f1ff6aafb06`) is corrected if it survives anywhere in the prompt.

### Task 6 — Rewrite analyser.md and reporter.md to consume structured fields (Phase 1)

- [x] 6.1 In analyser.md, replace any "re-parse the scanner's narrative" framing with "consume the structured fields directly from scan-results.md" framing.
- [x] 6.2 Add the explicit instruction: "only flag issues you can directly cite from the structured fields returned by `fetch_url(extract: 'structured')`".
- [x] 6.3 Add null/empty-field handling guidance: "if `title` is null or `headings.h1` is empty, the page may be malformed or non-HTML — flag in the report and do not invent missing facts".
- [x] 6.4 In reporter.md, same treatment. Trim verbosity that defended against now-impossible failure modes.
- [x] 6.5 Confirm the structural sections in the reporter's output are still defined: executive summary → top findings → page-by-page breakdown → prioritised recommendations.

### Task 7 — Run the test suites (Phase 1)

- [x] 7.1 `dotnet test AgentRun.Umbraco.slnx` → must report **0 failures across all projects**. AC #12.
- [x] 7.2 `npm test` in `Client/` if applicable (this story is server + prompt only, so likely `[~]`).

### Task 8 — Phase 1 manual E2E gate (Adam owns this — closes Phase 1)

- [ ] 8.1 Build the unmerged Phase 1 branch into a fresh TestSite.
- [ ] 8.2 Run the Content Quality Audit workflow against the same 5-URL batch from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`.
- [ ] 8.3 Inspect `conversation-scanner.jsonl`. Confirm: every `fetch_url` `tool_result` is a small structured handle (under ~2 KB); `read_file` is **not** called against any cached HTML; the workflow reaches step `Complete`; `audit-report.md` is structurally well-formed. AC #13.
- [ ] 8.4 If any Phase 1 AC fails, fix and re-run. Phase 1 is unbounded in iteration cycles — it ships when the gate passes.

### Task 9 — Phase 2 polish loop kick-off (5-pass soft cap)

- [ ] 9.1 Create the signoff artefact at `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` (or confirm location with Adam first if a different convention is preferred). Seed with the structure required by AC #14.
- [ ] 9.2 Adam picks ≥3 representative real test inputs (mix of his own pages, public pages with known issues, at least one deliberately problematic page).
- [ ] 9.3 Run the workflow against each input.
- [ ] 9.4 Review each agent's output critically against the trustworthiness gate. Identify what's weak, generic, hallucinated, or evidence-free. Note that with deterministic parsing in Phase 1, the iteration target is the model's *reasoning over facts*, not its parsing.
- [ ] 9.5 Iterate on the agent prompt markdown files (scanner.md, analyser.md, reporter.md). Re-run.
- [ ] 9.6 Repeat until the trustworthiness gate passes for every test input. **5-pass soft cap (polish phase only).** If 5 passes do not converge, escalate to Winston for prompt structure rethink.

### Task 10 — Phase 2 signoff and lock (closes Phase 2 and the story)

- [ ] 10.1 Adam signs the trustworthiness signoff artefact. AC #14 / #15.
- [ ] 10.2 Confirm AC #16 (determinism property holds across repeat runs) by running the locked workflow five times against one of the test inputs and comparing the resulting `scan-results.md` for byte-identity of the extracted facts.
- [ ] 10.3 Confirm AC #17 (non-technical reviewer test).
- [ ] 10.4 Production smoke test (AC #18): clean fresh TestSite build, locked prompts, end-to-end completion. Recorded as the closing entry in the signoff artefact.
- [ ] 10.5 Lock the prompts. Commit with a clear message noting the polish pass is complete and pointing back at this spec.

## Dev Notes

### Phase 1 implementation notes (engine + tool contract)

**Branch on `extract` *after* every existing safety check.** The `extract: "raw"` branch is the existing post-9.7 code path verbatim. The `extract: "structured"` branch reuses everything up to and including the `bytesToWrite` computation; only the disposition (write to disk + return raw handle vs parse + return structured handle) differs. Do **not** duplicate the SSRF / timeout / context / byte-ceiling / empty-body / HTTP-error logic — that path is shared and is the most-tested code in the project.

**Use `ToolContextMissingException`, not `InvalidOperationException`.** The current `FetchUrlTool.cs` already does this correctly post-Story 9.9 D2 (lines 63–68). Preserve that. Per `feedback_agentrun_exception_classifier.md`, raw `InvalidOperationException` from a tool gets misclassified by `LlmErrorClassifier` as a generic provider error. The typed `AgentRunException` subtype is the only thing the classifier handles correctly.

**The truncation marker is in `bytesToWrite`.** This is a Phase 1 footgun. Read [`FetchUrlTool.cs:121-129`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L121-L129) carefully:

```csharp
var truncated = totalRead > maxBytes;
byte[] bytesToWrite;
if (truncated)
{
    var marker = Encoding.UTF8.GetBytes($"\n\n[Response truncated at {maxBytes} bytes]");
    bytesToWrite = new byte[maxBytes + marker.Length];
    Buffer.BlockCopy(buffer, 0, bytesToWrite, 0, maxBytes);
    Buffer.BlockCopy(marker, 0, bytesToWrite, maxBytes, marker.Length);
}
```

The marker is appended to the bytes that get written to `.fetch-cache/`. For `extract: "raw"`, that is the correct existing behaviour and is preserved. For `extract: "structured"`, **AngleSharp must NOT see the marker text** — it would otherwise treat `[Response truncated at N bytes]` as HTML text content and either insert it as a stray text node or (depending on the surrounding markup state) corrupt the parsed DOM. **The structured branch must parse the original `buffer[0..maxBytes]` slice (the un-marked truncated bytes), not `bytesToWrite`.** Set `truncated_during_parse: true` whenever `truncated == true`. This is concrete enough that the implementation can do it without further architect input — but it is also captured in Q2 below for the architect-review eyeball.

**Internal vs external link classification.** Parse `uri.Host` from the source URL. For each `a[href]`:
- Skip non-http(s) schemes (`mailto:`, `tel:`, `javascript:`, `#fragment-only`).
- Resolve relative hrefs against the source URL via `new Uri(sourceUri, hrefAttribute)`.
- Compare the resolved host (case-insensitive) to the source host. Equal → internal. Different → external.
- If `Uri.TryCreate` fails (malformed href), classify as external (or skip — pick one and write a test). Pick the simpler option.

**Strict host equality is intentional (D3 — Phase 1 code-review fix pass 2026-04-09).** `www.example.com` is classified as **external** from `example.com`, and any subdomain (`blog.example.com`, `shop.example.com`, `m.example.com`) is likewise external. The structured handle's `links.internal` / `links.external` fields are raw structural facts at the parser layer, not opinionated SEO classifications. SEO-flavoured "same site" judgements (treating www. and apex as one site, treating subdomains as same-site, registrable-domain matching via a public suffix list) are the analyser/reporter's job in Phase 2's polish loop, not the parser's. If a future workflow needs registrable-domain matching, that is the job for that workflow's prompt or for a new tool — not for `fetch_url(extract: "structured")`. **Phase 2 watch-item:** during the trustworthiness gate test runs, watch specifically for whether strict host-equality classification produces misleading audits on real test inputs. If yes, the analyser prompt gets an explicit "treat www-prefixed links to the source domain as internal" instruction; if no, leave the strict default in place.

**Locked Decision #11 — Manual HTTP redirect handling with per-hop SSRF re-validation (D2 — Phase 1 code-review fix pass 2026-04-09).** HTTP redirects in `fetch_url` are followed manually, not automatically. The `FetchUrl` named `HttpClient` is configured with `AllowAutoRedirect = false` in `AgentRunComposer`. `FetchUrlTool.ExecuteAsync` implements a manual redirect-following loop that re-runs `SsrfProtection.ValidateUrlAsync` against each `Location` URL before the next `SendAsync`. The chain is capped at 5 redirects (6 total HTTP requests; matches `HttpClientHandler.MaxAutomaticRedirections` default); exceeding the cap throws `ToolExecutionException("Too many redirects (max 5)")`. Relative `Location` headers are resolved against the **current** request URI via `new Uri(currentUri, locationHeaderValue)` per RFC 7231 §7.1.2. Custom headers and cookies are intentionally NOT forwarded across hops. The pre-existing `timeoutCts` linked-token bound applies to the entire redirect chain wall-clock, not per-hop. **This is a security invariant — do NOT switch the named client back to `AllowAutoRedirect = true` and do NOT remove the per-hop SSRF re-validation under any circumstances.** The pre-existing SSRF bypass it closes was discovered via Phase 1 code review on 2026-04-09 (no separate finding report; the discovery is captured in the Phase 1 fix-pass commit message and in this Locked Decision). **Out of scope (deliberately):** DNS rebinding defence (caching resolved IPs across hops), redirect-chain telemetry/logging, configurable max-hops via workflow YAML, and SSRF validation result caching across calls — all separate hardening stories.

**AngleSharp instantiation cost.** A fresh `HtmlParser` is cheap. Do not cache one across calls — the cost is dominated by the parse itself, not the instantiation. If profiling later shows otherwise, that is a separate concern.

**JSON serialisation of `null` fields.** `meta_description` and `title` may be null. The existing `HandleJsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never }` already serialises nulls. Preserve that — the agent prompts need to see `"title": null` explicitly so they handle it gracefully.

**No new tunables.** `extract` is an agent argument, not a workflow option. Story 9.6's `ToolLimitResolver` is **not** extended. If a future story wants a workflow-author opt-out (e.g. "always force raw mode for this workflow"), that is a separate concern.

**Raw + structured calls against the same URL are NOT deduplicated.** _Architect-locked 2026-04-09._ Each call fully executes. `extract: "raw"` writes to `.fetch-cache/`; `extract: "structured"` skips the cache and parses in-memory. The two modes share no state. If a future workflow needs both the structured fields and the raw HTML for the same URL, it issues two separate `fetch_url` calls. This is the simplest possible composability story and is correct for current usage (the scanner only needs structured).

**Empty `alt=""` is intentionally classified as "with alt".** _Architect-locked 2026-04-09._ The accessibility-correct interpretation of `<img alt="">` is "decorative image, intentionally not announced by screen readers" — the *existence* of the attribute is what matters, not the value. A future reviewer may be tempted to "fix" this to count empty-string alts as "missing alt"; do not. The locked rule is: `with_alt = count where img.GetAttribute("alt") != null` (i.e. `!string.IsNullOrEmpty` is wrong here — use `!= null`). Update Task 3.6's mapping accordingly: `images.with_alt` ← count where `img.GetAttribute("alt") != null`; `images.missing_alt` ← `images.total - images.with_alt`.

### Phase 1 prompt rewrite notes

**Prompts get shorter, not longer.** Strip the contract-defence verbosity. The current scanner.md contains substantial prose that exists only because the model previously had to defend against context bloat. After Phase 1, the contract eliminates the failure mode upstream, so the prose is dead weight. The five hard invariants govern *when* the agent calls tools, not *what the tools return* — they stay verbatim.

**The `read_file`-against-cached-content prose from Story 9.7's session amendments is removable.** With structured extraction, the scanner does not need to read cached HTML at all. The `read_file` tool stays in the scanner step's `tools` declaration (so the agent retains the affordance for edge cases like inspecting prior artifacts), but the prompt no longer instructs the agent to use it for parsing source HTML.

**The verbatim opening line stays.** It is one of the five hard invariants.

### Phase 2 polish loop notes

**The polish target is reasoning, not parsing.** With AngleSharp providing deterministic facts, hallucinated headings / drifting word counts / inconsistent alt-text counts should drop substantially or disappear. If they persist, the polish loop must add explicit "only cite the structured fields" instructions to the analyser prompt. If they still persist after that, the architect escalation triggers — the failure mode would no longer be in the prompt layer.

**5-pass soft cap is on passes, not edits.** A pass = one full run of the workflow against the chosen test input set followed by one prompt edit cycle. Five minor edits within a pass do not consume cap budget; five full re-runs do.

**Trustworthiness gate is subjective and that is the point.** It is Adam reading the report and deciding whether he would send it to a paying client. Do not invent additional objective metrics to short-circuit the gate. The signoff artefact is the source of truth; if it is signed, the gate passed.

## References

- [`AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) — the file modified by this story (Phase 1).
- [`AgentRun.Umbraco/Tools/ReadFileTool.cs`](../../AgentRun.Umbraco/Tools/ReadFileTool.cs) — read-only reference for the Story 9.9 `ToolContextMissingException` pattern and the resolution-chain integration.
- [`AgentRun.Umbraco/Engine/ToolContextMissingException.cs`](../../AgentRun.Umbraco/Engine/ToolContextMissingException.cs) — the typed `AgentRunException` subtype this story preserves.
- [`AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs) — existing test surface to extend.
- [`AgentRun.Umbraco.Tests/Tools/Fixtures/`](../../AgentRun.Umbraco.Tests/Tools/Fixtures/) — Story 9.7's captured fixtures, reused here. Confirmed present 2026-04-08: `fetch-url-100kb.html`, `fetch-url-500kb.html`, `fetch-url-1500kb.html`, `README.md`.
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md) — rewritten in Phase 1; iterated in Phase 2.
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/analyser.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/analyser.md) — same.
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md) — same.
- [`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml) — verified 2026-04-08; scanner step's `tools: [fetch_url, read_file, write_file]` declaration is correct as-is and is **not** modified by 9.1b.
- [9-1c-architectural-finding-fetch-url-context-bloat.md](../planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md) — first architectural finding (Option 2 = AngleSharp).
- [9-7-architectural-finding-read-file-bloat.md](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md) — second architectural finding (the reframing).
- [Sprint Change Proposal — 9.1b Rescope (original)](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope.md)
- [Sprint Change Proposal — 9.1b Rescope Addendum (read_file finding)](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md)
- [9-7-tool-result-offloading-fetch-url.md](9-7-tool-result-offloading-fetch-url.md) — the offloading layer this story extends.
- [9-9-read-file-size-guard.md](9-9-read-file-size-guard.md) — the defence-in-depth sibling.
- [9-6-workflow-configurable-tool-limits.md](9-6-workflow-configurable-tool-limits.md) — the byte ceiling that Phase 1 cooperates with.
- `_bmad-output/project-context.md` — standing project rules (`dotnet test AgentRun.Umbraco.slnx`, deny by default, failure cases mandatory, etc.)
- `feedback_agentrun_exception_classifier.md` — why `ToolContextMissingException` is required (engine-domain exceptions must skip `LlmErrorClassifier`).

## Definition of Done

- [ ] All Phase 1 Acceptance Criteria #1–#13 verified
- [ ] All Phase 2 Acceptance Criteria #14–#18 verified
- [ ] All Tasks 1–10 complete
- [ ] `dotnet test AgentRun.Umbraco.slnx` is green (target: 421 + ~10 new = ~431 passing)
- [ ] Phase 1 manual E2E gate (Task 8) verified by Adam against the 5-URL batch from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`
- [ ] Trustworthiness signoff artefact at `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` exists, lists ≥3 representative real test inputs, and is signed by Adam
- [ ] Phase 2 production smoke test executed on a clean fresh TestSite build with locked prompts
- [ ] `read_file` is **not** called against any cached HTML payload during the audit pattern in any verified run (the structural change closes the second context-bloat path)
- [ ] **Pre-Implementation Architect Review gate ticked** — Winston approved (see below)
- [ ] **Post-implementation code review** — Bob runs adversarial review against Phase 1's code changes before the story moves to `done`

## Pre-Implementation Architect Review

**This is a hard gate. Amelia does not start implementation until this checkbox is ticked.**

- [x] **Architect (Winston) has reviewed the structured return shape against AngleSharp's actual API and resolved the open questions below before code is written.**

**Reviewed:** Winston, 2026-04-09.

Field-by-field mappability of the locked structured shape verified against AngleSharp's `IDocument` / `IElement` API surface. All ten fields map cleanly to standard selectors and properties. One small refinement recorded for `title`: treat AngleSharp's empty-string return as `null` in the handle for consistency with the agent's null-handling pattern (applied to Task 3.6).

The `truncated_during_parse` mechanism is approved as Bob recommended: parse the un-marked `buffer[0..maxBytes]` slice directly rather than the post-marker `bytesToWrite`. Implementation hint for Amelia: compute the un-marked slice first, branch on `extract` after, and have the raw branch derive `bytesToWrite` from un-marked + marker as today. Don't compute `bytesToWrite` first and try to recover the un-marked bytes from it — backwards and brittle.

Strip-vs-stay list for prompt simplification approved as written. `read_file` stays in the scanner step's `tools` declaration as an affordance for inspecting prior step artifacts; the prompt no longer instructs the agent to use it for parsing source HTML during the audit pattern.

**What the architect is reviewing:**

1. **Field-by-field mappability of the locked structured return shape against AngleSharp's actual API.** Each of `title`, `meta_description`, `headings.h1`, `headings.h2`, `headings.h3_h6_count`, `word_count`, `images.{total,with_alt,missing_alt}`, `links.{internal,external}`, `truncated`, `truncated_during_parse` must map cleanly to an AngleSharp selector or property without contortions. The selector mappings in Task 3.6 are the design intent; the gate confirms they survive contact with the parser. If any field requires a workaround that distorts its meaning, surface it now and either adjust the mapping or amend the locked shape.

2. **`truncated_during_parse` is implementable cleanly given how `fetch_url`'s existing byte-stream truncation interacts with AngleSharp parsing.** Specifically: confirm that the Phase 1 implementation must parse `buffer[0..maxBytes]` (the un-marked truncated bytes) rather than `bytesToWrite` (which has the truncation marker text appended) so AngleSharp does not see `[Response truncated at N bytes]` as HTML. Bob's read of [`FetchUrlTool.cs:121-129`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L121-L129) says yes, the marker is in the bytes. Winston confirms the implementation approach (parse the un-marked slice) is the right call, or proposes an alternative (e.g. strip the marker from `bytesToWrite` before parsing).

3. **The `extract: "raw" | "structured"` parameter design composes cleanly with the existing 9.7 offloading pattern.** Specifically the **cache behaviour for structured mode** — see Q1 below.

4. **Agent prompt simplification is bounded.** Confirm the list of sections in scanner.md / analyser.md / reporter.md that get **stripped** vs the sections that **stay**:
   - **Stays:** verbatim opening line; the five hard invariants; the failure-bucketing logic for non-HTML and HTTP errors.
   - **Stripped:** "defending against context bloat" framing; the read_file-cached-content-inspection prose carried over from Story 9.7 amendments; verbose post-fetch narration warnings made redundant by the new contract.
   - The resulting prompts must be **shorter** than today's, not longer. Confirm this framing is correct.

### Open questions for architect review (Q1–Q5)

**Q1 — Cache behaviour for `extract: "structured"`.** The morning's reshape SCP did not explicitly answer whether structured mode also writes to `.fetch-cache/` (mirroring Story 9.7's offloading pattern) or skips the cache entirely (because the structured fields are already small enough to live in conversation context).

- **(a)** Skip the cache for structured mode. The structured fields are returned directly to the agent without writing the raw HTML to disk. **Architect-leaning answer per Adam's brief.** Rationale: structured mode's per-call context cost is already small (< 2 KB handle), so the offloading rationale does not apply; skipping the write avoids needless disk I/O and a redundant `.fetch-cache/` entry. Avoids a degenerate case where the agent mixes raw and structured calls against the same URL and ends up with both a cache file and a structured handle.
- **(b)** Write the cache entry exactly as raw mode does, then parse from disk. Mirrors the existing pattern verbatim; provides a fallback for the agent if it ever wants to inspect the raw HTML after the structured call (it would not, in current usage).

**Bob's recommendation:** (a). Implement structured mode without writing to `.fetch-cache/`. The structured handle is the only return value; no disk write happens in structured mode. This also means Edge Case #14 (`InstanceFolderPath` null) becomes acceptable for structured mode — there is no cache write to fail.

**Q2 — Truncation marker interaction with AngleSharp.** Confirmed by reading [`FetchUrlTool.cs:121-129`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs#L121-L129): the truncation marker is appended as bytes to `bytesToWrite` before the cache write. For structured mode, AngleSharp must not see the marker.

- **(a)** Parse the un-marked slice `buffer[0..maxBytes]` directly. **Bob's recommendation.** Simple, direct, no marker to strip.
- **(b)** Strip the marker from `bytesToWrite` before parsing. Adds a brittle string-search step.

**Bob's recommendation:** (a).

**Q3 — workflow.yaml verification.** Verified 2026-04-08 by reading [`workflow.yaml`](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml) — the scanner step's `tools` declaration is `[fetch_url, read_file, write_file]`. **No workflow.yaml change is needed for 9.1b.** Architect confirms or flags any gap.

**Q4 — Phase 1 commit hygiene.** Single commit (AngleSharp + tool + prompt rewrites all together) or two commits (engine first, prompts second)?

- **(a)** Single commit. **Bob's recommendation.** The prompt rewrites are tightly coupled to the new tool contract — they cannot be tested independently because the prompts now reference `extract: "structured"` which does not exist without the engine change. Splitting introduces a bisect-hostile half-state on `main`.
- **(b)** Two commits. Smaller individual diffs; easier to review one at a time.

**Bob's recommendation:** (a). Single commit.

**Q5 — Story 9.4 inheritance and non-HTML content type behaviour.** Story 9.4 (Accessibility Quick-Scan) is blocked on 9.1b and is meant to inherit the structured extraction pattern. Does 9.1b's spec need to explicitly leave room for 9.4's accessibility-specific structured fields (e.g. ARIA landmarks, form labels), or does 9.4 add its own `extract: "accessibility"` mode later?

Related: how does `extract: "structured"` against a non-HTML content type (`application/pdf`, `image/png`, `application/json`, etc.) behave? AC #9 / Edge Case #8.

- **(a)** 9.1b ships only `extract: "structured"` (the audit pattern). Story 9.4 adds a sibling mode (e.g. `extract: "accessibility"`) later when it lands. For non-HTML content types under `extract: "structured"`, raise `ToolExecutionException("Cannot extract structured fields from content type '<type>'. Use extract: 'raw' instead.")`. **Bob's architect-leaning recommendation.** Keeps 9.1b's surface area minimal; 9.4 owns its own extraction needs.
- **(b)** 9.1b ships a generic structured shape that Story 9.4 can extend. For non-HTML, fall back to `extract: "raw"` automatically with a marker field on the handle.
- **(c)** For non-HTML, return a structured-shaped handle with all parser fields null. Less-good — silently hides the failure mode.

**Bob's recommendation:** (a). 9.1b ships only the audit `structured` mode; non-HTML under `structured` raises a clear error pointing the agent at `raw`. Story 9.4 owns its own mode.

### Process

1. Bob delivers this spec to Adam.
2. Adam hands the spec to Winston for the structured-shape-against-AngleSharp eyeball plus Q1–Q5 resolution.
3. Winston either approves (tick the checkbox above, record the answers to Q1–Q5 inline below, add any notes) or returns the spec to Bob with required edits.
4. Once the checkbox is ticked, the spec is handed to Amelia and Phase 1 implementation begins.

**Architect answers (filled in by Winston during review):**

- **Q1: (a) Skip the cache for `extract: "structured"`.** Structured mode never writes to `.fetch-cache/`. Edge Case #14 reframed as "InstanceFolderPath is empty or not a valid directory" (the type is non-nullable on `ToolExecutionContext`); structured mode makes that case acceptable because no write happens. Raw + structured calls against the same URL are NOT deduplicated — both fully execute, no shared state. _(See Dev Notes.)_
- **Q2: (a) Parse `buffer[0..maxBytes]` directly.** Bob's source-reading verified. Implementation hint: compute the un-marked slice first, branch on `extract` after.
- **Q3: Verified.** No workflow.yaml change needed (Bob's pre-review check 2026-04-08).
- **Q4: (a) Single commit.** Phase 1 ships as one logical commit on `main`. Amelia may split within a draft PR for review hygiene; the merged result is one commit.
- **Q5: (a) 9.1b ships only `extract: "structured"` for the audit pattern.** Non-HTML content types under structured mode raise `ToolExecutionException` pointing the agent at `extract: "raw"`. Story 9.4 owns its own extraction needs and may add a sibling mode later. The content-type check happens after the existing content sniffing and before parser invocation.

## Dev Agent Record

### Phase 1 — Build (Amelia, 2026-04-09)

**AngleSharp version pinned:** `1.4.0` (current latest stable on NuGet.org at implementation time, MIT-licensed). Restored cleanly with no transitive dependency conflicts against Umbraco 17.2.2 / Microsoft.Extensions.AI / the existing dependency tree.

**Architect-locked answers applied verbatim:**
- **Q1 (a) — structured mode skips the cache.** The `InstanceFolderPath` precondition check is now scoped to `extract: "raw"` only. Structured mode never writes to `.fetch-cache/` and never invokes `PathSandbox`.
- **Q2 (a) — parse the un-marked slice.** `unmarkedLength = truncated ? maxBytes : totalRead` is computed once after the byte-stream read; the structured branch parses `buffer[0..unmarkedLength]` so AngleSharp never sees `[Response truncated at N bytes]` as HTML. The raw branch derives `bytesToWrite` from the same un-marked slice + marker exactly as before.
- **Q4 (a) — single commit.** All Phase 1 changes (engine + tests + prompts) are a single logical change, ready for one commit on `main`.
- **Q5 (a) — non-HTML structured throws.** `IsHtmlContentType` accepts `text/html` and `application/xhtml+xml`. Anything else under `extract: "structured"` raises `ToolExecutionException("Cannot extract structured fields from content type '<type>'. Use extract: 'raw' instead.")`. Check happens after the empty-body short-circuit and HTTP-error short-circuit, before AngleSharp is invoked.

**Implementation notes:**
- `extract` parameter parsing handles missing/null (defaults to raw), string `"raw"` / `"structured"`, JsonElement string, and rejects everything else with `ToolExecutionException("Invalid extract value: '<value>'. Must be 'raw' or 'structured'.")`.
- Structured handle is built from a single AngleSharp parse pass via `new HtmlParser().ParseDocument(MemoryStream)` over the un-marked byte slice. No double parsing, no caching of parser instances.
- Title is normalised: AngleSharp returns empty-string for both missing and empty `<title>`; the handle stores `null` in both cases (architect-locked).
- Image alt accounting uses attribute existence (`GetAttribute("alt") != null`), not non-emptiness — so `<img alt="">` is correctly counted as "with alt" (decorative image marker, architect-locked).
- Link classification preserves the AC #4 invariant `internal + external == document.QuerySelectorAll("a[href]").Length` by classifying non-http(s) hrefs (mailto:/tel:/javascript:/fragment-only/malformed) as external rather than skipping them. Documented inline.
- AngleSharp parse failures wrap into `ToolExecutionException("Failed to parse response as HTML: <inner>")` — never silently fall back to raw.
- The existing `ToolContextMissingException` for missing Step/Workflow context is preserved (raw and structured both gate on it).

**Test coverage added:** 14 new tests in `FetchUrlToolTests.cs`:
1. `Extract_Raw_DefaultBehaviour_PreservesStory97Handle` — AC #3 regression (omitted parameter).
2. `Extract_Raw_ExplicitParameter_PreservesStory97Handle` — AC #3 regression (explicit `"raw"`).
3. `Extract_Structured_100kb_ProducesLockedShape` — AC #4 (locked shape, every field present, accounting invariant, no cache write).
4. `Extract_Structured_207kb_PopulatesMetaAndWordCount` — AC #5.
5. `Extract_Structured_1mb_ParsesAtProductionScale` — AC #6.
6. `Extract_Structured_DeterministicAcrossTwoParses` — AC #4 byte-identical determinism (same bytes in → same JSON out).
7. `Extract_Structured_TruncatedDuringParse_FlagSetCorrectly` — AC #7 (`truncated == true && truncated_during_parse == true`; parser did NOT see the marker text).
8. `Extract_Structured_NotInvokedOn_HttpError` — AC #8 (HTTP 404 → existing string contract; no parser invocation).
9. `Extract_Structured_NotInvokedOn_EmptyBody` — AC #8 (HTTP 204 → existing empty-body handle).
10. `Extract_Structured_NonHtmlContentType_ThrowsToolExecutionException` — AC #9 (verbatim error message).
11. `Extract_InvalidValue_ThrowsToolExecutionException` — Edge Case #4.
12. `Extract_NonStringType_ThrowsToolExecutionException` — Edge Case #5.
13. `Extract_Structured_HandcraftedFixture_ImagesAccountingAndLinkClassification` — AC #4 invariants on a deterministic hand-crafted fixture (covers `<img alt="">` decorative-image rule, internal/external classification including mailto:).
14. `Extract_Structured_EmptyTitle_NormalisedToNull` — architect-locked title normalisation rule.

**Test result:** `dotnet test AgentRun.Umbraco.slnx` → **435 passed, 0 failed** (was 421 + 14 new = 435, exactly as projected in the DoD).

**Prompt rewrites:**
- `scanner.md` — Switched all audit fetches to `fetch_url(url, extract: "structured")`. Documented the structured handle shape inline. Stripped the `read_file → write_file` pipeline (Story 9.7 amendments) and the verbose contract-defence framing — the structured handle replaces the read step entirely. Failure-bucketing logic preserved and updated to recognise the new "Cannot extract structured fields from content type '<type>'" error as the non-HTML signal. Verbatim opening line, zero-URL re-prompt, and the five hard invariants preserved verbatim (Invariant #4 simplified from "post-fetch → read_file → write_file" to "post-fetch → write_file" because read_file is no longer needed in the audit pattern). Net result: prompt is shorter than before, contract-eliminated failure modes are gone.
- `analyser.md` — Added explicit "only flag issues you can directly cite from the structured fields" instruction. Added null/empty-field handling guidance.
- `reporter.md` — Added a `read_file` of `scan-results.md` so findings cite the structured fields directly. Same "only cite, don't invent" rule applied.

**Out of scope (deliberately not touched):**
- Phase 2 polish loop (Tasks 9–10) is owned by Adam — it requires real test inputs and the trustworthiness gate is subjective.
- Phase 1 manual E2E gate (Task 8) is owned by Adam against the 5-URL batch from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`.
- `read_file` stays declared in the scanner step's `tools` list as an affordance for inspecting prior step artifacts; the prompt no longer instructs the agent to use it for parsing source HTML.
- Story 9.2's JSON Schema entry for `extract` is a forward dependency, deliberately not added.

### File List

**Modified (engine source):**
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` — added `extract` parameter, structured-extraction code path, parser helpers, structured handle records. **Fix pass:** D2 manual redirect loop with per-hop SSRF re-validation; P1 dropped `truncated_during_parse` from `StructuredFetchHandle`; P2 case-insensitive meta selector; P3 script/style stripping in word count; P4 `<base href>` resolution; P5 empty-body short-circuit returns structured shape in structured mode.
- `AgentRun.Umbraco/AgentRun.Umbraco.csproj` — added `<PackageReference Include="AngleSharp" Version="1.4.0" />`.
- `AgentRun.Umbraco/Composers/AgentRunComposer.cs` — **fix pass D2:** `FetchUrl` named HttpClient registered with `ConfigurePrimaryHttpMessageHandler` setting `AllowAutoRedirect = false`.

**Modified (tests):**
- `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` — added 14 new tests for the structured-extraction code path; preserved every existing raw-mode test unchanged. **Fix pass:** P1 dropped `truncated_during_parse` from the test record + assertions; P5 reworked the empty-body test to assert on the structured shape; P7 added `meta_description` assertion to `Extract_Structured_207kb_PopulatesMetaAndWordCount`; P8 added symmetric byte-count assertion to `Extract_Raw_ExplicitParameter_PreservesStory97Handle`; D2 added 4 new redirect tests (`Redirect_ToPrivateIp_RejectedBySsrf`, `Redirect_ChainExceedsCap_ThrowsTooManyRedirects`, `Redirect_ToPublicUrl_FollowsAndReturnsBody`, `Redirect_RelativeLocation_ResolvesAgainstCurrentRequestUri`).

**Modified (agent prompts):**
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` — full rewrite for `extract: "structured"`; verbatim invariants preserved; contract-defence verbosity stripped. **Fix pass D1:** restored Invariant #4 verbatim ("post-fetch → read_file → write_file"), restored Invariant #3's trailing clause ("they cause post-batch stalls and break the workflow"), restored zero-URL re-prompt trailing clause ("including after a previous re-prompt"), restored the verbatim "Reminder — Post-fetch → read_file → write_file invariant" prose paragraph. **Fix pass P1:** dropped `truncated_during_parse` from the JSON schema block. **Fix pass P6:** trimmed non-invariant prose so net file length is 108 lines (−6 vs pre-Phase-1).
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/analyser.md` — added "only cite structured fields" rule and null-field handling guidance.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md` — same; added `read_file` of `scan-results.md` and the cite-don't-invent rule.

**No changes:** `ReadFileTool.cs`, `WriteFileTool.cs`, `ListFilesTool.cs`, `ToolLoop.cs`, `SsrfProtection.cs`, `PathSandbox.cs`, `ToolLimitResolver.cs`, `workflow.yaml`, `workflow-schema.json`.

### Change Log

| Date | Version | Description | Author |
|------|---------|-------------|--------|
| 2026-04-09 | 0.1 | Phase 1 build complete: AngleSharp 1.4.0 added; `extract: "raw" \| "structured"` parameter on `fetch_url`; server-side AngleSharp extraction in `FetchUrlTool`; 14 new parser-layer tests; scanner/analyser/reporter prompts rewritten to consume the structured handle. Full test suite green (435/435). Phase 1 manual E2E gate (Task 8) and Phase 2 polish loop (Tasks 9–10) still owned by Adam. | Amelia |
| 2026-04-09 | 0.4 | **Phase 2 polish loop pass #1 (architect-escalation-triggered Invariant #4 rewording).** Manual E2E gate run #4 (after the User-Agent fix landed) returned `200 OK` with full structured fields for all 5 URLs **including Wikipedia** — confirming the UA carve-out worked. The model still stalled with an empty turn after the 5th `fetch_url`. With Wikipedia 403 ruled out, Sonnet 4.6 non-determinism ruled out (4 stalls in a row, deterministic), and the failure case ruled out (this run had no failures), the only remaining hypothesis was the verbatim Invariant #4 wording confusing the model in structured mode — which is exactly the architect-escalation trigger Adam defined in the D1 ruling. Adam authorised Phase 2 polish to drive the rewording. **Pass #1 change:** Invariant #4 reworded from "Post-fetch → read_file → write_file" to "Post-fetch → write_file" (the `read_file` mention removed entirely; the structured handle is now described as the source of truth in the invariant body itself). The matching reminder paragraph in the Writing Results section was trimmed to mirror. The other four invariants are untouched — they remain verbatim. Phase 2 polish soft-cap budget consumed: 1 of 5. Full test suite green (441/441). Phase 1 manual E2E gate (Task 8) re-run still owned by Adam. | Amelia |
| 2026-04-09 | 0.3 | **Phase 1 manual E2E carve-out: default User-Agent on `fetch_url`.** Manual E2E gate (Task 8) against the 5-URL batch surfaced a deterministic stall after the 5th URL (Wikipedia) returned `HTTP 403: Forbidden`. Conversation log analysis exonerated the verbatim-restored Invariant #4 (the agent followed it cleanly through all 4 successful fetches; the failure was specifically on the post-failure transition). Two distinct problems identified: (1) Wikipedia 403 itself, caused by `FetchUrlTool` not setting a `User-Agent` — many WAFs / CDNs / sites with bot protection 403 bare-UA clients on principle; (2) the model not transitioning to `write_file` when the FINAL fetch in a batch returns an error string. This carve-out addresses ONLY problem #1 — added a generic engine default `User-Agent: AgentRun/1.0` set on every outgoing `HttpRequestMessage` (initial fetch and every redirect hop). Generic, not workflow-specific; if a future workflow needs a custom UA that becomes a Story 9.6-style tunable. 2 new tests: `UserAgent_DefaultHeaderSetOnOutgoingRequest` and `UserAgent_DefaultHeaderSetOnEveryRedirectHop`. New Failure & Edge Cases row #22. Problem #2 (terminal-failure transition stall) is logged as a Phase 2 polish-loop watch-item and is NOT touched in this commit — it requires real test inputs and architect-approved prompt iteration. Full test suite green (441/441). Phase 1 manual E2E gate (Task 8) re-run still owned by Adam. | Amelia |
| 2026-04-09 | 0.2 | **Phase 1 code-review fix pass.** Headline change: closed a pre-existing SSRF redirect bypass (D2 / Locked Decision #11) — `FetchUrl` HttpClient is now configured with `AllowAutoRedirect = false` and `FetchUrlTool.ExecuteAsync` follows redirects manually with per-hop `SsrfProtection.ValidateUrlAsync` re-validation, capped at 5 hops, headers/cookies not forwarded across hops. Spec amendment: new Failure & Edge Cases row #21 + new Locked Decision #11 in Phase 1 implementation notes. Subordinate fixes: D1 — restored Invariant #4 verbatim and Invariant #3's trailing clause in `scanner.md`; D3 — strict host equality dev note (parser-layer raw facts; SEO judgements are the analyser's job, with a Phase 2 watch-item recorded). P1 dropped the `truncated_during_parse` dead-alias field from `StructuredFetchHandle` and the scanner.md JSON schema. P2 made the meta-description selector case-insensitive (`meta[name=description i]`). P3 strips `<script>` and `<style>` descendants of `<body>` before computing `word_count` so inline JSON-LD / minified analytics no longer inflate the count. P4 respects `<base href>` when resolving relative anchor URLs (host comparison still uses the source URL's host per D3). P5 the empty-body short-circuit now returns the structured shape in structured mode instead of the raw shape (schema drift fix). P6 net `scanner.md` line count is now 108 vs pre-Phase-1 114 (−6 lines, AC #10 satisfied). P7/P8 test gaps closed (`meta_description` now asserted; raw-mode tests now have symmetric byte-count assertions). 4 new D2 redirect tests added: `Redirect_ToPrivateIp_RejectedBySsrf`, `Redirect_ChainExceedsCap_ThrowsTooManyRedirects`, `Redirect_ToPublicUrl_FollowsAndReturnsBody`, `Redirect_RelativeLocation_ResolvesAgainstCurrentRequestUri`. Full test suite green (439/439). Phase 1 manual E2E gate (Task 8) still owned by Adam. | Amelia |

