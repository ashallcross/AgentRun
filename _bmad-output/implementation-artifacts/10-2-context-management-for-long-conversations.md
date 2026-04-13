# Story 10.2: Context Management for Long Conversations

Status: done

**Depends on:** 9.7 (tool result offloading pattern — done), 9.12 (content tools — done), 9.6 (configurable tool limits — done)
**Branch:** `feature/epic-10` (all Epic 10 work lives here; `main` stays clean for beta v1 hotfixes; tag `v1.0.0-beta.1` marks the stable point)

> **Priority 1 in Epic 10.** The Umbraco content tools make this urgent. A content audit on a 100+ node site exhausts the context window and triggers the same stall pattern Story 9.7 solved for `fetch_url`. On a 26-node test site the scanner already accumulates all `get_content` results in conversation context — scale that to a real site and it's a guaranteed failure. See `deferred-work.md` section "Context bloat risk for Umbraco content tools on larger sites (2026-04-12)" for the full analysis.

## Story

As a developer,
I want the engine to manage conversation context size for long-running steps,
So that workflows on larger content sets don't stall or fail from context window exhaustion.

## Context

**UX Mode: N/A — engine + tool contract change. No UI work.**

**The bug being fixed:**

When the Content Audit workflow runs on a site with many content nodes, the scanner step calls `get_content` for every node. Each call returns up to 256 KB of JSON (the `GetContentMaxResponseBytes` ceiling). On a 100-node site that's potentially 25 MB of tool results accumulating in conversation context. Sonnet 4.6 produces empty assistant turns after ~32k prompt tokens of accumulated content, and Story 9.0's stall detector fires. The workflow fails not because of a bug in the agent prompt, but because the engine has no mechanism to prevent unbounded context growth.

This is the same root cause as Story 9.7's `fetch_url` stall — raw tool results inlined into conversation context. Story 9.7 solved it for `fetch_url` with the offloading pattern. This story extends that pattern to the content tools and adds engine-level conversation compaction as a safety net.

**Two mitigations, layered:**

1. **Tool result offloading for content tools** (primary fix) — `get_content` writes full results to a workspace scratch path and returns a small JSON handle. Same proven pattern as `fetch_url`'s `.fetch-cache/`.
2. **Engine-level conversation compaction** (safety net) — after a tool result has been consumed (the model has seen it and moved on), the engine replaces the full result in conversation history with a compact placeholder. This protects against ANY tool producing large results in future, not just content tools.

**Out of scope:**
- Batched sub-invocation / sub-agent spawning — that's a workflow authoring concern, not an engine fix
- Changes to `list_content` — already returns compact summaries (id, name, type, URL); 256 KB cap is adequate
- Changes to `list_content_types` — already returns schema metadata, not content bodies
- Changes to `fetch_url` — already has offloading via Story 9.7
- Prompt changes to workflow agent files — workflow authors adapt to the new tool contract themselves
- Token counting / budget tracking — deferred to v2 (no M.E.AI support for prompt caching token counts)

### Architect's locked decisions (do not relitigate)

1. **Content tool offloading uses the same scratchpad handle pattern as `fetch_url`.** The pattern is proven. Don't invent a new one.
2. **Scratch path: `{instanceFolder}/.content-cache/{contentId}.json`.** Same sandbox strategy as `.fetch-cache/` — inside instance folder, dot-prefixed, covered by existing PathSandbox, cleaned up by instance deletion.
3. **Hash key is content node ID, not SHA-256.** Unlike URLs (which need hashing for filesystem-safe names), Umbraco content IDs are integers. Use the ID directly: `.content-cache/1234.json`. If the same node is fetched twice in one step, the cached file is overwritten — this is correct behaviour (content may have changed between calls in a long-running step, but the latest version is the one that matters).
4. **Handle shape matches `fetch_url` contract:**
   ```json
   {
     "id": 1234,
     "name": "About Us",
     "contentType": "contentPage",
     "url": "/about-us/",
     "propertyCount": 12,
     "size_bytes": 45231,
     "saved_to": ".content-cache/1234.json",
     "truncated": false
   }
   ```
   Target under 1 KB. The handle includes enough metadata for the agent to decide whether to `read_file` the cached content.
