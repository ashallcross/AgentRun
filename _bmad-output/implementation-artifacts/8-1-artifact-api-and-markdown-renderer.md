# Story 8.1: Artifact API & Markdown Renderer

Status: done

## Story

As a developer or editor,
I want to retrieve artifact content via API and render markdown safely in the dashboard,
So that agent-produced documents are displayed as polished, readable content without security risks.

## Context

**UX Mode: Both interactive and autonomous.**

This is the first story of Epic 8 (Artifact Browsing & Review). It builds the backend API endpoint for serving artifact files and the frontend markdown renderer component. Story 8.2 will consume both to build the artifact viewer UI integrated into the instance detail view.

**What already exists:**
- Artifacts are written to disk by `WriteFileTool` into `{instanceFolder}/artifacts/` subdirectory (path like `artifacts/review-notes.md`)
- `WritesTo` arrays in workflow YAML (and `StepResponse.WritesTo` in the API) contain relative paths from the instance folder (e.g., `artifacts/review-notes.md`)
- `IInstanceManager.GetInstanceFolderPath(workflowAlias, instanceId)` returns the disk path for an instance
- `IInstanceManager.FindInstanceAsync(instanceId)` resolves an instance without requiring the workflow alias
- `PathSandbox.ValidatePath(relativePath, rootPath)` already validates path traversal and symlinks
- `markdown-sanitiser.ts` already exists in `Client/src/utils/` with full markdown-to-HTML conversion and HTML whitelist sanitisation (created in Story 6.4)
- `ConversationEndpoints.cs` provides the closest API endpoint pattern (GET with instance lookup + validation)

**What this story adds:**
- `ArtifactEndpoints.cs` — a new endpoint controller with `GET /instances/{id}/artifacts/{*filePath}` that serves artifact file content
- `shallai-markdown-renderer` — a Lit web component that wraps the existing `sanitiseMarkdown()` utility with UUI typography styling
- `getArtifact()` — a new API client function for fetching artifact content as raw text
- Backend and frontend tests

## Acceptance Criteria

1. **Given** a workflow instance has completed steps that produced artifact files
   **When** the `GET /umbraco/api/shallai/instances/{id}/artifacts/{*filePath}` endpoint is called
   **Then** it returns the artifact file content as a string
   **And** the response has `Content-Type: text/plain; charset=utf-8` (raw content — the frontend handles rendering)
   **And** the endpoint requires backoffice authentication (NFR13)

2. **Given** a filename with path traversal characters (`../`, encoded sequences, symlinks)
   **When** the artifact endpoint validates the filename
   **Then** it returns 400 Bad Request with error code `invalid_path`
   **And** the error message describes the violation without leaking server paths

3. **Given** a valid filename that does not exist on disk
   **When** the artifact endpoint processes the request
   **Then** it returns 404 Not Found with error code `artifact_not_found`

4. **Given** an instance ID that does not exist
   **When** the artifact endpoint is called
   **Then** it returns 404 with error code `instance_not_found`

5. **Given** raw markdown content from an artifact
   **When** the `shallai-markdown-renderer` component renders it
   **Then** the content is sanitised via the existing `sanitiseMarkdown()` utility before rendering
   **And** headings use UUI typography tokens (`--uui-font-size-*` for h1-h6)
   **And** paragraphs use `--uui-font-size-default` with `--uui-color-text`
   **And** tables use `--uui-color-border` for borders and `--uui-color-surface-emphasis` for header rows
   **And** code blocks use monospace font with `--uui-color-surface-emphasis` background and `--uui-size-space-3` padding
   **And** links use `--uui-color-interactive` and open in new tab (`target="_blank" rel="noopener"`)
   **And** the rendered content looks like a professional document — proportional font for body text, proper heading hierarchy

6. **Given** the API client
   **When** `getArtifact(instanceId, filePath)` is called
   **Then** it fetches `GET /instances/{id}/artifacts/{filePath}` and returns the raw text content
   **And** it URL-encodes each segment of the filePath (to handle `artifacts/review-notes.md` correctly)

7. **Given** all implementations
   **When** backend and frontend tests run
   **Then** all tests pass

## What NOT to Build

