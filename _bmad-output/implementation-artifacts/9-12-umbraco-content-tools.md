# Story 9.12: Umbraco Content Tools — In-Process Content & Schema Access

Status: done

**Depends on:** None (no code conflicts with any in-progress story)
**Blocks:** 9.13 (Umbraco Content Audit Workflow), 9.14 (Documentation Update), 9.5 (Private Beta Distribution)

> BETA BLOCKER — product credibility. AgentRun.Umbraco ships as an Umbraco package but currently has zero connection to the Umbraco instance it's installed in. All existing workflows scan external URLs via `fetch_url` — something any generic AI tool does. This story builds three in-process content tools (`list_content`, `get_content`, `list_content_types`) that give workflow agents direct access to published CMS content. This is the Umbraco connection that justifies the package name.

## Story

As a workflow author,
I want tools that can read content, content types, and media metadata from the Umbraco instance this package is installed in,
So that I can build workflows that audit, analyse, and report on actual CMS content rather than external URLs.

## Context

**Key architectural insight:** AgentRun runs in-process inside the Umbraco host. It has direct access to Umbraco's DI container — `IPublishedContentCache`, `IContentTypeService`. No HTTP calls, no OAuth2 tokens, no Management API pagination. The tools inject Umbraco services directly and return structured JSON results. This is an enormous advantage over external tools like the Umbraco MCP Server.

**Engine boundary (FR23/NFR25):** These tools live in `Tools/`, not `Engine/`. The engine calls `IWorkflowTool.ExecuteAsync()` and doesn't know or care that Umbraco services are behind it. The "Engine has zero Umbraco dependencies" rule holds. The runner stays workflow-generic.

**UX Mode: N/A — engine tool layer. No UI work.**

## Acceptance Criteria

**AC1 — `list_content` returns published content inventory**
Given the Umbraco instance has published content nodes,
When a workflow step calls `list_content` with no parameters,
Then the tool returns a JSON array of all published content nodes, each containing: `id` (int), `name` (string), `contentType` (document type alias), `url` (string), `level` (int), `createDate` (ISO 8601), `updateDate` (ISO 8601), `creatorName` (string), `childCount` (int).

**AC2 — `list_content` supports `contentType` filter**
Given the Umbraco instance has published content of various document types,
When a workflow step calls `list_content` with `contentType: "blogPost"`,
Then only content nodes of document type alias `blogPost` are returned.

**AC3 — `list_content` supports `parentId` subtree filter**
Given the Umbraco instance has a content tree with parent-child relationships,
When a workflow step calls `list_content` with `parentId: 1234`,
Then only direct children of the node with ID 1234 are returned.

**AC4 — `list_content` truncates at size limit**
Given the serialised result exceeds the configurable max response bytes limit,
When the tool serialises the result,
Then it truncates with a marker: `[Response truncated — returned {count} of {total} content nodes. Use contentType or parentId filters to narrow results.]`
And the truncation respects the `IToolLimitResolver` chain (step override -> workflow default -> site ceiling -> engine default).

**AC5 — `get_content` returns full property values for a single node**
Given a published content node with ID 5678,
When a workflow step calls `get_content` with `id: 5678`,
Then the tool returns a JSON object containing: `id`, `name`, `contentType`, `url`, `level`, `createDate`, `updateDate`, `creatorName`, `templateAlias`, and a `properties` object where each key is the property alias and each value is the extracted text representation.

**AC6 — `get_content` extracts text from common property editors**
Given a content node with Rich Text, Text String, Textarea, Content Picker, Media Picker, and Block List properties,
When `get_content` is called for that node,
Then:
- Rich Text: HTML stripped, returns plain text
- Text String / Textarea: returned as-is
- Content Picker: returns referenced node name + URL (or "not published" if target is unpublished)
- Media Picker: returns media name + URL + alt text
- Block List / Block Grid: returns a simplified text representation of block content labels and settings
- Unknown editor: returns `JsonSerializer.Serialize(value)` as fallback

