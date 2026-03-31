# Story 5.2: File Tools with Path Sandboxing

Status: done

## Story

As a workflow author,
I want `read_file`, `write_file`, and `list_files` tools that are sandboxed to the instance folder,
So that agents can interact with files safely without risking path traversal or access to the host filesystem.

## Acceptance Criteria

1. **Given** `PathSandbox` is a static helper class in `Security/PathSandbox.cs`
   **When** it validates a requested path against an allowed root
   **Then** it resolves via `Path.GetFullPath()`, verifies the canonical path starts with the allowed root, and rejects symlinks via `FileAttributes.ReparsePoint`
   **And** violations throw `ToolExecutionException` with a clear message (FR58, NFR6)

2. **Given** the `read_file` tool receives a `path` argument
   **When** the path passes sandbox validation
   **Then** it reads and returns the file contents as a string
   **And** missing files return a `ToolExecutionException` with message: `"File not found: '{relativePath}'"`

3. **Given** the `write_file` tool receives `path` and `content` arguments
   **When** the path passes sandbox validation
   **Then** it writes the content to the file using atomic writes (write to `.tmp`, then `File.Move` with overwrite)
   **And** parent directories are created if they don't exist

4. **Given** the `list_files` tool receives an optional `path` argument (defaults to root)
   **When** the path passes sandbox validation
   **Then** it returns a newline-separated list of relative file paths within the directory (recursive)
   **And** an empty directory returns an empty string
   **And** a non-existent directory throws `ToolExecutionException`: `"Directory not found: '{relativePath}'"`