- Artifact viewer UI integration into instance detail (Story 8.2)
- Click-to-view step artifacts in sidebar (Story 8.2)
- Auto-activation of artifact viewer on workflow completion (Story 8.2)
- Step-level artifact browsing or file listing endpoint (not needed — `StepResponse.WritesTo` already provides the filename list)
- Content-Type sniffing or non-markdown rendering (all artifacts are treated as markdown text)
- Server-side markdown rendering (client-side only)
- Download functionality for artifacts

## Failure & Edge Cases

- **Path traversal via encoded characters** — URL-decoded filenames like `%2e%2e%2f` must be caught. `PathSandbox.ValidatePath` handles this after `Path.GetFullPath()` canonicalises.
- **Symlink escape** — `PathSandbox.ValidatePath` detects symlinks via `FileAttributes.ReparsePoint`. Endpoint must catch `UnauthorizedAccessException` and return 400.
- **Empty artifact file** — valid case, return 200 with empty body.
- **Binary files** — not expected (agents write text), but if hit, return raw bytes with `text/plain`. No special handling.
- **Very large files** — no streaming needed for v1. Artifacts are agent-written text documents, typically < 100KB.
- **Concurrent file write during read** — possible if agent is still writing. Acceptable for v1 — the read will return whatever state the file is in (atomic writes via .tmp+move mean the file is always complete or absent).
- **Filename with spaces or special characters** — URL encoding in the API client handles this. Backend uses the raw decoded path.
- **Wildcard route `{*filePath}`** — required because artifact paths contain slashes (e.g., `artifacts/review-notes.md`). ASP.NET catch-all parameter captures the full remaining path.

## Tasks / Subtasks

