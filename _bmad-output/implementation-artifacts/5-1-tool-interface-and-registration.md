# Story 5.1: Tool Interface & Registration

Status: done

## Story

As a developer,
I want an extensible tool interface with DI-based registration and per-step tool filtering,
So that new tools can be added without modifying the engine and each step only has access to its declared tools.

## Acceptance Criteria

1. **Given** the `IWorkflowTool` interface defines `Name` (string), `Description` (string), and `ExecuteAsync(arguments, context, cancellationToken)`
   **Then** it exists in `Tools/IWorkflowTool.cs` with the correct signature (ALREADY EXISTS — verify only)

2. **Given** `ToolExecutionContext` provides instance folder path, instance ID, step ID, and workflow alias
   **Then** it exists in `Tools/ToolExecutionContext.cs` as a sealed record (ALREADY EXISTS — verify only)

3. **Given** a `ToolExecutionException` type inherits from `AgentRunnerException`
   **When** a tool encounters an execution error
   **Then** it throws `ToolExecutionException` which the engine catches and returns to the LLM as an error result
   **And** `ToolExecutionException` lives in `Tools/ToolExecutionException.cs` (NEW — must create)

4. **Given** a tool implementation is registered in DI via `AgentRunnerComposer`
   **Then** each tool is registered individually as `IWorkflowTool`: `services.AddSingleton<IWorkflowTool, ConcreteToolName>()`
   **And** the DI pattern is validated by uncommenting and adding a registration block (no concrete tools yet — Stories 5.2/5.3 add them)

5. **Given** the engine receives a step with declared `tools: [read_file, write_file]`
   **When** `StepExecutor` resolves tools from `IEnumerable<IWorkflowTool>`
   **Then** only tools whose `Name` matches the step's `tools[]` list are included (case-insensitive)
   **And** tools not declared in the step are excluded (ALREADY IMPLEMENTED in `StepExecutor.cs:93-97` — verify via tests)

6. **Given** resolved tools are wrapped as `AIFunction` instances
   **When** `StepExecutor` builds `ChatOptions.Tools`
   **Then** each tool is wrapped via `AIFunctionFactory.Create(tool.ExecuteAsync, tool.Name, tool.Description)`
   **And** only `AIFunction` instances for declared tools are passed in `ChatOptions.Tools` (ALREADY IMPLEMENTED in `StepExecutor.cs:118-132` — verify via tests)

7. **Given** the `ToolLoop` dispatches tool calls from the LLM
   **When** a `FunctionCallContent` arrives with a tool name
   **Then** ToolLoop looks up the tool by name (case-insensitive) from the declared tools dictionary
   **And** undeclared tool calls return an error result to the LLM (not thrown) (ALREADY IMPLEMENTED — verify via tests)

8. **Given** a tool throws `ToolExecutionException` during execution
   **When** `ToolLoop` catches the exception
   **Then** the error message is returned to the LLM as a `FunctionResultContent` error (engine already catches all exceptions — NEW: also catch `ToolExecutionException` specifically for structured error messages)

9. **Given** adding a new tool requires only implementing `IWorkflowTool` and registering in DI
   **Then** no engine modification is needed (NFR25 — verify via integration-style test)

10. **Given** unit tests cover tool resolution by name, per-step filtering, AIFunction wrapping, and error handling
    **Then** all tests pass and cover the scenarios listed in the Testing section below

## What NOT to Build

- Concrete tool implementations (ReadFileTool, WriteFileTool, ListFilesTool, FetchUrlTool) — those are Stories 5.2 and 5.3
- Path sandboxing or SSRF protection — Stories 5.2 and 5.3
- Workflow-level tool validation (checking tool names exist at load time) — Story 5.4
- Changes to ToolLoop's core dispatch logic (already working correctly)
- Changes to StepExecutor's filtering/wrapping logic (already working correctly)
- Frontend changes — no UI work in this story

## Tasks / Subtasks

