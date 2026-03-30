# Story 1.2: Frontend Build Toolchain

Status: done

## Story

As a developer,
I want the Vite + Lit + TypeScript frontend toolchain configured in the Client/ folder,
So that I can build Bellissima dashboard components that compile to the correct output location.

## Acceptance Criteria

1. **Given** the RCL project from Story 1.1 exists with the template-scaffolded `Client/` folder, **When** `npm install` is run in `Client/`, **Then** all dependencies install successfully including `lit`, `@umbraco-cms/backoffice` (devDependency pinned to `17.2.2`), and `vite`.
2. **Given** `vite.config.ts` is configured in lib mode, **When** `npm run build` is executed, **Then** an ES module bundle `shallai-umbraco-agent-runner.js` is output to `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/`.
3. **Given** Rollup config in `vite.config.ts`, **When** the bundle is built, **Then** all `@umbraco` and `lit` imports are externalised (not bundled — provided by host at runtime via import maps).
4. **Given** `public/umbraco-package.json` exists, **When** `npm run build` runs, **Then** `umbraco-package.json` is copied to `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/`.
5. **Given** the build toolchain is configured, **When** `npm run watch` is run in `Client/`, **Then** Vite watches for file changes and rebuilds automatically.
8. **Given** `@web/test-runner` is installed, **When** `npm test` is run in `Client/`, **Then** the test runner executes successfully (even with zero tests).
6. **Given** the `Client/src/` folder, **When** inspected, **Then** it contains: `index.ts`, `manifests.ts`, `components/`, `contexts/`, `api/`, `utils/` matching the architecture document.
7. **Given** the `tsconfig.json`, **When** inspected, **Then** it targets `ESNext` with `experimentalDecorators: true` and `useDefineForClassFields: false` (required for Lit decorators).

## Tasks / Subtasks

