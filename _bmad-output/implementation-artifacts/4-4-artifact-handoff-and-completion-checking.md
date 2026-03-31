# Story 4.4: Artifact Handoff & Completion Checking

Status: done

## Story

As a developer,
I want steps to declare their input and output artifacts with automatic completion checking,
so that the engine knows when a step is done and can make artifacts available to subsequent steps.

## Acceptance Criteria

1. **Given** a step definition declares `reads_from` artifact list, **when** the step begins execution, **then** the engine verifies all listed files exist in the instance folder before sending the prompt to the LLM (FR25)
2. **Given** a `reads_from` file is missing, **when** pre-execution validation runs, **then** step status transitions to `Error` with a message indicating which expected input files are missing — the LLM is never called
3. **Given** a step has `reads_from` artifacts, **when** the PromptAssembler builds the prompt, **then** the runtime context section lists reads_from files with existence status, labelled as "Input Artifacts" distinct from the existing "Prior Artifacts" section (FR25, FR27)
4. **Given** a step has `writes_to` artifacts, **when** the PromptAssembler builds the prompt, **then** the runtime context section lists the expected output files so the agent knows what to produce (FR26)
5. **Given** a step defines `completion_check.files_exist`, **when** the tool loop finishes (no more tool calls), **then** the `ICompletionChecker` verifies all listed files exist in the instance folder (FR16)
6. **Given** completion check passes, **when** all expected files are present, **then** step status transitions to `Complete` and instance state is updated atomically
7. **Given** completion check fails, **when** one or more expected files are missing, **then** step status transitions to `Error` with a message indicating which files are missing
8. **Given** a step has no `completion_check` defined (null), **when** the tool loop finishes, **then** the step completes normally without any file checks (backwards compatible with steps that have no completion criteria)
9. **Given** a step has `reads_from: []` (empty list) or null, **when** pre-execution validation runs, **then** validation passes immediately — no files to check
10. The `ICompletionChecker` interface is registered as a singleton in `AgentRunnerComposer`
11. Unit tests verify: completion checking with all files present, missing files, no completion check defined, reads_from pre-validation pass/fail, and StepExecutor integration with the checker

## What NOT to Build

- No workflow mode logic (interactive vs autonomous), no step advancement or `CurrentStepIndex` changes (Story 4.5)
- No SSE streaming or event emission (Story 4.5 / Epic 6)
- No conversation persistence to JSONL (Story 6.1)
- No retry logic for failed completion checks (Story 7.2)
- No `IHostedService` registration (Story 4.5)
- No reading artifact file **content** into the prompt — just list paths and existence status. Reading content into context is a potential future enhancement but risks prompt bloat.
- No dashboard UI changes
- No artifact viewer component (Epic 8)
- No path sandboxing validation on `reads_from`/`writes_to`/`completion_check` paths — these are developer-authored YAML values, not user input. Story 5.2 tool path sandboxing is the defence-in-depth layer.

## Tasks / Subtasks

