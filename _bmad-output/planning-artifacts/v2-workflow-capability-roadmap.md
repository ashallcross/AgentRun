# V2+ Workflow Capability Roadmap

**Date:** 2026-04-12
**Author:** Winston (architect) with Adam
**Purpose:** Maps the original Cogworks Agent Runner workflow vision to specific AgentRun.Umbraco capabilities and epics. Ensures the v2+ roadmap is driven by real workflow use cases, not abstract infrastructure.

**Context:** The original product brief (2026-03-27) was based on four production workflows from the Cogworks BMAD Agent Runner: an 8-step audit delivery, a 4-step content pipeline, an 8-step Umbraco upgrade analysis, and a 4-step compliance assessment. V1 deliberately shipped as a read-only analysis and reporting engine ("safety first"). This document captures what's needed to unlock the full original vision.

---

## The Editorial Content Pipeline — Primary V2 Use Case

This is the workflow that proves AgentRun isn't just an audit tool — it's an editorial workflow platform.

**User story:** An editor logs into the Umbraco backoffice on Monday morning, clicks "Research & Write," selects their content area (e.g. "AI in Healthcare"), and by the time they've had their coffee there's a draft blog post sitting in their content tree ready for review.

### Workflow Structure

| Step | Agent | Tools | Writes | What It Does |
|------|-------|-------|--------|-------------|
| Scout | `agents/scout.md` | `fetch_url`, `write_file` | `research-raw.md` | Fetches RSS feeds, searches news APIs, finds relevant articles from the past 5-7 days on the specified subject |
| Analyst | `agents/analyst.md` | `read_file`, `write_file` | `quality-gate.md`, `research-analysis.md` | Quality gate (PASS/REJECT), theme identification, article angle recommendation. Rejects if nothing newsworthy found. |
| Writer | `agents/writer.md` | `read_file`, `write_file`, **`read_data_file`** | `article-draft.md` | Produces publication-ready article using the workflow's style guide (data file). Fact-checks claims before inclusion. |
| Publisher | `agents/publisher.md` | `read_file`, **`create_content`**, **`pick_media`** or **`upload_media`** | `publish-report.md` | Creates a content node in Umbraco as Draft, attaches a hero image from the media library, sets metadata. Awaits manual review and publishing. |

### What Each Step Needs That Doesn't Exist Yet

| Capability | Needed By | Current Status | Planned Epic |
|-----------|-----------|---------------|-------------|
| `fetch_url` with API keys (RSS, news APIs) | Scout | `fetch_url` exists; API key configuration per workflow does not | Epic 11 (data files runtime — API keys in workflow config) |
| `read_data_file` (read workflow-shipped files) | Writer | Schema allows `data_files`; no runtime implementation | Epic 11 |
| Style guide as a data file | Writer | Not implemented | Epic 11 (data files runtime) |
| `create_content` (write to Umbraco content tree) | Publisher | Not implemented | Epic 13 |
| `pick_media` / `upload_media` | Publisher | `list_media` planned for 11.4; write tools not planned | Epic 13 |
| Cross-run memory ("don't repeat last week's angle") | Analyst | Not implemented | Epic 13 (sidecar memory writing) |

### The Style Guide Pattern

The writer's style guide is a **data file**, not sidecar memory. It doesn't change between runs — it's authored by the workflow creator and ships with the workflow definition:

```
workflows/
  content-pipeline/
    workflow.yaml
    agents/
      scout.md
      analyst.md
      writer.md
      publisher.md
    data/
      style-guide.md          # Voice, tone, structure rules
      sources.yaml            # RSS feeds, API endpoints, search queries
      relevance.md            # What counts as on-topic
```

The agent prompt references: "Read the style guide from data/style-guide.md and apply it to all writing." The engine makes the data folder readable via a declared, sandboxed path.

---

## Capability Categories: What Unlocks What

### Category 1: Data Files Runtime (Epic 11 — Free Tier)

**What it is:** Workflows ship with read-only data files (style guides, checklists, templates, source lists, relevance criteria). Agents can read them via a tool or sandboxed path.

**Schema status:** `data_files` field exists in workflow schema and validator. No runtime implementation.

**Implementation:** Either a `read_data_file` tool that reads from `{workflowFolderPath}/data/{filename}`, or extend `read_file` to accept a `source: "data"` parameter that reads from the workflow's data folder instead of the instance folder. Sandboxed via `PathSandbox.ValidatePath()` against the workflow data root.

