# Story 1.3: Dashboard Shell & Empty Views

Status: done

## Story

As a developer,
I want to see an "Agent Workflows" section in the Umbraco backoffice with navigable views,
So that I can confirm the package is installed and the dashboard extension is registered correctly.

## Acceptance Criteria

1. **Given** the frontend toolchain from Story 1.2 is configured and `umbraco-package.json` registers a `backofficeEntryPoint` extension, **When** the Umbraco site starts and a user navigates to the backoffice, **Then** an "Agent Workflows" entry appears in the backoffice section sidebar.
2. **Given** the section is registered, **When** clicking "Agent Workflows", **Then** the `shallai-dashboard` component loads using `umb-body-layout`.
3. **Given** the dashboard component exists, **When** it renders, **Then** the dashboard uses `umb-router-slot` with three internal routes: `/workflows`, `/workflows/{alias}`, `/instances/{id}`.
4. **Given** the routes are configured, **When** navigating to each route, **Then** placeholder components exist for `shallai-workflow-list`, `shallai-instance-list`, and `shallai-instance-detail`.
5. **Given** placeholder components exist, **When** each is rendered, **Then** each displays a simple message confirming the route is active (e.g. "Workflow list view").
6. **Given** routes are configured, **When** navigating between routes, **Then** navigation works via internal routing without full page reload.
7. **Given** the dashboard is rendered, **When** inspecting the UI, **Then** the dashboard follows UUI design token conventions for colours, typography, and spacing.

## What NOT to Build

- No API calls, data fetching, or backend endpoints — views are static placeholders only
- No context providers (WorkflowContext, InstanceContext, ChatContext) — those come in later epics
- No real data or dynamic content in placeholder views
- No chat panel, step progress, or artifact viewer components — those come in Epics 3, 6, and 8
- No tests for routing behaviour — placeholder components are trivial; testing infrastructure was verified in 1.2
- Do NOT create a `backofficeEntryPoint` manifest that uses `UmbEntryPointOnInit` — Story 1.2 established the **bundle pattern** where `umbraco-package.json` loads the JS file and reads the exported `manifests` array. Keep that pattern.

## Tasks / Subtasks