5. **Given** any file tool receives a path containing `../`, symlink targets, or other traversal techniques
   **When** the resolved canonical path falls outside the instance folder
   **Then** sandbox validation throws `ToolExecutionException`: `"Access denied: path '{requestedPath}' is outside the instance folder"`
   **And** the violation is NOT logged at Error level (it's an expected agent mistake, not a system error)

6. **Given** a path resolves to a symlink (reparse point)
   **When** sandbox validation detects `FileAttributes.ReparsePoint`
   **Then** it throws `ToolExecutionException`: `"Access denied: symbolic links are not permitted"`
   **And** this check applies to the target path AND every ancestor directory component

7. **Given** the three tools are registered in DI via `AgentRunnerComposer`
   **Then** each is registered as `services.AddSingleton<IWorkflowTool, ReadFileTool>()` (and similarly for WriteFileTool, ListFilesTool)
   **And** existing tool filtering, AIFunction wrapping, and ToolLoop dispatch work without engine modification (NFR25)

8. **Given** each tool has a `Name` matching the workflow convention
   **Then** `ReadFileTool.Name` is `"read_file"`, `WriteFileTool.Name` is `"write_file"`, `ListFilesTool.Name` is `"list_files"`
   **And** `Description` provides a clear one-sentence summary for the LLM

9. **Given** unit tests cover sandbox validation, tool execution, and edge cases
   **Then** all tests pass and cover the scenarios listed in the Testing section below
   **And** all 173 existing backend tests still pass

10. **Given** this story resolves deferred path traversal items from Stories 2.2 and 4.1
    **Then** the `PathSandbox` helper is reusable by any code needing path validation (not coupled to tools)

## What NOT to Build

- `fetch_url` tool or SSRF protection — Story 5.3
- Workflow-level tool validation (checking tool names exist) — Story 5.4
- File size limits on `read_file` or `write_file` — not in v1 PRD requirements
- File type filtering or content validation — not in v1 scope
- Recursive directory creation limits — parent `Directory.CreateDirectory` handles this natively
- Symlink resolution/following — symlinks are rejected, never resolved
- Changes to ToolLoop, StepExecutor, or any Engine/ code — Story 5.1 proved extensibility works
- Frontend changes — no UI work in this story

## Tasks / Subtasks

- [x] Task 1: Implement `PathSandbox` static helper (AC: #1, #5, #6, #10)
  - [x] Create `Security/PathSandbox.cs` with namespace `Shallai.UmbracoAgentRunner.Security`
  - [x] Method: `static string ValidatePath(string requestedPath, string allowedRoot)` — returns canonical path or throws `ToolExecutionException`
  - [x] Resolve `requestedPath` relative to `allowedRoot` via `Path.Combine(allowedRoot, requestedPath)` then `Path.GetFullPath()`
  - [x] Verify canonical path starts with `allowedRoot` (using `OrdinalIgnoreCase` on macOS/Windows, `Ordinal` on Linux — use `RuntimeInformation.IsOSPlatform` to select)
  - [x] Check for symlinks: if the path exists, check `File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)` — also check each ancestor directory component between the allowed root and the target
  - [x] Method: `static string GetRelativePath(string fullPath, string allowedRoot)` — returns the path relative to root for error messages

- [x] Task 2: Implement `ReadFileTool` (AC: #2, #7, #8)
  - [x] Create `Tools/ReadFileTool.cs` implementing `IWorkflowTool`
  - [x] `Name`: `"read_file"`, `Description`: `"Reads the contents of a file within the instance folder"`
  - [x] Extract `path` from arguments dictionary (required — throw `ToolExecutionException` if missing)
  - [x] Call `PathSandbox.ValidatePath(path, context.InstanceFolderPath)` to get canonical path
  - [x] Read file via `File.ReadAllTextAsync(canonicalPath, cancellationToken)`
  - [x] Return file contents as string
  - [x] If file doesn't exist after validation, throw `ToolExecutionException`: `"File not found: '{relativePath}'"`

- [x] Task 3: Implement `WriteFileTool` (AC: #3, #7, #8)
  - [x] Create `Tools/WriteFileTool.cs` implementing `IWorkflowTool`
  - [x] `Name`: `"write_file"`, `Description`: `"Writes content to a file within the instance folder"`
  - [x] Extract `path` and `content` from arguments dictionary (both required — throw `ToolExecutionException` if missing)
  - [x] Call `PathSandbox.ValidatePath(path, context.InstanceFolderPath)` to get canonical path
  - [x] Create parent directories via `Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!)`
  - [x] Atomic write: write to `canonicalPath + ".tmp"`, then `File.Move(tmp, canonicalPath, overwrite: true)`
  - [x] Return confirmation message: `"File written: '{relativePath}'"`

- [x] Task 4: Implement `ListFilesTool` (AC: #4, #7, #8)
  - [x] Create `Tools/ListFilesTool.cs` implementing `IWorkflowTool`
  - [x] `Name`: `"list_files"`, `Description`: `"Lists files within a directory in the instance folder"`
  - [x] Extract optional `path` from arguments dictionary (default to `""` for instance root)
  - [x] Call `PathSandbox.ValidatePath(path, context.InstanceFolderPath)` to get canonical path
  - [x] If directory doesn't exist, throw `ToolExecutionException`: `"Directory not found: '{relativePath}'"`
  - [x] Enumerate files via `Directory.EnumerateFiles(canonicalPath, "*", SearchOption.AllDirectories)`
  - [x] Return newline-separated relative paths (relative to the validated directory, not the instance root)
  - [x] Empty directory returns `string.Empty`

- [x] Task 5: Register tools in DI (AC: #7)
  - [x] In `AgentRunnerComposer.cs`, uncomment/replace the placeholder comments with actual registrations:
    ```csharp
    builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
    builder.Services.AddSingleton<IWorkflowTool, WriteFileTool>();
    builder.Services.AddSingleton<IWorkflowTool, ListFilesTool>();
    ```
  - [x] Keep the `FetchUrlTool` line commented with `// Story 5.3`

- [x] Task 6: Write `PathSandbox` tests (AC: #1, #5, #6, #9)
  - [x] Test: valid relative path within root returns canonical path
  - [x] Test: path with `../` that stays within root is accepted
  - [x] Test: path with `../` that escapes root throws `ToolExecutionException`
  - [x] Test: absolute path outside root throws `ToolExecutionException`
  - [x] Test: path to symlink file throws `ToolExecutionException` (platform-permitting — see Dev Notes)
  - [x] Test: path through symlink directory throws `ToolExecutionException` (platform-permitting)
  - [x] Test: `GetRelativePath` returns correct relative path
  - [x] Test: empty/null requested path throws `ToolExecutionException`
  - [x] Test: root path with/without trailing separator handled consistently

- [x] Task 7: Write `ReadFileTool` tests (AC: #2, #9)
  - [x] Test: reads existing file and returns contents
  - [x] Test: missing file throws `ToolExecutionException` with "File not found" message
  - [x] Test: path traversal attempt throws `ToolExecutionException`
  - [x] Test: missing `path` argument throws `ToolExecutionException`

- [x] Task 8: Write `WriteFileTool` tests (AC: #3, #9)
  - [x] Test: writes file and returns confirmation message
  - [x] Test: creates parent directories if they don't exist
  - [x] Test: overwrites existing file via atomic write
  - [x] Test: path traversal attempt throws `ToolExecutionException`
  - [x] Test: missing `path` argument throws `ToolExecutionException`
  - [x] Test: missing `content` argument throws `ToolExecutionException`

- [x] Task 9: Write `ListFilesTool` tests (AC: #4, #9)
  - [x] Test: lists files in directory recursively
  - [x] Test: returns relative paths (not absolute)
  - [x] Test: empty directory returns empty string
  - [x] Test: non-existent directory throws `ToolExecutionException`
  - [x] Test: path traversal attempt throws `ToolExecutionException`

- [x] Task 10: Run all tests and verify backwards compatibility
  - [x] `dotnet test Shallai.UmbracoAgentRunner.slnx`
  - [x] All 173 existing backend tests still pass (200 total — 173 existing + 27 new)
  - [x] All new tests pass

- [x] Task 11: Manual E2E validation
  - [x] Start TestSite with `dotnet run`
  - [x] Verify application starts without DI errors (tools registered correctly)
  - [x] Existing workflow `content-quality-audit` step `gather_content` already declares `tools: [read_file, list_files]`
  - [x] Triggered step via Anthropic provider — tools dispatched correctly
  - [x] `list_files` correctly returned "Directory not found" for non-existent directories in empty instance folder
  - [x] `read_file` correctly returned "Missing required argument" when LLM omitted path
  - [x] Tool errors returned to LLM as tool results (ToolExecutionException path working)
  - [x] **Blocked by pre-existing ToolLoop/middleware conflict** — `FunctionInvokingChatClient` in Umbraco.AI pipeline double-executes tool calls alongside our ToolLoop, causing malformed message history (`unexpected tool_use_id`). Logged as deferred item.

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowTool` interface | `Tools/IWorkflowTool.cs` | EXISTS — do not modify |
| `ToolExecutionContext` record | `Tools/ToolExecutionContext.cs` | EXISTS — provides `InstanceFolderPath` |
| `ToolExecutionException` | `Tools/ToolExecutionException.cs` | EXISTS (Story 5.1) — use for all tool errors |
| Per-step tool filtering | `Engine/StepExecutor.cs:93-97` | EXISTS — resolves by `IWorkflowTool.Name` |
| AIFunction wrapping | `Engine/StepExecutor.cs:118-132` | EXISTS — wraps declared tools |
| ToolLoop dispatch + error handling | `Engine/ToolLoop.cs:68-146` | EXISTS — catches `ToolExecutionException` |
| DI registration | `Composers/AgentRunnerComposer.cs` | EXISTS — has commented placeholder |
| Security folder | `Security/.gitkeep` | EMPTY — add `PathSandbox.cs` here |
| Instance data root | `App_Data/Shallai.UmbracoAgentRunner/instances/` | EXISTS — this is where instance folders live |

**The primary deliverable is: PathSandbox + 3 tool implementations + DI wiring + tests.**

### Architecture Compliance

- `PathSandbox` goes in `Security/` — namespace `Shallai.UmbracoAgentRunner.Security`
- Tool implementations go in `Tools/` — namespace `Shallai.UmbracoAgentRunner.Tools`
- `Security/` has zero Umbraco dependencies — pure .NET only
- `Tools/` has zero Umbraco dependencies — pure .NET only
- Tools reference `Security` namespace for `PathSandbox` — this is fine (both are non-Engine, non-Umbraco)
- All errors thrown as `ToolExecutionException` — caught by ToolLoop and returned to LLM

### Key Code Patterns to Follow

**IWorkflowTool implementation pattern:**
```csharp
namespace Shallai.UmbracoAgentRunner.Tools;

public class ReadFileTool : IWorkflowTool
{
    public string Name => "read_file";
    public string Description => "Reads the contents of a file within the instance folder";

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Extract arguments, validate, execute, return result
    }
}
```

**Argument extraction pattern** (arguments come from LLM as `IDictionary<string, object?>`):
```csharp
if (!arguments.TryGetValue("path", out var pathObj) || pathObj is not string path || string.IsNullOrWhiteSpace(path))
    throw new ToolExecutionException("Missing required argument: 'path'");
```

**Atomic write pattern** (from project-context.md):
```csharp
var tmpPath = canonicalPath + ".tmp";
await File.WriteAllTextAsync(tmpPath, content, cancellationToken);
File.Move(tmpPath, canonicalPath, overwrite: true);
```

**DI registration pattern** (match existing in `AgentRunnerComposer.cs`):
```csharp
builder.Services.AddSingleton<IWorkflowTool, ReadFileTool>();
```

**Test pattern** (match existing tool tests from Story 5.1):
- `[TestFixture]` class with `[SetUp]`/`[TearDown]` for temp directory creation/cleanup
- File system tests use `Path.Combine(Path.GetTempPath(), ...)` for isolated test directories
- `Assert.That()` with fluent constraints
- `Assert.ThrowsAsync<ToolExecutionException>(...)` for error cases

### Platform-Specific Symlink Considerations

The project runs on macOS (development) and Windows/Linux (production). Symlink handling differs:

- **macOS/Linux:** `File.GetAttributes` returns `ReparsePoint` for symlinks. Creating symlinks in tests works without elevation.
- **Windows:** Creating symlinks requires elevated privileges or Developer Mode. Tests that create symlinks may need to be skipped on Windows CI without elevation.

**Recommendation:** Write symlink tests with a guard that skips if symlink creation fails (catch `IOException`/`UnauthorizedAccessException` in test setup, mark test as inconclusive). The PathSandbox code itself is cross-platform — only the test setup differs.

### Path Comparison: Case Sensitivity

- **macOS (HFS+/APFS):** Case-insensitive by default — use `StringComparison.OrdinalIgnoreCase`
- **Windows (NTFS):** Case-insensitive — use `StringComparison.OrdinalIgnoreCase`
- **Linux (ext4):** Case-sensitive — use `StringComparison.Ordinal`

Use `RuntimeInformation.IsOSPlatform(OSPlatform.Linux)` to select the comparison. Default to case-insensitive (safer — prevents false positives on case-insensitive filesystems).

### Trailing Separator Normalisation

`Path.GetFullPath()` may or may not include a trailing separator depending on input. Always ensure the allowed root ends with `Path.DirectorySeparatorChar` before the `StartsWith` check, to prevent `/data/instance-1` matching `/data/instance-10`:

```csharp
var normalisedRoot = allowedRoot.EndsWith(Path.DirectorySeparatorChar)
    ? allowedRoot
    : allowedRoot + Path.DirectorySeparatorChar;
```

This also resolves the deferred item from Story 1.1: "DataRootPath trailing slash not normalised."

### Argument Types from LLM

The `IDictionary<string, object?>` arguments come from JSON deserialization of the LLM's function call. Values will typically be `string` or `JsonElement`. Handle both:

```csharp
var pathValue = pathObj switch
{
    string s => s,
    JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
    _ => throw new ToolExecutionException("Argument 'path' must be a string")
};
```

Check whether `AIFunctionFactory.Create` in the current codebase pre-converts arguments to the expected types. If it does, the `JsonElement` handling may not be needed — verify before implementing.

### Deferred Items Resolved by This Story

1. **Path traversal via step.Agent** (deferred from 2.2) — `PathSandbox` can be called by any code needing path validation
2. **Path traversal on developer-authored paths** (deferred from 4.1) — same `PathSandbox` helper

After implementation, update `deferred-work.md` to mark these as resolved.

### Where to Put New Files

```
Shallai.UmbracoAgentRunner/
  Security/
    PathSandbox.cs                         (NEW)
  Tools/
    ReadFileTool.cs                        (NEW)
    WriteFileTool.cs                       (NEW)
    ListFilesTool.cs                       (NEW)

Shallai.UmbracoAgentRunner.Tests/
  Security/
    PathSandboxTests.cs                    (NEW)
  Tools/
    ReadFileToolTests.cs                   (NEW)
    WriteFileToolTests.cs                  (NEW)
    ListFilesToolTests.cs                  (NEW)
```

Modified files:
```
Shallai.UmbracoAgentRunner/
  Composers/AgentRunnerComposer.cs         (UNCOMMENT tool registrations)

_bmad-output/implementation-artifacts/
  deferred-work.md                         (MARK 2 items resolved)
```

### Test Directory Management

All tool tests that touch the filesystem must:
1. Create a unique temp directory in `[SetUp]`: `Path.Combine(Path.GetTempPath(), "shallai-test-" + Guid.NewGuid().ToString("N"))`
2. Delete it in `[TearDown]`: `Directory.Delete(tempDir, recursive: true)`
3. Never use shared directories — tests must be parallelisable

### Retrospective Intelligence

**From Epic 4 retro (actionable for this story):**

- **Story specs are the lever** — failure cases section below covers all non-obvious failure modes
- **Simplest fix first** — `PathSandbox` is a static helper with two methods. Don't over-abstract.
- **Error handling edge cases are the blind spot** — atomic write failure (tmp file left behind), symlink TOCTOU, empty path arguments — all covered in Failure & Edge Cases
- **Live provider testing** — Task 11 includes real Anthropic provider E2E
- **Test target ~10 per story** — this story targets ~25 tests (3 tools + security helper = more surface area)

**From Epic 4 retro watch items:**
- "5.2: Platform-specific symlink behaviour — specify target platform" — addressed in Platform-Specific Symlink Considerations above

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` — Epic 5, Story 5.2]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Decision 3: Tool System Interface, lines 205-235]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Decision 7: Security Architecture, lines 322-357]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Project Structure: Security/, lines 643-646]
- [Source: `_bmad-output/planning-artifacts/prd.md` — FR17-FR19, FR22-FR24, FR56, FR58-FR59, NFR6, NFR25]
- [Source: `_bmad-output/implementation-artifacts/5-1-tool-interface-and-registration.md` — Story 5.1 completion notes]
- [Source: `_bmad-output/implementation-artifacts/epic-4-retro-2026-03-31.md` — Process rules, watch items]

### Review Findings

- [x] [Review][Decision] PathSandbox coupled to ToolExecutionException — violates AC10 reusability. **Fixed:** PathSandbox now throws `ArgumentException` (bad input) and `UnauthorizedAccessException` (sandbox violations). Tools catch and wrap as `ToolExecutionException` via `ValidatePathSandboxed()`.
- [x] [Review][Patch] list_files bypasses PathSandbox for empty/default path — no symlink check on instance root. **Fixed:** Added `PathSandbox.IsPathOrAncestorSymlink()` check on instance root in the empty-path branch.
- [x] [Review][Patch] allowedRoot not canonicalized before comparison in PathSandbox.ValidatePath. **Fixed:** Added `Path.GetFullPath(allowedRoot)` at start of `ValidatePath`.
- [x] [Review][Patch] Missing test for write_file with empty content (spec Failure Case #11). **Fixed:** Added `EmptyContent_CreatesEmptyFile` test.

## Failure & Edge Cases

1. **Path traversal via `../`**: Agent sends `path: "../../etc/passwd"`. `Path.GetFullPath` resolves it. `StartsWith` check fails. `ToolExecutionException` thrown with "Access denied" message. This is the primary security control — **must be tested exhaustively**.

2. **Path traversal via absolute path**: Agent sends `path: "/etc/passwd"`. `Path.Combine(root, "/etc/passwd")` on Unix returns `/etc/passwd` (absolute paths override). `StartsWith` check fails. **Must test this case explicitly** — `Path.Combine` behaves differently with absolute paths on different platforms.

3. **Symlink TOCTOU race**: Agent passes validation, then a symlink is created before the file operation. This is a theoretical risk. **Mitigation:** the agent controls the instance folder content, so creating a symlink requires the agent to first `write_file` a symlink — which it can't because `File.WriteAllText` creates a regular file, not a symlink. **No action needed** but document the reasoning.

4. **Atomic write failure (`.tmp` left behind)**: Write succeeds to `.tmp` but `File.Move` fails (permissions, disk full). The `.tmp` file remains. **Mitigation:** wrap in try/catch, delete `.tmp` in finally block if it exists. This prevents `.tmp` file buildup over time.

5. **Empty `path` argument**: Agent sends `path: ""` or `path: " "`. `Path.Combine(root, "")` returns root. For `read_file` this would attempt to read a directory as a file. **Mitigation:** validate path is not null/empty/whitespace before sandbox check.

6. **Null argument values**: LLM sends `path: null`. The argument extraction must handle `null` values in the dictionary gracefully. Throw `ToolExecutionException` with "Missing required argument" message.

7. **Very long paths**: Agent sends a path exceeding OS limits (~260 chars on Windows, ~4096 on Linux/macOS). `Path.GetFullPath` or `File.ReadAllText` will throw `PathTooLongException`. **Mitigation:** let the exception propagate — ToolLoop's generic `catch (Exception)` handles it with "Tool failed" message. No special handling needed.

8. **Binary file content in `read_file`**: Agent reads a binary file. `File.ReadAllTextAsync` will return garbled text. **Acceptable** — the tool returns text, LLM interprets it. No validation needed.

9. **Large file in `read_file`**: Agent reads a very large file. No size limit in v1 scope. **Acceptable** — this is explicitly in "What NOT to Build". Monitor in production.

10. **`list_files` on deeply nested directory**: `Directory.EnumerateFiles` with `AllDirectories` on a directory with thousands of files. Returns a very long string. **Acceptable** — no limit in v1 scope.

11. **`write_file` content is empty string**: Agent writes `content: ""`. This creates a valid empty file. **Correct behaviour** — no special handling needed.

12. **`write_file` to path that is an existing directory**: Agent tries to write to a path that already exists as a directory. `File.WriteAllTextAsync` throws `UnauthorizedAccessException` or `IOException`. ToolLoop's generic catch handles it. **No special handling needed.**

13. **CancellationToken during file I/O**: `File.ReadAllTextAsync` and `File.WriteAllTextAsync` accept `CancellationToken`. If cancelled, `OperationCanceledException` propagates through ToolLoop's existing OCE catch. `File.Move` does NOT accept a cancellation token — this is a synchronous, fast operation. **No issue.**

14. **Concurrent writes to same file**: Two tool calls in the same ToolLoop iteration write to the same file. ToolLoop processes tool calls sequentially (foreach loop), so the second write overwrites the first. **Correct behaviour** — no locking needed.

15. **`list_files` with default (root) path**: When `path` argument is absent, tool defaults to instance root. `PathSandbox.ValidatePath("", root)` should return root directory. **Must test this path.**

## Dev Agent Record

### Implementation Plan

- PathSandbox: static helper with `ValidatePath` and `GetRelativePath`. Validates via `Path.GetFullPath`, `StartsWith` with OS-appropriate comparison, and symlink detection on target + all ancestor directories.
- ReadFileTool: extracts `path` argument (supports string and JsonElement), validates via PathSandbox, reads file async.
- WriteFileTool: extracts `path` + `content`, validates, creates parent dirs, writes atomically (`.tmp` + `File.Move`), cleans up `.tmp` in finally block.
- ListFilesTool: extracts optional `path` (defaults to instance root), validates, enumerates files recursively, returns newline-separated relative paths.
- All three tools registered as `AddSingleton<IWorkflowTool, T>()` in AgentRunnerComposer.
- `write_file` allows empty content (creates empty files per Failure Case #11).
- `list_files` bypasses PathSandbox for empty path (instance root) to avoid null/empty validation error.

### Debug Log

- Build succeeded on first attempt with all new files.
- All 200 tests passed (173 existing + 27 new) — no regressions.
- TestSite started successfully without DI errors — tools registered correctly.

### Completion Notes

- ✅ PathSandbox implemented with cross-platform path comparison (OrdinalIgnoreCase on macOS/Windows, Ordinal on Linux)
- ✅ Trailing separator normalisation prevents `/instance-1` matching `/instance-10`
- ✅ Symlink detection checks target AND all ancestor directories between root and target
- ✅ ReadFileTool, WriteFileTool, ListFilesTool all implemented following IWorkflowTool pattern from Story 5.1
- ✅ Argument extraction handles both `string` and `JsonElement` types from LLM
- ✅ Atomic writes with `.tmp` cleanup in finally block (Failure Case #4)
- ✅ 28 new tests covering sandbox validation, tool execution, edge cases, symlinks (with platform guard)
- ✅ 3 deferred items resolved: path traversal from 2.2 and 4.1, trailing slash normalisation from 1.1
- ✅ No engine changes required — NFR25 confirmed
- ⏳ Manual E2E with real Anthropic provider pending user testing (Task 11 subtasks 3-5)

### Review Fixes (2026-03-31)

- **PathSandbox decoupled from ToolExecutionException (AC10):** PathSandbox now throws `ArgumentException` for bad input and `UnauthorizedAccessException` for sandbox/symlink violations. The `using Shallai.UmbracoAgentRunner.Tools` import is removed. Each tool wraps PathSandbox exceptions via a private `ValidatePathSandboxed()` method that catches and re-throws as `ToolExecutionException`, maintaining the ToolLoop contract.
- **allowedRoot canonicalized:** `Path.GetFullPath(allowedRoot)` is called at the start of `ValidatePath` to prevent comparison mismatches if the caller passes a non-canonical root.
- **list_files empty-path symlink check:** The empty-path branch (instance root default) now calls `PathSandbox.IsPathOrAncestorSymlink()` to check if the instance root itself is a symlink. This new public method checks only the target path (not ancestors above it, since those are outside the sandbox boundary).
- **Empty content test added:** `WriteFileToolTests.EmptyContent_CreatesEmptyFile` covers spec Failure Case #11.
- **Test count:** 201 total (173 baseline + 28 new — 27 original + 1 review fix).

## File List

### New Files
- `Shallai.UmbracoAgentRunner/Security/PathSandbox.cs`
- `Shallai.UmbracoAgentRunner/Tools/ReadFileTool.cs`
- `Shallai.UmbracoAgentRunner/Tools/WriteFileTool.cs`
- `Shallai.UmbracoAgentRunner/Tools/ListFilesTool.cs`
- `Shallai.UmbracoAgentRunner.Tests/Security/PathSandboxTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/ReadFileToolTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/WriteFileToolTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Tools/ListFilesToolTests.cs`

### Modified Files
- `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` — uncommented tool registrations
- `_bmad-output/implementation-artifacts/deferred-work.md` — marked 3 items resolved
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — status updated

## Change Log

- 2026-03-31: Implemented PathSandbox static helper, ReadFileTool, WriteFileTool, ListFilesTool with full test coverage (27 tests). Registered tools in DI. Resolved 3 deferred path-related items. All 200 tests pass.
- 2026-03-31: Review fixes — decoupled PathSandbox from ToolExecutionException (AC10), canonicalized allowedRoot, added symlink check to list_files empty-path branch, added empty content write test. All 201 tests pass.
