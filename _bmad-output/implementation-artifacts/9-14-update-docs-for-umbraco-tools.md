# Story 9.14: Update Documentation and Beta Test Plan for Umbraco Content Tools

Status: done

**Depends on:** Story 9.13 (Umbraco Content Audit workflow), Story 9.12 (Umbraco content tools)
**Blocks:** 9.5 (Private Beta Distribution)

> This is a documentation-only story. Three files need updating to reflect the Umbraco-connected tools and workflow added in Stories 9.12 and 9.13. Zero C# changes, zero test changes.

## Story

As a beta tester,
I want the documentation to describe the Umbraco-connected tools and the Content Audit workflow,
So that I understand this package audits my actual Umbraco content, not just external URLs.

## Acceptance Criteria

**AC1 — README leads with Umbraco Content Audit**
Given the README.md exists with the current Quick Start,
When the dev agent updates it,
Then the introductory blurb and Quick Start lead with the Umbraco Content Audit workflow as the headline use case,
And the external-URL workflows (CQA, Accessibility Quick-Scan) are repositioned as "also included",
And the workflow table in "Copy the example workflows" lists all three workflows.

**AC2 — README does not lose existing content**
Given the current README has sections for Quick Start, Creating Your Own Workflows, Configuration, Known Limitations, and Uninstalling,
When the dev agent updates it,
Then all existing sections are preserved (updated, not replaced),
And external-URL workflow documentation remains valid and present.

**AC3 — Workflow authoring guide documents content tools**
Given the workflow-authoring-guide.md has an "Available Tools" section,
When the dev agent updates it,
Then `list_content`, `get_content`, and `list_content_types` are added as new subsections after the existing `list_files` tool,
And each tool documents: description, parameters table (Name, Required, Description), return value summary, and security/access notes,
And the parameter names, types, and descriptions match the actual tool implementations exactly.

