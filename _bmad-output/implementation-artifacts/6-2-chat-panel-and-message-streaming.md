# Story 6.2: Chat Panel & Message Streaming

Status: done

## Story

As a developer or editor,
I want to watch agent output stream in real-time in a chat panel during step execution,
So that I can see what the agent is doing as it works.

## Acceptance Criteria

1. **Given** a step is executing and SSE events are streaming
   **When** the `shallai-chat-panel` component is active in the instance detail main area
   **Then** it displays `text.delta` events as streaming text in a `shallai-chat-message` component with role "agent"
   **And** the streaming message shows a blinking cursor at the end while text is actively arriving (UX-DR7)
   **And** when the LLM response completes (next non-text-delta event or step finish), the cursor disappears and the message becomes static

2. **Given** SSE lifecycle events
   **When** `step.started` events are received
   **Then** a system message is displayed: "Starting {stepName}..."
   **When** `step.finished` events are received
   **Then** a system message is displayed: "{stepName} completed"

3. **Given** the chat panel is displaying messages
   **When** new content arrives
   **Then** the chat panel uses `uui-scroll-container` with auto-scroll to follow new content
   **And** when the user scrolls up (more than 50px from bottom), auto-scroll pauses
   **And** a "New messages" floating indicator appears at the bottom when auto-scroll is paused and new messages arrive
   **And** clicking the indicator or scrolling to bottom resumes auto-scroll (UX-DR6)

4. **Given** message rendering
   **Then** agent messages use `--uui-color-surface` background, left-aligned
   **And** system messages use no background with `--uui-color-text-alt` text and `--uui-font-size-s`, centred (UX-DR7)
   **And** the chat message list has `role="log"` and `aria-live="polite"` for screen reader announcements (UX-DR21)
   **And** a streaming indicator `aria-live` region announces "Agent is responding..." during streaming and "Response complete" when done (UX-DR24)

5. **Given** a completed step's conversation is being reviewed
   **When** the user selects a completed step in the step progress sidebar
   **Then** the chat panel loads messages from `GET /umbraco/api/shallai/instances/{id}/conversation/{stepId}` and displays them in chronological order in read-only mode (no blinking cursor, no streaming state)

6. **Given** a step executes through the ToolLoop
   **When** the LLM produces text responses and tool calls
   **Then** the ToolLoop persists conversation entries via `IConversationRecorder` so that completed step conversations are available for review via the conversation history endpoint (Story 6.1)
   **And** recording failures are logged but do NOT terminate step execution

## What NOT to Build

- **No `shallai-tool-call` component** — inline tool call display is Story 6.3. Tool SSE events (`tool.start`, `tool.args`, `tool.end`, `tool.result`) should be consumed by the event handler (to correctly track streaming state boundaries) but NOT rendered visually. Tool calls are invisible to the user in 6.2.
- **No message input / send button** — user messaging is Story 6.4. The input area is NOT rendered. The chat panel is view-only in this story.
- **No markdown rendering or sanitisation** — agent message text is rendered as plain text. Markdown → HTML is Story 6.4.
- **No `shallai-tool-call` component** — tool call display is Story 6.3
- **No FR47 rollback** — rolling back failed messages from conversation context is Story 7.2
- **No AbortController / SSE reconnection** — deferred (NFR5 scope), per deferred-work.md item from Story 4.5
- **No user role messages** — only `agent` and `system` roles rendered. `user` role rendering is Story 6.4.
- **No changes to ExecutionEndpoints SSE response format** — the existing SSE event types and payloads are unchanged
- **No changes to WorkflowOrchestrator** — orchestrator already emits step.started/step.finished/run.* events. No changes needed.

## Tasks / Subtasks

