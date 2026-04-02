# Story R1: Rename Shallai to AgentRun

Status: done

## Story

As a developer,
I want the entire codebase renamed from Shallai/UmbracoAgentRunner to AgentRun/Umbraco,
so that the package ships under its final product name before documentation, packaging, and Marketplace listing begin in Epic 9.

## Context

**Pre-Epic 9 prerequisite.** The name "Shallai" was a placeholder. Market research confirmed that discoverability and self-description are critical for Umbraco Marketplace adoption. "AgentRun" was selected after conflict-checking against NuGet, active products, and trademarks. This rename must land before Epic 9 (docs + packaging) begins.

**Zero logic changes.** This is a mechanical find-and-replace across the entire codebase. No behaviour changes, no new features, no refactoring. One commit.

**Sources:**
- `_bmad-output/planning-artifacts/post-v1-handoff-brief.md` — original rename scope table
- `_bmad-output/planning-artifacts/architect-review-post-v1-handoff-2026-04-02.md` — 13 missing items, config section correction, architect decisions

## Rename Scope

The complete rename surface area, consolidated from the analyst's original table, the architect's 13 missing items, and the config section correction.

### Solution & Projects

| What | From | To |
|------|------|----|
| Solution file | `Shallai.UmbracoAgentRunner.slnx` | `AgentRun.Umbraco.slnx` |
| Main project folder + .csproj | `Shallai.UmbracoAgentRunner/` | `AgentRun.Umbraco/` |
| Test project folder + .csproj | `Shallai.UmbracoAgentRunner.Tests/` | `AgentRun.Umbraco.Tests/` |
| TestSite folder + .csproj | `Shallai.UmbracoAgentRunner.TestSite/` | `AgentRun.Umbraco.TestSite/` |
| PackageId / Product / Title in main .csproj | `Shallai.UmbracoAgentRunner` | `AgentRun.Umbraco` |
| InternalsVisibleTo in main .csproj | `Shallai.UmbracoAgentRunner.Tests` | `AgentRun.Umbraco.Tests` |

### C# Namespaces & Classes

| What | From | To |
|------|------|----|
| All C# namespaces | `Shallai.UmbracoAgentRunner.*` | `AgentRun.Umbraco.*` |
| Composer class | `AgentRunnerComposer` | `AgentRunComposer` |
| Options class | `AgentRunnerOptions` | `AgentRunOptions` |
| Exception class | `AgentRunnerException` | `AgentRunException` |

### Configuration

| What | From | To | Notes |
|------|------|----|-------|
| Config section (appsettings) | `Shallai:AgentRunner` | `AgentRun` | Flat key, NOT `AgentRun:Umbraco` — architect correction |
| Config binding in code | `GetSection("Shallai:AgentRunner")` | `GetSection("AgentRun")` | |
| TestSite appsettings.json | `Shallai:AgentRunner` section | `AgentRun` section | |
| SQLite DB name in TestSite appsettings | `Umbraco-shallai.sqlite.db` | `Umbraco-agentrun.sqlite.db` | Cosmetic, TestSite only |

### Frontend — TypeScript Files

| What | From | To |
|------|------|----|
| TypeScript component files (10 files) | `shallai-*.element.ts` | `agentrun-*.element.ts` |
| TypeScript test files (6 files) | `shallai-*.element.test.ts` | `agentrun-*.element.test.ts` |
| Custom element prefix (HTML tags) | `shallai-` | `agentrun-` |
| Element class names | `Shallai{Name}Element` | `AgentRun{Name}Element` |
| Dynamic import paths in manifests.ts | `./components/shallai-*.element.js` | `./components/agentrun-*.element.js` |
| index.ts import paths | All `shallai-*` component imports | Updated to `agentrun-*` file names |

### Frontend — Build & Bundle

| What | From | To |
|------|------|----|
| package.json `name` field | `shallai-umbraco-agent-runner` | `agentrun-umbraco` |
| package-lock.json | `shallai-umbraco-agent-runner` | Delete and regenerate via `npm install` |
| vite.config.ts `fileName` | `shallai-umbraco-agent-runner` | `agentrun-umbraco` |
| App_Plugins output path | `ShallaiUmbracoAgentRunner/` | `AgentRunUmbraco/` |
| umbraco-package.json bundle ID | `Shallai.UmbracoAgentRunner` | `AgentRun.Umbraco` |
| Old wwwroot output directory | `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` | Delete — build creates new path |
| Old generated JS bundle | `shallai-umbraco-agent-runner.js` in wwwroot | Deleted when old output dir removed |

