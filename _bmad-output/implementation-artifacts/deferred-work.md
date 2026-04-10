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
- ~~**[BLOCKING MULTI-TURN] ToolLoop exits immediately when LLM returns no tool calls**~~ — **RESOLVED in Story 6.4 implementation**: ToolLoop now drains queued messages, checks completion, emits `input.wait` SSE event, then blocks on `WaitToReadAsync` with 5-minute timeout (ToolLoop.cs:94-149). Interactive multi-turn works correctly.

## Deferred from: code review of 7-0-interactive-mode-label-ux-cleanup (2026-04-02)

- **Failed+interactive: list shows "In progress" but instance-detail shows "Workflow complete"** — `displayStatus("Failed", "interactive")` returns "In progress" (AC #7), but `shallai-instance-detail.element.ts` line 824 treats Failed as `isTerminal = true`, showing "Workflow complete" with input disabled. Story 7.2 (step retry) must make Failed non-terminal in interactive mode — the Retry button can't coexist with "Workflow complete" / disabled input. This resolves naturally when 7.2 is specced.
- **Cancelled autonomous instance shows "Workflow complete" placeholder** — Instance-detail's `isTerminal` check includes "Cancelled", showing "Workflow complete" which is technically inaccurate for a cancelled run. Minor, cosmetic-only.

## Deferred from: code review of 7-1-llm-error-detection-and-reporting (2026-04-02)

- No inner exception inspection in LlmErrorClassifier — provider SDKs may wrap HttpRequestException in their own exception types. Walking `InnerException` would improve classification accuracy but is a pre-existing gap not introduced by this change. [Engine/LlmErrorClassifier.cs]

## Deferred from: code review of 8-1-artifact-api-and-markdown-renderer (2026-04-02)

- Symlink escape test coverage — no endpoint-level test for symlink traversal returning 400. PathSandbox handles internally; creating symlinks in unit tests is platform-dependent. Consider integration test if needed.
- Binary file handling with ReadAllTextAsync — spec says "return raw bytes with text/plain" for binary files, but `ReadAllTextAsync` may corrupt invalid UTF-8 sequences. Binary artifacts not expected in v1. If binary artifact support is added later, switch to `ReadAllBytesAsync` + `File()` result.

## Deferred from: code review of 7-2-step-retry-with-context-management (2026-04-02)

- Test RetryInstance_FailedInstance relies on NullReferenceException catch — test verifies state mutations by catching NullReferenceException from missing HttpResponse. Brittle but doesn't affect production code. Pre-existing test pattern across endpoint tests.
- File truncation not concurrency-safe — TruncateLastAssistantEntryAsync does read-modify-write without locking. Same pre-existing pattern across all ConversationStore file operations (AppendAsync also has no lock). Per-instance SemaphoreSlim from 3-1 deferred item covers this.
- Truncation of tool-call assistant entry could leave orphaned tool results — if last assistant entry is a tool-call, removing it leaves orphaned tool-result entries. Theoretical; error always occurs during LLM call, not after tool execution.
- No rollback if status update throws after truncation — truncation modifies conversation file before status updates; if status update throws, conversation is truncated but instance remains Failed. Pre-existing pattern; no transactions across file+state operations.
- FindIndex returns first Error step not last — if multiple steps somehow have Error status, wrong step gets retried. Only one step can be in Error per orchestrator flow.
- Consecutive assistant text + tool-call entries produce two adjacent assistant messages in ConvertHistoryToMessages — some LLM providers reject consecutive same-role messages. Theoretical; providers don't mix text and tool calls in same turn.

## Deferred from: code review of 8-2-artifact-viewer-and-step-review (2026-04-02)

- Tests are logic-only stubs — none mount actual components. All 23 new tests test extracted helper logic in isolation. Established project pattern since story 2-3. Actual behavior verified via manual E2E.
- SSE `run.finished` + `_loadData` failure leaves floating popover — if `_loadData()` fails after `run.finished` sets `_popoverOpen = true`, the error template replaces the parent but the popover overlay floats with no dismiss handler. Unlikely in practice; requires `_loadData` failure after successful SSE stream.

# notes from Adam
I have noticed that if you run a workflow that has 2 steps in. If you run the first step to completion, you get the green bar at the top which allows you to proceed to the next step, which is fine.

But when you go out of the workflow and then back in, the green bar has gone and there is no way to proceed to step 2. This is a bug we need to fix
## Deferred from: code review of R1-rename-shallai-to-agentrun (2026-04-02)

- `agentrun-tool-call.element.ts` missing `HTMLElementTagNameMap` declaration — all other custom elements have it. Pre-existing gap, not caused by rename. [AgentRun.Umbraco/Client/src/components/agentrun-tool-call.element.ts]

## Deferred from: code review of 9-6-workflow-configurable-tool-limits (2026-04-07)

- LOH allocation pressure in `FetchUrlTool`: `new byte[maxBytes + 1]` up to 20 MB at default ceiling. Pre-existing pattern, exposed wider by 9.6.
- `maxBytes + 1` integer overflow if validator allows `int.MaxValue`. Add upper bound to validator.
- `CancelAfter` rejects spans > ~24.8 days; `timeout_seconds` upper edge unbounded by validator.
- UTF-8 mid-codepoint truncation in `FetchUrlTool` produces replacement characters. Pre-existing.
- Out-of-scope drift in 9.6: `WorkflowDefinition.Mode` default changed to `"interactive"`, `ValidateMode` made optional. Reconcile with whichever epic actually owns this.
- Out-of-scope drift in 9.6: `AllowedRootKeys` / `AllowedStepKeys` extended with `icon`, `variants`, `data_files`, `description` — accepted but never parsed onto the model. Either implement or remove.
- Out-of-scope drift in 9.6: content-quality-audit example workflow wholesale rewrite (new agents, deleted sample content, renamed steps). Story only asked for the `tool_defaults` block.

## Deferred from: Story 9.1b Phase 1 carve-out #3 (2026-04-09)

- **Story 9.6.1 — Wire `MaxOutputTokens` through `IToolLimitResolver` with per-step / per-workflow override.** Phase 1 carve-out #3 hardcoded `MaxOutputTokens = 32768` directly on `StepExecutor.cs:160` to unblock the CQA scanner stall (FinishReason=length on multi-URL batches). The hardcode is intentional and correct for the immediate fix — Adam needed beta unblocked and a single-line literal change was the minimum-blast-radius path. The proper home is the existing 9.6 resolver pattern alongside `fetch_url_max_response_bytes` and `fetch_url_timeout_seconds`. Acceptance: workflow authors can set a global default in `tool_defaults`, override per-step via the resolver chain, validator enforces a sane upper bound (Sonnet 4.6 documented max output is 64k — that's the natural ceiling). Migration: replace the literal `32768` on `StepExecutor.cs:160` with a resolver call. Trivial change, well-bounded scope. Track as Story 9.6.1.
- **CQA scanner: incremental writes for crash-resilience.** Original CQA design (Story 9.7 era) had a per-fetch file-operation rhythm that was lost when 9.1b moved parsing server-side. The "rhythm regression" was investigated as a candidate root cause for the multi-URL stall but turned out NOT to be the cause (the actual root cause was MaxOutputTokens — see Phase 1 carve-out #3). However, restoring per-fetch incremental writes is still a real durability improvement for an unrelated reason: if the workflow crashes at fetch #4 of 5, the user's `scan-results.md` should already contain pages 1-3 instead of being empty. Backlog item with a clear motivation that doesn't depend on the stall narrative — file as a future polish item, not Phase 1 / not Phase 2. Architect (Winston) shelved-not-cancelled 2026-04-09 with explicit guidance: "If it never gets prioritised, fine. The contract is preserved either way."

## Deferred from: Story 9.1b trustworthiness signoff verification (2026-04-09)

These were caught by inspecting the artefacts produced by the trustworthiness gate run (instance `64dc770a0d6b4ae2aea9a3f97454aa02`). They are cosmetic / editorial-judgement polish, not correctness or blocker issues. Non-blocking for beta launch.

- **Agents stamp `2025-07-10` (model training-cutoff default) instead of today's actual date in artefact headers.** Affects the `Date:` line at the top of `scan-results.md`, `quality-scores.md`, and `audit-report.md`. Root cause: the workflow does not currently pass a current-date variable into the agent prompts, so the model is guessing. Cosmetic only — does not affect any audit content correctness. Fix: add a `{current_date}` template variable to the prompt assembler that interpolates `DateTime.UtcNow.ToString("yyyy-MM-dd")` (or the locale-appropriate format) at prompt-build time. Cross-cutting prompt-assembly improvement — touches `PromptAssembler` and adds a variable to every workflow step's available substitutions. Worth doing as a small dedicated story rather than buried inside another change.
- **CQA reporter over-scopes "remove navigation labels from H2 headings" findings on long marketing pages.** The analyser/reporter conflates site-footer navigation H2 labels (e.g. "Platform & Hosting", "Partners", "Learn", "Develop") with editorial content drift. Specific evidence: instance `64dc770a0d6b4ae2aea9a3f97454aa02` `audit-report.md` Action #7 flags this on both `umbraco.com` and `umbraco.com/products/umbraco-cms`. Phase 2 polish item — give the analyser/reporter prompts a heuristic that long flat H2 lists at the bottom of a page (especially with labels matching common nav patterns like "Get to know us", "Connect", "Develop") are almost always footer navigation, not editorial drift. Belongs in the Phase 2 polish loop budget when more real test inputs are accumulated.
- **CQA reporter recommends adding a meta description to Wikipedia at high priority.** Specific evidence: instance `64dc770a0d6b4ae2aea9a3f97454aa02` `audit-report.md` Action #2 recommends adding a meta description to `en.wikipedia.org/wiki/Content_management_system` despite Wikipedia intentionally omitting meta descriptions as a site-wide policy. The model partially saved itself by hedging "If you do not own this page, note it as an uncontrolled external asset" but the action is still listed at #2 priority. Phase 2 polish item — teach the reporter to deprioritise (or skip) findings for non-owned domains, or to detect known platforms with policy-based markup conventions (Wikipedia, GitHub README pages, etc.) and apply different rules. Belongs in the Phase 2 polish loop budget.

## Deferred from: code review of 9-2-json-schema-for-workflow-yaml (2026-04-10)

- **`NormalizeForJson` blind scalar coercion in `WorkflowSchemaTests.cs`.** The test's YAML→JSON helper coerces *any* string scalar that parses as `long`/`double`/`bool` to that type. The current shipped CQA workflow has no fields whose values look numeric or boolean, so the bug is latent — but a future workflow with e.g. `id: "001"`, `description: "true"`, or `name: "1.0"` would fail schema validation in the test even though the runtime accepts it. Cleaner fix: drop the manual coercion and rely on YamlDotNet's typed scalar resolution from `Deserialize<object?>()` (which already returns `int`/`double`/`bool` for unquoted YAML scalars and preserves `string` for quoted). Cosmetic / latent — fix when a real workflow trips it.
- **Schema rejects `tool_defaults: null` and `completion_check: null`; runtime validator accepts both.** Tiny validator-vs-schema drift: `WorkflowValidator.ValidateToolTuningBlock` early-returns when the block value is null (treats as absent), and `completion_check` similarly tolerates null, but the schema's `type: object` rejects JSON `null`. Per the locked decision "validator wins ties / schema must accept everything the validator accepts" the schema is technically too strict here. Nobody writes `tool_defaults: null` in practice. Either widen schema leaves to `"type": ["object", "null"]` or tighten the validator. Lowest-priority of the 9.2 review findings.

## Deferred from: code review of 9-10-workflow-definition-path-traversal-validation (2026-04-10)

- `StringComparison.Ordinal` in defence-in-depth `StartsWith` checks is case-sensitive — incorrect on Windows (NTFS case-insensitive). Should use `OrdinalIgnoreCase` on Windows. [PromptAssembler.cs:32, ArtifactValidator.cs:34, CompletionChecker.cs:33]
- Windows reserved device names (CON, NUL, AUX) and alternate data stream colons not rejected by `ValidatePathSafety`. Windows-specific, not applicable to current macOS/Linux target. [WorkflowValidator.cs:402-450]
- Windows-illegal filename chars (`<`, `>`, `"`, `|`, `*`, `?`) not blocked in ConversationStore step ID check. Windows-specific. [ConversationStore.cs:172]
- `TrimEnd(Path.DirectorySeparatorChar)` on root path `/` produces empty string, allowing all absolute paths to pass the containment check. Instance/workflow folders should never be root. [PromptAssembler.cs:31, ArtifactValidator.cs:33]

## Deferred from: Story 9.1c Task 7.4 manual E2E exploration (2026-04-10)

- **Instance resume after step completion fails — chat input rejects all messages with "Failed to send message. Try again."** Found by Adam during 9.1c Task 7.4 manual E2E. After completing the scanner step on instance `f95d49e1ca1c485faeb79313d453e958` (5-URL multi-fetch run), Adam navigated away and then reopened the instance from the instance list. The scanner had completed (`Step scanner completed` … `Advanced CurrentStepIndex to 1`), but on resume the chat input rejected every message — `ok whats next`, `hello`, `hi` — with the UI error "Failed to send message. Try again." No engine log entries for the failed sends, so the failure is UI-side, not engine-side. **Two distinct hypotheses, both plausible, both need investigation:** (1) the orchestrator pauses workflow advancement when an instance is suspended in the UI and does not pick the analyser back up on reopen; (2) the chat surface in resumed instances posts to the wrong step (the completed scanner index 0 instead of the active analyser index 1) and the engine silently rejects the post. Reproduction: start a CQA instance, run the scanner to completion, navigate away, reopen, attempt to send any chat message. Severity: Medium — affects basic "I left and came back" UX expectations but does not block any happy path that runs to completion in one sitting. **Not** a 9.1c regression and **not** a 9.1b regression — pre-existing instance-lifecycle gap surfaced by exploratory QA. Suggested home: a new Epic 10 story (e.g. **10.X — Instance resume after step completion**) before public 1.0; safe to ship private beta with this open if it's documented in known-issues. Full finding artefact: [bug-finding-2026-04-10-instance-resume-after-step-completion.md](../planning-artifacts/bug-finding-2026-04-10-instance-resume-after-step-completion.md).