**AC7 — `get_content` respects size guard**
Given a content node whose serialised properties exceed the configurable max response bytes,
When the tool serialises the result,
Then properties are truncated with a marker (same pattern as `read_file` Story 9.9),
And the truncation limit is resolved via `IToolLimitResolver`.

**AC8 — `get_content` returns error for unknown node**
Given no published content node exists with ID 9999,
When a workflow step calls `get_content` with `id: 9999`,
Then the tool returns a tool error: `"Content node with ID 9999 not found or is not published"`.

**AC9 — `list_content_types` returns document type definitions**
Given the Umbraco instance has document type definitions,
When a workflow step calls `list_content_types`,
Then the tool returns a JSON array of document types, each containing: `alias` (string), `name` (string), `description` (string), `icon` (string), `properties` (array of `{alias, name, editorAlias, mandatory}`), `compositions` (array of aliases), `allowedChildTypes` (array of aliases).

**AC10 — `list_content_types` supports `alias` filter**
Given the Umbraco instance has multiple document types,
When a workflow step calls `list_content_types` with `alias: "blogPost"`,
Then only the document type with alias `blogPost` is returned.

**AC11 — All three tools registered in DI and WorkflowValidator**
Given the application starts,
When `AgentRunComposer` runs,
Then `ListContentTool`, `GetContentTool`, and `ListContentTypesTool` are registered as `IWorkflowTool` singletons,
And `WorkflowValidator.AllowedToolSettings` includes entries for `list_content`, `get_content`, and `list_content_types`.

**AC12 — Tools reject unrecognised parameters**
Given a workflow step calls `list_content` with an unrecognised parameter (e.g. `foo: "bar"`),
When the tool validates arguments,
Then it throws `ToolExecutionException` with a message naming the unrecognised parameter (deny-by-default).

**AC13 — Empty content tree handled gracefully**
Given the Umbraco instance has no published content,
When a workflow step calls `list_content`,
Then the tool returns an empty JSON array `[]`, no error.

**AC14 — Published content only**
Given a content node exists in draft but is not published,
When a workflow step calls `list_content` or `get_content`,
Then the unpublished node is NOT included in results (published snapshot via `IPublishedContentCache` only).

## Implementation Notes

### Tools to Implement

| Tool Name | Primary Umbraco Service | Settings |
|-----------|------------------------|----------|
| `list_content` | `IPublishedContentCache` (via `IUmbracoContextAccessor`) | `max_response_bytes` |
| `get_content` | `IPublishedContentCache` (via `IUmbracoContextAccessor`) | `max_response_bytes` |
| `list_content_types` | `IContentTypeService` | `max_response_bytes` |

### Accessing IPublishedContentCache in a Singleton Tool

**Critical:** `IPublishedContentCache` is NOT directly injectable as a singleton because it depends on `IUmbracoContextAccessor` which is scoped. The tools run inside `IHostedService` (background thread) where there is no Umbraco context by default.

**Pattern to use:** Inject `IUmbracoContextFactory` and create a temporary context per tool execution:

```csharp
private readonly IUmbracoContextFactory _umbracoContextFactory;

public async Task<object> ExecuteAsync(...)
{
    using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();
    var contentCache = contextReference.UmbracoContext.Content;
    // ... use contentCache
}
```

This is the standard Umbraco pattern for accessing published content from background services. `EnsureUmbracoContext()` returns a reference-counted `UmbracoContextReference` that is disposed at the end of the tool call. The `IUmbracoContextFactory` is a singleton and safe to inject.

`IContentTypeService` (used by `list_content_types`) is a singleton service and can be injected directly — no context factory needed.

### Parameter Schemas

**`list_content`:**
```json
{
  "type": "object",
  "properties": {
    "contentType": {
      "type": "string",
      "description": "Filter by document type alias (e.g. 'blogPost'). Returns all types if omitted."
    },
    "parentId": {
      "type": "integer",
      "description": "Filter to direct children of this content node ID. Returns all content if omitted."
    }
  },
  "additionalProperties": false
}
```