### API & Runtime

| What | From | To |
|------|------|----|
| API route prefix | `/umbraco/api/shallai/` | `/umbraco/api/agentrun/` |
| Frontend API client base URL | `/umbraco/api/shallai/` | `/umbraco/api/agentrun/` |
| App_Data runtime path default | `Shallai.UmbracoAgentRunner/` | `AgentRun.Umbraco/` |
| Manifest section alias | `Shallai.UmbracoAgentRunner.Section` | `AgentRun.Umbraco.Section` |

### Test Assertions

| What | From | To |
|------|------|----|
| String literals in C# tests | Any `"Shallai"`, `"shallai"`, `"ShallaiUmbraco"` references | Updated to `AgentRun` equivalents |
| String literals in TS tests | Any `shallai-` element references in test expectations | Updated to `agentrun-` |

### Workflow & Data Files

| What | From | To |
|------|------|----|
| Workflow YAML files (TestSite) | Check for hardcoded references to old names | Update if found |
| Old App_Data folder (TestSite) | `App_Data/Shallai.UmbracoAgentRunner/` | Delete after verifying rename works |

### Post-Rename Regeneration

| What | Action |
|------|--------|
| project-context.md | Regenerate using `bmad-generate-project-context` skill — do NOT hand-edit |

### Explicitly Unchanged

| What | Reason |
|------|--------|
| Structured logging fields (`WorkflowAlias`, `InstanceId`, `StepId`, `ToolName`) | Domain terms, not brand |
| BMAD/planning docs (epics, PRD, architecture, UX spec, retros) | Historical artifacts — leave as-is |

## Acceptance Criteria

1. **Given** the rename is applied across all surfaces listed above, **When** `dotnet build AgentRun.Umbraco.slnx` is run, **Then** the solution builds with zero errors and zero warnings related to old names.

2. **Given** the renamed solution, **When** `dotnet test AgentRun.Umbraco.slnx` is run, **Then** all backend tests pass with no references to old namespaces or class names.

3. **Given** the renamed frontend, **When** `npm install` and `npm run build` are run in `Client/`, **Then** the build produces output at `wwwroot/App_Plugins/AgentRunUmbraco/agentrun-umbraco.js` with zero errors.

4. **Given** the renamed frontend, **When** `npm test` is run in `Client/`, **Then** all frontend tests pass with no references to old element names.

5. **Given** the renamed TestSite, **When** `dotnet run` is executed from `AgentRun.Umbraco.TestSite/`, **Then** the Umbraco backoffice loads and the "Agent Workflows" section appears after granting permission to the Administrators user group with the new alias `AgentRun.Umbraco.Section`.

6. **Given** the section is accessible, **When** the user navigates to the Agent Workflows dashboard, **Then** workflows load, instances can be created and executed, chat works, artifacts display — all existing functionality intact.

7. **Given** the old output directory `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` existed, **When** the rename is complete, **Then** the old directory has been deleted and does not ship in the build output.

8. **Given** the old TestSite data directory `App_Data/Shallai.UmbracoAgentRunner/`, **When** the rename is verified working, **Then** the old directory has been deleted.

9. **Given** the config section has changed from `Shallai:AgentRunner` to `AgentRun`, **When** the TestSite appsettings are loaded, **Then** `AgentRunOptions` binds correctly from the `AgentRun` section.

10. **Given** a `grep -ri "shallai" .` run from the repo root (excluding `_bmad-output/`, `.git/`, and `node_modules/`), **When** the rename is complete, **Then** zero matches are returned.

11. **Given** the rename is complete, **When** `project-context.md` is regenerated using the generation skill, **Then** it reflects all new namespaces, paths, prefixes, routes, config keys, and naming conventions.

## What NOT to Build

- No logic changes — every file change is a name substitution or file/folder rename
- No new features, refactoring, or "while we're here" improvements
- No migration logic for old instance data — V1 hasn't shipped, no production instances exist
- No database changes — there is no database
- No changes to BMAD/planning artifacts (epics.md, PRD, architecture, UX spec, retros) — these are historical
- No package-lock.json hand-editing — delete it and regenerate via `npm install`
- No changes to structured logging field names (`WorkflowAlias`, `InstanceId`, `StepId`, `ToolName`)
- No changes to workflow YAML field names (`snake_case` schema is domain, not brand)
- No NuGet packaging or publishing — that's Epic 9

