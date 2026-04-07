# Story 9.1a: Content Quality Audit — Working Skeleton

Status: done (full validation blocked by Story 9.0 stall fix — skeleton itself is committed and runs end-to-end modulo the stall bug)

**Depends on:** none
**Blocks:** 9.1b (polish), 9.1c (URL UX) — both build on this skeleton

_Renamed from "Story 9.1" on 2026-04-07 after the polish split. The workflow files (workflow.yaml, agents/scanner.md, agents/analyser.md, agents/reporter.md) exist on disk and the workflow runs end-to-end except for the stall described in instance 642b6c583e3540cda11a8d88938f37e1, which is Story 9.0's territory. The body of this spec captures the work that has already been done; do not re-do it._

## Story

As a developer,
I want a pre-built example workflow that runs out-of-the-box after package installation,
so that I can see multi-agent orchestration in action without writing any workflow files myself.

## Context

**UX Mode: Interactive (primary). The user drives step progression.**

This story replaces the existing dev-testing workflow in `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/` with a production-quality example that ships as the package's showcase. The existing workflow was built for engine testing (autonomous mode, different step names, hardcoded httpbin.org calls) and is not suitable as the first-run experience.

The example workflow lives in the **consuming site's** `App_Data/AgentRun.Umbraco/workflows/` folder — it is NOT embedded in the RCL. For the TestSite (our dev host), the files go in `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/`. Story 9.3 will handle how the example ships in the NuGet package (likely as content files or a first-run copy mechanism).

**Important:** This story is content-only — no C# or TypeScript code changes. The engine, tools, and UI already support everything this workflow needs. The work is authoring the workflow.yaml and three agent prompt markdown files.

**Key design decision:** The scanner agent asks the user for URLs to audit via the interactive chat, then uses `fetch_url` to retrieve real page content. This makes the workflow genuinely useful — it audits the user's actual site, not canned sample data. No sample content files are needed.

## Acceptance Criteria

1. **Given** the package is installed and Umbraco.AI has at least one provider configured with a profile, **When** the developer opens the Agent Workflows dashboard, **Then** a "Content Quality Audit" workflow appears in the workflow list with description "Scans, analyses, and reports on content quality", 3 steps, interactive mode.

2. **Given** the developer starts the Content Quality Audit workflow, **When** Step 1 (Content Scanner) executes, **Then** the scanner agent greets the user and asks them to provide one or more URLs to audit, **And** waits for the user to type URLs into the chat (interactive mode — ToolLoop waits for user input), **And** uses `fetch_url` to retrieve the HTML content of each provided URL, **And** parses the HTML to extract: page title, heading structure, meta description, word count, image alt text presence, internal/external link count, **And** uses `write_file` to produce `artifacts/scan-results.md` containing a structured catalogue of each page, **And** the output is readable, structured markdown.

3. **Given** Step 1 has completed and the developer advances to Step 2, **When** Step 2 (Quality Analyser) executes, **Then** the analyser agent reads `artifacts/scan-results.md` via `read_file`, **And** scores each page on SEO completeness, readability, accessibility, and content freshness, **And** uses `write_file` to produce `artifacts/quality-scores.md` with per-page scores and brief justifications.

4. **Given** Step 2 has completed and the developer advances to Step 3, **When** Step 3 (Report Generator) executes, **Then** the reporter agent reads `artifacts/quality-scores.md` via `read_file`, **And** produces `artifacts/audit-report.md` containing an executive summary, page-by-page findings, and a prioritised action plan with recommended fixes, **And** the report is structured, actionable, and understandable by a non-technical content manager.

5. **And** the `workflow.yaml` uses `mode: interactive` and a single `default_profile` (no per-step overrides) so it works with any single configured profile (FR65).

6. **And** all three agent prompts produce high-quality, deterministic output — the prompts guide the LLM to produce consistent structure regardless of provider (Anthropic or OpenAI).

7. **And** all existing tests pass (`dotnet test AgentRun.Umbraco.slnx` and `npm test` in `Client/`).

## What NOT to Build