**`get_content`:**
```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer",
      "description": "The ID of the published content node to retrieve."
    }
  },
  "required": ["id"],
  "additionalProperties": false
}
```

**`list_content_types`:**
```json
{
  "type": "object",
  "properties": {
    "alias": {
      "type": "string",
      "description": "Filter by document type alias. Returns all document types if omitted."
    }
  },
  "additionalProperties": false
}
```

### Size Guard Pattern (matches Story 9.9 `read_file`)

All three tools must implement result-size truncation:

1. Serialise result to JSON string via `JsonSerializer.Serialize()`
2. Check byte length against limit from `IToolLimitResolver`
3. If over limit: for `list_content` — remove items from the end of the array until under limit, append truncation marker with count of returned vs total. For `get_content` — truncate the serialised string at the byte limit and append marker. For `list_content_types` — same as `list_content`.
4. Resolution chain: step `tool_overrides.{tool}.max_response_bytes` -> workflow `tool_defaults.{tool}.max_response_bytes` -> site ceiling -> `EngineDefaults` constant.

**New engine default constant:** `GetContentMaxResponseBytes = 262_144` (256 KB, same as `read_file`). Use the same default for all three tools — they share the `max_response_bytes` setting name.

### IToolLimitResolver Changes

Add three new methods to `IToolLimitResolver`:

```csharp
int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);
int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);
int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow);
```

Add corresponding implementations in `ToolLimitResolver` following the existing `ResolveReadFileMaxResponseBytes` pattern (step override -> workflow default -> site ceiling -> engine default).

### WorkflowValidator Changes

Add entries to `AllowedToolSettings` dictionary:

```csharp
["list_content"] = new(StringComparer.Ordinal) { "max_response_bytes" },
["get_content"] = new(StringComparer.Ordinal) { "max_response_bytes" },
["list_content_types"] = new(StringComparer.Ordinal) { "max_response_bytes" },
```

### Property Value Extraction (get_content)

Implement a helper method or class for extracting readable text from Umbraco property values. The published content model surfaces values via `IPublishedContent.Value<T>(alias)` or the raw `IPublishedProperty.GetSourceValue()`.

**Strategy per editor alias:**

| Editor Alias | Extraction | Notes |
|-------------|-----------|-------|
| `Umbraco.RichText` | `Value<IHtmlEncodedString>()` -> strip HTML tags, return plain text | Use a simple regex or `HtmlDocument` strip; do NOT add AngleSharp dependency (already in FetchUrlTool but property extraction should be lightweight) |
| `Umbraco.TextBox` / `Umbraco.TextArea` | `Value<string>()` | Direct |
| `Umbraco.ContentPicker` | `Value<IPublishedContent>()` -> `{Name} ({Url()})` | Handle null (unpublished target) |
| `Umbraco.MediaPicker3` | `Value<IPublishedContent>()` or `Value<IEnumerable<IPublishedContent>>()` -> `{Name} ({Url()}) alt="{Value("altText")}"` | Multiple items possible |
| `Umbraco.BlockList` / `Umbraco.BlockGrid` | `Value<BlockListModel>()` / `Value<BlockGridModel>()` -> extract content type alias + label from each block's content | Simplified representation; do not recurse into nested blocks deeper than 1 level |
| `Umbraco.TrueFalse` | `Value<bool>()` -> `"true"` / `"false"` | |
| `Umbraco.Integer` | `Value<int>()` -> `.ToString()` | |
| `Umbraco.Decimal` | `Value<decimal>()` -> `.ToString()` | |
| `Umbraco.DateTime` | `Value<DateTime>()` -> ISO 8601 | |
| `Umbraco.Tags` | `Value<IEnumerable<string>>()` -> comma-joined | |
| **Fallback** | `property.GetSourceValue()` -> `JsonSerializer.Serialize(value)` | Catch exceptions, return `"[extraction failed]"` with logged warning |

**Important:** Wrap each property extraction in try/catch. If a value converter throws, log a warning and fall back to `JsonSerializer.Serialize(GetSourceValue())`. Never let one bad property kill the entire tool call.

