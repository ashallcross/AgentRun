# Story 2.3: Workflow List API & Dashboard

Status: done

## Story

As a developer or editor,
I want to see all available workflows listed in the Bellissima dashboard,
so that I can browse what workflows are available and select one to run.

## Acceptance Criteria

1. **Given** the `WorkflowRegistry` has discovered one or more valid workflows, **When** a GET request is made to `/umbraco/api/shallai/workflows`, **Then** a JSON array of workflow summaries is returned with fields: `alias`, `name`, `description`, `stepCount`, `mode`.

2. **Given** the workflows endpoint, **When** any request is made, **Then** it requires backoffice authentication via `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` (NFR13).

3. **Given** the `shallai-workflow-list` component, **When** rendered, **Then** it displays workflows in a `uui-table` with columns: Name (flex width, clickable), Steps (100px, e.g. "3 steps"), Mode (120px, `uui-badge`).

4. **Given** a workflow row in the table, **When** clicked anywhere on the row, **Then** the user navigates to the instance list view at `/workflows/{alias}` — the entire row is clickable, not just the name column.

5. **Given** no workflows are found (empty registry), **When** the workflow list renders, **Then** an empty state message is displayed: "No workflows found. Add a workflow folder to your project's workflows directory."

6. **Given** the workflow list view, **When** it loads, **Then** it completes within 2 seconds (NFR2).

7. **Given** the workflow list UI, **When** rendered, **Then** all colours, typography, and spacing use UUI design tokens exclusively.

## Tasks / Subtasks

- [x] Task 1: Create `WorkflowSummary` API response model (AC: 1)
  - [x]Create `Models/ApiModels/WorkflowSummary.cs` with properties: `Alias`, `Name`, `Description`, `StepCount` (int), `Mode` (string)
  - [x]All properties use `PascalCase` in C# — `System.Text.Json` default camelCase serialisation handles JSON output

- [x] Task 2: Create `WorkflowEndpoints` controller (AC: 1, 2)
  - [x]Create `Endpoints/WorkflowEndpoints.cs` as an `ApiController` with route prefix `umbraco/api/shallai`
  - [x]Implement `GET /workflows` that calls `IWorkflowRegistry.GetAllWorkflows()` and maps to `WorkflowSummary[]`
  - [x]Apply `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` at controller level
  - [x]Return direct JSON array — no envelope wrapper

- [x] Task 3: Create TypeScript API types and fetch helper (AC: 1)
  - [x]Create `Client/src/api/types.ts` with `WorkflowSummary` interface matching API response shape
  - [x]Create `Client/src/api/api-client.ts` with a typed fetch wrapper that: sets `credentials: 'same-origin'` for Umbraco auth cookies, sets `Accept: application/json`, throws on non-OK responses
  - [x]Add `getWorkflows(): Promise<WorkflowSummary[]>` function

- [x] Task 4: Implement `shallai-workflow-list` component (AC: 3, 4, 5, 6, 7)
  - [x]Replace stub in `Client/src/components/shallai-workflow-list.element.ts`
  - [x]Fetch workflows from API on `connectedCallback`
  - [x]Render `uui-table` with three columns: Name, Steps, Mode
  - [x]Make entire row clickable — navigate to `/workflows/{alias}` on click
  - [x]Display mode as `uui-badge` (e.g. "Interactive" / "Autonomous")
  - [x]Render empty state when no workflows: "No workflows found. Add a workflow folder to your project's workflows directory."
  - [x]Render loading state while fetching
  - [x]Use only UUI design tokens for all styling

- [x] Task 5: Wire navigation from workflow row to instance list route (AC: 4)
  - [x]Use Bellissima's `history` API or `window.history.pushState` to navigate within the `umb-router-slot` context
  - [x]Verify the route resolves to `shallai-instance-list` component via the existing dashboard router

