# Deferred Work

## Deferred from: code review of 1-1-scaffold-rcl-package-project (2026-03-30)

- AgentRunnerOptions string properties are nullable-unaware despite `<Nullable>enable</Nullable>` — public setters accept null via deserialization with no guard. Address when options binding is wired up.
- ~~DataRootPath trailing slash not normalised — consumer code may produce inconsistent path comparisons. Address when path resolution logic is implemented.~~ **RESOLVED in Story 5.2** — `PathSandbox.ValidatePath` normalises trailing separators before `StartsWith` check.

## Deferred from: code review of 2-1-workflow-yaml-parsing-and-validation (2026-03-30)

- YAML date coercion on string fields — unquoted date-like values silently coerced by YamlDotNet. Cross-cutting concern; unlikely in workflow YAML but needs project-wide deserializer config review.
- List item type validation — `tools`, `reads_from`, `writes_to`, `files_exist` lists accept non-string items silently. Semantic validation is Story 5.4 scope.

## Deferred from: code review of 2-2-workflow-registry-and-discovery (2026-03-30)

- LoadWorkflowsAsync on IWorkflowRegistry interface — AC5 specifies only GetAllWorkflows/GetWorkflow as the public contract; load method exposes mutation to all consumers. Consider separating into IWorkflowRegistryLoader or moving to concrete class with forwarding DI registration.
- ~~Path traversal via step.Agent in VerifyAgentFiles — Path.Combine with unsanitised relative path could resolve outside workflow folder. Currently only File.Exists (info disclosure risk via logs). Will be addressed by Story 5.2 path sandboxing when agent files are actually read.~~ **RESOLVED in Story 5.2** — `PathSandbox` static helper available in `Security/PathSandbox.cs` for any code needing path validation.
- RegisteredWorkflow wraps mutable WorkflowDefinition — sealed class with get-only properties but Definition has public setters and mutable Steps list. Consumers can mutate singleton state through the reference. Pre-existing design from Story 2.1 WorkflowDefinition.

## Deferred from: code review of 2-3-workflow-list-api-and-dashboard (2026-03-30)

- Frontend tests verify logic in isolation, not component DOM rendering — Umbraco backoffice package exports aren't directly importable by web-test-runner. Tests cover data mapping, navigation logic, and API client but not actual Lit component lifecycle or DOM output.
- No CSRF token in fetch helper — `fetchJson` sends `credentials: 'same-origin'` but no anti-forgery token header. Low risk for GET-only, but becomes a pattern issue when POST endpoints are added (Story 3.2+).
- Test monkey-patches (fetch, pushState) lack cleanup guards — If an assertion throws before restore code, globals remain patched. Should use `afterEach` hooks for reliable cleanup.
- fetchJson doesn't validate Content-Type before calling response.json() — If server returns HTML (e.g., login redirect on expired session), `response.json()` throws an untyped `SyntaxError` that surfaces as the generic catch in the component.

## Deferred from: code review of 3-1-instance-state-management (2026-03-30)

- Read-modify-write race condition — `SetInstanceStatusAsync` and `UpdateStepStatusAsync` have no in-process locking. Two concurrent callers can both read stale state and overwrite each other. Add per-instance `SemaphoreSlim` when step executor (Epic 4) introduces concurrent callers.
- `CurrentStepIndex` never advanced — field exists on `InstanceState` but `UpdateStepStatusAsync` does not increment it when a step completes. Advancing is step execution logic (Story 4.3/4.5).
- No state machine enforcement on step/instance status transitions — any transition is accepted (e.g., Complete → Pending). Step executor (Epic 4) owns transition rules.

## Deferred from: code review of 3-2-instance-api-endpoints (2026-03-30)

- Cancel endpoint TOCTOU race between FindInstanceAsync and SetInstanceStatusAsync — status could change between the two calls allowing cancellation of a just-completed instance. Requires InstanceManager-level locking (same as 3-1 read-modify-write race).
- Delete endpoint TOCTOU race between FindInstanceAsync and DeleteInstanceAsync — same TOCTOU pattern. A concurrent status change could cause DeleteInstanceAsync to throw an unhandled InvalidOperationException (500). Requires InstanceManager-level locking.