- [x] Task 1: Create `ArtifactEndpoints.cs` (AC: #1, #2, #3, #4)
  - [x] 1.1 New controller class in `Endpoints/` with route prefix `umbraco/api/shallai`, `[Authorize]` attribute
  - [x] 1.2 Inject `IInstanceManager` — no other dependencies needed
  - [x] 1.3 `GET instances/{id}/artifacts/{*filePath}` endpoint:
    - Find instance via `FindInstanceAsync(id)` → 404 if null
    - Get instance folder via `GetInstanceFolderPath(workflowAlias, instanceId)`
    - Validate filePath using `PathSandbox.ValidatePath(filePath, instanceFolderPath)` — catch `ArgumentException`/`UnauthorizedAccessException` → 400
    - Check `File.Exists(canonicalPath)` → 404 if missing
    - Read file with `File.ReadAllTextAsync(canonicalPath)` → return `Content(text, "text/plain; charset=utf-8")`
  - [x] 1.4 Error responses use `ErrorResponse` model with codes: `instance_not_found`, `invalid_path`, `artifact_not_found`

- [x] Task 2: Create `ArtifactEndpointsTests.cs` (AC: #7)
  - [x] 2.1 Test fixture with NSubstitute mock for `IInstanceManager`
  - [x] 2.2 Tests: valid artifact returns 200 with content, instance not found returns 404, path traversal returns 400, missing file returns 404, empty file returns 200 with empty string, valid nested path (`artifacts/review-notes.md`) works
  - [x] 2.3 Note: file I/O tests need real temp directory — use `Path.GetTempPath()` + cleanup in `[TearDown]`. Configure `GetInstanceFolderPath` mock to return the temp path.

- [x] Task 3: Create `shallai-markdown-renderer` component (AC: #5)
  - [x] 3.1 New file: `Client/src/components/shallai-markdown-renderer.element.ts`
  - [x] 3.2 `@customElement('shallai-markdown-renderer')`, extends `UmbLitElement`
  - [x] 3.3 `@property({ type: String }) content = ''` — raw markdown input
  - [x] 3.4 `render()`: call `sanitiseMarkdown(this.content)` and render result via `.innerHTML` on div (safe because sanitised) — `unsafeHTML` not used as `.innerHTML` pattern matches existing chat-message component
  - [x] 3.5 Import `sanitiseMarkdown` from `../utils/markdown-sanitiser.js`
  - [x] 3.6 Imports from `@umbraco-cms/backoffice/external/lit` — used `.innerHTML` binding instead of `unsafeHTML` (consistent with existing shallai-chat-message pattern)
  - [x] 3.7 Static `styles` with UUI token-based typography (see Dev Notes below for exact CSS)
  - [x] 3.8 Register in `Client/src/index.ts` via side-effect import

- [x] Task 4: Add `getArtifact()` to API client (AC: #6)
  - [x] 4.1 New `fetchText()` helper alongside existing `fetchJson()` — same auth pattern, but returns `response.text()` instead of `response.json()`
  - [x] 4.2 `getArtifact(instanceId, filePath, token?)`: calls `fetchText(`/instances/${encodeURIComponent(instanceId)}/artifacts/${encodeArtifactPath(filePath)}`)`
  - [x] 4.3 `encodeArtifactPath(path)`: split on `/`, `encodeURIComponent` each segment, rejoin with `/` — preserves path structure while encoding special characters

- [x] Task 5: Frontend tests (AC: #7)
  - [x] 5.1 Markdown renderer test: verify `sanitiseMarkdown` renders headings, bold/italic, code blocks, links, blockquotes; verify XSS sanitisation strips script tags
  - [x] 5.2 API client test: verify `getArtifact()` calls correct URL with encoded path segments, returns raw text content
  - [x] 5.3 Test `encodeArtifactPath` helper for paths with slashes, spaces, hash, question marks, and special characters

- [x] Task 6: Build frontend and verify (AC: #7)
  - [x] 6.1 Run `npm run build` in `Client/` to update `wwwroot/` output
  - [x] 6.2 Run `dotnet test Shallai.UmbracoAgentRunner.slnx` — all 328 tests pass
  - [x] 6.3 Run `npm test` in `Client/` — all 139 tests pass

## Dev Notes

### Endpoint Pattern — Follow ConversationEndpoints.cs Exactly

The artifact endpoint is structurally identical to `ConversationEndpoints.cs` (a GET endpoint that finds an instance, validates a parameter, reads data, returns it). Follow that pattern:
- Same controller base class (`ControllerBase`), same attributes, same route prefix
- Same `FindInstanceAsync` → 404 pattern
- `ErrorResponse` model for all error cases

### Catch-All Route Parameter

The filePath must be a catch-all (`{*filePath}`) because artifact paths contain slashes: `artifacts/review-notes.md`. Without the catch-all, ASP.NET would treat `artifacts/review-notes.md` as two separate route segments and 404.

Example: `GET /umbraco/api/shallai/instances/abc123/artifacts/artifacts/review-notes.md`

The double `artifacts/artifacts` looks odd but is correct — the first is the route segment, the second is part of the WritesTo path from the workflow YAML. This is how the data actually looks.

### Path Sandboxing — Reuse Existing Pattern from WriteFileTool

```csharp
try
{
    var canonicalPath = PathSandbox.ValidatePath(filePath, instanceFolderPath);
    // Use canonicalPath for file operations
}
catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
{
    return BadRequest(new ErrorResponse { Error = "invalid_path", Message = "The requested file path is not permitted." });
}
```

Do NOT expose the actual path or the specific validation failure reason in the error message — that's information leakage.

### Markdown Renderer — UUI Typography CSS

```css
:host {
  display: block;
  font-family: var(--uui-font-family);
  font-size: var(--uui-font-size-default);
  color: var(--uui-color-text);
  line-height: 1.6;
}
h1 { font-size: var(--uui-font-size-xxl); margin: 0 0 var(--uui-size-space-4); font-weight: 700; }
h2 { font-size: var(--uui-font-size-xl); margin: var(--uui-size-space-5) 0 var(--uui-size-space-3); font-weight: 700; }
h3 { font-size: var(--uui-font-size-l); margin: var(--uui-size-space-4) 0 var(--uui-size-space-2); font-weight: 600; }
h4 { font-size: var(--uui-font-size-m); margin: var(--uui-size-space-3) 0 var(--uui-size-space-2); font-weight: 600; }
h5, h6 { font-size: var(--uui-font-size-s); margin: var(--uui-size-space-2) 0 var(--uui-size-space-1); font-weight: 600; }
p { margin: 0 0 var(--uui-size-space-3); }
a { color: var(--uui-color-interactive); text-decoration: none; }
a:hover { text-decoration: underline; }
ul, ol { margin: 0 0 var(--uui-size-space-3); padding-left: var(--uui-size-space-5); }
li { margin-bottom: var(--uui-size-space-1); }
pre {
  background: var(--uui-color-surface-emphasis);
  padding: var(--uui-size-space-3);
  border-radius: var(--uui-border-radius);
  overflow-x: auto;
  margin: 0 0 var(--uui-size-space-3);
}
pre code { font-family: monospace; font-size: var(--uui-font-size-s); background: none; padding: 0; }
code { font-family: monospace; font-size: 0.9em; background: var(--uui-color-surface-emphasis); padding: 2px 6px; border-radius: 3px; }
blockquote {
  border-left: 3px solid var(--uui-color-border);
  margin: 0 0 var(--uui-size-space-3);
  padding: var(--uui-size-space-2) var(--uui-size-space-3);
  color: var(--uui-color-text-alt);
}
table { width: 100%; border-collapse: collapse; margin: 0 0 var(--uui-size-space-3); }
th, td { border: 1px solid var(--uui-color-border); padding: var(--uui-size-space-2) var(--uui-size-space-3); text-align: left; }
th { background: var(--uui-color-surface-emphasis); font-weight: 600; }
hr { border: none; border-top: 1px solid var(--uui-color-border); margin: var(--uui-size-space-4) 0; }
strong { font-weight: 700; }
```

### unsafeHTML Import

The `unsafeHTML` directive is available from `@umbraco-cms/backoffice/external/lit`. Do NOT import from bare `lit/directives/unsafe-html.js` — Bellissima's import map doesn't include it.

```typescript
import { LitElement, html, css, unsafeHTML } from '@umbraco-cms/backoffice/external/lit';
```

Verify `unsafeHTML` is actually re-exported. If not, fall back to setting `innerHTML` in `updated()` lifecycle method on a container div. This is functionally equivalent — the content is already sanitised.

### API Client — fetchText Pattern

```typescript
async function fetchText(path: string, token?: string): Promise<string> {
  const url = `${API_BASE}${path}`;
  const headers: HeadersInit = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }
  const response = await fetch(url, { headers, credentials: "same-origin" });
  if (!response.ok) {
    throw new Error(`API error: ${response.status}`);
  }
  return response.text();
}
```

### File I/O Tests — Real Filesystem Required

Backend tests for artifact serving need real files on disk because the endpoint reads `File.ReadAllTextAsync`. Create a temp directory in `[SetUp]`, write test files, configure `GetInstanceFolderPath` mock to return it, clean up in `[TearDown]`.

```csharp
private string _tempDir = null!;

[SetUp]
public void SetUp()
{
    _tempDir = Path.Combine(Path.GetTempPath(), $"artifact-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
    _instanceManager.GetInstanceFolderPath("test-wf", "inst-001").Returns(_tempDir);
}

[TearDown]
public void TearDown()
{
    if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
}
```

For path traversal tests, the endpoint catches the exception from `PathSandbox.ValidatePath` — these don't need real files, just a valid `_tempDir` as root.

### Existing markdown-sanitiser.ts — Already Complete

The `markdown-sanitiser.ts` at `Client/src/utils/markdown-sanitiser.ts` already handles:
- Fenced code blocks, headings, blockquotes, lists, paragraphs, horizontal rules
- Inline: bold, italic, inline code, links (http-only)
- HTML whitelist sanitisation (ALLOWED_TAGS: p, h1-h6, ul, ol, li, a, strong, em, code, pre, blockquote, hr, br, table, tr, td, th, thead, tbody)
- Link validation (only http/https), target="_blank" rel="noopener"

**Do NOT recreate or modify this file.** The renderer component wraps it.

Known limitation (from deferred work): pipe-table markdown (`| col | col |`) is not parsed — only raw HTML tables pass through. This is acceptable per epic spec.

### Project Structure Notes

New files:
- `Shallai.UmbracoAgentRunner/Endpoints/ArtifactEndpoints.cs`
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/ArtifactEndpointsTests.cs`
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-markdown-renderer.element.ts`

Modified files:
- `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` (add `fetchText`, `getArtifact`, `encodeArtifactPath`)
- `Shallai.UmbracoAgentRunner/Client/src/index.ts` (add side-effect import for renderer)

No DI registration changes needed — `ArtifactEndpoints` is a controller, auto-discovered by ASP.NET.

### Manual E2E Validation

**Deferred to Story 8.2** — Umbraco Bellissima's BFF auth pattern requires a Bearer JWT that Chrome redacts in dev tools, making direct API testing impractical. The endpoint follows the identical pattern as ConversationEndpoints (proven in production). Story 8.2 wires `getArtifact()` into the artifact viewer UI, providing a natural E2E validation path through the backoffice.

### References

- [ArtifactEndpoints architecture: architecture.md line 289, 602](Source: _bmad-output/planning-artifacts/architecture.md)
- [Epic 8.1 acceptance criteria: epics.md lines 917-942](Source: _bmad-output/planning-artifacts/epics.md)
- [Path sandboxing pattern: PathSandbox.cs](Source: Shallai.UmbracoAgentRunner/Security/PathSandbox.cs)
- [Endpoint pattern: ConversationEndpoints.cs](Source: Shallai.UmbracoAgentRunner/Endpoints/ConversationEndpoints.cs)
- [Test pattern: ConversationEndpointsTests.cs](Source: Shallai.UmbracoAgentRunner.Tests/Endpoints/ConversationEndpointsTests.cs)
- [Existing sanitiser: markdown-sanitiser.ts](Source: Shallai.UmbracoAgentRunner/Client/src/utils/markdown-sanitiser.ts)
- [WriteFileTool sandbox pattern: WriteFileTool.cs](Source: Shallai.UmbracoAgentRunner/Tools/WriteFileTool.cs)
- [UX spec: markdown renderer styling](Source: _bmad-output/planning-artifacts/ux-design-specification.md lines 801-823)
- [Workflow YAML with WritesTo paths](Source: Shallai.UmbracoAgentRunner.TestSite/App_Data/.../content-review/workflow.yaml)

### Review Findings

- [x] [Review][Defer] Symlink escape test coverage — no endpoint-level test for symlink traversal returning 400. PathSandbox handles internally; creating symlinks in unit tests is platform-dependent. [ArtifactEndpointsTests.cs] — deferred, pre-existing
- [x] [Review][Defer] Binary file handling with ReadAllTextAsync — spec says "return raw bytes with text/plain" for binary files, but `ReadAllTextAsync` may corrupt invalid UTF-8 sequences. Binary artifacts not expected in v1; acceptable limitation. [ArtifactEndpoints.cs:65] — deferred, v1 acceptable

## Change Log

- Story 8.1 implemented: Artifact API endpoint and markdown renderer component (Date: 2026-04-02)

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Task 2: Fixed `StepStatus.Completed` → `StepStatus.Complete` to match enum definition
- Task 3: Used `.innerHTML` binding instead of `unsafeHTML` directive — consistent with existing `shallai-chat-message` pattern and avoids import availability uncertainty
- Task 5: Renderer component test avoids importing `UmbLitElement` (not resolvable in web-test-runner without Umbraco backoffice context); tests `sanitiseMarkdown` integration directly

### Completion Notes List

- **Task 1**: Created `ArtifactEndpoints.cs` with GET endpoint, path sandboxing, instance lookup, and error responses matching ConversationEndpoints pattern exactly
- **Task 2**: Created 7 backend tests covering: valid file (200), instance not found (404), path traversal (400), missing file (404), empty file (200), nested path, and empty filePath (400). All use real temp directory with cleanup.
- **Task 3**: Created `shallai-markdown-renderer` Lit web component with UUI typography CSS tokens, `sanitiseMarkdown` integration, and side-effect import registration in `index.ts`
- **Task 4**: Added `fetchText()` helper, `encodeArtifactPath()` path encoder, and `getArtifact()` API function to `api-client.ts`
- **Task 5**: Added 9 markdown renderer tests (sanitisation, XSS, rendering), 6 `encodeArtifactPath` tests, and 4 `getArtifact` tests — 19 new frontend tests total
- **Task 6**: Frontend build succeeds (15 modules), all 328 backend tests pass, all 139 frontend tests pass (0 failures)

### File List

New files:
- `Shallai.UmbracoAgentRunner/Endpoints/ArtifactEndpoints.cs`
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/ArtifactEndpointsTests.cs`
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-markdown-renderer.element.ts`
- `Shallai.UmbracoAgentRunner/Client/src/components/shallai-markdown-renderer.element.test.ts`

Modified files:
- `Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts` (added `fetchText`, `getArtifact`, `encodeArtifactPath`)
- `Shallai.UmbracoAgentRunner/Client/src/api/api-client.test.ts` (added `encodeArtifactPath` and `getArtifact` tests)
- `Shallai.UmbracoAgentRunner/Client/src/index.ts` (added side-effect import for markdown renderer)
- `Shallai.UmbracoAgentRunner/Client/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/*` (rebuilt frontend output)
