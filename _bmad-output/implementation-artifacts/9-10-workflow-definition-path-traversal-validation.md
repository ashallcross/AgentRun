# Story 9.10: Workflow Definition Path Traversal Validation — BETA BLOCKER (security)

Status: ready-for-dev

**Depends on:** 9.6 (done), 9.0 (done), 9.1a (done), 9.1b (done), 9.1c (done), 9.4 (done), 9.2 (done), 9.3 (in-progress — no conflict, 9.3 is documentation/NuGet packaging)
**Blocks:** 9.5 (Private Beta Distribution Plan — beta cannot ship with this file-exfiltration vector open)

> **BETA BLOCKER — security.** The runtime tool layer (`ReadFileTool`, `WriteFileTool`, `ListFilesTool`) correctly uses `PathSandbox.ValidatePath()` to canonicalise paths and reject traversal. However, workflow-defined values — `agent`, `reads_from`, `writes_to`, `completion_check.files_exist`, and step IDs — enter the system through `WorkflowValidator` and are consumed by `PromptAssembler`, `ArtifactValidator`, `CompletionChecker`, and `ConversationStore` without any path-safety validation. A malicious or malformed `workflow.yaml` can use `../../` values to read arbitrary host files into an LLM system prompt, probe file existence outside the instance folder, or inject path separators via step IDs into conversation filenames. In a package that sends host content to third-party LLM providers, this is a launch-blocking file-exfiltration risk.

## Story

As the AgentRun engine,
I want all workflow-defined path values and identifiers validated for path safety at workflow-load time,
So that no workflow definition — whether authored by a package consumer, a community contributor, or an attacker — can escape its intended sandbox boundaries through the definition layer.

## Context

**UX Mode: N/A — engine security hardening. No UI work.**