## Deferred from: code review of 3-3-instance-list-dashboard (2026-03-31)

- _formatStep edge cases with out-of-range stepIndex or stepCount=0 — If backend returns `currentStepIndex >= stepCount`, displays "6 of 3". When stepCount=0, falls back to "Step N". Pre-existing backend data integrity concern.
- Empty workflowAlias not guarded — If URL ends with `/`, extractWorkflowAlias returns empty string. Umbraco router matching makes this unlikely in practice.
- relativeTime never shows years — Dates >365 days show "12 months ago", "24 months ago" etc. No year category. Instances unlikely to be that old at current scale.
- Promise.all partial failure — If getWorkflows succeeds but getInstances fails (or vice versa), both results are discarded. Acceptable for current scope.
- No abort on component disconnect — _loadData promise is fire-and-forget; if component disconnects mid-fetch, state sets on detached element. Standard Lit lifecycle pattern.
- Full workflow list fetch for name resolution — Fetches entire workflow list to resolve one name. Acceptable at current scale.

## Deferred from: manual testing of story-3.4 (2026-03-31)

- UX: Move workflow list into section tree sidebar — Currently workflows are listed in the main content area as a dashboard route. Should use a proper section tree (`ManifestTree` / `UmbTreeElement`) with workflows as tree items, so clicking a workflow in the left-hand tree opens its runs list directly. Saves a click and follows standard Umbraco backoffice convention from other sections. Structural rework — consider for Epic 9 or a dedicated UX improvement pass.

## Deferred from: code review of story-3.4 (2026-03-31)

- TOCTOU race in cancel endpoint — `CancelInstance` reads instance status, checks it's Running/Pending, then writes Cancelled. Between read and write, the workflow runner could advance to Completed/Failed. The cancel would overwrite a terminal status. No optimistic concurrency guard visible. Pre-existing from Story 3.2. [InstanceEndpoints.cs:82-106]

## Deferred from: code review of 4-1-prompt-assembly (2026-03-31)

- ~~Path traversal on developer-authored paths — `Step.Agent`, `Step.Id`, and `WritesTo` paths used in `Path.Combine` are not validated to stay within their root folders. Inputs come from developer-authored YAML, not user input. Story 5.2 tool path sandboxing is the natural place for defence-in-depth.~~ **RESOLVED in Story 5.2** — `PathSandbox` static helper available in `Security/PathSandbox.cs` for any code needing path validation.
- TOCTOU on File.Exists then ReadAllText — agent and sidecar files could be deleted between existence check and read. Theoretical; Story 4.3 instance locking will prevent concurrent modifications to running instances.

## Deferred from: code review of 4-2-profile-resolution (2026-03-31)

- `ProfileNotFoundException` missing typed `ProfileAlias` property — alias is baked into message string but not exposed as a property. Callers needing to programmatically inspect which alias failed must parse the exception message. Nice-to-have for future retry/fallback logic.

## Deferred from: code review of 4-3-step-executor-and-tool-loop (2026-03-31)

- ~~AIFunction lambdas in StepExecutor contain actual execution logic (delegates passed to `AIFunctionFactory.Create`) — if the Umbraco.AI middleware pipeline ever includes `FunctionInvokingChatClient`, tools would execute twice (once by middleware, once by ToolLoop). Current architecture explicitly uses manual tool loop so this is latent only. Revisit if provider pipeline changes.~~ **CONFIRMED in Story 5.2 E2E** — `FunctionInvokingChatClient` IS active in the Umbraco.AI pipeline and double-executes tool calls. After multiple ToolLoop iterations, the conversation history becomes malformed (`unexpected tool_use_id` in `tool_result` blocks), causing `AnthropicBadRequestException`. ~~Fix required: either disable `FunctionInvokingChatClient` in the pipeline when getting the chat client, or remove our ToolLoop dispatch and let the middleware handle execution (but we need custom tool filtering). **Blocking real E2E execution.**~~ **RESOLVED in Story 5.3** — replaced executable `AIFunction` with declaration-only `ToolDeclaration` (subclass of `AIFunctionDeclaration`) in `StepExecutor.cs`. The middleware sees no callable delegate and passes through. Also added `IWorkflowTool.ParameterSchema` to provide correct tool parameter schemas to the LLM — without this, tools received empty arguments because the factory generated zero-parameter schemas.

