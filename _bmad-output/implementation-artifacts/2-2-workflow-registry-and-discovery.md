# Story 2.2: Workflow Registry & Discovery

Status: done

## Story

As a developer,
I want the engine to automatically discover workflow definitions from folders on disk at startup,
so that I can add new workflows by dropping a folder without any code registration.

## Acceptance Criteria

1. **Given** a configured workflows directory path in `AgentRunnerOptions.WorkflowPath`, **When** the Umbraco application starts, **Then** the `WorkflowRegistry` scans all immediate subdirectories for `workflow.yaml` files.

2. **Given** a subdirectory containing a valid `workflow.yaml`, **When** the registry loads it, **Then** the workflow is registered with its alias derived from the folder name (e.g., folder `content-quality-audit/` becomes alias `content-quality-audit`).

3. **Given** a workflow step that references an `agent` markdown file (e.g., `agents/content-gatherer.md`), **When** the registry loads the workflow, **Then** it verifies the agent file exists relative to the workflow folder and logs a warning if missing (workflow still loads — agent file absence is a warning, not a blocker).

4. **Given** a workflow folder containing a `workflow.yaml` with validation errors, **When** the registry attempts to load it, **Then** the errors are logged with clear messages but the invalid workflow is skipped — other valid workflows still load successfully.

5. **Given** the `IWorkflowRegistry` interface, **When** consumed by other services, **Then** it exposes `GetAllWorkflows()` returning all successfully loaded workflows, and `GetWorkflow(string alias)` returning a single workflow or null.

6. **Given** the `WorkflowRegistry` class, **When** registered in DI, **Then** it is registered as a singleton via `AgentRunnerComposer` alongside `IWorkflowParser` and `IWorkflowValidator`.

7. **Given** the unit test suite, **When** tests run, **Then** tests verify: discovery of multiple workflows from separate folders, handling of missing agent files (warning logged, workflow loads), graceful skip of invalid workflows alongside valid ones, alias derivation from folder name, empty workflows directory produces empty registry, and non-existent workflows directory is handled gracefully.

## Tasks / Subtasks

- [x] Task 1: Create `IWorkflowRegistry` interface (AC: 5)
  - [x] Create `Workflows/IWorkflowRegistry.cs` with `GetAllWorkflows()` and `GetWorkflow(string alias)` methods
  - [x] Return types: `IReadOnlyList<RegisteredWorkflow>` and `RegisteredWorkflow?`
- [x] Task 2: Create `RegisteredWorkflow` model (AC: 2)
  - [x] Create `Workflows/RegisteredWorkflow.cs` — wraps `WorkflowDefinition` with `Alias` (string) and `FolderPath` (string) properties
  - [x] This is a thin wrapper that adds runtime context (alias + folder path) to the parsed definition
- [x] Task 3: Create `WorkflowRegistry` implementation (AC: 1, 2, 3, 4)
  - [x] Create `Workflows/WorkflowRegistry.cs` implementing `IWorkflowRegistry`
  - [x] Constructor takes `IWorkflowParser`, `IWorkflowValidator`, `ILogger<WorkflowRegistry>`
  - [x] Implement `LoadWorkflowsAsync(string workflowsRootPath, CancellationToken)` — scans subdirectories, parses/validates each, stores valid ones
  - [x] Derive alias from folder name: `new DirectoryInfo(subDir).Name`
  - [x] For each valid workflow, verify agent markdown files exist relative to the workflow folder
  - [x] Log warnings for missing agent files (do not reject the workflow)
  - [x] Log errors for validation failures and skip the workflow
  - [x] Store workflows in a `Dictionary<string, RegisteredWorkflow>` keyed by alias
- [x] Task 4: Wire up DI registration in `AgentRunnerComposer` (AC: 6)
  - [x] Register `IWorkflowParser` / `WorkflowParser` as singleton
  - [x] Register `IWorkflowValidator` / `WorkflowValidator` as singleton
  - [x] Register `IWorkflowRegistry` / `WorkflowRegistry` as singleton
  - [x] Uncomment and configure `AgentRunnerOptions` binding from `Shallai:AgentRunner` config section
  - [x] Call `registry.LoadWorkflowsAsync()` — use an `INotificationHandler<UmbracoApplicationStartedNotification>` to trigger loading after startup