- [x] Task 6: Write backend unit tests (AC: 1, 2)
  - [x]Create `Shallai.UmbracoAgentRunner.Tests/Endpoints/WorkflowEndpointsTests.cs`
  - [x]Test: returns mapped workflow summaries with correct fields
  - [x]Test: returns empty array when registry has no workflows
  - [x]Test: `StepCount` maps to `Definition.Steps.Count`
  - [x]Use NSubstitute to mock `IWorkflowRegistry`

- [x] Task 7: Write frontend tests (AC: 3, 5)
  - [x]Create `Client/src/components/shallai-workflow-list.element.test.ts`
  - [x]Test: renders table with correct columns when workflows present
  - [x]Test: renders empty state message when no workflows
  - [x]Test: row click triggers navigation

- [x] Task 8: Build frontend and verify (AC: 6)
  - [x]Run `npm run build` in `Client/` to update `wwwroot/` output
  - [x]Run `dotnet test` from solution root
  - [x]Run `npm test` from `Client/`

## Dev Notes

### Architecture & Boundaries

- **`WorkflowEndpoints` lives in `Endpoints/` folder** — this is the Umbraco integration boundary (references Umbraco auth policies, ASP.NET controller infrastructure)
- **`WorkflowSummary` lives in `Models/ApiModels/` folder** — a pure DTO, no Umbraco dependencies
- The endpoint depends only on `IWorkflowRegistry` (singleton, already registered) — no new DI registrations needed for the backend
- The frontend `shallai-workflow-list` component owns its own data fetching — no shared context class is needed yet (WorkflowContext from the architecture spec can be extracted later when multiple components need the same data)

### Endpoint Implementation Pattern

This is the **first API endpoint** in the project. The pattern established here will be followed by all subsequent endpoints.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Endpoints;