- [x] Task 1: Create `ToolExecutionException` (AC: #3)
  - [x] Create `Tools/ToolExecutionException.cs` inheriting from `AgentRunnerException`
  - [x] Include constructors: `(string message)` and `(string message, Exception innerException)`

- [x] Task 2: Update ToolLoop to handle `ToolExecutionException` specifically (AC: #8)
  - [x] In the existing `catch (Exception ex)` block in `ToolLoop.cs:116-131`, add a preceding `catch (ToolExecutionException tex)` that returns a more structured error message: `"Tool '{tool.Name}' execution error: {tex.Message}"`
  - [x] The generic `catch (Exception ex)` continues to handle unexpected errors with: `"Tool '{tool.Name}' failed: {ex.Message}"`
  - [x] Ensure `catch (OperationCanceledException)` remains FIRST (before ToolExecutionException)

- [x] Task 3: Finalize DI registration pattern in AgentRunnerComposer (AC: #4)
  - [x] Uncomment the tool registration line and add a comment block documenting the pattern
  - [x] Leave concrete registrations for Stories 5.2/5.3 but ensure the pattern is clear:
    ```csharp
    // Tools — register each IWorkflowTool individually (Epic 5)
    // Concrete tools added in Stories 5.2 (file tools) and 5.3 (fetch_url):
    // builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
    // builder.Services.AddSingleton<IWorkflowTool, WriteFileTool>();
    // builder.Services.AddSingleton<IWorkflowTool, ListFilesTool>();
    // builder.Services.AddSingleton<IWorkflowTool, FetchUrlTool>();
    ```

- [x] Task 4: Write tool resolution and filtering tests (AC: #5, #10)
  - [x] Test: registered tool found by exact name
  - [x] Test: registered tool found by name (case-insensitive)
  - [x] Test: unregistered tool name returns empty result
  - [x] Test: step with `tools: [read_file]` filters to only that tool from a set of 3
  - [x] Test: step with `tools: null` or empty list results in no tools
  - [x] Test: step with tools that don't match any registered tool results in empty dict

- [x] Task 5: Write AIFunction wrapping tests (AC: #6, #10)
  - [x] Test: AIFunction wrapper created with correct name and description
  - [x] Test: AIFunction invocation calls through to `IWorkflowTool.ExecuteAsync` with correct arguments and context
  - [x] Test: multiple tools produce multiple AIFunction wrappers

- [x] Task 6: Write ToolExecutionException handling tests (AC: #3, #8, #10)
  - [x] Test: ToolExecutionException thrown by tool is caught and returned as structured error result
  - [x] Test: generic Exception thrown by tool is caught and returned as error result (existing test — verify)
  - [x] Test: OperationCanceledException propagates (existing test — verify)

- [x] Task 7: Write extensibility verification test (AC: #9, #10)
  - [x] Test: create a custom IWorkflowTool in-test, add to declared tools dict, verify ToolLoop dispatches to it correctly (proves no engine changes needed)

- [x] Task 8: Run all tests and verify backwards compatibility
  - [x] `dotnet test Shallai.UmbracoAgentRunner.slnx`
  - [x] All 153 existing backend tests still pass
  - [x] All new tests pass

- [ ] Task 9: Manual E2E validation
  - [ ] Start TestSite with `dotnet run`
  - [ ] Verify application starts without DI errors
  - [ ] Verify existing workflows still load and execute (no regressions)

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

Most of Story 5.1's acceptance criteria are **already implemented** from Epic 4 work. The engine was built tool-ready:

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowTool` interface | `Tools/IWorkflowTool.cs` | EXISTS — do not modify |
| `ToolExecutionContext` record | `Tools/ToolExecutionContext.cs` | EXISTS — do not modify |
| Per-step tool filtering | `Engine/StepExecutor.cs:93-97` | EXISTS — do not modify |
| AIFunction wrapping | `Engine/StepExecutor.cs:118-132` | EXISTS — do not modify |
| Tool dispatch + error handling | `Engine/ToolLoop.cs:68-131` | EXISTS — modify only for ToolExecutionException catch |
| ToolLoop tests (7 tests) | `Tests/Engine/ToolLoopTests.cs` | EXISTS — do not modify existing tests |
| DI placeholder | `Composers/AgentRunnerComposer.cs:42-43` | EXISTS — update comment block |
| Tool test folder | `Tests/Tools/.gitkeep` | EMPTY — add new tests here |

**The primary deliverable is: ToolExecutionException + new tests that specifically verify tool resolution, filtering, wrapping, and extensibility.**

### Architecture Compliance

- `ToolExecutionException` goes in `Tools/` folder (namespace `Shallai.UmbracoAgentRunner.Tools`)
- `Tools/` has zero Umbraco dependencies — pure .NET only
- All exceptions inherit from `AgentRunnerException` (base in `Engine/AgentRunnerException.cs`)
- Cross-namespace reference is fine: `Tools` namespace references `Engine` namespace for base exception

### Key Code Patterns to Follow

**Exception pattern** (match `AgentRunnerException`):
```csharp
namespace Shallai.UmbracoAgentRunner.Tools;

public class ToolExecutionException : Engine.AgentRunnerException
{
    public ToolExecutionException(string message) : base(message) { }
    public ToolExecutionException(string message, Exception innerException) : base(message, innerException) { }
}
```

**Test pattern** (match `ToolLoopTests.cs`):
- `[TestFixture]` class with `[SetUp]`/`[TearDown]`
- `NSubstitute` for mocks: `Substitute.For<IWorkflowTool>()`
- `Assert.That()` with fluent constraints
- Helper methods for creating test responses

**DI registration pattern** (match existing registrations in `AgentRunnerComposer.cs`):
- `builder.Services.AddSingleton<IWorkflowTool, ConcreteToolName>()`
- Each tool registered individually, not via assembly scanning

### ToolLoop Catch Block Ordering (Critical)

Current catch ordering in `ToolLoop.cs:112-131`:
```
catch (OperationCanceledException) { throw; }           // FIRST — must propagate
catch (Exception ex) { ... return error result ... }     // LAST — catch-all
```

After this story:
```
catch (OperationCanceledException) { throw; }           // FIRST — must propagate
catch (ToolExecutionException tex) { ... structured error ... }  // NEW — tool-specific
catch (Exception ex) { ... return error result ... }     // LAST — unexpected errors
```

### StepExecutor Tool Resolution Flow (Reference)

1. `_allTools` injected via DI as `IEnumerable<IWorkflowTool>` (line 11)
2. `step.Tools` contains declared tool names from workflow.yaml (line 94)
3. LINQ filter: `.Where(t => declaredToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))` (line 96)
4. `.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase)` (line 97)
5. Each tool wrapped via `AIFunctionFactory.Create()` (lines 126-131)
6. Tool dict passed to `ToolLoop.RunAsync()` (line 144)

### Where to Put New Tests

- **Tool resolution/filtering tests**: `Tests/Tools/ToolResolutionTests.cs` — tests the LINQ filtering logic extracted from StepExecutor (test via StepExecutor or replicate the filtering logic in test)
- **ToolExecutionException tests**: `Tests/Tools/ToolExecutionExceptionTests.cs` — verify exception hierarchy and message propagation
- **AIFunction wrapping tests**: `Tests/Tools/AIFunctionWrappingTests.cs` — verify wrapping produces correct function
- **New ToolLoop tests**: Add to existing `Tests/Engine/ToolLoopTests.cs` — for ToolExecutionException-specific catch behaviour

### Project Structure Notes

New files to create:
```
Shallai.UmbracoAgentRunner/
  Tools/
    ToolExecutionException.cs          (NEW)

Shallai.UmbracoAgentRunner.Tests/
  Tools/
    ToolResolutionTests.cs             (NEW)
    ToolExecutionExceptionTests.cs     (NEW — or combine into ToolResolutionTests)
```

Modified files:
```
Shallai.UmbracoAgentRunner/
  Engine/ToolLoop.cs                   (ADD ToolExecutionException catch block)
  Composers/AgentRunnerComposer.cs     (UPDATE comment block for tool registration pattern)

Shallai.UmbracoAgentRunner.Tests/
  Engine/ToolLoopTests.cs              (ADD ToolExecutionException test)
```

### Retrospective Intelligence

**From Epics 1-4 retrospectives (actionable for this story):**

- **Story specs are the lever** — dev agent builds exactly what's specified. Unspecified failure paths become bugs caught only in review. This story includes explicit failure cases below.
- **Simplest fix first** — don't overcomplicate. ToolExecutionException is a simple subclass. The catch block is a 5-line addition. Don't over-engineer.
- **Existing tests must not break** — 153 backend + 67 frontend tests must all still pass. The ToolLoop catch block change must be backwards-compatible (generic catch still handles non-ToolExecutionException errors).
- **Error-path CancellationToken.None** — any error-path cleanup must use `CancellationToken.None`. Not directly applicable here (ToolLoop already handles this), but keep the pattern in mind.
- **OperationCanceledException FIRST** — always catch OCE before any other exception type. Already correct in ToolLoop, must stay correct after adding ToolExecutionException catch.
- **Code review will check**: OCE ordering, null/empty edge cases, Engine boundary compliance (no Umbraco deps in Tools/).

**From Epic 4 retro specifically:**
- 8 critical catches in review, all genuine production failures (not style issues). Expect ~5-10 review fixes.
- Test target ~10-12 per story. This story should aim for ~10 new tests.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` — Epic 5, Story 5.1]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Decision 3: Tool System Interface, lines 205-235]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Tool Execution Patterns, lines 413-414]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — DI Registration, lines 400-404]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Test Project Structure, lines 700-724]
- [Source: `_bmad-output/planning-artifacts/prd.md` — FR17-FR24, FR56-FR62, NFR25-26]
- [Source: `_bmad-output/implementation-artifacts/epic-4-retro-2026-03-31.md` — Process rules, error patterns]