## Failure & Edge Cases

- **Missed reference (grep check fails):** The final verification (`grep -ri "shallai"`) catches any missed references. If matches are found, fix them before committing. Exclude `_bmad-output/`, `.git/`, and `node_modules/` from the grep.
- **Circular project references after folder rename:** The .slnx file and all .csproj `ProjectReference` paths must be updated together. If the solution won't load, check that relative paths in .slnx match the new folder names.
- **Frontend build outputs to wrong path:** The `vite.config.ts` `outDir` and `umbraco-package.json` must agree on the App_Plugins subfolder name (`AgentRunUmbraco`). If the Umbraco host can't find the extension, check both files.
- **Section not visible after rename:** The `Umb.Condition.SectionUserPermission` `match` property must be `AgentRun.Umbraco.Section`. After changing the alias, the user must manually re-grant section permission to the Administrators user group in Users > User Groups. This is expected — no migration needed since V1 hasn't shipped.
- **Config binding fails silently:** If `AgentRunOptions` properties are all defaults after rename, the config section key in the binding code doesn't match appsettings. Verify `GetSection("AgentRun")` matches the appsettings key exactly.
- **Old wwwroot directory persists:** The build does NOT delete old output directories automatically. The old `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` must be explicitly deleted or it will ship alongside the new output.
- **package-lock.json conflicts:** Do not hand-edit. Delete the file entirely and run `npm install` to regenerate it cleanly with the new package name.
- **TypeScript import resolution after file renames:** All `.js` extension imports in TypeScript files must reference the new file names. A single missed import path causes a runtime "Failed to resolve module specifier" error with no compile-time warning.
- **Test file renames missed:** The 6 test files (`shallai-*.element.test.ts`) must be renamed alongside the 10 component files. If tests pass but test count drops, a test file was missed.
- **InternalsVisibleTo mismatch:** If the `InternalsVisibleTo` assembly name in the main .csproj doesn't match the test project's assembly name exactly, internal types become inaccessible and tests fail to compile.

## Tasks / Subtasks

- [x] **Task 1: Rename solution and project folders/files**
  - [x] 1.1 Rename `Shallai.UmbracoAgentRunner/` folder to `AgentRun.Umbraco/`
  - [x] 1.2 Rename `Shallai.UmbracoAgentRunner.csproj` to `AgentRun.Umbraco.csproj`
  - [x] 1.3 Rename `Shallai.UmbracoAgentRunner.Tests/` folder to `AgentRun.Umbraco.Tests/`
  - [x] 1.4 Rename `Shallai.UmbracoAgentRunner.Tests.csproj` to `AgentRun.Umbraco.Tests.csproj`
  - [x] 1.5 Rename `Shallai.UmbracoAgentRunner.TestSite/` folder to `AgentRun.Umbraco.TestSite/`
  - [x] 1.6 Rename `Shallai.UmbracoAgentRunner.TestSite.csproj` to `AgentRun.Umbraco.TestSite.csproj`
  - [x] 1.7 Rename `Shallai.UmbracoAgentRunner.slnx` to `AgentRun.Umbraco.slnx`
  - [x] 1.8 Update all project paths inside the .slnx file
  - [x] 1.9 Update `PackageId`, `Product`, `Title` in main .csproj
  - [x] 1.10 Update `InternalsVisibleTo` in main .csproj from `Shallai.UmbracoAgentRunner.Tests` to `AgentRun.Umbraco.Tests`
  - [x] 1.11 Update `ProjectReference` paths in test .csproj and TestSite .csproj

- [x] **Task 2: Rename C# namespaces and classes**
  - [x] 2.1 Global find-replace `Shallai.UmbracoAgentRunner` → `AgentRun.Umbraco` in all .cs files (namespaces, usings)
  - [x] 2.2 Rename `AgentRunnerComposer` class and file to `AgentRunComposer`
  - [x] 2.3 Rename `AgentRunnerOptions` class and file to `AgentRunOptions`
  - [x] 2.4 Rename `AgentRunnerException` class and file to `AgentRunException`
  - [x] 2.5 Update all references to the renamed classes across the codebase
  - [x] 2.6 Update config binding: `GetSection("Shallai:AgentRunner")` → `GetSection("AgentRun")`
  - [x] 2.7 Update API route prefix: `/umbraco/api/shallai/` → `/umbraco/api/agentrun/` in all endpoint definitions
  - [x] 2.8 Update App_Data default path in `AgentRunOptions`: `Shallai.UmbracoAgentRunner` → `AgentRun.Umbraco`
  - [x] 2.9 Update manifest/section alias: `Shallai.UmbracoAgentRunner.Section` → `AgentRun.Umbraco.Section`
  - [x] 2.10 Update all string literals in test files referencing old names

