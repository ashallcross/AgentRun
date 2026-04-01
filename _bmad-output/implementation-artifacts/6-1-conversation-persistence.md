# Story 6.1: Conversation Persistence

Status: done

## Story

As a developer,
I want conversation history persisted incrementally per step in append-only JSONL files,
So that conversations survive crashes and can be reviewed after step completion.

## Acceptance Criteria

1. **Given** a step is executing and the agent produces messages
   **When** each message (agent response, tool result, system) is generated
   **Then** the ConversationStore appends a JSON object per line to `conversation-{stepId}.jsonl` in the instance folder (FR43)
   **And** each JSON object contains: `role` (assistant/user/system/tool), `content`, `timestamp`, and optional tool call data
   **And** the append-only format ensures a crash mid-step loses at most the last partial line — all prior messages are preserved (NFR22)
   **And** one JSONL file is created per step, keeping conversations isolated

2. **Given** a completed or errored step exists with conversation history
   **When** the `GET /umbraco/api/shallai/instances/{id}/conversation/{stepId}` endpoint is called
   **Then** it returns a JSON array of all messages for that step in chronological order (FR44)
   **And** the endpoint requires backoffice authentication (NFR13)

3. **And** the `IConversationStore` interface is registered in `AgentRunnerComposer`
   **And** unit tests verify: append writes, crash-safe partial write handling, per-step isolation, and history retrieval

## What NOT to Build

- **No StepExecutor/ToolLoop integration** — wiring ConversationStore into the tool loop is NOT in scope. This story builds and tests the store in isolation. Integration is a future story.
- **No SSE streaming** — that's Story 6.2
- **No chat panel UI** — that's Story 6.2
- **No user messaging endpoint** — that's Story 6.4
- **No markdown sanitisation** — that's Story 6.4
- **No FR47 rollback** — rolling back failed messages from conversation context is Story 7.2 scope
- **No ChatMessage serialization of Microsoft.Extensions.AI types directly** — use a project-owned DTO (`ConversationEntry`) to avoid leaking SDK types across the Engine boundary (architecture rule: SDK import boundary)
- **No frontend changes**

## Tasks / Subtasks

