# Story 6.4: User Messaging & Multi-Turn Conversation

Status: review

## Story

As a developer or editor,
I want to send messages to an agent during step execution and have a back-and-forth conversation,
So that I can guide, clarify, or provide additional context to the agent while it works.

## Acceptance Criteria

1. **Given** a step is actively executing
   **When** the user types a message in the chat input (`uui-textarea`) and presses Enter or clicks the Send button
   **Then** the message is sent via POST `/umbraco/api/shallai/instances/{id}/message` (FR41)
   **And** the user's message appears in the chat panel as a `shallai-chat-message` with role "user" and `--uui-color-surface-emphasis` background
   **And** the agent receives the message in context and responds, maintaining multi-turn conversation within the step (FR42)
   **And** the agent's response streams via SSE as with any other agent output
   **And** Shift+Enter inserts a newline instead of sending (UX-DR21)
   **And** the Send button is disabled when the input is empty
   **And** the input has `aria-label="Message to agent"` (UX-DR21)

2. **Given** no step is currently active (step complete, pending, or error)
   **When** the user views the chat panel
   **Then** the input is disabled with an appropriate placeholder: "Step complete" (after completion), "Click 'Start' to begin the workflow." (before start)

3. **Given** agent messages may contain markdown formatting
   **When** agent message content is rendered in the chat panel
   **Then** the content is sanitised before rendering to prevent XSS — no raw HTML, script tags, or event handler attributes (FR57, NFR9)
   **And** sanitisation uses a whitelist approach permitting only standard markdown-generated HTML elements: `p`, `h1`-`h6`, `ul`, `ol`, `li`, `table`, `tr`, `td`, `th`, `a`, `strong`, `em`, `code`, `pre`, `blockquote`, `hr`

4. **Given** a user message is sent during an active step
   **When** the conversation history is viewed later (step clicked in sidebar)
   **Then** user messages appear with role "user" and correct styling, interleaved in chronological order with agent responses

## What NOT to Build

- **No AbortController / SSE reconnection** — deferred (NFR5 scope)
- **No CSRF anti-forgery tokens** — deferred per deferred-work.md (from 2-3 review)
- **No retry/resume of failed workflows** — Story 7.2 scope
- **No artifact viewer / shallai-markdown-renderer component** — this story adds markdown rendering to `shallai-chat-message` only, not the artifact viewer (Story 8.2)
- **No external markdown library** — implement basic markdown-to-HTML parsing manually. No `marked`, `markdown-it`, or `remark`. Keep it simple: the common subset the LLM produces (paragraphs, bold, italic, code blocks, inline code, lists, headings, links, blockquotes, horizontal rules). Tables are stretch — omit if complex.
- **No external sanitisation library** — no `DOMPurify` or `sanitize-html`. Whitelist approach with manual DOM walking is sufficient for the limited markdown subset.
- **No changes to WorkflowOrchestrator step-advancement logic** — the orchestrator already handles step execution flow. The message endpoint injects messages into the running ToolLoop, it does NOT trigger new step execution.
- **No concurrent message queueing** — if the ToolLoop is in the middle of streaming an LLM response, the user message waits until the current LLM turn completes. This is the natural behaviour of injecting into the message list.

## Tasks / Subtasks