- No C# backend changes — the engine already supports everything (workflow discovery, tool execution, artifact handoff, completion checking, interactive mode, `fetch_url`)
- No TypeScript/frontend changes — the UI already renders workflows, instances, chat, artifacts
- No new tools — `fetch_url`, `read_file`, `write_file` are sufficient
- No sample content files — the scanner fetches real content from user-provided URLs
- No `list_files` usage — the scanner doesn't need to discover local files; it fetches URLs
- No automated tests for workflow content — agent prompts are tested via manual E2E
- No NuGet packaging changes — Story 9.3 handles how files ship in the package
- No changes to `WorkflowRegistryInitializer`, `WorkflowRegistry`, or discovery logic
- No sidecar files — keep it simple for the example

## Failure & Edge Cases

- **LLM produces inconsistent output format:** Agent prompts must be highly prescriptive about output structure (exact markdown headings, section order). Include explicit format instructions and an output template skeleton in each prompt. The LLM should fill in the template, not invent its own structure.
- **User provides no URLs:** The scanner prompt must handle this — if the user's message contains no recognisable URLs, the agent should ask again politely. Do not proceed with zero URLs.
- **User provides invalid or unreachable URLs:** `fetch_url` returns error strings for connection failures (`"Connection failed: ..."`) and non-200 responses (`"HTTP 404: Not Found"`). The scanner prompt must instruct the agent to note failed URLs in the scan results rather than crashing or retrying indefinitely. Include the URL and error in the output so the user knows what happened.
- **User provides localhost/internal URLs:** `SsrfProtection` blocks RFC1918, loopback, and link-local addresses. `fetch_url` throws `ToolExecutionException` with a clear message. The agent receives this as a tool error result and should report it to the user — "Could not fetch [URL] — internal/private addresses are blocked for security."
- **`fetch_url` returns raw HTML (up to 100KB):** The scanner agent must parse HTML to extract content. LLMs handle HTML parsing well, but the prompt should give explicit guidance on what to extract (title tag, h1-h6, meta description, img alt attributes, link counts, approximate word count of body text).
- **Very large pages truncated at 100KB:** `FetchUrlTool` appends `[Response truncated at 100KB]` to oversized responses. The scanner should note truncation in its output but still process what was returned.
- **Non-HTML content (PDFs, images, JSON):** The scanner prompt should instruct the agent to note the content type and skip analysis — "URL returned non-HTML content, skipping."
- **Empty or malformed scan-results.md passed to Step 2:** The analyser prompt should handle gracefully — note that scan data is incomplete and produce partial scores rather than crashing or looping.
- **Agent tries to write outside artifacts/ directory:** Path sandboxing already prevents this at the engine level. No prompt-level mitigation needed.
- **Profile not configured:** The engine's `ProfileResolver` throws `ProfileNotFoundException` with a clear message. No workflow-level mitigation needed.
- **Agent produces extremely long output:** Prompts should include explicit length guidance (e.g., "Keep the report under 200 lines").

## Tasks / Subtasks

- [x] **Task 1: Delete existing dev-testing workflow** (AC: all)
  - [x] 1.1 Delete all files under `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/`
  - [x] 1.2 Delete all files under `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/` (stale test instance data)