- [x] Task 5: Create startup notification handler (AC: 1)
  - [x] Create `Composers/WorkflowRegistryInitializer.cs` implementing `INotificationHandler<UmbracoApplicationStartedNotification>`
  - [x] Inject `IWorkflowRegistry`, `IOptions<AgentRunnerOptions>`, `IWebHostEnvironment`, `ILogger`
  - [x] Resolve absolute workflows path: `Path.Combine(webHostEnvironment.ContentRootPath, options.WorkflowPath)`
  - [x] Call `LoadWorkflowsAsync()` on the registry
  - [x] Register handler in `AgentRunnerComposer`: `builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WorkflowRegistryInitializer>()`
- [x] Task 6: Write unit tests (AC: 7)
  - [x] Create `Workflows/WorkflowRegistryTests.cs` in test project
  - [x] Tests use temp directories with mocked parser/validator (NSubstitute) — no static fixture folders needed
  - [x] Test: multiple valid workflows discovered from separate subdirectories
  - [x] Test: alias derived from folder name correctly
  - [x] Test: missing agent markdown file logs warning, workflow still loads
  - [x] Test: invalid workflow.yaml is skipped, valid workflows still load
  - [x] Test: empty workflows directory returns empty list
  - [x] Test: non-existent workflows directory handled gracefully (no throw)
  - [x] Test: `GetWorkflow()` returns null for unknown alias
  - [x] Test: `GetAllWorkflows()` returns all loaded workflows

## Dev Notes

### Architecture & Boundaries

- **`WorkflowRegistry` lives in `Workflows/` folder** — this is within the engine boundary (pure .NET, zero Umbraco dependencies)
- `WorkflowRegistry` depends only on: `IWorkflowParser`, `IWorkflowValidator`, `ILogger<T>`, `System.IO` — nothing from Umbraco
- **`WorkflowRegistryInitializer` lives in `Composers/` folder** — this is the Umbraco integration boundary because it references `IWebHostEnvironment`, `UmbracoApplicationStartedNotification`, and `IOptions<AgentRunnerOptions>`
- The registry itself does NOT know about `AgentRunnerOptions` or `IWebHostEnvironment` — it receives the resolved path string. This keeps the engine boundary clean.
- `AgentRunnerComposer` is the single DI registration point — all new registrations go here

### WorkflowRegistry Design