**Workflows this unlocks:**
- Content Pipeline (writer reads style guide)
- Any audit workflow with configurable checklists (Beat 4 investigation tasks)
- SEO Metadata Audit with configurable rule sets
- Any workflow with variant-specific configuration

### Category 2: Variant System Runtime (Epic 11 or 13 — Free Tier)

**What it is:** A workflow can have variants that load different data files. The Content Pipeline has "ai", "umbraco", "devops" content type variants, each with different sources, relevance criteria, and style guides. The Audit workflow has "umbraco-healthcheck", "sustainability", "ux-workup" variants, each with different checklists and templates.

**Schema status:** `variants` field exists in workflow schema and validator. No runtime implementation.

**Implementation:** At instance creation, the user selects a variant. The variant name is stored in instance state. Data file paths resolve via the variant: `{workflowFolderPath}/data/{variantName}/style-guide.md`.

**Workflows this unlocks:**
- Content Pipeline with multiple content type specialisations
- Audit workflows with different audit type configurations
- Any template-based workflow where the agent's behaviour is data-driven

### Category 3: Write-Back Tools (Epic 13 — Pro Tier)

**What it is:** Tools that create, update, and manage content in the Umbraco content tree. The in-process advantage means these use `IContentService` directly — no HTTP, no auth tokens, no JSON Schema endpoint calls.

**Tools needed:**

| Tool | Umbraco Service | What It Does |
|------|----------------|-------------|
| `create_content` | `IContentService` | Creates a content node with specified document type, parent, name, and property values. Saves as Draft by default. |
| `update_content` | `IContentService` | Updates property values on an existing content node. Does not publish. |
| `publish_content` | `IContentService` | Publishes a draft content node. Requires explicit opt-in in workflow config ("allow_publish: true"). |
| `upload_media` | `IMediaService` | Uploads a file to the media library. Returns the media ID for use in content pickers. |
| `pick_media` | `IMediaService` / published cache | Searches existing media by name/type. Returns media ID + metadata. |

**Property value construction:** The `create_content` tool accepts a normalised intermediate format from the agent and translates it into correct Umbraco property values in C#. The agent doesn't construct Block List JSON — the tool does. The tool uses `IContentTypeService` to look up the document type, determines the property editor for each field, and constructs the value accordingly.

**Safety model:**
- All write tools require explicit opt-in in the workflow definition (`tools: [create_content]`)
- `publish_content` requires additional opt-in (`allow_publish: true` in workflow config) — default is draft-only
- All writes are logged with the backoffice user's identity
- Write tools are Pro-tier only

**Workflows this unlocks:**
- Content Pipeline (publisher creates draft blog post)
- Content Migration (creates content from external source data)
- SEO Metadata Fix (updates missing meta descriptions found by the audit)
- Any workflow that produces actionable changes, not just reports

### Category 4: Sidecar Memory Writing (Epic 13 — Pro Tier)

**What it is:** Agents can persist learned patterns, preferences, and observations across workflow runs. Quinn remembers "Adam prefers accelerated qualification." The analyst remembers "last week's article covered AI in healthcare — don't repeat that angle."

**Storage model:**

| Scope | Location | Use Case |
|-------|----------|----------|
| Per-workflow (shared) | `{DataRoot}/memory/{workflowAlias}/` | Workflow-level patterns — "this audit type usually takes 3 weeks" |
| Per-workflow-per-user | `{DataRoot}/memory/{workflowAlias}/{userId}/` | User preferences — "Adam prefers listicle format" |

**Tools:**
- `write_sidecar` — writes to the agent's memory file in the scoped location
- `read_sidecar` — already partially exists (sidecar instructions); extend to include memory files

**Security:**
- Sandboxed to the memory root via `PathSandbox.ValidatePath()` — agents cannot write to arbitrary locations
- Agents can only read/write their own workflow's memory — cross-workflow memory access is blocked
- Per-user scoping is automatic based on the authenticated backoffice user

**Workflows this unlocks:**
- Content Pipeline (analyst avoids repeating topics)
- Audit workflows (agents learn client patterns across engagements)
- Any workflow where continuity across runs improves quality

### Category 5: Phases Within Steps (Epic 14 — Pro Tier)

