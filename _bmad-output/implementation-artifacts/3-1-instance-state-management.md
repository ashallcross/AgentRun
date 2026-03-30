# Story 3.1: Instance State Management

Status: done

## Story

As a developer,
I want the engine to manage workflow instance state on disk with atomic writes,
so that instance state survives app pool restarts and is never corrupted by partial writes.

## Acceptance Criteria

1. **Given** a valid workflow definition exists in the registry, **When** a new instance is created via the `IInstanceManager`, **Then** an instance folder is created at `{DataRootPath}/{workflowAlias}/{instanceId}/` and an `instance.yaml` file is written containing `workflowAlias`, `currentStepIndex` (0), `status` (pending), `steps` array with per-step status (all pending), `createdAt`, `updatedAt`, and `createdBy`.

2. **Given** any write to `instance.yaml`, **When** persisted, **Then** the write uses the atomic temp file + rename pattern: write to `instance.yaml.tmp`, then `File.Move(tmp, target, overwrite: true)` (NFR20).

3. **Given** an instance exists on disk, **When** `GetInstanceAsync` is called with its ID, **Then** the `InstanceManager` reads and deserialises `instance.yaml` back into an `InstanceState` model.

4. **Given** an instance in any state, **When** `UpdateStepStatusAsync` is called, **Then** the step status is updated (pending, active, complete, error), `updatedAt` is set to the current UTC time, and the change is persisted atomically.

5. **Given** a request to start execution on an instance, **When** the instance is already in `running` status, **Then** the `InstanceManager` rejects the request to prevent concurrent execution (FR52).

6. **Given** `AgentRunnerOptions`, **When** `DefaultProfile` is set, **Then** it is available for profile resolution fallback (FR54). (Note: the actual fallback chain is Story 4.2 — this story only ensures the config property exists and is accessible.)

7. **Given** an instance with status `running` and at least one step with status `complete`, **When** the instance is loaded after an app pool restart, **Then** it can be identified as interrupted and resumed from the last completed step (FR10, NFR21). (Note: actual resume execution is Story 4.5 — this story ensures the state model supports detection.)

8. **Given** the `IInstanceManager` interface, **When** the application starts, **Then** it is registered as a singleton in `AgentRunnerComposer`.

9. **Given** all of the above, **When** the test suite runs, **Then** unit tests verify: creation, read-back, update, atomic writes, concurrent execution prevention, listing instances, and interrupted-instance detection.

## What NOT to Build

- No API endpoints — that is Story 3.2
- No UI components — that is Story 3.3
- No conversation persistence (JSONL) — that is a separate concern within `ConversationStore`
- No actual step execution or resume logic — that is Epic 4
- No profile resolution chain — just ensure `DefaultProfile` config property exists (Story 4.2 handles the chain)

## Architecture Reference

### File Placement (from architecture.md)

```
Shallai.UmbracoAgentRunner/
  Instances/
    IInstanceManager.cs        # Interface — create, read, update, list, delete instance state
    InstanceManager.cs         # Disk-based state management with atomic writes
    InstanceState.cs           # Model — instance.yaml schema
    StepStatus.cs              # Enum — pending, active, complete, error

Shallai.UmbracoAgentRunner.Tests/
  Instances/
    InstanceManagerTests.cs
```

### instance.yaml Schema (from architecture.md)

```yaml
workflowAlias: content-quality-audit
currentStepIndex: 0
status: pending          # pending | running | completed | failed | cancelled
steps:
  - id: scan-content
    status: pending      # pending | active | complete | error
    startedAt: null
    completedAt: null
  - id: generate-report
    status: pending
    startedAt: null
    completedAt: null
createdAt: "2026-03-30T10:00:00Z"
updatedAt: "2026-03-30T10:00:00Z"
createdBy: admin@example.com
```

### Instance Folder Structure

```
{DataRootPath}/{workflowAlias}/{instanceId}/
  instance.yaml
```

`DataRootPath` default: `App_Data/Shallai.UmbracoAgentRunner/instances/` (relative to ContentRootPath).
Configured via `AgentRunnerOptions.DataRootPath`.

### InstanceState Enum Values

- **Instance status:** `pending`, `running`, `completed`, `failed`, `cancelled`
- **Step status:** `pending`, `active`, `complete`, `error`

### Concurrency Prevention

The `InstanceManager` must prevent concurrent execution on the same instance. When starting execution:
1. Check `instance.status` — if already `running`, reject the request
2. Set status to `running` atomically before returning

This is an in-process guard (single server). File locking is not required — the singleton `InstanceManager` is the single point of mutation.