- [x]Task 1: Switch ToolLoop to streaming LLM responses (AC: #1, #6)
  - [x]In `Engine/ToolLoop.cs`, replace `client.GetResponseAsync()` with `client.GetStreamingResponseAsync()` (returns `IAsyncEnumerable<ChatResponseUpdate>`)
  - [x]During streaming enumeration, emit `text.delta` SSE events for each text chunk as it arrives (currently emits full text at once — this enables true character-by-character streaming UX)
  - [x]After streaming completes, assemble the complete response using the M.E.AI streaming assembly API (check `ChatResponseUpdateExtensions` or equivalent for the `ToChatResponse()` / accumulation pattern in the SDK version used)
  - [x]Extract function calls from the assembled response and process them exactly as before (tool dispatch loop unchanged)
  - [x]The `messages` list must still be updated with the complete assembled response for multi-turn context
  - [x]Return type remains the same — the final assembled response
  - [x]If `emitter` is null, streaming still works — just no SSE events emitted (test path)
  - [x]**CRITICAL:** Verify that `FunctionCallContent` items are correctly extracted from the assembled streaming response. The `ToolDeclaration` pattern (Story 5.3) prevents middleware double-execution — streaming should not change this.

- [x]Task 2: Create `IConversationRecorder` interface in `Engine/` (AC: #6)
  - [x]Create `Engine/IConversationRecorder.cs`
  - [x]Namespace: `Shallai.UmbracoAgentRunner.Engine`
  - [x]Methods:
    - `Task RecordAssistantTextAsync(string content, CancellationToken cancellationToken)` — for complete LLM text responses (called once per streaming response, with accumulated text)
    - `Task RecordToolCallAsync(string toolCallId, string toolName, string arguments, CancellationToken cancellationToken)` — for function call requests
    - `Task RecordToolResultAsync(string toolCallId, string toolResult, CancellationToken cancellationToken)` — for tool execution results
    - `Task RecordSystemMessageAsync(string message, CancellationToken cancellationToken)` — for system messages (step lifecycle)
  - [x]This interface lives in Engine/ (pure .NET, no Umbraco dependencies) — follows the `ISseEventEmitter` pattern

- [x]Task 3: Create `ConversationRecorder` implementation in `Services/` (AC: #6)
  - [x]Create `Services/` folder if it doesn't exist (project-context.md specifies "Services folder for cross-cutting concerns that bridge Umbraco and Engine")
  - [x]Create `Services/ConversationRecorder.cs`
  - [x]Namespace: `Shallai.UmbracoAgentRunner.Services`
  - [x]Constructor: accept `IConversationStore`, `string workflowAlias`, `string instanceId`, `string stepId`, `ILogger<ConversationRecorder>`
  - [x]NOT registered in DI — created per-execution in `ExecutionEndpoints` or `StepExecutor` (scoped to a specific step execution)
  - [x]Each `Record*Async` method creates a `ConversationEntry` with appropriate role/content/tool fields and calls `IConversationStore.AppendAsync()`
  - [x]Mapping:
    - `RecordAssistantTextAsync` → `ConversationEntry { Role = "assistant", Content = text, Timestamp = DateTime.UtcNow }`
    - `RecordToolCallAsync` → `ConversationEntry { Role = "assistant", Content = null, ToolCallId, ToolName, ToolArguments, Timestamp = DateTime.UtcNow }`
    - `RecordToolResultAsync` → `ConversationEntry { Role = "tool", Content = null, ToolCallId, ToolResult, Timestamp = DateTime.UtcNow }`
    - `RecordSystemMessageAsync` → `ConversationEntry { Role = "system", Content = message, Timestamp = DateTime.UtcNow }`
  - [x]**All methods must catch exceptions, log at Warning level, and NOT rethrow** — recording failure must never kill step execution (AC #6)

- [x]Task 4: Wire recorder into ToolLoop and execution path (AC: #6)
  - [x]Add `IConversationRecorder? recorder = null` as optional parameter to `ToolLoop.RunAsync()` (follows `ISseEventEmitter? emitter = null` pattern)
  - [x]In ToolLoop: after streaming response completes and text is accumulated, call `recorder?.RecordAssistantTextAsync(accumulatedText, ct)`
  - [x]In ToolLoop: before tool execution (alongside `emitter?.EmitToolStartAsync`), call `recorder?.RecordToolCallAsync(toolCallId, toolName, arguments, ct)`
  - [x]In ToolLoop: after tool execution (alongside `emitter?.EmitToolResultAsync`), call `recorder?.RecordToolResultAsync(toolCallId, result, ct)`
  - [x]Add `IConversationRecorder?` to `StepExecutionContext` record
  - [x]In `StepExecutor.ExecuteStepAsync`: pass `context.ConversationRecorder` to `ToolLoop.RunAsync`
  - [x]In `WorkflowOrchestrator.ExecuteNextStepAsync`: create `ConversationRecorder` instance using resolved step details and pass via `StepExecutionContext`
  - [x]`WorkflowOrchestrator` needs `IConversationStore` injected (add constructor parameter)
  - [x]Also call `recorder?.RecordSystemMessageAsync` for step.started and step.finished events in `WorkflowOrchestrator`

- [x]Task 5: Create `shallai-chat-message` component (AC: #1, #2, #4)
  - [x]Create `Client/src/components/shallai-chat-message.element.ts`
  - [x]Class: `ShallaiChatMessageElement extends UmbLitElement`
  - [x]Custom element tag: `shallai-chat-message`
  - [x]Properties:
    - `role: 'agent' | 'system'` — determines styling
    - `content: string` — message text (plain text, no markdown rendering)
    - `timestamp: string` — ISO timestamp, displayed as local time (HH:MM format)
    - `isStreaming: boolean` — shows blinking cursor at end of content
  - [x]Styling (use UUI design tokens only):
    - Agent message: `--uui-color-surface` background, `--uui-size-space-4` padding, `border-radius: var(--uui-border-radius)`, left-aligned, proportional font
    - System message: no background, `color: var(--uui-color-text-alt)`, `font-size: var(--uui-font-size-s)`, centred, no timestamp shown
  - [x]Blinking cursor: CSS `@keyframes blink` animation on a `▋` character appended to content when `isStreaming` is true
  - [x]Accessibility: `role="listitem"`, agent messages get `aria-label="Agent message"`, system messages get `aria-label="System notification"`
  - [x]Import lit from `@umbraco-cms/backoffice/external/lit` (NEVER bare `lit`)

- [x]Task 6: Create `shallai-chat-panel` component with auto-scroll (AC: #1, #2, #3, #4, #5)
  - [x]Create `Client/src/components/shallai-chat-panel.element.ts`
  - [x]Class: `ShallaiChatPanelElement extends UmbLitElement`
  - [x]Custom element tag: `shallai-chat-panel`
  - [x]Properties:
    - `messages: ChatMessage[]` — array of messages to render (passed from parent)
    - `isStreaming: boolean` — whether streaming is active (controls cursor on last message)
  - [x]Render `uui-scroll-container` containing a message list (`role="log"`, `aria-live="polite"`)
  - [x]Each message renders a `shallai-chat-message` component
  - [x]The last agent message gets `isStreaming=true` when `this.isStreaming` is true
  - [x]**Auto-scroll logic:**
    1. After each `updated()` lifecycle, check if scroll position is within 50px of bottom
    2. If yes → auto-scroll to bottom (user is following)
    3. If no → don't scroll, set `_autoScrollPaused = true`
    4. Show a "New messages ↓" floating button at bottom when `_autoScrollPaused && isStreaming`
    5. Clicking the button → scroll to bottom, set `_autoScrollPaused = false`
    6. If user scrolls manually to bottom (within 50px) → set `_autoScrollPaused = false`
  - [x]**Streaming accessibility region:** Hidden `aria-live="assertive"` region that shows "Agent is responding..." when streaming starts and "Response complete" when streaming ends
  - [x]**Empty state:** When `messages` is empty, show centred placeholder text: "Start the workflow to begin" in `--uui-color-text-alt`

- [x]Task 7: Integrate chat panel into instance-detail and wire SSE events (AC: #1, #2, #3, #5)
  - [x]In `shallai-instance-detail.element.ts`:
    - Add `_chatMessages: ChatMessage[]` state array
    - Add `_streamingText: string` state for accumulating text.delta chunks
    - Replace the placeholder main content area with `<shallai-chat-panel .messages=${this._chatMessages} .isStreaming=${this._streaming}>`
  - [x]Update `_handleSseEvent` to process chat-relevant events:
    - `text.delta`: append `data.content` to `_streamingText`, update the last message in `_chatMessages` (or create a new agent message if none is streaming)
    - `step.started`: finalise any streaming message (clear `_streamingText`), push system message "Starting {stepName}..."
    - `step.finished`: finalise any streaming message, push system message "{stepName} completed"
    - `tool.start`: finalise the current streaming text into a complete agent message (tool events mark the end of text streaming for that response). Do NOT render tool call visually (Story 6.3).
    - `run.finished`: finalise any streaming message, set `_streaming = false`
    - `run.error`: finalise any streaming message, push system message with error, set `_streaming = false`
  - [x]"Finalise streaming message" means: if `_streamingText` is non-empty, update the last agent message's content to the accumulated text, mark `isStreaming = false`, reset `_streamingText = ''`
  - [x]When a completed step is selected in the sidebar, call `getConversation()` and populate `_chatMessages` from the response (read-only mode — `_streaming = false`)
  - [x]When the active step is selected, show the live streaming messages

- [x]Task 8: Add conversation history API client function (AC: #5)
  - [x]In `Client/src/api/api-client.ts`, add:
    ```typescript
    async function getConversation(instanceId: string, stepId: string, token?: string): Promise<ConversationEntryResponse[]>
    ```
  - [x]Calls `GET /umbraco/api/shallai/instances/${instanceId}/conversation/${stepId}`
  - [x]In `Client/src/api/types.ts`, add:
    ```typescript
    interface ConversationEntryResponse {
      role: string;
      content: string | null;
      timestamp: string;
      toolCallId?: string;
      toolName?: string;
      toolArguments?: string;
      toolResult?: string;
    }

    interface ChatMessage {
      role: 'agent' | 'system';
      content: string;
      timestamp: string;
      isStreaming?: boolean;
    }
    ```
  - [x]When loading history, map `ConversationEntryResponse` to `ChatMessage` — filter to only `role === "assistant"` (→ agent) and `role === "system"` entries. Skip `role === "tool"` entries (Story 6.3 scope).

- [x]Task 9: Backend tests (AC: #6)
  - [x]Create `Shallai.UmbracoAgentRunner.Tests/Services/ConversationRecorderTests.cs`
  - [x]Test: `RecordAssistantTextAsync` creates entry with role "assistant" and correct content
  - [x]Test: `RecordToolCallAsync` creates entry with role "assistant", toolCallId, toolName, toolArguments
  - [x]Test: `RecordToolResultAsync` creates entry with role "tool", toolCallId, toolResult
  - [x]Test: `RecordSystemMessageAsync` creates entry with role "system"
  - [x]Test: Recording failure (IConversationStore throws) is caught and logged, does NOT propagate
  - [x]Update existing ToolLoop tests to account for streaming API change (method signature change)
  - [x]Test: ToolLoop with recorder calls RecordAssistantTextAsync after LLM text response
  - [x]Test: ToolLoop with recorder calls RecordToolCallAsync and RecordToolResultAsync around tool execution
  - [x]Test: ToolLoop with null recorder does not throw (existing null-emitter pattern)
  - [x]Target: ~10 new backend tests

- [x]Task 10: Frontend tests (AC: #1, #3)
  - [x]Create `Client/test/chat-message.test.ts`
  - [x]Test: ChatMessage type mapping from ConversationEntryResponse filters correctly (assistant → agent, system → system, tool → skipped)
  - [x]Test: Auto-scroll threshold calculation (within 50px = follow, beyond = pause)
  - [x]Test: Streaming text accumulation from multiple text.delta events
  - [x]Test: Finalise streaming message on tool.start event
  - [x]Test: API client getConversation returns typed response
  - [x]Note: Frontend tests verify logic only, not DOM rendering (per deferred-work.md constraint — Umbraco backoffice exports not directly importable by web-test-runner)
  - [x]Target: ~5 frontend tests

- [x]Task 11: Run all tests and manual E2E validation
  - [x]`dotnet test Shallai.UmbracoAgentRunner.slnx` — all existing + new backend tests pass
  - [x]`npm test` from `Client/` — all existing + new frontend tests pass
  - [x]`npm run build` from `Client/` — frontend builds without errors, `wwwroot/` output updated
  - [x]Start TestSite with `dotnet run` — application starts without DI errors
  - [x]Manual E2E: Start a workflow instance → verify chat panel appears → verify streaming text appears character-by-character → verify system messages for step start/finish → verify auto-scroll behaviour → verify blinking cursor during streaming → verify cursor disappears when response completes
  - [x]Manual E2E: After step completes, click the completed step in sidebar → verify conversation history loads from endpoint in read-only mode
  - [x]Manual E2E: Verify keyboard/screen reader accessibility — tab to chat panel, verify aria-live announcements

### Review Findings

- [x] [Review][Patch] P1: Single `_chatMessages` array lost live messages when viewing history — added `_historyMessages` array, ternary now switches correctly [shallai-instance-detail.element.ts]
- [x] [Review][Patch] P2: Dead code — redundant `functionCalls` extraction scanning all assistant messages before overwriting from last only [ToolLoop.cs:83-87]
- [x] [Review][Patch] P3: Direct mutation of ChatMessage objects broke Lit reactivity — switched to immutable object creation with spread [shallai-instance-detail.element.ts]
- [x] [Review][Patch] P4: aria-live "Response complete" announced on every non-streaming render — added `_wasStreaming` transition tracking [shallai-chat-panel.element.ts]
- [x] [Review][Patch] P5: step.finished system message always said "completed" even for error status — now checks `data.status` [shallai-instance-detail.element.ts]
- [x] [Review][Patch] P6: Tool errors in ToolLoop catch blocks not recorded to conversation — added `recorder?.RecordToolResultAsync` in both catch blocks [ToolLoop.cs:160-190]
- [x] [Review][Patch] P7: `_finaliseStreamingMessage` didn't check `isStreaming` flag — addressed in P3 fix, now checks `last.isStreaming` [shallai-instance-detail.element.ts]
- [x] [Review][Defer] D1: No AbortController for SSE abort in disconnectedCallback — deferred, NFR5 scope per story spec
- [x] [Review][Defer] D2: Engine→Services architecture boundary — WorkflowOrchestrator directly instantiates ConversationRecorder — deferred, designed into story spec Task 4
- [x] [Review][Defer] D3: step.finished SSE payload lacks stepName — pre-existing payload design, frontend falls back to stepId
- [x] [Review][Defer] D4: CancellationToken inconsistency — error path uses CancellationToken.None (correct), success path uses live token — low impact, ConversationRecorder swallows exceptions

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

| Component | File | Status |
|-----------|------|--------|
| `ToolLoop` | `Engine/ToolLoop.cs` | **MODIFY** — switch to streaming API, add recorder parameter |
| `StepExecutionContext` | `Engine/StepExecutionContext.cs` | **MODIFY** — add `IConversationRecorder?` property |
| `StepExecutor` | `Engine/StepExecutor.cs` | **MODIFY** — pass recorder to ToolLoop |
| `WorkflowOrchestrator` | `Engine/WorkflowOrchestrator.cs` | **MODIFY** — create recorder, inject IConversationStore, record system messages |
| `ISseEventEmitter` | `Engine/Events/ISseEventEmitter.cs` | DO NOT MODIFY |
| `SseEventEmitter` | `Engine/Events/SseEventEmitter.cs` | DO NOT MODIFY |
| `ExecutionEndpoints` | `Endpoints/ExecutionEndpoints.cs` | DO NOT MODIFY — SSE response already configured correctly |
| `IConversationStore` | `Instances/IConversationStore.cs` | DO NOT MODIFY — consume as-is |
| `ConversationStore` | `Instances/ConversationStore.cs` | DO NOT MODIFY |
| `ConversationEntry` | `Instances/ConversationEntry.cs` | DO NOT MODIFY |
| `ConversationEndpoints` | `Endpoints/ConversationEndpoints.cs` | DO NOT MODIFY |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | **MODIFY** — add IConversationStore to WorkflowOrchestrator injection if needed |
| `shallai-instance-detail` | `Client/src/components/shallai-instance-detail.element.ts` | **MODIFY** — integrate chat panel, wire SSE events |
| `api-client` | `Client/src/api/api-client.ts` | **MODIFY** — add getConversation function |
| `types` | `Client/src/api/types.ts` | **MODIFY** — add ConversationEntryResponse and ChatMessage types |

### Architecture Compliance

- **`IConversationRecorder` lives in `Engine/`** — it's a pure .NET interface with no Umbraco dependencies, following the `ISseEventEmitter` pattern. The Engine defines the contract; the implementation lives outside Engine/.
- **`ConversationRecorder` lives in `Services/`** — this is a cross-cutting concern bridging Engine (defines the interface) and Instances (provides ConversationStore). The project-context.md specifies "Services folder for cross-cutting concerns that bridge Umbraco and Engine."
- **`ConversationRecorder` is NOT a DI singleton** — it's created per step execution because it's scoped to a specific `workflowAlias/instanceId/stepId`. `WorkflowOrchestrator` creates it before passing to `StepExecutor`.
- **Recording failures are swallowed** — a disk I/O failure in ConversationStore should never kill a step execution. The conversation is a nice-to-have audit trail; the step completing successfully is the primary goal.
- **Engine/ folder changes are pure .NET** — `IConversationRecorder` and the ToolLoop streaming change introduce no Umbraco dependencies.

### Key Design Decisions

**Why switch ToolLoop to streaming:**
- The current `GetResponseAsync` returns the complete LLM response at once. The frontend receives one large `text.delta` event — there's nothing to "stream" visually.
- `GetStreamingResponseAsync` returns chunks as the LLM generates them. Each chunk is emitted as a `text.delta` SSE event, enabling the character-by-character streaming UX described in UX-DR7.
- Without streaming, the blinking cursor and auto-scroll behaviour are meaningless — the entire response appears instantly.
- NFR1 ("messages must appear within 500ms") is about perceived latency — streaming satisfies this by showing the first token immediately.

**Why `IConversationRecorder` rather than calling `IConversationStore` directly in ToolLoop:**
- `IConversationStore` is in `Instances/` namespace — Engine/ cannot reference it (architecture boundary).
- `IConversationRecorder` is a thin, Engine-owned interface that abstracts the persistence mechanism.
- The recorder also encapsulates the mapping from raw text/tool data to `ConversationEntry` objects, keeping ToolLoop clean.

**Why ConversationRecorder swallows exceptions:**
- Step execution is the critical path. A disk write failure for conversation history should not abort a step that's otherwise succeeding.
- Warning-level logging ensures the failure is visible without being alarmist.
- The conversation is reconstructible from SSE events if needed — it's supplementary data, not primary state.

**Why tool SSE events are consumed but not rendered:**
- `tool.start` is the signal that text streaming has ended for the current response (the LLM stops generating text and starts making tool calls). The frontend needs this event to finalise the streaming message.
- Tool call rendering (visual blocks with name, args, result) is Story 6.3 scope.
- Consuming without rendering prevents data loss — when 6.3 adds rendering, the event handling is already in place.

### Streaming Response Pattern (Microsoft.Extensions.AI)

The `IChatClient` interface provides `GetStreamingResponseAsync`:

```csharp
// Returns IAsyncEnumerable<ChatResponseUpdate>
await foreach (var update in client.GetStreamingResponseAsync(messages, options, ct))
{
    // update.Text contains the text chunk (may be null for non-text updates)
    if (!string.IsNullOrEmpty(update.Text))
    {
        await emitter?.EmitTextDeltaAsync(update.Text, ct);
        textBuilder.Append(update.Text);
    }
}
// After enumeration, assemble complete response for function call extraction
// Check SDK for assembly API — may be ToChatResponse() extension or manual ChatMessage construction
```

**IMPORTANT:** Verify the exact API surface of the installed `Microsoft.Extensions.AI` version. The streaming assembly pattern may differ. Check the `ChatResponseUpdate` type for properties like `Contents` (which may contain `FunctionCallContent` items) and `FinishReason`. If no built-in assembly exists, manually accumulate text and function calls from the update stream.

### Frontend SSE Event Flow (Instance Detail → Chat Panel)

```
SSE Stream → instance-detail._handleSseEvent()
  │
  ├─ text.delta → append to _streamingText, update last agent message
  │                (creates new agent ChatMessage if none streaming)
  │
  ├─ tool.start → finalise streaming text into complete message
  │                (do NOT render tool call — 6.3 scope)
  │
  ├─ tool.args/tool.end/tool.result → ignore for now (6.3 scope)
  │
  ├─ step.started → finalise streaming text, push system message
  │
  ├─ step.finished → finalise streaming text, push system message
  │
  ├─ run.finished → finalise streaming text, set streaming=false
  │
  └─ run.error → finalise streaming text, push error system message
```

### Auto-Scroll Implementation Notes

Use `uui-scroll-container` as the scrollable wrapper. After Lit's `updated()` lifecycle callback:

```typescript
private _checkAutoScroll(): void {
  const container = this.renderRoot.querySelector('uui-scroll-container');
  if (!container) return;
  const { scrollTop, scrollHeight, clientHeight } = container;
  const isNearBottom = scrollHeight - scrollTop - clientHeight < 50;
  if (isNearBottom) {
    container.scrollTop = scrollHeight;
    this._autoScrollPaused = false;
  }
}
```

Listen for `scroll` events on the container to detect manual scroll-up (set `_autoScrollPaused = true` when user scrolls away from bottom).

### Frontend Component File Structure

New files:
```
Client/src/components/
  shallai-chat-panel.element.ts      (NEW — container with scroll + message list)
  shallai-chat-message.element.ts    (NEW — individual message rendering)
```

The components must be imported in `Client/src/index.ts` (or wherever the entry point aggregates component imports) so they're included in the Vite build.

### Test Pattern References

- Backend: follow `ConversationStoreTests.cs` for temp directory pattern, `ToolLoopTests.cs` for ToolLoop test setup
- Frontend: follow existing `*.test.ts` files in `Client/test/` — logic tests only, not DOM rendering (per deferred-work.md)

### Retrospective Intelligence

**From Epics 1-5 retrospectives (actionable for this story):**

- **Streaming is the product's "wow moment"** (UX spec) — this is the most user-visible story yet. Get the streaming UX right.
- **Simplest fix first** — the ToolLoop streaming change is the riskiest task. Get it working with a minimal change before adding recording. Test streaming in E2E before proceeding to the chat panel.
- **Existing tests must not break** — 265 existing backend tests must pass. The ToolLoop method signature change will break existing ToolLoop tests — update them.
- **SDK import boundary is a hard rule** — `IConversationRecorder` in Engine/ must not reference types from outside Engine/. `ConversationEntry` is used only in `ConversationRecorder` (Services/), never in Engine/.
- **Error catch blocks don't emit tool.result SSE event on error** (deferred from 5.1 code review) — known inconsistency. Don't try to fix in this story. The frontend should handle missing tool.result events gracefully.
- **Browser testing shortcut** — when stuck on frontend issues, ask Adam to verify in the browser rather than guessing.
- **Terminal-log feedback loop** — Adam's browser-to-terminal debugging workflow is the fastest way to iterate on streaming issues.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` — Epic 6, Story 6.2]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Decision 2: Streaming Protocol (SSE), Decision 5: SSE Endpoints, Decision 6: Frontend State Management]
- [Source: `_bmad-output/planning-artifacts/prd.md` — FR38: Real-time chat panel, FR39: Message streaming, NFR1: 500ms latency, NFR5: Reconnection, NFR15: Chat accessibility]
- [Source: `_bmad-output/planning-artifacts/ux-design-specification.md` — shallai-chat-panel component spec, shallai-chat-message component spec, auto-scroll pattern, streaming UX, design tokens]
- [Source: `_bmad-output/implementation-artifacts/6-1-conversation-persistence.md` — ConversationStore and ConversationEntry built]
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md` — SSE reconnection deferred, error emit inconsistency]
- [Source: `_bmad-output/project-context.md` — Engine boundary rule, Services folder convention, SDK import boundary, lit import rule]

## Failure & Edge Cases

1. **SSE connection drops mid-stream**: The `ReadableStream` reader throws when the connection is lost. The existing `_onStartClick` in instance-detail already has a try/catch around the reader loop. On connection drop, the chat panel should finalise any streaming message and show a system message: "Connection lost." Set `_streaming = false`. Do NOT attempt reconnection (deferred, NFR5 scope). **Must handle in frontend.**

2. **LLM returns empty text response**: The LLM responds with function calls only (no text content). `text.delta` events are never emitted for that response. The recorder should NOT create an empty assistant text entry. Guard: `if (!string.IsNullOrEmpty(accumulatedText))` before calling `RecordAssistantTextAsync`. **Must handle in ToolLoop.**

3. **Very rapid text.delta events**: LLM streaming can produce many small chunks in quick succession. The frontend must batch Lit re-renders efficiently. Lit's reactive property system handles this — each property set triggers a microtask-coalesced render. Do NOT use `requestAnimationFrame` or debouncing for message updates — let Lit's scheduler handle it. **No special handling needed — verify in E2E.**

4. **Auto-scroll paused but streaming continues**: User scrolls up to read earlier messages while new text.delta events arrive. The "New messages" indicator must appear. When streaming finishes, the indicator remains until the user scrolls down or clicks it. **Must implement per AC #3.**

5. **Recording failure during step execution**: `IConversationStore.AppendAsync` throws (disk full, permission denied, etc.). `ConversationRecorder` catches the exception and logs at Warning level. Step execution continues normally. The conversation history for that step will be incomplete, but the step completes successfully. **Must implement — catch in ConversationRecorder, never in ToolLoop.**

6. **Conversation history endpoint returns empty array**: A step completed but no conversation was recorded (e.g., recording failed throughout). The chat panel shows the empty state placeholder. This is acceptable degradation — the step result/artifacts are still available. **Must handle — empty message array renders empty state.**

7. **Concurrent text.delta and step.finished events**: In theory, the SSE stream is ordered (single HTTP response body). Events arrive sequentially. However, if the stream parser splits on `\n\n` boundaries, it's possible to process multiple events in one batch. Ensure finalise-streaming-message is idempotent — calling it when `_streamingText` is empty should be a no-op. **Must handle — guard against empty finalisation.**

8. **Step selected during active streaming**: User clicks a completed step in the sidebar while the active step is streaming. The chat panel should switch to read-only mode for the selected step (load from conversation endpoint) and stop showing live streaming messages. If the user clicks back to the active step, resume showing the live stream from the accumulated `_chatMessages` array (not re-fetched from endpoint, since the step isn't complete yet). **Must implement — track selected vs active step.**

9. **Streaming response with no FinishReason**: The `IAsyncEnumerable` completes normally but the last `ChatResponseUpdate` has no explicit `FinishReason`. This can happen if the LLM hits a max token limit. Treat enumeration completion as response completion regardless of `FinishReason`. Process any accumulated function calls. **Must handle in ToolLoop — don't gate on FinishReason.**

10. **GetStreamingResponseAsync throws mid-stream**: The LLM provider fails after some chunks have been emitted. The `await foreach` throws. The existing ToolLoop error handling (catching exceptions from `GetResponseAsync`) should be adapted to also catch streaming exceptions. Emit whatever text was accumulated before the error, then let the error propagate to the orchestrator for `run.error` handling. **Must handle in ToolLoop — flush partial text before rethrowing.**

11. **Frontend navigation during streaming**: User navigates away from instance detail (back to instance list or different section) while streaming is active. The SSE reader loop should terminate (the component disconnects, and the ReadableStream reader should be cleaned up). Currently there's no AbortController — this is a known deferred item. For this story: ensure the component's `disconnectedCallback` sets `_streaming = false` and breaks out of any active processing. The server-side step continues independently. **Must handle — basic cleanup in disconnectedCallback.**

12. **Conversation history contains tool entries**: When loading history for a completed step, the endpoint returns all entries including `role === "tool"` and assistant entries with `toolCallId`. Story 6.2 only renders text messages. Filter: only map entries where `role === "assistant" && content != null && toolCallId == null` (pure text responses) and `role === "system"`. Skip tool-related entries. Story 6.3 will add rendering for these. **Must handle — filter in frontend mapping.**

## Dev Agent Record

### Implementation Plan

- Switched ToolLoop from `GetResponseAsync` to `GetStreamingResponseAsync` with `IAsyncEnumerable<ChatResponseUpdate>` enumeration
- Used `messages.AddMessages(updates)` extension method for streaming assembly (M.E.AI 10.2.0)
- Created `IConversationRecorder` interface in Engine/ (pure .NET, no Umbraco deps)
- Created `ConversationRecorder` in Services/ (bridges Engine and Instances)
- All recorder methods catch and log exceptions — never kill step execution
- Built `shallai-chat-message` and `shallai-chat-panel` Lit components with UUI design tokens
- Integrated chat panel into instance-detail with SSE event wiring for text.delta, tool.start, step events, run events
- Added auto-scroll with 50px threshold, "New messages" indicator, and aria-live regions

### Debug Log

- ToolLoop streaming: used `messages.AddMessages(updates)` for streaming assembly instead of manual accumulation
- Existing ToolLoop/StepExecutor/WorkflowOrchestrator tests required updating from `GetResponseAsync` to `GetStreamingResponseAsync` (3 test files)
- WorkflowOrchestrator constructor gained `IConversationStore` and `ILoggerFactory` parameters — updated test setup accordingly

### Code Review Fixes (2026-04-01)

- **P1 — Separate live vs history messages**: Added `_historyMessages: ChatMessage[]` state. Conversation history now loads into `_historyMessages`, not `_chatMessages`. The chat panel ternary switches between arrays based on `_viewingStepId`. Fixes Failure Case 8: clicking back to the active step during streaming now shows the live messages instead of stale history.
- **P2 — Dead code removal**: Removed the first `functionCalls` extraction in ToolLoop that scanned all assistant messages — it was immediately overwritten by the `lastAssistantMessage` extraction on the next line.
- **P3 — Immutable ChatMessage updates**: `_finaliseStreamingMessage` and `text.delta` handler now create new message objects via spread (`{ ...last, content: ... }`) and use `slice` to build new arrays. Ensures Lit child elements detect property changes and re-render correctly.
- **P4 — aria-live transition tracking**: Added `_wasStreaming` state to `shallai-chat-panel`. "Response complete" only announces on the streaming→not-streaming edge, not on every render with messages. Prevents spurious screen reader announcements when loading history.
- **P5 — step.finished error status**: `step.finished` handler now checks `data.status === "Error"` and renders "failed" instead of "completed". Note: `data.stepName` may be undefined (D3 — pre-existing payload gap), frontend falls back to `data.stepId`.
- **P6 — Tool error recording**: Both `ToolExecutionException` and general `Exception` catch blocks in ToolLoop now call `recorder?.RecordToolResultAsync` with the error message. Conversation log no longer has orphaned tool calls without results.
- **P7 — isStreaming guard**: Addressed as part of P3. `_finaliseStreamingMessage` now checks `last.isStreaming` before acting, preventing mutation of already-finalised or history-loaded messages.

### Completion Notes

- All 11 tasks completed, all acceptance criteria satisfied
- 273 backend tests pass (8 new: 5 ConversationRecorderTests + 3 ToolLoop recorder tests)
- 76 frontend tests pass (9 new: mapping, streaming accumulation, finalise, auto-scroll threshold)
- Frontend builds cleanly, wwwroot/ output updated
- All failure/edge cases from story spec addressed:
  - SSE connection drops: handled in _onStartClick catch block
  - Empty LLM text: guarded with `string.IsNullOrEmpty` check
  - Auto-scroll pause: implemented per AC #3
  - Recording failures: caught in ConversationRecorder
  - Empty conversation history: renders empty state
  - Idempotent finalise: guarded against empty `_streamingText`
  - Step selection during streaming: tracks `_viewingStepId`
  - No FinishReason gating: enumeration completion = response completion
  - Mid-stream throws: partial text flushed to recorder before rethrow
  - disconnectedCallback: sets `_streaming = false`
  - Tool entry filtering: `mapConversationToChat` filters by role

## File List

### New Files
- `Shallai.UmbracoAgentRunner/Engine/IConversationRecorder.cs`
- `Shallai.UmbracoAgentRunner/Services/ConversationRecorder.cs`
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-chat-message.element.ts`
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-chat-panel.element.ts`
- `Shallai.UmbracoAgentRunner/Client/src/chat-message.test.ts`
- `Shallai.UmbracoAgentRunner.Tests/Services/ConversationRecorderTests.cs`

### Modified Files
- `Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs` — streaming API, recorder parameter
- `Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs` — added ConversationRecorder property
- `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs` — pass recorder to ToolLoop
- `Shallai.UmbracoAgentRunner/Engine/WorkflowOrchestrator.cs` — create recorder, inject IConversationStore, record system messages
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` — chat panel integration, SSE event wiring
- `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` — getConversation, mapConversationToChat
- `Shallai.UmbracoAgentRunner/Client/src/api/types.ts` — ConversationEntryResponse, ChatMessage types
- `Shallai.UmbracoAgentRunner.Tests/Engine/ToolLoopTests.cs` — streaming API, recorder tests
- `Shallai.UmbracoAgentRunner.Tests/Engine/StepExecutorTests.cs` — streaming API
- `Shallai.UmbracoAgentRunner.Tests/Engine/WorkflowOrchestratorTests.cs` — new constructor params
- `Shallai.UmbracoAgentRunner.Tests/Tools/ToolExtensibilityTests.cs` — streaming API

## Change Log

- Story 6.2: Chat panel and message streaming implementation (Date: 2026-04-01)
  - Switched ToolLoop to streaming LLM responses via GetStreamingResponseAsync
  - Created IConversationRecorder interface and ConversationRecorder implementation for conversation persistence during execution
  - Built shallai-chat-message and shallai-chat-panel web components with auto-scroll and accessibility
  - Integrated chat panel into instance-detail view with full SSE event wiring
  - Added conversation history API client for reviewing completed step conversations
  - 8 new backend tests, 9 new frontend tests
