# Story 1.1: Scaffold RCL Package Project

Status: done

## Story

As a developer,
I want a properly structured .NET RCL package project with a test project,
So that I have the foundation to build the workflow engine as a distributable NuGet package.

## Acceptance Criteria

1. **Given** the Umbraco extension template is installed, **When** the project is scaffolded with `dotnet new umbraco-extension -n Shallai.UmbracoAgentRunner`, **Then** a `Shallai.UmbracoAgentRunner.csproj` exists targeting .NET 10 with `Microsoft.NET.Sdk.Razor` SDK.
2. A `Shallai.UmbracoAgentRunner.Tests.csproj` exists with NUnit 4.5.1, NUnit3TestAdapter 6.2.0, and NSubstitute 5.3.0 dependencies.
3. A test Umbraco site project exists and references the RCL package project.
4. The solution builds successfully with `dotnet build`.
5. The RCL project contains the initial folder structure: `Composers/`, `Services/`, `Models/`, `Tools/`, `Engine/`, `Workflows/`, `Instances/`, `Security/`, `Configuration/`, `Endpoints/`.
6. A `GlobalUsings.cs` file contains shared using statements.
7. An `AgentRunnerComposer.cs` exists implementing `IComposer` with an empty DI registration skeleton.
8. An `AgentRunnerOptions.cs` exists with placeholder configuration properties (`DataRootPath`, `DefaultProfile`, `WorkflowPath`).

## Tasks / Subtasks