- [x] Task 1: Backend — Add `RecordUserMessageAsync` to conversation recorder (AC: #4)
  - [x] In `Engine/IConversationRecorder.cs`, add:
    ```csharp
    Task RecordUserMessageAsync(string content, CancellationToken cancellationToken);
    ```
  - [x] In `Services/ConversationRecorder.cs`, implement `RecordUserMessageAsync`:
    ```csharp
    public async Task RecordUserMessageAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new ConversationEntry
            {
                Role = "user",
                Content = content,
                Timestamp = DateTime.UtcNow
            };
            await _store.AppendAsync(_workflowAlias, _instanceId, _stepId, entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record user message for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                _stepId, _workflowAlias, _instanceId);
        }
    }
    ```

- [x] Task 2: Backend — Add user message injection to ToolLoop (AC: #1)
  - [x]Add a `ConcurrentQueue<string>` field to enable message injection from outside the loop. Modify `ToolLoop.RunAsync` to accept an optional `ConcurrentQueue<string>? userMessages` parameter (last before emitter/recorder).
  - [x]At the start of each loop iteration (after the iteration guard), drain the queue:
    ```csharp
    while (userMessages is not null && userMessages.TryDequeue(out var userMsg))
    {
        messages.Add(new ChatMessage(ChatRole.User, userMsg));
        await (recorder?.RecordUserMessageAsync(userMsg, cancellationToken) ?? Task.CompletedTask);
        if (emitter is not null)
        {
            await emitter.EmitUserMessageAsync(userMsg, cancellationToken);
        }
    }
    ```
  - [x]Also drain the queue after tool results are added (before the next LLM call), so user messages sent during tool execution are included:
    ```csharp
    // After: messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
    while (userMessages is not null && userMessages.TryDequeue(out var userMsg))
    {
        messages.Add(new ChatMessage(ChatRole.User, userMsg));
        await (recorder?.RecordUserMessageAsync(userMsg, cancellationToken) ?? Task.CompletedTask);
        if (emitter is not null)
        {
            await emitter.EmitUserMessageAsync(userMsg, cancellationToken);
        }
    }
    ```
  - [x]Extract the drain logic into a private helper to avoid duplication:
    ```csharp
    private static async Task DrainUserMessagesAsync(
        ConcurrentQueue<string>? userMessages,
        IList<ChatMessage> messages,
        IConversationRecorder? recorder,
        ISseEventEmitter? emitter,
        CancellationToken cancellationToken)
    ```

- [x] Task 3: Backend — Add `EmitUserMessageAsync` to SSE emitter (AC: #1)
  - [x] In `Engine/Events/ISseEventEmitter.cs`, add:
    ```csharp
    Task EmitUserMessageAsync(string content, CancellationToken cancellationToken);
    ```
  - [x] In `Engine/Events/SseEventEmitter.cs`, implement it. Use a new event type `user.message`:
    ```csharp
    public async Task EmitUserMessageAsync(string content, CancellationToken cancellationToken)
    {
        await EmitEventAsync("user.message", new { content }, cancellationToken);
    }
    ```
  - [x] In `Engine/Events/SseEventTypes.cs`, add `public const string UserMessage = "user.message";` if event type constants are centralized there.

- [x] Task 4: Backend — Store the user message queue per active instance (AC: #1)
  - [x]The ToolLoop runs inside `WorkflowOrchestrator.ExecuteNextStepAsync`, called from `ExecutionEndpoints.StartInstance`. The ToolLoop needs a `ConcurrentQueue<string>` that the message endpoint can write to.
  - [x]Create a simple in-memory registry: `Engine/ActiveInstanceRegistry.cs`
    ```csharp
    public interface IActiveInstanceRegistry
    {
        ConcurrentQueue<string>? GetMessageQueue(string instanceId);
        ConcurrentQueue<string> RegisterInstance(string instanceId);
        void UnregisterInstance(string instanceId);
    }
    ```
  - [x]Implementation uses `ConcurrentDictionary<string, ConcurrentQueue<string>>`. Register as singleton in `AgentRunnerComposer`.
  - [x] In `WorkflowOrchestrator.ExecuteNextStepAsync`, register the instance before calling `_stepExecutor.ExecuteStepAsync` and unregister in a `finally` block.
  - [x]Pass the queue through `StepExecutionContext` to `StepExecutor`, then to `ToolLoop.RunAsync`.
  - [x]Update `StepExecutionContext` record to include `ConcurrentQueue<string>? UserMessageQueue`.

- [x] Task 5: Backend — POST `/instances/{id}/message` endpoint (AC: #1)
  - [x]Create `Models/ApiModels/SendMessageRequest.cs`:
    ```csharp
    public class SendMessageRequest
    {
        public required string Message { get; init; }
    }
    ```
  - [x] In `Endpoints/ExecutionEndpoints.cs`, add:
    ```csharp
    [HttpPost("instances/{id}/message")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(400)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public IActionResult SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "empty_message",
                Message = "Message cannot be empty."
            });
        }

        var queue = _activeInstanceRegistry.GetMessageQueue(id);
        if (queue is null)
        {
            return Conflict(new ErrorResponse
            {
                Error = "not_running",
                Message = $"Instance '{id}' is not currently executing a step."
            });
        }

        queue.Enqueue(request.Message);
        return Ok();
    }
    ```
  - [x] Inject `IActiveInstanceRegistry` into `ExecutionEndpoints` constructor.
  - [x]The endpoint is synchronous — it enqueues the message and returns immediately. The ToolLoop picks it up on its next iteration. The user sees their message via the `user.message` SSE event emitted by the ToolLoop when it drains the queue.

- [x] Task 6: Backend unit tests (AC: #1, #4)
  - [x]`Tests/Engine/ToolLoopTests.cs` — Add test: user message in queue is added to conversation messages and recorded
  - [x]`Tests/Engine/ToolLoopTests.cs` — Add test: user message queue drained after tool results
  - [x]`Tests/Engine/ActiveInstanceRegistryTests.cs` — **CREATE** — Test register/unregister/get for message queue registry
  - [x]`Tests/Services/ConversationRecorderTests.cs` — Add test: `RecordUserMessageAsync` creates entry with role "user"
  - [x]`Tests/Endpoints/ExecutionEndpointsTests.cs` — Add test: POST message with no running instance returns 409
  - [x]`Tests/Endpoints/ExecutionEndpointsTests.cs` — Add test: POST message with empty body returns 400
  - [x]`Tests/Endpoints/ExecutionEndpointsTests.cs` — Add test: POST message with running instance enqueues and returns 200

- [x] Task 7: Frontend — Add `sendMessage` to API client (AC: #1)
  - [x] In `Client/src/api/api-client.ts`, add:
    ```typescript
    export async function sendMessage(instanceId: string, message: string, token?: string): Promise<void> {
      const headers: Record<string, string> = { "Content-Type": "application/json" };
      if (token) headers.Authorization = `Bearer ${token}`;
      const response = await fetch(
        `${API_BASE}/instances/${encodeURIComponent(instanceId)}/message`,
        { method: "POST", headers, body: JSON.stringify({ message }) },
      );
      if (!response.ok) {
        throw new Error(`API error: ${response.status} ${response.statusText}`);
      }
    }
    ```

- [x] Task 8: Frontend — Update `ChatMessage` type for user role (AC: #1, #4)
  - [x] In `Client/src/api/types.ts`, update the `ChatMessage` interface:
    ```typescript
    export interface ChatMessage {
      role: "agent" | "system" | "user";
      content: string;
      timestamp: string;
      isStreaming?: boolean;
      toolCalls?: ToolCallData[];
    }
    ```

- [x] Task 9: Frontend — Create `markdown-sanitiser.ts` utility (AC: #3)
  - [x]Create `Client/src/utils/markdown-sanitiser.ts`
  - [x]Export function `sanitiseMarkdown(raw: string): string` that returns sanitised HTML
  - [x]Markdown parsing (basic subset):
    - **Headings**: `# ` through `###### ` at line start → `<h1>` through `<h6>`
    - **Bold**: `**text**` or `__text__` → `<strong>`
    - **Italic**: `*text*` or `_text_` → `<em>`
    - **Inline code**: `` `code` `` → `<code>`
    - **Code blocks**: ` ```lang\n...\n``` ` → `<pre><code>`
    - **Unordered lists**: `- item` or `* item` at line start → `<ul><li>`
    - **Ordered lists**: `1. item` at line start → `<ol><li>`
    - **Links**: `[text](url)` → `<a href="url" target="_blank" rel="noopener">` — **only allow `http:` and `https:` schemes** (reject `javascript:`, `data:`, etc.)
    - **Blockquotes**: `> text` at line start → `<blockquote>`
    - **Horizontal rules**: `---` or `***` or `___` on their own line → `<hr>`
    - **Paragraphs**: Text blocks separated by blank lines → `<p>`
    - **Line breaks**: Preserve line breaks within paragraphs (like current `pre-wrap` behaviour) — use `<br>` for single newlines within a paragraph
  - [x]Sanitisation (whitelist approach):
    - Parse the generated HTML into a DOM fragment via `document.createElement('template').innerHTML`
    - Walk all nodes recursively
    - Remove any element NOT in the whitelist: `p`, `h1`-`h6`, `ul`, `ol`, `li`, `a`, `strong`, `em`, `code`, `pre`, `blockquote`, `hr`, `br`, `table`, `tr`, `td`, `th`, `thead`, `tbody`
    - For allowed elements, strip ALL attributes except: `href`, `target`, `rel` on `<a>` tags
    - For `<a>` tags: validate `href` starts with `http://` or `https://`, force `target="_blank"` and `rel="noopener"`
    - Return the sanitised HTML string from the template
  - [x]**IMPORTANT**: The function takes raw markdown text and returns sanitised HTML string. The component renders it via Lit's `unsafeHTML` directive (from `lit/directives/unsafe-html.js`) — but the HTML is safe because we sanitised it.

- [x] Task 10: Frontend — Update `shallai-chat-message` for user role and markdown rendering (AC: #1, #3, #4)
  - [x] In `shallai-chat-message.element.ts`:
  - [x]Update the `role` property type: `role: "agent" | "system" | "user" = "agent";`
  - [x]Import `unsafeHTML` from `@umbraco-cms/backoffice/external/lit` (check if re-exported) or from `lit/directives/unsafe-html.js` — **check what's available via the Umbraco backoffice external lit package first**. If `unsafeHTML` is not available from the backoffice package, use `innerHTML` setter on a container element instead (avoid adding a new external import).
  - [x]Import `sanitiseMarkdown` from `../utils/markdown-sanitiser.js`
  - [x]Add styles for user messages:
    ```css
    .user-message {
      background: var(--uui-color-surface-emphasis);
      padding: var(--uui-size-space-4);
      border-radius: var(--uui-border-radius);
      text-align: left;
    }
    .user-content {
      white-space: pre-wrap;
      word-break: break-word;
    }
    .user-timestamp {
      display: block;
      margin-top: var(--uui-size-space-2);
      color: var(--uui-color-text-alt);
      font-size: var(--uui-font-size-s);
    }
    ```
  - [x]Add styles for markdown-rendered agent content (replace `.agent-content` `white-space: pre-wrap`):
    ```css
    .agent-content :is(h1, h2, h3, h4, h5, h6) {
      margin: var(--uui-size-space-3) 0 var(--uui-size-space-2) 0;
    }
    .agent-content :is(h1, h2, h3, h4, h5, h6):first-child {
      margin-top: 0;
    }
    .agent-content p { margin: 0 0 var(--uui-size-space-2) 0; }
    .agent-content p:last-child { margin-bottom: 0; }
    .agent-content pre {
      background: var(--uui-color-surface-emphasis);
      padding: var(--uui-size-space-3);
      border-radius: var(--uui-border-radius);
      overflow-x: auto;
      font-family: monospace;
      font-size: var(--uui-font-size-s);
    }
    .agent-content code {
      background: var(--uui-color-surface-emphasis);
      padding: 1px 4px;
      border-radius: 3px;
      font-family: monospace;
      font-size: var(--uui-font-size-s);
    }
    .agent-content pre code { background: none; padding: 0; }
    .agent-content a { color: var(--uui-color-interactive); }
    .agent-content blockquote {
      border-left: 3px solid var(--uui-color-border);
      padding-left: var(--uui-size-space-3);
      margin: var(--uui-size-space-2) 0;
      color: var(--uui-color-text-alt);
    }
    .agent-content ul, .agent-content ol {
      padding-left: var(--uui-size-space-5);
      margin: var(--uui-size-space-2) 0;
    }
    .agent-content hr {
      border: none;
      border-top: 1px solid var(--uui-color-border);
      margin: var(--uui-size-space-3) 0;
    }
    ```
  - [x]Update the `render()` method to handle all three roles:
    - **system**: unchanged
    - **user**: new block with `user-message` class, `aria-label="Your message"`, plain text (no markdown — user input is NOT markdown-rendered), show timestamp
    - **agent**: change from `${this.content}` plain text to sanitised markdown HTML rendered into `.agent-content`. During streaming (`isStreaming === true`), continue using plain text with cursor (markdown parsing mid-stream produces broken output). On stream finalisation (when `isStreaming` becomes false), the content is the complete text and can be markdown-rendered.
  - [x]For agent messages, the rendering logic:
    ```typescript
    // Streaming: plain text + cursor (don't parse incomplete markdown)
    // Complete: sanitised markdown HTML
    const agentContent = this.isStreaming
      ? html`<span style="white-space:pre-wrap">${this.content}<span class="cursor">▋</span></span>`
      : this._renderMarkdown(this.content);
    ```
  - [x]Add `_renderMarkdown(content: string)` private method that calls `sanitiseMarkdown(content)` and renders via `innerHTML` on a container div (or `unsafeHTML` if available).

- [x] Task 11: Frontend — Add chat input to `shallai-chat-panel` (AC: #1, #2)
  - [x] In `shallai-chat-panel.element.ts`:
  - [x]Add new properties:
    ```typescript
    @property({ type: Boolean, attribute: "input-enabled" })
    inputEnabled = false;

    @property({ type: String, attribute: "input-placeholder" })
    inputPlaceholder = "Click 'Start' to begin the workflow.";
    ```
  - [x]Add internal state:
    ```typescript
    @state()
    private _inputValue = "";
    ```
  - [x]Add styles for the input area (pinned to bottom):
    ```css
    .input-area {
      display: flex;
      gap: var(--uui-size-space-2);
      padding: var(--uui-size-space-3);
      border-top: 1px solid var(--uui-color-border);
      align-items: flex-end;
    }
    .input-area uui-textarea {
      flex: 1;
    }
    ```
  - [x]Add the input area to the render method (after the scroll container, before the sr-only div):
    ```typescript
    <div class="input-area">
      <uui-textarea
        aria-label="Message to agent"
        placeholder=${this.inputEnabled ? "Message the agent..." : this.inputPlaceholder}
        ?disabled=${!this.inputEnabled}
        .value=${this._inputValue}
        @input=${this._onInput}
        @keydown=${this._onKeydown}
      ></uui-textarea>
      <uui-button
        label="Send"
        look="primary"
        compact
        ?disabled=${!this.inputEnabled || !this._inputValue.trim()}
        @click=${this._onSend}
      >
        <uui-icon name="icon-message"></uui-icon>
      </uui-button>
    </div>
    ```
  - [x]Implement event handlers:
    ```typescript
    private _onInput(e: Event): void {
      this._inputValue = (e.target as HTMLTextAreaElement).value;
    }

    private _onKeydown(e: KeyboardEvent): void {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        this._onSend();
      }
    }

    private _onSend(): void {
      const msg = this._inputValue.trim();
      if (!msg || !this.inputEnabled) return;
      this._inputValue = "";
      this.dispatchEvent(new CustomEvent("send-message", {
        detail: { message: msg },
        bubbles: true,
        composed: true,
      }));
    }
    ```
  - [x]The panel dispatches `send-message` event — the parent (`shallai-instance-detail`) handles the API call. The panel is a presentation component.

- [x] Task 12: Frontend — Wire up message sending in `shallai-instance-detail` (AC: #1, #2)
  - [x] In `shallai-instance-detail.element.ts`:
  - [x]Import `sendMessage` from `../api/api-client.js`
  - [x]Compute `inputEnabled` and `inputPlaceholder` based on instance/step state:
    ```typescript
    // In render(), compute these before passing to chat-panel:
    const activeStep = inst.steps.find(s => s.status === "Active");
    const inputEnabled = this._streaming && !this._viewingStepId;
    const inputPlaceholder = inst.status === "Completed" || inst.status === "Failed"
      ? "Workflow complete"
      : activeStep ? "Step complete" : "Click 'Start' to begin the workflow.";
    ```
  - [x]Pass to chat panel:
    ```typescript
    <shallai-chat-panel
      .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
      ?is-streaming=${this._streaming && !this._viewingStepId}
      ?input-enabled=${inputEnabled}
      input-placeholder=${inputPlaceholder}
      @send-message=${this._onSendMessage}
    ></shallai-chat-panel>
    ```
  - [x]Implement `_onSendMessage`:
    ```typescript
    private async _onSendMessage(e: CustomEvent<{ message: string }>): Promise<void> {
      if (!this._instance) return;
      const message = e.detail.message;

      // Optimistically add user message to chat
      this._chatMessages = [
        ...this._chatMessages,
        {
          role: "user" as const,
          content: message,
          timestamp: new Date().toISOString(),
        },
      ];

      try {
        const token = await this.#authContext?.getLatestToken();
        await sendMessage(this._instance.id, message, token);
      } catch {
        // Message failed — add system error message
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: "Failed to send message. Try again.",
            timestamp: new Date().toISOString(),
          },
        ];
      }
    }
    ```
  - [x]Handle the `user.message` SSE event in `_handleSseEvent`:
    ```typescript
    case "user.message":
      // Server confirms the user message was injected into the conversation.
      // We already added it optimistically, so ignore the SSE echo to avoid duplication.
      break;
    ```

- [x] Task 13: Frontend — Update `mapConversationToChat` for user messages in history (AC: #4)
  - [x] In `Client/src/api/api-client.ts`, add handling for `role === "user"` entries in `mapConversationToChat`:
    ```typescript
    else if (entry.role === "user" && entry.content != null) {
      messages.push({
        role: "user",
        content: entry.content,
        timestamp: entry.timestamp,
      });
    }
    ```
  - [x]Place this BEFORE the `system` role check in the existing if-else chain.

- [x] Task 14: Frontend — Fix detail-grid viewport height (AC: #1, #2)
  - [x]Per deferred-work.md (from 6-2 review): "Chat panel and instance detail grid need fixed viewport height layout". Now that we're adding the input area, fix the layout:
  - [x] In `shallai-instance-detail.element.ts` styles, update `.detail-grid`:
    ```css
    .detail-grid {
      display: grid;
      grid-template-columns: 240px 1fr;
      gap: var(--uui-size-layout-1);
      height: calc(100vh - 200px);  /* Subtract header + padding */
      min-height: 400px;
    }
    ```
  - [x]This ensures the chat panel fills a constrained area with its own scroll, and the input stays pinned to the bottom of that area.

- [x] Task 15: Frontend tests (AC: #1, #3, #4)
  - [x] In `Client/src/chat-message.test.ts`, add:
  - [x]Test: `mapConversationToChat` creates user role messages from "user" entries
  - [x]Test: `mapConversationToChat` interleaves user and agent messages correctly
  - [x]Create `Client/src/markdown-sanitiser.test.ts`:
  - [x]Test: headings (`# text` through `###### text`) produce correct `<h1>`-`<h6>` tags
  - [x]Test: bold (`**text**`) produces `<strong>`
  - [x]Test: italic (`*text*`) produces `<em>`
  - [x]Test: inline code produces `<code>`
  - [x]Test: fenced code block produces `<pre><code>`
  - [x]Test: unordered list (`- item`) produces `<ul><li>`
  - [x]Test: ordered list (`1. item`) produces `<ol><li>`
  - [x]Test: links produce `<a>` with correct attributes
  - [x]Test: `javascript:` URLs in links are stripped
  - [x]Test: raw `<script>` tags are removed
  - [x]Test: `onclick` and other event handler attributes are removed
  - [x]Test: `<img src="javascript:...">` is removed
  - [x]Test: plain text with no markdown returns content wrapped in `<p>`
  - [x]Test: blockquotes produce `<blockquote>`
  - [x]Test: horizontal rules produce `<hr>`
  - [x]Target: ~15 new frontend tests, ~7 new backend tests

- [x] Task 16: Run all tests and manual E2E validation
  - [x]`dotnet test Shallai.UmbracoAgentRunner.slnx` — all existing + new backend tests pass
  - [x]`npm test` from `Client/` — all existing + new frontend tests pass
  - [x]`npm run build` from `Client/` — frontend builds without errors
  - [x]Start TestSite with `dotnet run` — application starts without errors
  - [x]Manual E2E: Start a workflow instance → verify input is disabled with "Click 'Start'..." placeholder
  - [x]Manual E2E: Click Start → verify input becomes enabled with "Message the agent..." placeholder
  - [x]Manual E2E: Type a message and press Enter → verify user message appears with emphasis background → verify agent responds
  - [x]Manual E2E: Type a message with Shift+Enter → verify newline inserted, message NOT sent
  - [x]Manual E2E: Verify Send button disabled when input is empty
  - [x]Manual E2E: After step completes → verify input disabled with "Step complete" placeholder
  - [x]Manual E2E: Click a completed step in sidebar → verify user messages appear in history with correct styling
  - [x]Manual E2E: Verify agent messages render markdown: bold, italic, code blocks, headings, lists, links
  - [x]Manual E2E: Verify no raw HTML execution — if agent sends `<script>alert(1)</script>`, it must NOT execute
  - [x]Manual E2E: Verify streaming agent messages show as plain text with cursor (no broken markdown mid-stream)
  - [x]Manual E2E: Verify completed agent messages render as markdown (after stream finishes)

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

This story spans **both backend and frontend**. It's the first story that modifies the ToolLoop's execution model.

| Component | File | Action |
|-----------|------|--------|
| `IConversationRecorder` | `Engine/IConversationRecorder.cs` | **MODIFY** — add `RecordUserMessageAsync` |
| `ConversationRecorder` | `Services/ConversationRecorder.cs` | **MODIFY** — implement `RecordUserMessageAsync` |
| `ToolLoop` | `Engine/ToolLoop.cs` | **MODIFY** — add `ConcurrentQueue<string>?` parameter, drain user messages |
| `ISseEventEmitter` | `Engine/Events/ISseEventEmitter.cs` | **MODIFY** — add `EmitUserMessageAsync` |
| `SseEventEmitter` | `Engine/Events/SseEventEmitter.cs` | **MODIFY** — implement `EmitUserMessageAsync` |
| `SseEventTypes` | `Engine/Events/SseEventTypes.cs` | **MODIFY** — add `UserMessage` constant |
| `IActiveInstanceRegistry` | `Engine/ActiveInstanceRegistry.cs` | **CREATE** — in-memory message queue registry |
| `StepExecutionContext` | `Engine/StepExecutionContext.cs` | **MODIFY** — add `UserMessageQueue` property |
| `StepExecutor` | `Engine/StepExecutor.cs` | **MODIFY** — pass queue to ToolLoop |
| `WorkflowOrchestrator` | `Engine/WorkflowOrchestrator.cs` | **MODIFY** — register/unregister instance, pass queue |
| `ExecutionEndpoints` | `Endpoints/ExecutionEndpoints.cs` | **MODIFY** — add POST message endpoint, inject registry |
| `SendMessageRequest` | `Models/ApiModels/SendMessageRequest.cs` | **CREATE** — request model |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | **MODIFY** — register `IActiveInstanceRegistry` |
| `types.ts` | `Client/src/api/types.ts` | **MODIFY** — add "user" to ChatMessage role union |
| `api-client.ts` | `Client/src/api/api-client.ts` | **MODIFY** — add `sendMessage()`, update `mapConversationToChat` |
| `markdown-sanitiser.ts` | `Client/src/utils/markdown-sanitiser.ts` | **CREATE** — markdown parsing + HTML sanitisation |
| `shallai-chat-message` | `Client/src/components/shallai-chat-message.element.ts` | **MODIFY** — add user role rendering, markdown for agent messages |
| `shallai-chat-panel` | `Client/src/components/shallai-chat-panel.element.ts` | **MODIFY** — add input area with send button |
| `shallai-instance-detail` | `Client/src/components/shallai-instance-detail.element.ts` | **MODIFY** — wire up message sending, handle user.message SSE, pass input state |
| `chat-message.test.ts` | `Client/src/chat-message.test.ts` | **MODIFY** — add user message mapping tests |
| `markdown-sanitiser.test.ts` | `Client/src/markdown-sanitiser.test.ts` | **CREATE** — sanitiser tests |
| Backend test files | `Tests/Engine/`, `Tests/Services/`, `Tests/Endpoints/` | **MODIFY/CREATE** — new tests |

### Backend Architecture: User Message Flow

```
User types message
    → shallai-chat-panel dispatches "send-message" event
    → shallai-instance-detail calls sendMessage(instanceId, text)
    → POST /umbraco/api/shallai/instances/{id}/message
    → ExecutionEndpoints.SendMessage enqueues to ConcurrentQueue
    → ToolLoop drains queue at start of each iteration
    → Message added to ChatMessage list (ChatRole.User)
    → ConversationRecorder records it
    → SseEventEmitter emits user.message SSE event
    → LLM sees the user message in next GetStreamingResponseAsync call
    → LLM responds, streamed via text.delta SSE events as usual
```

The key insight: the ToolLoop already iterates (call LLM → process tool calls → repeat). User messages are injected into the conversation `messages` list between iterations. The LLM naturally sees them on the next call. No new execution flow is needed — just a way to get messages into the list from outside the loop.

### ToolLoop Modification Details

The current ToolLoop signature:
```csharp
public static async Task<ChatResponse> RunAsync(
    IChatClient client,
    IList<ChatMessage> messages,
    ChatOptions options,
    IReadOnlyDictionary<string, IWorkflowTool> declaredTools,
    ToolExecutionContext context,
    ILogger logger,
    CancellationToken cancellationToken,
    ISseEventEmitter? emitter = null,
    IConversationRecorder? recorder = null)
```

Add `ConcurrentQueue<string>? userMessages = null` before `emitter`. The queue is checked at two points:
1. **Start of each iteration** (before calling the LLM) — catches messages sent during previous LLM response streaming
2. **After tool results are collected** (before looping back to call LLM again) — catches messages sent during tool execution

When a message is dequeued, three things happen atomically:
1. `messages.Add(new ChatMessage(ChatRole.User, userMsg))` — the LLM sees it
2. `recorder.RecordUserMessageAsync(userMsg)` — persisted to conversation history
3. `emitter.EmitUserMessageAsync(userMsg)` — frontend gets confirmation via SSE

### Markdown Sanitiser Design

**Why no external library:** The `package.json` has no markdown or sanitisation dependencies. Adding `marked` + `DOMPurify` would be ~50KB bundled. The LLM produces a predictable subset of markdown. A simple parser handles the common cases.

**Parsing approach:** Line-by-line processing:
1. Extract fenced code blocks first (``` delimited) → replace with placeholders
2. Process remaining lines:
   - Lines starting with `#` → headings
   - Lines starting with `- ` or `* ` → unordered list items (group consecutive)
   - Lines starting with `\d+. ` → ordered list items (group consecutive)
   - Lines starting with `> ` → blockquote
   - Lines that are `---` / `***` / `___` alone → `<hr>`
   - Everything else → paragraphs
3. Within text runs, apply inline patterns:
   - `` `code` `` → `<code>code</code>`
   - `**bold**` → `<strong>bold</strong>`
   - `*italic*` → `<em>italic</em>`
   - `[text](url)` → `<a>` with validation
4. Restore code block placeholders
5. Run through DOM whitelist sanitiser

**Sanitisation approach:** After markdown→HTML conversion:
1. Create a `<template>` element, set `innerHTML`
2. Walk all child nodes recursively
3. For each element: if tag not in whitelist, replace with its text content
4. For each allowed element: remove all attributes except `href`/`target`/`rel` on `<a>`
5. For `<a>`: validate href scheme, force `target="_blank" rel="noopener"`
6. Return `template.content` serialised back to HTML string

### Streaming vs Complete Markdown Rendering

During streaming, agent message content is incomplete — markdown parsed mid-stream produces broken HTML (e.g., `**bold` without closing `**`). Solution:
- **While `isStreaming === true`**: render content as plain text with `white-space: pre-wrap` (same as current behaviour) + blinking cursor
- **When `isStreaming` becomes false**: render content through `sanitiseMarkdown()` as HTML

This transition happens naturally when `_finaliseStreamingMessage()` sets `isStreaming: false` on the message. Lit re-renders the component, which now uses the markdown path.

### User Message Optimistic Updates

The frontend adds the user message to `_chatMessages` immediately (optimistic update). The backend processes it asynchronously. The SSE `user.message` event confirms the message was injected — the frontend ignores this echo to avoid duplication.

If the POST fails, a system error message is added to the chat. The user can retry.

### Deferred Work Addressed

This story addresses the deferred item from 6-2 review: "Chat panel and instance detail grid need fixed viewport height layout" — Task 14 adds `height: calc(100vh - 200px)` to `.detail-grid` so the chat panel fills a constrained area with the input pinned at the bottom.

### Architecture Compliance

- **Engine boundary respected**: `ActiveInstanceRegistry` is in `Engine/` — no Umbraco dependencies. `ConcurrentQueue` is pure .NET.
- **ToolLoop stays static**: The method signature gains a parameter but remains a static helper. No instance state.
- **Error handling**: Tool execution errors continue to be returned to the LLM, not thrown. User message recording failures are swallowed (same pattern as all ConversationRecorder methods).
- **Frontend conventions**: `shallai-` prefix, UUI design tokens, imports from `@umbraco-cms/backoffice/external/lit`, relative `.js` imports.

### Previous Story Intelligence (Story 6.3)

Key patterns to carry forward:
- **Immutable state updates** — all `_chatMessages` mutations use spread + slice (P3 fix from 6.2)
- **`_toolBatchOpen` flag** — clear this on `user.message` SSE event too? No — user messages don't affect tool batching. The flag is only for grouping sequential tool calls from the same LLM response.
- **`_finaliseStreamingMessage()` guard** — checks `last.isStreaming` before acting (P7 fix from 6.2)
- **`_viewingStepId` / `_historyMessages`** — input should be disabled when viewing history
- **Frontend tests are logic-only** — no DOM rendering tests (per deferred-work.md)
- **`tool.end` as secondary completion signal** — this pattern continues unchanged

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` — Epic 6, Story 6.4]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — Decision 1: Step Executor Integration Layer, Decision 2: Streaming Protocol, Decision 7: Security Architecture (XSS Sanitisation)]
- [Source: `_bmad-output/planning-artifacts/ux-design-specification.md` — shallai-chat-panel component spec, chat input states, chat message role colours, shallai-markdown-renderer sanitisation rules]
- [Source: `_bmad-output/planning-artifacts/prd.md` — FR41, FR42, FR57, NFR9]
- [Source: `_bmad-output/implementation-artifacts/6-3-tool-call-display.md` — Immutable state patterns, tool batch flag, SSE event handling]
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md` — Chat panel viewport height fix (from 6-2), error detection string matching (from 6-3)]
- [Source: `_bmad-output/project-context.md` — Lit import rules, UUI design tokens, async CancellationToken patterns, System.Text.Json]

## Failure & Edge Cases

1. **User sends message while LLM is mid-stream**: The ToolLoop is inside `client.GetStreamingResponseAsync` when the message arrives. The message sits in the `ConcurrentQueue` until the current streaming completes. Then the loop drains the queue before the next LLM call. The user sees their message immediately (optimistic update) but the LLM doesn't see it until its next turn. This is acceptable — the conversation is sequential. **No special handling needed.**

2. **User sends message during tool execution**: The ToolLoop is executing `tool.ExecuteAsync` when the message arrives. The queue is drained after tool results are collected (Task 2 second drain point). The LLM sees the user message along with the tool results on the next call. **No special handling needed.**

3. **User sends multiple messages rapidly**: Each message is enqueued independently. The drain loop processes all queued messages in order, adding each as a separate `ChatMessage(ChatRole.User, ...)`. The LLM sees them all. **No special handling needed beyond the drain loop.**

4. **User sends message when no step is active**: The POST endpoint checks `IActiveInstanceRegistry.GetMessageQueue(id)` — returns null when no ToolLoop is running. Returns 409. Frontend should never reach this state because the input is disabled when no step is active. **Defence-in-depth guard.**

5. **POST message endpoint called after step finishes but before SSE stream closes**: Small race window. The `WorkflowOrchestrator` unregisters the instance in its `finally` block after `ExecuteStepAsync` returns. Between the orchestrator's step-finished emit and its unregister call, a message could be enqueued but never drained. This is a very narrow window and the message would be lost. **Acceptable for v1 — the user would see "Step complete" in the UI shortly after and know the step is done.**

6. **SSE connection drops while user message is in queue**: The message is in the `ConcurrentQueue` and will be drained by the ToolLoop regardless of SSE state. The LLM sees it and responds. But the user won't see the response (SSE is dead). The conversation is recorded, so reviewing history shows both the user message and agent response. **Acceptable — NFR5 reconnection deferred.**

7. **Agent response contains malicious markdown**: `<script>alert(1)</script>` or `[click](javascript:void(0))` in agent content. The sanitiser strips all non-whitelisted elements and validates link hrefs. `<script>` is not in the whitelist → removed. `javascript:` href → stripped. **Must verify in E2E testing (Task 16).**

8. **Agent response contains nested/malformed markdown**: e.g., unclosed bold `**text`, overlapping formatting. The basic parser handles these gracefully by not matching incomplete patterns — they pass through as plain text. **Acceptable — LLMs produce well-formed markdown in practice.**

9. **Very long user message**: No length limit on the POST endpoint. The message is added to the LLM context, potentially exceeding token limits. The LLM provider will return an error, caught by ToolLoop's existing exception handling. **Acceptable for v1 — token management is a v2 concern.**

10. **Markdown rendering during streaming produces flash**: When `isStreaming` transitions to `false`, the message switches from plain text to markdown HTML. This could cause a visual flash (content re-layout). Mitigate by ensuring the markdown output closely resembles the plain text layout. **Verify in E2E — if jarring, consider a CSS transition.**

11. **`innerHTML` and Shadow DOM**: Lit's Shadow DOM scoping means styles in the component's `static styles` block apply to dynamically inserted HTML via `innerHTML`. The markdown styles (`.agent-content h1`, `.agent-content code`, etc.) will apply correctly because they're scoped to the component's shadow root. **Should work — verify in E2E.**

12. **`unsafeHTML` import availability**: The `@umbraco-cms/backoffice/external/lit` re-export may or may not include `unsafeHTML` directive. If not available, use a `<div>` element reference and set `.innerHTML` directly in `updated()` lifecycle. **Check import availability first — fallback approach documented in Task 10.**

13. **ConversationRecorder does not have RecordUserMessageAsync**: The interface currently only has `RecordAssistantTextAsync`, `RecordToolCallAsync`, `RecordToolResultAsync`, `RecordSystemMessageAsync`. We must add `RecordUserMessageAsync` (Task 1). Any existing mocks in tests (NSubstitute) will need updating if they substitute `IConversationRecorder`. **Check existing test mocks.**

14. **ToolLoop existing tests pass null for userMessages**: The new parameter has a default value of `null`, so all existing tests continue to work without modification. **No breaking changes.**

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Backend tests: 284 passed, 0 failed
- Frontend tests: 104 passed, 0 failed
- Frontend build: clean, 7 chunks

### Completion Notes List

- Task 1: Added `RecordUserMessageAsync` to `IConversationRecorder` interface and `ConversationRecorder` implementation with same error-swallowing pattern as other Record methods.
- Task 2: Modified `ToolLoop.RunAsync` to accept `ConcurrentQueue<string>? userMessages` parameter. Messages drained at two points: start of each iteration (before LLM call) and after tool results (before looping back). Extracted `DrainUserMessagesAsync` private helper.
- Task 3: Added `EmitUserMessageAsync` to `ISseEventEmitter`/`SseEventEmitter` with new `user.message` event type and `UserMessagePayload` record.
- Task 4: Created `ActiveInstanceRegistry` (interface + implementation) as singleton in-memory `ConcurrentDictionary<string, ConcurrentQueue<string>>`. Registered in `AgentRunnerComposer`. Wired through `StepExecutionContext` (new `UserMessageQueue` property), `StepExecutor` (passes to ToolLoop), and `WorkflowOrchestrator` (register/unregister in try/finally around step execution).
- Task 5: Added `POST /instances/{id}/message` endpoint to `ExecutionEndpoints`. Synchronous — enqueues message and returns immediately. Returns 400 for empty messages, 409 when instance not running.
- Task 6: 10 new backend tests — `ActiveInstanceRegistryTests` (5 tests), `ToolLoopTests` (2 user message tests), `ConversationRecorderTests` (1 user message test), `ExecutionEndpointsTests` (3 message endpoint tests).
- Task 7: Added `sendMessage()` function to `api-client.ts`.
- Task 8: Updated `ChatMessage.role` type union to include `"user"`.
- Task 9: Created `markdown-sanitiser.ts` — line-by-line markdown parser (headings, bold, italic, code, lists, links, blockquotes, hr, paragraphs) + DOM whitelist sanitiser. No external dependencies.
- Task 10: Updated `shallai-chat-message` — added user role rendering (plain text, emphasis background), markdown rendering for completed agent messages (streaming keeps plain text + cursor), agent-content CSS for markdown elements.
- Task 11: Added chat input area to `shallai-chat-panel` — `uui-textarea` + send button, `inputEnabled`/`inputPlaceholder` properties, Enter sends/Shift+Enter newline, dispatches `send-message` custom event.
- Task 12: Wired `shallai-instance-detail` — imports `sendMessage`, computes `inputEnabled`/`inputPlaceholder` from instance state, optimistic user message add, error fallback system message, `user.message` SSE echo ignored.
- Task 13: Added user role handling to `mapConversationToChat` — placed before system role check.
- Task 14: Fixed `.detail-grid` height to `calc(100vh - 200px)` with `min-height: 400px`.
- Task 15: 17 new frontend tests — 2 user message mapping tests in `chat-message.test.ts`, 15 markdown sanitiser tests in `markdown-sanitiser.test.ts`.
- Task 16: All backend tests pass (284), all frontend tests pass (104), frontend builds clean, TestSite builds clean.

### File List

**Backend — New:**
- Shallai.UmbracoAgentRunner/Engine/ActiveInstanceRegistry.cs
- Shallai.UmbracoAgentRunner/Models/ApiModels/SendMessageRequest.cs
- Shallai.UmbracoAgentRunner.Tests/Engine/ActiveInstanceRegistryTests.cs
- Shallai.UmbracoAgentRunner.Tests/Endpoints/ExecutionEndpointsTests.cs

**Backend — Modified:**
- Shallai.UmbracoAgentRunner/Engine/IConversationRecorder.cs
- Shallai.UmbracoAgentRunner/Services/ConversationRecorder.cs
- Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs
- Shallai.UmbracoAgentRunner/Engine/Events/ISseEventEmitter.cs
- Shallai.UmbracoAgentRunner/Engine/Events/SseEventEmitter.cs
- Shallai.UmbracoAgentRunner/Engine/Events/SseEventTypes.cs
- Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs
- Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs
- Shallai.UmbracoAgentRunner/Engine/WorkflowOrchestrator.cs
- Shallai.UmbracoAgentRunner/Endpoints/ExecutionEndpoints.cs
- Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs
- Shallai.UmbracoAgentRunner.Tests/Engine/ToolLoopTests.cs
- Shallai.UmbracoAgentRunner.Tests/Engine/WorkflowOrchestratorTests.cs
- Shallai.UmbracoAgentRunner.Tests/Services/ConversationRecorderTests.cs

**Frontend — New:**
- Client/src/utils/markdown-sanitiser.ts
- Client/src/markdown-sanitiser.test.ts

**Frontend — Modified:**
- Client/src/api/types.ts
- Client/src/api/api-client.ts
- Client/src/components/shallai-chat-message.element.ts
- Client/src/components/shallai-chat-panel.element.ts
- Client/src/components/shallai-instance-detail.element.ts
- Client/src/chat-message.test.ts

### Change Log

- 2026-04-01: Story 6.4 implemented — user messaging, multi-turn conversation, markdown rendering with XSS sanitisation
- 2026-04-01: Addendum — ToolLoop wait-for-user-message fix (architect-approved, Channel\<string\> approach)

---

## Addendum: ToolLoop Wait-for-User-Message Fix

**Status:** Ready for implementation
**Discovered during:** 6.4 code review / testing
**Scope:** Backend only — no frontend changes

### Problem

The ToolLoop exits when the LLM returns text with no tool calls. Interactive agents that ask a question and wait for user input hit the exit immediately — the completion check runs, fails (no artifact written yet), and the step errors. Multi-turn conversation is impossible in interactive workflows.

### Approach — Replace ConcurrentQueue with Channel\<string\>

Replace `ConcurrentQueue<string>` with `System.Threading.Channels.Channel<string>` throughout the message pipeline. Channel is the correct .NET async producer-consumer primitive — it eliminates the need for a separate signalling mechanism and has fewer moving parts than a SemaphoreSlim wrapper.

Use `Channel.CreateUnbounded<string>()` — messages are small and infrequent, bounded backpressure adds complexity with no benefit.

### Required Behaviour

| Condition | Action |
|---|---|
| `ChannelReader` is `null` (autonomous mode) | Exit immediately — return `ChatResponse`. Backward compatible with all existing workflows. |
| `ChannelReader` provided + no tool calls + `TryRead` succeeds | Drain all available messages, add as `ChatRole.User` messages, record + emit, then `continue` (call LLM again). |
| `ChannelReader` provided + no tool calls + nothing to read | Await `WaitToReadAsync` with a 5-minute timeout (linked `CancellationTokenSource`). If a message arrives, drain and `continue`. |
| Timeout (5 min) | Exit normally — return `ChatResponse`. The existing completion check in `StepExecutor` decides what happens next. |
| Cancellation token fired | Propagate `OperationCanceledException` as usual. Distinguish from timeout using `cancellationToken.IsCancellationRequested`. |

### Failure & Edge Cases

- **Multiple messages queued while waiting:** `WaitToReadAsync` returns `true`, then drain loop via `TryRead` picks up all queued messages in one pass before calling LLM. No messages lost.
- **Message arrives during LLM streaming:** Already handled — existing `DrainUserMessagesAsync` runs after tool execution. The Channel `TryRead` pattern is identical to the current `TryDequeue` pattern.
- **Endpoint writes to completed Channel:** Cannot happen — `ActiveInstanceRegistry.UnregisterInstance` runs in the `finally` block of `WorkflowOrchestrator.ExecuteNextStepAsync`. The endpoint checks `GetMessageWriter(id)` returns null and returns 409.
- **Double-drain race:** Not possible — `ChannelReader.TryRead` is thread-safe and each message is read exactly once.
- **Timeout + message arrives simultaneously:** `WaitToReadAsync` throws `OperationCanceledException` from the timeout CTS. The message stays in the channel but the loop exits. This is acceptable — the step completes/fails via the normal completion check path, and the message is not lost from the channel (it just won't be processed for this step).
- **MaxIterations with wait cycles:** Each wait-drain-continue counts as an iteration. The existing `MaxIterations` (100) guard still applies, preventing infinite conversation loops.

### Implementation Tasks

#### Task A: Refactor ActiveInstanceRegistry to use Channel\<string\>

In `Engine/ActiveInstanceRegistry.cs`:
- Change `ConcurrentDictionary<string, ConcurrentQueue<string>>` to `ConcurrentDictionary<string, Channel<string>>`
- `RegisterInstance` creates `Channel.CreateUnbounded<string>()`, stores the Channel, returns `ChannelReader<string>`
- Add `GetMessageWriter(string instanceId)` returning `ChannelWriter<string>?`
- Keep `GetMessageQueue` renamed to `GetMessageReader` returning `ChannelReader<string>?` (or remove if only the Writer is needed externally)
- Update `IActiveInstanceRegistry` interface signatures to match
- Update `ActiveInstanceRegistryTests` — replace queue assertions with Channel read/write assertions

#### Task B: Update StepExecutionContext

In `Engine/StepExecutionContext.cs`:
- Change `ConcurrentQueue<string>? UserMessageQueue` to `ChannelReader<string>? UserMessageReader`
- Add `using System.Threading.Channels;`

#### Task C: Update WorkflowOrchestrator

In `Engine/WorkflowOrchestrator.cs`:
- `RegisterInstance` now returns `ChannelReader<string>` — assign to a local and pass into `StepExecutionContext` as `UserMessageReader`
- Update `WorkflowOrchestratorTests` if they reference the queue type

#### Task D: Update ExecutionEndpoints

In `Endpoints/ExecutionEndpoints.cs` (`SendMessage` method):
- Replace `GetMessageQueue(id)` with `GetMessageWriter(id)`
- Replace `queue.Enqueue(request.Message)` with `writer.TryWrite(request.Message)` (unbounded channel — TryWrite always succeeds)
- Update `ExecutionEndpointsTests` to match new registry method names

#### Task E: Update ToolLoop — the core change

In `Engine/ToolLoop.cs`:
- Change parameter `ConcurrentQueue<string>? userMessages` to `ChannelReader<string>? userMessageReader`
- Update `DrainUserMessagesAsync` to use `TryRead` instead of `TryDequeue` — same loop structure
- At the "no tool calls" exit point (current line ~93), add the wait logic:

```csharp
if (functionCalls.Count == 0)
{
    if (userMessageReader is null)
    {
        return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
            ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
    }

    // Try non-blocking drain first
    var drained = await DrainUserMessagesAsync(
        userMessageReader, messages, recorder, emitter, cancellationToken);

    if (drained > 0)
        continue;

    // Nothing waiting — block with 5-minute timeout
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

    try
    {
        if (await userMessageReader.WaitToReadAsync(timeoutCts.Token))
        {
            await DrainUserMessagesAsync(
                userMessageReader, messages, recorder, emitter, cancellationToken);
            continue;
        }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        // Timeout — not real cancellation. Exit normally.
    }

    return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
        ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
}
```

- Update `DrainUserMessagesAsync` to return `int` (count of messages drained) and use `ChannelReader.TryRead`:

```csharp
private static async Task<int> DrainUserMessagesAsync(
    ChannelReader<string>? userMessageReader,
    IList<ChatMessage> messages,
    IConversationRecorder? recorder,
    ISseEventEmitter? emitter,
    CancellationToken cancellationToken)
{
    var count = 0;
    while (userMessageReader is not null && userMessageReader.TryRead(out var userMsg))
    {
        count++;
        messages.Add(new ChatMessage(ChatRole.User, userMsg));
        await (recorder?.RecordUserMessageAsync(userMsg, cancellationToken) ?? Task.CompletedTask);
        if (emitter is not null)
        {
            await emitter.EmitUserMessageAsync(userMsg, cancellationToken);
        }
    }
    return count;
}
```

#### Task F: Update StepExecutor

In `Engine/StepExecutor.cs`:
- Pass `context.UserMessageReader` instead of `context.UserMessageQueue` to `ToolLoop.RunAsync`

### Tests (Task G)

Add/update in `ToolLoopTests.cs`:
1. **Null reader exits immediately** — no tool calls + null reader → returns ChatResponse (existing behaviour, verify still works)
2. **Reader with queued message continues** — no tool calls + message pre-queued → drains, calls LLM again
3. **Reader waits then drains** — no tool calls + empty reader + message written after short delay → drains after signal, calls LLM again
4. **Timeout exits normally** — no tool calls + empty reader + no message arrives within timeout → returns ChatResponse (use a short timeout override for testing)
5. **Cancellation propagates OCE** — no tool calls + waiting + cancellation token fires → throws OperationCanceledException
6. **Multiple messages drained in one pass** — 3 messages queued → all 3 added to messages list before next LLM call
7. **Wait counts toward MaxIterations** — each wait-drain-continue cycle increments the iteration counter
8. **Messages during tool execution still drained** — existing drain-after-tools behaviour works with ChannelReader

Update in `ActiveInstanceRegistryTests.cs`:
9. **RegisterInstance returns reader, GetMessageWriter returns writer** — write via writer, read via reader
10. **UnregisterInstance removes channel** — GetMessageWriter returns null after unregister

### File List

**Modified:**
- `Shallai.UmbracoAgentRunner/Engine/ActiveInstanceRegistry.cs`
- `Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs`
- `Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs`
- `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs`
- `Shallai.UmbracoAgentRunner/Engine/WorkflowOrchestrator.cs`
- `Shallai.UmbracoAgentRunner/Endpoints/ExecutionEndpoints.cs`
- `Shallai.UmbracoAgentRunner.Tests/Engine/ToolLoopTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Engine/ActiveInstanceRegistryTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/ExecutionEndpointsTests.cs`
- `Shallai.UmbracoAgentRunner.Tests/Engine/WorkflowOrchestratorTests.cs`

### Testing Note

For tests that exercise the timeout path, extract the 5-minute timeout as an `internal const` (e.g. `internal static TimeSpan UserMessageTimeout = TimeSpan.FromMinutes(5)`) so tests can override it via `[assembly: InternalsVisibleTo]` or pass it as a parameter. Do NOT make tests wait 5 real minutes.
