# Story 9.11: Fail Workflow Registration on Missing Agent Files

Status: done

**Depends on:** None (no code conflicts with any in-progress story)
**Blocks:** 9.5 (Private Beta Distribution Plan)

> Pre-launch code review finding (2026-04-10). `VerifyAgentFiles()` logs a warning but allows registration to proceed. A workflow with a missing agent file will appear in the UI, be startable, and then fail at execution time with `AgentFileNotFoundException`. This is inconsistent with `VerifyToolReferences()`, which correctly blocks registration. The fix is to make `VerifyAgentFiles` match `VerifyToolReferences`.

## Story

As a backoffice user,
I want workflows with missing agent files to be rejected at registration time,
So that only workflows that can actually execute appear in the UI.

## Context

**UX Mode: N/A — engine registration hardening. No UI work.**

### Current behaviour

[WorkflowRegistry.cs:134](AgentRun.Umbraco/Workflows/WorkflowRegistry.cs#L134) calls `VerifyAgentFiles()` (line 165), which is `void`. It iterates steps, checks `File.Exists` for each agent path, and logs `LogWarning` if missing — but the workflow still registers at line 141.

In contrast, [WorkflowRegistry.cs:136](AgentRun.Umbraco/Workflows/WorkflowRegistry.cs#L136) calls `VerifyToolReferences()` (line 144), which returns `bool`. When it returns `false`, `LoadSingleWorkflowAsync` returns early and the workflow is **not** registered.

### What this story changes

Make `VerifyAgentFiles` match the `VerifyToolReferences` pattern:
1. Change return type from `void` to `bool`
2. Return `false` if any agent file is missing
3. Escalate log level from `Warning` to `Error` (matching tool references)
4. Add a registration-blocking `if (!VerifyAgentFiles(...)) return;` check in `LoadSingleWorkflowAsync`
5. Update existing tests and add coverage for multi-step and mixed scenarios

## Acceptance Criteria

**AC1 — Single missing agent file blocks registration**
Given a `workflow.yaml` where step `scanner` references `agents/scanner.md` which does not exist on disk,
When the workflow is loaded by `WorkflowRegistry`,
Then the workflow is NOT registered (not in `GetAllWorkflows()`, `GetWorkflow()` returns null),
And an `Error`-level log is emitted naming the workflow alias, step ID, and missing file path.

**AC2 — All agent files present allows registration**
Given a `workflow.yaml` where all steps reference agent files that exist on disk,
When the workflow is loaded,
Then the workflow is registered successfully (no regression).

**AC3 — Multiple missing agent files all reported**
Given a workflow with 3 steps where 2 have missing agent files,
When the workflow is loaded,
Then the workflow is NOT registered,
And an `Error`-level log is emitted for **each** missing file (not just the first).

**AC4 — Empty/whitespace agent path skipped**
Given a step where the `agent` field is empty or whitespace (which `WorkflowValidator` already rejects as a required field, but `VerifyAgentFiles` should be defensive),
When `VerifyAgentFiles` encounters that step,
Then it skips the step with `continue` (preserving existing behaviour — no crash, no false-positive error).

**AC5 — Mixed valid and invalid agent files rejects workflow**
Given a workflow with 2 steps — one with a valid agent file on disk, one with a missing agent file,
When the workflow is loaded,
Then the entire workflow is NOT registered (not just the step with the missing file).

**AC6 — Invalid agent-file workflow doesn't block valid workflows**
Given two workflow folders — one with a missing agent file, one fully valid,
When `LoadWorkflowsAsync` runs,
Then the valid workflow is registered,
And the invalid workflow is not.

**AC7 — No regression on tool validation ordering**
Given a workflow where both agent files are missing AND tool references are invalid,
When the workflow is loaded,
Then both `VerifyAgentFiles` and `VerifyToolReferences` errors are logged (agent files checked first, tools second),
And the workflow is not registered.

## Implementation Notes

### Files to modify

1. **`AgentRun.Umbraco/Workflows/WorkflowRegistry.cs`**
   - `VerifyAgentFiles` (line 165): change signature from `private void` to `private bool`, add `valid` tracker matching `VerifyToolReferences` pattern, change `LogWarning` to `LogError`, return `valid`
   - `LoadSingleWorkflowAsync` (line 134): wrap call in `if (!VerifyAgentFiles(...)) return;` — place it before the `VerifyToolReferences` call so both checks run and all errors are reported

2. **`AgentRun.Umbraco.Tests/Workflows/WorkflowRegistryTests.cs`**
   - **Update** `LoadWorkflowsAsync_MissingAgentFile_LogsWarningAndLoads` (line 67) → rename to `LoadWorkflowsAsync_MissingAgentFile_WorkflowRejected`, assert `GetWorkflow` returns `null`, assert `LogLevel.Error` instead of `Warning`
   - **Update** `LoadWorkflowsAsync_ExistingAgentFile_NoWarning` (line 98) → update log assertion from `Warning` to `Error` (DidNotReceive Error)
   - **Add** test for AC3 (multiple missing files, both logged)
   - **Add** test for AC5 (mixed valid/invalid agent files, workflow rejected)
   - **Add** test for AC6 (invalid agent-file workflow doesn't block valid workflow)

### Exact code change in `LoadSingleWorkflowAsync`

Current (lines 134–139):
```csharp
VerifyAgentFiles(alias, folderPath, definition);

if (!VerifyToolReferences(alias, definition))
{
    return;
}
```

Target:
```csharp
if (!VerifyAgentFiles(alias, folderPath, definition))
{
    return;
}

if (!VerifyToolReferences(alias, definition))
{
    return;
}
```

### Exact code change in `VerifyAgentFiles`

Current (lines 165–185):
```csharp
private void VerifyAgentFiles(string alias, string folderPath, WorkflowDefinition definition)
{
    foreach (var step in definition.Steps)
    {
        if (string.IsNullOrWhiteSpace(step.Agent))
        {
            continue;
        }

        var agentPath = Path.Combine(folderPath, step.Agent);

        if (!File.Exists(agentPath))
        {
            _logger.LogWarning(
                "Workflow '{WorkflowAlias}': agent file '{AgentPath}' referenced by step '{StepId}' not found",
                alias,
                step.Agent,
                step.Id);
        }
    }
}
```

Target:
```csharp
private bool VerifyAgentFiles(string alias, string folderPath, WorkflowDefinition definition)
{
    var valid = true;
    foreach (var step in definition.Steps)
    {
        if (string.IsNullOrWhiteSpace(step.Agent))
        {
            continue;
        }

        var agentPath = Path.Combine(folderPath, step.Agent);

        if (!File.Exists(agentPath))
        {
            _logger.LogError(
                "Workflow '{WorkflowAlias}': agent file '{AgentPath}' referenced by step '{StepId}' not found",
                alias,
                step.Agent,
                step.Id);
            valid = false;
        }
    }
    return valid;
}
```

## Failure & Edge Cases

- **All agent files missing** → workflow not registered, all missing files listed in separate log entries
- **Agent path is empty/whitespace** → already handled by `WorkflowValidator` (required field); `VerifyAgentFiles` skips these with `continue` — preserve that behaviour
- **Agent file exists at load time but is deleted later** → out of scope; `PromptAssembler` already throws `AgentFileNotFoundException` at execution time
- **Workflow folder itself is missing or inaccessible** → already handled by existing registry error handling (try/catch around entire `LoadSingleWorkflowAsync`)
- **Deny-by-default statement:** if the agent file cannot be confirmed as present and readable, the workflow must not register

## What NOT to Build

- Do NOT add agent file content validation (e.g. checking markdown structure) — presence check only
- Do NOT add hot-reload of agent files — if a file is added later, the registry reloads on app restart as it does today
- Do NOT change `VerifyToolReferences` — it's already correct; this story makes `VerifyAgentFiles` match it
- Do NOT modify any workflow YAML files or agent prompt files

## Test Targets

~5 tests (3 new + 2 updated existing). This is a small story — the test count reflects the narrow scope.

## Manual E2E Validation

1. Start TestSite with both shipped workflows (CQA, A11y Quick Scan) — both should appear in the dashboard (no regression)
2. Rename one agent `.md` file temporarily (e.g. `scanner.md` → `scanner.md.bak`)
3. Restart TestSite — the affected workflow should NOT appear in the dashboard
4. Check application logs — should contain an `Error` entry naming the missing file
5. Restore the file, restart — workflow reappears

## Tasks/Subtasks

- [x] Change `VerifyAgentFiles` return type from `void` to `bool`, add `valid` tracker, change `LogWarning` to `LogError`, return `valid`
- [x] Update `LoadSingleWorkflowAsync` to block registration with `if (!VerifyAgentFiles(...)) return;`
- [x] Update test `LoadWorkflowsAsync_MissingAgentFile_LogsWarningAndLoads` → renamed to `_WorkflowRejected`, asserts `GetWorkflow` returns null and `LogLevel.Error`
- [x] Update test `LoadWorkflowsAsync_ExistingAgentFile_NoWarning` → renamed to `_NoError`, asserts `LogLevel.Error` not received
- [x] Add test `LoadWorkflowsAsync_MultipleMissingAgentFiles_AllErrorsLogged` (AC3)
- [x] Add test `LoadWorkflowsAsync_MixedValidAndMissingAgentFiles_WorkflowRejected` (AC5)
- [x] Add test `LoadWorkflowsAsync_MissingAgentFileWorkflow_DoesNotBlockValidWorkflow` (AC6)
- [x] All 491 tests pass: `dotnet test AgentRun.Umbraco.slnx`
- [ ] Manual E2E steps 1–5 verified by Adam

### Review Findings
- [x] [Review][Decision] D1: AC7 — early return replaced with combined check so both VerifyAgentFiles and VerifyToolReferences always run; AC7 test added [WorkflowRegistry.cs:134]
- [x] [Review][Patch] P1: AC4 — added test `LoadWorkflowsAsync_EmptyAgentPath_SkippedNoError` for empty/whitespace agent path [WorkflowRegistryTests.cs]

## Dev Agent Record

### Implementation Plan
Surgical change matching the `VerifyToolReferences` pattern exactly. Two edits to `WorkflowRegistry.cs`: (1) `VerifyAgentFiles` signature and body, (2) call-site guard in `LoadSingleWorkflowAsync`. Two existing tests updated + three new tests added.

### Debug Log
No issues encountered. All changes applied cleanly, 489/489 tests green on first run.

### Completion Notes
- `VerifyAgentFiles` now returns `bool` with `valid` tracker (matches `VerifyToolReferences` pattern)
- Log level escalated from `Warning` to `Error`
- Registration blocked via combined boolean gate in `LoadSingleWorkflowAsync` — both `VerifyAgentFiles` and `VerifyToolReferences` always run so all errors surface in a single restart cycle (AC7)
- 2 existing tests updated (renamed + assertion changes), 5 new tests added
- Total test count: 491 (up from 486)
- Code review applied 2 fixes: D1 combined check for AC7, P1 empty/whitespace agent path test for AC4

## File List

- `AgentRun.Umbraco/Workflows/WorkflowRegistry.cs` — modified (VerifyAgentFiles signature + LoadSingleWorkflowAsync guard)
- `AgentRun.Umbraco.Tests/Workflows/WorkflowRegistryTests.cs` — modified (2 tests updated, 3 tests added)

## Change Log

- Story 9.11: VerifyAgentFiles returns bool and blocks registration on missing agent files; log level Warning→Error; 3 new tests + 2 updated (Date: 2026-04-10)
- Story 9.11 code review: D1 — combined VerifyAgentFiles+VerifyToolReferences check (AC7); P1 — AC4 empty/whitespace agent path test added; 491/491 tests (Date: 2026-04-11)

## Definition of Done

- [x] `VerifyAgentFiles` returns `bool` and blocks registration on `false`
- [x] Log level escalated from `Warning` to `Error`
- [x] Existing test updated to assert rejection (not just warning)
- [x] New tests for multi-missing, mixed valid/invalid, isolation from other workflows
- [x] All tests pass: `dotnet test AgentRun.Umbraco.slnx`
- [ ] Manual E2E steps 1–5 verified by Adam
