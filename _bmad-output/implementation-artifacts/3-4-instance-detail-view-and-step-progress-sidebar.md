# Story 3.4: Instance Detail View & Step Progress Sidebar

Status: done

## Story

As a developer or editor,
I want to see the full detail of a workflow run with a step progress sidebar,
so that I can see which step is active, which are complete, and what comes next.

## Acceptance Criteria

1. **Given** the user has navigated to an instance detail view (`/instances/{id}`)
   **When** the `shallai-instance-detail` component loads
   **Then** it fetches instance detail from `GET /instances/{id}` and renders a CSS Grid layout with 240px left sidebar and 1fr main content area

2. **And** the `shallai-step-progress` sidebar displays a vertical list of all steps with status icons: pending (`circle-outline`, `--uui-color-disabled`), active (`sync` animated, `--uui-color-current`), complete (`check`, `--uui-color-positive`), error (`close`, `--uui-color-danger`)

3. **And** each step shows its human-readable name from workflow.yaml and a subtitle (Pending/Running.../artifact filename/Failed)

4. **And** clicking a completed step fires a `step-selected` custom event (for future artifact viewing)

5. **And** clicking the active step fires a `step-selected` event (for future chat panel display)

6. **And** clicking a pending step has no action

7. **And** the selected step has a `--uui-color-current` left border highlight

8. **And** the `umb-body-layout` header shows contextual title: "{workflow name} — Run #{number}"

9. **And** the header shows the appropriate primary action button: "Start" when status is pending (button hidden when no action is available)

10. **And** a secondary "Cancel" button appears in the overflow menu when the instance is running, requiring `uui-dialog` confirmation

11. **And** the step progress sidebar is accessible: `role="list"`, `role="listitem"` per step, `aria-current="step"` on active, status in `aria-label` (UX-DR22)

12. **And** the main content area shows a placeholder message: "Click 'Start' to begin the workflow."

13. **And** back navigation returns to the instance list

## What NOT to Build

- No step execution or "Start" button handler logic — Epic 4 implements the execution engine. The "Start" button renders but its click handler is a no-op stub that logs "Start not yet implemented" to console.
- No SSE streaming or chat panel — Epic 6
- No artifact viewer or artifact fetching — Epic 8
- No conversation history display — Epic 6
- No retry button or retry logic — Epic 7
- No "Continue" button for advancing steps — Epic 4
- No polling or auto-refresh of instance status — the view is a static fetch on load
- No `shallai-step-progress` as a separate file — implement it inline within `shallai-instance-detail.element.ts` as a private render method. Extract to a separate component only if a future story needs to reuse it.

## Tasks / Subtasks