### Engine Wiring Guard

All three tools must check `context.Step` and `context.Workflow` are set (Story 9.9 D2 pattern):

```csharp
if (context.Step is null || context.Workflow is null)
    throw new ToolContextMissingException("list_content requires Step and Workflow on ToolExecutionContext");
```

### Argument Validation

Use the `ExtractStringArgument` / `ExtractIntArgument` helper pattern from existing tools. For optional parameters, check if the key exists in the arguments dictionary before extracting. For `additionalProperties: false`, validate that no unrecognised keys are present — throw `ToolExecutionException` listing the unknown key.

**Unrecognised parameter rejection pattern:**
```csharp
var known = new HashSet<string>(StringComparer.Ordinal) { "contentType", "parentId" };
var unknown = arguments.Keys.Where(k => !known.Contains(k)).ToList();
if (unknown.Count > 0)
    throw new ToolExecutionException($"Unrecognised parameter(s): {string.Join(", ", unknown)}");
```

### Files to Create

| File | Purpose |
|------|---------|
| `AgentRun.Umbraco/Tools/ListContentTool.cs` | `list_content` tool implementation |
| `AgentRun.Umbraco/Tools/GetContentTool.cs` | `get_content` tool implementation |
| `AgentRun.Umbraco/Tools/ListContentTypesTool.cs` | `list_content_types` tool implementation |
| `AgentRun.Umbraco.Tests/Tools/ListContentToolTests.cs` | Unit tests for `list_content` |
| `AgentRun.Umbraco.Tests/Tools/GetContentToolTests.cs` | Unit tests for `get_content` |
| `AgentRun.Umbraco.Tests/Tools/ListContentTypesToolTests.cs` | Unit tests for `list_content_types` |

### Files to Modify

| File | Change |
|------|--------|
| `AgentRun.Umbraco/Composers/AgentRunComposer.cs` | Add DI registration for 3 new tools |
| `AgentRun.Umbraco/Workflows/WorkflowValidator.cs` | Add 3 entries to `AllowedToolSettings` |
| `AgentRun.Umbraco/Engine/IToolLimitResolver.cs` | Add 3 new resolver methods |
| `AgentRun.Umbraco/Engine/ToolLimitResolver.cs` | Implement 3 new resolver methods |
| `AgentRun.Umbraco/Engine/EngineDefaults.cs` | Add `ListContentMaxResponseBytes`, `GetContentMaxResponseBytes`, `ListContentTypesMaxResponseBytes` constants (all 262_144) |
| `AgentRun.Umbraco.Tests/Tools/FakeToolLimitResolver` (shared fixture or inline) | Add stub implementations for new resolver methods |

### DI Registration in AgentRunComposer

Add after the existing tool registrations:

```csharp
// Umbraco content tools (Story 9.12)
builder.Services.AddSingleton<IWorkflowTool, ListContentTool>();
builder.Services.AddSingleton<IWorkflowTool, GetContentTool>();
builder.Services.AddSingleton<IWorkflowTool, ListContentTypesTool>();
```

No special HttpClient or handler setup needed — these tools use in-process Umbraco services only.

## Failure & Edge Cases