**AC4 — Workflow authoring guide mentions content tools in examples**
Given the authoring guide has a "Learning from the Examples" section,
When the dev agent updates it,
Then the Umbraco Content Audit workflow is added as a third example reference,
And its description highlights the in-process content tool pattern (no external URLs, reads from Umbraco's published content cache).

**AC5 — Beta test plan exercises Umbraco Content Audit**
Given the beta-test-plan.md currently only exercises the CQA workflow,
When the dev agent updates it,
Then the smoke test includes running the Umbraco Content Audit workflow as the primary test,
And the pass criteria include verifying the Content Audit workflow produces artifacts referencing actual site content,
And the plan updates step 9 to include the third workflow profile,
And step 10 verifies all three workflows appear in the dashboard.

**AC6 — Beta test plan preserves existing steps**
Given the beta test plan has 13 existing steps,
When the dev agent updates it,
Then all existing steps remain (renumbered if needed),
And the CQA workflow test remains as a secondary verification.

**AC7 — No broken cross-references**
Given the README and authoring guide cross-reference each other,
When the dev agent updates both files,
Then all internal links (`docs/workflow-authoring-guide.md`, anchor links) remain valid.

## Tasks / Subtasks

- [x] Task 1: Update README.md (AC: #1, #2, #7)
  - [x] 1.1 Rewrite the introductory blurb (lines 6-11) to lead with the Umbraco Content Audit workflow — "AgentRun ships with three example workflows, including a Content Audit that analyses the content in your Umbraco instance directly"
  - [x] 1.2 Update the workflow table (lines 60-63) to add the Umbraco Content Audit as the first entry
  - [x] 1.3 Reposition the blurb so the Content Audit is the headline ("audit your own content"), external-URL workflows are "also included"
  - [x] 1.4 Update "Open Agent Workflows" section (lines 80-83) to mention all three workflows and suggest starting with the Content Audit
  - [x] 1.5 Verify all internal links still work (e.g., `docs/workflow-authoring-guide.md` reference)

- [x] Task 2: Update docs/workflow-authoring-guide.md (AC: #3, #4, #7)
  - [x] 2.1 Add `### list_content` subsection after `### list_files` (after line 290)
  - [x] 2.2 Add `### get_content` subsection after `list_content`
  - [x] 2.3 Add `### list_content_types` subsection after `get_content`
  - [x] 2.4 Each subsection must include: description, parameters table, return value summary, access notes
  - [x] 2.5 Add a note that these tools read from the Umbraco published content cache in-process — no HTTP requests, no SSRF concerns
  - [x] 2.6 Update the "Learning from the Examples" section (lines 507-519) to add the Umbraco Content Audit as a third example
  - [x] 2.7 Verify anchor links in README still resolve (e.g., `#configuring-tool-tuning-values`)

- [x] Task 3: Update docs/beta-test-plan.md (AC: #5, #6)
  - [x] 3.1 Update step 9 to include the `umbraco-content-audit` workflow profile alongside CQA and Accessibility
  - [x] 3.2 Update step 10 to verify all three workflows appear in the dashboard
  - [x] 3.3 Add a new step (after the existing step 11) to run the Umbraco Content Audit workflow as the primary smoke test
  - [x] 3.4 The new step should: start the workflow, confirm it scans the site's own content using `list_content_types`/`list_content`/`get_content`, verify it produces `audit-report.md` referencing actual site content
  - [x] 3.5 Keep the existing CQA step 11 as a secondary verification (renumber as needed)
  - [x] 3.6 Update the Pass Criteria checklist to include a Content Audit line item

- [x] Task 4: Final validation (AC: #7)
  - [x] 4.1 Read all three updated files end-to-end to verify no broken references or orphaned sections
  - [x] 4.2 Verify the workflow count is consistently "three" across all docs (not "two")

## Dev Notes

### This story is documentation-only — zero code changes

The dev agent modifies exactly three files:
1. `README.md` (repo root)
2. `docs/workflow-authoring-guide.md`
3. `docs/beta-test-plan.md`

No `.cs`, `.ts`, `.csproj`, `.yaml`, or test files. The test count must remain at 521/521 — if tests fail, something unrelated broke.

### Content tool reference data (from source code — use these exact values)

#### `list_content`

- **Source:** `AgentRun.Umbraco/Tools/ListContentTool.cs`
- **Name:** `list_content`
- **Description:** Lists published content nodes from the Umbraco instance. Optionally filter by document type alias or parent node ID.
- **Parameters:**
  - `contentType` (string, optional) — Filter by document type alias (e.g., `'blogPost'`). Returns all types if omitted.
  - `parentId` (integer, optional) — Filter to direct children of this content node ID. Returns all content if omitted.
- **Returns:** JSON array of objects: `id`, `name`, `contentType`, `url`, `level`, `createDate`, `updateDate`, `creatorName`, `childCount`
- **Access:** Reads from Umbraco's published content cache in-process. No HTTP requests, no SSRF concerns. Large result sets are truncated with a marker indicating how many nodes were returned vs total.

#### `get_content`

- **Source:** `AgentRun.Umbraco/Tools/GetContentTool.cs`
- **Name:** `get_content`
- **Description:** Gets the full details and property values of a single published content node by ID.
- **Parameters:**
  - `id` (integer, required) — The ID of the published content node to retrieve.
- **Returns:** JSON object: `id`, `name`, `contentType`, `url`, `level`, `createDate`, `updateDate`, `creatorName`, `templateAlias`, `properties` (object mapping alias to extracted text value)
- **Property extraction:** Rich Text is stripped to plain text, Text String/Textarea as-is, Content Picker shows name + URL, Media Picker shows name + URL + alt text, Block List/Grid shows simplified block content, TrueFalse shows "true"/"false", fallback is JSON-serialised source value.
- **Access:** Same in-process published content cache. Large responses truncated by removing properties from the end.

#### `list_content_types`

- **Source:** `AgentRun.Umbraco/Tools/ListContentTypesTool.cs`
- **Name:** `list_content_types`
- **Description:** Lists document type definitions from the Umbraco instance, including their properties, compositions, and allowed child types.
- **Parameters:**
  - `alias` (string, optional) — Filter by document type alias. Returns all document types if omitted.
- **Returns:** JSON array of objects: `alias`, `name`, `description`, `icon`, `properties` (array of `{alias, name, editorAlias, mandatory}`), `compositions` (array of aliases), `allowedChildTypes` (array of aliases)
- **Access:** Reads from `IContentTypeService`. No published content cache needed. Large responses truncated.

### Authoring guide structure — where to insert content tools

The existing "Available Tools" section (lines 219-290) has this order:
1. `fetch_url` (line 223)
2. `read_file` (line 248)
3. `write_file` (line 265)
4. `list_files` (line 280)

Insert the three content tools **after `list_files`** as a new group. Add a brief intro line like "The following tools are available when the workflow runs inside an Umbraco instance:" to distinguish them from the file/network tools.

### README restructuring — what to change

Current intro blurb (lines 6-11):
```
> AgentRun ships with two example workflows: Content Quality Audit and Accessibility Quick-Scan.
> After installing the package and configuring an Umbraco.AI profile, open the Agent Workflows
> section, click a workflow, and click Start. The agent will ask you for one or more URLs to
> audit -- paste URLs from your own site (or any public site you'd like a second opinion on)
> and watch the workflow run.
```

This needs rewriting to:
- Lead with "three example workflows" (not two)
- Lead with the Content Audit as the headline ("audit your Umbraco content directly from the backoffice")
- Move the external-URL workflows to secondary position ("also includes Content Quality Audit and Accessibility Quick-Scan for auditing any public URL")

Current workflow table (lines 60-63):
```
| Workflow | Steps | What it does |
|----------|-------|-------------|
| **Content Quality Audit** | Scanner, Analyser, Reporter | Fetches pages, scores content quality, generates an audit report |
| **Accessibility Quick-Scan** | Scanner, Reporter | Fetches pages, identifies WCAG 2.1 AA issues, produces a prioritised fix list |
```

Add a new first row:
```
| **Umbraco Content Audit** | Scanner, Analyser, Reporter | Reads your Umbraco content tree, scores completeness and structure, generates a prioritised audit report |
```

### Beta test plan — new step content

The new Umbraco Content Audit step should mirror the existing step 11 (CQA) structure but for in-process content:

1. Click **Umbraco Content Audit**
2. Click **Start**
3. Confirm when prompted (this workflow reads from the site's own content — no URL input needed)
4. **Check:** The scanner calls `list_content_types` and `list_content` to inventory the content model
5. **Check:** The scanner calls `get_content` for individual content nodes
6. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
7. **Check:** The audit report references actual site content (node names, content types, real property values)

### What NOT to change

- Do NOT rewrite the entire README — update, don't replace
- Do NOT remove external-URL workflow documentation
- Do NOT change `umbraco-package.json` or `.csproj`
- Do NOT add any code, tests, or workflow files
- Do NOT change the structure of the authoring guide beyond adding tools and updating examples

## Failure & Edge Cases

- **Dev agent rewrites too much of README** — the epic spec says "update, don't replace". Verify all existing sections survive.
- **Tool parameter names don't match source** — the Dev Notes section above has the exact parameter names from the tool source code. Use these verbatim.
- **Authoring guide anchor links break** — if the dev agent changes heading text (e.g., "Available Tools"), the README's `#configuring-tool-tuning-values` anchor must still resolve. Check all cross-file anchor references.
- **Beta test plan step numbering goes wrong** — inserting a new step requires renumbering subsequent steps. Verify step numbers are sequential.
- **"Two" vs "three" inconsistency** — any remaining reference to "two example workflows" in the docs is a bug. Search all three files for the word "two" after editing.
- **Unrecognised or unspecified inputs must be denied/rejected** — N/A for a docs-only story, but the dev agent should not invent tool parameters or return fields that don't exist in the source code.

## What NOT to Build

- Do NOT add any C# code, tools, engine changes, or test code
- Do NOT modify workflow YAML files
- Do NOT create new documentation files — update the existing three only
- Do NOT add a changelog or release notes file
- Do NOT rewrite sections that don't need updating (e.g., Configuration, Known Limitations, Uninstalling)

## Previous Story Intelligence

**From Story 9.13 (direct dependency — Umbraco Content Audit workflow):**
- Workflow is at `AgentRun.Umbraco/Workflows/umbraco-content-audit/` with 3 steps: scanner, analyser, reporter.
- Scanner tools: `list_content`, `list_content_types`, `get_content`, `write_file`. No `fetch_url`.
- Scanner produces `content-inventory.md` and `scan-results.md`. Analyser produces `quality-scores.md`. Reporter produces `audit-report.md`.
- Manual E2E on Clean Starter Kit: 26 nodes, 33 document types. All 4 artifacts produced.
- Zero C# changes. 521/521 tests.
- Workflow mode: interactive. Profile: `anthropic-sonnet-4-6` (user must change to their profile alias).

**From Story 9.12 (content tools):**
- Three tools: `list_content`, `get_content`, `list_content_types`.
- Code review caught 7 patches: `templateAlias`, truncation, `IHtmlEncodedString`, Block List text, `CompositionPropertyTypes`.
- Property extraction covers: RTE, TextBox, TextArea, ContentPicker, MediaPicker3, BlockList, BlockGrid, TrueFalse, Integer, Decimal, DateTime, Tags, fallback.
- All tools truncate large responses with a descriptive marker rather than erroring.

**From Story 9.3 (documentation — Round 2 Paige):**
- README, authoring guide, and beta test plan were written in story 9.3. This story updates them.
- The authoring guide structure was designed to be extensible — the "Available Tools" section is a flat list of `###` subsections.
- The beta test plan is a numbered step-by-step manual procedure.

## Project Structure Notes

- Modified files:
  - `README.md` (repo root)
  - `docs/workflow-authoring-guide.md`
  - `docs/beta-test-plan.md`
- No new files created
- No files deleted
- Namespace: N/A (no code)

## References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 9.14] — epic entry
- [Source: _bmad-output/implementation-artifacts/9-13-umbraco-content-audit-workflow.md] — workflow details
- [Source: _bmad-output/implementation-artifacts/9-12-umbraco-content-tools.md] — content tool implementations
- [Source: AgentRun.Umbraco/Tools/ListContentTool.cs] — `list_content` parameter schema and return shape
- [Source: AgentRun.Umbraco/Tools/GetContentTool.cs] — `get_content` parameter schema, property extraction, return shape
- [Source: AgentRun.Umbraco/Tools/ListContentTypesTool.cs] — `list_content_types` parameter schema and return shape
- [Source: README.md] — current README to update
- [Source: docs/workflow-authoring-guide.md] — current authoring guide to update
- [Source: docs/beta-test-plan.md] — current beta test plan to update

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

None — documentation-only story, no debugging required.

### Completion Notes List

- README.md: Rewrote intro blurb to lead with Umbraco Content Audit as headline use case; repositioned CQA and Accessibility as "also included"; added Content Audit as first row in workflow table; updated step 6 to recommend Content Audit as starting point; all existing sections preserved.
- workflow-authoring-guide.md: Added "Umbraco Content Tools" intro paragraph and three tool subsections (`list_content`, `get_content`, `list_content_types`) after `list_files` with parameter tables, return value summaries, and access notes; added Umbraco Content Audit to "Learning from the Examples" section; all anchor links verified intact.
- beta-test-plan.md: Updated step 9 to list all three workflow profiles; updated step 10 to verify three workflows; inserted new step 11 (Umbraco Content Audit primary smoke test); renumbered old step 11 to step 12 (CQA secondary verification); renumbered subsequent steps; updated Pass Criteria with Content Audit line item.
- Validation: No stale "two" references remain; "three" used consistently; all cross-file links valid; 521/521 tests pass (zero code changes).

### Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-12 | Documentation updated for Umbraco content tools and Content Audit workflow across README, authoring guide, and beta test plan |

### File List

- `README.md` (modified)
- `docs/workflow-authoring-guide.md` (modified)
- `docs/beta-test-plan.md` (modified)
