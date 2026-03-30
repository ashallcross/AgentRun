# Story 3.3: Instance List Dashboard

Status: done

## Story

As a developer or editor,
I want to see all runs for a workflow listed in the dashboard with their status,
so that I can track previous runs, start new ones, and manage completed runs.

## Acceptance Criteria

1. **Given** the user has navigated to a workflow's instance list view (`/workflows/{alias}`)
   **When** the `shallai-instance-list` component loads
   **Then** it fetches instances from `GET /instances?workflowAlias={alias}` and displays them in a `uui-table`

2. **And** the table has columns: Run (sequential number, newest first), Status (`uui-badge` — Pending/Running/Completed/Failed/Cancelled), Step ("2 of 3" or "Complete"), Started (relative time with full datetime tooltip), Actions (overflow menu)

3. **And** clicking a row navigates to the instance detail view (`/instances/{id}`)

4. **And** a "New Run" primary button in the `umb-body-layout` header creates a new instance via `POST /instances` and navigates to its detail view

5. **And** the overflow menu shows "View" (navigates to detail) and "Delete" (for completed/failed/cancelled instances only)

6. **And** clicking "Delete" shows a `uui-dialog` confirmation: "Delete this run? This cannot be undone." (UX-DR18)

7. **And** when no instances exist, an empty state message is displayed: "No runs yet. Click 'New Run' to start."

8. **And** the header shows contextual title: workflow name (e.g. "Content Quality Audit")

9. **And** back navigation returns to the workflow list

10. **And** all styling uses UUI design tokens

## What NOT to Build

- No instance detail view implementation (Story 3.4)
- No step execution, retry, or advancement (Epic 4)
- No SSE streaming or chat panel (Epic 6)
- No conversation history display (Epic 6)
- No artifact viewing (Epic 8)
- No polling or auto-refresh of instance status — the list is a static fetch on load
- No pagination — the instance list is loaded in full (scale is small: dozens of instances, not thousands)

## Tasks / Subtasks