- **Content tree is empty** -> `list_content` returns empty JSON array `[]`, no error
- **Content node ID not found** -> `get_content` returns tool error `"Content node with ID {id} not found or is not published"`
- **`contentType` filter matches no types** -> `list_content` returns empty array
- **`parentId` matches no node** -> `list_content` returns empty array (parent doesn't exist = no children)
- **`IUmbracoContextFactory.EnsureUmbracoContext()` fails** -> tool error with clear message `"Umbraco content cache is not available — the site may still be starting up"`
- **Property value converter throws** -> catch, return raw value as JSON string, log warning per-property
- **Very large site (10,000+ nodes)** -> `list_content` truncates at size limit with count of remaining nodes
- **Block List/Grid with deeply nested blocks** -> extract only top-level block labels, do not recurse
- **Content node has no properties** -> `get_content` returns empty `properties` object `{}`
- **`alias` filter on `list_content_types` matches nothing** -> return empty array
- **Deny-by-default:** unrecognised parameters rejected with `ToolExecutionException`
- **Null/empty `id` on `get_content`** -> `ToolExecutionException` ("id is required")
- **Negative or zero `id`/`parentId`** -> `ToolExecutionException` ("id must be a positive integer")
- **`context.Step` or `context.Workflow` is null** -> `ToolContextMissingException` (engine wiring guard)

## What NOT to Build

- Do NOT build `list_media` — defer to a follow-on story when a workflow needs it
- Do NOT build write-back tools (`update_content`, `publish_content`) — read-only for v1
- Do NOT build Management API / HTTP-based equivalents — in-process only
- Do NOT build draft content access — published content only via `IPublishedContentCache`
- Do NOT add any Umbraco service dependencies to the `Engine/` folder
- Do NOT build content tree visualisation or navigation tools
- Do NOT add AngleSharp as a dependency to the property extraction — use simple regex for RTE HTML stripping (AngleSharp is already used by FetchUrlTool but property extraction should stay lightweight)
- Do NOT implement cursor-based pagination in any tool — single response with truncation
- Do NOT build property editor-specific write serialisation (e.g. converting text back to RTE HTML)

## Test Strategy

### Unit Tests — Mocking Strategy

The Umbraco services (`IPublishedContentCache`, `IContentTypeService`, `IUmbracoContextFactory`) should be mocked using NSubstitute. The tools are singletons but the Umbraco context is created per-call via the factory.

**Mock setup pattern for `IUmbracoContextFactory`:**
```csharp
var contextFactory = Substitute.For<IUmbracoContextFactory>();
var umbracoContext = Substitute.For<IUmbracoContext>();
var contentCache = Substitute.For<IPublishedContentCache>();

umbracoContext.Content.Returns(contentCache);
contextFactory.EnsureUmbracoContext()
    .Returns(new UmbracoContextReference(umbracoContext, false));
```

Note: `UmbracoContextReference` is a struct with a public constructor. The `false` parameter means "do not dispose the context" — appropriate for mocked scenarios. If `UmbracoContextReference` constructor is not directly usable in tests, wrap the context access behind a thin internal interface that can be substituted.

**Mock setup pattern for `IPublishedContent`:**
```csharp
var node = Substitute.For<IPublishedContent>();
node.Id.Returns(1234);
node.Name.Returns("Blog Post Title");
node.ContentType.Alias.Returns("blogPost");
node.Url().Returns("/blog/post-title/");
// ... etc
```

### Test Cases per Tool

**ListContentToolTests (~10 tests):**
- Empty content tree -> returns `[]`
- Multiple nodes -> returns all with correct fields
- `contentType` filter -> returns only matching type
- `contentType` filter no match -> returns `[]`
- `parentId` filter -> returns only direct children
- `parentId` no match -> returns `[]`
- Both filters combined -> both applied
- Truncation at size limit -> marker appended with counts
- Unrecognised parameter -> `ToolExecutionException`
- Engine wiring guard (null Step/Workflow) -> `ToolContextMissingException`

**GetContentToolTests (~12 tests):**
- Valid node -> returns full properties
- Node not found -> tool error
- Rich Text property -> HTML stripped to plain text
- Text String property -> returned as-is
- Content Picker property -> name + URL
- Media Picker property -> name + URL + alt
- Block List property -> simplified text representation
- Unknown editor -> JSON serialised fallback
- Property converter throws -> fallback with warning
- Size guard truncation -> marker appended
- Missing `id` parameter -> `ToolExecutionException`
- Engine wiring guard -> `ToolContextMissingException`

**ListContentTypesToolTests (~7 tests):**
- Returns all document types with correct fields
- `alias` filter -> returns single match
- `alias` filter no match -> returns `[]`
- Includes properties, compositions, allowed child types
- Truncation at size limit -> marker appended
- Unrecognised parameter -> `ToolExecutionException`
- Engine wiring guard -> `ToolContextMissingException`

**Estimated total: ~29 new tests.**

### Existing Test Updates

- `FakeToolLimitResolver` (used in ReadFileToolTests, FetchUrlToolTests) must be updated with stub implementations for the 3 new resolver methods. This is the shared test fake — add the new methods returning `EngineDefaults` values.

### Test Command

```bash
dotnet test AgentRun.Umbraco.slnx
```

## Manual E2E Validation

1. Start TestSite with the CQA workflow — verify both existing workflows still appear and run (no regression)
2. Create a test workflow YAML that declares `tools: [list_content, get_content, list_content_types, write_file]` with a simple agent that calls each tool
3. Run the test workflow — verify:
   - `list_content` returns the TestSite's published content tree
   - `list_content` with `contentType` filter returns only matching nodes
   - `get_content` for a known node returns properties with extracted text
   - `list_content_types` returns the TestSite's document type definitions
4. Check that property extraction works for at least: Rich Text, Text String, and Content Picker
5. Verify tool errors surface correctly in the chat panel (e.g. `get_content` with non-existent ID)

## Previous Story Intelligence

**From Story 9.11 (most recent):**
- Surgical pattern-matching changes applied cleanly. This project has strong conventions — follow them exactly.
- Code review caught: combined boolean check pattern (both validation methods always run so all errors surface), missing edge case test for empty/whitespace input.
- Test count at 491/491 before this story.

**From Story 9.9 (size guard pattern origin):**
- Single bounded-read path eliminates TOCTOU window.
- `ToolContextMissingException` (not `InvalidOperationException`) for engine wiring bugs — the error classifier recognises it.
- FakeToolLimitResolver is the shared test double pattern.

**From Story 9.6 (tool limit resolver origin):**
- Resolution chain is step -> workflow -> site -> engine default. Never hardcode limits.
- `AllowedToolSettings` dictionary in `WorkflowValidator` must be updated for new tools.
- `EngineDefaults` static class holds the fallback constants.

## Tasks/Subtasks

- [x] Add `ListContentMaxResponseBytes`, `GetContentMaxResponseBytes`, `ListContentTypesMaxResponseBytes` constants to `EngineDefaults.cs` (AC: #4, #7)
- [x] Add 3 new resolver methods to `IToolLimitResolver` interface (AC: #4, #7)
- [x] Implement 3 new resolver methods in `ToolLimitResolver` (AC: #4, #7)
- [x] Add 3 entries to `AllowedToolSettings` in `WorkflowValidator.cs` (AC: #11)
- [x] Implement `ListContentTool.cs` with `IUmbracoContextFactory` + `IDocumentNavigationQueryService` + `IPublishedUrlProvider` pattern (AC: #1, #2, #3, #4, #12, #13, #14)
- [x] Implement `GetContentTool.cs` with property extraction (AC: #5, #6, #7, #8, #12, #14)
- [x] Implement `ListContentTypesTool.cs` with `IContentTypeService` (AC: #9, #10, #12)
- [x] Register all 3 tools in `AgentRunComposer.cs` (AC: #11)
- [x] Update `FakeToolLimitResolver` in test projects with new method stubs
- [x] Write `ListContentToolTests.cs` (10 tests) (AC: #1, #2, #3, #4, #12, #13)
- [x] Write `GetContentToolTests.cs` (12 tests) (AC: #5, #6, #7, #8, #12)
- [x] Write `ListContentTypesToolTests.cs` (7 tests) (AC: #9, #10, #12) — added extra test for properties/compositions
- [x] All tests pass: `dotnet test AgentRun.Umbraco.slnx` — 521/521
- [ ] Manual E2E steps 1-5 verified by Adam

### Review Findings

- [x] [Review][Patch] templateAlias always empty — both ternary branches returned string.Empty [GetContentTool.cs:107] — fixed: ResolveTemplateAlias via IFileService
- [x] [Review][Patch] GetContentTool truncation produced broken JSON via byte-slicing [GetContentTool.cs:120-125] — fixed: property-removal loop preserves valid JSON
- [x] [Review][Patch] EnforceCeilings missing for list_content/get_content/list_content_types [WorkflowValidator.cs:347-408] — fixed: added 6 EnforceField calls (3 workflow + 3 step)
- [x] [Review][Patch] long/double to int cast overflow in argument extractors [ListContentTool.cs:281, GetContentTool.cs:361] — fixed: range checks before cast
- [x] [Review][Patch] RichText extraction failed on IHtmlEncodedString [GetContentTool.cs:188-195] — fixed: check for IHtmlEncodedString, call ToHtmlString()
- [x] [Review][Patch] Block List/Grid returned aliases only, not text representation [GetContentTool.cs:246-266] — fixed: extract string-valued properties from block content
- [x] [Review][Patch] list_content_types returned only direct properties, not inherited [ListContentTypesTool.cs:78] — fixed: PropertyTypes → CompositionPropertyTypes
- [x] [Review][Defer] cancellationToken never used in any tool — pre-existing pattern across all tools, not introduced by this story
- [x] [Review][Defer] Duplicated helper methods across 3 tools (RejectUnknownParameters, ExtractOptional*) — real DRY violation, refactor candidate for 10-7
- [x] [Review][Defer] O(n²) truncation loop on large result sets — only triggers when result exceeds 256 KB, unlikely in beta
- [x] [Review][Defer] BFS loads full content tree before truncation — large-site performance, not a beta blocker

## Project Structure Notes

- New tool files follow existing pattern: `AgentRun.Umbraco/Tools/{ToolName}Tool.cs`
- Test files mirror: `AgentRun.Umbraco.Tests/Tools/{ToolName}ToolTests.cs`
- No new folders created — all files land in existing `Tools/` directories
- Namespace: `AgentRun.Umbraco.Tools` (follows folder structure convention)
- Test namespace: `AgentRun.Umbraco.Tests.Tools`

## References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 9.12] — epic entry with full tool specifications
- [Source: _bmad-output/planning-artifacts/architecture.md#Decision 3] — Tool System Interface
- [Source: _bmad-output/planning-artifacts/architecture.md#Decision 7] — Security Architecture
- [Source: _bmad-output/planning-artifacts/prd.md#FR23] — Pluggable tool interface
- [Source: _bmad-output/planning-artifacts/prd.md#NFR25] — Tool interface extensibility
- [Source: project-context.md#C# Rules] — nullable refs, async patterns, DI via IComposer
- [Source: 9-11-fail-registration-on-missing-agent-files.md] — most recent story patterns
- [Source: AgentRun.Umbraco/Tools/ReadFileTool.cs] — size guard reference implementation
- [Source: AgentRun.Umbraco/Tools/FetchUrlTool.cs] — tool implementation reference
- [Source: AgentRun.Umbraco/Engine/EngineDefaults.cs] — constants pattern
- [Source: AgentRun.Umbraco/Workflows/WorkflowValidator.cs:32-37] — AllowedToolSettings pattern

## Definition of Done

- [x] `ListContentTool` returns published content with all specified fields
- [x] `ListContentTool` supports `contentType` and `parentId` filters
- [x] `GetContentTool` returns property values with text extraction for common editors
- [x] `GetContentTool` returns tool error for unknown node IDs
- [x] `ListContentTypesTool` returns document type definitions with properties and compositions
- [x] All three tools registered in `AgentRunComposer` as `IWorkflowTool` singletons
- [x] All three tools added to `WorkflowValidator.AllowedToolSettings`
- [x] `IToolLimitResolver` extended with 3 new methods, implemented in `ToolLimitResolver`
- [x] Size guard truncation works for all three tools via resolver chain
- [x] Unrecognised parameters rejected with `ToolExecutionException`
- [x] Engine wiring guard (`ToolContextMissingException`) on all three tools
- [x] 30 new unit tests passing
- [x] All tests pass: `dotnet test AgentRun.Umbraco.slnx` — 521/521
- [ ] Manual E2E steps 1-5 verified by Adam

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Umbraco 17 API research: `IPublishedContentCache` no longer has `GetAtRoot()` — used `IDocumentNavigationQueryService.TryGetRootKeys()` + `IPublishedContentStatusFilteringService.FilterAvailable()` for tree traversal
- `IPublishedContent.CreatorId` (int) resolved to name via `IUserService.GetProfileById()` through `IServiceScopeFactory` (IUserService is scoped)
- `Url()` "Friendly" extension relies on `StaticServiceProvider` — not available in unit tests. Switched to explicit `IPublishedUrlProvider` injection for testability
- `UmbracoContextReference` struct is in `Umbraco.Cms.Core` namespace (not `Umbraco.Cms.Core.Web`), constructor requires `IUmbracoContextAccessor` as 3rd param
- Namespace collision: `AgentRun.Umbraco.Cms.Core` vs `Umbraco.Cms.Core` — resolved with `global::` prefix where needed

### Completion Notes List

- 3 new Umbraco content tools implemented: `list_content`, `get_content`, `list_content_types`
- All tools use `IUmbracoContextFactory.EnsureUmbracoContext()` for background-service-safe published content access
- `ListContentTool` traverses the full published tree via `IDocumentNavigationQueryService` (BFS), supports `contentType` and `parentId` filters
- `GetContentTool` extracts text from 11 property editor types (RichText, TextBox, TextArea, ContentPicker, MediaPicker3, BlockList, BlockGrid, TrueFalse, Integer, Decimal, DateTime, Tags) with fallback to JSON serialisation for unknown editors
- `ListContentTypesTool` uses `IContentTypeService.GetAll()` / `.Get(alias)` — singleton service, no scope needed
- Size guard truncation via `IToolLimitResolver` chain on all 3 tools (256 KB default, matching `read_file`)
- Config classes extended: `ToolDefaultsConfig`, `AgentRunToolDefaultsOptions`, `AgentRunToolLimitsOptions` — full step → workflow → site → engine default resolution chain
- 30 new unit tests, 521/521 total passing
- Property extraction wraps each property in try/catch — one bad property cannot kill the entire tool call

### File List

**New files:**
- AgentRun.Umbraco/Tools/ListContentTool.cs
- AgentRun.Umbraco/Tools/GetContentTool.cs
- AgentRun.Umbraco/Tools/ListContentTypesTool.cs
- AgentRun.Umbraco.Tests/Tools/ListContentToolTests.cs
- AgentRun.Umbraco.Tests/Tools/GetContentToolTests.cs
- AgentRun.Umbraco.Tests/Tools/ListContentTypesToolTests.cs

**Modified files:**
- AgentRun.Umbraco/Engine/EngineDefaults.cs — 3 new constants
- AgentRun.Umbraco/Engine/IToolLimitResolver.cs — 3 new methods
- AgentRun.Umbraco/Engine/ToolLimitResolver.cs — 3 new implementations
- AgentRun.Umbraco/Workflows/WorkflowValidator.cs — 3 new AllowedToolSettings entries
- AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs — 3 new config sections
- AgentRun.Umbraco/Configuration/AgentRunToolDefaultsOptions.cs — 3 new defaults classes
- AgentRun.Umbraco/Configuration/AgentRunToolLimitsOptions.cs — 3 new limits classes
- AgentRun.Umbraco/Composers/AgentRunComposer.cs — 3 new tool registrations
- AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs — FakeToolLimitResolver updated
- AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs — FakeToolLimitResolver updated
- AgentRun.Umbraco.Tests/Engine/StepExecutorTests.cs — StubToolLimitResolver updated

## Change Log

- 0.1 (2026-04-12): Initial implementation of 3 Umbraco content tools with 30 unit tests; 521/521 tests passing; awaiting Manual E2E verification by Adam
- 0.2 (2026-04-12): Code review — 7 patches applied (templateAlias fix, truncation fix, EnforceCeilings gap, overflow guards, IHtmlEncodedString handling, BlockList text extraction, CompositionPropertyTypes); 4 deferred; 5 dismissed; 521/521 tests passing