- [x] Task 1: Register the section and section view manifests in `manifests.ts` (AC: #1, #2)
  - [x] 1.1 Add a `section` manifest with alias `Shallai.UmbracoAgentRunner.Section`, label `"Agent Workflows"`. The `meta.pathname` must be `"agent-workflows"` (this is the URL path segment under `/section/`)
  - [x] 1.2 Add a `sectionSidebarApp` manifest that creates the sidebar menu item pointing to the section
  - [x] 1.3 Add a `sectionView` manifest with alias `Shallai.UmbracoAgentRunner.SectionView.Dashboard` that uses an `element` loader dynamically importing `shallai-dashboard.element.js`. Set `meta.pathname` to `"overview"` and `meta.label` to `"Overview"`
  - [x] 1.4 Set `conditions` on section manifests requiring `Umb.Condition.SectionUserPermission` so the section respects Umbraco user permissions
  - [x] 1.5 Update `index.ts` if needed — it currently re-exports the `manifests` array which is the correct pattern

- [x] Task 2: Create `shallai-dashboard.element.ts` (AC: #2, #3, #6)
  - [x] 2.1 Create `Client/src/components/shallai-dashboard.element.ts`
  - [x] 2.2 Class name: `ShallaiDashboardElement` extending `UmbLitElement` (from `@umbraco-cms/backoffice/lit-element`)
  - [x] 2.3 Register custom element with `@customElement('shallai-dashboard')`
  - [x] 2.4 Render `umb-body-layout` with a headline of `"Agent Workflows"`
  - [x] 2.5 Inside `umb-body-layout`, render `umb-router-slot` with route configuration
  - [x] 2.6 Define routes array mapping:
    - `""` and `"workflows"` → dynamically import `shallai-workflow-list.element.js`, tag `shallai-workflow-list`
    - `"workflows/:alias"` → dynamically import `shallai-instance-list.element.js`, tag `shallai-instance-list`
    - `"instances/:id"` → dynamically import `shallai-instance-detail.element.js`, tag `shallai-instance-detail`
  - [x] 2.7 Use `UmbRouterSlotElement` type for router-slot and the Umbraco route interfaces for type safety
  - [x] 2.8 Use UUI design tokens for any layout styling (e.g. padding with `--uui-size-layout-1`)

- [x] Task 3: Create placeholder view components (AC: #4, #5, #7)
  - [x] 3.1 Create `Client/src/components/shallai-workflow-list.element.ts` — `ShallaiWorkflowListElement` extending `UmbLitElement`, displays "Workflow list view" in a `uui-box` with a heading
  - [x] 3.2 Create `Client/src/components/shallai-instance-list.element.ts` — `ShallaiInstanceListElement` extending `UmbLitElement`, displays "Instance list view" in a `uui-box` with a heading
  - [x] 3.3 Create `Client/src/components/shallai-instance-detail.element.ts` — `ShallaiInstanceDetailElement` extending `UmbLitElement`, displays "Instance detail view" in a `uui-box` with a heading
  - [x] 3.4 All placeholder components use `@customElement('shallai-{name}')` decorator
  - [x] 3.5 All styles use UUI design tokens: `--uui-color-text`, `--uui-color-surface`, `--uui-size-space-4`, etc.
  - [x] 3.6 Remove `.gitkeep` from `components/` once real files exist

- [x] Task 4: Build verification (AC: #1–#7)
  - [x] 4.1 Run `npm run build` in `Client/` — must succeed with no errors
  - [x] 4.2 Run `npm test` in `Client/` — existing placeholder test must still pass
  - [x] 4.3 Run `dotnet build` from solution root — must succeed
  - [x] 4.4 Run `dotnet test` — must still pass (2 tests from previous stories)
  - [ ] 4.5 Run `dotnet run` from TestSite project — Umbraco backoffice must load
  - [ ] 4.6 Navigate to backoffice — "Agent Workflows" section must appear in sidebar
  - [ ] 4.7 Click through each route — placeholders must display correct messages, no full page reloads

## Dev Notes

### Current Codebase State (from Story 1.2)

`manifests.ts` currently exports an empty array:
```typescript
export const manifests: Array<UmbExtensionManifest> = [];
```

`index.ts` re-exports it:
```typescript
import { manifests } from "./manifests.js";
export { manifests };
```

`umbraco-package.json` registers a `bundle` extension that loads the JS file and reads the exported `manifests` array. This is the **bundle pattern** — NOT the `UmbEntryPointOnInit` pattern. Story 1.2 explicitly decided to keep the bundle pattern. Do not change this.

The `components/` folder has only `.gitkeep`. All other folders (`contexts/`, `api/`, `utils/`) remain untouched this story.

### Bellissima Section Registration Pattern

Umbraco Bellissima uses manifest-driven extension registration. To add a new section to the backoffice, you need these manifest types:

1. **`section`** — Declares the section itself (appears in the section picker / sidebar)
2. **`sectionSidebarApp`** — Adds a menu item in the sidebar for the section (use `Umb.SectionSidebarApp.MenuItem` kind)
3. **`sectionView`** — Defines a view within the section (the actual content area)

All manifests go in the `manifests` array exported from `manifests.ts`. The bundle loader picks them up automatically.

**Critical:** The `meta.pathname` on the `section` manifest controls the URL segment (e.g. `/section/agent-workflows`). The `meta.pathname` on `sectionView` controls the sub-path within that section.

### Component Base Class

Use `UmbLitElement` from `@umbraco-cms/backoffice/lit-element` — NOT raw `LitElement`. `UmbLitElement` extends LitElement with:
- `this.observe()` for consuming Umbraco observables (needed in later epics)
- `UmbElementMixin` integration
- Proper Umbraco lifecycle management

Import pattern:
```typescript
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { customElement } from 'lit/decorators.js';
import { html, css } from 'lit';
```

Note: `html` and `css` come from `lit` directly (externalised). Decorators come from `lit/decorators.js`. The base class comes from `@umbraco-cms/backoffice`.

### Router Slot Configuration

`umb-router-slot` expects a routes array with this shape:

```typescript
import type { UmbRoute } from '@umbraco-cms/backoffice/router';

private _routes: UmbRoute[] = [
  {
    path: 'workflows',
    component: () => import('./shallai-workflow-list.element.js'),
    setup: () => { /* optional setup callback */ }
  },
  // ...
];
```

The `component` property uses dynamic `import()` for lazy loading. The import path MUST use `.js` extension (TypeScript ES module resolution — source is `.ts` but import paths reference the compiled `.js`).

Pass routes to the element:
```html
<umb-router-slot .routes=${this._routes}></umb-router-slot>
```

Add a default redirect from `""` to `"workflows"` so the section has a landing page.

### Manifest Type Definitions

The `UmbExtensionManifest` type is provided by `@umbraco-cms/backoffice/extension-types` (already in tsconfig types). Manifest objects are plain objects conforming to this type — no decorators or class instantiation needed.

Example section manifest shape:
```typescript
{
  type: 'section',
  alias: 'Shallai.UmbracoAgentRunner.Section',
  name: 'Agent Workflows Section',
  meta: {
    label: 'Agent Workflows',
    pathname: 'agent-workflows',
  },
}
```

### UUI Design Tokens for Placeholders

Placeholder views should look clean and native. Use:
- `uui-box` for content cards (provides elevation and padding)
- `--uui-size-layout-1` for outer padding/margins
- `--uui-size-space-4` for internal spacing
- `--uui-color-text` for text colour
- `--uui-color-text-alt` for secondary/muted text
- `:host { display: block; padding: var(--uui-size-layout-1); }` on all view components

### File Structure After This Story

```
Client/src/
  index.ts                                    # Entry — exports manifests (unchanged)
  manifests.ts                                # Section + sectionView manifests (updated)
  components/
    shallai-dashboard.element.ts              # Dashboard shell with umb-router-slot (new)
    shallai-workflow-list.element.ts           # Placeholder view (new)
    shallai-instance-list.element.ts           # Placeholder view (new)
    shallai-instance-detail.element.ts         # Placeholder view (new)
  contexts/                                   # Untouched (Epic 3+)
  api/                                        # Untouched (Epic 2+)
  utils/
    placeholder.test.ts                       # Existing test (unchanged)
```

### Project Structure Notes

- All new files go in `Client/src/components/` — one component per file
- File naming: `shallai-{name}.element.ts` (architecture convention)
- No backend changes in this story — purely frontend
- Build output remains at `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/`
- The `.gitkeep` in `components/` should be removed once real component files exist

### Previous Story Intelligence (1.2)

**Key learnings from Story 1.2:**
- `lit` is a devDependency only — externalised by Vite. Import `html`, `css` from `'lit'` and decorators from `'lit/decorators.js'` using bare specifiers
- Local `.ts` file imports must use `.js` extension (e.g., `import './shallai-workflow-list.element.js'`) — TypeScript ES module resolution requirement
- The bundle pattern is established: `umbraco-package.json` → loads JS → reads exported `manifests` array. Do NOT switch to EntryPoint pattern
- `@web/dev-server-esbuild` is configured for tests — test files are compiled separately from the main build
- `tsconfig.json` excludes `*.test.ts` files — tests use mocha globals that would fail tsc
- Build output is ~0.10 kB currently — will grow with real components but should remain small since Lit and Umbraco imports are externalised

**Files from 1.2 that this story modifies:**
- `manifests.ts` — currently empty array, this story populates it
- `components/.gitkeep` — removed once real components exist

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 1, Story 1.3]
- [Source: _bmad-output/planning-artifacts/architecture.md — Dashboard Architecture, Component Architecture, Frontend Patterns, File Organisation]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md — Component Hierarchy, Navigation Routes, Layout Specifications]
- [Source: _bmad-output/planning-artifacts/prd.md — FR32-FR37 Dashboard UI Requirements]
- [Source: _bmad-output/implementation-artifacts/1-2-frontend-build-toolchain.md — Previous Story Intelligence]
- [Source: _bmad-output/project-context.md — Frontend rules, naming conventions, Bellissima patterns]

## Dev Agent Record

### Agent Model Used
Claude Opus 4.6 (1M context)

### Debug Log References
- `npm run build`: 5 modules transformed, 0 errors. Output sizes: entry 1.27 kB, dashboard chunk 1.44 kB, 3 placeholder chunks ~0.81 kB each.
- `npm test`: 1/1 test files passed (placeholder.test.ts)
- `dotnet build Shallai.UmbracoAgentRunner.slnx`: 3 projects built, 0 errors, 6 warnings (MimeKit NU1902 — pre-existing)
- `dotnet test Shallai.UmbracoAgentRunner.slnx`: 2/2 passed

### Completion Notes List
- **Task 1**: Populated `manifests.ts` with 4 manifests: `section` (alias `Shallai.UmbracoAgentRunner.Section`, pathname `agent-workflows`), `menu` (alias `Shallai.UmbracoAgentRunner.Menu`), `sectionSidebarApp` (kind `menu`, meta `{ label, menu }` referencing the menu alias), `sectionView` (alias `Shallai.UmbracoAgentRunner.SectionView.Dashboard`, dynamic import of dashboard element, pathname `overview`). All section manifests have `Umb.Condition.SectionUserPermission` condition. SectionSidebarApp and sectionView have `Umb.Condition.SectionAlias` condition matching the section. `index.ts` unchanged — already re-exports correctly.
- **Task 2**: Created `shallai-dashboard.element.ts` — `ShallaiDashboardElement` extending `UmbLitElement`, renders `umb-body-layout` with headline "Agent Workflows" containing `umb-router-slot`. Routes: `workflows` → workflow-list, `workflows/:alias` → instance-list, `instances/:id` → instance-detail, `""` redirects to `workflows`. Uses `UmbRoute` type from `@umbraco-cms/backoffice/router`.
- **Task 3**: Created 3 placeholder components (`shallai-workflow-list`, `shallai-instance-list`, `shallai-instance-detail`) each extending `UmbLitElement`, using `uui-box` with headings, UUI design tokens for spacing (`:host` with `--uui-size-layout-1` padding). Removed `.gitkeep` from `components/`.
- **Task 4**: All automated builds and tests pass. Subtasks 4.5–4.7 require manual verification (start TestSite, navigate backoffice, click routes).
- **No tests added for this story** — story spec explicitly states "No tests for routing behaviour — placeholder components are trivial; testing infrastructure was verified in 1.2"
- **[Review Fix]**: `sectionSidebarApp` changed from invalid `kind: "menuItem"` to `kind: "menu"` with a registered `menu` manifest. Bellissima 17 only supports `"menu"` and `"menuWithEntityActions"` kinds — `"menuItem"` silently fails. Future `menuItem` manifests should be registered under `Shallai.UmbracoAgentRunner.Menu` to populate the sidebar tree.
- **[Review Fix]**: `Umb.Condition.SectionUserPermission` requires a `match` property set to the section alias (e.g. `match: "Shallai.UmbracoAgentRunner.Section"`). Without it, `allowedSections.includes(undefined)` always returns `false` and the section never renders. Found by inspecting the condition source in `@umbraco-cms/backoffice/dist-cms`.
- **[Review Fix]**: Bellissima does NOT provide bare `lit` or `lit/decorators.js` in its import map. It provides `@umbraco-cms/backoffice/external/lit` which re-exports all of lit + decorators. Changed all component imports from `lit`/`lit/decorators.js` to `@umbraco-cms/backoffice/external/lit`. Removed `/^lit/` from Vite externals (no longer needed). Without this fix, dynamically-imported chunks fail at runtime with "Failed to resolve module specifier" error.
- **[Manual Step Required]**: New custom sections are not auto-granted to user groups. To see "Agent Workflows" in the backoffice section picker, go to **Users > User Groups > Administrators > Sections** and add "Agent Workflows". This is a one-time setup per environment.

### Review Findings
- [x] [Review][Patch] `sectionSidebarApp` manifest uses invalid `kind: "menuItem"` — Bellissima 17 only registers `"menu"` and `"menuWithEntityActions"` kinds. Current meta shape `{ label, icon, entityType, menus }` also doesn't match expected `{ label, menu }`. Sidebar item will silently fail to render. [manifests.ts:20-35] — **Fixed:** Changed to `kind: "menu"` with correct `{ label, menu }` meta and added a `menu` manifest (`Shallai.UmbracoAgentRunner.Menu`).

### File List
- `Shallai.UmbracoAgentRunner/Client/src/manifests.ts` — updated (was empty array, now 3 manifests)
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-dashboard.element.ts` — new
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-workflow-list.element.ts` — new
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-list.element.ts` — new
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts` — new
- `Shallai.UmbracoAgentRunner/Client/src/components/.gitkeep` — deleted
- `Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` — rebuilt (5 JS files + sourcemaps)