## Deferred from: code review of 4-4-artifact-handoff-and-completion-checking (2026-03-31)

- Null/empty entries inside `ReadsFrom`/`FilesExist` lists from YAML deserialization quirks — `Path.Combine(path, null)` throws `ArgumentNullException`, `Path.Combine(path, "")` returns folder path causing misleading "missing file" errors. Defensive validation belongs at YAML parse/validation boundary (Story 2.1 area), not in the Engine consumers.
- `NullCompletionCheck` StepExecutor test name is misleading — mock returns `Passed=true` regardless, so it doesn't uniquely prove the null path. The actual null handling is covered by `CompletionCheckerTests.NullCheck_ReturnsPassed`. Cosmetic issue only.

## Deferred from: code review of story-4.5 (2026-03-31)

- TOCTOU race on concurrent POST /start requests — two requests can both pass the Running guard and execute concurrently. Per-instance SemaphoreSlim locking explicitly deferred in story spec.
- No timeout/abort on frontend SSE stream reader — if server hangs, client waits indefinitely with no AbortController. Deferred to v2 (NFR5 reconnection scope).
- No way to resume failed autonomous workflow — if an intermediate step fails, instance is stuck at Failed with advanced CurrentStepIndex. Retry/resume is Story 7.2.

## Deferred from: code review of 5-1-tool-interface-and-registration (2026-03-31)

- Generic `Exception` in ToolLoop logged at Warning — unexpected failures (bugs, OOM, network) should be Error level for monitoring/alerting [Engine/ToolLoop.cs:133]
- Error catch blocks don't emit `tool.result` SSE event — success path emits both `tool.end` + `tool.result`, error paths only emit `tool.end`, creating inconsistency for frontend consumers [Engine/ToolLoop.cs:122-141]
- Emitter exceptions not caught — if SSE connection drops mid-step, emitter calls at lines 44/85/92/106 propagate uncaught and kill the step [Engine/ToolLoop.cs]
- `client.GetResponseAsync` exceptions propagate uncaught from ToolLoop — no retry or structured error for transient LLM failures [Engine/ToolLoop.cs:33]
- `functionCall.Name` null from malformed LLM response causes `ArgumentNullException` on `OrdinalIgnoreCase` dictionary lookup [Engine/ToolLoop.cs:70]
- Duplicate tool names in DI crash `StepExecutor.ToDictionary()` with unstructured `ArgumentException` — no guard or helpful error message [Engine/StepExecutor.cs:97]
- No test for CancellationToken propagation to `client.GetResponseAsync` [Engine/ToolLoop.cs:33]
- No test coverage for SSE emitter interactions — all ToolLoop tests pass null emitter
- `ExecuteAsync` returns `Task<object>` — null return value untested, may produce null `FunctionResultContent.Result`

## Deferred from: code review of 5-3-fetch-url-tool-with-ssrf-protection (2026-04-01)

- SsrfProtection depends on Tools namespace (ToolExecutionException) — Security/ importing from Tools/ is an inverted dependency direction. Could be fixed by moving ToolExecutionException to a shared namespace or introducing a security-specific exception type. Pre-existing architectural pattern.

## Deferred from: code review of 6-1-conversation-persistence (2026-04-01)

- stepId has no format validation for filesystem safety in WorkflowValidator — step IDs from workflow YAML are interpolated into filenames (`conversation-{stepId}.jsonl`) without character-set validation. A step ID containing path separators (e.g., `../../foo`) would escape the instance folder. The API endpoint validates stepId against the instance's Steps list (security gate per spec), but `ConversationStore.AppendAsync` is public and will be called directly by StepExecutor in a future story. Recommend adding step ID format regex (`^[a-zA-Z0-9_-]+$`) to `WorkflowValidator`.

## Deferred from: code review of 6-2-chat-panel-and-message-streaming (2026-04-01)