- [x] Task 1: Enrich backend `InstanceDetailResponse` with step names and workflow name (AC: #3, #8)
  - [x] 1.1: Add `name` property to `StepResponse` in `Models/ApiModels/InstanceDetailResponse.cs` — `public required string Name { get; init; }`
  - [x] 1.2: Add `writesTo` property to `StepResponse` — `public string[]? WritesTo { get; init; }` (for artifact filename subtitles)
  - [x] 1.3: Add `workflowName` property to `InstanceDetailResponse` — `public required string WorkflowName { get; init; }`
  - [x] 1.4: Update `MapToDetailResponse` in `InstanceEndpoints.cs` to look up the workflow definition from `_workflowRegistry.GetWorkflow(state.WorkflowAlias)` and populate `Name` from `StepDefinition.Name`, `WritesTo` from `StepDefinition.WritesTo`, and `WorkflowName` from `WorkflowDefinition.Name`
  - [x] 1.5: Handle missing workflow gracefully — if workflow was deleted after instance creation, use step ID as fallback name and empty string for workflow name
  - [x] 1.6: Add backend unit tests: detail response includes step names, detail response includes writesTo, detail response includes workflow name, fallback when workflow is missing

- [x] Task 2: Add frontend API client and types (AC: #1)
  - [x] 2.1: Add `InstanceDetailResponse` and `StepDetailResponse` interfaces to `api/types.ts`:
    ```typescript
    export interface StepDetailResponse {
      id: string;
      name: string;
      status: string;
      startedAt: string | null;
      completedAt: string | null;
      writesTo: string[] | null;
    }
    export interface InstanceDetailResponse {
      id: string;
      workflowAlias: string;
      workflowName: string;
      status: string;
      currentStepIndex: number;
      createdAt: string;
      updatedAt: string;
      createdBy: string;
      steps: StepDetailResponse[];
    }
    ```
  - [x] 2.2: Add `getInstance(id: string, token?: string)` to `api-client.ts` — calls `GET /instances/${encodeURIComponent(id)}`, returns `InstanceDetailResponse`

- [x] Task 3: Add instance detail helper functions (AC: #8, #13)
  - [x] 3.1: Create `utils/instance-detail-helpers.ts` with pure functions:
    - `extractInstanceId(pathname: string): string` — extract last URL segment and decode (same pattern as `extractWorkflowAlias`)
    - `buildInstanceListPath(pathname: string, workflowAlias: string): string` — replace `instances/{id}` with `workflows/{encodedAlias}` in the current path
    - `stepSubtitle(step: StepDetailResponse): string` — returns subtitle based on status: `"Pending"` for Pending, `"Running..."` for Active, first `writesTo` filename for Complete (fallback: `"Complete"`), `"Failed"` for Error
    - `stepIconName(status: string): string` — maps status to UUI icon name: Pending → `"icon-circle-dotted-line"`, Active → `"icon-sync"`, Complete → `"icon-check"`, Error → `"icon-close"`
    - `stepIconColor(status: string): string | undefined` — maps status to UUI colour token value: Pending → `undefined` (uses `--uui-color-disabled` via CSS), Active → `"current"`, Complete → `"positive"`, Error → `"danger"`

- [x] Task 4: Implement `shallai-instance-detail` component (AC: #1–#3, #7, #8, #12, #13)
  - [x] 4.1: Replace stub content in `shallai-instance-detail.element.ts`. Import `UmbLitElement`, Lit decorators (`customElement`, `html`, `css`, `state`, `nothing`), `UMB_AUTH_CONTEXT`, API functions, types, and helper functions
  - [x] 4.2: Add `@state()` properties: `_instance: InstanceDetailResponse | null`, `_loading: boolean`, `_error: boolean`, `_selectedStepId: string | null`, `_cancelling: boolean`
  - [x] 4.3: In `connectedCallback()`: extract instance ID from URL (Task 3.1 helper), consume auth context, fetch instance detail via `getInstance(id, token)`, compute run number
  - [x] 4.4: **Run number computation**: Fetch instances list via `getInstances(instance.workflowAlias, token)` after detail loads, sort by `createdAt` ascending, find current instance's index to derive run number. Store as `_runNumber: number`.
  - [x] 4.5: Render loading state with `<uui-loader>` (matching pattern from instance-list)
  - [x] 4.6: Render error state with message div (matching pattern from instance-list)
  - [x] 4.7: Render CSS Grid layout with `grid-template-columns: 240px 1fr` and `gap: var(--uui-size-layout-1)`
  - [x] 4.8: Render step progress sidebar as `_renderStepProgress()` private method — vertical list of steps
  - [x] 4.9: Render main content area with placeholder: "Click 'Start' to begin the workflow." (when instance is Pending)
  - [x] 4.10: Wrap in `<umb-body-layout>` with headline `${this._instance.workflowName} — Run #${this._runNumber}` — note: this replaces the dashboard's own `umb-body-layout` headline, so the detail view uses its OWN `umb-body-layout` wrapping the grid

- [x] Task 5: Implement step progress sidebar rendering (AC: #2, #3, #4, #5, #6, #7, #11)
  - [x] 5.1: `_renderStepProgress()` returns a `<div class="step-sidebar">` containing an `<ul role="list">` with one `<li role="listitem">` per step
  - [x] 5.2: Each step `<li>` contains: status icon (`<uui-icon>`), step name (`<span class="step-name">`), subtitle (`<span class="step-subtitle">`)
  - [x] 5.3: Active step gets `aria-current="step"` attribute
  - [x] 5.4: Each step's `aria-label` includes full context: `"Step ${index + 1}: ${step.name} — ${statusText}"` (AC #11)
  - [x] 5.5: Completed and active steps are clickable (cursor: pointer, click handler). Pending steps are not clickable.
  - [x] 5.6: Click handler dispatches `step-selected` CustomEvent with `detail: { stepId, status }` (AC #4, #5) and sets `_selectedStepId`
  - [x] 5.7: Selected step gets `class="selected"` which applies `border-left: 3px solid var(--uui-color-current)` (AC #7)
  - [x] 5.8: Active step icon uses CSS animation for spin effect: `@keyframes spin { to { transform: rotate(360deg); } }`

- [x] Task 6: Implement header with action buttons and back navigation (AC: #8, #9, #10, #13)
  - [x] 6.1: Header back button: `<uui-button>` with left arrow icon. Click calls `_navigateBack()` which uses `window.history.pushState()` with `buildInstanceListPath()` helper
  - [x] 6.2: Primary action: render "Start" `<uui-button look="primary">` ONLY when instance status is `Pending`. Hidden (not disabled) for all other statuses. Click handler logs `"Start not yet implemented"` to console (stub for Epic 4).
  - [x] 6.3: Cancel button: render in header (not overflow menu — simpler, matches UX spec secondary button pattern) as `<uui-button look="secondary" color="danger">` ONLY when status is `Running` or `Pending` (matches backend cancel logic)
  - [x] 6.4: Cancel click: show confirmation using `umbConfirmModal` from `@umbraco-cms/backoffice/modal` (same pattern as Story 3.3 review fix). Message: "Cancel this workflow run?". On confirm: call `POST /instances/{id}/cancel` via a new `cancelInstance(id, token)` API function, update local state.
  - [x] 6.5: Add `cancelInstance(id: string, token?: string)` to `api-client.ts` — calls `postJson<InstanceResponse>("/instances/${encodeURIComponent(id)}/cancel", {}, token)`

- [x] Task 7: Add styles using UUI design tokens (AC: #1, #2, #7)
  - [x] 7.1: `:host` — `display: block; padding: var(--uui-size-layout-1);`
  - [x] 7.2: `.detail-grid` — `display: grid; grid-template-columns: 240px 1fr; gap: var(--uui-size-layout-1); height: 100%;`
  - [x] 7.3: `.step-sidebar` — vertical flex column, step items spaced with `var(--uui-size-space-4)`
  - [x] 7.4: Step items — flex row: icon + text column. Icon sized at `18px`. Name uses `--uui-color-text`, subtitle uses `--uui-color-text-alt` and smaller font
  - [x] 7.5: Clickable steps (completed/active): `cursor: pointer`, hover uses `--uui-color-interactive-emphasis` on the name
  - [x] 7.6: Pending steps: icon and text use `--uui-color-disabled`
  - [x] 7.7: Selected step: `border-left: 3px solid var(--uui-color-current)` with padding-left adjustment
  - [x] 7.8: Main content placeholder: centred text, `--uui-color-text-alt`, vertically centred in the grid cell
  - [x] 7.9: Header layout: flex row with back button + headline + action buttons, matching instance-list pattern
  - [x] 7.10: Spin animation for active step icon

- [x] Task 8: Write unit tests (AC: #1–#13)
  - [x] 8.1: Test `getInstance` API function sends GET to correct URL with bearer token
  - [x] 8.2: Test `cancelInstance` API function sends POST to correct URL
  - [x] 8.3: Test `extractInstanceId` extracts and decodes last URL segment
  - [x] 8.4: Test `buildInstanceListPath` replaces `instances/{id}` with `workflows/{alias}`
  - [x] 8.5: Test `stepSubtitle` returns correct subtitle for each status (Pending, Active, Complete with writesTo, Complete without writesTo, Error)
  - [x] 8.6: Test `stepIconName` returns correct icon for each status
  - [x] 8.7: Test `stepIconColor` returns correct colour for each status
  - [x] 8.8: Test step click dispatches `step-selected` event with correct detail (verify via `CustomEvent` construction logic)
  - [x] 8.9: Test pending steps do not dispatch events (click handler not attached)
  - [x] 8.10: Test "Start" button only renders for Pending status
  - [x] 8.11: Test "Cancel" button only renders for Running or Pending status
  - [x] 8.12: Test run number computation: given 3 instances sorted by createdAt, the current instance gets the correct number

## Dev Notes

### Critical Gap: Backend Enrichment Required

The current `GET /instances/{id}` response (`InstanceDetailResponse`) returns steps with only `id`, `status`, `startedAt`, `completedAt`. It does **not** include:
- **Step name** — needed for AC #3 ("human-readable name from workflow.yaml")
- **Step writesTo** — needed for AC #3 subtitle ("artifact filename")
- **Workflow name** — needed for AC #8 header title ("{workflow name} — Run #{number}")

**Task 1 adds these fields.** The `MapToDetailResponse` method in `InstanceEndpoints.cs` already has access to `_workflowRegistry` — look up the workflow definition and join step names/writesTo from `StepDefinition` by matching step IDs.

### Existing Stub to Replace

`Client/src/components/shallai-instance-detail.element.ts` is a minimal stub from Story 1.3 — just a `<uui-box>` with placeholder text. Replace its contents entirely.

### Route Parameter Extraction

The dashboard routes (in `shallai-dashboard.element.ts`) define `instances/:id`. The component must extract the ID from the URL, same pattern as `extractWorkflowAlias`:

```typescript
export function extractInstanceId(pathname: string): string {
  const segments = pathname.split("/");
  return decodeURIComponent(segments[segments.length - 1]);
}
```

### Navigation — Back to Instance List

The instance detail is at `.../instances/{id}`. Back navigation needs to go to `.../workflows/{alias}`. The workflow alias comes from the loaded `InstanceDetailResponse.workflowAlias`:

```typescript
export function buildInstanceListPath(pathname: string, workflowAlias: string): string {
  const segments = pathname.split("/");
  // Replace last 2 segments (instances/{id}) with workflows/{alias}
  if (segments.length >= 2) {
    segments.splice(-2, 2, "workflows", encodeURIComponent(workflowAlias));
  }
  return segments.join("/");
}
```

### Run Number Computation

The AC requires "Run #{number}" in the header. The run number is not in the API response — it's the instance's position when sorted by `createdAt` ascending (oldest = #1). To compute it:

1. After loading instance detail, fetch `getInstances(instance.workflowAlias, token)`
2. Sort by `createdAt` ascending
3. Find index of current instance ID → run number = index + 1

This is the same approach as Story 3.3's `numberAndSortInstances`. Reuse the sort logic.

### Cancel Confirmation — Use umbConfirmModal

Story 3.3 review established `umbConfirmModal` as the pattern for destructive confirmations (replacing the initial `window.confirm()`). Use the same import:

```typescript
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
```

Call pattern:
```typescript
await umbConfirmModal(this, {
  headline: "Cancel workflow run",
  content: "Cancel this workflow run?",
  color: "danger",
  confirmLabel: "Cancel Run",
});
```

### Dashboard umb-body-layout Nesting

The dashboard element (`shallai-dashboard.element.ts` line 38) wraps the router in `<umb-body-layout headline="Agent Workflows">`. The instance detail component should use its **own** `umb-body-layout` for its specific headline. This creates nested `umb-body-layout` elements — Bellissima handles this correctly; the inner layout overrides the header content.

Look at how `shallai-instance-list` handles this — it uses a custom flex header with back button + h2 (from Story 3.3 implementation notes). Follow the same pattern: custom header inside the component, not `umb-body-layout` headline attribute.

### Step Icon Names — UUI Icon Registry

UUI icons are referenced by name string. The correct icon names for this story:
- `"icon-circle-dotted-line"` — empty circle (pending)
- `"icon-sync"` — rotating arrows (active, with CSS animation)
- `"icon-check"` — checkmark (complete)
- `"icon-close"` — X mark (error)

Verify these exist in the Umbraco backoffice by inspecting the icon picker or UUI docs. If `icon-circle-dotted-line` doesn't exist, fall back to `"icon-record"` (filled circle) with disabled colour.

### Step Status Enum Values

From the backend `StepStatus.cs` (JSON serialised as strings via `[JsonStringEnumConverter]`):
- `"Pending"` — step has not started
- `"Active"` — step is currently executing
- `"Complete"` — step finished successfully (note: not "Completed" — different from InstanceStatus)
- `"Error"` — step failed (note: not "Failed" — different from InstanceStatus)

**This is a common mistake source.** Instance status uses `Completed`/`Failed`; Step status uses `Complete`/`Error`. The helper functions must use the step-level values.

### Instance Status Enum Values

From `InstanceStatus.cs`: `"Pending"`, `"Running"`, `"Completed"`, `"Failed"`, `"Cancelled"`.

### API Response Shape — GET /instances/{id}

After Task 1 enrichment:
```json
{
  "id": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
  "workflowAlias": "content-audit",
  "workflowName": "Content Quality Audit",
  "status": "Pending",
  "currentStepIndex": 0,
  "createdAt": "2026-03-30T10:00:00Z",
  "updatedAt": "2026-03-30T10:00:00Z",
  "createdBy": "admin@example.com",
  "steps": [
    {
      "id": "scanner",
      "name": "Content Scanner",
      "status": "Pending",
      "startedAt": null,
      "completedAt": null,
      "writesTo": ["scan-results.md"]
    },
    {
      "id": "analyser",
      "name": "Quality Analyser",
      "status": "Pending",
      "startedAt": null,
      "completedAt": null,
      "writesTo": ["quality-scores.md"]
    }
  ]
}
```

### Cancel Endpoint — POST /instances/{id}/cancel

- Allowed when status is `Running` or `Pending`
- Returns 409 Conflict if status is `Completed`, `Failed`, or `Cancelled`
- Returns updated `InstanceResponse` (not detail) on success
- After cancel: reload instance detail to refresh all step statuses and UI state

### Testing Conventions

- Test file: `Client/src/components/shallai-instance-detail.element.test.ts`
- Uses `describe`/`it` with `expect` from `@open-wc/testing`
- Tests verify logic (data mapping, navigation paths, API calls, helper functions), not DOM rendering
- Monkey-patch `fetch` and `history.pushState` for API and navigation tests
- Use `afterEach` for cleanup of patched globals
- Pure functions in `utils/instance-detail-helpers.ts` are directly importable in tests (no Umbraco dependencies)

### Build Verification

Run `npm run build` in `Client/` before marking done — the frontend build output in `wwwroot/` must be up to date.

### Previous Story Intelligence

From Story 3.3:
- **Auth pattern**: Consume `UMB_AUTH_CONTEXT` in constructor via `this.consumeContext(UMB_AUTH_CONTEXT, (context) => { this.#authContext = context; })`, call `getLatestToken()` before each API call
- **State pattern**: `@state()` for `_loading`, `_error`, data properties
- **Loading/error/empty states**: Conditional returns in `render()` method
- **Navigation**: `window.history.pushState({}, "", targetPath)` — Umbraco patches pushState
- **Pure helpers extraction**: Extract all pure functions to a helpers file in `utils/` (both component and tests import from same source)
- **Review learning**: `umbConfirmModal` is the correct confirmation dialog pattern (not `window.confirm()`)
- **Review learning**: Always `encodeURIComponent` IDs in API URLs to prevent path injection
- **Review learning**: Don't set `_error = true` on action failures (create/cancel) — only on initial load. Preserve the existing valid view.

### File Placement

| File | Path | Action |
|------|------|--------|
| InstanceDetailResponse.cs | `Shallai.UmbracoAgentRunner/Models/ApiModels/InstanceDetailResponse.cs` | Modified — add `Name`, `WritesTo` to `StepResponse`; add `WorkflowName` to `InstanceDetailResponse` |
| InstanceEndpoints.cs | `Shallai.UmbracoAgentRunner/Endpoints/InstanceEndpoints.cs` | Modified — update `MapToDetailResponse` to populate new fields from workflow registry |
| types.ts | `Shallai.UmbracoAgentRunner/Client/src/api/types.ts` | Modified — add `StepDetailResponse`, `InstanceDetailResponse` interfaces |
| api-client.ts | `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` | Modified — add `getInstance`, `cancelInstance` |
| instance-detail-helpers.ts | `Shallai.UmbracoAgentRunner/Client/src/utils/instance-detail-helpers.ts` | New — pure helper functions for instance detail |
| shallai-instance-detail.element.ts | `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` | Modified — full replacement of stub |
| shallai-instance-detail.element.test.ts | `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.test.ts` | New — unit tests |
| InstanceEndpointsTests (detail enrichment) | `Shallai.UmbracoAgentRunner.Tests/Endpoints/` | Modified or new — tests for enriched detail response |

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 3, Story 3.4]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md — Instance detail layout, step progress sidebar, button hierarchy, accessibility, CSS Grid]
- [Source: _bmad-output/planning-artifacts/architecture.md — Frontend state, routing, API endpoints, component naming, UUI tokens]
- [Source: _bmad-output/implementation-artifacts/3-3-instance-list-dashboard.md — Auth pattern, navigation pattern, helpers extraction, review learnings]
- [Source: _bmad-output/implementation-artifacts/3-2-instance-api-endpoints.md — API response shapes, status enums, cancel logic]
- [Source: _bmad-output/project-context.md — All framework rules, Engine boundary, testing conventions]
- [Source: Client/src/components/shallai-instance-list.element.ts — Reference component for patterns]
- [Source: Client/src/utils/instance-list-helpers.ts — Reference helper functions pattern]
- [Source: Shallai.UmbracoAgentRunner/Endpoints/InstanceEndpoints.cs — Current MapToDetailResponse implementation]
- [Source: Shallai.UmbracoAgentRunner/Models/ApiModels/InstanceDetailResponse.cs — Current DTO shape]

### Review Findings

- [x] [Review][Patch] Cancel API error unhandled — `_onCancelClick` has no `catch` around `cancelInstance()`, causing unhandled promise rejection and no user feedback on failure [shallai-instance-detail.element.ts:233-241]
- [x] [Review][Patch] `aria-current` uses property binding (`.ariaCurrent`) instead of attribute binding — screen readers may not consistently detect it; use `aria-current=${...}` instead [shallai-instance-detail.element.ts:261]
- [x] [Review][Patch] Empty workflow name renders " — Run #N" when workflow deleted — fallback to `workflowAlias` in header for readability [shallai-instance-detail.element.ts:303]
- [x] [Review][Defer] TOCTOU race in cancel endpoint — status check then status write is non-atomic; pre-existing from Story 3.2 [InstanceEndpoints.cs:82-106] — deferred, pre-existing

## Dev Agent Record

### Agent Model Used
Claude Opus 4.6 (1M context)

### Debug Log References
- All 83 backend tests pass (was 82, +1 new fallback test)
- All 62 frontend tests pass (was 37, +25 new tests)
- Frontend build clean, no TypeScript errors

### Review Fix Notes
- Patch 1: Added `catch` block to `_onCancelClick` around the `cancelInstance` call. Logs `console.warn` on failure and preserves existing view (per Story 3.3 review learning — don't set `_error` on action failures).
- Patch 2: Changed `.ariaCurrent=${...}` (property binding) to `aria-current=${...}` (attribute binding) with `nothing` for removal. Ensures `aria-current="step"` attribute is always present in DOM for assistive tech.
- Patch 3: Header now renders `${inst.workflowName || inst.workflowAlias}` — falls back to workflow alias when workflow name is empty (deleted workflow scenario).
- Deferred: TOCTOU race in cancel endpoint is pre-existing from Story 3.2, already tracked in deferred-work.md.
- Review stats: 3 layers (Blind Hunter, Edge Case Hunter, Acceptance Auditor). 3 patches fixed, 1 deferred, 15 dismissed. All tests pass post-fix.

### Completion Notes List
- Task 1: Added `WorkflowName` to `InstanceDetailResponse`, `Name` and `WritesTo` to `StepResponse`. Updated `MapToDetailResponse` to look up workflow from registry with graceful fallback (step ID as name, empty string for workflow name when workflow deleted).
- Task 2: Added `StepDetailResponse` and `InstanceDetailResponse` interfaces to `types.ts`. Added `getInstance` and `cancelInstance` to `api-client.ts`.
- Task 3: Created `instance-detail-helpers.ts` with `extractInstanceId`, `buildInstanceListPath`, `stepSubtitle`, `stepIconName`, `stepIconColor` pure functions.
- Tasks 4-7: Full replacement of stub component. CSS Grid layout (240px sidebar + 1fr main). Step progress sidebar with status icons, clickable completed/active steps, `step-selected` custom event, ARIA attributes. Header with back button, contextual title with run number, Start button (pending-only stub), Cancel button with `umbConfirmModal` confirmation. UUI design tokens throughout. Used custom header pattern (not `umb-body-layout` headline) matching instance-list.
- Task 8: 25 new frontend tests covering API functions, helper functions, component behaviour logic (event dispatch, button visibility, run number computation), and navigation. 1 new backend test for workflow-deleted fallback.
- Decision: Used custom flex header instead of `umb-body-layout` headline attribute, matching the pattern from `shallai-instance-list` (Story 3.3).

### File List
- `Shallai.UmbracoAgentRunner/Models/ApiModels/InstanceDetailResponse.cs` — Modified (added WorkflowName, Name, WritesTo)
- `Shallai.UmbracoAgentRunner/Endpoints/InstanceEndpoints.cs` — Modified (enriched MapToDetailResponse with workflow lookup)
- `Shallai.UmbracoAgentRunner/Client/src/api/types.ts` — Modified (added StepDetailResponse, InstanceDetailResponse interfaces)
- `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` — Modified (added getInstance, cancelInstance)
- `Shallai.UmbracoAgentRunner/Client/src/utils/instance-detail-helpers.ts` — New (pure helper functions)
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` — Modified (full component implementation)
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.test.ts` — New (25 frontend tests)
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/InstanceEndpointsTests.cs` — Modified (updated existing test, added fallback test, added WritesTo to TestDefinition)