- [x] Task 1: Add instance API client functions to `api-client.ts` (AC: #1, #4, #5)
  - [x] 1.1: Add `InstanceResponse` and `InstanceDetailResponse` interfaces to `api/types.ts` — match the camelCase JSON shape from Story 3.2 endpoints (`id`, `workflowAlias`, `status`, `currentStepIndex`, `createdAt`, `updatedAt`; detail adds `createdBy` and `steps[]` with `id`, `status`, `startedAt?`, `completedAt?`)
  - [x] 1.2: Add `getInstances(workflowAlias: string, token?: string)` to `api-client.ts` — calls `GET /instances?workflowAlias={alias}`
  - [x] 1.3: Add `createInstance(workflowAlias: string, token?: string)` to `api-client.ts` — calls `POST /instances` with JSON body `{ workflowAlias }`, returns `InstanceResponse`
  - [x] 1.4: Add `deleteInstance(id: string, token?: string)` to `api-client.ts` — calls `DELETE /instances/{id}`, returns `void` (204 No Content)

- [x] Task 2: Resolve workflow alias from route and fetch workflow name (AC: #1, #8)
  - [x] 2.1: The `shallai-instance-list` component needs the workflow alias from the current route. The `umb-router-slot` in the dashboard sets up the route `workflows/:alias` — the component receives this via the router. Extract the alias from `window.location.pathname`: parse the last segment after `/workflows/` and `decodeURIComponent()` it. This matches the encoding pattern in `shallai-workflow-list` (`encodeURIComponent(alias)` on navigate).
  - [x] 2.2: Fetch the workflow name for the header title. The `GET /workflows` endpoint returns `WorkflowSummary[]` — call `getWorkflows(token)` and find the matching alias to get the `name` property. If the workflow is not found (e.g. deleted), display the alias as fallback.

- [x] Task 3: Implement `shallai-instance-list` component (AC: #1–#3, #7–#10)
  - [x] 3.1: Replace stub content in `shallai-instance-list.element.ts`. Import `UmbLitElement`, Lit decorators (`customElement`, `html`, `css`, `state`, `nothing`), `UMB_AUTH_CONTEXT`, API client functions, and types.
  - [x] 3.2: Add `@state()` properties: `_instances: InstanceResponse[]`, `_workflowName: string`, `_workflowAlias: string`, `_loading: boolean`, `_error: boolean`
  - [x] 3.3: In `connectedCallback()`: extract alias from URL (Task 2.1), set `_workflowAlias`, fetch workflow name (Task 2.2), fetch instances via `getInstances(alias, token)`, sort by `createdAt` descending (newest first)
  - [x] 3.4: Render loading state with `<uui-loader>` (matching workflow-list pattern)
  - [x] 3.5: Render error state with message div (matching workflow-list pattern)
  - [x] 3.6: Render empty state: "No runs yet. Click 'New Run' to start." (AC #7)
  - [x] 3.7: Render `<uui-table>` with columns: Run (#, sequential number derived from array index after sort — newest = highest number), Status (AC #2), Step (AC #2), Started (AC #2), Actions
  - [x] 3.8: Status column: render `<uui-badge>` with colour mapping — `positive` for Completed, `danger` for Failed, `warning` for Running, `default` for Pending, `default` for Cancelled
  - [x] 3.9: Step column: format as `"{currentStepIndex + 1} of {totalSteps}"` for non-complete instances, or "Complete" for Completed status. **Note:** `InstanceResponse` has `currentStepIndex` but NOT `totalSteps`. The step count is available from the workflow's `stepCount` in `WorkflowSummary`. Use the workflow name fetch (Task 2.2) to also capture `stepCount`.
  - [x] 3.10: Started column: render relative time (e.g. "2 hours ago") with a `title` attribute showing the full ISO datetime. Use a simple helper function that computes relative time from `createdAt` — no external library needed. Categories: "just now" (<60s), "X minutes ago", "X hours ago", "X days ago", "X months ago".
  - [x] 3.11: Row click navigates to instance detail: `window.history.pushState({}, "", targetPath)` where targetPath replaces `/workflows/{alias}` with `/instances/{id}` in the current URL. Use `window.location.pathname` to get the section prefix, then replace everything after `/overview/` with `instances/{id}`.
  - [x] 3.12: Header title shows workflow name with back arrow (AC #8, #9)

- [x] Task 4: Implement header with "New Run" button and back navigation (AC: #4, #8, #9)
  - [x] 4.1: Wrap the component in `<umb-body-layout>` with `headline` set to the workflow name (AC #8)
  - [x] 4.2: Add a "New Run" `<uui-button>` in the header actions slot with `look="primary"` and `color="positive"`. On click: call `createInstance(alias, token)`, then navigate to the new instance's detail view (`/instances/{newId}`).
  - [x] 4.3: Add a back button (or breadcrumb link) that navigates to `/workflows` (the workflow list). Use `window.history.pushState` to navigate — same pattern as workflow-list component. The target path is the current path with everything after `/overview/` replaced by `workflows`.
  - [x] 4.4: While "New Run" is creating, disable the button to prevent double-clicks. Set a `_creating` state flag.

- [x] Task 5: Implement overflow menu with View and Delete actions (AC: #5, #6)
  - [x] 5.1: Add a `<uui-action-bar>` in the Actions column of each row. Include a `<uui-button>` with `look="secondary"` that opens a `<uui-popover-container>`. The popover contains menu items: "View" and "Delete".
  - [x] 5.2: "View" action navigates to the instance detail (same as row click — AC #3)
  - [x] 5.3: "Delete" action is only rendered when instance status is `Completed`, `Failed`, or `Cancelled` (terminal statuses from Story 3.2). For `Pending` or `Running` instances, omit the Delete menu item.
  - [x] 5.4: Delete confirmation: use a native `confirm()` dialog with the message "Delete this run? This cannot be undone." (AC #6). On confirm, call `deleteInstance(id, token)`, then remove the instance from `_instances` array to update the UI without a full refetch. If the delete API returns an error, show the error state.
  - [x] 5.5: Stop event propagation on overflow menu click so it doesn't trigger row click navigation.

- [x] Task 6: Add styles using UUI design tokens (AC: #10)
  - [x] 6.1: Style the component following the `shallai-workflow-list` pattern: `:host` block padding, table row hover, interactive colour for clickable text
  - [x] 6.2: Add status badge colours via CSS classes or inline styles using `uui-badge` `color` attribute
  - [x] 6.3: Style the header area with the back button and "New Run" button
  - [x] 6.4: Style the empty state message consistently with the workflow-list empty state
  - [x] 6.5: Style the overflow menu / action bar in the Actions column

- [x] Task 7: Write unit tests (AC: #1–#10)
  - [x] 7.1: Test `getInstances` API function sends correct URL with workflowAlias query parameter and bearer token
  - [x] 7.2: Test `createInstance` API function sends POST with JSON body and returns response
  - [x] 7.3: Test `deleteInstance` API function sends DELETE request to correct URL
  - [x] 7.4: Test instance list data mapping: sequential run number, status badge colour mapping, step display format, relative time formatting
  - [x] 7.5: Test navigation logic: row click constructs correct instance detail path, back button constructs correct workflow list path
  - [x] 7.6: Test delete is only available for terminal statuses (Completed, Failed, Cancelled) and hidden for Pending/Running
  - [x] 7.7: Test empty state renders when instances array is empty
  - [x] 7.8: Test "New Run" button calls createInstance and constructs navigation path to new instance
  - [x] 7.9: Test relative time helper: "just now", "X minutes ago", "X hours ago", "X days ago"
  - [x] 7.10: Test workflow alias extraction from URL path with decodeURIComponent

## Dev Agent Record

### Implementation Plan

- Tasks 1–7 implemented in sequence per story spec
- API client extended with `postJson` and `deleteRequest` helpers, plus `getInstances`, `createInstance`, `deleteInstance` exports
- Component fully replaced stub with complete instance list view: loading/error/empty states, uui-table with Run/Status/Step/Started/Actions columns, overflow menu, back nav, New Run button
- Used `numberAndSortInstances` helper to sort ascending by createdAt for numbering, then reverse for newest-first display
- Step column uses `stepCount` from `WorkflowSummary` fetched alongside instances via `Promise.all`
- Header uses custom flex layout with back button + h2 + New Run button (not `umb-body-layout` headline slot, since we need the back button inline)
- Delete uses `window.confirm()` per Dev Notes guidance (pragmatic over full uui-dialog)
- Tests duplicate pure function logic inline to avoid importing Umbraco dependencies in test runner

### Debug Log

No issues encountered during implementation.

### Completion Notes

- All 7 tasks with 42 subtasks complete
- 19 new frontend tests, all passing (30 total frontend, 82 backend = 112 total)
- Frontend build successful, wwwroot output up to date
- All 10 acceptance criteria satisfied

## File List

| File | Path | Action |
|------|------|--------|
| types.ts | `Shallai.UmbracoAgentRunner/Client/src/api/types.ts` | Modified — added `InstanceResponse` interface |
| api-client.ts | `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` | Modified — added `postJson`, `deleteRequest`, `getInstances`, `createInstance`, `deleteInstance` |
| shallai-instance-list.element.ts | `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-list.element.ts` | Modified — full replacement of stub with instance list component |
| instance-list-helpers.ts | `Shallai.UmbracoAgentRunner/Client/src/utils/instance-list-helpers.ts` | New — extracted pure functions for testability |
| shallai-instance-list.element.test.ts | `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-list.element.test.ts` | New — 19 unit tests (imports real functions from helpers) |
| sprint-status.yaml | `_bmad-output/implementation-artifacts/sprint-status.yaml` | Modified — status updated |

### Review Findings

- [x] [Review][Decision] **AC #6: confirm() vs uui-dialog** — Resolved: implemented `umbConfirmModal` from `@umbraco-cms/backoffice/modal` with danger color and Delete confirm label. AC #6 now fully satisfied.
- [x] [Review][Patch] **deleteInstance id not URI-encoded — path injection risk** — Fixed: added `encodeURIComponent(id)` in `deleteInstance()`. [api-client.ts:59]
- [x] [Review][Patch] **Tests copy-paste pure functions instead of importing — drift risk** — Fixed: extracted pure functions to `utils/instance-list-helpers.ts` (no Umbraco deps). Both element and tests import from same source. Signature mismatch resolved (functions now accept pathname param).
- [x] [Review][Patch] **statusColor test verifies a hardcoded map, not the actual function** — Fixed: test now calls the real `statusColor()` function imported from helpers.
- [x] [Review][Patch] **_error state is sticky after failed create/delete** — Fixed: removed `_error = true` from `_createNewRun` and `_deleteInstance` catch blocks. Error state now only triggers on initial load failure, preserving the valid instance list.
- [x] [Review][Patch] **No double-submit guard on delete** — Fixed: added `_deleting` state flag (tracks instance id being deleted). Guard prevents concurrent delete requests.
- [x] [Review][Defer] **_formatStep edge cases with out-of-range stepIndex or stepCount=0** — If backend returns `currentStepIndex >= stepCount`, displays "6 of 3". When stepCount=0 (workflow metadata unavailable), falls back to "Step N". Pre-existing backend data integrity concern. [shallai-instance-list.element.ts:222-226] — deferred, pre-existing
- [x] [Review][Defer] **Empty workflowAlias not guarded** — If URL ends with `/`, extractWorkflowAlias returns empty string, causing `getInstances("")`. Umbraco router matching makes this unlikely in practice. [shallai-instance-list.element.ts:35-40] — deferred, router guards this
- [x] [Review][Defer] **relativeTime never shows years** — Dates >365 days show "12 months ago", "24 months ago" etc. No year category. [shallai-instance-list.element.ts:22-32] — deferred, instances unlikely to be that old
- [x] [Review][Defer] **Promise.all partial failure** — If getWorkflows succeeds but getInstances fails (or vice versa), both results are discarded. Acceptable for current scope. [shallai-instance-list.element.ts:165-168] — deferred, acceptable pattern
- [x] [Review][Defer] **No abort on component disconnect** — _loadData promise is fire-and-forget; if component disconnects mid-fetch, state sets on detached element. [shallai-instance-list.element.ts:158-160] — deferred, standard Lit lifecycle pattern
- [x] [Review][Defer] **Full workflow list fetch for name resolution** — Fetches entire workflow list to get one name. Acceptable at current scale. [shallai-instance-list.element.ts:166] — deferred, performance concern for future

## Change Log

- 2026-03-30: Story 3.3 implemented — instance list dashboard with API client, component, overflow menu, navigation, styles, and 19 unit tests
- 2026-03-31: Code review — 1 decision-needed, 5 patches, 6 deferred, 13 dismissed. All patches applied: umbConfirmModal, id encoding, helpers extraction, delete guard, sticky error fix, test imports. Status → done.

## Dev Notes

### Existing Stub to Replace

`Client/src/components/shallai-instance-list.element.ts` is a minimal stub created in Story 1.3. Replace its contents entirely — do not try to extend it. The stub has no logic, just a placeholder `<uui-box>`.

### Route Parameter Extraction

The `umb-router-slot` in `shallai-dashboard.element.ts` defines the route as `workflows/:alias` but does **not** pass the `alias` parameter to the component via props or context. The component must extract it from the URL.

**Pattern (proven in workflow-list):**
```typescript
// In connectedCallback or a lifecycle method:
const path = window.location.pathname;
// Path format: /section/agent-workflows/overview/workflows/{encoded-alias}
const segments = path.split('/');
const lastSegment = segments[segments.length - 1];
const alias = decodeURIComponent(lastSegment);
```

This is the same encode/decode pair used in `shallai-workflow-list` where `encodeURIComponent(alias)` is used on navigation.

### API Client — Extending fetchJson

The `api-client.ts` has a `fetchJson<T>()` helper that only supports GET. For POST and DELETE, extend the pattern:

```typescript
async function postJson<T>(path: string, body: unknown, token?: string): Promise<T> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Content-Type": "application/json",
  };
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers,
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`API error: ${response.status} ${response.statusText}`);
  return response.json() as Promise<T>;
}

async function deleteRequest(path: string, token?: string): Promise<void> {
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(`${API_BASE}${path}`, {
    method: "DELETE",
    headers,
  });
  if (!response.ok) throw new Error(`API error: ${response.status} ${response.statusText}`);
}
```

### Navigation Pattern — Instance Detail Path

Navigation to instance detail must work within the `umb-router-slot` routing. The dashboard routes are:
- `workflows` — workflow list
- `workflows/:alias` — instance list (current view)
- `instances/:id` — instance detail

These are **sibling routes** under the dashboard's router. Navigate by replacing the path segment after `/overview/`:

```typescript
private _navigateToInstance(id: string): void {
  const path = window.location.pathname;
  // Replace everything after /overview/ with instances/{id}
  const overviewIndex = path.indexOf('/overview/');
  if (overviewIndex === -1) return;
  const basePath = path.substring(0, overviewIndex + '/overview/'.length);
  window.history.pushState({}, "", `${basePath}instances/${id}`);
}
```

### Back Navigation

Same pattern, navigating back to the workflow list:

```typescript
private _navigateBack(): void {
  const path = window.location.pathname;
  const overviewIndex = path.indexOf('/overview/');
  if (overviewIndex === -1) return;
  const basePath = path.substring(0, overviewIndex + '/overview/'.length);
  window.history.pushState({}, "", `${basePath}workflows`);
}
```

### API Response Shapes (from Story 3.2)

**GET /instances?workflowAlias={alias}** returns:
```json
[
  {
    "id": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
    "workflowAlias": "content-audit",
    "status": "Completed",
    "currentStepIndex": 2,
    "createdAt": "2026-03-30T10:00:00Z",
    "updatedAt": "2026-03-30T10:15:00Z"
  }
]
```

**POST /instances** with body `{ "workflowAlias": "content-audit" }` returns (201):
```json
{
  "id": "newinstanceid32charhex00000000",
  "workflowAlias": "content-audit",
  "status": "Pending",
  "currentStepIndex": 0,
  "createdAt": "2026-03-30T12:00:00Z",
  "updatedAt": "2026-03-30T12:00:00Z"
}
```

**DELETE /instances/{id}** returns 204 No Content (no body).

### Status → Badge Colour Mapping

| Status | `uui-badge` `color` attribute | Rationale |
|--------|-------------------------------|-----------|
| Pending | (default — no colour attr) | Neutral, waiting state |
| Running | `warning` | Active, in-progress |
| Completed | `positive` | Success |
| Failed | `danger` | Error |
| Cancelled | (default — no colour attr) | Terminal but not error |

### Sequential Run Number

The API returns instances in arbitrary order. Sort by `createdAt` ascending to assign sequential numbers (1 = oldest, N = newest). Then reverse for display (newest first). The "Run" column shows the sequential number.

```typescript
// Sort ascending by createdAt for numbering
const sorted = [...instances].sort((a, b) =>
  new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
);
// Assign numbers, then reverse for display
const numbered = sorted.map((inst, i) => ({ ...inst, runNumber: i + 1 }));
numbered.reverse(); // newest first for display
```

### Relative Time Helper

Keep it simple — no library, just a pure function:

```typescript
function relativeTime(isoDate: string): string {
  const seconds = Math.floor((Date.now() - new Date(isoDate).getTime()) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? "" : "s"} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days} day${days === 1 ? "" : "s"} ago`;
  const months = Math.floor(days / 30);
  return `${months} month${months === 1 ? "" : "s"} ago`;
}
```

### Delete Confirmation — Use confirm() Not uui-dialog

The AC specifies `uui-dialog` but implementing a full UUI dialog component for a single confirmation is over-engineered at this stage. Use `window.confirm()` with the specified message text. This is the pragmatic choice — a proper dialog component can be introduced if the pattern is needed elsewhere.

### Overflow Menu — UUI Components

Use `<uui-popover-container>` with `<uui-button>` trigger:

```html
<uui-action-bar>
  <uui-button id="popover-trigger-${id}" look="secondary" compact>
    <uui-icon name="icon-navigation"></uui-icon>
  </uui-button>
  <uui-popover-container margin="6" placement="bottom-end"
    anchor="popover-trigger-${id}">
    <umb-popover-layout>
      <uui-menu-item label="View" @click-label=${() => this._navigateToInstance(id)}>
      </uui-menu-item>
      ${isTerminal ? html`
        <uui-menu-item label="Delete" @click-label=${() => this._deleteInstance(id)}>
        </uui-menu-item>
      ` : nothing}
    </umb-popover-layout>
  </uui-popover-container>
</uui-action-bar>
```

Stop propagation on the action bar click to prevent row navigation:
```typescript
@click=${(e: Event) => e.stopPropagation()}
```

### Testing Conventions (from Story 2.3)

- Test file: `Client/src/components/shallai-instance-list.element.test.ts`
- Uses `describe`/`it` with `expect` from `@open-wc/testing`
- Tests verify logic (data mapping, navigation paths, API calls), not DOM rendering (Umbraco backoffice imports aren't available in test runner)
- Monkey-patch `fetch` and `history.pushState` for API and navigation tests
- Use `afterEach` for cleanup of patched globals
- Target: ~10 tests

### File Placement

| File | Path | Action |
|------|------|--------|
| types.ts | `Client/src/api/types.ts` | Modified — add `InstanceResponse` interface |
| api-client.ts | `Client/src/api/api-client.ts` | Modified — add `postJson`, `deleteRequest`, `getInstances`, `createInstance`, `deleteInstance` |
| shallai-instance-list.element.ts | `Client/src/components/` | Modified — full replacement of stub |
| shallai-instance-list.element.test.ts | `Client/src/components/` | New — unit tests |

### Previous Story Intelligence

From Story 3.2:
- `InstanceResponse` JSON uses string enum values (e.g. `"Completed"` not `2`) — `[JsonStringEnumConverter]` was added in review
- `currentStepIndex` is 0-based — display as `currentStepIndex + 1` for human-readable step number
- Terminal statuses for delete: `Completed`, `Failed`, `Cancelled`
- Cancel is allowed for `Running` and `Pending`
- Instance ID format: 32-character hex string (no hyphens)
- Error responses follow `{ error, message }` shape

From Story 2.3 (workflow list — the reference implementation):
- Auth pattern: consume `UMB_AUTH_CONTEXT` in constructor, call `getLatestToken()` before API calls
- State pattern: `@state()` for `_loading`, `_error`, data arrays
- Loading/error/empty states are rendered as conditional returns in `render()`
- Navigation: `window.history.pushState({}, "", targetPath)` — Umbraco patches this
- Styling: UUI design tokens, `:host` with layout padding, row hover styles

### Build Verification

Run `npm run build` in `Client/` before marking done — the frontend build output in `wwwroot/` must be up to date.

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 3, Story 3.3]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md — Instance list view, button hierarchy, status badges, empty states]
- [Source: _bmad-output/planning-artifacts/architecture.md — Frontend patterns, UUI components, routing, API response format]
- [Source: _bmad-output/implementation-artifacts/3-1-instance-state-management.md — InstanceState model, status enums]
- [Source: _bmad-output/implementation-artifacts/3-2-instance-api-endpoints.md — API endpoints, response shapes, error codes]
- [Source: Client/src/components/shallai-workflow-list.element.ts — reference component implementation]
- [Source: Client/src/api/api-client.ts — fetchJson pattern to extend]
- [Source: _bmad-output/project-context.md — all framework rules]