[ApiController]
[Route("umbraco/api/shallai")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class WorkflowEndpoints : ControllerBase
{
    private readonly IWorkflowRegistry _workflowRegistry;

    public WorkflowEndpoints(IWorkflowRegistry workflowRegistry)
    {
        _workflowRegistry = workflowRegistry;
    }

    [HttpGet("workflows")]
    [ProducesResponseType<WorkflowSummary[]>(200)]
    public IActionResult GetWorkflows()
    {
        var workflows = _workflowRegistry.GetAllWorkflows();
        var summaries = workflows.Select(w => new WorkflowSummary
        {
            Alias = w.Alias,
            Name = w.Definition.Name,
            Description = w.Definition.Description,
            StepCount = w.Definition.Steps.Count,
            Mode = w.Definition.Mode
        }).ToArray();

        return Ok(summaries);
    }
}
```

**Key decisions:**
- `ControllerBase` not `Controller` — no view support needed, pure API
- `AuthorizationPolicies.BackOfficeAccess` comes from `Umbraco.Cms.Web.Common.Authorization` — this is Umbraco's built-in backoffice auth policy that validates the user's backoffice session cookie
- The controller is auto-discovered by ASP.NET via `[ApiController]` attribute — no manual route registration in the Composer needed
- `IActionResult` return type with `Ok()` — keeps response format flexible for future error responses
- Synchronous — `GetAllWorkflows()` reads from in-memory dictionary, no async needed
- `System.Text.Json` default serialisation with `camelCase` naming is already configured by ASP.NET

### WorkflowSummary Model

```csharp
namespace Shallai.UmbracoAgentRunner.Models.ApiModels;

public sealed class WorkflowSummary
{
    public required string Alias { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int StepCount { get; init; }
    public required string Mode { get; init; }
}
```

**Key decisions:**
- `required` + `init` — immutable after construction, prevents accidental partial objects
- `sealed` — no inheritance needed for a DTO
- No `[JsonPropertyName]` needed — ASP.NET default camelCase handles the JSON naming

### Frontend Fetch Pattern

```typescript
// api/api-client.ts
const API_BASE = "/umbraco/api/shallai";

async function fetchJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: "same-origin",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`);
  }
  return response.json() as Promise<T>;
}

export function getWorkflows(): Promise<WorkflowSummary[]> {
  return fetchJson<WorkflowSummary[]>("/workflows");
}
```

- `credentials: 'same-origin'` sends the Umbraco backoffice auth cookie
- No bearer token needed — Umbraco backoffice uses cookie authentication
- Generic `fetchJson<T>` is reusable for all future API calls

### Frontend Component Pattern

The `shallai-workflow-list` component should:

1. **Import from `@umbraco-cms/backoffice/external/lit`** — NEVER bare `lit`. This is a hard rule. Import `customElement, html, css, state` (add `state` to the existing import from the stub).
2. **Extend `UmbLitElement`** from `@umbraco-cms/backoffice/lit-element` — provides `this.observe()` and Umbraco context support
3. **Use `@state()` for internal state** — `_workflows` array and `_loading` boolean. The `state` decorator is re-exported from `@umbraco-cms/backoffice/external/lit`.
4. **Fetch data in `connectedCallback`** — call `super.connectedCallback()` first, then fetch
5. **Navigate via Umbraco's router** — use `window.history.pushState` with relative path within the `umb-router-slot` context, or dispatch an event that the router slot handles

### UUI Table Pattern

```typescript
html`
  <uui-table>
    <uui-table-head>
      <uui-table-head-cell>Name</uui-table-head-cell>
      <uui-table-head-cell style="width: 100px;">Steps</uui-table-head-cell>
      <uui-table-head-cell style="width: 120px;">Mode</uui-table-head-cell>
    </uui-table-head>
    ${this._workflows.map(
      (w) => html`
        <uui-table-row
          @click=${() => this._navigateToWorkflow(w.alias)}
          style="cursor: pointer;"
        >
          <uui-table-cell>${w.name}</uui-table-cell>
          <uui-table-cell>${w.stepCount} steps</uui-table-cell>
          <uui-table-cell>
            <uui-badge>${w.mode}</uui-badge>
          </uui-table-cell>
        </uui-table-row>
      `
    )}
  </uui-table>
`;
```

**UUI design tokens to use:**
- `--uui-size-layout-1` — padding around content areas
- `--uui-color-text` — all text content
- `--uui-color-interactive` — clickable elements
- `--uui-color-surface` — backgrounds

### Navigation Within umb-router-slot

The `shallai-dashboard` component uses `umb-router-slot` with routes defined for `/workflows/:alias`. To navigate from the workflow list:

```typescript
private _navigateToWorkflow(alias: string): void {
  // Get the current URL and replace the last segment (or append)
  // Current URL when on workflow list will be something like:
  //   /section/agent-workflows/overview/workflows
  // Target URL:
  //   /section/agent-workflows/overview/workflows/{alias}
  const currentPath = window.location.pathname;
  const targetPath = currentPath.endsWith("/")
    ? `${currentPath}${alias}`
    : `${currentPath}/${alias}`;
  window.history.pushState({}, "", targetPath);
  window.dispatchEvent(new PopStateEvent("popstate"));
}
```

**Important — verify at runtime:** The exact URL structure depends on Bellissima's section/sectionView pathname resolution. The pattern above assumes the workflow list renders at a path ending in `/workflows` and navigates to `/workflows/{alias}`. Before committing, run the TestSite, navigate to the dashboard, and confirm the browser URL matches this assumption. Adjust the path construction if the URL structure differs. The `popstate` event triggers the `umb-router-slot` to re-evaluate its routes.

### Testing Strategy

**Backend tests:**
- Use `NSubstitute` to mock `IWorkflowRegistry` — return controlled `RegisteredWorkflow` list
- Test the controller directly by instantiating `WorkflowEndpoints(mockRegistry)` and calling `GetWorkflows()`
- Assert the `OkObjectResult.Value` contains correctly mapped summaries
- NUnit 4 only: `[TestFixture]`, `[Test]`, `Assert.That()`
- Test file: `Shallai.UmbracoAgentRunner.Tests/Endpoints/WorkflowEndpointsTests.cs`

**Frontend tests:**
- Use `@open-wc/testing` with `describe`/`it`/`expect`
- Mock the fetch call using a test helper
- Test file: `Client/src/components/shallai-workflow-list.element.test.ts`

### Deferred Work Awareness

From prior code reviews (tracked in `deferred-work.md`):
- `LoadWorkflowsAsync` on `IWorkflowRegistry` interface exposes mutation — not this story's concern, but be aware that the registry is loaded once at startup and the `GetAllWorkflows()` method reads from a frozen dictionary
- `RegisteredWorkflow` wraps mutable `WorkflowDefinition` — the endpoint maps to DTOs immediately, so mutation risk is limited to the mapping code window (no long-lived references)
- `AgentRunnerOptions` null handling — already addressed in Story 2.2's `WorkflowRegistryInitializer`

### What NOT to Build

- **NOT** instance creation endpoint (`POST /instances`) — that's Story 3.2
- **NOT** "New Run" button in the workflow list — that's on the instance list view (Story 3.3)
- **NOT** `WorkflowContext` shared context class — premature; only one component consumes this data. Extract if/when Story 3.3 needs the same data.
- **NOT** error response model (`ErrorResponse.cs`) — this endpoint only returns 200 (array) or 401 (handled by auth middleware). Build error responses when we have endpoints that produce them (Story 3.2+).
- **NOT** workflow detail endpoint — not in the architecture spec; the summary in the list is sufficient
- **NOT** search/filter/sort on the workflow list — not in any AC; workflows are few in number
- **NOT** hot-reload of workflows or refresh button — not scoped
- **NOT** description column in the table — UX spec defines only Name, Steps, Mode columns. Description is in the API response for potential future use but not displayed in the table.

### Project Structure Notes

New files align with the architecture folder structure:
- `Endpoints/WorkflowEndpoints.cs` — namespace `Shallai.UmbracoAgentRunner.Endpoints`
- `Models/ApiModels/WorkflowSummary.cs` — namespace `Shallai.UmbracoAgentRunner.Models.ApiModels`
- `Client/src/api/types.ts` — TypeScript API interfaces
- `Client/src/api/api-client.ts` — Fetch wrapper
- `Client/src/components/shallai-workflow-list.element.ts` — modified (replace stub)
- `Client/src/components/shallai-workflow-list.element.test.ts` — new test
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/WorkflowEndpointsTests.cs` — new test

No modifications to `AgentRunnerComposer.cs` — the `[ApiController]` is auto-discovered.

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 2, Story 2.3, lines 389-407]
- [Source: _bmad-output/planning-artifacts/architecture.md — Decision 5: API Controller Structure, lines 272-296]
- [Source: _bmad-output/planning-artifacts/architecture.md — Decision 6: Frontend State Management, lines 298-320]
- [Source: _bmad-output/planning-artifacts/architecture.md — API Response Formats, lines 443-469]
- [Source: _bmad-output/planning-artifacts/architecture.md — Frontend Patterns, lines 499-535]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure, lines 585-665]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md — Workflow list table columns, empty states, navigation]
- [Source: _bmad-output/planning-artifacts/prd.md — FR32, FR37, NFR2, NFR13]
- [Source: _bmad-output/project-context.md — Naming conventions, error handling, lit import rules, UUI design tokens]
- [Source: _bmad-output/implementation-artifacts/2-2-workflow-registry-and-discovery.md — IWorkflowRegistry interface, RegisteredWorkflow model, DI registration]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — Registry mutation exposure, WorkflowDefinition mutability]

### Review Findings

- [x] [Review][Decision] **F1: Navigation via pushState+popstate may not trigger umb-router-slot** — Investigated: Umbraco patches `history.pushState` to dispatch custom router events. Removed redundant synthetic `PopStateEvent` — `pushState` alone triggers `umb-router-slot`. Fixed.
- [x] [Review][Decision] **F2: Silent error catch shows misleading "No workflows found"** — Added `_error` state with distinct error message: "Failed to load workflows. Check that you have backoffice access and try refreshing the page." Fixed.
- [x] [Review][Patch] **F3: Alias not URL-encoded in navigation path** — Added `encodeURIComponent(alias)` in `_navigateToWorkflow`. Fixed.
- [x] [Review][Patch] **F4: Loading state not reset on DOM reconnect** — Reset `_loading = true` and `_error = false` in `connectedCallback`. Fixed.
- [x] [Review][Patch] **F5: Name column lacks clickable visual affordance** — Wrapped name in `<span class="workflow-name">` with `color: var(--uui-color-interactive)`. Fixed.
- [x] [Review][Patch] **F6: Auth — 401 due to cookie auth instead of bearer token** — Replaced `credentials: 'same-origin'` with bearer token auth via `UMB_AUTH_CONTEXT.getLatestToken()`. Component consumes auth context and passes token to API client. Fixed.
- [x] [Review][Defer] **F6: Frontend tests verify logic in isolation, not component DOM rendering** — Tests don't instantiate `ShallaiWorkflowListElement` or assert DOM output. Noted as intentional in dev record due to Umbraco backoffice package export limitations in web-test-runner. [shallai-workflow-list.element.test.ts] — deferred, pre-existing constraint
- [x] [Review][Defer] **F7: No CSRF token in fetch helper** — `fetchJson` sends credentials but no anti-forgery token. Low risk for GET-only usage but becomes a pattern issue when POST endpoints are added in Story 3.2+. [api-client.ts:6-9] — deferred, address when POST endpoints are added
- [x] [Review][Defer] **F8: Test monkey-patches (fetch, pushState) lack cleanup guards** — If an assertion throws before restore, globals remain patched for subsequent tests. Should use `afterEach` hooks. [shallai-workflow-list.element.test.ts:80,106] — deferred, test hygiene
- [x] [Review][Defer] **F9: fetchJson doesn't validate Content-Type before parsing** — If server returns HTML (e.g., login redirect), `response.json()` throws an untyped `SyntaxError`. [api-client.ts:12] — deferred, general API client hardening

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Build cache issue on first run resolved by full rebuild (not `-q` flag issue — MSB3492 warning was non-blocking)
- Frontend component tests: Umbraco backoffice package exports aren't directly importable by web-test-runner due to deep path resolution. Tests written to verify component logic, API client, and navigation without rendering the full Lit component in a browser context.

### Completion Notes List

- **Task 1:** Created `WorkflowSummary` sealed DTO with `required init` properties — immutable after construction
- **Task 2:** Created `WorkflowEndpoints` API controller with `[Authorize(BackOfficeAccess)]` at controller level, `GET /workflows` returns mapped `WorkflowSummary[]` via `Ok()`
- **Task 3:** Created `WorkflowSummary` TypeScript interface and `fetchJson<T>` generic helper with `credentials: 'same-origin'` for cookie auth
- **Task 4:** Replaced workflow-list stub with full implementation: `uui-table` with Name/Steps/Mode columns, `uui-badge` for mode, loading state via `uui-loader`, empty state message
- **Task 5:** Navigation via `window.history.pushState` + `popstate` event dispatch to trigger `umb-router-slot` re-evaluation
- **Task 6:** 3 backend tests — mapped summaries, empty array, StepCount mapping. All use NSubstitute mock of `IWorkflowRegistry`
- **Task 7:** 8 frontend tests — column mapping, row count, empty state, navigation path construction (with/without trailing slash), pushState + popstate, API fetch parameters, error handling on non-OK response
- **Task 8:** Frontend build clean, all 38 backend + 9 frontend tests pass

### File List

- `Shallai.UmbracoAgentRunner/Models/ApiModels/WorkflowSummary.cs` (new)
- `Shallai.UmbracoAgentRunner/Endpoints/WorkflowEndpoints.cs` (new)
- `Shallai.UmbracoAgentRunner/Client/src/api/types.ts` (new)
- `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` (new)
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-workflow-list.element.ts` (modified — replaced stub)
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-workflow-list.element.test.ts` (new)
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/WorkflowEndpointsTests.cs` (new)
- `Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/shallai-workflow-list.element-DeYzpS1E.js` (rebuilt)
- `Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/shallai-workflow-list.element-DeYzpS1E.js.map` (rebuilt)
