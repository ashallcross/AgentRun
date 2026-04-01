# Story 5.4: Workflow Tool Validation

Status: done

## Story

As a developer,
I want workflow.yaml validation to check that all referenced tools exist in the registered tool set,
So that invalid tool references are caught at load time rather than at runtime.

## Acceptance Criteria

1. **Given** a workflow.yaml file declares tools in a step's `tools[]` array
   **When** the WorkflowRegistry loads and validates the workflow
   **Then** each declared tool name is checked against the set of registered `IWorkflowTool` implementations

2. **Given** a step references a tool name that is not registered
   **When** the tool validation runs
   **Then** the workflow is rejected with a clear error message: `"Step '{stepId}' references unknown tool '{toolName}'. Available tools: {list}"` (FR56)
   **And** the workflow is NOT added to the registry (unlike agent file warnings which still load)

3. **Given** workflows with invalid tool references are rejected
   **Then** they are logged as validation errors but do not prevent other valid workflows from loading

4. **Given** the validation runs after DI registration is complete
   **Then** all tools are available for checking (WorkflowRegistry receives `IEnumerable<IWorkflowTool>` via constructor injection)

5. **Given** unit tests verify: valid tool references pass, unknown tool references are rejected with correct error messages, and partial failures don't block other workflows

## What NOT to Build

- Changes to `WorkflowValidator` (YAML structure validation) -- tool validation is semantic, happens in `WorkflowRegistry` after parsing
- Changes to `StepExecutor`, `ToolLoop`, or any `Engine/` code
- Changes to tool implementations (`ReadFileTool`, `WriteFileTool`, `ListFilesTool`, `FetchUrlTool`)
- Frontend changes -- no UI work in this story
- Tool name case-sensitivity changes -- the validation should use the same case-insensitive comparison as `StepExecutor`
- Workflow-level tool whitelisting or blacklisting -- just validate that referenced tools exist

## Tasks / Subtasks

