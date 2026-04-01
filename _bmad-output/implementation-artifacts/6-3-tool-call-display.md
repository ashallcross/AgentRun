# Story 6.3: Tool Call Display

Status: done

## Story

As a developer or editor,
I want to see tool call activity displayed inline in the chat panel,
So that I can understand what actions the agent is taking during execution.

## Acceptance Criteria

1. **Given** the SSE stream emits `tool.start`, `tool.args`, `tool.end`, and `tool.result` events
   **When** a tool call occurs during step execution
   **Then** a `shallai-tool-call` component is rendered inline within the current agent message (UX-DR8)
   **And** the collapsed view shows: a wrench icon (`uui-icon`), tool name, and a one-line summary (e.g. filename or URL)
   **And** clicking the component expands to show arguments and results in monospace font (`--uui-font-size-s`)
   **And** the expand/collapse toggle is keyboard accessible via Enter/Space

2. **Given** a tool call is in progress
   **When** `tool.start` has been received but `tool.result` has not
   **Then** the tool call block shows a `uui-loader` spinner replacing the expand icon and `--uui-color-border` outline

3. **Given** a tool call has completed
   **When** `tool.result` has been received
   **Then** the spinner is replaced with a `uui-icon` check indicator and the block becomes expandable

4. **Given** a tool call has errored
   **When** `tool.result` contains an error
   **Then** the block shows `--uui-color-danger` border and the expanded view shows the error message

5. **And** tool call blocks have `role="group"`, `aria-label="Tool call: {toolName}"`, and `aria-expanded` state (UX-DR23)
   **And** expanding a tool call does not affect chat scroll position

6. **Given** a completed step's conversation is being reviewed (read-only mode)
   **When** the conversation history contains tool call and tool result entries
   **Then** tool calls are rendered inline as completed `shallai-tool-call` components (no spinners, all expandable)

## What NOT to Build

- **No message input / send button** -- user messaging is Story 6.4
- **No markdown rendering or sanitisation** -- agent message text stays plain text until Story 6.4
- **No FR47 rollback** -- rolling back failed messages is Story 7.2
- **No AbortController / SSE reconnection** -- deferred (NFR5 scope)
- **No changes to ToolLoop.cs or any backend SSE emission** -- the backend already emits `tool.start`, `tool.args`, `tool.end`, `tool.result` events in the correct order. This is a **frontend-only** story.
- **No changes to ConversationRecorder or conversation persistence** -- the backend already records tool calls (`role="assistant"` with `toolCallId`/`toolName`/`toolArguments`) and tool results (`role="tool"` with `toolCallId`/`toolResult`). This story only changes how the frontend reads and renders them.
- **No changes to SseEventEmitter, SseEventTypes, or ISseEventEmitter** -- all tool SSE payloads are already defined and emitted
- **No tool result truncation** -- show full result in expanded view (UX spec mentions truncation as optional)

## Tasks / Subtasks