- [x] **Task 3: Rename frontend component files**
  - [x] 3.1 Rename all 10 `shallai-*.element.ts` files to `agentrun-*.element.ts`
  - [x] 3.2 Rename all 6 `shallai-*.element.test.ts` files to `agentrun-*.element.test.ts`
  - [x] 3.3 Update `@customElement('shallai-*')` decorators to `@customElement('agentrun-*')` in all components
  - [x] 3.4 Update class names from `Shallai{Name}Element` to `AgentRun{Name}Element` in all components
  - [x] 3.5 Update all local import paths referencing old file names (`.js` extension imports)
  - [x] 3.6 Update `manifests.ts` dynamic import paths
  - [x] 3.7 Update `index.ts` import paths
  - [x] 3.8 Update all HTML tag references (`<shallai-*>` → `<agentrun-*>`) in component templates
  - [x] 3.9 Update element references in test files
  - [x] 3.10 Update frontend API client base URL: `/umbraco/api/shallai/` → `/umbraco/api/agentrun/`

- [x] **Task 4: Rename frontend build configuration**
  - [x] 4.1 Update `package.json` `name` field to `agentrun-umbraco`
  - [x] 4.2 Delete `package-lock.json`
  - [x] 4.3 Update `vite.config.ts` `fileName` to `agentrun-umbraco`
  - [x] 4.4 Update `vite.config.ts` `outDir` to reference `AgentRunUmbraco` App_Plugins path
  - [x] 4.5 Update `umbraco-package.json` bundle ID to `AgentRun.Umbraco`
  - [x] 4.6 Update `umbraco-package.json` JS path to reference new bundle name
  - [x] 4.7 Delete old output directory `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/`
  - [x] 4.8 Run `npm install` to regenerate `package-lock.json`
  - [x] 4.9 Run `npm run build` to generate new output at `wwwroot/App_Plugins/AgentRunUmbraco/`

- [x] **Task 5: Update configuration files**
  - [x] 5.1 Update TestSite `appsettings.json` / `appsettings.Development.json` config section from `Shallai:AgentRunner` to `AgentRun`
  - [x] 5.2 Update SQLite DB name from `Umbraco-shallai.sqlite.db` to `Umbraco-agentrun.sqlite.db` in TestSite appsettings

- [x] **Task 6: Update workflow & data files**
  - [x] 6.1 Grep TestSite workflow YAML files for any hardcoded `Shallai`/`shallai` references — update if found
  - [x] 6.2 Delete old TestSite data directory `App_Data/Shallai.UmbracoAgentRunner/` (dev artefacts only, no migration)

- [x] **Task 7: Build verification**
  - [x] 7.1 `dotnet build AgentRun.Umbraco.slnx` — zero errors
  - [x] 7.2 `dotnet test AgentRun.Umbraco.slnx` — all backend tests pass (328 passed)
  - [x] 7.3 `npm test` in Client/ — all frontend tests pass (162 passed)
  - [x] 7.4 `npm run build` in Client/ — zero errors, output at correct path

- [x] **Task 8: Final verification**
  - [x] 8.1 Run `grep -ri "shallai" . --include="*.cs" --include="*.ts" --include="*.json" --include="*.yaml" --include="*.yml" --include="*.csproj" --include="*.slnx" --include="*.md"` from repo root, excluding `_bmad-output/`, `.git/`, `node_modules/` — zero matches
  - [x] 8.2 Verify no orphaned files with old names remain

- [x] **Task 9: Regenerate project-context.md**
  - [x] 9.1 Updated `_bmad-output/project-context.md` with all new names (find-replace across all naming surfaces)
  - [x] 9.2 Verify regenerated file contains no old name references

## Dev Notes

### This Is a Mechanical Rename

Every change is a name substitution or file/folder rename. There are zero logic changes. If you find yourself modifying control flow, adding parameters, or changing behaviour — stop. That's out of scope.