- [x] Task 1: Create `ConversationEntry` DTO in `Instances/` (AC: #1)
  - [x] Create `Instances/ConversationEntry.cs`
  - [x] Properties: `Role` (string), `Content` (string), `Timestamp` (DateTime), `ToolCallId` (string?), `ToolName` (string?), `ToolArguments` (string?), `ToolResult` (string?)
  - [x] Use `System.Text.Json` `[JsonPropertyName]` attributes with camelCase names for serialisation consistency
  - [x] This is a project-owned DTO — does NOT reference `Microsoft.Extensions.AI.ChatMessage` or any SDK types

- [x] Task 2: Create `IConversationStore` interface in `Instances/` (AC: #1, #3)
  - [x] Create `Instances/IConversationStore.cs`
  - [x] Method: `Task AppendAsync(string workflowAlias, string instanceId, string stepId, ConversationEntry entry, CancellationToken cancellationToken)`
  - [x] Method: `Task<IReadOnlyList<ConversationEntry>> GetHistoryAsync(string workflowAlias, string instanceId, string stepId, CancellationToken cancellationToken)`
  - [x] Namespace: `Shallai.UmbracoAgentRunner.Instances`

- [x] Task 3: Implement `ConversationStore` in `Instances/` (AC: #1, #3)
  - [x] Create `Instances/ConversationStore.cs`
  - [x] Constructor: inject `IInstanceManager` (to call `GetInstanceFolderPath`) and `ILogger<ConversationStore>`
  - [x] Internal test constructor: accept `string dataRootPath` and `ILogger<ConversationStore>` directly (same pattern as `InstanceManager`)
  - [x] `AppendAsync`: serialise `ConversationEntry` to JSON, append line to `conversation-{stepId}.jsonl` using `File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken)` — single call, no temp file needed (append is already atomic at OS level for reasonable line sizes)
  - [x] `GetHistoryAsync`: read all lines from `conversation-{stepId}.jsonl`, deserialise each non-empty line, return as `IReadOnlyList<ConversationEntry>` in file order (chronological)
  - [x] File path: `{instanceFolderPath}/conversation-{stepId}.jsonl`
  - [x] If the JSONL file does not exist in `GetHistoryAsync`, return an empty list (not an error — step may not have started)
  - [x] Skip empty/whitespace-only lines during read (handles trailing newline or crash-corrupted partial line)
  - [x] Use `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` for consistent serialisation
  - [x] Log at Debug level for each append, Warning level for corrupted lines skipped during read

- [x] Task 4: Register `IConversationStore` in `AgentRunnerComposer` (AC: #3)
  - [x] Add `builder.Services.AddSingleton<IConversationStore, ConversationStore>();` after the `IInstanceManager` registration line
  - [x] Add the comment: `// Conversation persistence (JSONL append-only per step)`

- [x] Task 5: Create conversation history endpoint (AC: #2)
  - [x] Create `Endpoints/ConversationEndpoints.cs`
  - [x] Class: `ConversationEndpoints : ControllerBase` with `[ApiController]`, `[Route("umbraco/api/shallai")]`, `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`
  - [x] Constructor inject: `IConversationStore`, `IInstanceManager`
  - [x] Endpoint: `[HttpGet("instances/{instanceId}/conversation/{stepId}")]`
  - [x] First validate that the instance exists via `IInstanceManager.FindInstanceAsync(instanceId)` — return 404 with `ErrorResponse` if not found
  - [x] Then validate that `stepId` exists in the instance's `Steps` list — return 404 with `ErrorResponse` if not found
  - [x] Call `IConversationStore.GetHistoryAsync(instance.WorkflowAlias, instanceId, stepId)` and return `Ok(entries)` (JSON array)
  - [x] Follow existing endpoint pattern from `InstanceEndpoints.cs`

- [x] Task 6: Write `ConversationStore` unit tests (AC: #1, #3)
  - [x] Create `Shallai.UmbracoAgentRunner.Tests/Instances/ConversationStoreTests.cs`
  - [x] Use temp directory pattern from `InstanceManagerTests` (SetUp creates temp dir, TearDown deletes)
  - [x] Create `ConversationStore` using internal test constructor with temp dir path
  - [x] Test: AppendAsync creates JSONL file and writes one JSON line
  - [x] Test: AppendAsync appending multiple entries produces multiple lines in order
  - [x] Test: GetHistoryAsync returns entries in chronological (file) order
  - [x] Test: GetHistoryAsync on non-existent file returns empty list
  - [x] Test: GetHistoryAsync skips empty/whitespace lines (simulates trailing newline)
  - [x] Test: GetHistoryAsync skips corrupted/non-JSON lines (simulates crash mid-write)
  - [x] Test: Per-step isolation — two different stepIds produce two different files
  - [x] Test: ConversationEntry serialises with camelCase property names
  - [x] Test: ConversationEntry with null optional fields omits them from JSON
  - [x] Test: Tool call entry round-trips correctly (ToolCallId, ToolName, ToolArguments, ToolResult all populated)
  - [x] Target: ~10 tests

- [x] Task 7: Write `ConversationEndpoints` unit tests
  - [x] Create `Shallai.UmbracoAgentRunner.Tests/Endpoints/ConversationEndpointsTests.cs`
  - [x] Use NSubstitute mocks for `IConversationStore` and `IInstanceManager`
  - [x] Test: Valid instance and stepId returns 200 with conversation entries
  - [x] Test: Non-existent instance returns 404
  - [x] Test: Instance exists but stepId not in steps list returns 404

- [x] Task 8: Run all tests and verify backwards compatibility
  - [x] `dotnet test Shallai.UmbracoAgentRunner.slnx`
  - [x] All existing backend tests still pass
  - [x] All new tests pass

- [x] Task 9: Manual E2E validation
  - [x] Start TestSite with `dotnet run` from TestSite project
  - [x] Verify application starts without DI errors (ConversationStore resolves correctly)
  - [ ] ~Call `GET /umbraco/api/shallai/instances/{id}/conversation/{stepId}` with a valid instance — verify 200 empty array~ Deferred: direct API calls return 401 due to Bellissima bearer token auth (affects all custom endpoints equally). Will be validated when Story 6.2 chat panel UI consumes this endpoint with Umbraco's auth context.
  - [ ] ~Call with non-existent instance — verify 404~ Deferred: same auth constraint. Covered by unit tests.
  - [ ] ~Manually write a test JSONL file to the instance folder and verify the endpoint returns it~ Deferred: same auth constraint. Covered by unit tests.

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

| Component | File | Status |
|-----------|------|--------|
| `InstanceManager` | `Instances/InstanceManager.cs` | DO NOT MODIFY — use `GetInstanceFolderPath` for path resolution |
| `IInstanceManager` | `Instances/IInstanceManager.cs` | DO NOT MODIFY |
| `InstanceState` | `Instances/InstanceState.cs` | DO NOT MODIFY — reference `Steps` list for stepId validation |
| `StepState` | `Instances/InstanceState.cs` | DO NOT MODIFY — `Id` property is the stepId |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | MODIFY — add ConversationStore registration |
| `InstanceEndpoints` | `Endpoints/InstanceEndpoints.cs` | DO NOT MODIFY — follow as pattern for new endpoint |
| `ErrorResponse` | `Models/ApiModels/ErrorResponse.cs` | DO NOT MODIFY — reuse for 404 responses |
| `InstanceManagerTests` | `Tests/Instances/InstanceManagerTests.cs` | DO NOT MODIFY — follow as test pattern |

**The primary deliverables are: `ConversationEntry` DTO, `IConversationStore` interface, `ConversationStore` implementation, `ConversationEndpoints` controller, DI registration, and tests.**

### Architecture Compliance

- `ConversationEntry` is a **project-owned DTO** in `Instances/` namespace — it does NOT reference `Microsoft.Extensions.AI.ChatMessage`. The SDK import boundary rule forbids leaking SDK types. A future integration story will map `ChatMessage` → `ConversationEntry` in the Engine boundary.
- `ConversationStore` lives in `Instances/` (not `Engine/`) because it's a persistence concern, not execution logic. It mirrors `InstanceManager`'s placement.
- `Engine/` folder is NOT touched — zero changes to `StepExecutor`, `ToolLoop`, `PromptAssembler`, or any engine component.
- The JSONL append pattern (`File.AppendAllTextAsync`) does NOT use the temp-file-then-rename atomic write pattern. Append is inherently safe: a crash mid-write corrupts at most the last partial line, and `GetHistoryAsync` skips corrupted lines. This matches the architecture doc's "crash-safe by design" specification.

### Key Design Decisions

**Why ConversationEntry DTO instead of serialising ChatMessage directly:**
- `ChatMessage` is from `Microsoft.Extensions.AI` — an SDK type that must not leak across boundaries
- `ChatMessage.Contents` is polymorphic (`TextContent`, `FunctionCallContent`, `FunctionResultContent`) — complex to serialise/deserialise reliably with System.Text.Json
- A flat DTO with explicit string fields is simpler, more debuggable, and stable across SDK version changes
- The mapping from `ChatMessage` → `ConversationEntry` will happen in the `StepExecutor`/`ToolLoop` integration story (NOT this story)

**Why singleton lifetime for ConversationStore:**
- Stateless (no in-memory cache) — all state is on disk
- Matches `InstanceManager` pattern (also singleton, also disk-based)
- Thread safety: `File.AppendAllTextAsync` is safe for concurrent appends within a single step (OS-level atomic for single-line writes). Cross-step writes go to different files.

**ConversationEntry field mapping (for future integration reference):**

| ConversationEntry field | Source in tool loop |
|------------------------|---------------------|
| `Role = "system"` | Initial system prompt message |
| `Role = "assistant"`, `Content` | `response.Messages` text content |
| `Role = "assistant"`, `ToolCallId`, `ToolName`, `ToolArguments` | `FunctionCallContent` from response |
| `Role = "tool"`, `ToolCallId`, `ToolResult` | `FunctionResultContent` after tool execution |

### ConversationStore Internal Test Constructor

Follow the exact pattern from `InstanceManager` (line 50):

```csharp
/// <summary>
/// Constructor for testing — accepts a resolved data root path directly.
/// </summary>
internal ConversationStore(string dataRootPath, ILogger<ConversationStore> logger)
```

The test constructor allows `ConversationStoreTests` to pass a temp directory directly, bypassing the need for `IInstanceManager` in tests. The production constructor uses `IInstanceManager.GetInstanceFolderPath()` to resolve paths.

**Important:** The test constructor receives the data root (parent of all workflow/instance folders), NOT the instance folder directly. The store must combine `dataRootPath + workflowAlias + instanceId` to get the instance folder, OR accept `IInstanceManager` and delegate path resolution. Given that `IInstanceManager.GetInstanceFolderPath` already encapsulates this logic:

- Production constructor: inject `IInstanceManager`, call `GetInstanceFolderPath(workflowAlias, instanceId)` in each method
- Test constructor: accept `dataRootPath`, construct path as `Path.Combine(dataRootPath, workflowAlias, instanceId)` — mirror InstanceManager's internal logic

Actually, simpler approach: the test constructor should just accept a base path and the store methods should build `{basePath}/{workflowAlias}/{instanceId}/conversation-{stepId}.jsonl`. This is what `InstanceManager` does internally via `GetInstanceDirectory`.

### Endpoint Pattern to Follow

From `InstanceEndpoints.cs`:

```csharp
[ApiController]
[Route("umbraco/api/shallai")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class ConversationEndpoints : ControllerBase
{
    // ...

    [HttpGet("instances/{instanceId}/conversation/{stepId}")]
    [ProducesResponseType<IReadOnlyList<ConversationEntry>>(200)]
    [ProducesResponseType<ErrorResponse>(404)]
    public async Task<IActionResult> GetConversation(
        string instanceId,
        string stepId,
        CancellationToken cancellationToken)
    {
        var instance = await _instanceManager.FindInstanceAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{instanceId}' was not found."
            });
        }

        if (!instance.Steps.Any(s => s.Id == stepId))
        {
            return NotFound(new ErrorResponse
            {
                Error = "step_not_found",
                Message = $"Step '{stepId}' was not found in instance '{instanceId}'."
            });
        }

        var entries = await _conversationStore.GetHistoryAsync(
            instance.WorkflowAlias, instanceId, stepId, cancellationToken);
        return Ok(entries);
    }
}
```

### JSONL File Format Example

Each line in `conversation-{stepId}.jsonl`:

```jsonl
{"role":"system","content":"You are a content auditor...","timestamp":"2026-04-01T10:00:00Z"}
{"role":"assistant","content":"I'll start by reading the content files.","timestamp":"2026-04-01T10:00:01Z"}
{"role":"assistant","content":null,"timestamp":"2026-04-01T10:00:01Z","toolCallId":"call_abc123","toolName":"read_file","toolArguments":"{\"path\":\"content.md\"}"}
{"role":"tool","content":null,"timestamp":"2026-04-01T10:00:02Z","toolCallId":"call_abc123","toolResult":"# Page Title\nContent here..."}
{"role":"assistant","content":"The content looks good. Here's my analysis...","timestamp":"2026-04-01T10:00:03Z"}
```

### Project Structure Notes

New files:
```
Shallai.UmbracoAgentRunner/
  Instances/ConversationEntry.cs            (NEW — DTO)
  Instances/IConversationStore.cs           (NEW — interface)
  Instances/ConversationStore.cs            (NEW — implementation)
  Endpoints/ConversationEndpoints.cs        (NEW — API controller)

Shallai.UmbracoAgentRunner.Tests/
  Instances/ConversationStoreTests.cs       (NEW — ~10 tests)
  Endpoints/ConversationEndpointsTests.cs   (NEW — 3 tests)
```

Modified files:
```
Shallai.UmbracoAgentRunner/
  Composers/AgentRunnerComposer.cs          (MODIFY — add one DI registration line)
```

### Retrospective Intelligence

**From Epics 1-5 retrospectives (actionable for this story):**

- **Story specs are the lever** — failure cases section below covers all non-obvious failure modes
- **Simplest fix first** — this is a straightforward persistence layer with a simple endpoint. Don't over-engineer.
- **Existing tests must not break** — all 252 existing backend tests must pass
- **SDK import boundary is a hard rule** — `ConversationEntry` must NOT reference `Microsoft.Extensions.AI` types
- **Test target ~10 per story** — this story targets ~13 new tests (10 store + 3 endpoint)
- **Atomic writes everywhere** — but for append-only JSONL, the standard `File.AppendAllTextAsync` is the correct pattern (not temp-file-then-rename)

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` — Epic 6, Story 6.1]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Decision 4: Data Architecture, JSONL conversation persistence]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Instances/ folder structure, ConversationStore placement]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Conversation endpoint specification]
- [Source: `_bmad-output/planning-artifacts/prd.md` — FR43: Persist conversation history, FR44: Review conversation history]
- [Source: `_bmad-output/planning-artifacts/prd.md` — NFR22: Incremental append-only persistence, NFR13: Backoffice auth]
- [Source: `_bmad-output/implementation-artifacts/5-4-workflow-tool-validation.md` — Latest story format reference]
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md` — No conversation-related deferred items]
- [Source: `_bmad-output/project-context.md` — SDK import boundary rule, atomic writes rule, endpoint auth pattern]

## Failure & Edge Cases

1. **JSONL file does not exist on read**: `GetHistoryAsync` called for a step that hasn't started yet. File doesn't exist. Return empty list — not an error. **Must test.**

2. **Empty/whitespace lines in JSONL**: Trailing newline after last entry, or blank lines from interrupted writes. `GetHistoryAsync` must skip these silently. **Must test.**

3. **Corrupted/partial JSON line**: A crash mid-write leaves a truncated JSON line (e.g., `{"role":"assistant","con`). `GetHistoryAsync` must catch `JsonException` for that line, log a Warning, skip it, and continue reading subsequent valid lines. **Must test.**

4. **Per-step file isolation**: Two steps in the same instance must write to separate files (`conversation-step-one.jsonl` vs `conversation-step-two.jsonl`). Verify no cross-contamination. **Must test.**

5. **Instance not found on endpoint**: `GET .../conversation/{stepId}` with non-existent `instanceId`. Return 404 with `ErrorResponse`. **Must test.**

6. **Step not found in instance**: Instance exists but `stepId` is not in the `Steps` list. Return 404. This prevents arbitrary file reads via path manipulation (stepId goes into file path). **Must test.**

7. **StepId path traversal**: A malicious `stepId` like `../../etc/passwd` in the endpoint URL could escape the instance folder. Defence: validate that `stepId` exists in the instance's `Steps` list (which only contains legitimate step IDs from the workflow definition) BEFORE constructing any file path. The step-not-found check (edge case #6) is the security gate. **Handled by AC #2 validation — no additional code needed beyond the stepId-in-steps check.**

8. **Concurrent appends to same file**: Two tool results completing simultaneously could interleave writes. `File.AppendAllTextAsync` on a single short line is effectively atomic on modern OS filesystems. For this story's scope (building the store in isolation), this is acceptable. The future integration story will ensure single-writer semantics via the tool loop's sequential execution model. **No special handling needed.**

9. **Very large conversation history**: A step with hundreds of tool calls produces a large JSONL file. `GetHistoryAsync` reads all lines into memory. Acceptable for v1 — conversation files are bounded by `ToolLoop.MaxIterations` (100 iterations × ~3 entries per iteration = ~300 lines max). **No special handling needed.**

10. **ConversationEntry with all optional fields null**: An entry with only `Role`, `Content`, and `Timestamp` (no tool data). The optional tool fields should be omitted from JSON (use `JsonIgnoreCondition.WhenWritingNull`). **Must test.**

11. **Instance folder doesn't exist yet**: `AppendAsync` called before the instance folder is created (shouldn't happen in practice — `InstanceManager.CreateInstanceAsync` creates it). If it does, `File.AppendAllTextAsync` will throw `DirectoryNotFoundException`. Let it propagate — this indicates a bug in the caller. **No special handling needed.**

12. **InstanceId format in endpoint**: The endpoint receives `instanceId` as a route parameter string. `FindInstanceAsync` already handles non-existent IDs by returning null. No additional format validation needed. **No special handling needed.**

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Build error: `StepStatus.Running` doesn't exist — enum uses `Active`. Fixed in test file.

### Completion Notes List

- Task 1: Created `ConversationEntry` DTO with `[JsonPropertyName]` camelCase attributes and `[JsonIgnore(Condition = WhenWritingNull)]` for optional tool fields
- Task 2: Created `IConversationStore` interface with `AppendAsync` and `GetHistoryAsync` methods
- Task 3: Implemented `ConversationStore` with production constructor (IInstanceManager) and internal test constructor (dataRootPath). Append uses `File.AppendAllTextAsync`, read skips empty/corrupted lines with Warning log
- Task 4: Registered `IConversationStore` as singleton in `AgentRunnerComposer` after `IInstanceManager`
- Task 5: Created `ConversationEndpoints` controller following `InstanceEndpoints` pattern — validates instance exists, validates stepId in steps list (security gate against path traversal), returns conversation history
- Task 6: 10 ConversationStore unit tests covering: append, multi-append ordering, history retrieval, non-existent file, empty lines, corrupted lines, per-step isolation, camelCase serialisation, null field omission, tool call round-trip
- Task 7: 3 ConversationEndpoints unit tests covering: valid request 200, instance not found 404, step not found 404
- Task 8: All 265 tests pass (252 existing + 13 new), zero regressions

### Change Log

- 2026-04-01: Implemented Tasks 1-9. All 265 automated tests pass (13 new). DI verified via TestSite startup. Endpoint HTTP validation deferred to Story 6.2 (Bellissima bearer token auth prevents direct API testing — same constraint as all custom endpoints).

### File List

- Shallai.UmbracoAgentRunner/Instances/ConversationEntry.cs (NEW)
- Shallai.UmbracoAgentRunner/Instances/IConversationStore.cs (NEW)
- Shallai.UmbracoAgentRunner/Instances/ConversationStore.cs (NEW)
- Shallai.UmbracoAgentRunner/Endpoints/ConversationEndpoints.cs (NEW)
- Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs (MODIFIED — added ConversationStore DI registration)
- Shallai.UmbracoAgentRunner.Tests/Instances/ConversationStoreTests.cs (NEW)
- Shallai.UmbracoAgentRunner.Tests/Endpoints/ConversationEndpointsTests.cs (NEW)

### Review Findings

- [x] [Review][Patch] `InstanceManager` private auto-property should be `private readonly IInstanceManager? _instanceManager` field [ConversationStore.cs:40] — fixed
- [x] [Review][Defer] stepId has no format validation for filesystem safety in WorkflowValidator — deferred, pre-existing gap not introduced by this story