- No AbortController for SSE stream abort in disconnectedCallback — `disconnectedCallback` sets `_streaming = false` but cannot cancel the active fetch/reader. The SSE reader loop continues running on a detached component until the server closes the connection. Explicitly deferred per story spec (NFR5 scope). [shallai-instance-detail.element.ts]
- Engine→Services architecture boundary — `WorkflowOrchestrator` (Engine/) directly instantiates `ConversationRecorder` (Services/) via `new ConversationRecorder(...)` with `using Shallai.UmbracoAgentRunner.Services`. Should use a factory or inject `IConversationRecorder` to keep Engine dependency-free. Designed into story spec Task 4; refactor in a future story.
- step.finished SSE payload lacks stepName — `StepFinishedPayload` only carries `(StepId, Status)`. Frontend falls back to `stepId` when `stepName` is undefined. Pre-existing payload design from Story 4.5.
- CancellationToken inconsistency between success and error paths in ToolLoop — error path correctly uses `CancellationToken.None` for best-effort recording before rethrow, but success path uses live `cancellationToken`. If cancellation fires between streaming completion and recording, assistant text is lost. Low impact since `ConversationRecorder` swallows exceptions anyway.
- ~~Navigating away from a running instance and returning shows empty chat panel~~ — **RESOLVED in Story 7.2**: `_loadData()` now loads conversation history for the current step (Active/Error/last Complete) on mount when not streaming. Prior messages are visible on return. SSE reconnection (NFR5) still deferred.
- Chat panel and instance detail grid need fixed viewport height layout — currently the whole page scrolls instead of the chat panel scrolling independently within a viewport-filling region. The UX spec calls for "fixed-height regions" with `uui-scroll-container` and "input area pinned to bottom" (Story 6.4). The `.detail-grid` should fill available viewport height so the chat panel's scroll container works within a constrained area. Address when Story 6.4 adds the input area — the pinned input + fixed-height chat is one layout change.

## Deferred from: code review of 6-3-tool-call-display (2026-04-01)

- Fragile error detection via string matching — tool call error status detection uses `resultStr.startsWith("Tool '") && (resultStr.includes("error") || resultStr.includes("failed"))` in both SSE handler and history mapping. Matches spec exactly, but any change to backend error format silently breaks detection. Proper fix requires adding a structured `isError` field to the SSE `tool.result` payload and `ConversationEntry` (backend change).

## Deferred from: code review of 6-4-user-messaging-and-multi-turn-conversation (2026-04-01)

- `inputEnabled` tied to SSE stream state — `inputEnabled = this._streaming && !this._viewingStepId` means if the SSE connection drops, the input disables even though the ToolLoop may still be running server-side. User cannot send follow-up messages after a disconnection. Proper fix requires a separate `_awaitingInput` state or reconnection logic. Belongs with NFR5 AbortController/SSE reconnection work. [shallai-instance-detail.element.ts:708]
- Weak test for second ToolLoop drain point — `UserMessageQueue_DrainedAfterToolResults` test passes regardless of whether the second drain point (after tool results, before next LLM call) exists, because the first drain at the start of the next iteration would also catch the message. Making it properly verify ordering requires capturing what the second `GetStreamingResponseAsync` call receives. The code is correct — the test is just not uniquely proving the second drain point. [ToolLoopTests.cs]
- `run.started` and `system.message` SSE events not handled in frontend — both event types are emitted by the backend (since Story 4.5) but silently dropped by the `_handleSseEvent` switch statement. `run.started` is informational only (UI is already in streaming mode). `system.message` should add a system-role message to `_chatMessages` for visibility (e.g., "Auto-advancing to Step 2..."). Pre-existing gap, not introduced by 6.4. [shallai-instance-detail.element.ts]
- Table markdown not supported in parser — `ALLOWED_TAGS` whitelist includes `table`, `tr`, `td`, `th`, `thead`, `tbody` for HTML passthrough, but `markdownToHtml` has no pipe-table parsing logic. Markdown tables (`| col | col |`) render as plain text paragraphs. Only raw HTML tables pass through the sanitiser. Acceptable per story spec ("Tables are stretch — omit if complex"). [markdown-sanitiser.ts]
- `uui-textarea` cast to `HTMLTextAreaElement` — `_onInput` handler casts `e.target` to `HTMLTextAreaElement` but `uui-textarea` is a web component, not a native textarea. `.value` works via UUI's API but the type cast is technically incorrect. Low impact. [shallai-chat-panel.element.ts:124]
- **Conversation context management / token efficiency** — The ToolLoop sends the full conversation history to the LLM on every call. For multi-turn interactive conversations, this means the LLM sees its original greeting, the user's response, and everything again — leading to repetitive responses and wasted tokens. In the original audit app, work was chunked and stored in .md/.yaml artifact files to reduce context size. Needs a dedicated story covering: (1) conversation summarisation (compress older messages into a summary before sending to LLM), (2) artifact-based memory (store intermediate work in files, reference them instead of carrying full text in context), (3) context windowing (only send last N messages + system prompt + relevant artifact refs). This is critical for production use — without it, long conversations will hit token limits and burn budget. [Engine/ToolLoop.cs]
- **[BLOCKING MULTI-TURN] ToolLoop exits immediately when LLM returns no tool calls** — the loop treats "no function calls in response" as the exit condition (ToolLoop.cs:92-97). For conversational agents that ask a question and wait for user input, this exits the loop immediately and the step fails the completion check. The user message queue was never drained because the loop already returned. **Fix requires**: (1) a signalling mechanism (e.g. `SemaphoreSlim` or `TaskCompletionSource` on `ActiveInstanceRegistry`) so the ToolLoop can `await` a user message instead of exiting, (2) a configurable timeout so the loop doesn't block forever if the user abandons, (3) updating the exit condition so "no tool calls" only exits when there's no user message queue (autonomous) or the wait times out, (4) new tests for the wait/signal/timeout behaviour. This is the core enabler for interactive multi-turn conversation — without it, Story 6.4's user messaging only works if the user sends a message while the agent is processing tool calls, not when the agent asks a question and waits. Recommend a dedicated story in Epic 7 or a 6.4-follow-up.