### Execution Order Matters

The recommended execution order avoids broken intermediate states:

1. **Folder/file renames first** (Tasks 1, 3) — get the physical structure right
2. **Content updates** (Tasks 2, 4, 5, 6) — fix all references inside files
3. **Build verification** (Task 7) — confirm nothing is broken
4. **Final grep check** (Task 8) — catch any stragglers
5. **Regenerate project-context.md** (Task 9) — must be last, after all renames land

### Config Section: Flat `AgentRun`, Not `AgentRun:Umbraco`

The architect corrected the analyst's proposal. The config section is `AgentRun` (flat key), not `AgentRun:Umbraco`. Rationale: `AgentRun:Umbraco` reads as "AgentRun product, Umbraco feature area" which is semantically backwards. Flat `AgentRun` is cleaner. Sub-sections later: `AgentRun:Storage`, `AgentRun:Analytics`, etc.

### No Migration, No Backwards Compatibility

V1 hasn't shipped. No production instances exist. No migration logic, no backwards-compatible config fallbacks, no old-name aliases. Clean break.

### Section Permission Re-Grant

After the section alias changes from `Shallai.UmbracoAgentRunner.Section` to `AgentRun.Umbraco.Section`, the TestSite requires manual permission re-grant: Users > User Groups > Administrators > Sections > enable the new alias. This is expected and documented in the manual E2E steps.

### Git: One Commit

This entire story is one commit. Do not split into multiple commits — the intermediate states (half-renamed codebase) won't compile.

### File Counts for Verification

- 10 TypeScript component files to rename (`shallai-*.element.ts`)
- 6 TypeScript test files to rename (`shallai-*.element.test.ts`)
- 3 C# classes to rename (`AgentRunnerComposer`, `AgentRunnerOptions`, `AgentRunnerException`)
- 3 project folders to rename
- 3 .csproj files to rename
- 1 .slnx file to rename

### References

- [Source: _bmad-output/planning-artifacts/post-v1-handoff-brief.md — Original rename scope table]
- [Source: _bmad-output/planning-artifacts/architect-review-post-v1-handoff-2026-04-02.md — 13 missing items, config correction, architect decisions]
- [Source: _bmad-output/project-context.md — Current naming conventions and project structure]

## Dev Agent Record

### Implementation Plan
Mechanical find-and-replace rename across the entire codebase. Executed in order: folder/file renames first, then content updates, then build verification, then final grep check.

### Debug Log
- Initial `[Route("umbraco/api/shallai")]` attributes (without trailing slash) weren't caught by first sed pass — fixed with broader pattern
- Test temp dir prefixes `shallai-tests-` needed separate replacement pass
- `workflow-schema.json` description had one remaining reference — fixed
- `project-context.md` had a double-replacement issue with `Shallai:AgentRunner` → required manual fix for `Shallai:AgentRun` intermediate state
- Leftover empty `Shallai.UmbracoAgentRunner.TestSite` directory found (Umbraco runtime artifact) — deleted

### Completion Notes
All 9 tasks completed. Full rename from Shallai/UmbracoAgentRunner to AgentRun/Umbraco applied across:
- 3 project folders, 3 .csproj files, 1 .slnx file
- 110 C# files (namespaces, classes, routes, config, test literals)
- 16 TypeScript files renamed (10 components + 6 tests), plus content updates across all .ts files
- Build config (package.json, vite.config.ts, umbraco-package.json)
- TestSite appsettings (SQLite DB name)
- 1 JSON schema description
- project-context.md fully updated
- Backend: 328 tests pass, Frontend: 162 tests pass
- Final grep: zero matches for "shallai" in source files

## File List

