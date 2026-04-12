# Story 9.13: Umbraco Content Audit Workflow — Audit the Content in This Instance

Status: ready-for-dev

**Depends on:** Story 9.12 (Umbraco content tools — `list_content`, `get_content`, `list_content_types`)
**Blocks:** 9.14 (Documentation Update), 9.5 (Private Beta Distribution)

> This is the headline workflow that proves AgentRun.Umbraco is an Umbraco package, not a generic AI wrapper. It uses the in-process content tools from Story 9.12 to scan and audit the actual content in the Umbraco instance where the package is installed. No external URLs, no fetch_url — everything is read from the published content cache in-process.

## Story

As an Umbraco editor or agency developer,
I want a workflow that audits the content in my Umbraco instance for quality, completeness, and structural issues,
So that I get actionable findings about my actual CMS content without leaving the backoffice.

## Acceptance Criteria

**AC1 — Workflow YAML is valid and registers on startup**
Given the `umbraco-content-audit` workflow folder exists in `Workflows/`,
When the application starts,
Then `WorkflowRegistry` discovers and registers the workflow with no validation errors,
And the workflow appears in the backoffice workflow list.

**AC2 — Scanner reads content model first**
Given the workflow is started,
When the scanner step begins,
Then the scanner calls `list_content_types` to understand the content model before scanning content nodes.

**AC3 — Scanner inventories all published content**
Given the Umbraco instance has published content,
When the scanner step runs,
Then the scanner calls `list_content` to get the full content inventory,
And calls `get_content` for each content node (or a prioritised subset if the tree is large),
And writes structured findings to `artifacts/content-inventory.md` and `artifacts/scan-results.md`.

**AC4 — Scanner analyses content against the content model**
Given the scanner has content type definitions and content node properties,
When the scanner writes `scan-results.md`,
Then findings include: empty required fields, unused optional fields, content types with no instances, content nodes with no template, content nodes with thin content (low property fill rate).

**AC5 — Analyser scores findings**
Given the scanner has written `scan-results.md`,
When the analyser step runs,
Then it reads `scan-results.md`, scores each content node on quality dimensions relevant to CMS content (completeness, structure, metadata), and writes scores to `artifacts/quality-scores.md`.

**AC6 — Reporter produces actionable audit report**
Given the analyser has written `quality-scores.md`,
When the reporter step runs,
Then it reads both `scan-results.md` and `quality-scores.md`,
And writes a prioritised audit report to `artifacts/audit-report.md` that a non-technical content manager can understand and act on.

**AC7 — Empty site handled gracefully**
Given the Umbraco instance has no published content,
When the user starts the workflow,
Then the scanner reports that no content was found and writes a scan-results.md noting this,
And the workflow completes with an appropriate message.

**AC8 — Workflow ships in NuGet package**
Given the workflow files are in `AgentRun.Umbraco/Workflows/umbraco-content-audit/`,
When the NuGet package is built,
Then the workflow files are included as contentFiles and land at `App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/` in the consumer project.

**AC9 — Existing workflows unaffected**
Given the CQA and Accessibility Quick-Scan workflows already exist,
When the new workflow is added,
Then both existing workflows continue to register and run without changes.

## Tasks / Subtasks