- [x] Task 1: Create `ICompletionChecker` interface and `CompletionChecker` implementation (AC: #5-#8)
  - [x] 1.1: Create `Engine/ICompletionChecker.cs` with method `CheckAsync(CompletionCheckDefinition? check, string instanceFolderPath, CancellationToken cancellationToken)` returning `CompletionCheckResult`
  - [x] 1.2: Create `Engine/CompletionCheckResult.cs` as a record: `(bool Passed, IReadOnlyList<string> MissingFiles)`
  - [x] 1.3: Create `Engine/CompletionChecker.cs` implementing `ICompletionChecker`
  - [x] 1.4: When `check` is null or `FilesExist` is empty, return `Passed = true` with empty `MissingFiles`
  - [x] 1.5: For each file in `check.FilesExist`, resolve `Path.Combine(instanceFolderPath, file)` and check `File.Exists`
  - [x] 1.6: Return `Passed = false` with list of missing filenames if any are absent
  - [x] 1.7: Log at Debug level each file checked, log at Warning level each missing file

- [x] Task 2: Create `IArtifactValidator` for reads_from pre-validation (AC: #1-#2, #9)
  - [x] 2.1: Create `Engine/IArtifactValidator.cs` with method `ValidateInputArtifactsAsync(StepDefinition step, string instanceFolderPath, CancellationToken cancellationToken)` returning `ArtifactValidationResult`
  - [x] 2.2: Create `Engine/ArtifactValidationResult.cs` as a record: `(bool Passed, IReadOnlyList<string> MissingFiles)`
  - [x] 2.3: Create `Engine/ArtifactValidator.cs` implementing `IArtifactValidator`
  - [x] 2.4: When `step.ReadsFrom` is null or empty, return `Passed = true` immediately
  - [x] 2.5: For each file in `step.ReadsFrom`, resolve `Path.Combine(instanceFolderPath, file)` and check `File.Exists`
  - [x] 2.6: Return `Passed = false` with list of missing filenames if any are absent
  - [x] 2.7: Log at Debug level each file checked, log at Warning level each missing file

- [x] Task 3: Enhance PromptAssembler for reads_from and writes_to context (AC: #3-#4)
  - [x] 3.1: Add an "Input Artifacts (reads_from)" subsection before the existing "Prior Artifacts" section
  - [x] 3.2: List each `step.ReadsFrom` file with existence status (same pattern as existing prior artifacts)
  - [x] 3.3: Add an "Expected Output Files (writes_to)" subsection after "Prior Artifacts"
  - [x] 3.4: List each `step.WritesTo` file so the agent knows what it must produce
  - [x] 3.5: Skip sections if `ReadsFrom`/`WritesTo` are null or empty

- [x] Task 4: Integrate CompletionChecker and ArtifactValidator into StepExecutor (AC: #1-#8)
  - [x] 4.1: Add `IArtifactValidator` and `ICompletionChecker` as constructor dependencies on `StepExecutor`
  - [x] 4.2: After setting step to `Active`, call `IArtifactValidator.ValidateInputArtifactsAsync` — if failed, set step to `Error` and return (do NOT call the LLM)
  - [x] 4.3: After `ToolLoop.RunAsync` completes, call `ICompletionChecker.CheckAsync(step.CompletionCheck, instanceFolderPath, ct)`
  - [x] 4.4: If completion check passes (or check was null), set step to `Complete` (existing behaviour)
  - [x] 4.5: If completion check fails, log the missing files and set step to `Error`
  - [x] 4.6: Structured logging includes missing file names and completion check outcome

- [x] Task 5: Register in DI (AC: #10)
  - [x] 5.1: Add `builder.Services.AddSingleton<ICompletionChecker, CompletionChecker>();` to `AgentRunnerComposer`
  - [x] 5.2: Add `builder.Services.AddSingleton<IArtifactValidator, ArtifactValidator>();` to `AgentRunnerComposer`

- [x] Task 6: Write unit tests (AC: #11)
  - [x] 6.1: **CompletionChecker tests** — all files present returns `Passed = true`
  - [x] 6.2: **CompletionChecker tests** — missing files returns `Passed = false` with correct missing list
  - [x] 6.3: **CompletionChecker tests** — null `CompletionCheckDefinition` returns `Passed = true`
  - [x] 6.4: **CompletionChecker tests** — empty `FilesExist` list returns `Passed = true`
  - [x] 6.5: **ArtifactValidator tests** — all reads_from files present returns `Passed = true`
  - [x] 6.6: **ArtifactValidator tests** — missing reads_from file returns `Passed = false` with correct missing list
  - [x] 6.7: **ArtifactValidator tests** — null `ReadsFrom` returns `Passed = true`
  - [x] 6.8: **StepExecutor tests** — reads_from validation failure sets step to Error, LLM never called
  - [x] 6.9: **StepExecutor tests** — completion check failure after tool loop sets step to Error
  - [x] 6.10: **StepExecutor tests** — completion check pass sets step to Complete
  - [x] 6.11: **StepExecutor tests** — null completion check still sets step to Complete (backwards compat)
  - [x] 6.12: **PromptAssembler tests** — reads_from artifacts listed in prompt
  - [x] 6.13: **PromptAssembler tests** — writes_to artifacts listed in prompt

### Review Findings

- [x] [Review][Patch] Missing test for `reads_from: []` (empty list) — AC9 requires both null AND empty list pass immediately. Only null is tested in ArtifactValidatorTests. [Shallai.UmbracoAgentRunner.Tests/Engine/ArtifactValidatorTests.cs] — FIXED: added `EmptyReadsFrom_ReturnsPassed` test
- [x] [Review][Defer] Null/empty entries inside `ReadsFrom`/`FilesExist` lists from YAML deserialization quirks — `Path.Combine(path, null)` throws, `Path.Combine(path, "")` returns folder path. Not introduced by this story; defensive validation belongs at YAML parse boundary.
- [x] [Review][Defer] `NullCompletionCheck` StepExecutor test name is misleading — mock returns `Passed=true` regardless of input, so test doesn't prove null handling works differently from non-null. The actual null path is covered by `CompletionCheckerTests.NullCheck_ReturnsPassed`. Cosmetic, not a bug.

## Dev Notes

### CompletionChecker Design

The `CompletionChecker` is a simple file-existence checker. The architecture places it at `Engine/CompletionChecker.cs`. It has no Umbraco dependencies — just `System.IO` and logging.

```csharp
public interface ICompletionChecker
{
    Task<CompletionCheckResult> CheckAsync(
        CompletionCheckDefinition? check,
        string instanceFolderPath,
        CancellationToken cancellationToken);
}

public sealed record CompletionCheckResult(
    bool Passed,
    IReadOnlyList<string> MissingFiles);
```

The `Task` return is for consistency with async patterns even though `File.Exists` is synchronous — it avoids forcing callers to `Task.FromResult` and allows future async checks (e.g., file size validation).

### ArtifactValidator Design

Separated from CompletionChecker because it runs at a different point in the lifecycle:
- **ArtifactValidator** runs BEFORE the LLM call (pre-condition)
- **CompletionChecker** runs AFTER the tool loop (post-condition)

```csharp
public interface IArtifactValidator
{
    Task<ArtifactValidationResult> ValidateInputArtifactsAsync(
        StepDefinition step,
        string instanceFolderPath,
        CancellationToken cancellationToken);
}

public sealed record ArtifactValidationResult(
    bool Passed,
    IReadOnlyList<string> MissingFiles);
```

### StepExecutor Integration Points

The StepExecutor currently follows this flow:
1. Set step → Active
2. Resolve profile
3. Assemble prompt
4. Build tool wrappers
5. Run tool loop
6. Set step → Complete

After this story, the flow becomes:
1. Set step → Active
2. **Validate reads_from artifacts** — if failed, set Error and return
3. Resolve profile
4. Assemble prompt (now includes reads_from + writes_to context)
5. Build tool wrappers
6. Run tool loop
7. **Run completion check** — if failed, set Error; if passed, set Complete

**Critical:** If reads_from validation fails, the step goes to `Error` WITHOUT ever calling the LLM. This saves API costs and provides a fast, clear failure.

### PromptAssembler Enhancement

The existing `AppendArtifactsSection` already lists prior artifacts from completed steps' `WritesTo`. This story adds two new sections:

**Before "Prior Artifacts":**
```
**Input Artifacts (reads_from):**
- scan-results.md (from step "scanner"): exists
```

**After "Prior Artifacts":**
```
**Expected Output Files (writes_to):**
- analysis-report.md
- quality-scores.md
```

The reads_from section uses the same "exists/missing" pattern as prior artifacts. The writes_to section is just a file list — no existence check (the agent hasn't produced them yet).

### Existing Types to Use

| Type | Location | Usage |
|------|----------|-------|
| `StepExecutor` | `Engine/StepExecutor.cs` | Modified — add IArtifactValidator + ICompletionChecker injection and calls |
| `IStepExecutor` | `Engine/IStepExecutor.cs` | Unchanged |
| `StepExecutionContext` | `Engine/StepExecutionContext.cs` | Unchanged |
| `PromptAssembler` | `Engine/PromptAssembler.cs` | Modified — add reads_from + writes_to sections |
| `PromptAssemblyContext` | `Engine/PromptAssemblyContext.cs` | Unchanged (Step property already gives access to ReadsFrom/WritesTo) |
| `CompletionCheckDefinition` | `Workflows/CompletionCheckDefinition.cs` | Existing — has `FilesExist` property |
| `StepDefinition` | `Workflows/StepDefinition.cs` | Existing — has `ReadsFrom`, `WritesTo`, `CompletionCheck` |
| `IInstanceManager` | `Instances/IInstanceManager.cs` | Existing — `UpdateStepStatusAsync` for status transitions |
| `StepStatus` | `Instances/StepStatus.cs` | Existing — `Pending`, `Active`, `Complete`, `Error` |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | Modified — add DI registrations |

### Error Handling

**reads_from validation failure:**
- Log at Error: `"Input artifact validation failed for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}: missing files {MissingFiles}"`
- Set step → Error via `UpdateStepStatusAsync`
- Return immediately — do NOT throw. The StepExecutor handles this internally. The caller (future hosted service in 4.5) sees the step status as Error.

**Completion check failure:**
- Log at Warning: `"Completion check failed for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}: missing files {MissingFiles}"`
- Set step → Error via `UpdateStepStatusAsync`

**Error-path status update:** Same pattern as Story 4.3 — use `CancellationToken.None` for error-path status writes, wrap in try/catch with `LogCritical`.

### Testing Approach

- **CompletionChecker and ArtifactValidator** are tested against real temp directories with actual files — these are pure I/O classes, no mocking needed. Use `TestContext.CurrentContext.WorkDirectory` or `Path.GetTempPath()` for test directories.
- **StepExecutor integration** — mock `ICompletionChecker` and `IArtifactValidator` via NSubstitute to verify the wiring (calls in correct order, result handling).
- **PromptAssembler** — existing test patterns apply. Set up `StepDefinition` with `ReadsFrom`/`WritesTo` lists and verify the prompt output contains the expected sections.
- Use `NullLogger<T>.Instance` for loggers.
- Test target: ~13 tests (aligns with ~10 guideline, slightly above due to two new classes + integration).

**Existing StepExecutor tests will need updating** — they currently mock no `ICompletionChecker` or `IArtifactValidator`. The constructor signature changes. Update existing test fixtures to provide NSubstitute mocks for the two new dependencies. Default mock behaviour: `Passed = true`, so existing tests continue to pass without modification to their assertions.

### Namespace

- `ICompletionChecker`, `CompletionChecker`, `CompletionCheckResult`: `Shallai.UmbracoAgentRunner.Engine`
- `IArtifactValidator`, `ArtifactValidator`, `ArtifactValidationResult`: `Shallai.UmbracoAgentRunner.Engine`
- Tests: `Shallai.UmbracoAgentRunner.Tests.Engine`

### File Placement

| File | Path | Action |
|------|------|--------|
| ICompletionChecker.cs | `Shallai.UmbracoAgentRunner/Engine/ICompletionChecker.cs` | New |
| CompletionChecker.cs | `Shallai.UmbracoAgentRunner/Engine/CompletionChecker.cs` | New |
| CompletionCheckResult.cs | `Shallai.UmbracoAgentRunner/Engine/CompletionCheckResult.cs` | New |
| IArtifactValidator.cs | `Shallai.UmbracoAgentRunner/Engine/IArtifactValidator.cs` | New |
| ArtifactValidator.cs | `Shallai.UmbracoAgentRunner/Engine/ArtifactValidator.cs` | New |
| ArtifactValidationResult.cs | `Shallai.UmbracoAgentRunner/Engine/ArtifactValidationResult.cs` | New |
| PromptAssembler.cs | `Shallai.UmbracoAgentRunner/Engine/PromptAssembler.cs` | Modified |
| StepExecutor.cs | `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs` | Modified |
| AgentRunnerComposer.cs | `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` | Modified |
| CompletionCheckerTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/CompletionCheckerTests.cs` | New |
| ArtifactValidatorTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/ArtifactValidatorTests.cs` | New |
| StepExecutorTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/StepExecutorTests.cs` | Modified |
| PromptAssemblerTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/PromptAssemblerTests.cs` | Modified |

### Project Structure Notes

- All new files go in `Engine/` — maintaining zero Umbraco dependencies
- `CompletionCheckDefinition` already exists in `Workflows/` — no changes needed
- `StepDefinition` already has `ReadsFrom`, `WritesTo`, `CompletionCheck` — no model changes needed

### Retrospective Intelligence

From Epics 1-3 retrospective (2026-03-31):

- **Simplest fix first** — CompletionChecker is just `File.Exists` in a loop. Do not over-engineer with abstract file system providers or async file existence checks.
- **OperationCanceledException must propagate** — the new validation and checking code paths must include `catch (OperationCanceledException) { throw; }` before generic catches in StepExecutor.
- **Test target ~13 per story** — slightly above the ~10 guideline due to two new classes needing coverage, plus integration and prompt assembly tests.
- **Error handling edge cases are the blind spot** — ensure the reads_from validation failure path correctly sets Error status and returns without calling the LLM. Don't leave a code path where validation fails but execution continues.

From Story 4.3 dev notes:
- **Error-path status update uses `CancellationToken.None`** — apply the same pattern for new error paths
- **ToolLoop.MaxIterations (100)** — no change needed, just be aware it exists
- **`WithAlias()` required by Umbraco.AI** — no impact on this story (profile resolution is unchanged)
- **Existing StepExecutor tests (123 total)** — constructor signature change means all StepExecutor tests must be updated to provide the new mocks

### Manual E2E Validation

After automated tests pass, manually verify with the test site:

1. Create/update the example workflow to include `reads_from`, `writes_to`, and `completion_check` on at least one step
2. Start an instance — step with `reads_from` referencing a non-existent file should immediately error (no LLM call)
3. Create the expected input file in the instance folder, restart — step should proceed to LLM execution
4. If the step has `completion_check.files_exist` and the agent does NOT write the expected file, verify step ends in Error
5. If the agent writes the expected file, verify step completes successfully
6. Verify a step with no `completion_check` still completes normally after the tool loop

### Build Verification

Run `dotnet test Shallai.UmbracoAgentRunner.slnx` — never bare `dotnet test`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 4, Story 4.4]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure: Engine/CompletionChecker.cs, Engine/ICompletionChecker.cs]
- [Source: _bmad-output/planning-artifacts/architecture.md — FR25-28: Artifact Handoff mapping]
- [Source: _bmad-output/planning-artifacts/prd.md — FR16, FR25, FR26, FR27, FR28]
- [Source: _bmad-output/project-context.md — Engine boundary, atomic writes, error handling patterns]
- [Source: _bmad-output/implementation-artifacts/epic-1-2-3-retro-2026-03-31.md — Simplest fix first, OperationCanceledException, test targets]
- [Source: _bmad-output/implementation-artifacts/4-3-step-executor-and-tool-loop.md — StepExecutor integration points, error-path hardening, existing test count]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — Path traversal on developer-authored paths (deferred to 5.2)]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

### Completion Notes List

- Task 1: Created `ICompletionChecker`, `CompletionCheckResult`, `CompletionChecker` in Engine/. Simple File.Exists loop with Debug/Warning logging. Returns Task for async consistency.
- Task 2: Created `IArtifactValidator`, `ArtifactValidationResult`, `ArtifactValidator` in Engine/. Same pattern as CompletionChecker but validates reads_from at step start.
- Task 3: Enhanced PromptAssembler with `AppendInputArtifactsSection` (reads_from with existence flags) before Prior Artifacts, and `AppendOutputArtifactsSection` (writes_to file list) after Prior Artifacts. Both sections skipped when null/empty.
- Task 4: Integrated both services into StepExecutor. Artifact validation runs after Active status set, before LLM call — fails fast to Error without calling the LLM. Completion check runs after tool loop — fails to Error if files missing. Error-path status updates use CancellationToken.None with try/catch LogCritical pattern from Story 4.3.
- Task 5: Registered both as singletons in AgentRunnerComposer.
- Task 6: 13 new tests across 4 test files. Updated existing StepExecutor tests with new constructor dependencies (default mocks return Passed=true for backwards compat). All 136 tests pass.
- Code Review: Added `EmptyReadsFrom_ReturnsPassed` test for AC9 coverage (empty list path). 137 tests pass. Deferred 2 items to deferred-work.md (YAML null/empty list entries, misleading test name).

### Change Log

- 2026-03-31: Story 4.4 implementation complete — artifact handoff and completion checking

### File List

- Shallai.UmbracoAgentRunner/Engine/ICompletionChecker.cs (new)
- Shallai.UmbracoAgentRunner/Engine/CompletionCheckResult.cs (new)
- Shallai.UmbracoAgentRunner/Engine/CompletionChecker.cs (new)
- Shallai.UmbracoAgentRunner/Engine/IArtifactValidator.cs (new)
- Shallai.UmbracoAgentRunner/Engine/ArtifactValidationResult.cs (new)
- Shallai.UmbracoAgentRunner/Engine/ArtifactValidator.cs (new)
- Shallai.UmbracoAgentRunner/Engine/PromptAssembler.cs (modified)
- Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs (modified)
- Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs (modified)
- Shallai.UmbracoAgentRunner.Tests/Engine/CompletionCheckerTests.cs (new)
- Shallai.UmbracoAgentRunner.Tests/Engine/ArtifactValidatorTests.cs (new)
- Shallai.UmbracoAgentRunner.Tests/Engine/StepExecutorTests.cs (modified)
- Shallai.UmbracoAgentRunner.Tests/Engine/PromptAssemblerTests.cs (modified)