- [x] Task 1: Add `ToolCallData` type and update `ChatMessage` (AC: #1, #6)
  - [x]In `Client/src/api/types.ts`, add:
    ```typescript
    export interface ToolCallData {
      toolCallId: string;
      toolName: string;
      summary: string;
      arguments: Record<string, unknown> | null;
      result: string | null;
      status: "running" | "complete" | "error";
    }
    ```
  - [x]Update `ChatMessage` to add optional `toolCalls` property:
    ```typescript
    export interface ChatMessage {
      role: "agent" | "system";
      content: string;
      timestamp: string;
      isStreaming?: boolean;
      toolCalls?: ToolCallData[];
    }
    ```

- [x] Task 2: Create `shallai-tool-call` component (AC: #1, #2, #3, #4, #5)
  - [x]Create `Client/src/components/shallai-tool-call.element.ts`
  - [x]Class: `ShallaiToolCallElement extends UmbLitElement`
  - [x]Custom element tag: `shallai-tool-call`
  - [x]Properties:
    - `toolName: string` -- e.g. "read_file", "write_file", "fetch_url"
    - `toolCallId: string` -- for traceability
    - `summary: string` -- one-line description (e.g. filename or URL extracted from arguments)
    - `arguments: Record<string, unknown> | null` -- tool input parameters (rendered as JSON in monospace)
    - `result: string | null` -- tool output (rendered in monospace)
    - `status: "running" | "complete" | "error"` -- tool execution state
  - [x]Internal state:
    - `_expanded: boolean = false` -- expand/collapse state
  - [x]Styling (UUI design tokens only):
    - Collapsed: `--uui-color-border` outline, `--uui-size-space-3` padding, `border-radius: var(--uui-border-radius)`
    - Running: `uui-loader` spinner before tool name, `--uui-color-border` outline
    - Complete: `uui-icon name="check"` before tool name (positive colour), clickable to expand
    - Error: `--uui-color-danger` border, error icon
    - Expanded: `--uui-color-surface` background, monospace font (`font-family: monospace; font-size: var(--uui-font-size-s)`), arguments and result shown
  - [x]Layout anatomy (collapsed):
    ```
    [icon/spinner] toolName -- summary  [chevron]
    ```
  - [x]Layout anatomy (expanded):
    ```
    [icon] toolName -- summary          [chevron-up]
    ----------------------------------------
    Arguments:
      { "path": "scan-results.md" }
    Result:
      File contents here...
    ```
  - [x]Toggle: clicking anywhere on the component (except when `status === "running"`) toggles `_expanded`. Keyboard: `Enter`/`Space` on the outer element.
  - [x]Accessibility: `role="group"`, `aria-label="Tool call: {toolName}"`, `aria-expanded` attribute reflects `_expanded` state, toggle target has `tabindex="0"`.
  - [x]Import lit from `@umbraco-cms/backoffice/external/lit` (NEVER bare `lit`)
  - [x]Import UmbLitElement from `@umbraco-cms/backoffice/lit-element`

- [x] Task 3: Render tool calls inside `shallai-chat-message` (AC: #1, #5)
  - [x]In `shallai-chat-message.element.ts`, add a `toolCalls` property:
    ```typescript
    @property({ attribute: false })
    toolCalls: ToolCallData[] = [];
    ```
  - [x]Import `./shallai-tool-call.element.js` and import `ToolCallData` type from `../api/types.js`
  - [x]In the agent message render method, after the content text, render tool calls:
    ```typescript
    ${this.toolCalls.map(tc => html`
      <shallai-tool-call
        .toolName=${tc.toolName}
        .toolCallId=${tc.toolCallId}
        .summary=${tc.summary}
        .arguments=${tc.arguments}
        .result=${tc.result}
        .status=${tc.status}
      ></shallai-tool-call>
    `)}
    ```
  - [x]Tool calls render AFTER agent text content, BEFORE the timestamp
  - [x]Only render for `role === "agent"` -- system messages never have tool calls

- [x] Task 4: Pass `toolCalls` through `shallai-chat-panel` (AC: #1, #5)
  - [x]In `shallai-chat-panel.element.ts`, update the message rendering to pass `toolCalls`:
    ```typescript
    <shallai-chat-message
      .role=${msg.role}
      .content=${msg.content}
      .timestamp=${msg.timestamp}
      .toolCalls=${msg.toolCalls ?? []}
      ?is-streaming=${i === lastIndex && msg.role === "agent" && this.isStreaming}
    ></shallai-chat-message>
    ```
  - [x]No other changes to `shallai-chat-panel` -- scroll behaviour, auto-scroll, and accessibility are unchanged

- [x] Task 5: Handle tool SSE events in `shallai-instance-detail` (AC: #1, #2, #3, #4)
  - [x]In `_handleSseEvent`, update the `tool.start` case. Currently it only calls `_finaliseStreamingMessage()`. Change it to:
    1. Call `_finaliseStreamingMessage()` (existing -- this ends text streaming for the current response)
    2. Extract `toolCallId` and `toolName` from `data`
    3. Generate a `summary` from the tool name (just use the tool name as summary initially -- `tool.args` will refine it)
    4. Create a new `ToolCallData` object with `status: "running"`, `arguments: null`, `result: null`
    5. Attach it to the last agent message's `toolCalls` array. If no agent message exists yet, create one with empty content.
    6. Use immutable updates (spread + slice) to trigger Lit reactivity
  - [x]Add a `tool.args` case:
    1. Find the `ToolCallData` with matching `toolCallId` in the last agent message
    2. Update its `arguments` property with `data.arguments` (the `ToolArgsPayload` data)
    3. Extract a one-line summary from the arguments: for tools with a `path` argument use the filename, for tools with a `url` argument use the URL, otherwise use the first string value or the tool name
    4. Update the `summary` property
    5. Immutable update of the message array
  - [x]Add a `tool.end` case -- no-op for now. The `tool.end` event marks execution completion but carries no useful payload beyond `toolCallId`. Status transitions happen on `tool.result`.
  - [x]Add a `tool.result` case:
    1. Find the `ToolCallData` with matching `toolCallId` in the last agent message
    2. Set `result` from `data.result` (serialised to string via `JSON.stringify` if object, or use directly if string)
    3. Determine `status`: if the result string starts with `"Tool '"` and contains `"error"` or `"failed"`, set `status: "error"`, otherwise `status: "complete"`
    4. Immutable update
  - [x]**IMPORTANT**: After a set of tool calls completes and the LLM produces a new text response, a new `text.delta` event arrives. The existing `text.delta` handler creates a new agent message (because the previous one is no longer `isStreaming`). This naturally separates pre-tool-call text from post-tool-call text. No changes needed to the `text.delta` handler.

- [x] Task 6: Update `mapConversationToChat` for tool call history (AC: #6)
  - [x]In `Client/src/api/api-client.ts`, update `mapConversationToChat` to include tool call data:
    - Currently it skips entries with `toolCallId`. Change it to:
    1. When encountering an `assistant` entry with `toolCallId` (a tool call), create a `ToolCallData` with `status: "complete"` and attach it to the preceding agent message's `toolCalls` array
    2. When encountering a `tool` entry (a tool result), find the corresponding `ToolCallData` by `toolCallId` and set its `result`
    3. Determine error status: if `toolResult` starts with `"Tool '"` and contains `"error"` or `"failed"`, set `status: "error"`
    4. Generate `summary` from `toolArguments` (same logic as live: extract path/url/first-value)
  - [x]The mapping must handle interleaved entries:
    ```
    assistant (text: "I will read the file")     -> ChatMessage { role: "agent", content: "I will read..." }
    assistant (toolCallId: "c1", toolName: "read_file", toolArguments: '{"path":"file.md"}')  -> attach ToolCallData to previous ChatMessage
    tool (toolCallId: "c1", toolResult: "contents...")  -> update ToolCallData.result
    assistant (text: "Done!")                     -> new ChatMessage { role: "agent", content: "Done!" }
    ```
  - [x]If a tool call entry appears with no preceding text agent message, create one with `content: ""` and attach the tool call to it

- [x] Task 7: Extract summary helper function (AC: #1, #6)
  - [x]Create a small helper function `extractToolSummary(toolName: string, args: Record<string, unknown> | null): string` in `types.ts` or a new `utils/tool-helpers.ts` file
  - [x]Logic:
    - If args has a `path` key: return the filename portion (last segment after `/`)
    - If args has a `url` key: return the URL (truncated to 60 chars if longer)
    - Otherwise: return the first string value from args, or the toolName as fallback
  - [x]Used by both Task 5 (live SSE) and Task 6 (history mapping) to keep summary generation consistent

- [x] Task 8: Frontend tests (AC: #1, #3, #6)
  - [x]Update `Client/src/chat-message.test.ts` with new test cases:
  - [x]Test: `mapConversationToChat` correctly attaches tool calls to preceding agent messages
  - [x]Test: `mapConversationToChat` creates empty-content agent message when tool call has no preceding text
  - [x]Test: `mapConversationToChat` sets error status for tool results containing error strings
  - [x]Test: `extractToolSummary` extracts filename from path argument
  - [x]Test: `extractToolSummary` returns URL for url argument
  - [x]Test: `extractToolSummary` falls back to toolName when no path/url
  - [x]Test: Live SSE tool event sequence creates ToolCallData correctly (simulate tool.start -> tool.args -> tool.result)
  - [x]Note: Frontend tests verify logic only, not DOM rendering (per deferred-work.md constraint)
  - [x]Target: ~7 new frontend tests

- [x] Task 9: Run all tests and manual E2E validation
  - [x]`dotnet test Shallai.UmbracoAgentRunner.slnx` -- all existing backend tests pass (no backend changes, but verify nothing is broken)
  - [x]`npm test` from `Client/` -- all existing + new frontend tests pass
  - [x]`npm run build` from `Client/` -- frontend builds without errors, `wwwroot/` output updated
  - [x]Start TestSite with `dotnet run` -- application starts without errors
  - [x]Manual E2E: Start a workflow instance with a step that uses tools (e.g. `read_file`, `write_file`) -> verify tool call blocks appear inline in agent messages -> verify running state shows spinner -> verify completed state shows check icon -> verify clicking expands to show arguments and result -> verify clicking again collapses -> verify keyboard toggle with Enter/Space
  - [x]Manual E2E: Trigger a tool error (e.g. read a non-existent file) -> verify error state shows danger border and error message
  - [x]Manual E2E: After step completes, click the completed step in sidebar -> verify conversation history loads with tool calls rendered as completed blocks
  - [x]Manual E2E: Verify expanding a tool call does NOT scroll the chat panel
  - [x]Manual E2E: Verify multiple tool calls in a single agent response render correctly in sequence

## Dev Notes

### Current Codebase State (Critical -- Read Before Implementing)

This is a **frontend-only** story. No C# changes are required.

| Component | File | Action |
|-----------|------|--------|
| `types.ts` | `Client/src/api/types.ts` | **MODIFY** -- add `ToolCallData`, update `ChatMessage` |
| `api-client.ts` | `Client/src/api/api-client.ts` | **MODIFY** -- update `mapConversationToChat` to include tool calls |
| `shallai-tool-call` | `Client/src/components/shallai-tool-call.element.ts` | **CREATE** -- new component |
| `shallai-chat-message` | `Client/src/components/shallai-chat-message.element.ts` | **MODIFY** -- add `toolCalls` property, render tool call components |
| `shallai-chat-panel` | `Client/src/components/shallai-chat-panel.element.ts` | **MODIFY** -- pass `toolCalls` to chat messages |
| `shallai-instance-detail` | `Client/src/components/shallai-instance-detail.element.ts` | **MODIFY** -- handle tool.start/tool.args/tool.end/tool.result SSE events |
| `chat-message.test.ts` | `Client/src/chat-message.test.ts` | **MODIFY** -- add tool call tests |
| Backend (ALL files) | `Engine/`, `Services/`, `Endpoints/` | **DO NOT MODIFY** |

### Backend SSE Tool Event Flow (Already Implemented)

The backend emits tool events in this order per tool call (see `Engine/ToolLoop.cs`):

1. `tool.start` -> `{ toolCallId: "tc_001", toolName: "read_file" }` (ToolStartPayload)
2. `tool.args` -> `{ toolCallId: "tc_001", arguments: { path: "file.md" } }` (ToolArgsPayload)
3. Tool executes...
4. `tool.end` -> `{ toolCallId: "tc_001" }` (ToolEndPayload)
5. `tool.result` -> `{ toolCallId: "tc_001", result: "file contents..." }` (ToolResultPayload)

On error, `tool.end` is still emitted, but `tool.result` is NOT emitted for errors. Instead, the error result is added to `FunctionResultContent` for the LLM context only. **UPDATE**: Per Story 6.2 review fix P6, `tool.result` IS emitted on error paths now via the recorder, but the emitter does NOT emit `tool.result` on error -- only `tool.end`. Check `ToolLoop.cs` lines 153-187: both `ToolExecutionException` and generic `Exception` catch blocks emit `tool.end` but NOT `tool.result`.

**CORRECTION**: Looking at the actual code more carefully:
- Success path: emits `tool.end` + `tool.result` (lines 137-141)
- `ToolExecutionException` catch: emits `tool.end` only (lines 159-161) -- NO `tool.result` SSE event
- Generic `Exception` catch: emits `tool.end` only (lines 177-179) -- NO `tool.result` SSE event

**Frontend implication**: For error cases, the frontend receives `tool.start` -> `tool.args` -> `tool.end` but NO `tool.result`. The tool call block will be stuck in "running" status. Solution: treat `tool.end` as a secondary completion signal. If `tool.end` arrives and no `tool.result` follows before the next `text.delta` or `tool.start` or `step.finished`, mark the tool as errored. **Simpler approach**: on `tool.end`, if the tool is still "running", schedule a status check -- but this adds complexity.

**Simplest approach**: On `tool.end`, transition `status` from `"running"` to `"complete"`. Then if `tool.result` arrives, update the result. If no `tool.result` arrives (error case), the block stays as "complete" with no expandable result. This is acceptable -- the error result goes to the LLM, and the conversation recorder captures it for history view. The history mapping (Task 6) WILL show the error because `ConversationRecorder` records tool results for both success and error paths.

### Conversation History Data (Already Persisted)

The `ConversationRecorder` (Story 6.2) already records:
- Tool calls as: `{ role: "assistant", toolCallId: "c1", toolName: "read_file", toolArguments: '{"path":"file.md"}' }`
- Tool results as: `{ role: "tool", toolCallId: "c1", toolResult: "contents..." }` (including error results)

The `ConversationEndpoints` returns these via `GET /instances/{id}/conversation/{stepId}`. The current `mapConversationToChat` skips them -- this story changes the mapping to include them.

### Immutable State Updates (Critical for Lit Reactivity)

Story 6.2 review fix P3 established that `ChatMessage` objects and the `_chatMessages` array must be updated immutably for Lit to detect changes. The same applies to `ToolCallData` objects within messages.

When updating a tool call within a message:
```typescript
// Find the message containing the tool call
const msgIndex = this._chatMessages.findLastIndex(
  m => m.role === "agent" && m.toolCalls?.some(tc => tc.toolCallId === toolCallId)
);
if (msgIndex === -1) return;
const msg = this._chatMessages[msgIndex];
const updatedToolCalls = msg.toolCalls!.map(tc =>
  tc.toolCallId === toolCallId ? { ...tc, result, status: "complete" as const } : tc
);
this._chatMessages = [
  ...this._chatMessages.slice(0, msgIndex),
  { ...msg, toolCalls: updatedToolCalls },
  ...this._chatMessages.slice(msgIndex + 1),
];
```

### Expand/Collapse and Scroll Position (AC: #5)

When a tool call expands, it increases the height of the message. If the chat panel auto-scrolls, the expanded content will push content up. To prevent this:
- The expand/collapse is handled entirely within the `shallai-tool-call` component (local `_expanded` state)
- The chat panel's auto-scroll only triggers on new messages (in `updated()` lifecycle), not on child component size changes
- Since expanding doesn't add new messages or change `this.messages`, the `updated()` callback won't fire for the panel, so auto-scroll won't activate
- This should work naturally -- verify in E2E

### Architecture Compliance

- **No backend changes** -- this story only adds/modifies frontend components
- **Engine/ folder untouched** -- zero Umbraco dependencies rule preserved
- **Frontend conventions followed**:
  - Components use `shallai-` prefix with `Shallai{Name}Element` class naming
  - Import lit from `@umbraco-cms/backoffice/external/lit` (NEVER bare `lit`)
  - Import UmbLitElement from `@umbraco-cms/backoffice/lit-element`
  - Use UUI design tokens for all styling
  - Local imports use relative paths with `.js` extension
  - Strict mode: no unused locals/parameters

### Previous Story Intelligence (Story 6.2)

Key patterns to follow from 6.2:
- **Immutable array/object updates** for Lit reactivity (P3 fix)
- **`_wasStreaming` edge tracking** for aria-live (P4 fix) -- don't introduce similar state-announcement bugs
- **`_finaliseStreamingMessage` guard** checks `last.isStreaming` before acting (P7 fix)
- **`_viewingStepId` / `_historyMessages`** pattern for switching between live and history views (P1 fix)
- All SSE event handling is in `_handleSseEvent` switch statement in instance-detail
- Frontend tests are logic-only (not DOM rendering) per deferred-work.md constraint

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` -- Epic 6, Story 6.3]
- [Source: `_bmad-output/planning-artifacts/architecture.md` -- Decision 2: Streaming Protocol, SSE Event Types]
- [Source: `_bmad-output/planning-artifacts/ux-design-specification.md` -- shallai-tool-call component spec, shallai-chat-message anatomy with inline tool calls]
- [Source: `_bmad-output/implementation-artifacts/6-2-chat-panel-and-message-streaming.md` -- Review fixes P1-P7, event handling patterns]
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md` -- Frontend test constraint, SSE reconnection deferred]
- [Source: `_bmad-output/project-context.md` -- Lit import rules, UUI design tokens, frontend naming conventions]

## Failure & Edge Cases

1. **Multiple tool calls in a single LLM response**: The LLM can return multiple `FunctionCallContent` items in one response. ToolLoop processes them sequentially, emitting `tool.start`/`tool.args`/`tool.end`/`tool.result` for each. All tool calls attach to the same agent message (the one whose text was finalised by the first `tool.start`). The frontend must support an array of `ToolCallData` on a single message. **Must handle -- `toolCalls` is an array.**

2. **Tool call with no preceding text**: The LLM may respond with function calls only (no text content before the tool call). `tool.start` arrives and `_finaliseStreamingMessage()` is a no-op (no streaming text). The tool call must still be attached to an agent message. If the last message is not an agent message, create a new agent message with `content: ""` and attach the tool call. **Must handle in Task 5.**

3. **No `tool.result` SSE event on error** (backend known issue): Error catch blocks in ToolLoop emit `tool.end` but NOT `tool.result`. The frontend will see `tool.start` -> `tool.args` -> `tool.end` with no `tool.result`. Handle by treating `tool.end` as a completion signal: set `status: "complete"` on `tool.end`. If `tool.result` subsequently arrives (success path), update the result. If no `tool.result` arrives (error path), the block shows as "complete" but with no expandable content. This is acceptable because: (a) the error result IS recorded by ConversationRecorder, so history view WILL show it, and (b) a subsequent `text.delta` from the LLM's next response will explain what happened. **Must handle in Task 5 -- use `tool.end` for status transition.**

4. **Rapid tool call events**: Multiple tool calls execute quickly. Lit's microtask-coalesced rendering handles rapid property changes. Each state update should be immutable (new array/object) to ensure Lit detects changes. **No special handling needed beyond immutable updates.**

5. **Expand during streaming**: User expands a completed tool call while new text.delta events arrive for the next LLM response. Expanding is local `_expanded` state in `shallai-tool-call` -- it doesn't affect the parent's message array, so it won't be reset by parent re-renders. Verify that Lit preserves component state when the parent re-renders the same `shallai-tool-call` element (keyed by `toolCallId`). **Must verify in E2E -- Lit should preserve local state for same-element re-renders.**

6. **Tool call in history with missing result**: `ConversationRecorder` catches exceptions -- if recording a tool result fails, history may have a tool call entry with no matching tool result entry. The `mapConversationToChat` mapping should handle this gracefully: create `ToolCallData` with `status: "complete"` and `result: null`. The component shows as completed but with "No result recorded" or just no expandable content. **Must handle in Task 6.**

7. **Long tool results**: Tool results (e.g. file contents) can be very long. The expanded view should show the full result in a scrollable monospace area. Use `overflow-y: auto; max-height: 300px` on the result container to prevent the expanded tool call from consuming the entire viewport. **Must handle in Task 2 styling.**

8. **Arguments JSON parsing**: `tool.args` SSE event sends `arguments` as an object (`ToolArgsPayload`). The `ConversationEntry.ToolArguments` is a JSON string. The frontend needs to handle both: SSE events provide parsed objects, history entries provide JSON strings. In `mapConversationToChat`, parse `toolArguments` via `JSON.parse()` with a try/catch fallback. **Must handle in Task 6.**

9. **Step selection during live tool execution**: User clicks a completed step while a tool is running in the active step. The history view loads (replacing `_chatMessages` display with `_historyMessages`). When user clicks back to the active step, `_viewingStepId` resets to null and `_chatMessages` is displayed again -- the in-progress tool call should still be visible. Since `_chatMessages` is never cleared during step selection, this works. **Verify in E2E.**

10. **`tool.start` arrives before `text.delta` for a new response**: After tool results are sent back to the LLM, the LLM's next response may start with a tool call immediately (no text). The sequence would be: previous `tool.result` -> new `tool.start` (no `text.delta` in between). The frontend should create a new agent message for this new tool call (since the previous agent message already has completed tool calls). **Must handle in Task 5 -- check if last agent message has any "running" tool calls; if not, create a new agent message.**

11. **Component key stability**: When Lit re-renders the message list, it needs to match existing `shallai-tool-call` elements to preserve their `_expanded` state. Use Lit's `repeat` directive with `toolCallId` as the key, or rely on index-based matching (since tool calls within a message don't reorder). Index-based is simpler and sufficient. **No special handling needed -- verify in E2E.**

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Existing test "skips assistant entries with toolCallId" updated to reflect new behaviour where tool call entries create agent messages with attached ToolCallData instead of being skipped
- `tool.end` SSE event used as secondary completion signal (sets status to "complete") to handle backend error paths where `tool.result` is not emitted
- Review fix P1: Added `_toolBatchOpen` flag to correctly group sequential tool calls from the same LLM response into a single agent message. ToolLoop processes tool calls sequentially (A completes before B starts), so the previous status-based heuristic incorrectly split them across messages. The flag is set on `tool.start`, cleared on `text.delta` (new LLM turn) and `step.started` (step boundary)

### Completion Notes List

- Task 1: Added `ToolCallData` interface and `toolCalls?: ToolCallData[]` to `ChatMessage` in types.ts
- Task 7: Created `extractToolSummary()` helper in `utils/tool-helpers.ts` — extracts filename from path, URL from url arg, or falls back to toolName
- Task 2: Created `shallai-tool-call.element.ts` component with collapsed/expanded states, running/complete/error visual states, UUI design tokens, keyboard accessibility (Enter/Space), ARIA attributes (role="group", aria-label, aria-expanded)
- Task 3: Added `toolCalls` property to `shallai-chat-message` and rendered tool call components inline after content, before timestamp
- Task 4: Passed `toolCalls` through `shallai-chat-panel` to chat message components
- Task 5: Handled `tool.start`, `tool.args`, `tool.end`, `tool.result` SSE events in `shallai-instance-detail` with immutable state updates. Handles edge cases: no preceding agent message, multiple tool calls in same response, new tool batch after completed tools
- Task 6: Updated `mapConversationToChat` to attach tool calls to preceding agent messages (or create empty-content agent message), set tool results, detect error status. Added `findLastAgentMessage` helper
- Task 8: Added 7 new tests: 3 for mapConversationToChat with tool calls, 3 for extractToolSummary, 1 for live SSE event sequence. Updated 2 existing tests for new tool call behaviour. Total: 84 frontend tests pass
- Task 9: All 273 backend tests pass, 84 frontend tests pass, TypeScript + Vite build clean

### Change Log

- 2026-04-01: Implemented Story 6.3 — Tool Call Display (all 9 tasks complete)

### File List

- Client/src/api/types.ts (MODIFIED — added ToolCallData interface, updated ChatMessage)
- Client/src/api/api-client.ts (MODIFIED — updated mapConversationToChat with tool call handling, added findLastAgentMessage helper)
- Client/src/utils/tool-helpers.ts (NEW — extractToolSummary helper function)
- Client/src/components/shallai-tool-call.element.ts (NEW — tool call display component)
- Client/src/components/shallai-chat-message.element.ts (MODIFIED — added toolCalls property, renders shallai-tool-call components)
- Client/src/components/shallai-chat-panel.element.ts (MODIFIED — passes toolCalls to chat message components)
- Client/src/components/shallai-instance-detail.element.ts (MODIFIED — handles tool.start/tool.args/tool.end/tool.result SSE events)
- Client/src/chat-message.test.ts (MODIFIED — added 7 new tests, updated 2 existing tests)

### Review Findings

- [x] [Review][Patch] Sequential tool calls in same LLM response create separate agent messages — fixed via `_toolBatchOpen` flag. Set on `tool.start`, cleared on `text.delta` and `step.started`. [shallai-instance-detail.element.ts]
- [x] [Review][Defer] Fragile error detection via string matching — error status detection uses `resultStr.startsWith("Tool '") && (resultStr.includes("error") || resultStr.includes("failed"))`. Matches spec exactly, but any change to backend error format silently breaks detection. Proper fix requires a structured `isError` field in the SSE payload (backend change, out of scope for this frontend-only story). — deferred, needs backend change