## Existing Code Context

### AgentRunnerOptions (Configuration/AgentRunnerOptions.cs)

Already has `DataRootPath` (default `"App_Data/Shallai.UmbracoAgentRunner/instances/"`) and `DefaultProfile` (default `string.Empty`). No changes needed to this file.

### AgentRunnerComposer (Composers/AgentRunnerComposer.cs)

Currently registers `IWorkflowParser`, `IWorkflowValidator`, `IWorkflowRegistry`. Add `IInstanceManager` → `InstanceManager` as singleton here.

### WorkflowDefinition (Workflows/WorkflowDefinition.cs)

Has `Steps` list of `StepDefinition` — each step has `Id` (string), `Name` (string). The `InstanceState.Steps` array should reference step IDs from the workflow definition.

### Namespace Convention

`Shallai.UmbracoAgentRunner.Instances` for all files in the `Instances/` folder.

## Tasks / Subtasks

- [x] Task 1: Create `StepStatus` enum (AC: 4)
  - [x] Create `Instances/StepStatus.cs` with values: `Pending`, `Active`, `Complete`, `Error`
  - [x] Use `[YamlMember(Alias = "...")]` or configure snake_case naming convention for YAML serialisation

- [x] Task 2: Create `InstanceStatus` enum
  - [x] Create `Instances/InstanceStatus.cs` with values: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`
  - [x] Same YAML serialisation convention as `StepStatus`

- [x] Task 3: Create `InstanceState` model (AC: 1, 3)
  - [x] Create `Instances/InstanceState.cs` with properties matching the instance.yaml schema above
  - [x] `WorkflowAlias` (string), `CurrentStepIndex` (int), `Status` (InstanceStatus), `Steps` (List of step state objects), `CreatedAt` (DateTime), `UpdatedAt` (DateTime), `CreatedBy` (string)
  - [x] Step state object: `Id` (string), `Status` (StepStatus), `StartedAt` (DateTime?), `CompletedAt` (DateTime?)
  - [x] Configure YamlDotNet snake_case naming convention or use `[YamlMember(Alias = "...")]` attributes — YAML output must use `snake_case`
  - [x] Quote date-like values in YAML output to avoid date coercion (see project-context.md lesson)

- [x] Task 4: Create `IInstanceManager` interface (AC: 1, 3, 4, 5, 8)
  - [x] Create `Instances/IInstanceManager.cs`
  - [x] Methods: `CreateInstanceAsync(string workflowAlias, WorkflowDefinition definition, string createdBy, CancellationToken)` → `InstanceState`
  - [x] `GetInstanceAsync(string workflowAlias, string instanceId, CancellationToken)` → `InstanceState?`
  - [x] `ListInstancesAsync(string? workflowAlias, CancellationToken)` → `IReadOnlyList<InstanceState>`
  - [x] `UpdateStepStatusAsync(string workflowAlias, string instanceId, int stepIndex, StepStatus status, CancellationToken)` → `InstanceState`
  - [x] `SetInstanceStatusAsync(string workflowAlias, string instanceId, InstanceStatus status, CancellationToken)` → `InstanceState`
  - [x] `DeleteInstanceAsync(string workflowAlias, string instanceId, CancellationToken)` → `bool`

- [x] Task 5: Implement `InstanceManager` (AC: 1, 2, 3, 4, 5, 7)
  - [x] Create `Instances/InstanceManager.cs` implementing `IInstanceManager`
  - [x] Constructor injects `IOptions<AgentRunnerOptions>` and `ILogger<InstanceManager>`
  - [x] Resolve `DataRootPath` relative to `ContentRootPath` if not rooted — inject `IWebHostEnvironment` for `ContentRootPath`
  - [x] `CreateInstanceAsync`: generate `Guid.NewGuid().ToString("N")` for instance ID, create folder, build `InstanceState` from `WorkflowDefinition.Steps`, write atomically
  - [x] `GetInstanceAsync`: read and deserialise `instance.yaml` from `{DataRootPath}/{workflowAlias}/{instanceId}/`
  - [x] `ListInstancesAsync`: enumerate instance folders under `{DataRootPath}/{workflowAlias}/` (or all workflow folders if alias is null), read each `instance.yaml`
  - [x] `UpdateStepStatusAsync`: read state, update step, set `updatedAt`, write atomically. Also set step `startedAt` when transitioning to `Active`, and `completedAt` when transitioning to `Complete`
  - [x] `SetInstanceStatusAsync`: read state, update status, set `updatedAt`, write atomically. If setting to `Running`, check current status is not already `Running` — throw `InvalidOperationException` if so (AC5)
  - [x] `DeleteInstanceAsync`: verify instance is `Completed`, `Failed`, or `Cancelled` before deleting folder
  - [x] All YAML serialisation via `YamlDotNet` with `CamelCaseNamingConvention` mapped to snake_case (use `UnderscoreNamingConvention` or `HyphenatedNamingConvention` — verify which produces `snake_case`)
  - [x] Atomic write helper: private method that writes to `.tmp` then `File.Move`

- [x] Task 6: Register in AgentRunnerComposer (AC: 8)
  - [x] Add `builder.Services.AddSingleton<IInstanceManager, InstanceManager>();` to `AgentRunnerComposer`

- [x] Task 7: Unit tests (AC: 9)
  - [x] Create `Instances/InstanceManagerTests.cs` in the test project
  - [x] Use a temp directory (`Path.GetTempPath()` + unique folder) for `DataRootPath` — clean up in `[TearDown]`
  - [x] Test: create instance produces correct folder structure and `instance.yaml` content
  - [x] Test: `instance.yaml` uses snake_case field names
  - [x] Test: read-back returns equivalent state to what was written
  - [x] Test: update step status persists correctly and updates `updatedAt`
  - [x] Test: setting status to `Running` when already `Running` throws `InvalidOperationException`
  - [x] Test: list instances returns all instances for a workflow alias
  - [x] Test: list instances with null alias returns instances across all workflows
  - [x] Test: delete instance removes folder (for completed/cancelled instances)
  - [x] Test: delete instance rejects deletion of running/pending instances
  - [x] Test: interrupted instance detection — instance with `Running` status and at least one `Complete` step can be identified
  - [x] Test: atomic writes — verify `.tmp` file does not persist after successful write (i.e. only `instance.yaml` exists)
  - [x] NUnit 4 attributes: `[TestFixture]`, `[Test]`, `Assert.That()`

## Dev Notes

- The `Instances/` and `Engine/` folders already exist with `.gitkeep` files — remove `.gitkeep` when adding real files.
- `IWebHostEnvironment` is available via DI in Umbraco — use `ContentRootPath` to resolve relative `DataRootPath`.
- For YAML serialisation, use `YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance` — this converts `PascalCase` → `snake_case`.
- Date values in YAML: YamlDotNet will serialise `DateTime` as ISO format by default. The project-context.md warns about YAML date coercion — since we control both serialisation and deserialisation, ensure the deserialiser is configured to handle ISO dates correctly.
- Instance ID format: `Guid.NewGuid().ToString("N")` produces a 32-char hex string without dashes — compact and filesystem-safe.
- The `InstanceManager` is a singleton but deals with file I/O. No in-memory caching of instance state is needed — always read from disk for consistency (instances may be modified by background step execution).

### Review Insights for Future Stories

- **Path sandboxing is mandatory for any method accepting user-supplied path segments.** `GetInstanceDirectory` was the first real consumer and needed a `Path.GetFullPath` + `StartsWith` guard. Every future method that builds filesystem paths from external input (API parameters, workflow YAML values) must apply the same pattern. The `_dataRootPath` trailing separator normalisation in the constructor is critical — without it, `StartsWith` can match partial directory names.
- **ListInstances resilience pattern.** Any enumeration over disk-based state files must catch per-item exceptions. A single corrupt YAML file should never prevent the dashboard from loading. Log a warning and skip — don't propagate.
- **Read-modify-write on disk state needs locking before Epic 4.** The singleton `InstanceManager` has no per-instance locking. Currently safe because only one caller chain exists, but the step executor (Story 4.3) will introduce concurrent read-modify-write on the same instance. Add `ConcurrentDictionary<string, SemaphoreSlim>` keyed by `{workflowAlias}/{instanceId}` before that story ships.
- **`CurrentStepIndex` advancement is Epic 4's responsibility.** The field exists on `InstanceState` and serialises correctly, but is never incremented by `UpdateStepStatusAsync`. Story 4.3/4.5 must own this logic.
- **Delete allows all terminal statuses (Completed, Failed, Cancelled).** This is a deliberate extension beyond the original spec's "completed/cancelled" wording. Failed is terminal and should be deletable.

## Deferred Work to Watch

From previous code reviews (see `deferred-work.md`):
- `AgentRunnerOptions` string properties are nullable-unaware — be aware when using `DataRootPath` that it could theoretically be null via misconfigured deserialization. Defensive null check is appropriate here.
- `DataRootPath` trailing slash not normalised — normalise the path when resolving it in the `InstanceManager` constructor.

## Test Target

~12 tests (guideline, not ceiling)

## Dev Agent Record

### Implementation Plan

- Tasks 1-3 (enums + model): Created as plain C# types. YAML snake_case handled via `UnderscoredNamingConvention` on the serialiser/deserialiser, not per-property attributes — single configuration point in `InstanceManager`.
- Task 4 (interface): All six async methods with `CancellationToken` as final parameter per project conventions.
- Task 5 (implementation): Two constructors — production (DI with `IOptions<AgentRunnerOptions>` + `IWebHostEnvironment`) and internal test constructor accepting a resolved path. Null-coalescing for `DataRootPath` addresses deferred-work item. Trailing separator normalised in constructor.
- Task 6 (DI registration): Singleton in `AgentRunnerComposer` per architecture spec.
- Task 7 (tests): 13 tests covering all ACs. Temp directory per test run with `[TearDown]` cleanup.

### Debug Log

- Build error: `ILogger<>` not in scope in test project (no global using for `Microsoft.Extensions.Logging`). Fixed by adding explicit `using` in test file.
- Build error: `internal` test constructor not accessible from test project. Fixed by adding `InternalsVisibleTo` in .csproj.

### Completion Notes

All 7 tasks implemented and verified. 55 total tests pass (39 existing + 16 new). All 9 acceptance criteria satisfied:
- AC1: Instance folder + instance.yaml created with correct schema
- AC2: Atomic writes via .tmp + File.Move
- AC3: GetInstanceAsync deserialises back to InstanceState
- AC4: UpdateStepStatusAsync with timestamp tracking
- AC5: Concurrent execution prevention (InvalidOperationException)
- AC6: DefaultProfile property exists on AgentRunnerOptions (pre-existing)
- AC7: Interrupted instance detection pattern (running + complete step)
- AC8: Singleton DI registration in AgentRunnerComposer
- AC9: 13 unit tests covering all specified scenarios

### Review Findings

- [x] [Review][Decision] Delete allows `Failed` status — kept as intentional. Failed is terminal, deletion is sensible. Added test.
- [x] [Review][Patch] Path traversal — `GetInstanceDirectory` now validates resolved path stays within `_dataRootPath`, throws `ArgumentException` on traversal. Added test. [InstanceManager.cs:259]
- [x] [Review][Patch] `ListInstances` — `CollectInstancesFromDirectoryAsync` now catches exceptions per-instance with warning log, preventing single corrupt file from killing entire listing [InstanceManager.cs:298]
- [x] [Review][Patch] Added test for `Cancelled` instance deletion [InstanceManagerTests.cs]
- [x] [Review][Patch] Added test for `Failed` instance deletion [InstanceManagerTests.cs]
- [x] [Review][Defer] Read-modify-write race condition on concurrent `SetInstanceStatusAsync` calls — no in-process locking. Real but no concurrent callers exist until Epic 4 step executor. Add `SemaphoreSlim` per instance when step execution lands — deferred, Epic 4 scope
- [x] [Review][Defer] `CurrentStepIndex` never advanced by `UpdateStepStatusAsync` — advancing is step execution logic, Story 4.3/4.5 scope — deferred
- [x] [Review][Defer] No state machine enforcement on step/instance status transitions — step executor (Epic 4) owns transition rules — deferred

## File List

- `Shallai.UmbracoAgentRunner/Instances/StepStatus.cs` (new)
- `Shallai.UmbracoAgentRunner/Instances/InstanceStatus.cs` (new)
- `Shallai.UmbracoAgentRunner/Instances/InstanceState.cs` (new)
- `Shallai.UmbracoAgentRunner/Instances/IInstanceManager.cs` (new)
- `Shallai.UmbracoAgentRunner/Instances/InstanceManager.cs` (new)
- `Shallai.UmbracoAgentRunner/Instances/.gitkeep` (deleted)
- `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` (modified — added IInstanceManager registration)
- `Shallai.UmbracoAgentRunner/Shallai.UmbracoAgentRunner.csproj` (modified — added InternalsVisibleTo)
- `Shallai.UmbracoAgentRunner.Tests/Instances/InstanceManagerTests.cs` (new)

## Change Log

- 2026-03-30: Implemented instance state management — enums, model, interface, disk-based manager with atomic writes, DI registration, and 13 unit tests
- 2026-03-30: Review pass — added path traversal sandboxing, ListInstances resilience, 3 new tests (Failed/Cancelled deletion, path traversal), updated dev notes with future-story guidance