## Deferred from: code review of 7-0-interactive-mode-label-ux-cleanup (2026-04-02)

- **Failed+interactive: list shows "In progress" but instance-detail shows "Workflow complete"** — `displayStatus("Failed", "interactive")` returns "In progress" (AC #7), but `shallai-instance-detail.element.ts` line 824 treats Failed as `isTerminal = true`, showing "Workflow complete" with input disabled. Story 7.2 (step retry) must make Failed non-terminal in interactive mode — the Retry button can't coexist with "Workflow complete" / disabled input. This resolves naturally when 7.2 is specced.
- **Cancelled autonomous instance shows "Workflow complete" placeholder** — Instance-detail's `isTerminal` check includes "Cancelled", showing "Workflow complete" which is technically inaccurate for a cancelled run. Minor, cosmetic-only.

## Deferred from: code review of 7-1-llm-error-detection-and-reporting (2026-04-02)

- No inner exception inspection in LlmErrorClassifier — provider SDKs may wrap HttpRequestException in their own exception types. Walking `InnerException` would improve classification accuracy but is a pre-existing gap not introduced by this change. [Engine/LlmErrorClassifier.cs]

## Deferred from: code review of 7-2-step-retry-with-context-management (2026-04-02)

- Test RetryInstance_FailedInstance relies on NullReferenceException catch — test verifies state mutations by catching NullReferenceException from missing HttpResponse. Brittle but doesn't affect production code. Pre-existing test pattern across endpoint tests.
- File truncation not concurrency-safe — TruncateLastAssistantEntryAsync does read-modify-write without locking. Same pre-existing pattern across all ConversationStore file operations (AppendAsync also has no lock). Per-instance SemaphoreSlim from 3-1 deferred item covers this.
- Truncation of tool-call assistant entry could leave orphaned tool results — if last assistant entry is a tool-call, removing it leaves orphaned tool-result entries. Theoretical; error always occurs during LLM call, not after tool execution.
- No rollback if status update throws after truncation — truncation modifies conversation file before status updates; if status update throws, conversation is truncated but instance remains Failed. Pre-existing pattern; no transactions across file+state operations.
- FindIndex returns first Error step not last — if multiple steps somehow have Error status, wrong step gets retried. Only one step can be in Error per orchestrator flow.
- Consecutive assistant text + tool-call entries produce two adjacent assistant messages in ConvertHistoryToMessages — some LLM providers reject consecutive same-role messages. Theoretical; providers don't mix text and tool calls in same turn.