- **Singleton lifetime** — workflows are loaded once at startup and cached for the application lifetime
- `LoadWorkflowsAsync` is called once by the notification handler, not on every `GetWorkflow` call
- Internal storage: `Dictionary<string, RegisteredWorkflow>` — keyed by alias for O(1) lookup
- The registry is NOT responsible for hot-reloading workflows if files change on disk (that's a future concern, not this story)
- Thread safety: the dictionary is populated once at startup before any reads occur, so no concurrent access concerns during loading. After loading, reads are thread-safe on a frozen dictionary.

### Agent File Verification

- Each step's `Agent` property is a relative path (e.g., `agents/content-gatherer.md`)
- Resolve against the workflow folder: `Path.Combine(workflowFolderPath, step.Agent)`
- Check `File.Exists()` — if missing, log `LogWarning("Workflow '{Alias}': agent file '{AgentPath}' referenced by step '{StepId}' not found", ...)`
- **Do NOT reject the workflow** for missing agent files — the file might not exist yet during development, and it will be caught at step execution time anyway
- Use structured logging field names: `WorkflowAlias`, `AgentPath`, `StepId` (matching project-context.md conventions)

### Folder Scanning Logic

```
WorkflowPath (e.g., App_Data/Shallai.UmbracoAgentRunner/workflows/)
├── content-quality-audit/          → alias: "content-quality-audit"
│   ├── workflow.yaml               → parsed + validated
│   └── agents/
│       ├── content-gatherer.md     → verified exists
│       └── quality-analyser.md     → verified exists
├── broken-workflow/                → alias: "broken-workflow"
│   └── workflow.yaml               → validation fails → skipped, logged
└── another-workflow/               → alias: "another-workflow"
    └── workflow.yaml               → parsed + validated
```

- Use `Directory.GetDirectories(workflowsRootPath)` to list subdirectories
- For each subdirectory, look for `workflow.yaml` (exact filename, case-sensitive on Linux)
- Skip subdirectories with no `workflow.yaml` silently (not an error — might be a non-workflow folder)

### Error Handling

- **Workflows directory doesn't exist:** Log information message, return empty registry. Do NOT throw — the directory might not exist yet in a fresh installation.
- **Parse failure:** Catch `InvalidOperationException` from `WorkflowParser.Parse()`, log error with folder name, skip
- **Validation failure:** Log each `WorkflowValidationError` from the result, skip the workflow
- **File I/O errors:** Catch and log `IOException` / `UnauthorizedAccessException` per-workflow, skip, continue

### DI Registration Order

In `AgentRunnerComposer.Compose()`:
```csharp
// Configuration
builder.Services.Configure<AgentRunnerOptions>(
    builder.Config.GetSection("Shallai:AgentRunner"));

// Workflow services (singletons — stateless parsers + startup-loaded registry)
builder.Services.AddSingleton<IWorkflowParser, WorkflowParser>();
builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
builder.Services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>();

// Startup handler
builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WorkflowRegistryInitializer>();
```

### Async Patterns

- `LoadWorkflowsAsync` accepts `CancellationToken` as last parameter
- File I/O uses `File.ReadAllTextAsync` with cancellation token
- Use `ConfigureAwait(false)` on all awaits (this is library code, per architecture rules)
- `GetAllWorkflows()` and `GetWorkflow()` are synchronous — they read from the in-memory dictionary, no I/O

### Existing Code to Build On

- `WorkflowParser` at `Workflows/WorkflowParser.cs` — call `Parse(yamlContent)` to get `WorkflowDefinition`
- `WorkflowValidator` at `Workflows/WorkflowValidator.cs` — call `Validate(yamlContent)` to get `WorkflowValidationResult`
- `WorkflowValidationResult.IsValid` and `.Errors` for checking validation outcome
- `AgentRunnerOptions.WorkflowPath` at `Configuration/AgentRunnerOptions.cs` — relative path, defaults to `App_Data/Shallai.UmbracoAgentRunner/workflows/`
- `AgentRunnerComposer` at `Composers/AgentRunnerComposer.cs` — has commented-out registration patterns ready to uncomment
- `StepDefinition.Agent` — string property containing relative path to agent .md file

### Testing Strategy

- **Do NOT use the real filesystem via temp directories** — create a test helper that sets up a virtual workflow folder structure, OR use the real filesystem with `Path.GetTempPath()` and cleanup
- **Pragmatic approach:** Use `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())` to create isolated temp directories per test, populate with real files, clean up in teardown
- Use `NSubstitute` to mock `IWorkflowParser` and `IWorkflowValidator` for unit tests — the parser/validator already have their own tests from Story 2.1
- Use `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>` or `NSubstitute` mock logger to verify log calls
- Test file names mirror source: `Workflows/WorkflowRegistryTests.cs`
- NUnit 4 only: `[TestFixture]`, `[Test]`, `[SetUp]`, `[TearDown]`, `Assert.That()`

### Deferred Work Awareness

From prior code reviews (tracked in `deferred-work.md`):
- `AgentRunnerOptions` string properties don't guard against null — this story wires up options binding, so null/empty `WorkflowPath` should be handled defensively in the initializer (check `string.IsNullOrWhiteSpace` before calling `LoadWorkflowsAsync`)
- `DataRootPath` trailing slash not normalised — not this story's concern, but be aware when constructing paths

### What NOT to Build

- **NOT** API endpoints for listing workflows — that's Story 2.3
- **NOT** frontend components — Story 2.3
- **NOT** hot-reload / file watcher for workflow changes — future feature, not scoped
- **NOT** profile resolution or validation — Story 4.2
- **NOT** tool name validation — Story 5.4
- **NOT** workflow execution — Epic 4
- **NOT** typed exceptions (`WorkflowNotFoundException`) — will be needed by API layer in Story 2.3, not by the registry itself
- Keep the registry focused: scan, parse, validate, store, serve

### Project Structure Notes

- New files align with architecture: `Workflows/IWorkflowRegistry.cs`, `Workflows/WorkflowRegistry.cs`, `Workflows/RegisteredWorkflow.cs`
- New file in integration boundary: `Composers/WorkflowRegistryInitializer.cs`
- Modified file: `Composers/AgentRunnerComposer.cs` (uncomment registrations, add new ones)
- Namespace: `Shallai.UmbracoAgentRunner.Workflows` for registry and model
- Namespace: `Shallai.UmbracoAgentRunner.Composers` for initializer
- Test namespace: `Shallai.UmbracoAgentRunner.Tests.Workflows`

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 2, Story 2.2, lines 371-387]
- [Source: _bmad-output/planning-artifacts/architecture.md — DI Registration pattern, lines 400-404]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure, lines 620-626]
- [Source: _bmad-output/planning-artifacts/architecture.md — Engine Boundary, lines 734-750]
- [Source: _bmad-output/planning-artifacts/architecture.md — Async Patterns, lines 394-398]
- [Source: _bmad-output/planning-artifacts/prd.md — FR1, FR3, FR4]
- [Source: _bmad-output/project-context.md — Error handling, structured logging field names, DI registration via IComposer]
- [Source: _bmad-output/implementation-artifacts/2-1-workflow-yaml-parsing-and-validation.md — Previous story completion notes and review findings]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — AgentRunnerOptions null guards, trailing slash normalisation]

