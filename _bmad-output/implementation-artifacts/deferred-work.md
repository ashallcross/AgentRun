# Deferred Work

## Deferred from: code review of 1-1-scaffold-rcl-package-project (2026-03-30)

- AgentRunnerOptions string properties are nullable-unaware despite `<Nullable>enable</Nullable>` — public setters accept null via deserialization with no guard. Address when options binding is wired up.
- DataRootPath trailing slash not normalised — consumer code may produce inconsistent path comparisons. Address when path resolution logic is implemented.

## Deferred from: code review of 2-1-workflow-yaml-parsing-and-validation (2026-03-30)

- YAML date coercion on string fields — unquoted date-like values silently coerced by YamlDotNet. Cross-cutting concern; unlikely in workflow YAML but needs project-wide deserializer config review.
- List item type validation — `tools`, `reads_from`, `writes_to`, `files_exist` lists accept non-string items silently. Semantic validation is Story 5.4 scope.

## Deferred from: code review of 2-2-workflow-registry-and-discovery (2026-03-30)

- LoadWorkflowsAsync on IWorkflowRegistry interface — AC5 specifies only GetAllWorkflows/GetWorkflow as the public contract; load method exposes mutation to all consumers. Consider separating into IWorkflowRegistryLoader or moving to concrete class with forwarding DI registration.
- Path traversal via step.Agent in VerifyAgentFiles — Path.Combine with unsanitised relative path could resolve outside workflow folder. Currently only File.Exists (info disclosure risk via logs). Will be addressed by Story 5.2 path sandboxing when agent files are actually read.
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

- Path traversal on developer-authored paths — `Step.Agent`, `Step.Id`, and `WritesTo` paths used in `Path.Combine` are not validated to stay within their root folders. Inputs come from developer-authored YAML, not user input. Story 5.2 tool path sandboxing is the natural place for defence-in-depth.
- TOCTOU on File.Exists then ReadAllText — agent and sidecar files could be deleted between existence check and read. Theoretical; Story 4.3 instance locking will prevent concurrent modifications to running instances.

## Deferred from: code review of 4-2-profile-resolution (2026-03-31)

- `ProfileNotFoundException` missing typed `ProfileAlias` property — alias is baked into message string but not exposed as a property. Callers needing to programmatically inspect which alias failed must parse the exception message. Nice-to-have for future retry/fallback logic.

## Deferred from: code review of 4-3-step-executor-and-tool-loop (2026-03-31)

- AIFunction lambdas in StepExecutor contain actual execution logic (delegates passed to `AIFunctionFactory.Create`) — if the Umbraco.AI middleware pipeline ever includes `FunctionInvokingChatClient`, tools would execute twice (once by middleware, once by ToolLoop). Current architecture explicitly uses manual tool loop so this is latent only. Revisit if provider pipeline changes.

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