- [x] Task 1: Add `IEnumerable<IWorkflowTool>` to `WorkflowRegistry` constructor (AC: #4)
  - [x] Add `using Shallai.UmbracoAgentRunner.Tools;` import
  - [x] Add `private readonly HashSet<string> _registeredToolNames;` field (built from injected tools in constructor, `StringComparer.OrdinalIgnoreCase`)
  - [x] Constructor stores tool names as a `HashSet<string>` for O(1) lookup -- do NOT store `IEnumerable<IWorkflowTool>` directly (only names needed)
  - [x] DI resolves automatically -- `WorkflowRegistry` is already registered as singleton, `IEnumerable<IWorkflowTool>` resolves from the 4 registered tool singletons

- [x] Task 2: Add `VerifyToolReferences` method to `WorkflowRegistry` (AC: #1, #2)
  - [x] Add private method: `bool VerifyToolReferences(string alias, WorkflowDefinition definition)`
  - [x] Returns `true` if all tool references are valid, `false` if any are invalid (caller skips workflow)
  - [x] For each step in `definition.Steps`, check each tool name in `step.Tools` against `_registeredToolNames`
  - [x] Skip steps where `step.Tools` is null or empty (no tools declared = valid)
  - [x] For each unknown tool, log error: `"Workflow '{WorkflowAlias}': step '{StepId}' references unknown tool '{ToolName}'. Available tools: {list}"`
  - [x] The `{list}` is comma-separated, alphabetically sorted registered tool names (e.g., `"fetch_url, list_files, read_file, write_file"`)
  - [x] If ANY step has ANY invalid tool reference, return `false` (reject entire workflow)
  - [x] Log ALL invalid tool references before returning (don't stop at first error -- report everything)

- [x] Task 3: Integrate `VerifyToolReferences` into `LoadSingleWorkflowAsync` (AC: #1, #3)
  - [x] Call `VerifyToolReferences` after `VerifyAgentFiles` (line 115) and before adding to `_workflows` dict (line 117)
  - [x] If `VerifyToolReferences` returns `false`, return early (same pattern as validation failure on line 100)
  - [x] This ensures invalid tool references reject the workflow but don't affect other workflows (the foreach loop continues)

- [x] Task 4: Write `WorkflowRegistry` tool validation tests (AC: #5)
  - [x] Add tests to existing `WorkflowRegistryTests.cs` -- these are integration tests of the registry, not standalone
  - [x] Update `SetUp` to create `WorkflowRegistry` with a mock/real `IEnumerable<IWorkflowTool>` (constructor change)
  - [x] Test: workflow with valid tool references loads successfully
  - [x] Test: workflow with unknown tool reference is rejected (not in registry)
  - [x] Test: error message contains step ID, tool name, and available tools list
  - [x] Test: workflow with mix of valid steps and one invalid-tool step is rejected entirely
  - [x] Test: invalid-tool workflow rejected, other valid workflow still loads (partial failure isolation)
  - [x] Test: step with null/empty tools list passes validation (no tools = valid)
  - [x] Test: tool name matching is case-insensitive (`Read_File` matches `read_file`)
  - [x] Test: step with all tools from registered set passes validation
  - [x] Test: multiple steps each referencing different invalid tools -- all errors logged
  - [x] Test: workflow with no steps declaring any tools passes validation

- [x] Task 5: Run all tests and verify backwards compatibility
  - [x] `dotnet test Shallai.UmbracoAgentRunner.slnx`
  - [x] All 240 existing backend tests still pass
  - [x] All new tests pass

- [x] Task 6: Manual E2E validation
  - [x] Start TestSite with `dotnet run`
  - [x] Verify application starts without DI errors (constructor change is backwards-compatible)
  - [x] Verify existing workflows still load (valid tool references pass)
  - [x] Check logs for any tool validation messages

## Dev Notes

### Current Codebase State (Critical -- Read Before Implementing)

| Component | File | Status |
|-----------|------|--------|
| `WorkflowRegistry` | `Workflows/WorkflowRegistry.cs` | MODIFY -- add tool names field, constructor param, VerifyToolReferences method |
| `IWorkflowRegistry` | `Workflows/IWorkflowRegistry.cs` | DO NOT MODIFY -- interface unchanged |
| `WorkflowRegistryInitializer` | `Composers/WorkflowRegistryInitializer.cs` | DO NOT MODIFY -- calls registry, no changes needed |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | DO NOT MODIFY -- DI already registers both WorkflowRegistry and IWorkflowTool implementations |
| `StepDefinition` | `Workflows/StepDefinition.cs` | DO NOT MODIFY -- `Tools` is `List<string>?` |
| `IWorkflowTool` | `Tools/IWorkflowTool.cs` | DO NOT MODIFY |
| `WorkflowRegistryTests` | `Tests/Workflows/WorkflowRegistryTests.cs` | MODIFY -- update constructor calls, add new tests |
| `VerifyAgentFiles` | `Workflows/WorkflowRegistry.cs:120-139` | EXISTS -- pattern to follow for VerifyToolReferences |

**The primary deliverable is: `WorkflowRegistry` constructor change + `VerifyToolReferences` method + integration into load flow + tests.**

### Architecture Compliance

- `WorkflowRegistry` is in `Workflows/` namespace -- allowed to reference `Tools/IWorkflowTool` for tool name extraction
- The registry only stores tool NAMES (strings), not tool instances -- no runtime coupling to tool implementations
- `Engine/` folder is NOT touched -- zero changes to StepExecutor, ToolLoop, or PromptAssembler
- DI registration in `AgentRunnerComposer` needs no changes -- `IEnumerable<IWorkflowTool>` is automatically resolved from the 4 registered `AddSingleton<IWorkflowTool, T>()` calls

### Key Design Decision: Reject vs Warn

`VerifyAgentFiles` (line 120) WARNS about missing agent files but still loads the workflow. Tool validation must REJECT -- the AC says "the workflow is rejected." This is the correct severity because:
- Missing agent files might be a deployment issue (file not copied yet) -- recoverable
- Invalid tool references mean the step WILL fail at runtime -- not recoverable without workflow file changes

### Pattern to Follow: VerifyAgentFiles

```csharp
// EXISTING pattern (warns, still loads):
private void VerifyAgentFiles(string alias, string folderPath, WorkflowDefinition definition)
{
    foreach (var step in definition.Steps)
    {
        // ... check agent files, log warning if missing
    }
}

// NEW pattern (rejects if invalid):
private bool VerifyToolReferences(string alias, WorkflowDefinition definition)
{
    var valid = true;
    foreach (var step in definition.Steps)
    {
        if (step.Tools is null or { Count: 0 }) continue;

        foreach (var toolName in step.Tools)
        {
            if (!_registeredToolNames.Contains(toolName))
            {
                _logger.LogError(
                    "Workflow '{WorkflowAlias}': step '{StepId}' references unknown tool '{ToolName}'. Available tools: {AvailableTools}",
                    alias, step.Id, toolName, string.Join(", ", _registeredToolNames.Order()));
                valid = false;
            }
        }
    }
    return valid;
}
```

### Integration Point in LoadSingleWorkflowAsync

```csharp
// Line 115 (existing):
VerifyAgentFiles(alias, folderPath, definition);

// ADD after VerifyAgentFiles:
if (!VerifyToolReferences(alias, definition))
{
    return;
}

// Line 117 (existing):
_workflows[alias] = new RegisteredWorkflow(alias, folderPath, definition);
```

### Constructor Change

```csharp
// BEFORE:
public WorkflowRegistry(
    IWorkflowParser parser,
    IWorkflowValidator validator,
    ILogger<WorkflowRegistry> logger)

// AFTER:
public WorkflowRegistry(
    IWorkflowParser parser,
    IWorkflowValidator validator,
    IEnumerable<IWorkflowTool> registeredTools,
    ILogger<WorkflowRegistry> logger)
{
    _parser = parser;
    _validator = validator;
    _logger = logger;
    _registeredToolNames = new HashSet<string>(
        registeredTools.Select(t => t.Name),
        StringComparer.OrdinalIgnoreCase);
}
```

### Test Updates Required

ALL existing `WorkflowRegistryTests` create `WorkflowRegistry` directly in `SetUp`:
```csharp
_registry = new WorkflowRegistry(_parser, _validator, _logger);
```

This must become:
```csharp
_registry = new WorkflowRegistry(_parser, _validator, Enumerable.Empty<IWorkflowTool>(), _logger);
```

Using `Enumerable.Empty<IWorkflowTool>()` ensures all existing tests continue to pass unchanged -- workflows with no tool references in their steps (which is the case for all existing test fixtures) will still load fine since `step.Tools` is null on `new StepDefinition()`.

For new tests that need registered tools, create stub `IWorkflowTool` implementations via NSubstitute:
```csharp
var tool = Substitute.For<IWorkflowTool>();
tool.Name.Returns("read_file");
var tools = new[] { tool };
_registry = new WorkflowRegistry(_parser, _validator, tools, _logger);
```

### Available Tools List for Error Messages

Current registered tools (sorted alphabetically for deterministic output):
- `fetch_url`
- `list_files`
- `read_file`
- `write_file`

The error message `{list}` should be generated dynamically from `_registeredToolNames` (sorted), not hardcoded. Use `string.Join(", ", _registeredToolNames.Order())` (.NET 10 LINQ `Order()` method).

### Deferred Items Relevant to This Story

From `deferred-work.md`:
- "List item type validation -- `tools`, `reads_from`, `writes_to`, `files_exist` lists accept non-string items silently. Semantic validation is Story 5.4 scope."

This story validates tool NAMES against registered tools, but does NOT add type validation to `WorkflowValidator` for the `tools[]` list items (e.g., ensuring all items are strings). The deferred item about list item type validation is PARTIALLY addressed -- tool names are validated semantically, but YAML-level type checking of list items remains deferred. Update `deferred-work.md` to note this.

### Project Structure Notes

Modified files:
```
Shallai.UmbracoAgentRunner/
  Workflows/WorkflowRegistry.cs              (MODIFY - constructor, field, new method, integration)

Shallai.UmbracoAgentRunner.Tests/
  Workflows/WorkflowRegistryTests.cs         (MODIFY - constructor calls, new tests)
```

No new files needed.

### Retrospective Intelligence

**From Epics 1-4 retrospectives (actionable for this story):**

- **Story specs are the lever** -- failure cases section below covers all non-obvious failure modes
- **Simplest fix first** -- this is a small, focused change: one constructor param, one HashSet field, one validation method, one integration point. Don't over-engineer.
- **Existing tests must not break** -- 240 backend tests must all pass. The constructor change affects every test that creates WorkflowRegistry directly.
- **Code review will check**: case sensitivity consistency with StepExecutor, error message format matches AC exactly, null/empty tool list handling, existing test compatibility

**From Epic 4 retro specifically:**
- Test target ~10 per story. This story targets ~10 new tests.
- Failure & Edge Cases section included per the "story specs missed failure modes" lesson.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` -- Epic 5, Story 5.4]
- [Source: `_bmad-output/planning-artifacts/architecture.md` -- Decision 3: Tool System Interface, lines 205-235]
- [Source: `_bmad-output/planning-artifacts/prd.md` -- FR56: Validate workflow.yaml only references allowed tools]
- [Source: `_bmad-output/implementation-artifacts/5-1-tool-interface-and-registration.md` -- DI registration pattern, tool resolution flow]
- [Source: `_bmad-output/implementation-artifacts/5-3-fetch-url-tool-with-ssrf-protection.md` -- Latest tool registration in AgentRunnerComposer]
- [Source: `_bmad-output/implementation-artifacts/epic-4-retro-2026-03-31.md` -- Process rules, failure case requirement]
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md` -- List item type validation deferred item]

## Failure & Edge Cases

1. **Step with null Tools list**: `StepDefinition.Tools` is `List<string>?`. When null, skip validation for that step. This is the common case for steps that don't use tools. **Must test.**

2. **Step with empty Tools list**: `step.Tools` is `new List<string>()` (not null, but empty). Same as null -- skip validation. **Must test.**

3. **Case-insensitive tool name matching**: Workflow declares `tools: [Read_File]` but registered tool name is `read_file`. Must match case-insensitively to stay consistent with `StepExecutor.cs:96` which uses `StringComparer.OrdinalIgnoreCase`. **Must test.**

4. **All tool references valid**: Workflow step declares `tools: [read_file, write_file]` and both are registered. Workflow loads successfully. This is the happy path. **Must test.**

5. **One invalid tool among valid ones**: Step declares `tools: [read_file, nonexistent_tool]`. The entire workflow is rejected (not just the step). All errors are logged before rejection. **Must test.**

6. **Multiple steps with different invalid tools**: Step 1 references `bad_tool_a`, step 2 references `bad_tool_b`. Both errors are logged (don't stop at first error). Workflow rejected. **Must test.**

7. **No tools registered in DI**: `IEnumerable<IWorkflowTool>` is empty. `_registeredToolNames` is empty HashSet. Any tool reference is invalid. Available tools list in error message is empty string. **Must test.**

8. **Workflow with no steps declaring tools**: All steps have `Tools: null`. Entire workflow passes tool validation. **Must test.**

9. **Invalid tool workflow doesn't block valid workflow**: Two workflows loaded in same `LoadWorkflowsAsync` call. First has invalid tools, second is valid. First rejected, second loads. **Must test (existing pattern from `InvalidWorkflowSkipped_ValidStillLoads`).**

10. **Constructor with null IEnumerable**: If DI somehow provides null (shouldn't happen with MS DI, but defensive). Let it throw -- don't add null guards for impossible DI scenarios. **No test needed.**

11. **Duplicate tool names in step's tools[] array**: `tools: [read_file, read_file]`. Both are valid (both match). Not an error -- just redundant. The validation checks each name independently. **No special handling needed.**

12. **Tool name is null or empty in step's tools[] list**: If YAML parsing produces a null or empty string in the `Tools` list, the `HashSet.Contains` call returns false. This would log an error about an empty/null tool name. **Acceptable behaviour** -- the deferred list-item type validation would catch this at YAML level, but for now the semantic check handles it as an unknown tool.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- All 252 tests pass (240 existing + 12 new tool validation tests)
- TestSite started without DI errors, workflow loaded and executed all 4 tools successfully

### Completion Notes List

- Added `IEnumerable<IWorkflowTool>` constructor parameter to `WorkflowRegistry`, storing tool names as `HashSet<string>` with `OrdinalIgnoreCase` for O(1) lookup
- Added `VerifyToolReferences` method that iterates all steps, checks each declared tool against registered tools, logs all errors before returning rejection
- Integrated into `LoadSingleWorkflowAsync` after `VerifyAgentFiles` — invalid tool references reject the workflow but don't block other workflows
- Updated all existing test constructor calls to pass `Enumerable.Empty<IWorkflowTool>()`
- Added 11 new tests covering: valid tools, unknown tools, error message format, mixed valid/invalid, partial failure isolation, null/empty tools list, case-insensitive matching, all registered tools, multiple invalid tools across steps, no tools declared
- Added `CreateRegistryWithTools` helper for tests needing specific tool registrations
- [Review fix] Added `string.IsNullOrWhiteSpace(toolName)` guard before `HashSet.Contains` in `VerifyToolReferences` — `HashSet<string>.Contains(null)` with `OrdinalIgnoreCase` comparer throws `ArgumentNullException` in .NET 10, not `false` as spec edge case #12 assumed. YAML deserialization can produce null list entries (e.g. `tools: [read_file, ~]`). Added test `NullToolNameInList_WorkflowRejected`.

### Change Log

- Story 5.4: Workflow tool validation — validates step tool references against registered tools at load time (2026-04-01)
- Review fix: null/whitespace tool name guard + test (2026-04-01)

### File List

- Shallai.UmbracoAgentRunner/Workflows/WorkflowRegistry.cs (modified — constructor, field, VerifyToolReferences method, integration)
- Shallai.UmbracoAgentRunner.Tests/Workflows/WorkflowRegistryTests.cs (modified — constructor calls updated, 12 new tests, helper method)

### Review Findings

- [x] [Review][Patch] Null tool name in `Tools` list throws `ArgumentNullException` — fixed: added `string.IsNullOrWhiteSpace` guard + test [WorkflowRegistry.cs:140]