5. **Offloading threshold: always offload.** Don't add a size-based "inline if small" escape hatch. The `fetch_url` pattern always offloads for consistency and the same applies here. The agent reads the file if it needs detail.
6. **Conversation compaction is additive — it does NOT modify the JSONL persistence.** The compaction operates on the in-memory `List<ChatMessage>` only, before sending to the LLM. The full conversation history remains in the JSONL file for debugging and retry. This is critical: Story 7.2's retry path reloads from JSONL, and compacted messages would break retry context.
7. **Compaction trigger: tool result age.** After a configurable number of assistant turns have passed since a tool result was added, replace the tool result content with a placeholder: `[Content offloaded — {size_bytes} bytes from {tool_name}. Original result available in conversation log.]` The turn count threshold is configurable via `IToolLimitResolver` with a sensible default (3 turns).
8. **Compaction preserves the message structure.** The `FunctionResultContent` stays in the message list (required by the API contract — orphaned tool_call without tool_result causes 400 errors). Only the `.Result` string is replaced with the placeholder text.

## Acceptance Criteria (BDD)

### AC1: GetContentTool offloads to .content-cache

**Given** a workflow step that declares `get_content` in its tool list
**When** the agent calls `get_content` with a valid content ID
**Then** the full JSON result is written to `{instanceFolder}/.content-cache/{contentId}.json`
**And** the tool returns a JSON handle (under 1 KB) with id, name, contentType, url, propertyCount, size_bytes, saved_to, and truncated fields
**And** the handle's `saved_to` path is relative to the instance folder (not absolute)
**And** the cached file contains the same JSON that was previously returned inline

### AC2: Cached content is readable via read_file

