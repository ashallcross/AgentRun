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