## Failure & Edge Cases

1. **Duplicate tool names in DI**: If two `IWorkflowTool` implementations share the same `Name`, `ToDictionary()` in StepExecutor will throw `ArgumentException`. The story does NOT need to solve this (no concrete tools yet), but the test suite should document the behaviour with a test that shows the exception.

2. **Null/empty tool name on IWorkflowTool**: If a tool implementation returns `null` or `string.Empty` for `Name`, the LINQ filter and dictionary building will behave unexpectedly. Add a test documenting this edge case.

3. **Step declares tools but no tools registered in DI**: `_allTools` is empty `IEnumerable`. Filter produces empty dict. ToolLoop receives empty dict. LLM tool calls all return "not declared" errors. This is correct behaviour — verify with a test.

4. **ToolExecutionException with null message**: `AgentRunnerException(null)` passes null to `Exception(string)`. The error result sent to LLM would contain "Tool 'x' execution error: ". Acceptable — no special handling needed.

5. **ToolLoop ToolExecutionException catch vs generic catch**: The new `catch (ToolExecutionException)` must NOT change behaviour for non-ToolExecutionException errors. Existing tests verify this — run them to confirm.

### Review Findings

- [x] [Review][Patch] AIFunction wrapping test missing return value assertion [Tests/Tools/AIFunctionWrappingTests.cs:58] — fixed: added `Assert.That(result?.ToString(), Is.EqualTo("file contents here"))`
- [x] [Review][Patch] `NullToolName_BehavesAsEmptyString` test name is misleading [Tests/Tools/ToolResolutionTests.cs:129] — fixed: renamed to `NullToolName_NeverMatchesDeclaredTools`
- [x] [Review][Patch] `capturedTool` variable in `WrapTool` is redundant [Tests/Tools/AIFunctionWrappingTests.cs:23] — fixed: removed, using `tool` parameter directly
- [x] [Review][Patch] `ToolExtensibilityTests` does not dispose `chatClient` on test failure [Tests/Tools/ToolExtensibilityTests.cs:74] — fixed: moved to `[SetUp]`/`[TearDown]` pattern
- [x] [Review][Patch] Failure & Edge Case #4 (ToolExecutionException with null message) has no documenting test [Tests/Tools/ToolExecutionExceptionTests.cs] — fixed: added `NullMessage_ProducesDefaultExceptionMessage` test
- [x] [Review][Defer] Generic `Exception` in ToolLoop logged at Warning, should be Error for unexpected failures [Engine/ToolLoop.cs:133] — deferred, pre-existing
- [x] [Review][Defer] Error catch blocks don't emit `tool.result` SSE event (success path emits both `tool.end` + `tool.result`) [Engine/ToolLoop.cs:122-141] — deferred, pre-existing
- [x] [Review][Defer] Emitter exceptions not caught — if SSE connection drops, emitter calls propagate uncaught [Engine/ToolLoop.cs:44,85,92,106] — deferred, pre-existing
- [x] [Review][Defer] `client.GetResponseAsync` exceptions propagate uncaught from ToolLoop (no retry/structured error) [Engine/ToolLoop.cs:33] — deferred, pre-existing
- [x] [Review][Defer] `functionCall.Name` null from malformed LLM response causes `ArgumentNullException` on dictionary lookup [Engine/ToolLoop.cs:70] — deferred, pre-existing
- [x] [Review][Defer] Duplicate tool names in DI crash `StepExecutor.ToDictionary()` with unstructured `ArgumentException` — no guard [Engine/StepExecutor.cs:97] — deferred, pre-existing
- [x] [Review][Defer] No test for CancellationToken propagation to `client.GetResponseAsync` [Engine/ToolLoop.cs:33] — deferred, pre-existing
- [x] [Review][Defer] No test coverage for SSE emitter interactions (all tests pass null emitter) — deferred, pre-existing
- [x] [Review][Defer] `ExecuteAsync` returns `Task<object>` — null return untested, may produce null `FunctionResultContent.Result` — deferred, pre-existing

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

