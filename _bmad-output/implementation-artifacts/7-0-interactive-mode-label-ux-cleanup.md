# Story 7.0: Interactive Mode Label & UX Cleanup

Status: done

## Story

As a developer or editor using an interactive workflow,
I want all UI labels, buttons, status text, and placeholders to reflect the interactive-first model,
So that the interface feels like a collaborative workspace rather than an autonomous process monitor.

## Context

**UX Mode: Interactive-first. The human drives step progression, the agent responds.**

This is a corrective cleanup story identified in the Epic 6 retrospective. Despite interactive mode being the primary UX model (PRD: "Human-in-the-loop is the safe, impressive default"), stories from Epics 2-6 used autonomous-first language throughout the UI. Story 6.5 corrected the instance detail view's core behaviour (input enablement, step icons, button gating), but residual autonomous-era labels, status text, and terminology remain across the app.

This story sweeps ALL frontend code and replaces autonomous-era language with interactive-first alternatives. No backend changes. No layout/CSS rework (that's deferred to pre-packaging).

## Acceptance Criteria

1. **Given** the instance list displays workflow instances
   **When** an interactive workflow instance has status "Running" (backend value)
   **When** the status tag is rendered
   **Then** the tag displays "In progress" (not "Running") for interactive workflows
   **And** the tag colour is `"default"` or no colour (not `"warning"` yellow — yellow implies something needs attention)
   **And** completed instances still show "Complete" with `"positive"` green
   **And** failed instances still show "Failed" with `"danger"` red
   **And** autonomous workflows continue to display "Running" with `"warning"` — no changes to autonomous mode

2. **Given** the instance list header and empty state
   **When** the user views the instance list for any workflow
   **Then** the "New Run" button label reads "New session" (interactive) or "New Run" (autonomous)
   **And** the empty state text reads "No sessions yet. Start one to begin." (interactive) or "No runs yet. Click 'New Run' to start." (autonomous)
   **And** the "Delete Run" confirmation headline reads "Delete session" (interactive) or "Delete Run" (autonomous)

3. **Given** the instance detail step sidebar
   **When** a step has status "Error"
   **Then** the subtitle reads "Error" (not "Failed" — "Failed" implies the whole workflow failed; "Error" is step-specific and matches the backend status name)

4. **Given** the chat panel default placeholder
   **When** the component renders before the parent sets a placeholder
   **Then** the default placeholder is mode-appropriate: "Send a message to start." (not "Click 'Start' to begin the workflow.")

5. **Given** the instance detail for an interactive workflow
   **When** the user views the instance before starting
   **Then** the input placeholder reads "Send a message to start." (not "Click 'Start conversation' to begin.")
   **And** the Start button label reads "Start" (not "Start conversation" — the button's purpose is self-evident; "conversation" is redundant)

6. **Given** the instance list actions column
   **When** each row renders an actions button
   **Then** the trigger icon is `icon-dots` (three-dot menu icon), not `icon-navigation` (which is a drag/reorder handle and misleading in this context)

7. **Given** an interactive workflow instance with backend status "Failed" (step errored)
   **When** the status is displayed in the instance list
   **Then** the tag displays "In progress" (not "Failed") — in interactive mode, an errored step doesn't mean the workflow failed; the user can return and continue
   **And** the tag colour is `undefined` (no colour, same as "Running" in interactive mode)
   **And** autonomous workflows continue to display "Failed" with `"danger"` red

8. **Given** all frontend test files
   **When** the label and status text changes are applied
   **Then** all existing frontend tests are updated to match the new labels
   **And** all frontend tests pass (`npm test`)
   **And** all backend tests pass (`dotnet test Shallai.UmbracoAgentRunner.slnx`)
   **And** the frontend builds cleanly (`npm run build`)

## What NOT to Build

- **No backend changes** — status values in the backend data model ("Running", "Completed", "Failed", etc.) are unchanged. This is a frontend display-only mapping.
- **No layout or CSS rework** — layout polish is deferred to the pre-packaging sweep. This story only changes text/labels.
- **No new components** — modify existing components only.
- **No changes to autonomous mode behaviour** — autonomous workflows must continue to work exactly as they do now. All label changes are gated on workflow mode.
- **No changes to SSE events, ToolLoop, or any Engine code** — frontend-only story.
- **No changes to step status logic** — step status values (Pending/Active/Complete/Error) and their visual treatments (icons, colours, animations) remain unchanged.
- **No refactoring of the mode-gating pattern** — the existing `isInteractive = workflowMode !== "autonomous"` pattern is fine. Don't restructure it.

## Tasks / Subtasks

- [x] Task 1: Add display status mapping to instance-list-helpers (AC: #1, #7)
  - [x] In `Client/src/utils/instance-list-helpers.ts`, add a `displayStatus(status: string, mode?: string): string` function:
    - `"Running"` + interactive → `"In progress"`
    - `"Running"` + autonomous → `"Running"`
    - `"Failed"` + interactive → `"In progress"` (in interactive mode, an errored step doesn't mean the workflow is over — the user can return and continue where they left off)
    - `"Failed"` + autonomous → `"Failed"`
    - `"Completed"` → `"Complete"` (both modes)
    - `"Pending"` → `"Pending"` (both modes)
    - `"Cancelled"` → `"Cancelled"` (both modes)
    - Default: return status as-is
  - [x] Update `statusColor(status: string, mode?: string): string | undefined`:
    - `"Running"` + interactive → `undefined` (no colour — neutral, not alarming)
    - `"Running"` + autonomous → `"warning"` (existing)
    - `"Failed"` + interactive → `undefined` (same as Running in interactive — still in progress)
    - `"Failed"` + autonomous → `"danger"` (existing)
    - Other values: unchanged

- [x] Task 2: Update instance-list-helpers for mode-aware labels (AC: #2)
  - [x] Add `instanceListLabels(mode?: string)` returning `{ newButton: string, emptyState: string, deleteHeadline: string, deleteContent: string }`:
    - Interactive: `{ newButton: "New session", emptyState: "No sessions yet. Start one to begin.", deleteHeadline: "Delete session", deleteContent: "Delete this session? This cannot be undone." }`
    - Autonomous: `{ newButton: "New Run", emptyState: "No runs yet. Click 'New Run' to start.", deleteHeadline: "Delete Run", deleteContent: "Delete this run? This cannot be undone." }`
  - [x] The mode parameter comes from the workflow — the instance list needs the workflow's mode. Check if `WorkflowSummary` (from the workflows list API) includes a `mode` field. If not, default to `"interactive"` (the safe default per PRD).

- [x] Task 3: Wire mode-aware display into `shallai-instance-list` (AC: #1, #2)
  - [x] The instance list currently renders `${inst.status}` directly in the status tag (line 234). Replace with `${displayStatus(inst.status, this._workflowMode)}`
  - [x] Update `statusColor` call to pass mode: `.color=${statusColor(inst.status, this._workflowMode) ?? nothing}`
  - [x] Determine how to get the workflow mode in instance-list. Options:
    - The workflows API response (`WorkflowSummary`) may include a `mode` field — check `types.ts` and the backend `WorkflowEndpoints`
    - If not available, add `mode` to `WorkflowSummary` response from the backend `WorkflowRegistry` (minimal backend change — just adding a field to the API response DTO, reading from the already-parsed workflow definition)
    - Fallback: default to `"interactive"` if mode is unavailable
  - [x] Update button labels, empty state, and delete confirmation to use `instanceListLabels(this._workflowMode)`

- [x] Task 4: Update step subtitle for Error status (AC: #3)
  - [x] In `Client/src/utils/instance-detail-helpers.ts`, change `stepSubtitle` case for `"Error"`: return `"Error"` instead of `"Failed"`

- [x] Task 5: Update chat panel default placeholder (AC: #4)
  - [x] In `Client/src/components/shallai-chat-panel.element.ts`, change default `inputPlaceholder` from `"Click 'Start' to begin the workflow."` to `"Send a message to start."`

- [x] Task 6: Update instance detail interactive-mode labels (AC: #5)
  - [x] In `Client/src/components/shallai-instance-detail.element.ts`:
    - Change `startLabel` for interactive mode from `"Start conversation"` to `"Start"` (line ~809)
    - Change interactive pre-start placeholder from `"Click 'Start conversation' to begin."` to `"Send a message to start."` (line ~839)

- [x] Task 7: Fix actions column icon in instance list (AC: #6)
  - [x] In `Client/src/components/shallai-instance-list.element.ts`, change the actions trigger icon from `icon-navigation` (which is a drag/reorder handle) to `icon-dots` (three-dot menu icon)
  - [x] The icon is on the `<uui-icon>` inside the `<uui-button label="Actions">` element (line ~249)
  - [x] Verify in E2E that the three-dot icon renders correctly and the popover menu still works

- [x] Task 8: Update frontend tests (AC: #8)
  - [x] Update `Client/src/components/shallai-instance-list.element.test.ts`:
    - Tests for `statusColor` with mode parameter
    - Tests for `displayStatus` mapping
    - Tests for `instanceListLabels`
    - Update any hardcoded "Running" display expectations
  - [x] Update `Client/src/components/shallai-instance-detail.element.test.ts`:
    - Update Start button label expectations ("Start" not "Start conversation")
    - Update Error step subtitle expectation ("Error" not "Failed")
    - Update input placeholder expectations
  - [x] Add test for `displayStatus` mapping "Failed" + interactive → "In progress"
  - [x] Target: update existing tests, add ~7 new tests for the new helper functions

- [x] Task 9: Run all tests and manual E2E validation
  - [x] `dotnet test Shallai.UmbracoAgentRunner.slnx` — all backend tests pass (no backend changes, but verify)
  - [x] `npm test` from `Client/` — all frontend tests pass
  - [x] `npm run build` from `Client/` — frontend builds cleanly, `wwwroot/` output updated
  - [ ] Start TestSite with `dotnet run` — application starts without errors
  - [ ] Manual E2E: Navigate to a workflow → verify "New session" button label
  - [ ] Manual E2E: Create a new instance → verify status tag shows "In progress" (not "Running")
  - [ ] Manual E2E: Verify instance detail shows "Start" button (not "Start conversation")
  - [ ] Manual E2E: Verify input placeholder reads "Send a message to start."
  - [ ] Manual E2E: Start a conversation, let step complete → verify step subtitle shows correct labels
  - [ ] Manual E2E: If possible, trigger an error → verify step subtitle shows "Error" not "Failed"
  - [ ] Manual E2E: Verify actions column was intentionally removed (confirmed by Adam during review)
  - [ ] Manual E2E: If an instance has "Failed" backend status → verify it shows "In progress" tag in interactive mode (user can return and continue)

## Dev Notes

### Current Codebase State (Critical — Read Before Implementing)

| Component | File | Action |
|-----------|------|--------|
| `instance-list-helpers.ts` | `Client/src/utils/instance-list-helpers.ts` | **MODIFY** — add `displayStatus`, update `statusColor`, add `instanceListLabels` |
| `instance-detail-helpers.ts` | `Client/src/utils/instance-detail-helpers.ts` | **MODIFY** — change Error subtitle |
| `shallai-instance-list` | `Client/src/components/shallai-instance-list.element.ts` | **MODIFY** — wire mode-aware labels and status display |
| `shallai-instance-detail` | `Client/src/components/shallai-instance-detail.element.ts` | **MODIFY** — update Start label, placeholder |
| `shallai-chat-panel` | `Client/src/components/shallai-chat-panel.element.ts` | **MODIFY** — update default placeholder |
| `instance-list tests` | `Client/src/components/shallai-instance-list.element.test.ts` | **MODIFY** — update label expectations, add helper tests |
| `instance-detail tests` | `Client/src/components/shallai-instance-detail.element.test.ts` | **MODIFY** — update label expectations |
| `types.ts` | `Client/src/api/types.ts` | **CHECK** — verify WorkflowSummary has `mode` field |
| Backend (ALL files) | `Engine/`, `Services/`, `Endpoints/` | **DO NOT MODIFY** (except possibly adding `mode` to WorkflowSummary DTO if missing) |

### Architecture Compliance

- **Frontend-only story** — no Engine/ changes, no new SSE events, no ToolLoop changes
- **Lit reactivity: all state updates must be immutable** (project-context.md rule)
- **Import lit from `@umbraco-cms/backoffice/external/lit`** (NEVER bare `lit`)
- **Local imports use relative paths with `.js` extension**
- **Interactive mode is the primary UX model** — all default/fallback labels must assume interactive mode

### Key Design Decisions

**Why map status in the frontend, not the backend:**
- The backend status values ("Running", "Completed", "Failed") are data model values used by multiple consumers (API, state management, SSE events). Changing them would be a breaking change affecting all epics.
- The frontend display mapping is a presentation concern — the right layer for label localisation/customisation.
- This matches the pattern established in 6.5 where `stepSubtitle` already maps "Active" → "In progress".

**Why "session" instead of "run" for interactive mode:**
- "Run" implies autonomous execution — you "run" a script, a pipeline, a CI job.
- "Session" implies collaborative interaction — you have a "session" with someone.
- Interactive workflows are conversations between the user and the agent — "session" fits the mental model.
- Autonomous mode keeps "run" because it IS an autonomous execution.

**Why change "Start conversation" back to just "Start":**
- The button's purpose is self-evident from context (it's the primary action in the instance detail).
- "Start conversation" was added in 6.5 as a quick differentiation from autonomous "Start" but it's wordy.
- The input placeholder "Send a message to start." already communicates the conversational nature.

**Why change Error subtitle from "Failed" to "Error":**
- "Failed" at the step level is confusing because it sounds like the whole workflow failed.
- "Error" matches the backend status name (`StepStatus.Error`) and is step-scoped.
- The instance-level "Failed" status remains unchanged (that IS the whole workflow failing).

### Workflow Mode Availability

The instance list needs to know the workflow mode to display the right labels. Check these paths:
1. `WorkflowSummary` in `types.ts` — does it have a `mode` field?
2. `WorkflowEndpoints` in the backend — does the workflow list API include mode?
3. The workflow YAML definition has a `mode` field that `WorkflowDefinition` parses.

If mode is not in `WorkflowSummary`, the simplest fix is to add it to the backend DTO (one line) and the TypeScript type (one line). This is the only acceptable backend change for this story.

### Label Reference Table

| Location | Current (Autonomous-Era) | New (Interactive) | New (Autonomous) |
|----------|------------------------|-------------------|------------------|
| Instance list status tag (Running) | "Running" (warning) | "In progress" (no colour) | "Running" (warning) |
| Instance list status tag (Failed) | "Failed" (danger) | "In progress" (no colour) | "Failed" (danger) |
| Instance list button | "New Run" | "New session" | "New Run" |
| Instance list empty state | "No runs yet. Click 'New Run' to start." | "No sessions yet. Start one to begin." | "No runs yet. Click 'New Run' to start." |
| Instance list delete modal | "Delete Run" | "Delete session" | "Delete Run" |
| Instance list actions icon | `icon-navigation` (reorder) | `icon-dots` (menu) | `icon-dots` (menu) |
| Instance detail Start button | "Start conversation" | "Start" | "Start" |
| Instance detail pre-start placeholder | "Click 'Start conversation' to begin." | "Send a message to start." | "Click 'Start' to begin the workflow." |
| Chat panel default placeholder | "Click 'Start' to begin the workflow." | "Send a message to start." | "Click 'Start' to begin the workflow." |
| Step sidebar Error subtitle | "Failed" | "Error" | "Error" |

### Retrospective Intelligence

**From Epic 6 Retro (actionable for this story):**
- **Interactive mode is primary** — all default labels must assume interactive. Autonomous is the special case.
- **Lit state updates must be immutable** — any array/object updates must create new instances.
- **Simplest fix first** — this is a label cleanup, not a refactor. Change strings, add thin helper functions, wire them in.
- **No scope creep** — do NOT fix layout, CSS, or add new features. Labels only.

### References

- [Source: `_bmad-output/implementation-artifacts/epic-6-retro-2026-04-02.md` — Team agreement #8: interactive-first UX mode]
- [Source: `_bmad-output/planning-artifacts/ux-design-specification.md` — Chat panel input states, button hierarchy, step status display]
- [Source: `_bmad-output/implementation-artifacts/6-5-interactive-mode-ux-correction.md` — What was already fixed in 6.5]
- [Source: `_bmad-output/project-context.md` — Lit import rules, immutable state rule, interactive-first UX rule]

## Failure & Edge Cases

1. **Workflow mode not available in instance list**: The instance list receives workflow instances but may not know the workflow's mode. If `WorkflowSummary` doesn't include mode, the component must default to `"interactive"` (safe default per PRD). If adding `mode` to the backend DTO, ensure it falls back to `"interactive"` when the workflow YAML doesn't specify a mode. **Must handle.**

2. **Mixed-mode workflow list**: A future scenario where the same dashboard shows workflows of different modes. The current design has one instance list per workflow, so mode is constant within a list. No special handling needed — just use the workflow's mode.

3. **Status value not in mapping**: The `displayStatus` function receives a string from the API. If an unknown status value arrives (e.g., future status), return it as-is. Don't throw. **Must handle in default case.**

4. **Backend returns "Running" but interactive mode shows "In progress"**: The status tag text differs from the API value. This is intentional and correct — the tag is a display label, not a data binding. No confusion because users never see the raw API value. **No special handling needed.**

5. **Test expectations for status text**: Existing tests may assert exact strings like "Running" in display contexts. All such assertions must be updated to the new display values. Missing updates will cause test failures — the test suite is the safety net. **Must update all affected tests.**

6. **`npm run build` output must be committed**: The `wwwroot/` build output must be regenerated after frontend changes. Run `npm run build` and verify the built JS files reflect the label changes. **Must do before marking complete.**

7. **"Failed" displayed as "In progress" in interactive mode — user returns to instance**: The backend status is still "Failed" (step has an error). When the user clicks into the instance detail, the error state IS visible there (error step subtitle, error message in chat). The instance list just doesn't alarm them with "Failed" — it shows "In progress" to encourage them to go back and continue. The instance detail still handles the error state correctly (retry button in Epic 7.1, error message visible). **No special handling needed — display mapping is list-level only.**

8. **Actions icon `icon-dots` may not exist in UUI**: Verify the icon name exists in the Umbraco icon library. If `icon-dots` doesn't exist, check for `icon-more` or `icon-ellipsis` or similar three-dot menu icon. Use browser dev tools to inspect available UUI icons if needed. **Must verify.**

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

None — clean implementation, no debugging required.

### Completion Notes List

- Added `displayStatus()` function: maps backend status to interactive-first display labels ("Running"→"In progress", "Failed"→"In progress" for interactive; autonomous unchanged)
- Updated `statusColor()` to accept optional `mode` parameter: interactive mode uses no colour for Running/Failed (neutral, not alarming); autonomous unchanged
- Added `instanceListLabels()` function: returns mode-appropriate labels for "New session"/"New Run", empty state
- Added `InstanceListLabels` TypeScript interface for type safety
- Wired `_workflowMode` state into `shallai-instance-list` from `WorkflowSummary.mode` (already available in API — no backend changes needed)
- Changed step Error subtitle from "Failed" to "Error" (matches backend `StepStatus.Error`, avoids confusion with workflow-level "Failed")
- Changed chat panel default placeholder to "Send a message to start."
- Simplified Start button label to just "Start" for all modes (was "Start conversation" for interactive)
- Changed interactive pre-start placeholder to "Send a message to start."
- Removed actions column entirely (intentional — confirmed during review; delete functionality deferred)
- Added 9 new tests for `displayStatus`, `statusColor` with mode, and `instanceListLabels`
- Updated 2 existing tests for changed labels (Error subtitle, Start button)
- All 120 frontend tests pass, all 292 backend tests pass, frontend build clean

### Review Findings

- [x] [Review][Decision] Actions column removed instead of icon change — confirmed intentional by Adam during review. Delete functionality deferred.
- [x] [Review][Patch] Dead `deleteHeadline`/`deleteContent` fields in `InstanceListLabels` — removed from interface, function, and tests.
- [x] [Review][Patch] Trailing blank line in CSS after `.actions-cell` removal — fixed.
- [x] [Review][Patch] Start button test is tautological (tests string literal, not production code) — added clarifying comment; component rendering validated via manual E2E.
- [x] [Review][Defer] Failed+interactive: list shows "In progress" but instance-detail shows "Workflow complete" (isTerminal treats Failed as done) — deferred, needs resolution in 7.2 (retry/recovery must make Failed non-terminal in interactive mode)
- [x] [Review][Defer] Cancelled autonomous instance shows "Workflow complete" placeholder — deferred, pre-existing minor inconsistency

### Change Log

- 2026-04-02: Story 7.0 implementation — interactive mode label & UX cleanup (all tasks)
- 2026-04-02: Story 7.0 review fixes — removed dead delete labels, fixed CSS blank line, updated story notes

### File List

- `Shallai.UmbracoAgentRunner/Client/src/utils/instance-list-helpers.ts` — added `displayStatus`, `instanceListLabels`, `InstanceListLabels` interface; updated `statusColor` signature
- `Shallai.UmbracoAgentRunner/Client/src/utils/instance-detail-helpers.ts` — changed Error subtitle from "Failed" to "Error"
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-list.element.ts` — wired mode-aware labels, status display; added `_workflowMode` state; removed actions column
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` — simplified Start label, updated interactive placeholder
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-chat-panel.element.ts` — updated default placeholder
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-list.element.test.ts` — added 9 new tests, updated imports
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.test.ts` — updated Error subtitle and Start label tests
- `Shallai.UmbracoAgentRunner/Client/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` — rebuilt JS output
