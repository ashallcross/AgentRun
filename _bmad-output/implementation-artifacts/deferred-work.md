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