**What it is:** A single step can have sub-phases with pause points, human review gates, and file uploads between phases. Beat 5 of the audit workflow generates a briefing pack (phase 1), pauses for the team to run a synthesis session (human activity), then digests the uploaded session transcript (phase 2).

**Schema extension:**
```yaml
phases:
  - id: briefing
    pause_after: true
    pause_message: "Briefing pack ready. Run your session, then upload the transcript."
    requires_uploads: true
    completion_check:
      files_exist: [briefing-pack.md]
  - id: digest
    completion_check:
      files_exist: [synthesis-output.md]
```

**Engine changes:** Step state machine needs to track phase progression within a step. The orchestrator needs to handle pause/resume at phase boundaries. Upload endpoint needed for file injection at pause points.

**Workflows this unlocks:**
- Full 8-beat audit delivery (the original Cogworks runner's primary use case)
- Any consulting workflow with client review gates
- Workflows requiring human input mid-step (not just between steps)

### Category 6: Sub-Agent Spawning (Epic 14 — Pro Tier)

**What it is:** A step can spawn child agent instances that run in parallel, each with focused input (per-page, per-section, per-content-type). Results are collected and returned to the parent step.

**Workflows this unlocks:**
- Content Health Monitor (parallel per-page quality scoring)
- Large-site content audit (parallel per-content-type analysis)
- Migration workflows (parallel per-document-type transformation)

### Category 7: Per-User Instance Isolation (Epic 13 — Pro Tier)

**What it is:** Each backoffice user sees only their own workflow runs. Instance list filtered by `createdBy`. Access control on instance detail.

**What exists:** `createdBy` field on `InstanceState`. `BackOfficeAccess` auth on all endpoints. User identity is available.

**What's needed:** Filter query in `InstanceManager.GetInstancesAsync()`. Ownership check in instance detail/cancel/retry endpoints. Configuration toggle (`isolation: per-user` vs `isolation: shared`).

**Workflows this unlocks:**
- Agency deployments where multiple editors run workflows independently
- Compliance scenarios where audit trails must be per-user
- Any multi-user Umbraco installation

---

## Epic Mapping Summary

| Epic | Theme | Key Capabilities | Tier |
|------|-------|-----------------|------|
| **11** | Adoption Accelerators | Data files runtime, `list_media`, Umbraco-connected templates, variant system (if feasible) | Free |
| **12** | Pro Foundation | Storage abstraction, database provider, package split | Pro infrastructure |
| **13** | Pro Capabilities | `create_content`, `upload_media`, sidecar memory writing, per-user isolation, content pipeline workflow | Pro |
| **14** | Advanced Pro | Phases within steps, file uploads mid-workflow, sub-agent spawning, variant system (if not in 11) | Pro |

---

## Traceability to Original Vision

| Original Workflow (Product Brief) | V1 Status | When It Ships |
|----------------------------------|-----------|--------------|
| 8-step audit delivery | Engine supports linear steps; phases/uploads missing | Full capability: Epic 14 |
| 4-step content pipeline | Scout + Analyst work today; Writer needs data files; Publisher needs write-back | Full capability: Epic 13 |
| Umbraco upgrade analysis | Would need file upload + code analysis tools | Epic 14+ |
| Compliance assessment | Works today with content tools for read-only assessment | Read-only: now. Write-back: Epic 13 |

---

## What Was Deliberately Descoped from V1 (and Why)

All of the following were explicitly marked "v2" in the product brief with the rationale "v1 is read-only (no content modification tools) — safety first":

- Sub-agent spawning
- Phase system within steps
- Sidecar memories (write)
- Variant system (runtime)
- File uploads
- Content modification tools / write-back
- Scheduled/triggered pipeline execution

**Nothing was lost.** The descoping was deliberate, documented, and safety-driven. This document maps the path from the current v1 to the full original vision across Epics 11-14.

---

## Reference Documents

- Original vision: `planning-artifacts/umbraco-agent-workflow-ideas.md`
- Product brief: `planning-artifacts/product-brief-shallai-umbraco-agent-runner.md`
- Product brief distillate: `planning-artifacts/product-brief-shallai-umbraco-agent-runner-distillate.md`
- PRD: `planning-artifacts/prd.md`
- Architecture summary: `planning-artifacts/cogworks-agent-runner-architecture-summary.md`
- V2 futures: `planning-artifacts/v2-future-considerations.md`
- Cogworks runner workflows: `/Users/adamshallcross/Documents/Cogworks BMAD Agent Runner/workflows/`