- [x] **Task 2: Create workflow.yaml** (AC: #1, #5)
  - [x] 2.1 Create `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml`
  - [x] 2.2 Set `name: Content Quality Audit`
  - [x] 2.3 Set `description: Scans, analyses, and reports on content quality`
  - [x] 2.4 Set `mode: interactive`
  - [x] 2.5 Set `default_profile:` to a sensible value — use `default` as the profile alias (the most common single-profile setup). Document in a YAML comment that users should change this to match their configured Umbraco.AI profile alias.
  - [x] 2.6 Define 3 steps:
    - Step 1: `id: scanner`, `name: Content Scanner`, `agent: agents/scanner.md`, `tools: [fetch_url, write_file]`, `writes_to: [artifacts/scan-results.md]`, `completion_check: { files_exist: [artifacts/scan-results.md] }`
    - Step 2: `id: analyser`, `name: Quality Analyser`, `agent: agents/analyser.md`, `tools: [read_file, write_file]`, `reads_from: [artifacts/scan-results.md]`, `writes_to: [artifacts/quality-scores.md]`, `completion_check: { files_exist: [artifacts/quality-scores.md] }`
    - Step 3: `id: reporter`, `name: Report Generator`, `agent: agents/reporter.md`, `tools: [read_file, write_file]`, `reads_from: [artifacts/quality-scores.md]`, `writes_to: [artifacts/audit-report.md]`, `completion_check: { files_exist: [artifacts/audit-report.md] }`

- [x] **Task 3: Author scanner agent prompt (interactive URL-based)** (AC: #2, #6)
  - [x] 3.1 Create `agents/scanner.md`
  - [x] 3.2 Prompt must instruct the agent to:
    1. Greet the user and ask them to provide one or more page URLs to audit (e.g., "Paste the URLs of the pages you'd like me to audit, one per line.")
    2. Wait for the user to respond with URLs via the chat
    3. Extract URLs from the user's message (handle various formats: one per line, comma-separated, space-separated, or embedded in prose)
    4. Use `fetch_url` for each URL to retrieve the page HTML
    5. Parse each HTML response to extract: page title (`<title>` tag), heading structure (h1-h6 tags), meta description (`<meta name="description">`), approximate word count (body text), image count and alt text status, internal vs external link count
    6. Handle fetch errors gracefully — note the URL and error in the scan results, continue with remaining URLs
    7. Use `write_file` to write structured results to `artifacts/scan-results.md`
  - [x] 3.3 Include an explicit output template in the prompt showing the expected markdown structure for scan-results.md (per-page section with structured fields)
  - [x] 3.4 Include instruction: "If the user's message contains no recognisable URLs, ask them to provide URLs before proceeding. Do not proceed with zero URLs."
  - [x] 3.5 Include instruction: "If fetch_url returns an error for a URL, record the URL and error message in scan-results.md under a 'Failed URLs' section. Continue processing remaining URLs."
  - [x] 3.6 Include instruction: "If a page has no images, note 'No images found' — do not fabricate image references."
  - [x] 3.7 Include instruction: "If a response appears to be non-HTML (JSON, PDF, plain text), note the content type and skip detailed analysis for that URL."
  - [x] 3.8 Keep the prompt concise — under 100 lines including the output template

- [x] **Task 4: Author analyser agent prompt** (AC: #3, #6)
  - [x] 4.1 Create `agents/analyser.md`
  - [x] 4.2 Prompt must instruct the agent to:
    1. Use `read_file` to read `artifacts/scan-results.md`
    2. Score each page on 4 dimensions (1-10 scale): SEO completeness, readability, accessibility, content freshness
    3. Provide a brief one-line justification for each score
    4. Calculate an overall quality score per page (average of 4 dimensions)
    5. Use `write_file` to write results to `artifacts/quality-scores.md`
  - [x] 4.3 Include an explicit output template showing the expected markdown structure (per-page score card)
  - [x] 4.4 Include scoring rubric guidance so scores are consistent across providers:
    - SEO: meta description present, title tag quality, heading hierarchy, keyword relevance
    - Readability: sentence length, paragraph structure, jargon level, scannability
    - Accessibility: heading structure, link text quality, image alt text, language clarity
    - Content freshness: dates mentioned, timeliness of information, staleness indicators
  - [x] 4.5 Include instruction: "If scan-results.md is incomplete or malformed, score only the pages you can extract data for and note any issues"

- [x] **Task 5: Author reporter agent prompt** (AC: #4, #6)
  - [x] 5.1 Create `agents/reporter.md`
  - [x] 5.2 Prompt must instruct the agent to:
    1. Use `read_file` to read `artifacts/quality-scores.md`
    2. Produce a final report with three sections:
       - **Executive Summary:** 3-5 sentences covering overall site quality, top concern, and top strength
       - **Page-by-Page Findings:** For each page, show the scores and 2-3 specific, actionable findings
       - **Prioritised Action Plan:** Numbered list of recommended fixes, ordered by impact (highest first), each with a clear "what to do" instruction
    3. Use `write_file` to write the report to `artifacts/audit-report.md`
  - [x] 5.3 Include an explicit output template showing the expected markdown structure
  - [x] 5.4 Include instruction: "Write for a non-technical content manager. Avoid jargon. Be specific about what to fix and why."
  - [x] 5.5 Include instruction: "Keep the report under 150 lines. Be concise but actionable."

- [x] **Task 6: Verify existing tests still pass** (AC: #7)
  - [x] 6.1 Run `dotnet test AgentRun.Umbraco.slnx` — all 328+ backend tests must pass
  - [x] 6.2 Run `npm test` in `AgentRun.Umbraco/Client/` — all 162+ frontend tests must pass
  - [x] 6.3 No test changes expected — this story is content-only

- [ ] **Task 7: Manual E2E validation** (AC: all)
  - [ ] 7.1 Start the TestSite (`dotnet run` from TestSite project)
  - [ ] 7.2 Open the Agent Workflows dashboard — verify "Content Quality Audit" appears with correct description, 3 steps, interactive mode
  - [ ] 7.3 Create a new instance — verify it starts and shows the chat panel
  - [ ] 7.4 Run Step 1 (Content Scanner) — verify the agent asks for URLs in the chat
  - [ ] 7.5 Provide 2-3 real public URLs (e.g., the Umbraco website or any public site) — verify the agent fetches each via `fetch_url`, parses HTML, and writes `artifacts/scan-results.md`. Review the output for quality and completeness.
  - [ ] 7.6 Advance to Step 2 (Quality Analyser) — verify it reads scan-results.md and writes `artifacts/quality-scores.md` with per-page scores
  - [ ] 7.7 Advance to Step 3 (Report Generator) — verify it reads quality-scores.md and writes `artifacts/audit-report.md` with executive summary, findings, and action plan
  - [ ] 7.8 After Step 3 completes, verify the artifact popover auto-opens showing audit-report.md
  - [ ] 7.9 Browse all 3 artifacts via the artifact list — verify each renders correctly in the markdown renderer
  - [ ] 7.10 Verify the report is understandable by a non-technical reader — clear language, specific recommendations, no jargon
  - [ ] 7.11 Test error case: provide an invalid URL (e.g., `https://thisdomaindoesnotexist12345.com`) — verify the agent reports the error and continues with valid URLs

## Dev Notes

### Key Architecture Context

- **Workflow discovery:** `WorkflowRegistryInitializer` scans `App_Data/AgentRun.Umbraco/workflows/` at startup. Each subfolder must contain a `workflow.yaml`. Agent files are resolved relative to the workflow folder.
- **Tool resolution:** The engine resolves tool names from `workflow.yaml` against DI-registered `IWorkflowTool` implementations. Available tools: `read_file`, `write_file`, `list_files`, `fetch_url`.
- **Artifact handoff:** `PromptAssembler` injects runtime context including `reads_from` artifacts and their existence status. The agent prompt automatically receives: available tools list, input artifacts, prior artifacts from completed steps, expected output files.
- **Completion checking:** `CompletionChecker` verifies `files_exist` entries are present in the instance folder. Artifacts must be written to the exact paths declared in `writes_to`.
- **Path sandboxing:** All tool file operations (`read_file`, `write_file`, `list_files`) are sandboxed to the instance folder. Agent prompts should use relative paths (e.g., `artifacts/scan-results.md`). `fetch_url` is NOT sandboxed to the instance folder — it fetches external URLs with SSRF protection.
- **Interactive mode and ToolLoop wait:** The user clicks "Start" to begin a step. The agent runs, and when it produces text without tool calls, the ToolLoop emits `input.wait` and blocks on `WaitToReadAsync` for up to 5 minutes. The user types a response in the chat, which drains into the message queue and continues the loop. This is how the scanner agent asks for URLs and receives the user's response.
- **`fetch_url` returns raw HTML** (up to 100KB for HTML content type, 200KB for JSON/other). Non-200 responses return `"HTTP {code}: {reason}"`. Connection failures throw `ToolExecutionException` which the engine returns as a tool error result string to the LLM.

### Artifact Path Convention

The existing dev workflow wrote artifacts to `artifacts/` subdirectory within the instance folder. **Use `artifacts/` prefix** for all output files — this keeps agent-produced files separate from `instance.yaml` and conversation JSONL files.

### URL-Based Content Scanning

The scanner agent uses `fetch_url` to retrieve real page content from user-provided URLs. This approach:
- Makes the workflow genuinely useful (audits real content, not canned samples)
- Leverages interactive mode naturally (agent asks, user answers, agent acts)
- Requires no engine changes — `fetch_url` and interactive ToolLoop already work
- Exercises more of the engine's capabilities as a showcase (tool calls, user messaging, artifact generation)

**Future enhancement (not this story):** A dedicated `query_content` tool that reads from the Umbraco content tree directly would be even more powerful. This could be an Epic 11+ feature or a Pro tier differentiator.

### Profile Configuration

The `default_profile` value in workflow.yaml must match a profile alias configured in the consuming site's Umbraco.AI setup. Use `default` as the alias — this is the most common convention. Add a YAML comment explaining this.

### Previous Story Intelligence

Story 8.2 (previous story, different epic) established:
- Artifact viewer and popover work correctly with `artifacts/` prefixed paths
- Auto-open on `run.finished` works for the final step's last artifact
- All markdown rendering goes through the sanitiser

Story R1 confirmed:
- All naming is now AgentRun.Umbraco
- Routes, config section, file paths all updated

### Existing Dev Workflow Differences

The existing workflow in the TestSite differs significantly from the PRD spec:

| Aspect | Existing (dev testing) | Target (production) |
|--------|----------------------|---------------------|
| Mode | `autonomous` | `interactive` |
| Steps | external-checker → content-gatherer → quality-analyser | scanner → analyser → reporter |
| Tools | `fetch_url` (httpbin.org), `read_file`, `list_files`, `write_file` | `fetch_url` (user-provided URLs), `read_file`, `write_file` |
| Artifacts | content-inventory.json, external-check.md, quality-report.md | scan-results.md, quality-scores.md, audit-report.md |
| Content | Hardcoded Contoso sample pages + httpbin URLs | Real user content via URL fetching |
| Quality | Minimal prompts (10-15 lines each) | Production-quality prompts with templates, rubrics, and error handling |

### Project Structure Notes

All files go under `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/`:
```
content-quality-audit/
  workflow.yaml
  agents/
    scanner.md
    analyser.md
    reporter.md
```

No `sample-content/` directory — the scanner fetches real content from user-provided URLs.

No changes to any files under `AgentRun.Umbraco/` (the package project) or `AgentRun.Umbraco.Tests/`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Story 9.1 acceptance criteria]
- [Source: _bmad-output/planning-artifacts/architecture.md — Workflow YAML schema, tool system, prompt assembly]
- [Source: _bmad-output/planning-artifacts/prd.md — FR65, FR66]
- [Source: AgentRun.Umbraco/Engine/PromptAssembler.cs — Runtime context injection]
- [Source: AgentRun.Umbraco/Composers/WorkflowRegistryInitializer.cs — Workflow discovery]
- [Source: AgentRun.Umbraco/Configuration/AgentRunOptions.cs — WorkflowPath default]
- [Source: AgentRun.Umbraco/Tools/ReadFileTool.cs — Sandboxed to instance folder]

## Dev Agent Record

### Agent Model Used
Claude Opus 4.6 (1M context)

### Completion Notes List
- Task 1: Deleted old dev-testing workflow files (external-checker.md, content-gatherer.md, quality-analyser.md, sample-content/, workflow.yaml) and stale instance data directory
- Task 2: Created new workflow.yaml with interactive mode, default profile alias, and 3 steps (scanner → analyser → reporter) with proper tool assignments, artifact paths, and completion checks
- Task 3: Authored scanner.md — interactive prompt that greets user, requests URLs, fetches HTML via fetch_url, parses content (title, headings, meta desc, word count, images, links), handles errors gracefully, writes structured scan-results.md. Under 100 lines with output template.
- Task 4: Authored analyser.md — reads scan-results.md, scores each page on 4 dimensions (SEO, readability, accessibility, freshness) with detailed rubric for cross-provider consistency, writes quality-scores.md with per-page score cards and summary
- Task 5: Authored reporter.md — reads quality-scores.md, produces actionable audit-report.md with executive summary, page-by-page findings, and prioritised action plan. Written for non-technical content managers, under 150 lines.
- Task 6: All tests pass — 328 backend (dotnet test), 162 frontend (npm test). No regressions.
- Task 7: Manual E2E validation — requires user testing in browser

### Change Log
- 2026-04-02: Replaced dev-testing workflow with production-quality Content Quality Audit example workflow (interactive mode, URL-based scanning)

### File List
- DELETED: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/external-checker.md
- DELETED: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/content-gatherer.md
- DELETED: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/quality-analyser.md
- DELETED: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/sample-content/ (directory)
- DELETED: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/ (directory)
- NEW: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml
- NEW: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md
- NEW: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/analyser.md
- NEW: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/reporter.md