**Given** a `get_content` call has written a cached file to `.content-cache/1234.json`
**When** the agent calls `read_file` with path `.content-cache/1234.json`
**Then** the file contents are returned successfully
**And** PathSandbox does not reject the dotted directory (verify with test — same as Story 9.7's PathSandbox verification)

### AC3: Repeated get_content for same ID overwrites cache

**Given** the agent has already called `get_content` for content ID 1234
**When** the agent calls `get_content` for content ID 1234 again
**Then** the cached file at `.content-cache/1234.json` is overwritten with the latest result
**And** a new handle is returned with updated size_bytes

### AC4: Content not found returns error, not handle

**Given** a workflow step that declares `get_content`
**When** the agent calls `get_content` with a non-existent content ID
**Then** the tool returns an error string (not a handle), matching the existing error contract
**And** no file is written to `.content-cache/`

### AC5: Truncation flag preserved through offloading

**Given** a content node whose full JSON exceeds the `GetContentMaxResponseBytes` ceiling (256 KB default)
**When** the agent calls `get_content` for that node
**Then** the cached file contains the truncated result (with properties removed from the end, preserving JSON validity)
**And** the handle's `truncated` field is `true`
**And** the truncation marker text is present in the cached file

### AC6: Conversation compaction replaces old tool results

**Given** a step execution where the agent has called tools and received results
**When** `N` assistant turns have passed since a tool result was added (where N = the compaction threshold, default 3)
**Then** before the next LLM call, the tool result's content in the in-memory message list is replaced with a compact placeholder
**And** the placeholder includes the original tool name and size
**And** the JSONL conversation log is NOT modified (full results preserved for retry/debugging)

### AC7: Compaction preserves API message contract

**Given** a conversation with compacted tool results
**When** the compacted messages are sent to the LLM provider
**Then** every `FunctionCallContent` (tool_call) still has a matching `FunctionResultContent` (tool_result) in the message list
**And** the LLM provider does not return a 400 error due to orphaned tool calls

### AC8: Compaction threshold is configurable

**Given** a workflow YAML that specifies `tool_defaults.compaction_turns: 5` (or per-step override)
**When** the step executes
**Then** tool results are compacted after 5 assistant turns instead of the default 3
**And** the `IToolLimitResolver` resolution chain (step → workflow → site → engine default) is honoured

### AC9: Compaction does not affect user messages or system prompt

**Given** a conversation with user messages, system prompt, and tool results
**When** compaction runs
**Then** only `FunctionResultContent` entries are candidates for compaction
**And** user messages, assistant text messages, and the system prompt are never modified

### AC10: Handle size is bounded

**Given** any content node (including nodes with very long names or many properties)
**When** `get_content` returns a handle
**Then** the handle JSON is under 1 KB
**And** a unit test asserts this bound

## Files in Scope

| File | Change |
|---|---|
| `AgentRun.Umbraco/Tools/GetContentTool.cs` | Add offloading: write full result JSON to `.content-cache/{contentId}.json`; return JSON handle instead of inline result. Preserve existing truncation logic, error contract, and `GetContentMaxResponseBytes` ceiling. |
| `AgentRun.Umbraco/Engine/ToolLoop.cs` | Add conversation compaction: before each `client.GetResponseAsync` call, scan the message list for `FunctionResultContent` entries older than the compaction threshold and replace their content with placeholders. |
| `AgentRun.Umbraco/Engine/EngineDefaults.cs` | Add `CompactionTurnThreshold` default constant (3). |
| `AgentRun.Umbraco/Engine/IToolLimitResolver.cs` | Add `ResolveCompactionTurnThreshold` method to the interface and implementation. |
| `AgentRun.Umbraco.Tests/Tools/GetContentToolTests.cs` | Extend with offloading tests: handle shape, file written, size bound, truncation flag, error case, overwrite on repeat call. |
| `AgentRun.Umbraco.Tests/Engine/ToolLoopCompactionTests.cs` (NEW) | Compaction unit tests: threshold behaviour, placeholder content, message contract preservation, user/system message immunity, JSONL untouched. |

**Files explicitly NOT touched:**

- `AgentRun.Umbraco/Tools/ListContentTool.cs` — returns compact summaries already; offloading not needed
- `AgentRun.Umbraco/Tools/ListContentTypesTool.cs` — schema metadata, not content bodies
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` — already has offloading via Story 9.7
- `AgentRun.Umbraco/Instances/ConversationStore.cs` — JSONL persistence stays unchanged; compaction is in-memory only
- `AgentRun.Umbraco/Security/PathSandbox.cs` — verification only (same as 9.7); no change expected since `.content-cache/` follows the same dot-prefix pattern as `.fetch-cache/`
- Workflow agent prompt files — workflow authors update their own prompts to work with the new handle contract

## Tasks

### Task 1: GetContentTool offloading (AC1, AC3, AC4, AC5, AC10)

- [x] Modify `GetContentTool.ExecuteAsync` to write the full result JSON to disk and return a handle.

1. After the existing result-building logic (property extraction, truncation), serialize the result to JSON
2. Compute the output path: `Path.Combine(context.InstanceFolderPath, ".content-cache", $"{contentId}.json")`
3. Ensure the `.content-cache/` directory exists (`Directory.CreateDirectory`)
4. Write the JSON to the file (overwrite if exists)
5. Build and return the handle object with: id, name, contentType, url, propertyCount, size_bytes, saved_to (relative path), truncated
6. Preserve the existing error path — when content is not found, return the error string directly (no handle, no file write)
7. The `saved_to` field must be a relative path from the instance folder root (e.g., `.content-cache/1234.json`), not an absolute path

**Key pattern to follow:** Look at `FetchUrlTool.cs` lines where it writes to `.fetch-cache/` and returns the handle. The content tool follows the same structure but uses content ID instead of URL hash.

**Do NOT change:**
- The property extraction logic (Block List/Grid resolution, IHtmlEncodedString handling, CompositionPropertyTypes)
- The truncation logic (remove properties from end until under ceiling)
- The error string contract for not-found / unknown parameters
- The `GetContentMaxResponseBytes` ceiling resolution via `IToolLimitResolver`

### Task 2: PathSandbox verification (AC2)

- [x] Write a unit test that verifies `read_file` can reach `.content-cache/{id}.json` through PathSandbox. This is a verification gate — if it fails, PathSandbox needs a fix before proceeding.

**Expected result:** PASS — PathSandbox does pure containment + symlink checks and does not reject dotted directories. This was verified for `.fetch-cache/` in Story 9.7.

### Task 3: GetContentTool unit tests (AC1, AC3, AC4, AC5, AC10)

- [x] Extend `GetContentToolTests.cs` with offloading tests:

1. **Handle shape test** — call `get_content` with a valid ID, assert the returned JSON deserializes to a handle with all required fields, assert `saved_to` is a relative path
2. **File written test** — assert the file exists at the expected path after the call, assert the file content is valid JSON matching the original result structure
3. **Handle size bound test** — assert the serialized handle is under 1024 bytes for a node with a realistic name and property count
4. **Truncation flag test** — create a content node with properties exceeding 256 KB, call `get_content`, assert `truncated: true` in the handle and truncation marker in the cached file
5. **Error case test** — call `get_content` with non-existent ID, assert error string returned (not handle), assert no file in `.content-cache/`
6. **Overwrite test** — call `get_content` twice for the same ID, assert file exists and second call's content is present

### Task 4: Conversation compaction in ToolLoop (AC6, AC7, AC9)

- [x] Add a compaction pass to `ToolLoop.RunAsync`, executed before each `client.GetStreamingResponseAsync` call.

1. Add a method `CompactOldToolResults(IList<ChatMessage> messages, int turnsSinceThreshold)` to `ToolLoop`
2. Track assistant turn count — increment each time an assistant message is added to the list
3. Before each LLM call, iterate the message list. For each `FunctionResultContent`:
   - Calculate how many assistant turns have occurred since this result was added
   - If turns >= threshold, replace `.Result` with the placeholder string: `[Content offloaded — {originalSizeBytes} bytes from {toolName}. Original result available in conversation log.]`
   - Mark as compacted (to avoid re-processing on subsequent iterations)
4. Do NOT remove any messages or `FunctionResultContent` entries — only replace the `.Result` string
5. Do NOT compact the most recent tool result batch (the one the model hasn't responded to yet)

**Implementation detail:** Track compaction state with a lightweight wrapper or by checking placeholder prefix. Don't add a new field to `ChatMessage` (it's from M.E.AI and not ours to extend). A `HashSet<string>` of compacted `CallId`s works.

**Critical constraint:** The JSONL conversation log (`IConversationRecorder`) records messages BEFORE compaction. The recorder is called in `ToolLoop.RunAsync` when tool results are first added. Compaction happens later, on subsequent iterations. The JSONL file always has the full results.

### Task 5: Compaction threshold configuration (AC8)

- [x] Add compaction threshold to IToolLimitResolver resolution chain.

1. Add `CompactionTurnThreshold = 3` to `EngineDefaults.cs`
2. Add `ResolveCompactionTurnThreshold(StepDefinition? step, WorkflowDefinition? workflow)` to `IToolLimitResolver` interface
3. Implement in `ToolLimitResolver` with the standard resolution chain: step `tool_overrides` → workflow `tool_defaults` → engine default
4. The YAML key is `compaction_turns` (under `tool_defaults` or `tool_overrides.{tool_name}`)
5. Pass the resolved threshold into `ToolLoop.RunAsync` (add parameter)

### Task 6: Compaction unit tests (AC6, AC7, AC8, AC9)

- [x] Create `ToolLoopCompactionTests.cs`:

1. **Threshold test** — build a message list with tool results, simulate N assistant turns, run compaction, assert results older than threshold are compacted and newer ones are not
2. **Placeholder content test** — assert the placeholder string contains the original size and tool name
3. **Message contract test** — after compaction, assert every `FunctionCallContent` has a matching `FunctionResultContent` in the message list (no orphans)
4. **User/system immunity test** — assert user messages, assistant text messages, and system messages are never modified by compaction
5. **Most recent batch immunity test** — assert tool results from the current turn (not yet responded to by the model) are never compacted
6. **Custom threshold test** — set threshold to 5, verify compaction only triggers after 5 turns

### Task 7: Manual E2E verification

- [x] 7.1. Run the Content Audit workflow against the Clean Starter Kit test site (26 nodes) — 25 published, 2 not found
- [x] 7.2. Verify the scanner step completes without stalling — clean pass, all 25 handles preserved
- [x] 7.3. Check `.content-cache/` directory exists in the instance folder with cached JSON files — 25 files confirmed (instance beac05999e7245728d7b97006afa844a)
- [x] 7.4. Verify the scan-results.md artifact is produced and references real content — full scan summary with 14 doc types, 7 templateless nodes, 5 unused types
- [x] 7.5. Compaction did not fire for handles (under 1 KB threshold) — confirmed by successful scan with full node detail. Bug fix applied: CompactionMinSizeBytes=1024 skips small results.
- [x] 7.6. Run `dotnet test AgentRun.Umbraco.slnx` — all tests green (536/536)

### Review Findings

- [x] [Review][Patch] WorkflowValidator rejects `compaction_turns` as unknown tool name — Fixed: added `AllowedTuningScalars` set to `WorkflowValidator.cs`, scalar keys skip tool-name validation and are validated as positive integers. 4 tests added. 540/540 green.

## Failure & Edge Cases

### F1: Disk write failure during offloading
**When** the `.content-cache/` directory write fails (permissions, disk full)
**Then** `get_content` returns a tool error string describing the failure
**And** the error is logged at Error level with instance/step context
**And** the step continues (the agent sees the error and can decide to skip that node)

### F2: Content node with zero properties
**When** `get_content` is called for a node that has no properties (e.g., a folder node)
**Then** the handle is still returned with `propertyCount: 0` and `size_bytes` reflecting the minimal JSON
**And** the cached file contains a valid JSON object with an empty properties section

### F3: Very large number of tool calls in a single step
**When** the agent calls `get_content` 200+ times in a single step (large site audit)
**Then** compaction keeps the in-memory message list bounded
**And** the JSONL file grows large but this is acceptable (disk, not memory)
**And** there is no O(n^2) performance issue in the compaction scan (use the `HashSet<string>` of compacted CallIds to skip already-compacted entries)

### F4: Retry after compaction
**When** a step fails and the user retries
**Then** `StepExecutor.ConvertHistoryToMessages` reloads the FULL conversation from JSONL (not compacted)
**And** compaction re-applies naturally on subsequent ToolLoop iterations
**And** the retry has full context from the original conversation

### F5: Concurrent content changes during audit
**When** content is published/unpublished while a workflow step is running
**Then** `get_content` returns whatever is currently published (or error if unpublished)
**And** the cached file reflects the state at the time of the call
**And** this is acceptable — workflows are not transactional

## Dev Notes

### Existing patterns to follow

- **FetchUrlTool offloading** (`Tools/FetchUrlTool.cs`): The gold standard for this pattern. Study how it writes to `.fetch-cache/`, builds the handle, and preserves error paths. The content tool offloading is structurally identical but simpler (no SSRF protection, no HTTP streaming, no content-type detection).
- **IToolLimitResolver chain** (`Engine/ToolLimitResolver.cs`): Follow the existing `ResolveMaxResponseBytes` pattern for adding `ResolveCompactionTurnThreshold`. Step overrides → workflow defaults → engine defaults.
- **EngineDefaults** (`Engine/EngineDefaults.cs`): Add the new constant alongside existing defaults.

### What NOT to do

- Do NOT add a "structured mode" or "summary mode" to GetContentTool. The offloading pattern is simpler, proven, and consistent with fetch_url. The agent reads the file if it needs detail.
- Do NOT add pagination to GetContentTool. Pagination adds API complexity for no benefit — the agent fetches one node at a time by ID.
- Do NOT modify ConversationStore or the JSONL format. Compaction is in-memory only.
- Do NOT add token counting. M.E.AI doesn't expose prompt token counts reliably, and approximate counting adds complexity without value. The turn-based threshold is a pragmatic proxy.
- Do NOT touch `ListContentTool` or `ListContentTypesTool`. They return compact data and are not context bloat risks.

### Project Structure Notes

- All changes are in `AgentRun.Umbraco/` (engine + tools) and `AgentRun.Umbraco.Tests/` (tests)
- New test file: `AgentRun.Umbraco.Tests/Engine/ToolLoopCompactionTests.cs`
- No new projects, no new NuGet dependencies
- Run tests with: `dotnet test AgentRun.Umbraco.slnx`

### References

- [Source: _bmad-output/implementation-artifacts/9-7-tool-result-offloading-fetch-url.md] — offloading pattern spec
- [Source: _bmad-output/implementation-artifacts/deferred-work.md#Context-bloat-risk] — problem analysis
- [Source: _bmad-output/implementation-artifacts/epic-9-retro-2026-04-13.md] — "write to disk, read selectively" team agreement
- [Source: _bmad-output/planning-artifacts/epics.md#Story-10.2] — epic-level story definition
- [Source: AgentRun.Umbraco/Tools/FetchUrlTool.cs] — reference implementation for offloading
- [Source: AgentRun.Umbraco/Engine/ToolLoop.cs] — compaction insertion point
- [Source: AgentRun.Umbraco/Engine/EngineDefaults.cs] — default constants
- [Source: AgentRun.Umbraco/Engine/IToolLimitResolver.cs] — configuration resolution chain

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

### Completion Notes List

- Tasks 1–3 (GetContentTool offloading): Modified `GetContentTool.ExecuteAsync` to write full JSON to `.content-cache/{id}.json` via PathSandbox-validated path and return a compact handle (<1 KB). Error path preserved — no file write on not-found. Existing property extraction, truncation, and ceiling resolution untouched. 8 new tests + 12 updated tests in `GetContentToolTests.cs`. PathSandbox verification tests confirm `.content-cache/` access.
- Tasks 4–6 (Conversation compaction): Added `CompactOldToolResults` to `ToolLoop` — runs before each LLM call, replaces `FunctionResultContent.Result` strings older than the configurable turn threshold with compact placeholders. Most recent tool result batch immune. JSONL log unchanged (compaction is in-memory only). `CompactionTurnThreshold = 3` added to `EngineDefaults`, wired through `IToolLimitResolver` chain. 7 tests in new `ToolLoopCompactionTests.cs`.
- Task 7 (partial): 536/536 tests green. Manual E2E (7.1–7.5) pending Adam's browser testing.

### Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-13 | Initial story creation by Bob (SM) — comprehensive context for offloading + compaction |
| 0.2 | 2026-04-13 | Tasks 1–6 implemented: GetContentTool offloading + conversation compaction + all unit tests. 536/536 tests green. Manual E2E pending. |

### File List

| File | Change |
|---|---|
| `AgentRun.Umbraco/Tools/GetContentTool.cs` | Added offloading: writes full result to `.content-cache/{id}.json`, returns JSON handle. Added `GetContentHandle` record. Method now `async`. |
| `AgentRun.Umbraco/Engine/ToolLoop.cs` | Added `CompactOldToolResults` method + `CompactionPlaceholderPrefix` constant + `compactionTurnThreshold` parameter. Tracks assistant turns and compacts old tool results before each LLM call. |
| `AgentRun.Umbraco/Engine/EngineDefaults.cs` | Added `CompactionTurnThreshold = 3` |
| `AgentRun.Umbraco/Engine/IToolLimitResolver.cs` | Added `ResolveCompactionTurnThreshold` method |
| `AgentRun.Umbraco/Engine/ToolLimitResolver.cs` | Implemented `ResolveCompactionTurnThreshold` with standard resolution chain |
| `AgentRun.Umbraco/Engine/StepExecutor.cs` | Resolves compaction threshold and passes to `ToolLoop.RunAsync` |
| `AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs` | Added `CompactionTurns` property |
| `AgentRun.Umbraco/Configuration/AgentRunToolDefaultsOptions.cs` | Added `CompactionTurns` property |
| `AgentRun.Umbraco.Tests/Tools/GetContentToolTests.cs` | Updated existing tests for handle-based return + added 8 offloading tests + 2 PathSandbox verification tests |
| `AgentRun.Umbraco.Tests/Engine/ToolLoopCompactionTests.cs` | NEW — 7 compaction unit tests |
| `AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs` | Added `ResolveCompactionTurnThreshold` to FakeToolLimitResolver |
| `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` | Added `ResolveCompactionTurnThreshold` to FakeToolLimitResolver |
| `AgentRun.Umbraco.Tests/Tools/ListContentToolTests.cs` | Added `ResolveCompactionTurnThreshold` to FakeToolLimitResolver |
| `AgentRun.Umbraco.Tests/Tools/ListContentTypesToolTests.cs` | Added `ResolveCompactionTurnThreshold` to FakeToolLimitResolver |
| `AgentRun.Umbraco/Workflows/WorkflowValidator.cs` | Added `AllowedTuningScalars` set; `ValidateToolTuningBlock` now skips scalar keys and validates them as positive integers (review fix) |
| `AgentRun.Umbraco.Tests/Workflows/WorkflowValidatorTests.cs` | Added 4 tests for `compaction_turns` validation (accepted, zero rejected, negative rejected, step overrides accepted) |
| `AgentRun.Umbraco.Tests/Engine/StepExecutorTests.cs` | Added `ResolveCompactionTurnThreshold` to FakeToolLimitResolver |
