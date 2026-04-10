# Story 9.3: Documentation & NuGet Packaging

Status: in-progress

## Story

As a developer,
I want clear documentation and a properly configured NuGet package,
so that I can install, configure, and extend the workflow engine with confidence.

## Acceptance Criteria

1. **AC1 — NuGet metadata**: The .csproj is configured with correct NuGet metadata: PackageId (already set), Version (`1.0.0-beta.1`), Description, Authors, License, ProjectUrl, RepositoryUrl, Tags. `dotnet pack` produces a valid `.nupkg`.
2. **AC2 — Frontend assets in package**: The package includes compiled frontend assets in `wwwroot/App_Plugins/AgentRunUmbraco/`. Verified by inspecting the `.nupkg` contents.
3. **AC3 — Example workflows in package**: The package includes both example workflow folders (Content Quality Audit and Accessibility Quick-Scan) with their `workflow.yaml` and `agents/*.md` files. Consumers who install the package get working workflows without manual file copying.
4. **AC4 — JSON Schema in package**: The package includes `Schemas/workflow-schema.json` (already EmbeddedResource — verify it's accessible to consumers for IDE setup).
5. **AC5 — Clean install**: `dotnet add package AgentRun.Umbraco` installs cleanly into an Umbraco 17+ site with no manual configuration beyond Umbraco.AI profile setup.
6. **AC6 — Clean uninstall (NFR19)**: The package is removable without side effects — no database tables, configuration entries, or orphaned files left behind.
7. **AC7 — Getting started guide (FR68)**: A getting started guide covers: installation via NuGet, Umbraco.AI provider and profile configuration prerequisites, running the example workflow for the first time, and what to expect. Concise and task-oriented — not a reference manual.
8. **AC8 — First-run framing**: The getting started guide's "First Run" section explicitly uses the "paste your own URLs" framing established in Story 9.1c. The README front matter uses the same framing as a pull-quote. This is intentional product positioning — AgentRun being unapologetically real is differentiation.
9. **AC9 — Workflow authoring guide (FR69)**: A workflow authoring guide covers: workflow.yaml schema and field reference, writing agent prompt markdown files, artifact handoff (`reads_from`/`writes_to`), completion checking, profile configuration (per-step and defaults), available tools, tool tuning values (resolution chain per Story 9.6), and a walkthrough of creating a simple 2-step workflow. References the JSON Schema for IDE validation setup.
10. **AC10 — Package version alignment**: The `umbraco-package.json` version is updated from `0.0.0` to `1.0.0-beta.1` to match the NuGet package version.

## What NOT to Build

- Do NOT list on the Umbraco Marketplace — that's Story 10.5
- Do NOT create a public schema URL for the JSON Schema — consumers use the in-package file path with a relative `# yaml-language-server: $schema=` directive
- Do NOT write a full reference manual — the getting started guide is task-oriented, the authoring guide is a walkthrough, not exhaustive API docs
- Do NOT create CHANGELOG.md, CONTRIBUTING.md, or other community-facing files — those are post-beta concerns
- Do NOT modify engine code, tool code, or workflow files — this story is packaging and docs only
- Do NOT add build targets, MSBuild tasks, or CI/CD pipeline configuration — manual `dotnet pack` is sufficient for beta
- Do NOT add a LICENSE file yet — licensing decision is deferred to Story 10.4

## Failure & Edge Cases

- **Workflow files not found after install**: If example workflows are included as `Content` items but the RCL packaging strips them, the consumer gets an empty workflow list. The task must verify workflows are discoverable by `WorkflowRegistry` after a NuGet install (not just in the TestSite dev host). Test by inspecting `.nupkg` contents and confirming file paths match the workflow discovery path pattern.
- **Schema not extractable by consumers**: The schema is currently an `EmbeddedResource` (compiled into the DLL). Consumers who want IDE autocomplete for their own workflows need the `.json` file on disk, not embedded. Determine whether the schema should also be included as a `Content` item that copies to output, or whether documentation should instruct consumers to extract it manually. Prefer the simplest approach that lets `# yaml-language-server: $schema=` work.
- **Frontend assets missing from nupkg**: If `npm run build` hasn't been run before `dotnet pack`, the `wwwroot/` folder is empty and the package ships without a UI. The task must document (and verify) the pre-pack build sequence: `cd Client && npm run build && cd .. && dotnet pack`.
- **Version mismatch between NuGet and umbraco-package.json**: If these drift, Umbraco's extension loader may report a different version than NuGet. Both must be `1.0.0-beta.1`.
- **Stale documentation after future changes**: Docs written now will drift. This is accepted for beta. The authoring guide should avoid hardcoding values that change between releases (e.g., list available tools by category, not by exhaustive enumeration).

## Agent Assignment & Handoff Protocol

This story is split between two agents executed sequentially. **Amelia runs first, Paige runs second.** Each agent owns specific files and must not touch the other's.

### Round 1 — Amelia (dev agent): Phase 1 + Phase 3a

**Scope:** Tasks 1-4, Task 9. NuGet packaging, package verification, test baseline.
**Owns:** `AgentRun.Umbraco/AgentRun.Umbraco.csproj`, `AgentRun.Umbraco/Workflows/**`, `Client/public/umbraco-package.json`
**Does NOT touch:** `README.md`, `docs/**` — those are Paige's.

**Handoff requirement:** Before marking Phase 1 complete, Amelia MUST record the following decisions in the "Completion Notes List" section at the bottom of this file:
1. **Workflow packaging path**: What `PackagePath` or `contentFiles` structure was used for example workflows? What path do they land at in the consumer's project?
2. **Schema distribution decision**: Is the schema available on disk for consumers, or only embedded? If on disk, what's the path relative to the consumer's project root?
3. **`yaml-language-server` directive path**: What exact `$schema=` path should consumers use in their workflow.yaml files?
4. **Any .csproj surprises**: Anything unexpected about RCL packaging behaviour that Paige should know when writing docs.
5. **Package verification result**: Confirmed contents of the `.nupkg` (list the key paths).

These notes are the contract between Amelia and Paige. Paige will treat them as facts, not suggestions.

### Round 2 — Paige (tech writer): Phase 2 + Phase 3b

**Scope:** Tasks 5-8. README, workflow authoring guide, clean install verification docs.
**Owns:** `README.md` (repo root), `docs/workflow-authoring-guide.md`
**Does NOT touch:** `*.csproj`, `AgentRun.Umbraco/Workflows/**`, `Client/**`, any C# or TypeScript files.

**Prerequisite:** Read Amelia's Completion Notes in this file BEFORE starting. Use the documented paths and decisions — do not guess or assume.

**Additional .csproj edit (Task 5.4 only):** Paige adds `<PackageReadmeFile>` and the README `<None>` include to the .csproj. This is the ONE .csproj change Paige is authorised to make, and only after the README exists.

### Round 3 — Adam (manual E2E): Phase 3c

**Scope:** Task 8.2. Fresh Umbraco install test against the local `.nupkg`. Human-only gate.

---

## Tasks / Subtasks

### Phase 1 — NuGet Packaging (Amelia)

- [x] **Task 1: Add NuGet metadata to .csproj** (AC: 1, 10)
  - [x] 1.1 Add to the existing PropertyGroup: `<Version>1.0.0-beta.1</Version>`, `<Description>` (one-line: "AI-powered workflow engine for Umbraco CMS — define multi-step agent workflows in YAML"), `<Authors>Adam Shallcross</Authors>`, `<PackageTags>umbraco;ai;workflow;agent;content-quality;accessibility</PackageTags>`
  - [x] 1.2 Do NOT add License, ProjectUrl, or RepositoryUrl yet — these depend on Story 10.4 (licensing) and the repo not being public yet. Add XML comments noting these are deferred.
  - [x] 1.3 Update `Client/public/umbraco-package.json` version from `"0.0.0"` to `"1.0.0-beta.1"`

- [x] **Task 2: Include example workflows in the NuGet package** (AC: 3)
  - [x] 2.1 Create a `Workflows/` folder inside `AgentRun.Umbraco/` (the package project, not TestSite)
  - [x] 2.2 Copy both example workflow folders from `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/` into `AgentRun.Umbraco/Workflows/`:
    - `content-quality-audit/workflow.yaml` + `agents/scanner.md`, `agents/analyser.md`, `agents/reporter.md`
    - `accessibility-quick-scan/workflow.yaml` + `agents/scanner.md`, `agents/reporter.md`
  - [x] 2.3 Add `<Content>` items in .csproj to include `Workflows/**` in the NuGet package with `Pack="true"` and appropriate `PackagePath` so they land in a discoverable location. Use `contentFiles` or `content` NuGet folder convention.
  - [x] 2.4 **Critical decision**: The workflow files must end up in a location that `WorkflowRegistry` can discover. Check how `WorkflowRegistry` resolves its scan path (likely `App_Data/AgentRun.Umbraco/workflows/` under the consumer's content root). If workflows ship inside the NuGet package's `contentFiles/`, they may need a post-install copy or the registry needs a secondary scan path. **Simplest approach**: ship workflows as `contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/` in the nupkg so they auto-copy to the consumer's project. Verify this works with `dotnet pack` + `dotnet add package` round-trip.

- [x] **Task 3: Verify schema distribution** (AC: 4)
  - [x] 3.1 The schema is already `EmbeddedResource`. Determine if consumers also need a file-on-disk copy for IDE autocomplete. If yes, add a second `<Content>` include that copies `Schemas/workflow-schema.json` to the package output so it's extractable. If no, document in the authoring guide how to extract the embedded schema (e.g., copy from the NuGet cache or reference a relative path).
  - [x] 3.2 Whichever approach is chosen, document the `# yaml-language-server: $schema=<path>` directive syntax in the authoring guide with the correct path for consumers.

- [x] **Task 4: Verify package contents** (AC: 1, 2, 3, 4)
  - [x] 4.1 Run `cd Client && npm run build` to ensure frontend assets are current
  - [x] 4.2 Run `dotnet pack AgentRun.Umbraco/AgentRun.Umbraco.csproj -c Release -o ./nupkg`
  - [x] 4.3 Inspect the `.nupkg` (it's a zip) and verify it contains:
    - `lib/net10.0/AgentRun.Umbraco.dll` (compiled backend)
    - `staticwebassets/` or `wwwroot/App_Plugins/AgentRunUmbraco/` (frontend bundles)
    - Workflow files (in whatever path was chosen in Task 2)
    - NuGet metadata (description, tags, version)
  - [x] 4.4 Verify `umbraco-package.json` inside the package shows version `1.0.0-beta.1`

### Phase 2 — Documentation (Paige)

**START HERE:** Read Amelia's Completion Notes at the bottom of this file first. Use the documented paths and decisions as facts.

- [x] **Task 5: Create README.md** (AC: 7, 8)
  - [x] 5.1 Create `README.md` in the repo root (not inside the package project — it's for the repo and NuGet gallery)
  - [x] 5.2 Structure:
    - **Header**: Package name, one-line description, badges placeholder (version, Umbraco compatibility)
    - **Pull-quote** (product positioning): _"AgentRun ships with two example workflows: Content Quality Audit and Accessibility Quick-Scan. After installing the package and configuring an Umbraco.AI profile, open the Agent Workflows section, click a workflow, and click Start. The agent will ask you for one or more URLs to audit — paste URLs from your own site (or any public site you'd like a second opinion on) and watch the workflow run."_
    - **Quick start**: 4-step install (dotnet add package, configure Umbraco.AI profile, run site, open Agent Workflows section)
    - **Prerequisites**: Umbraco 17+, .NET 10, at least one Umbraco.AI provider package (Anthropic or OpenAI) with a configured profile
    - **What's included**: List the two example workflows with one-line descriptions
    - **Creating your own workflows**: Link to the workflow authoring guide
    - **Configuration**: Minimal — mention `AgentRun` section in `appsettings.json` for advanced settings (data root path, workflow scan path), note that sensible defaults mean most users need zero config beyond the Umbraco.AI profile
    - **Known limitations (beta)**: Single concurrent instance per workflow, no database persistence (disk-based), no Marketplace listing yet
  - [x] 5.3 Tone: direct, practical, no hype. Match the "unapologetically real" positioning.
  - [x] 5.4 Add `<PackageReadmeFile>README.md</PackageReadmeFile>` to the .csproj and a `<None Include="../README.md" Pack="true" PackagePath="/" />` item to include it in the nupkg.

- [x] **Task 6: Create workflow authoring guide** (AC: 9)
  - [x] 6.1 Create `docs/workflow-authoring-guide.md`
  - [x] 6.2 Sections:
    - **Overview**: What a workflow is (YAML definition + agent prompt files), how the engine executes steps
    - **Workflow structure**: Annotated `workflow.yaml` field reference covering all root keys (`name`, `description`, `mode`, `default_profile`, `steps`, `icon`, `variants`, `tool_defaults`) and all step keys (`id`, `name`, `agent`, `tools`, `reads_from`, `writes_to`, `completion_check`, `profile`, `description`, `data_files`, `tool_overrides`). Mark required vs optional.
    - **Writing agent prompts**: How the agent markdown files work, what context the engine injects, how to reference artifacts
    - **Artifact handoff**: How `reads_from` and `writes_to` connect steps, file naming conventions
    - **Completion checking**: How `completion_check` works (artifact existence verification)
    - **Profile configuration**: Default profile at workflow level, per-step override, fallback chain
    - **Available tools**: `fetch_url`, `read_file`, `write_file`, `list_files` — what each does, when to use it, key constraints (SSRF protection on fetch, path sandboxing on file tools)
    - **Configuring tool tuning values** (per Story 9.6 requirement): Resolution chain (step `tool_overrides` -> workflow `tool_defaults` -> site config `AgentRun:ToolDefaults` -> engine defaults), available settings (`fetch_url.max_response_bytes`, `fetch_url.timeout_seconds`, `read_file.max_response_bytes`, `tool_loop.user_message_timeout_seconds`), security rationale for hard caps, example YAML snippet
    - **IDE validation setup**: How to add `# yaml-language-server: $schema=<path>` directive, where to find the schema file
    - **Walkthrough: Creating a simple 2-step workflow**: Step-by-step guide to creating a minimal workflow (e.g., a "URL Summary" workflow with a fetcher step and a summariser step). Include complete `workflow.yaml` and agent `.md` file contents.
  - [x] 6.3 Reference the shipped example workflows (CQA and Accessibility Quick-Scan) as learning resources throughout
  - [x] 6.4 Do NOT exhaustively enumerate every possible value or edge case — keep it practical. Link to the JSON Schema as the authoritative field reference.

- [x] **Task 7: Link README to authoring guide** (AC: 7, 9)
  - [x] 7.1 Ensure the README's "Creating your own workflows" section links to `docs/workflow-authoring-guide.md`

### Phase 3a — Test Verification (Amelia, runs immediately after Phase 1)

- [x] **Task 9: Run existing tests** (AC: all)
  - [x] 9.1 Run `dotnet test AgentRun.Umbraco.slnx` — all tests must pass (baseline: 465/465)
  - [x] 9.2 Run `cd Client && npm test` — all frontend tests must pass
  - [x] 9.3 No new tests expected for this story (it's packaging and docs), but verify nothing is broken by .csproj changes

### Phase 3b — Documentation Verification (Paige, runs after Phase 2)

- [x] **Task 8: Clean install verification docs** (AC: 5, 6)
  - [x] 8.1 Verify NFR19 claim: confirm (by reading the codebase, not running a test) that the package creates no database tables, no configuration entries, no files outside `App_Data/AgentRun.Umbraco/` at runtime. Add a brief "Uninstalling" section to the README.
  - [x] 8.2 Document the manual E2E test steps in the README or a separate `docs/beta-test-plan.md` for Adam to execute:
    1. Create a fresh Umbraco 17 site: `dotnet new umbraco -n TestInstall`
    2. Add the local package: `dotnet add package AgentRun.Umbraco --source ./nupkg`
    3. Add an Umbraco.AI provider: `dotnet add package Umbraco.AI.Anthropic`
    4. Run the site, configure a profile in Settings > AI
    5. Navigate to Agent Workflows section
    6. Verify both example workflows appear
    7. Start a workflow, provide a URL, verify it runs
    8. Remove the package: `dotnet remove package AgentRun.Umbraco`
    9. Verify no orphaned files or errors on next run

### Phase 3c — Manual E2E Gate (Adam, runs after Phases 1+2 are both complete)

- [ ] **Task 10: Fresh install smoke test** (AC: 5, 6)
  - [ ] 10.1 Execute the test steps documented by Paige in Task 8.2 against the `.nupkg` produced by Amelia in Task 4
  - [ ] 10.2 Verify both example workflows appear and at least one runs successfully
  - [ ] 10.3 Verify clean uninstall leaves no orphaned files

## Dev Notes

### Current .csproj State (what exists vs what's needed)

**Already configured:**
- `PackageId`: `AgentRun.Umbraco`
- `Product` / `Title`: `AgentRun.Umbraco`
- `EmbeddedResource`: `Schemas/workflow-schema.json`
- `Content Remove`: `Client/**` (excluded from package)
- Frontend assets in `wwwroot/App_Plugins/AgentRunUmbraco/`

**Missing (this story adds):**
- `Version`: needs `1.0.0-beta.1`
- `Description`: one-liner for NuGet gallery
- `Authors`: `Adam Shallcross`
- `PackageTags`: discoverability tags
- `PackageReadmeFile`: points to README.md
- Example workflow `Content` includes
- `umbraco-package.json` version update from `0.0.0`

**Deferred (NOT this story):**
- `PackageLicenseExpression` or `PackageLicenseFile` — Story 10.4
- `PackageProjectUrl` / `RepositoryUrl` — repo not public yet
- `PackageIcon` — not required for private beta

### Workflow Discovery Path

`WorkflowRegistry` scans for workflows at a configurable path (default: `{ContentRootPath}/App_Data/AgentRun.Umbraco/workflows/`). Example workflows must land at this path in the consumer's project for auto-discovery. The NuGet `contentFiles` convention (`contentFiles/any/any/...`) copies files to the consumer's project root on package install. Verify this works for the `App_Data/` path.

### RCL Static Web Assets

Razor Class Libraries serve static files from `wwwroot/` via the `_content/{PackageId}/` path prefix at runtime. Umbraco's `StaticWebAssetBasePath` is set to `/` in the .csproj, which maps the package's `wwwroot/` to the site root. The `App_Plugins/AgentRunUmbraco/` path is what Umbraco's extension loader expects.

### Tool Tuning Resolution Chain (for authoring guide)

Per Story 9.6, the resolution order is:
1. Step-level `tool_overrides` (most specific)
2. Workflow-level `tool_defaults`
3. Site-level `AgentRun:ToolDefaults` in `appsettings.json`
4. Engine hard-coded defaults (least specific)

After resolution, the value is capped by site-level `AgentRun:ToolLimits` ceilings. Workflows that declare values above the ceiling fail validation at load time.

Available tuning keys:
- `fetch_url.max_response_bytes` (engine default: 2 MB)
- `fetch_url.timeout_seconds` (engine default: 30s)
- `read_file.max_response_bytes` (engine default: 256 KB)
- `tool_loop.user_message_timeout_seconds` (engine default: 300s)

### Product Positioning (for docs tone)

From the epics file and Story 9.1c decision: _"Most AI tool launches in 2026 are slick fake demos. AgentRun being unapologetically real is differentiation."_ The docs should be direct, practical, and honest about limitations. No hype, no screenshots of cherry-picked outputs. The pull-quote about pasting your own URLs is deliberate — it signals "this works on your content, not our curated demo."

### Project Structure Notes

- Solution: `AgentRun.Umbraco.slnx` (3 projects)
- Package project: `AgentRun.Umbraco/AgentRun.Umbraco.csproj`
- Test project: `AgentRun.Umbraco.Tests/AgentRun.Umbraco.Tests.csproj`
- Dev host: `AgentRun.Umbraco.TestSite/AgentRun.Umbraco.TestSite.csproj`
- Frontend source: `AgentRun.Umbraco/Client/` (excluded from NuGet)
- Frontend output: `AgentRun.Umbraco/wwwroot/App_Plugins/AgentRunUmbraco/`
- Workflows source of truth: `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/`
- New in this story: `AgentRun.Umbraco/Workflows/` (package-included copy)
- New in this story: `README.md` (repo root)
- New in this story: `docs/workflow-authoring-guide.md`

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 9.3] — acceptance criteria and context
- [Source: _bmad-output/planning-artifacts/epics.md#Story 9.6] — tool tuning resolution chain, doc requirements for authoring guide
- [Source: _bmad-output/planning-artifacts/prd.md#FR68] — getting started guide requirements
- [Source: _bmad-output/planning-artifacts/prd.md#FR69] — workflow authoring guide requirements
- [Source: _bmad-output/planning-artifacts/prd.md#NFR19] — clean uninstall requirement
- [Source: _bmad-output/planning-artifacts/architecture.md#Package Structure] — RCL layout, NuGet content conventions
- [Source: _bmad-output/implementation-artifacts/9-2-json-schema-for-workflow-yaml.md] — schema location, EmbeddedResource config, yaml-language-server directive pattern
- [Source: _bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md] — "paste your own URLs" product framing
- [Source: _bmad-output/implementation-artifacts/9-4-accessibility-quick-scan-workflow.md] — second example workflow structure

### Previous Story Intelligence (from 9-2 and 9-4)

**From 9-2 (JSON Schema):**
- Schema is at `AgentRun.Umbraco/Schemas/workflow-schema.json`, marked as `EmbeddedResource`
- CQA workflow.yaml has a `# yaml-language-server: $schema=` directive on line 1 (relative path to schema)
- The schema uses draft 2020-12 — do not downgrade
- `additionalProperties: false` at all levels (deny-by-default)
- Two low-severity deferred findings (NormalizeForJson scalar coercion, null-block schema/validator drift) — do not address in this story

**From 9-4 (Accessibility Quick-Scan):**
- Second workflow at `accessibility-quick-scan/` with 2 steps (scanner + reporter)
- Same interactive mode and URL-input pattern as CQA
- FetchUrlTool exposes generic HTML primitives only — workflow-specific logic lives in agent prompts
- Tests at 465/465

**Git patterns:**
- Commit convention: `Story X.Y → description` or `Story X.Y: description`
- Recent .csproj changes: 9-2 added JsonSchema.Net to tests project, rename commit changed PackageId

## Dev Agent Record

### Round 1 — Amelia

#### Agent Model Used

Claude Opus 4.6

#### Completion Notes List

**CRITICAL HANDOFF NOTES (Amelia must fill these before Phase 1 is complete):**

1. **Workflow packaging path:** Workflows are packed as `<None>` items with `Pack="true"` and `PackagePath="contentFiles\any\any\App_Data\AgentRun.Umbraco\workflows\{workflow-name}"`. Each workflow subfolder is explicitly included (not a wildcard glob) to avoid picking up C# source files that coexist in the `Workflows/` directory. In the nupkg, workflows land at `contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/` and `.../accessibility-quick-scan/`. With PackageReference consumers, NuGet includes these as project items at `App_Data/AgentRun.Umbraco/workflows/` — however, they are **linked from the NuGet cache**, not physically copied to the project directory. The `WorkflowRegistry` needs files on disk at `{ContentRootPath}/App_Data/AgentRun.Umbraco/workflows/`. **Consumers will likely need to copy the workflow files from the NuGet cache to their project's `App_Data/` folder manually on first install.** The README should include this step. The NuGet cache path is typically `~/.nuget/packages/agentrun.umbraco/1.0.0-beta.1/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/`.

2. **Schema distribution decision:** Both embedded AND on disk. The schema remains an `EmbeddedResource` (compiled into the DLL for runtime validation by `WorkflowValidator`). It is also shipped as a `contentFiles` item at `contentFiles/any/any/App_Data/AgentRun.Umbraco/workflow-schema.json`, landing at `App_Data/AgentRun.Umbraco/workflow-schema.json` in the consumer's project. Same NuGet cache caveat as workflows — consumers may need to copy it from cache to get IDE autocomplete.

3. **yaml-language-server directive path:** `# yaml-language-server: $schema=../../workflow-schema.json` — this resolves from `App_Data/AgentRun.Umbraco/workflows/{workflow-name}/workflow.yaml` up two levels to `App_Data/AgentRun.Umbraco/workflow-schema.json`. Both shipped example workflows already have this directive on line 1.

4. **Any .csproj surprises:** The `Workflows/` folder in `AgentRun.Umbraco/` contains both C# source files (WorkflowRegistry.cs, etc.) and the example workflow subdirectories (content-quality-audit/, accessibility-quick-scan/). A wildcard `Workflows\**\*` glob would leak C# source into the nupkg's contentFiles. The .csproj uses explicit per-workflow includes instead: `Workflows\content-quality-audit\**\*` and `Workflows\accessibility-quick-scan\**\*`. If a third example workflow is added later, it needs its own `<None Include>` line. Also: `Content Remove="Workflows\**"` was added alongside the existing `Content Remove="Client\**"` to prevent the Razor SDK from auto-including workflow files as build content.

5. **Package verification result:** Confirmed nupkg contents:
   - `lib/net10.0/AgentRun.Umbraco.dll` (228 KB compiled backend)
   - `staticwebassets/App_Plugins/AgentRunUmbraco/` (7 JS bundles + 7 source maps + umbraco-package.json with version 1.0.0-beta.1)
   - `contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/` (workflow.yaml + agents/scanner.md, analyser.md, reporter.md)
   - `contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/` (workflow.yaml + agents/scanner.md, reporter.md)
   - `contentFiles/any/any/App_Data/AgentRun.Umbraco/workflow-schema.json` (8 KB JSON Schema)
   - `AgentRun.Umbraco.nuspec` — version 1.0.0-beta.1, description, authors, tags all correct
   - No C# source files in contentFiles (verified after fixing wildcard glob issue)

#### Change Log

- 2026-04-10: Round 1 (Amelia) — Added NuGet metadata (version, description, authors, tags), updated umbraco-package.json to 1.0.0-beta.1, added example workflow files to package project, configured contentFiles packaging for workflows and JSON schema, verified nupkg contents. All tests pass (465/465 backend, 162/162 frontend).

#### File List

**Modified:**
- `AgentRun.Umbraco/AgentRun.Umbraco.csproj` — Added NuGet metadata (Version, Description, Authors, PackageTags), Content Remove for Workflows, None Pack includes for workflow contentFiles and schema contentFiles
- `AgentRun.Umbraco/Client/public/umbraco-package.json` — Version updated from 0.0.0 to 1.0.0-beta.1

**Added:**
- `AgentRun.Umbraco/Workflows/content-quality-audit/workflow.yaml` — Example workflow (copied from TestSite, schema directive updated)
- `AgentRun.Umbraco/Workflows/content-quality-audit/agents/scanner.md` — Agent prompt
- `AgentRun.Umbraco/Workflows/content-quality-audit/agents/analyser.md` — Agent prompt
- `AgentRun.Umbraco/Workflows/content-quality-audit/agents/reporter.md` — Agent prompt
- `AgentRun.Umbraco/Workflows/accessibility-quick-scan/workflow.yaml` — Example workflow (copied from TestSite, schema directive updated)
- `AgentRun.Umbraco/Workflows/accessibility-quick-scan/agents/scanner.md` — Agent prompt
- `AgentRun.Umbraco/Workflows/accessibility-quick-scan/agents/reporter.md` — Agent prompt

**Generated (not committed):**
- `nupkg/AgentRun.Umbraco.1.0.0-beta.1.nupkg` — Built package for verification

### Round 2 — Paige

#### Agent Model Used

Claude Opus 4.6

#### Completion Notes List

1. **NFR19 verified:** Confirmed by codebase inspection — no EF/DbContext, no SQL, no config file writes. All runtime data confined to `App_Data/AgentRun.Umbraco/` (instances under `DataRootPath`, workflows under `WorkflowPath`). Uninstall section added to README.
2. **Manual copy step documented:** README Quick Start includes explicit `cp -r` commands (macOS/Linux + Windows PowerShell) for copying workflow files from NuGet cache to project, per Amelia's handoff note about contentFiles being linked not copied.
3. **Schema copy step documented:** Authoring guide IDE Validation Setup section includes the same NuGet cache copy pattern for `workflow-schema.json`, with the `# yaml-language-server: $schema=../../workflow-schema.json` directive path from Amelia's notes.
4. **Beta test plan created:** Separate `docs/beta-test-plan.md` with 12 sequential steps and 4 pass criteria for Adam's Round 3 manual E2E gate.
5. **README includes .csproj change:** `PackageReadmeFile` property and `<None Include>` for pack inclusion added — the one authorised .csproj edit per the handoff protocol.

#### Change Log

- 2026-04-10: Round 2 (Paige) — Created README.md with quick start, configuration, limitations, and uninstalling sections. Created docs/workflow-authoring-guide.md with full field reference, tool docs, tuning chain, IDE setup, and 2-step walkthrough. Created docs/beta-test-plan.md for manual E2E gate. Added PackageReadmeFile to .csproj.

#### File List

**Added:**
- `README.md` — Package README with quick start, configuration, known limitations, and uninstalling sections (AC7, AC8)
- `docs/workflow-authoring-guide.md` — Workflow authoring guide with field reference, tool docs, tuning values, IDE setup, and URL Summary walkthrough (AC9)
- `docs/beta-test-plan.md` — Manual E2E test plan for Round 3 fresh install smoke test (AC5, AC6)

**Modified:**
- `AgentRun.Umbraco/AgentRun.Umbraco.csproj` — Added `<PackageReadmeFile>` and `<None Include>` for README in nupkg (AC7)
- `_bmad-output/implementation-artifacts/9-3-documentation-and-nuget-packaging.md` — Marked Tasks 5-8 complete, filled Round 2 agent record