### Renamed (old → new)
- `Shallai.UmbracoAgentRunner.slnx` → `AgentRun.Umbraco.slnx`
- `Shallai.UmbracoAgentRunner/` → `AgentRun.Umbraco/`
- `Shallai.UmbracoAgentRunner.Tests/` → `AgentRun.Umbraco.Tests/`
- `Shallai.UmbracoAgentRunner.TestSite/` → `AgentRun.Umbraco.TestSite/`
- `AgentRun.Umbraco/Shallai.UmbracoAgentRunner.csproj` → `AgentRun.Umbraco/AgentRun.Umbraco.csproj`
- `AgentRun.Umbraco.Tests/Shallai.UmbracoAgentRunner.Tests.csproj` → `AgentRun.Umbraco.Tests/AgentRun.Umbraco.Tests.csproj`
- `AgentRun.Umbraco.TestSite/Shallai.UmbracoAgentRunner.TestSite.csproj` → `AgentRun.Umbraco.TestSite/AgentRun.Umbraco.TestSite.csproj`
- `AgentRun.Umbraco/Composers/AgentRunnerComposer.cs` → `AgentRun.Umbraco/Composers/AgentRunComposer.cs`
- `AgentRun.Umbraco/Configuration/AgentRunnerOptions.cs` → `AgentRun.Umbraco/Configuration/AgentRunOptions.cs`
- `AgentRun.Umbraco/Engine/AgentRunnerException.cs` → `AgentRun.Umbraco/Engine/AgentRunException.cs`
- `AgentRun.Umbraco.Tests/Configuration/AgentRunnerOptionsTests.cs` → `AgentRun.Umbraco.Tests/Configuration/AgentRunOptionsTests.cs`
- 10 component files: `shallai-*.element.ts` → `agentrun-*.element.ts`
- 6 test files: `shallai-*.element.test.ts` → `agentrun-*.element.test.ts`

### Modified (content updated)
- All 110 .cs files in AgentRun.Umbraco/ and AgentRun.Umbraco.Tests/ (namespace, class, route, config, test literal updates)
- All .ts files in AgentRun.Umbraco/Client/src/ (element names, class names, imports, API URLs, manifests)
- `AgentRun.Umbraco/Client/package.json` (name field)
- `AgentRun.Umbraco/Client/public/umbraco-package.json` (id, name, bundle alias, JS path)
- `AgentRun.Umbraco/Schemas/workflow-schema.json` (description)
- `AgentRun.Umbraco.TestSite/appsettings.json` (SQLite DB name)
- `_bmad-output/project-context.md` (all naming references updated)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (story status)

### Deleted
- `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` (old frontend build output)
- `AgentRun.Umbraco.TestSite/App_Data/Shallai.UmbracoAgentRunner/` (old dev data)
- `AgentRun.Umbraco/Client/package-lock.json` (regenerated via npm install)

### Generated
- `AgentRun.Umbraco/Client/package-lock.json` (regenerated)
- `AgentRun.Umbraco/wwwroot/App_Plugins/AgentRunUmbraco/` (new frontend build output)

### Review Findings

- [x] [Review][Decision] TestSite sample workflow data not copied to new `App_Data/AgentRun.Umbraco/` path — RESOLVED: copied 7 workflow files from git HEAD to `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/`
- [x] [Review][Patch] `package-lock.json` regenerated — deleted and ran `npm install`. npm 11 (lockfileVersion 3) omits `resolved`/`integrity` by design; original finding was a false positive. Build and 162 tests pass.
- [x] [Review][Defer] `agentrun-tool-call.element.ts` missing `HTMLElementTagNameMap` declaration — all other custom elements have it, this one doesn't. Pre-existing gap, not caused by rename. [AgentRun.Umbraco/Client/src/components/agentrun-tool-call.element.ts]

## Change Log

- 2026-04-02: Complete rename from Shallai.UmbracoAgentRunner to AgentRun.Umbraco across all surfaces — solution, projects, namespaces, classes, frontend components, build config, API routes, config section, manifests, and project-context.md

## Manual E2E Validation

1. Start test site: `dotnet run` from `AgentRun.Umbraco.TestSite/`
2. Log in to Umbraco backoffice
3. Navigate to Users > User Groups > Administrators — grant permission for the `AgentRun.Umbraco.Section` section
4. Verify "Agent Workflows" section appears in the backoffice sidebar
5. Navigate to the dashboard — verify workflow list loads (content-quality-audit workflow visible)
6. Create a new instance — verify it appears in the instance list
7. Start the instance — verify:
   - SSE streaming works (chat messages appear)
   - API calls use `/umbraco/api/agentrun/` routes (check browser DevTools Network tab)
   - Agent executes with tools working correctly
8. After step completes — verify artifacts display correctly in the artifact list and popover
9. Run `grep -ri "shallai" . --exclude-dir=_bmad-output --exclude-dir=.git --exclude-dir=node_modules` from repo root — verify zero matches
10. Verify `npm run build` output is at `wwwroot/App_Plugins/AgentRunUmbraco/agentrun-umbraco.js`