- [x] Task 1: Update `package.json` dependencies (AC: #1)
  - [x] 1.1 Remove OpenAPI-related deps and scripts: `@hey-api/openapi-ts`, `chalk`, `node-fetch`, `generate-client` script
  - [x] 1.2 Add `lit` as a **devDependency** (`^3.3.2`) — Lit is provided by the Umbraco backoffice at runtime via import maps. Bundling a second copy causes duplicate custom element registration failures. DevDependency gives us types during development; the external config (Task 2.4) ensures it's not bundled
  - [x] 1.3 Pin `@umbraco-cms/backoffice` to exact `17.2.2` (remove `^` — Umbraco minor versions can introduce API changes)
  - [x] 1.4 Update `vite` to `^7.1.9` (keep current — Vite 8 uses Rolldown which is too new to trust for production Lit compilation)
  - [x] 1.5 Keep `typescript` at `^5.9.3` (TypeScript 6.x is too new — Umbraco's own packages target TS 5.x)
  - [x] 1.6 Remove `cross-env` (not needed — no cross-platform env var usage in scripts)
  - [x] 1.7 Add `@web/test-runner` (`^0.20.2`) and `@open-wc/testing` (`^4.0.0`) as devDependencies (AC: #8)
  - [x] 1.8 Add `test` script: `"test": "web-test-runner \"src/**/*.test.ts\" --node-resolve"` (AC: #8)

- [x] Task 2: Update `vite.config.ts` (AC: #2, #3, #4)
  - [x] 2.1 Change `entry` from `"src/bundle.manifests.ts"` to `"src/index.ts"`
  - [x] 2.2 Verify `formats: ["es"]`, `fileName: "shallai-umbraco-agent-runner"` (already correct)
  - [x] 2.3 Verify `outDir: "../wwwroot/App_Plugins/ShallaiUmbracoAgentRunner"` (already correct)
  - [x] 2.4 Update `external` to `[/^@umbraco/, /^lit/]` — must externalise both `@umbraco-cms/*` and `lit`/`lit/*` imports. Lit is provided by Umbraco's import map at runtime
  - [x] 2.5 Verify `emptyOutDir: true`, `sourcemap: true` (already correct)
  - [x] 2.6 Add `publicDir: "public"` to ensure `umbraco-package.json` is copied to outDir on build

- [x] Task 3: Update `tsconfig.json` (AC: #7)
  - [x] 3.1 Change `target` from `"ES2020"` to `"ESNext"`
  - [x] 3.2 Change `module` to `"ESNext"` (already correct)
  - [x] 3.3 Change `lib` from `["ES2020", "DOM", "DOM.Iterable"]` to `["ESNext", "DOM", "DOM.Iterable"]`
  - [x] 3.4 Verify `experimentalDecorators: true` and `useDefineForClassFields: false` (already correct — required for Lit `@property` decorators)
  - [x] 3.5 Verify `types: ["@umbraco-cms/backoffice/extension-types"]` (already correct)

- [x] Task 4: Restructure `Client/src/` folder (AC: #6)
  - [x] 4.1 Create `index.ts` — entry point that imports and re-exports the manifests array. Uses bundle pattern (not UmbEntryPointOnInit directly) as per Dev Notes.
  - [x] 4.2 Create `manifests.ts` — exports the `manifests` array (all extension registrations). Initially empty array — Story 1.3 will add the dashboard section and route registrations
  - [x] 4.3 Create empty directories: `components/`, `contexts/`, `api/`, `utils/` (with `.gitkeep` files)
  - [x] 4.4 Delete `bundle.manifests.ts` (replaced by `index.ts`)
  - [x] 4.5 Delete `entrypoints/` folder (replaced by `index.ts` + `manifests.ts`)

- [x] Task 5: Update `umbraco-package.json` (AC: #4)
  - [x] 5.1 Verify the bundle extension `js` path still points to the correct output: `/App_Plugins/ShallaiUmbracoAgentRunner/shallai-umbraco-agent-runner.js`
  - [x] 5.2 Keep the `bundle` type registration — this loads the JS file which then programmatically registers extensions via the manifests array

- [x] Task 6: Delete template scaffolding no longer needed (AC: #6)
  - [x] 6.1 Delete `Client/scripts/` directory if it exists (OpenAPI generator file was removed in Story 1.1 — directory may or may not remain)
  - [x] 6.2 Delete `Client/.vscode/` directory if it only contains template defaults — KEPT: contains useful `lit-plugin` recommendation

- [x] Task 7: Build verification (AC: #1, #2, #3, #4, #5, #8)
  - [x] 7.1 Run `npm install` in `Client/` — must succeed with no errors
  - [x] 7.2 Run `npm run build` in `Client/` — must produce `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/shallai-umbraco-agent-runner.js`
  - [x] 7.3 Verify `umbraco-package.json` is present in `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/`
  - [x] 7.4 Verify `shallai-umbraco-agent-runner.js.map` sourcemap exists
  - [x] 7.5 Inspect the JS bundle — confirm no `@umbraco` imports are bundled (they should be external)
  - [x] 7.6 Run `npm run watch` — confirm it starts watching (Ctrl+C to stop)
  - [x] 7.7 Run `dotnet build` from solution root — must still succeed
  - [x] 7.8 Run `dotnet test` — must still pass

## Dev Notes

### Current State from Story 1.1

The `umbraco-extension` template (Umbraco.Templates 17.2.2) already scaffolded `Client/` with:
- `package.json` — has Vite 7.1.9, TypeScript 5.9.3, `@umbraco-cms/backoffice ^17.2.2`, plus OpenAPI deps we don't need
- `vite.config.ts` — lib mode, correct output dir, correct externals. Entry points to `bundle.manifests.ts`
- `tsconfig.json` — targets ES2020 (needs ESNext), has correct decorator settings
- `public/umbraco-package.json` — registers a `bundle` type extension
- `src/bundle.manifests.ts` — collates manifests from `entrypoints/`
- `src/entrypoints/entrypoint.ts` — `UmbEntryPointOnInit` with console.log placeholder
- `src/entrypoints/manifest.ts` — registers the entrypoint

This story reconfigures the template scaffolding to match our architecture, NOT builds from scratch.

### Architecture Pattern: Entry Point Registration

The architecture specifies `index.ts` as the entry point using `UmbEntryPointOnInit`. The template uses a `bundle` type in `umbraco-package.json` which loads a JS file that exports a `manifests` array. These are two different patterns:

**Bundle pattern (template default):** `umbraco-package.json` → loads JS → reads exported `manifests` array → registers extensions declaratively.

**EntryPoint pattern (architecture spec):** `umbraco-package.json` → loads JS → calls `onInit(host, extensionRegistry)` → code programmatically registers extensions.

Both work. The template already uses the bundle pattern with an entrypoint manifest inside it. **Keep the bundle approach** — it's simpler and the template already has it working. The `index.ts` replaces `bundle.manifests.ts` as the entry, imports and re-exports the manifests array, and the entrypoint manifest within that array handles the `onInit` lifecycle hook.

**Simplified approach:** `index.ts` exports `manifests` (loaded by bundle registration) AND acts as the entry for Vite. Manifests include one `backofficeEntryPoint` that dynamically imports an `onInit` function for any runtime setup needed.

### Key Decisions

**Vite version — stay on 7.x:** Vite 8 replaces Rollup+esbuild with Rolldown (Rust-based bundler). Too new for production use in a package targeting Umbraco 17. Vite 7 is stable and well-tested with Lit.

**TypeScript version — stay on 5.x:** TypeScript 6 is available but `@umbraco-cms/backoffice` 17.2.2 targets TS 5.x. Mismatched TS versions can cause type resolution issues. Stay aligned.

**`@umbraco-cms/backoffice` — pin to exact 17.2.2:** The `latest` npm tag currently points to `17.3.0-rc3` (a release candidate). Pin to the exact version matching our Umbraco target. This is a devDependency only — it provides types and is externalised at build time.

**`lit` as devDependency only:** The Umbraco backoffice provides Lit via its import map at runtime. Bundling a second copy causes duplicate custom element registrations which breaks the app. `lit` is added as a devDependency (for type checking during development) and externalised in `vite.config.ts` alongside `@umbraco` imports.

### File Structure After This Story

```
Client/
  package.json              # Updated deps, scripts
  vite.config.ts            # Entry: src/index.ts, lib mode
  tsconfig.json             # ESNext target
  public/
    umbraco-package.json    # Bundle registration (unchanged)
  src/
    index.ts                # Entry — exports manifests array
    manifests.ts            # Extension manifest definitions (empty for now)
    components/             # Lit web components (Story 1.3+)
    contexts/               # Context providers (Epic 3+)
    api/                    # API client code (Epic 2+)
    utils/                  # Utility functions (Epic 6+)
```

### Lit Externalisation Detail

The Umbraco backoffice provides Lit via import maps. To avoid bundling a duplicate copy:

```typescript
// vite.config.ts
rollupOptions: {
  external: [/^@umbraco/, /^lit/],
}
```

This externalises both `@umbraco-cms/*` and `lit` / `lit/decorators.js` / `lit/directives/*` etc. At runtime, the Umbraco import map resolves these to the backoffice-provided versions.

### Frontend Component Naming (for future stories)

- Custom elements: `shallai-{name}` prefix
- Class names: `Shallai{Name}Element`
- File names: `shallai-{name}.element.ts`
- One component per file

### CSS Approach (for future stories)

- UUI design tokens only: `var(--uui-size-layout-1)`, `var(--uui-color-text)`
- Styles scoped via Shadow DOM (Lit default)
- No global stylesheets

### Project Structure Notes

- `Client/` is entirely independent — communicates only via HTTP API + SSE
- No shared types between C# and TypeScript (types defined independently)
- `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` is the compiled output included in the NuGet package
- `Client/` folder itself is excluded from the NuGet package (build output only)

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 1, Story 1.2]
- [Source: _bmad-output/planning-artifacts/architecture.md — Frontend Build Tooling, Package Structure, Frontend Patterns]
- [Source: _bmad-output/planning-artifacts/ux-design-specification.md — Design System Foundation, Component Mapping]
- [Source: _bmad-output/planning-artifacts/prd.md — Developer Experience Requirements]
- [Source: _bmad-output/implementation-artifacts/1-1-scaffold-rcl-package-project.md — Previous Story Intelligence]
- [Source: npm registry — vite 7.1.9, lit 3.3.2, @umbraco-cms/backoffice 17.2.2, @web/test-runner 0.20.2, @open-wc/testing 4.0.0]
- [Source: Umbraco Docs — Vite Package Setup, umbraco-package.json format]

### Previous Story Intelligence (1-1)

**Key learnings from Story 1.1:**
- Template name was `umbraco-extension`, NOT `umbracopackage-rcl` (docs were wrong)
- Template pins Umbraco PackageReferences correctly when `--version 17.2.2` is used
- `.NET 10 uses .slnx format` — functionally equivalent to .sln
- `ImplicitUsings` covers most System namespaces — only additive usings needed in GlobalUsings.cs
- Build warning NU1902 (MimeKit vulnerability) is transitive from Umbraco — not actionable
- Template scaffolded `Client/` with working Vite config — we're reconfiguring, not starting from scratch
- Review found: nullable string properties on options classes should be addressed when binding is wired up (deferred)

**Files created/modified in 1-1 relevant to this story:**
- `Client/` folder — exists with template defaults, this story reconfigures it
- `wwwroot/` — exists but empty, this story's build will populate `App_Plugins/`
- Solution builds with `dotnet build`, tests pass with `dotnet test` — must remain true after this story

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

### Completion Notes List

- Reconfigured template-scaffolded Client/ to match project architecture
- Removed OpenAPI deps (`@hey-api/openapi-ts`, `chalk`, `node-fetch`, `cross-env`), removed `generate-client` script
- Added `lit` (^3.3.2), pinned `@umbraco-cms/backoffice` to exact 17.2.2 (no caret), added `@web/test-runner` + `@open-wc/testing`
- Added `@web/dev-server-esbuild` — required for `@web/test-runner` to handle TypeScript files (esbuild transforms `.ts` before browser execution)
- Created `web-test-runner.config.mjs` with esbuild plugin configuration
- Created placeholder test `src/utils/placeholder.test.ts` — `@web/test-runner` errors on empty glob, so one test ensures AC #8 ("execute successfully even with zero tests") is met
- Added `exclude: ["src/**/*.test.ts"]` to `tsconfig.json` — test files use mocha globals (`describe`/`it`) which fail tsc without `@types/mocha`. Excluding keeps build clean; esbuild handles test compilation separately
- Updated vite.config.ts: entry → `src/index.ts`, externals include `/^lit/`, added `publicDir: "public"`
- Updated tsconfig.json: target/lib → ESNext
- Replaced `bundle.manifests.ts` + `entrypoints/` with `index.ts` + `manifests.ts` (bundle pattern preserved)
- Created empty `components/`, `contexts/`, `api/` directories with `.gitkeep`
- Deleted empty `scripts/` directory
- Kept `.vscode/` — contains `lit-plugin` extension recommendation (useful, not template-only)
- `umbraco-package.json` unchanged — bundle path already correct
- All verification passed: npm install, npm run build, npm test (1 passed), npm run watch, dotnet build, dotnet test (2 passed)
- Build output: `shallai-umbraco-agent-runner.js` (0.10 kB), sourcemap, and umbraco-package.json all in `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/`
- JS bundle contains no `@umbraco` or `lit` imports — correctly externalised

### File List

- Shallai.UmbracoAgentRunner/Client/package.json (modified)
- Shallai.UmbracoAgentRunner/Client/package-lock.json (new — generated by npm install)
- Shallai.UmbracoAgentRunner/Client/vite.config.ts (modified)
- Shallai.UmbracoAgentRunner/Client/tsconfig.json (modified)
- Shallai.UmbracoAgentRunner/Client/web-test-runner.config.mjs (new)
- Shallai.UmbracoAgentRunner/Client/src/index.ts (new)
- Shallai.UmbracoAgentRunner/Client/src/manifests.ts (new)
- Shallai.UmbracoAgentRunner/Client/src/components/.gitkeep (new)
- Shallai.UmbracoAgentRunner/Client/src/contexts/.gitkeep (new)
- Shallai.UmbracoAgentRunner/Client/src/api/.gitkeep (new)
- Shallai.UmbracoAgentRunner/Client/src/utils/placeholder.test.ts (new)
- Shallai.UmbracoAgentRunner/Client/src/bundle.manifests.ts (deleted)
- Shallai.UmbracoAgentRunner/Client/src/entrypoints/entrypoint.ts (deleted)
- Shallai.UmbracoAgentRunner/Client/src/entrypoints/manifest.ts (deleted)
- Shallai.UmbracoAgentRunner/Client/scripts/ (deleted — empty directory)
- Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/shallai-umbraco-agent-runner.js (new — build output)
- Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/shallai-umbraco-agent-runner.js.map (new — build output)
- Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/umbraco-package.json (new — copied from public/)