### Completion Notes List

- Created `ToolExecutionException` in `Tools/` — inherits `AgentRunnerException`, two constructors, zero Umbraco deps
- Added `catch (ToolExecutionException tex)` in `ToolLoop.cs` between OCE and generic catch — structured error format: `"Tool '{name}' execution error: {message}"`
- Updated `AgentRunnerComposer.cs` comment block documenting the DI registration pattern for concrete tools (Stories 5.2/5.3)
- 20 new tests across 5 test files: 10 tool resolution/filtering, 3 AIFunction wrapping, 4 exception hierarchy, 2 ToolLoop exception handling, 1 extensibility verification
- All 173 backend tests pass (153 existing + 20 new)
- Edge cases covered: duplicate tool names (ArgumentException), null tool name, empty DI registry, case-insensitive matching, null exception message
- Awaiting Adam's manual E2E validation (Task 9)

### Code Review Fixes Applied

- Added return value assertion to `Wrapper_CallsThroughToExecuteAsync` — was verifying call-through but not the return path
- Renamed `NullToolName_BehavesAsEmptyString` → `NullToolName_NeverMatchesDeclaredTools` — old name implied coercion that doesn't happen
- Removed redundant `capturedTool` variable in `WrapTool` helper — `tool` is a parameter not a loop variable
- Moved `chatClient` in `ToolExtensibilityTests` to `[SetUp]`/`[TearDown]` — ensures disposal even on assertion failure
- Added `NullMessage_ProducesDefaultExceptionMessage` test for Failure & Edge Case #4
- 9 pre-existing issues deferred to deferred-work.md (emitter error handling, log levels, null guards, test coverage gaps)

### File List

New files:
- `Shallai.UmbracoAgentRunner/Tools/ToolExecutionException.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/ToolExecutionExceptionTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/ToolResolutionTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/AIFunctionWrappingTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/ToolExtensibilityTests.cs`

Modified files:
- `Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs`
- `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs`
- `Shallai.UmbracoAgentRunner.Tests/Engine/ToolLoopTests.cs`