**Authorisation:** This story was added 2026-04-10 via pre-launch code review (Codex). The epic spec at [epics.md Story 9.10 (line 1716)](../planning-artifacts/epics.md#L1716) contains the full threat model, scope table, and architect's locked decisions. The `project-context.md` security rules (NFR6, FR22, FR58) and the "Security code: deny by default" process rule both apply.

### The gap this story closes

The existing security architecture has two layers:

1. **Runtime tool layer** — `PathSandbox.ValidatePath()` in `ReadFileTool`, `WriteFileTool`, `ListFilesTool`, and `ArtifactEndpoints`. This layer canonicalises agent-provided paths at execution time. It is **correct and complete** — Story 5.2 built it, Story 9.9 hardened it.

2. **Workflow definition layer** — `WorkflowValidator.ValidateStep()` checks structural YAML validity (required fields, types, allowed keys). It does **not** check path content. Values like `agent: "../../etc/passwd"` or `writes_to: ["/etc/crontab"]` pass validation and reach consumers that resolve them via unsandboxed `Path.Combine`.

The definition layer is consumed by four components that use `Path.Combine` without canonicalisation:
- `PromptAssembler.cs:27` — `Path.Combine(workflowFolderPath, step.Agent)` then `File.ReadAllTextAsync` (reads arbitrary file into LLM system prompt)
- `PromptAssembler.cs:138,165` — `Path.Combine(instanceFolderPath, artifactPath)` then `File.Exists` (probes file existence outside instance folder)
- `ArtifactValidator.cs:28` — `Path.Combine(instanceFolderPath, file)` then `File.Exists`
- `CompletionChecker.cs:28` — `Path.Combine(instanceFolderPath, file)` then `File.Exists`
- `ConversationStore.cs:172` — `$"conversation-{stepId}.jsonl"` (step ID injected into filename; path separator in ID escapes folder)

### Architect's locked decisions (do not re-litigate)

These are from the epic spec and are **not negotiable**:

1. **Reject absolute paths** — any value starting with `/`, `\`, or a drive letter (e.g. `C:`) fails validation at workflow load.
2. **Reject traversal segments** — any value containing `..` as a path segment (i.e. `../`, `..\`, or standalone `..`) fails validation.
3. **Reject path separators in step IDs** — step `id` values must not contain `/`, `\`, or null bytes. They are used as filename components, not paths.
4. **Validation lives in `WorkflowValidator`** — additions to the existing `ValidateStep()` method. No new classes needed.
5. **Validation runs at workflow-load time** — broken workflows fail registration in `WorkflowRegistry`, never appear in the UI, never reach execution.
6. **`PathSandbox` is NOT called from the validator** — the validator doesn't have runtime folder paths (instance folder doesn't exist yet at load time). Apply structural checks only. The runtime consumers add defence-in-depth canonicalisation independently.
7. **Defence-in-depth canonicalisation in consumers** — after the validator structural checks, each consumer that resolves a workflow-defined path must call `Path.GetFullPath()` and verify the result starts with the expected root. This mirrors what `PathSandbox.ValidatePath()` does for tool arguments but uses the appropriate root per context.
8. **Deny-by-default** — unrecognised or structurally suspect path patterns (encoded separators, null bytes, unicode normalisation tricks) MUST be rejected. If in doubt, reject.

## Acceptance Criteria

**AC1 — `agent` field rejects traversal and absolute paths**
Given a `workflow.yaml` with `agent: "../../etc/passwd"` (or any value containing `..` as a path segment, or starting with `/`, `\`, or a drive letter),
When the workflow is loaded by `WorkflowRegistry`,
Then the entire workflow fails validation with an error naming the offending field and value,
And the workflow does NOT appear in the workflow list API or the dashboard UI.

**AC2 — `reads_from` entries reject traversal and absolute paths**
Given a step declares `reads_from: ["../../../appsettings.json"]`,
When the workflow is loaded,
Then the entire workflow fails validation,
And the error identifies the step, the field (`reads_from`), and the offending value.

**AC3 — `writes_to` entries reject traversal and absolute paths**
Given a step declares `writes_to: ["/etc/crontab"]`,
When the workflow is loaded,
Then the entire workflow fails validation.

**AC4 — `completion_check.files_exist` entries reject traversal and absolute paths**
Given a step declares `completion_check: { files_exist: ["../../secret.txt"] }`,
When the workflow is loaded,
Then the entire workflow fails validation.

**AC5 — Step `id` rejects path separators and null bytes**
Given a step declares `id: "../escape"` (or `id: "foo/bar"`, or `id: "foo\bar"`, or `id` containing a null byte),
When the workflow is loaded,
Then the entire workflow fails validation,
And the error identifies the step ID as containing illegal characters.

**AC6 — Nested traversal rejected**
Given a step declares `agent: "foo/../../bar"`,
When the workflow is loaded,
Then validation rejects it (contains `..` as a path segment regardless of surrounding path components).

**AC7 — Valid workflows unaffected (no regression)**
Given the existing CQA and Accessibility Quick-Scan workflows with paths like `agents/scanner.md`, `artifacts/scan-results.md`, `reads_from: ["artifacts/scan-results.md"]`,
When the workflows are loaded,
Then validation succeeds as it does today.

**AC8 — Defence-in-depth canonicalisation in `PromptAssembler`**
Given a workflow-defined `agent` value that somehow passes the structural validator (e.g. via symlink or future bypass),
When `PromptAssembler` resolves the path via `Path.Combine(workflowFolderPath, step.Agent)`,
Then the resolved canonical path is verified to start with the workflow folder path,
And a traversal attempt throws `UnauthorizedAccessException`.
The same defence applies to `reads_from` artifact paths resolved against the instance folder at lines 138 and 165.

**AC9 — Defence-in-depth canonicalisation in `ArtifactValidator`**
Given a `reads_from` value passes the structural validator,
When `ArtifactValidator` resolves it via `Path.Combine(instanceFolderPath, file)`,
Then the canonical path is verified to start with the instance folder path.

**AC10 — Defence-in-depth canonicalisation in `CompletionChecker`**
Given a `files_exist` value passes the structural validator,
When `CompletionChecker` resolves it via `Path.Combine(instanceFolderPath, file)`,
Then the canonical path is verified to start with the instance folder path.

**AC11 — Defence-in-depth check in `ConversationStore`**
Given a step ID passes the structural validator,
When `ConversationStore` uses it in the filename `conversation-{stepId}.jsonl`,
Then the step ID is verified to not contain `/`, `\`, or null bytes (redundant with AC5 but enforced at the consumer as defence-in-depth).

**AC12 — All tests pass, no regressions**
Given all changes are complete,
When `dotnet test AgentRun.Umbraco.slnx` runs,
Then all tests pass (currently 465 — must remain 465+),
And the two shipped workflow YAMLs (CQA + Accessibility) validate without errors.

## Tasks / Subtasks

> **Phase ordering: Task 1 (structural validation in WorkflowValidator) → Task 2 (defence-in-depth in consumers) → Task 3 (tests) → Task 4 (regression check + existing workflow canary).**

### Task 1 — Structural path validation in `WorkflowValidator.ValidateStep()` (AC1–AC6)

Add path-safety validation to [WorkflowValidator.cs](AgentRun.Umbraco/Workflows/WorkflowValidator.cs). All new validation goes inside or is called from `ValidateStep()` (line 223).

**1.1 — Add a private static helper method `ValidatePathSafety`**

Signature: `private static bool ValidatePathSafety(string value, string fieldPath, List<WorkflowValidationError> errors, bool allowPathSeparators)`

Rules:
- Reject if value contains null byte (`\0`).
- Reject if value starts with `/` or `\` (absolute Unix/Windows path).
- Reject if value matches a Windows drive letter pattern (e.g. `C:`, `D:\`).
- Reject if any path segment (split on `/` and `\`) equals `..` (handles `../`, `..\`, `foo/../../bar`, standalone `..`).
- When `allowPathSeparators` is `false`: reject if value contains `/` or `\` (used for step IDs only).
- Return `true` if the value is safe, `false` if an error was added.
- Error message format: `"Field '{fieldPath}' contains an unsafe path value '{value}': {reason}"` — name the field and the value so the workflow author can find and fix the problem.

**1.2 — Validate `agent` field** (after existing non-empty check at line 248)

If `agentValue` is a non-empty string, call `ValidatePathSafety(agentStr, $"steps[{index}].agent", errors, allowPathSeparators: true)`.

**1.3 — Validate `reads_from` entries** (new block after agent validation)

If `reads_from` exists and is a `List<object>`, iterate entries. For each string entry, call `ValidatePathSafety(entry, $"steps[{index}].reads_from[{j}]", errors, allowPathSeparators: true)`.

**1.4 — Validate `writes_to` entries** (same pattern as reads_from)

If `writes_to` exists and is a `List<object>`, iterate entries. For each string entry, call `ValidatePathSafety(entry, $"steps[{index}].writes_to[{j}]", errors, allowPathSeparators: true)`.

**1.5 — Validate `completion_check.files_exist` entries** (extend the existing files_exist validation block at line 269)

After the existing non-empty list check, iterate the `filesList` entries. For each string entry, call `ValidatePathSafety(entry, $"steps[{index}].completion_check.files_exist[{j}]", errors, allowPathSeparators: true)`.

**1.6 — Validate step `id`** (extend existing id validation at line 229)

After extracting `stepId` (non-null, non-empty), call `ValidatePathSafety(stepId, $"steps[{index}].id", errors, allowPathSeparators: false)`. The `allowPathSeparators: false` flag rejects `/` and `\` in step IDs since they are filename components.

### Task 2 — Defence-in-depth canonicalisation in consumers (AC8–AC11)

These are redundant safety nets. Each consumer already has a `Path.Combine` call — add canonicalisation and containment check immediately after.

**2.1 — `PromptAssembler.cs` agent path (line 27)**

After `var agentPath = Path.Combine(context.WorkflowFolderPath, context.Step.Agent);`, add:
```csharp
var canonicalAgentPath = Path.GetFullPath(agentPath);
var canonicalWorkflowFolder = Path.GetFullPath(context.WorkflowFolderPath);
if (!canonicalAgentPath.StartsWith(canonicalWorkflowFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
{
    throw new UnauthorizedAccessException(
        $"Access denied: agent path '{context.Step.Agent}' resolves outside the workflow folder");
}
```

**2.2 — `PromptAssembler.cs` artifact paths (lines 138, 165)**

Add a private static helper `ValidateWithinInstanceFolder(string fullPath, string instanceFolderPath, string artifactPath)` that calls `Path.GetFullPath()` and checks containment. Call it after each `Path.Combine(context.InstanceFolderPath, artifactPath)`.

**2.3 �� `ArtifactValidator.cs` (line 28)**

After `var fullPath = Path.Combine(instanceFolderPath, file);`, canonicalise and verify containment within `instanceFolderPath`. Throw `UnauthorizedAccessException` on escape.

**2.4 — `CompletionChecker.cs` (line 28)**

Same pattern as ArtifactValidator — canonicalise `fullPath` and verify within `instanceFolderPath`.

**2.5 — `ConversationStore.cs` (line 172)**

Before constructing the filename, validate that `stepId` does not contain `/`, `\`, or `\0`. This is a redundant check (the validator already rejected it) but defends against future callers that bypass the validator. Throw `ArgumentException` on violation.

### Task 3 — Tests (AC1–AC12)

**3.1 — New path traversal tests in `WorkflowValidatorTests.cs`**

Add tests for every rejection rule. Use inline YAML (matching the existing test style). Each test constructs a YAML string with the offending value and asserts `result.IsValid` is `false` and the error message contains the field path and value.

Minimum test cases:
- `agent: "../../etc/passwd"` → rejected (traversal)
- `agent: "/etc/passwd"` → rejected (absolute Unix)
- `agent: "C:\\Windows\\System32\\cmd.exe"` → rejected (absolute Windows)
- `agent: "foo/../../bar"` → rejected (nested traversal)
- `reads_from: ["../../../appsettings.json"]` → rejected
- `writes_to: ["/etc/crontab"]` → rejected
- `completion_check.files_exist: ["../../secret.txt"]` → rejected
- Step `id: "../escape"` → rejected (contains path separator)
- Step `id: "foo/bar"` → rejected (contains path separator)
- Step `id: "valid-step"` with safe paths → passes (regression guard)
- Valid workflow with `agent: "agents/scanner.md"`, `reads_from: ["artifacts/scan-results.md"]` → passes
- `agent: "..hidden-folder/file.md"` → passes (not a traversal — `..hidden-folder` is a valid directory name, not a `..` segment)

**3.2 — Defence-in-depth tests in consumer test files**

- `PromptAssemblerTests.cs`: test that a `step.Agent` value resolving outside the workflow folder throws `UnauthorizedAccessException`.
- `ArtifactValidatorTests.cs`: test that a `reads_from` value resolving outside the instance folder throws `UnauthorizedAccessException`.
- `CompletionCheckerTests.cs`: test that a `files_exist` value resolving outside the instance folder throws `UnauthorizedAccessException`.
- `ConversationStoreTests.cs`: test that a step ID containing `/` throws `ArgumentException`.

**3.3 — Shipped workflow canary**

Load both shipped workflow YAML files (CQA + Accessibility Quick-Scan) and assert they validate without errors. This is the same canary pattern used in Story 9.2's `WorkflowSchemaTests`.

### Task 4 — Regression check

Run `dotnet test AgentRun.Umbraco.slnx`. All tests pass (465+). Verify the two shipped workflows still load correctly in the TestSite.

## Dev Notes

### Files in scope

**EDIT (engine — structural validation):**
- [WorkflowValidator.cs](AgentRun.Umbraco/Workflows/WorkflowValidator.cs) — add `ValidatePathSafety` helper, call it from `ValidateStep()` for `agent`, `reads_from`, `writes_to`, `files_exist`, and step `id`

**EDIT (engine — defence-in-depth consumers):**
- [PromptAssembler.cs](AgentRun.Umbraco/Engine/PromptAssembler.cs) — canonicalise + containment check at lines 27, 138, 165
- [ArtifactValidator.cs](AgentRun.Umbraco/Engine/ArtifactValidator.cs) — canonicalise + containment check at line 28
- [CompletionChecker.cs](AgentRun.Umbraco/Engine/CompletionChecker.cs) — canonicalise + containment check at line 28
- [ConversationStore.cs](AgentRun.Umbraco/Instances/ConversationStore.cs) — step ID character check at line 172

**EDIT (tests):**
- [WorkflowValidatorTests.cs](AgentRun.Umbraco.Tests/Workflows/WorkflowValidatorTests.cs) — add ~12 path traversal rejection tests + regression guard tests
- Consumer test files for defence-in-depth assertions (PromptAssemblerTests, ArtifactValidatorTests, CompletionCheckerTests, ConversationStoreTests)

### What NOT to build

- Do NOT modify `PathSandbox.cs` — the existing runtime tool validation is correct; this story adds the definition-layer validation that was missing.
- Do NOT add path validation to the JSON Schema (`workflow-schema.json`) — JSON Schema `pattern` constraints are too weak for proper path validation; this is code-level validation.
- Do NOT modify any tool implementation (`ReadFileTool`, `WriteFileTool`, `ListFilesTool`, `FetchUrlTool`) — those are already correctly sandboxed.
- Do NOT modify any workflow YAML files or agent prompt files.
- Do NOT add new configuration options — these are unconditional security rules, not tunables.
- Do NOT create new classes — all validation goes in the existing `WorkflowValidator.ValidateStep()` method as a private static helper. Defence-in-depth goes inline (or as a small private helper) in each consumer.

### Failure & Edge Cases

- `agent: "../../etc/passwd"` → rejected at workflow load with clear error message naming the offending field and value.
- `reads_from: ["../../../appsettings.json"]` → rejected at workflow load.
- Step `id: "../escape"` → rejected at workflow load (contains path separator).
- Step `id: "valid-step"` with `writes_to: ["/etc/crontab"]` → rejected (absolute path).
- Workflow with one valid step and one invalid step → entire workflow fails registration (fail-fast, not partial).
- Nested traversal like `foo/../../bar` → rejected (contains `..` segment).
- Consumer receives a value that passed validation but still escapes after `Path.GetFullPath()` resolution (e.g. via symlink) → defence-in-depth canonicalisation catches it and throws `UnauthorizedAccessException`.
- `agent: "..hidden-folder/file.md"` → passes. `..hidden-folder` is a valid directory name, not a traversal segment. The check splits on path separators and only rejects segments that are exactly `..`.
- Encoded path separators (`%2F`, `%5C`), unicode normalisation attacks → the structural check operates on raw string bytes. URL-encoded separators are literal characters in a filesystem path and would not resolve to separators. If somehow they did, the defence-in-depth `Path.GetFullPath()` canonicalisation catches the escape. Null bytes are explicitly rejected.
- **Deny-by-default:** unrecognised or structurally suspect path patterns are rejected. If in doubt, reject.

### Existing code patterns to follow

- Error reporting: use `WorkflowValidationError(fieldPath, message)` — same as all existing validation errors in `WorkflowValidator`.
- Defence-in-depth: mirror the `PathSandbox.ValidatePath()` pattern (canonicalise → containment check → symlink check) but without the symlink check in consumers (symlinks are a runtime concern handled by `PathSandbox` when tools execute).
- Platform-aware comparison: use `StringComparison.Ordinal` on Linux, `StringComparison.OrdinalIgnoreCase` on Windows — but for the structural validator, `Ordinal` is sufficient since we're checking for literal characters (`..`, `/`, `\`), not path resolution. The defence-in-depth consumers should use `RuntimeInformation.IsOSPlatform()` for the containment check if crossing platforms matters, but `Ordinal` is acceptable on macOS/Linux targets.

### Project Structure Notes

- `WorkflowValidator` is in `AgentRun.Umbraco/Workflows/` — engine boundary, no Umbraco dependencies.
- All consumer files (`PromptAssembler`, `ArtifactValidator`, `CompletionChecker`) are in `AgentRun.Umbraco/Engine/` — engine boundary.
- `ConversationStore` is in `AgentRun.Umbraco/Instances/` — engine boundary.
- Test mirrors: `AgentRun.Umbraco.Tests/Workflows/`, `AgentRun.Umbraco.Tests/Engine/`, `AgentRun.Umbraco.Tests/Instances/`.

### References

- [epics.md Story 9.10 (line 1716)](../planning-artifacts/epics.md#L1716) — full threat model and architect's locked decisions
- [architecture.md Security section (lines 327-362)](../planning-artifacts/architecture.md#L327) — NFR6, path sandboxing pattern
- [PathSandbox.cs](AgentRun.Umbraco/Security/PathSandbox.cs) — existing runtime sandboxing (reference implementation, do not modify)
- [project-context.md](../project-context.md) — FR22 (sandbox file operations), NFR6 (path sandboxing), "Security code: deny by default" process rule

## Dev Agent Record

### Agent Model Used

### Completion Notes

### File List