- [ ] Task 1: Create `workflow.yaml` for `umbraco-content-audit` (AC: #1, #8)
  - [ ] 1.1 Create `AgentRun.Umbraco/Workflows/umbraco-content-audit/workflow.yaml`
  - [ ] 1.2 Define 3-step pipeline: scanner → analyser → reporter
  - [ ] 1.3 Scanner tools: `list_content`, `list_content_types`, `get_content`, `write_file`
  - [ ] 1.4 Analyser tools: `read_file`, `write_file`
  - [ ] 1.5 Reporter tools: `read_file`, `write_file`
  - [ ] 1.6 Completion checks: `files_exist` for each step's output artifact
- [ ] Task 2: Create `agents/scanner.md` (AC: #2, #3, #4, #7)
  - [ ] 2.1 Opening line asks user to confirm they want to audit this site's content (or specify filters)
  - [ ] 2.2 Step 1: call `list_content_types` — understand the content model
  - [ ] 2.3 Step 2: call `list_content` — get full content inventory
  - [ ] 2.4 Step 3: call `get_content` for each node (prioritise: skip compositions/element types, focus on pages with templates)
  - [ ] 2.5 Step 4: write `artifacts/content-inventory.md` — structured content tree summary
  - [ ] 2.6 Step 5: write `artifacts/scan-results.md` — findings against the content model
  - [ ] 2.7 Handle empty site: write scan-results noting no content found
  - [ ] 2.8 Handle large sites: work within tool size limits, prioritise nodes with templates
  - [ ] 2.9 Interactive mode invariants: no text-only turns mid-task (same pattern as CQA scanner)
- [ ] Task 3: Create `agents/analyser.md` (AC: #5)
  - [ ] 3.1 Read `artifacts/scan-results.md` immediately (no greeting)
  - [ ] 3.2 Score each content node on: Completeness (property fill rate), Structure (heading hierarchy, content depth), Metadata (name, URL, template assignment)
  - [ ] 3.3 Write `artifacts/quality-scores.md` using structured output template
  - [ ] 3.4 Interactive mode invariants: tool call in every turn until write_file completes
- [ ] Task 4: Create `agents/reporter.md` (AC: #6)
  - [ ] 4.1 Read both `artifacts/scan-results.md` and `artifacts/quality-scores.md`
  - [ ] 4.2 Produce Executive Summary, Content-by-Content Findings, Prioritised Action Plan
  - [ ] 4.3 Write for non-technical content manager audience
  - [ ] 4.4 Write `artifacts/audit-report.md` using structured output template
  - [ ] 4.5 Interactive mode invariants
- [ ] Task 5: Copy workflow to TestSite (AC: #1, #9)
  - [ ] 5.1 Copy the workflow folder to `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/`
  - [ ] 5.2 Verify TestSite still discovers all 3 workflows (CQA, Accessibility, new Content Audit) plus any test workflows
- [ ] Task 6: Add NuGet contentFiles entry (AC: #8)
  - [ ] 6.1 Add `<None Include>` entry in `AgentRun.Umbraco.csproj` for the new workflow, following the exact pattern of the CQA and Accessibility entries
- [ ] Task 7: Manual E2E Validation (AC: #1–#9)
  - [ ] 7.1 Start TestSite — verify all 3 packaged workflows appear in the workflow list
  - [ ] 7.2 Start the Umbraco Content Audit workflow
  - [ ] 7.3 Confirm scanner calls `list_content_types`, `list_content`, then `get_content` for nodes
  - [ ] 7.4 Confirm scanner writes `content-inventory.md` and `scan-results.md`
  - [ ] 7.5 Advance to analyser — confirm it reads scan-results and writes `quality-scores.md`
  - [ ] 7.6 Advance to reporter — confirm it reads both artifacts and writes `audit-report.md`
  - [ ] 7.7 Review `audit-report.md` — confirm findings reference actual TestSite content (node names, content types, property values from the Clean Starter Kit)
  - [ ] 7.8 Verify CQA and Accessibility workflows still work (no regression)

## Dev Notes

### This story is workflow-only — zero C# code changes

The engine, tools, and infrastructure are all in place from previous stories. This story creates:
- 1 `workflow.yaml` file
- 3 agent prompt files (`.md`)
- 1 `.csproj` entry for NuGet packaging
- 1 copy of the workflow to the TestSite

No new tools. No engine changes. No test code changes. The test count should remain at 521/521 after this story — if `dotnet test AgentRun.Umbraco.slnx` fails, something unrelated broke.

### Workflow structure

```
umbraco-content-audit/
├── workflow.yaml
└── agents/
    ├── scanner.md
    ├── analyser.md
    └── reporter.md
```

**Mode:** interactive — the user can review scanner findings before analysis proceeds.

**Profile:** `default_profile: anthropic-sonnet-4-6` (same as CQA and Accessibility workflows — user must change this to match their configured Umbraco.AI profile alias).

**No `tool_defaults` needed** — the content tools don't use `fetch_url` so there's no need for the `max_response_bytes` / `timeout_seconds` overrides that CQA and Accessibility need. The content tools' default 256 KB limit from `EngineDefaults` is sufficient for the Clean Starter Kit (25 nodes, 32 document types).

### Tool availability (from Story 9.12)

| Tool | What it returns | Key fields |
|------|----------------|------------|
| `list_content` | All published content nodes | `id`, `name`, `contentType`, `url`, `level`, `createDate`, `updateDate`, `creatorName`, `childCount` |
| `list_content(contentType: "x")` | Filtered by document type alias | Same fields |
| `list_content(parentId: N)` | Direct children of node N | Same fields |
| `get_content(id: N)` | Full node with property values | Above + `templateAlias`, `properties` object (alias → extracted text) |
| `list_content_types` | All document type definitions | `alias`, `name`, `description`, `icon`, `properties` (with `alias`, `name`, `editorAlias`, `mandatory`), `compositions`, `allowedChildTypes` |
| `list_content_types(alias: "x")` | Single filtered type | Same fields |

**Property extraction** (per `get_content`): Rich Text → plain text (HTML stripped), Text String/Textarea → as-is, Content Picker → name + URL, Media Picker → name + URL + alt, Block List/Grid → simplified block content, TrueFalse → "true"/"false", fallback → JSON serialised.

### Scanner agent design — key differences from CQA

The CQA scanner fetches external URLs. This scanner reads internal content. The interaction model is fundamentally different:

1. **No URL input needed** — the scanner reads from the Umbraco instance directly. The opening message should ask the user to confirm they want to audit the site's content, or optionally specify a content type or subtree filter.
2. **Content model first** — call `list_content_types` before `list_content` so the scanner understands which fields are required vs optional, which types are compositions (element types), and which types have no instances.
3. **Sequential get_content** — call `get_content` one node per turn (same sequential invariant as CQA's fetch_url). For the Clean Starter Kit (25 nodes) this is fine. For larger sites, prioritise nodes that have a template (actual pages) and skip element type nodes.
4. **Two output artifacts** — `content-inventory.md` (structured summary of the content tree) and `scan-results.md` (findings per content node). The CQA scanner writes only `scan-results.md`.
5. **Findings are schema-aware** — the scanner compares actual property values against the content type definition (from `list_content_types`). Empty mandatory fields, unused optional fields, and content types with zero instances are all findings.

### Interactive mode invariants (copy from CQA — proven pattern)

These invariants are critical for preventing stalls in interactive mode. They are proven across CQA and Accessibility workflows:

1. **Post-confirmation invariant:** After the user confirms, the very next turn MUST contain a tool call.
2. **Between-calls invariant:** Progress markers only if accompanied by a tool call in the same turn.
3. **Sequential-call invariant:** One `get_content` call per assistant turn. Never batch.
4. **Post-scan → write_file invariant:** After the last `get_content`, the next turn MUST call `write_file`.
5. **Standalone-text invariant:** Only permitted text-only turns are: opening line, and summary after final `write_file`.

### Output templates

**`content-inventory.md`:**
```markdown
# Content Inventory

Instance: [site name from Home node or root] | Date: [today's date]

## Content Tree Summary

- Total published nodes: [count]
- Document types in use: [count] of [total defined]
- Unused document types: [list of aliases with zero instances]

## Content by Type

### [Document Type Name] ([alias]) — [count] nodes

| Node | URL | Level | Properties Filled | Last Updated |
|------|-----|-------|-------------------|--------------|
| [name] | [url] | [level] | [filled]/[total] | [updateDate] |
```

**`scan-results.md`:**
```markdown
# Content Audit Scan Results

Scanned: [number] nodes | Date: [today's date]

## Content Model Findings

- **Unused document types:** [list with aliases — types defined but with zero published instances]
- **Document types without templates:** [list — types that exist but none of their instances have a template assigned]

## Node: [node name]

- **URL:** [url]
- **Content Type:** [alias]
- **Template:** [templateAlias or "None assigned"]
- **Last Updated:** [updateDate]
- **Properties:**
  - [alias]: [value summary or "Empty" if blank] [REQUIRED — MISSING] if mandatory and empty
  - [alias]: [value summary or "Empty"]
- **Findings:**
  - [specific finding, e.g. "Required field 'metaDescription' is empty"]
  - [specific finding, e.g. "No template assigned — this page is not directly viewable"]

[Repeat for each node]

## Summary

- Nodes with all required fields filled: [count]
- Nodes with missing required fields: [count]
- Nodes with no template: [count]
- Content types with no instances: [count]
```

**`quality-scores.md`:**
```markdown
# Content Quality Scores

Analysed: [number] nodes | Date: [today's date]

## Node: [node name]

- **URL:** [url]
- **Content Type:** [alias]
- **Overall Score:** [average]/10

| Dimension | Score | Justification |
|-----------|-------|---------------|
| Completeness | [n]/10 | [e.g. "8 of 10 properties filled, 2 required fields empty"] |
| Structure | [n]/10 | [e.g. "Level 2 page, has template, logical tree position"] |
| Metadata | [n]/10 | [e.g. "Title and meta description present, SEO properties populated"] |

[Repeat for each node]

## Summary

- **Highest scoring:** [name] ([score]/10)
- **Lowest scoring:** [name] ([score]/10)
- **Most common issue:** [brief description]
```

**`audit-report.md`:** Same template structure as CQA reporter (Executive Summary → Content-by-Content Findings → Prioritised Action Plan). Adapted for CMS content findings rather than web page quality.

### Scoring rubric (analyser)

**Completeness (1–10):** Percentage of properties that have non-empty values, weighted by mandatory vs optional. All mandatory fields filled and most optional fields used = 9–10. Missing mandatory fields = major deduction.

**Structure (1–10):** Does the node have a template? Is it at a logical depth in the content tree? Does it have child content where expected? Orphaned nodes or nodes without templates score lower.

**Metadata (1–10):** Are SEO-relevant properties (title, meta description, keywords, isIndexable) populated? Nodes with composition-based SEO fields that are empty score lower.

### NuGet packaging entry

Add to `AgentRun.Umbraco.csproj` alongside the existing entries:

```xml
<None Include="Workflows\umbraco-content-audit\**\*" Pack="true" PackagePath="contentFiles\any\any\App_Data\AgentRun.Umbraco\workflows\umbraco-content-audit" />
```

### Language handling

Epic spec says: "Site has content in multiple languages → scan the default language only for v1; note in audit report that other variants exist." The content tools from Story 9.12 use `IPublishedContentCache` which returns content in the default language by default. No special handling needed in the scanner — just note in the agent prompt that the scan covers the default language only.

### workflow.yaml schema directive

Include the YAML language server directive for IDE autocomplete, same as existing workflows:

```yaml
# yaml-language-server: $schema=../../../../../AgentRun.Umbraco/Schemas/workflow-schema.json
```

Note: the schema path is relative to the TestSite copy. The packaged version in `AgentRun.Umbraco/Workflows/` uses a different relative path. Both need the directive. The TestSite copy should use `../../../../../AgentRun.Umbraco/Schemas/workflow-schema.json` and the package source copy should use `../../Schemas/workflow-schema.json`.

## Failure & Edge Cases

- **Site has content in multiple languages** → scan the default language only; note in audit report that other variants exist
- **Content types with no instances** → report as a finding ("unused content types") — this is a feature, not a bug
- **Very large content trees (100+ nodes)** → scanner should work within the tool size limits; prioritise nodes with templates (actual pages); skip element type content that only exists as nested block content
- **Scanner produces findings the analyser can't parse** → same failure mode as CQA; retry is the user's recovery path
- **Content node has no properties at all** → `get_content` returns empty `properties` object `{}`; scanner should note this
- **Content types that are pure compositions (element types)** → `list_content_types` returns these; scanner should identify them and not scan for their instances as standalone nodes
- **Template is empty string** → treat same as no template assigned; flag as a finding
- **User says "no" or wants to filter** → the opening message should handle user input about scope; if the user specifies a content type or subtree, use `list_content(contentType: ...)` or `list_content(parentId: ...)` accordingly
- **Unrecognised or unspecified inputs must be denied/rejected** — if user input is ambiguous, re-prompt rather than guessing

## What NOT to Build

- Do NOT build content modification or remediation — read-only audit only
- Do NOT build per-page SEO scores or comparison with external best practices — that's what the external CQA is for
- Do NOT duplicate the CQA or Accessibility workflows — this is a content model and completeness audit, not an SEO or accessibility scan
- Do NOT build custom UI for this workflow — it runs in the same instance detail view as all other workflows
- Do NOT add any C# code, tools, engine changes, or test code — this is a workflow-only story
- Do NOT use `fetch_url` — all data comes from in-process content tools
- Do NOT build draft content scanning — published content only
- Do NOT build media library scanning — defer to a future story

## Previous Story Intelligence

**From Story 9.12 (direct dependency — content tools):**
- 521/521 tests after completion. Content tools are registered and working.
- Code review caught 7 patches: `templateAlias` was always empty (fixed via `IFileService`), `get_content` truncation broke JSON (fixed with property-removal loop), `IHtmlEncodedString` handling for RTE, Block List text extraction, `CompositionPropertyTypes` for inherited properties.
- Manual E2E on the Clean Starter Kit TestSite confirmed: 25 content nodes, 32 document types (including element types and compositions), Home node at ID 1120 with 9 direct children.
- Property extraction verified for: `title` ("Clean Starter Kit"), `subtitle` ("For Umbraco"), `isIndexable` ("false"), `mainImage` (media with alt text).

**From Story 9.4 (Accessibility workflow — most recent workflow-only story):**
- Workflow-only stories have zero C# changes. The work is entirely in YAML + agent prompts.
- Agent prompts must include the five interactive mode invariants verbatim — proven to prevent stalls.
- The `# yaml-language-server: $schema=...` directive must be on line 1 of workflow.yaml.
- Code review focused on: prompt invariant correctness, output template structure, and locked phrases.

**From Story 9.1b/9.1c (CQA workflow polish — stall prevention):**
- The sequential-fetch invariant (one tool call per turn) is non-negotiable. Parallel tool calls cause post-batch stalls.
- The post-scan → write_file invariant prevents text-only turns between the last data-gathering call and write_file.
- Agent prompts that produce standalone text mid-task stall the workflow in interactive mode.

### TestSite Content Shape (from 9.12 E2E)

The Clean Starter Kit has this content:
- **25 published nodes** across 9 page content types
- **32 document types** total (includes element types like `richTextRow`, `imageRow`, compositions like `sEOControls`, `articleControls`)
- **Home** (id: 1120) is root with 9 children: Features, About, Blog, Contact, Error, XMLSitemap, Search, Authors, Categories
- **Blog** contains **7 article** nodes
- Properties include RTE, text strings, media pickers, content pickers, block lists, boolean toggles
- SEO composition (`sEOControls`) adds `metaName`, `metaDescription`, `metaKeywords`, `isIndexable`, `isFollowable` to most page types

This is the data the scanner will read. The agent prompts should be written to handle this specific shape (small-to-medium Umbraco site) while being generic enough for larger sites.

## Project Structure Notes

- New workflow files: `AgentRun.Umbraco/Workflows/umbraco-content-audit/` (package source)
- TestSite copy: `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/`
- No new C# files created or modified (except 1 line in `.csproj`)
- No new test files
- Namespace: N/A (no C# code)

## References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 9.13] — epic entry with workflow specification
- [Source: 9-12-umbraco-content-tools.md] — content tool implementations, property extraction behaviour, test results
- [Source: AgentRun.Umbraco/Workflows/content-quality-audit/] — CQA workflow as structural reference
- [Source: AgentRun.Umbraco/Workflows/accessibility-quick-scan/] — Accessibility workflow as structural reference
- [Source: AgentRun.Umbraco.csproj:61-62] — NuGet contentFiles pattern for shipping workflows
- [Source: project-context.md#Interactive mode is the primary UX model] — interactive mode is default

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