### Review Findings

- [x] [Review][Patch] Initializer Handle needs try/catch — unhandled exception from LoadWorkflowsAsync (e.g. Directory.GetDirectories throws on root path permissions) propagates into Umbraco notification pipeline, could crash startup [WorkflowRegistryInitializer.cs:42] — fixed
- [x] [Review][Patch] Per-workflow catch blocks miss unexpected exception types — if validator or parser throws something other than InvalidOperationException/IOException/UnauthorizedAccessException, it escapes the foreach loop and aborts loading all remaining workflows [WorkflowRegistry.cs:68-75] — fixed
- [x] [Review][Patch] Missing test for IOException/UnauthorizedAccessException error paths — these are explicitly implemented catch blocks with no test coverage [WorkflowRegistryTests.cs] — fixed
- [x] [Review][Defer] LoadWorkflowsAsync on IWorkflowRegistry interface — AC5 specifies only GetAllWorkflows/GetWorkflow; load method exposes mutation to all consumers — deferred, design simplification for future story
- [x] [Review][Defer] Path traversal via step.Agent in VerifyAgentFiles — Path.Combine with unsanitised relative path could resolve outside workflow folder; currently only File.Exists (info disclosure risk) — deferred, addressed by Story 5.2 path sandboxing
- [x] [Review][Defer] RegisteredWorkflow wraps mutable WorkflowDefinition — sealed class with get-only props but Definition has public setters, consumers can mutate singleton state — deferred, pre-existing from Story 2.1

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

### Completion Notes List

- All 7 ACs satisfied — registry discovers workflows from subdirectories, derives alias from folder name, verifies agent files with warnings, skips invalid workflows gracefully, exposes GetAllWorkflows/GetWorkflow, registered as singleton via AgentRunnerComposer, 11 unit tests all passing.
- `WorkflowRegistryInitializer` handles null/empty `WorkflowPath` defensively per deferred-work.md guidance.
- Initializer also handles already-absolute paths via `Path.IsPathRooted` check.
- Tests use isolated temp directories with NSubstitute mocks for parser/validator — no static fixture folders needed for registry tests.
- UmbracoApplicationStartedNotification handler is synchronous — used `GetAwaiter().GetResult()` since it runs once at startup before requests.
- **Code review fixes (2026-03-30):** Added try/catch in `WorkflowRegistryInitializer.Handle` so bad workflow directory can't crash Umbraco startup. Added `catch (Exception ex) when (ex is not OperationCanceledException)` at the per-workflow level in `WorkflowRegistry.LoadWorkflowsAsync` so unexpected exceptions from validator/parser don't abort loading of remaining workflows. Added 2 tests: IOException skip + unexpected exception skip. Total tests now 14.

### File List

- `Shallai.UmbracoAgentRunner/Workflows/IWorkflowRegistry.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/RegisteredWorkflow.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/WorkflowRegistry.cs` (new)
- `Shallai.UmbracoAgentRunner/Composers/WorkflowRegistryInitializer.cs` (new)
- `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` (modified)
- `Shallai.UmbracoAgentRunner.Tests/Workflows/WorkflowRegistryTests.cs` (new)