- [x] Task 1: Install/update Umbraco templates and scaffold the RCL project (AC: #1)
  - [x] 1.1 Run `dotnet new install Umbraco.Templates::17.2.2` to ensure latest templates
  - [x] 1.2 Run `dotnet new umbraco-extension -n Shallai.UmbracoAgentRunner --version 17.2.2` to scaffold the RCL
  - [x] 1.3 Verify `.csproj` targets `net10.0` with `Microsoft.NET.Sdk.Razor` SDK
  - [x] 1.4 Pin Umbraco PackageReferences to `17.2.2` (template defaults to `*` which is unstable for a package)
- [x] Task 2: Create test project (AC: #2)
  - [x] 2.1 Run `dotnet new nunit -n Shallai.UmbracoAgentRunner.Tests`
  - [x] 2.2 Add PackageReferences: `NUnit 4.5.1`, `NUnit3TestAdapter 6.2.0`, `NSubstitute 5.3.0`
  - [x] 2.3 Add ProjectReference to `Shallai.UmbracoAgentRunner`
  - [x] 2.4 Create `Fixtures/` folder with an empty `TestHelpers.cs`
- [x] Task 3: Create test Umbraco site and solution (AC: #3, #4)
  - [x] 3.1 Scaffold a test Umbraco site: `dotnet new umbraco -n Shallai.UmbracoAgentRunner.TestSite`
  - [x] 3.2 Add ProjectReference from test site to the RCL package project
  - [x] 3.3 Create `Shallai.UmbracoAgentRunner.slnx` and add all three projects (.NET 10 uses .slnx format)
  - [x] 3.4 Run `dotnet build` and verify success
- [x] Task 4: Create folder structure in RCL project (AC: #5)
  - [x] 4.1 Create directories: `Composers/`, `Services/`, `Models/`, `Models/ApiModels/`, `Models/Exceptions/`, `Tools/`, `Engine/`, `Workflows/`, `Instances/`, `Security/`, `Configuration/`, `Endpoints/`
  - [x] 4.2 Add `.gitkeep` to empty directories so they are tracked
- [x] Task 5: Create initial source files (AC: #6, #7, #8)
  - [x] 5.1 Create `GlobalUsings.cs` with shared using statements
  - [x] 5.2 Create `Composers/AgentRunnerComposer.cs` implementing `IComposer` with empty DI registration skeleton
  - [x] 5.3 Create `Configuration/AgentRunnerOptions.cs` with `DataRootPath`, `DefaultProfile`, `WorkflowPath` properties
- [x] Task 6: Clean up template scaffolding (AC: #1)
  - [x] 6.1 Remove template-generated example files that aren't needed (example controller, example API composer — keep only what aligns with our architecture)
  - [x] 6.2 Verify the `Client/` folder structure from template is intact (Vite + TypeScript toolchain — Story 1.2 will configure it)
  - [x] 6.3 Verify `umbraco-package.json` is present at `Client/public/umbraco-package.json`
- [x] Task 7: Final verification (AC: #4)
  - [x] 7.1 Run `dotnet build` — must succeed
  - [x] 7.2 Run `dotnet test` — must succeed (even if no meaningful tests yet)

## Dev Notes

### CRITICAL: Template Name Change

The epics and architecture documents reference `dotnet new umbracopackage-rcl`. This template name **no longer exists**. It was renamed to `umbraco-extension` in the Umbraco.Templates package. The correct command is:

```bash
dotnet new install Umbraco.Templates::17.2.2
dotnet new umbraco-extension -n Shallai.UmbracoAgentRunner --version 17.2.2
```

Template options:
- `--version 17.2.2` — pins Umbraco PackageReferences (default `*` is unstable)
- `-ex` / `--include-example` — DO NOT use, we don't want example code
- `-s` / `--support-pages-and-views` — DO NOT use, we don't need Razor pages/views

### What the Template Scaffolds

The `umbraco-extension` template generates a Razor Class Library with:

```
Shallai.UmbracoAgentRunner/
  Shallai.UmbracoAgentRunner.csproj    # Microsoft.NET.Sdk.Razor, net10.0
  Constants.cs
  README.txt
  Composers/
    ShallaiUmbracoAgentRunnerApiComposer.cs
  Controllers/
    ShallaiUmbracoAgentRunnerApiController.cs
    ShallaiUmbracoAgentRunnerApiControllerBase.cs
  Client/
    package.json
    tsconfig.json
    vite.config.ts
    public/umbraco-package.json
    scripts/generate-openapi.js
    src/
      bundle.manifests.ts
      entrypoints/
        entrypoint.ts
        manifest.ts
      api/
        (generated OpenAPI client files)
  wwwroot/   # empty — Vite builds output here
```

Key `.csproj` settings from template:
- SDK: `Microsoft.NET.Sdk.Razor`
- `StaticWebAssetBasePath` set to `/`
- `Client\**` excluded from Content (not packed into NuGet)
- PackageReferences: `Umbraco.Cms.Web.Website`, `Umbraco.Cms.Web.Common`, `Umbraco.Cms.Api.Common`, `Umbraco.Cms.Api.Management`

### What to Keep vs. Remove from Template

**Keep:**
- `.csproj` file and its SDK/target framework settings
- `Client/` folder entirely (Vite + TypeScript toolchain — Story 1.2 will configure further)
- `Client/public/umbraco-package.json` (package manifest)
- `wwwroot/` folder (build output target)

**Remove/Replace:**
- `Constants.cs` — not needed
- `README.txt` — not needed
- `Composers/ShallaiUmbracoAgentRunnerApiComposer.cs` — replace with our `AgentRunnerComposer.cs`
- `Controllers/` folder entirely — we use Minimal API endpoints, not controllers
- `Client/scripts/generate-openapi.js` — not needed (we define TypeScript types manually)
- `Client/src/api/` — remove generated OpenAPI client files (we'll write our own API client in Story 1.2+)

### Architecture Compliance

**Package name:** `Shallai.UmbracoAgentRunner`

**Engine boundary rule:** `Engine/`, `Workflows/`, `Instances/`, `Tools/`, `Security/`, `Models/` must have NO `using Umbraco.*` statements. Only `Composers/`, `Endpoints/`, and `Engine/ProfileResolver.cs` (future) may reference Umbraco types.

**Naming conventions:**
- Classes, interfaces, methods, properties: `PascalCase`
- Local variables, parameters: `camelCase`
- Private fields: `_camelCase` (underscore prefix)
- Constants: `PascalCase` (not SCREAMING_CASE)
- Interfaces: `I` prefix — `IWorkflowTool`, `IStepExecutor`
- All async methods suffixed with `Async`
- Always pass `CancellationToken` as last parameter
- Use `ConfigureAwait(false)` in library code (this is a NuGet package)

**DI registration pattern (AgentRunnerComposer):**
- Register interfaces, not concrete types: `services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>()`
- Scoped services for per-request state, singletons for stateless services
- Tools registered individually: `services.AddSingleton<IWorkflowTool, ReadFileTool>()`
- The skeleton should show this pattern in comments even though actual registrations come in later stories

**AgentRunnerOptions configuration properties:**
- `DataRootPath` — default: `{ContentRootPath}/App_Data/Shallai.UmbracoAgentRunner/instances/`
- `DefaultProfile` — default Umbraco.AI profile alias
- `WorkflowPath` — path to workflow definitions folder

### GlobalUsings.cs Content

Include these shared usings:

```csharp
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Text.Json;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.Extensions.Logging;
```

Do NOT include Umbraco usings in GlobalUsings — only engine-boundary files should import those.

### Test Project Structure

Mirror the source project structure:

```
Shallai.UmbracoAgentRunner.Tests/
  Engine/
  Workflows/
  Instances/
  Tools/
  Security/
  Fixtures/
    SampleWorkflows/   # will hold test YAML + agent markdown
    TestHelpers.cs     # shared test utilities
```

### Node.js Requirement

The `Client/` build toolchain requires Node.js 20.17.0+. This is not needed for Story 1.1 (we just verify the folder exists), but the dev agent should NOT attempt `npm install` or `npm run build` — that's Story 1.2.

### Project Structure Notes

- Alignment with unified project structure: all folder names match the architecture document exactly
- The solution structure is: RCL package + test project + test Umbraco site (3 projects)
- `wwwroot/App_Plugins/Shallai.UmbracoAgentRunner/` is the compiled frontend output location — created by Vite build in Story 1.2

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 1, Story 1.1]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure, Naming Conventions, DI Registration]
- [Source: _bmad-output/planning-artifacts/prd.md — Developer Tool Specific Requirements, Platform & Distribution]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md — Dashboard Shell Structure]
- [Source: Umbraco Docs — Umbraco Extension Template (template renamed from umbracopackage-rcl)]
- [Source: NuGet.org — Umbraco.Templates 17.2.2, NUnit 4.5.1, NSubstitute 5.3.0]

### Review Findings

- [x] [Review][Decision] AgentRunnerOptions defaults — resolved: use relative paths (`App_Data/...`) for DataRootPath and WorkflowPath, keep DefaultProfile as empty string. Composer will combine with ContentRootPath when binding is wired up. [Configuration/AgentRunnerOptions.cs]
- [x] [Review][Patch] Removed template leftover UnitTest1.cs [Shallai.UmbracoAgentRunner.Tests/UnitTest1.cs]
- [x] [Review][Patch] Removed redundant global usings — kept only System.Text.Json and Microsoft.Extensions.Logging (rest covered by ImplicitUsings) [Shallai.UmbracoAgentRunner/GlobalUsings.cs]
- [x] [Review][Defer] AgentRunnerOptions string properties are nullable-unaware despite `<Nullable>enable</Nullable>` — public setters accept null via deserialization with no guard. Address when options binding is wired up — deferred, pre-existing
- [x] [Review][Defer] DataRootPath trailing slash not normalised — consumer code may produce inconsistent path comparisons. Address when path resolution logic is implemented — deferred, pre-existing

## Dev Agent Record

### Agent Model Used
Claude Opus 4.6 (1M context)

### Debug Log References
- Build warning NU1902 (MimeKit vulnerability) — transitive dependency from Umbraco 17.2.2, not actionable
- .NET 10 SDK creates `.slnx` (XML solution format) instead of `.sln` — functionally equivalent, used as-is

### Code Review Notes (2026-03-30)
- Review model: Claude Opus 4.6 — Blind Hunter + Edge Case Hunter + Acceptance Auditor (3-layer parallel)
- DataRootPath default changed from `~/App_Data/...` to relative `App_Data/...` — `~` is a shell convention, not resolvable by System.IO. Relative path lets the composer combine with `ContentRootPath` at binding time.
- WorkflowPath default changed from `string.Empty` to `App_Data/Shallai.UmbracoAgentRunner/workflows/` — provides a sensible default matching DataRootPath convention.
- DefaultProfile kept as `string.Empty` — no universal Umbraco.AI profile alias exists to default to.
- Removed redundant global usings — `ImplicitUsings` already covers System, System.Collections.Generic, System.IO, System.Linq, System.Threading, System.Threading.Tasks. Only System.Text.Json and Microsoft.Extensions.Logging are additive.
- Removed template leftover UnitTest1.cs — inflated test count with no value.
- Tests updated to match new defaults. `dotnet build` 0 errors, `dotnet test` 2/2 passed.
- Deferred: nullable-unaware string properties (address when options binding lands), trailing slash normalisation (address when path resolution lands).

### Completion Notes List
- Task 1: Installed Umbraco.Templates 17.2.2 (upgraded from 17.1.0), scaffolded RCL with `umbraco-extension` template. Umbraco PackageReferences already pinned to 17.2.2 by `--version` flag.
- Task 2: Created NUnit test project with NUnit 4.5.1, NUnit3TestAdapter 6.2.0, NSubstitute 5.3.0. Added project reference to RCL. Created mirrored folder structure (Engine/, Workflows/, Instances/, Tools/, Security/, Fixtures/SampleWorkflows/).
- Task 3: Scaffolded test Umbraco site (17.2.2), added project reference to RCL, created Shallai.UmbracoAgentRunner.slnx with all 3 projects. Build succeeded.
- Task 4: Created all required directories with .gitkeep files for git tracking.
- Task 5: Created GlobalUsings.cs (no Umbraco usings per engine boundary rule), AgentRunnerComposer.cs (IComposer with commented DI registration patterns), AgentRunnerOptions.cs (DataRootPath, DefaultProfile, WorkflowPath). Added AgentRunnerOptionsTests with 2 tests — all pass.
- Task 6: Removed Constants.cs, README.txt, Controllers/, ShallaiUmbracoAgentRunnerApiComposer.cs, Client/scripts/generate-openapi.js, Client/src/api/. Verified Client/ folder and umbraco-package.json intact.
- Task 7: Final `dotnet build` — 0 errors. `dotnet test` — 3/3 passed.

### File List
- Shallai.UmbracoAgentRunner/Shallai.UmbracoAgentRunner.csproj (created by template)
- Shallai.UmbracoAgentRunner/GlobalUsings.cs (created)
- Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs (created)
- Shallai.UmbracoAgentRunner/Configuration/AgentRunnerOptions.cs (created)
- Shallai.UmbracoAgentRunner/Client/ (created by template, kept intact)
- Shallai.UmbracoAgentRunner/Services/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Models/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Models/ApiModels/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Models/Exceptions/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Tools/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Engine/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Workflows/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Instances/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Security/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Configuration/.gitkeep (created)
- Shallai.UmbracoAgentRunner/Endpoints/.gitkeep (created)
- Shallai.UmbracoAgentRunner.Tests/Shallai.UmbracoAgentRunner.Tests.csproj (created)
- Shallai.UmbracoAgentRunner.Tests/Configuration/AgentRunnerOptionsTests.cs (created)
- Shallai.UmbracoAgentRunner.Tests/Fixtures/TestHelpers.cs (created)
- Shallai.UmbracoAgentRunner.TestSite/Shallai.UmbracoAgentRunner.TestSite.csproj (created by template, project ref added)
- Shallai.UmbracoAgentRunner.slnx (created)
