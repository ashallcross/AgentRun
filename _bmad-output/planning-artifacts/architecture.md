---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-03-30'
inputDocuments:
  - "product-brief-shallai-umbraco-agent-runner.md"
  - "product-brief-shallai-umbraco-agent-runner-distillate.md"
  - "prd.md"
workflowType: 'architecture'
project_name: 'Shallai.UmbracoAgentRunner'
user_name: 'Adam'
date: '2026-03-28'
---

# Architecture Decision Document

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Project Context Analysis

### Requirements Overview

**Functional Requirements:**
69 FRs across 14 capability areas. The heaviest clusters are Step Execution (FR11-16), Tool System (FR17-24), and Chat Interface (FR38-42). These three areas define the core engine loop: assemble prompt → call LLM → handle tool calls → stream responses → persist state → check completion. Architecture must optimise for this hot path.

**Non-Functional Requirements:**
26 NFRs with security (NFR6-13) and reliability (NFR20-24) as the dominant concerns. Performance targets are reasonable (<500ms streaming, <2s dashboard load, <5s step transitions). The extensibility requirements (NFR25-26) explicitly demand a pluggable tool interface and configurable network policies — the architecture must be open for extension from day one.

**Scale & Complexity:**

- Primary domain: Backend workflow engine + CMS backoffice UI
- Complexity level: Medium-high
- Estimated architectural components: ~12 (workflow registry, instance manager, step executor, tool system, prompt assembler, artifact manager, profile resolver, state persistence, SignalR hub, dashboard shell, chat panel, API controllers)

### Technical Constraints & Dependencies

- **Umbraco 17+ on .NET 10** — target framework is fixed
- **Umbraco.AI 1.0.0+** — IAIChatService is the mandated LLM interface (NFR16)
- **YamlDotNet** — YAML parsing, no alternative considered
- **IHostedService** — background execution model mandated by PRD (FR15)
- **Bellissima (Lit + TypeScript + Vite)** — dashboard UI framework is fixed
- **SignalR** — real-time streaming mechanism (already used internally by Umbraco)
- **Disk-based state** — no database dependency by design
- **Open architectural question:** IAIChatService direct vs Umbraco.AI.Agent runtime — must be resolved before step executor design

### Cross-Cutting Concerns Identified

1. **Error handling & recovery** — spans step execution, tool system, LLM calls, state persistence. Must never crash the host (NFR23), must leave instances in recoverable states (NFR24)
2. **Security sandboxing** — spans file tools, fetch_url, tool access control, content rendering. Multiple attack surfaces, all must be addressed in v1
3. **Real-time streaming** — spans step executor, chat interface, dashboard UI. SignalR hub is the bridge between backend engine and frontend experience
4. **State persistence** — spans instance management, conversation history, artifact storage. Atomic writes and crash resilience are non-negotiable
5. **Profile resolution** — spans workflow registry, step executor, configuration. The 3-tier fallback chain touches multiple components

## Starter Template Evaluation

### Primary Technology Domain

.NET 10 NuGet package with Bellissima (Lit + TypeScript) frontend — technology stack is dictated by the target platform (Umbraco 17 + Umbraco.AI).

### Starter Options Considered

| Option | Description | Verdict |
|--------|-------------|---------|
| `dotnet new umbracopackage-rcl` (official Umbraco template) | Scaffolds RCL with App_Plugins structure, no frontend toolchain | **Selected** — official starting point, add Vite/Lit manually |
| Warren Buckley's Umbraco-Repo-Template | GitHub template that auto-scaffolds RCL + Vite + test site via GitHub Action | Considered — excellent for new repos, but designed for GitHub template workflow rather than local scaffolding |
| Multi-project pattern (Umbraco.AI style) | Separate projects for Core/Web/UI/Startup | Rejected for v1 — overkill for a community package PoC. Migration path to multi-project is mechanical (file moves + project references) provided engine logic and Umbraco integration are cleanly separated via interfaces |
| Manual `dotnet new classlib` | Build everything from scratch | Rejected — the official template provides correct RCL configuration |

### Selected Starter: `dotnet new umbracopackage-rcl`

**Rationale:** Official Umbraco template provides the correct Razor Class Library configuration, App_Plugins structure, and NuGet packaging setup. Frontend toolchain (Vite + Lit + TypeScript) is added manually — this is a one-time setup that all community packages do.

**Initialization Command:**

```bash
dotnet new install Umbraco.Templates
dotnet new umbracopackage-rcl -n Shallai.UmbracoAgentRunner
```

### Architectural Decisions Provided by Starter + Manual Setup

**Language & Runtime:**
- C# on .NET 10 (backend), TypeScript with ESNext target (frontend)
- `Microsoft.NET.Sdk.Razor` SDK for the package project

**Frontend Build Tooling:**
- Vite with `lib` mode, outputting ES modules to `wwwroot/App_Plugins/Shallai.UmbracoAgentRunner/`
- Rollup externalises all `@umbraco` imports (provided by host at runtime)
- `@umbraco-cms/backoffice` as devDependency, version-matched to Umbraco 17

**Testing Framework:**
- Backend: NUnit 4 + NSubstitute (aligns with Umbraco CMS's own test infrastructure and integration test base classes)
- Frontend: `@web/test-runner` + `@open-wc/testing` (standard Lit community tooling, matches Umbraco HQ's Bellissima tests)
- E2E: Playwright (matches Umbraco's acceptance test stack)

**Package Structure (Single RCL — designed for future multi-project split):**

```
Shallai.UmbracoAgentRunner/
  Shallai.UmbracoAgentRunner.csproj    # Microsoft.NET.Sdk.Razor
  Client/                               # Frontend source (excluded from NuGet)
    src/
      index.ts                          # Entry point using UmbEntryPointOnInit
      manifests.ts                      # Extension manifest registration
      components/                       # Lit web components
    public/
      umbraco-package.json              # Copied to wwwroot on build
    package.json
    vite.config.ts
    tsconfig.json
  wwwroot/                              # Compiled output (included in NuGet)
    App_Plugins/Shallai.UmbracoAgentRunner/
      shallai-umbraco-agent-runner.js
      umbraco-package.json
  Composers/                            # IComposer implementations
  Controllers/                          # API controllers
  Services/                             # Engine services (behind interfaces)
  Models/                               # Domain models
  Tools/                                # Tool implementations
```

**Future-proofing discipline:** Engine logic (Services/, Models/, Tools/) must depend on interfaces, not Umbraco types directly. Umbraco integration (Composers/, Controllers/) consumes engine interfaces. This separation makes a future multi-project split mechanical — file moves and project references, not a rewrite.

**Real-Time Communication:**
- SSE (Server-Sent Events) + POST pattern — POST to start/send messages, SSE stream for responses
- AG-UI-compatible event format for future alignment with Umbraco.AI.Agent protocol
- No SignalR dependency — simpler setup, no ReservedPaths configuration required
- Dashboard components consume SSE via standard EventSource API

**Dashboard Architecture:**
- Dashboard registered via `backofficeEntryPoint` extension type in umbraco-package.json
- Internal routing via `umb-router-slot` for sub-views (workflow list, instance detail, chat panel)
- UUI component library (80+ Lit components) for native Umbraco look and feel

**Note:** Project initialisation using this approach should be the first implementation story.

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (Block Implementation):**
1. Step Executor Integration Layer — IAIChatService direct with manual tool loop
2. Streaming Protocol — SSE + POST with AG-UI-compatible event format
3. Tool System Interface — `IWorkflowTool` with per-step `AIFunction` creation

**Important Decisions (Shape Architecture):**
4. Instance State Format — YAML + JSONL, disk-based, atomic writes
5. API Structure — Minimal API endpoints with backoffice auth
6. Frontend State — Umbraco Context API with observable state
7. Security Architecture — layered sandboxing, SSRF protection, tool access control

**Deferred Decisions (Post-MVP):**
- AG-UI protocol adoption (v2 — when migrating to Agent runtime)
- Copilot integration surface (v2/v3)
- Context eviction strategy (v2 — v1 targets short workflows)
- Content modification tool governance model (v2)

### Decision 1: Step Executor Integration Layer (CRITICAL)

**Decision:** Use IAIChatService with manual tool loop via `GetChatClientAsync(profileId)`, SSE + POST streaming, AG-UI-compatible event format. Step executor behind `IStepExecutor` interface for future swapability.

**Options Evaluated:**

| Option | Verdict | Rationale |
|--------|---------|-----------|
| A: IAIChatService direct + manual tool loop + SSE | **Selected** | Full control, no HttpContext dependency, per-step tool isolation trivial, proven patterns from Node.js runner |
| B: Umbraco.AI.Agent runtime + AG-UI streaming | Rejected for v1 | Cannot run reliably in IHostedService — HttpContext.Items dependency breaks ambient context accessor, auditing, profile metadata. Workaround requires replacing internal DI registrations (fragile, undocumented) |
| C: Hybrid (IAIChatService + AG-UI protocol) | Partially adopted | AG-UI event format adopted for future alignment, but not the runtime |

**Investigation Findings:**
- IAIChatService fully supports function calling via `ChatOptions.Tools` with automatic `FunctionInvokingChatClient` middleware
- `GetChatClientAsync(profileId)` returns middleware-wrapped IChatClient — full Umbraco.AI pipeline preserved (logging, governance, token tracking)
- Umbraco.AI.Agent's `IAIAgentFactory.CreateAgentAsync()` CAN create in-memory agents (no database required) — our initial assumption was wrong
- However, the Agent runtime stores scope in `HttpContext.Items` — `IAIRuntimeContextAccessor.Context` returns null in IHostedService, breaking downstream middleware
- SSE + POST matches how Umbraco.AI.Agent's own run endpoint works — natural alignment

**Implementation Approach:**
1. `GetChatClientAsync(profileId)` for each step — resolves profile, applies middleware
2. Manual tool loop: send messages with `ChatOptions.Tools` → detect `FunctionCallContent` → validate against declared tools → execute via `IWorkflowTool` → send `FunctionResultContent` → continue until no tool calls
3. Stream agent messages and tool activity as SSE events to dashboard
4. `IStepExecutor` interface wraps this — v2 can swap implementation to Agent runtime when background execution is supported

**Affects:** FR11-16, FR17-24, FR38-42, NFR16

### Decision 2: Streaming Protocol

**Decision:** SSE (Server-Sent Events) + POST endpoints. Events follow AG-UI-compatible format for future protocol alignment.

**Rationale:**
- SSE is simpler than SignalR — no hub registration, no `ReservedPaths` config, no Global Context extension needed
- Matches Umbraco.AI.Agent's own transport (SSE over HTTP)
- AG-UI-compatible event format means v2 migration to the Agent runtime requires changing the event source, not the event consumers
- Bidirectional communication via POST (send message) + SSE (receive stream) — same pattern as the Agent runtime's `RunAgentController`
- Browser-native `EventSource` API with built-in reconnection

**Event Types (AG-UI-aligned):**
- `run.started` / `run.finished` / `run.error` — lifecycle
- `text.delta` — streamed text chunks
- `tool.start` / `tool.args` / `tool.end` / `tool.result` — tool activity
- `step.started` / `step.finished` — step lifecycle

**Affects:** FR38-42, NFR1, NFR5

### Decision 3: Tool System Interface

**Decision:** Custom `IWorkflowTool` interface, registered via DI. Tools wrapped as `AIFunction` instances per-step via `AIFunctionFactory.Create()`. Only declared tools passed in `ChatOptions.Tools`.

**Interface:**
```csharp
public interface IWorkflowTool
{
    string Name { get; }
    string Description { get; }
    Task<object> ExecuteAsync(IDictionary<string, object?> arguments,
        ToolExecutionContext context, CancellationToken cancellationToken);
}
```

`ToolExecutionContext` provides: instance folder path, instance ID, step ID, cancellation token. Tools receive only what they need — no access to engine internals.

**Why not Umbraco.AI's `AIToolBase` / `[AITool]` system:** Global registration — all agents see all tools. Our tools need per-step filtering (FR21). Keeping our own interface also avoids dependency on `Umbraco.AI.Agent` package.

**Tool Resolution Flow:**
1. Step declares tools in workflow.yaml: `tools: [read_file, write_file]`
2. Engine resolves matching `IWorkflowTool` implementations from DI by name
3. Wraps each as `AIFunction` via `AIFunctionFactory.Create(tool.ExecuteAsync, tool.Name, tool.Description)`
4. Passes only those functions in `ChatOptions.Tools`
5. Manual loop validates every `FunctionCallContent.Name` against declared tools before execution (FR59)

**v1 Tools:** `read_file`, `write_file`, `list_files`, `fetch_url` — all sandboxed to instance folder.

**Extensibility (NFR25):** New tools implement `IWorkflowTool`, register in DI. Engine resolves by name at runtime. No engine modification needed.

**Affects:** FR17-24, FR56-62, NFR25-26

### Decision 4: Data Architecture — Instance State

**Decision:** Disk-based state using YAML for instance metadata and JSONL for conversation history. Atomic writes via temp file + rename.

**Instance Folder Structure:**
```
{dataRoot}/{workflowAlias}/{instanceId}/
  instance.yaml          # Single source of truth: workflow ref, current step, step statuses, timestamps
  conversation-{stepId}.jsonl  # Append-only conversation history per step
  {artifact files}       # Files written by agents (scan-results.md, quality-scores.md, etc.)
```

**instance.yaml schema:**
- `workflowAlias` — reference to workflow definition
- `currentStepIndex` — which step is active
- `status` — pending | running | completed | failed | cancelled
- `steps[]` — per-step status (pending | active | complete | error), started/completed timestamps
- `createdAt`, `updatedAt` — ISO timestamps
- `createdBy` — backoffice user who started the instance

**Conversation persistence (JSONL):**
- One JSON object per line — serialised `ChatMessage` (role, content, tool calls/results)
- Append-only — crash-safe by design (partial writes corrupt only the last line)
- One file per step — keeps conversations isolated and browsable

**Atomic writes:**
- Write to `instance.yaml.tmp`, then `File.Move(tmp, target, overwrite: true)`
- .NET's `File.Move` is atomic on POSIX; on Windows it's atomic if same volume (which it will be)

**Data root configuration:**
- Default: `{ContentRootPath}/App_Data/Shallai.UmbracoAgentRunner/instances/`
- Configurable via `appsettings.json`

**Affects:** FR7, FR9, FR43-44, NFR19-22

### Decision 5: API Controller Structure

**Decision:** Minimal API endpoints behind Umbraco's backoffice auth policy.

**Endpoints:**

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| GET | `/umbraco/api/shallai/workflows` | List available workflows | JSON array |
| POST | `/umbraco/api/shallai/instances` | Create new instance | JSON (instance) |
| GET | `/umbraco/api/shallai/instances` | List instances with status | JSON array |
| GET | `/umbraco/api/shallai/instances/{id}` | Instance detail + step statuses | JSON |
| POST | `/umbraco/api/shallai/instances/{id}/start` | Start or advance to next step | SSE stream |
| POST | `/umbraco/api/shallai/instances/{id}/message` | Send message to active agent | SSE stream |
| POST | `/umbraco/api/shallai/instances/{id}/retry` | Retry failed step | SSE stream |
| POST | `/umbraco/api/shallai/instances/{id}/cancel` | Cancel running instance | JSON |
| DELETE | `/umbraco/api/shallai/instances/{id}` | Delete instance | 204 |
| GET | `/umbraco/api/shallai/instances/{id}/artifacts/{filename}` | Get artifact content | text/markdown |
| GET | `/umbraco/api/shallai/instances/{id}/conversation/{stepId}` | Get conversation history | JSON array |

All endpoints decorated with `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` (NFR13).

SSE endpoints set `Content-Type: text/event-stream`, disable response buffering, and stream AG-UI-compatible events.

**Affects:** FR32-37, FR38-42, FR50-55

### Decision 6: Frontend State Management

**Decision:** Umbraco Context API with `UmbObjectState` / `UmbArrayState` observables. Component-local state, no persistent connection.

**Contexts:**
- **WorkflowContext** — available workflows list, fetched on dashboard load via GET
- **InstanceContext** — instance list and active instance, fetched via GET, refreshed after mutations
- **ChatContext** — active SSE connection, message buffer, streaming state. Opens EventSource on step start, closes on step complete or navigation

**Component Architecture:**
```
shallai-dashboard (umb-router-slot)
  ├── shallai-workflow-list      → /workflows
  ├── shallai-instance-list      → /workflows/{alias}
  ├── shallai-instance-detail    → /instances/{id}
  │   ├── shallai-step-progress  (step X of Y, status indicators)
  │   ├── shallai-artifact-viewer (markdown rendering)
  │   └── shallai-chat-panel     (message stream, input, tool activity)
```

Components use `this.observe()` from `UmbElementMixin` for reactive re-rendering.

**Affects:** FR32-37, FR38-42

### Decision 7: Security Architecture

**Path Sandboxing (NFR6):**
- `Path.GetFullPath(requestedPath)` to resolve canonical path
- `canonicalPath.StartsWith(instanceFolderPath)` to verify containment
- Reject if `File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)` (symlink check)
- Applied in every `IWorkflowTool` file operation

**SSRF Protection (NFR7-8):**
- `fetch_url` resolves DNS first via `Dns.GetHostAddressesAsync()`
- Check resolved IPs against blocklist: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 127.0.0.0/8, 169.254.0.0/16, ::1, fe80::/10
- Block before connection is established
- 15-second `HttpClient` timeout
- Response size limits: 200KB (JSON), 100KB (HTML/XML) via `MaxResponseContentBufferSize` or stream reading with limit
- Configurable network policies (NFR26) — v2 can whitelist specific local endpoints

**XSS Sanitisation (NFR9):**
- Markdown rendered client-side in Lit components
- Sanitise before rendering: strip raw HTML, script tags, event handler attributes
- UUI components handle their own output escaping

**Tool Access Control (NFR11):**
- Engine validates every `FunctionCallContent.Name` against the step's `tools[]` declaration
- Undeclared tool calls rejected with error logged and returned to the LLM as an error result
- The LLM only sees declared tools in `ChatOptions.Tools`, but validation is defence-in-depth

**Prompt Injection Mitigation (NFR12):**
- System prompt structure: `[Agent Instructions] --- [Runtime Context] --- [Tool Results Are Untrusted]`
- Tool results wrapped in explicit delimiters
- Agent prompt assembly follows fixed structure — no user-controlled content in system message

**Authentication (NFR13):**
- All endpoints require `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`
- SSE endpoints authenticated via bearer token in query string (EventSource limitation) or custom header

**Affects:** FR56-62, NFR6-13

### Decision Impact Analysis

**Implementation Sequence:**
1. Project scaffolding (RCL + Vite + test infrastructure)
2. Workflow YAML parsing + registry (foundation for everything)
3. Instance management + disk state (needed before execution)
4. Step executor + tool loop via IAIChatService (core engine)
5. Tool implementations (read_file, write_file, list_files, fetch_url)
6. SSE streaming endpoints (connect engine to UI)
7. Dashboard UI (workflow list → instance → chat panel)
8. Example workflow (content quality audit)
9. Packaging + documentation

**Cross-Component Dependencies:**
- Step executor depends on: workflow registry, instance manager, tool system, profile resolver
- SSE streaming depends on: step executor (produces events to stream)
- Dashboard depends on: API endpoints (consumes data and SSE streams)
- Tool system depends on: instance manager (provides sandbox paths)
- All components depend on: project scaffolding

## Implementation Patterns & Consistency Rules

### Why These Patterns Exist

This project will be implemented by AI coding agents across multiple sessions. These patterns prevent divergent implementation choices that would create inconsistent, hard-to-maintain code. Every pattern here addresses a specific conflict point where an agent could reasonably make a different choice.

### C# Code Patterns

**Naming Conventions (standard .NET):**
- Classes, interfaces, methods, properties: `PascalCase`
- Local variables, parameters: `camelCase`
- Private fields: `_camelCase` (underscore prefix)
- Constants: `PascalCase` (not SCREAMING_CASE)
- Interfaces: `I` prefix — `IWorkflowTool`, `IStepExecutor`, `IInstanceManager`

**Async Patterns:**
- All async methods suffixed with `Async` — `ExecuteStepAsync`, `LoadWorkflowAsync`
- Always pass `CancellationToken` as the last parameter
- Never use `.Result` or `.Wait()` — always `await`
- Use `ConfigureAwait(false)` in library code (this is a NuGet package, not an application)

**DI Registration:**
- All services registered in a single `AgentRunnerComposer : IComposer`
- Register interfaces, not concrete types: `services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>()`
- Scoped services for per-request state, singletons for stateless services
- Tools registered individually: `services.AddSingleton<IWorkflowTool, ReadFileTool>()`

**Error Handling:**
- Engine services throw typed exceptions: `WorkflowNotFoundException`, `StepExecutionException`, `ToolExecutionException`
- All exceptions inherit from `AgentRunnerException` (base)
- API controllers catch and map to appropriate HTTP status + error response
- Background step execution catches ALL exceptions, logs, sets step status to `error`, and streams an error event — never crashes the host (NFR23)
- Tool execution errors are returned to the LLM as error results, not thrown — the LLM can decide how to recover

**Result Pattern for Tool Execution:**
Tools return `object` (success) — the engine serialises it for the LLM. Tools throw `ToolExecutionException` for failures — engine catches, returns error to LLM.

### Workflow YAML Schema Conventions

**Field naming:** `snake_case` throughout — consistent with the existing Node.js runner schema and YAML community convention.

```yaml
name: "Content Quality Audit"
description: "Scans, analyses, and reports on content quality"
mode: interactive    # or: autonomous
default_profile: "analysis"
steps:
  - id: scanner
    name: "Content Scanner"
    agent: agents/scanner.md
    profile: "bulk"          # optional, overrides default_profile
    tools: [read_file, write_file, list_files]
    reads_from: []
    writes_to: [scan-results.md]
    completion_check:
      files_exist: [scan-results.md]
```

**Conventions:**
- Step IDs: `snake_case`, short, descriptive — `scanner`, `analyser`, `reporter`
- Tool names: `snake_case` matching `IWorkflowTool.Name` — `read_file`, `fetch_url`
- Profile references: string aliases matching Umbraco.AI profile aliases
- File references in `reads_from`/`writes_to`: relative to instance folder, include extension

### API Response Formats

**Success responses:** Direct JSON — no envelope wrapper. The HTTP status code indicates success.

```json
// GET /workflows
[
  { "alias": "content-audit", "name": "Content Quality Audit", "description": "...", "stepCount": 3, "mode": "interactive" }
]

// POST /instances
{ "id": "abc-123", "workflowAlias": "content-audit", "status": "pending", "currentStepIndex": 0, "createdAt": "2026-03-28T14:30:00Z" }
```

**Error responses:** Consistent structure across all endpoints.

```json
{
  "error": "step_execution_failed",
  "message": "Profile 'bulk' not found in Umbraco.AI configuration",
  "stepId": "scanner"
}
```

**Date format:** ISO 8601 strings in UTC — `2026-03-28T14:30:00Z`. Never Unix timestamps. `System.Text.Json` with `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.

**JSON field naming:** `camelCase` in API responses (standard .NET JSON convention).

### SSE Event Format

AG-UI-compatible structure. Each event is a single SSE message:

```
event: text.delta
data: {"content":"Scanning Homepage..."}

event: tool.start
data: {"toolCallId":"tc_001","toolName":"read_file"}

event: tool.result
data: {"toolCallId":"tc_001","result":"File contents here..."}

event: step.finished
data: {"stepId":"scanner","status":"complete"}

event: run.error
data: {"error":"rate_limit_exceeded","message":"OpenAI rate limit hit, retry available"}
```

**Rules:**
- One JSON object per `data:` line — never multiline
- `event:` field always present — consumers switch on event type
- Event names use `dot.notation` matching AG-UI conventions
- Tool call IDs use `tc_` prefix for traceability
- Error events include both machine-readable `error` code and human-readable `message`

### Frontend Patterns

**Component Naming:**
- Custom elements: `shallai-{name}` prefix — `shallai-dashboard`, `shallai-chat-panel`, `shallai-step-progress`
- Class names: `Shallai{Name}Element` — `ShallaiDashboardElement`, `ShallaiChatPanelElement`
- File names: `shallai-{name}.element.ts` — `shallai-dashboard.element.ts`
- One component per file

**File Organisation:**
```
Client/src/
  index.ts                    # UmbEntryPointOnInit
  manifests.ts                # All extension manifests
  components/
    shallai-dashboard.element.ts
    shallai-workflow-list.element.ts
    shallai-instance-list.element.ts
    shallai-instance-detail.element.ts
    shallai-chat-panel.element.ts
    shallai-step-progress.element.ts
    shallai-artifact-viewer.element.ts
  contexts/
    workflow.context.ts
    instance.context.ts
    chat.context.ts
  api/
    api-client.ts             # Fetch wrapper with auth
    types.ts                  # TypeScript interfaces matching API responses
  utils/
    sse-client.ts             # EventSource wrapper
    markdown-sanitiser.ts     # XSS-safe markdown rendering
```

**CSS:**
- Use UUI design tokens: `var(--uui-size-layout-1)`, `var(--uui-color-text)`, `var(--uui-color-surface)`
- Styles scoped to component via Shadow DOM (Lit default)
- No global stylesheets

### Logging Patterns

**Structured logging via `ILogger<T>`:**
- Engine lifecycle: `_logger.LogInformation("Workflow {WorkflowAlias} instance {InstanceId} step {StepId} started", ...)`
- Tool execution: `_logger.LogDebug("Tool {ToolName} executing for instance {InstanceId}", ...)`
- Errors: `_logger.LogError(ex, "Step {StepId} failed for instance {InstanceId}: {ErrorMessage}", ...)`
- Never log sensitive content (LLM prompts, tool results) at Information level — Debug only

**Structured fields (consistent across all log entries):**
- `WorkflowAlias`, `InstanceId`, `StepId`, `ToolName` — always use these exact names

### File I/O Patterns

**Atomic writes (used everywhere instance state is modified):**
```csharp
var tempPath = targetPath + ".tmp";
await File.WriteAllTextAsync(tempPath, content, cancellationToken);
File.Move(tempPath, targetPath, overwrite: true);
```

**Path sandboxing (used in every file tool):**
```csharp
var fullPath = Path.GetFullPath(Path.Combine(instanceFolder, requestedPath));
if (!fullPath.StartsWith(instanceFolder))
    throw new ToolExecutionException("Path traversal attempt blocked");
```

**JSONL append (used for conversation persistence):**
```csharp
var json = JsonSerializer.Serialize(message, jsonOptions);
await File.AppendAllTextAsync(conversationPath, json + Environment.NewLine, cancellationToken);
```

### Enforcement Guidelines

**All AI agents implementing stories MUST:**
1. Follow these naming patterns exactly — no "improvements" or alternative conventions
2. Use the error handling pattern specified — typed exceptions in services, error results for tools
3. Use atomic writes for any instance state modification
4. Apply path sandboxing in every file operation tool
5. Use structured logging with the exact field names specified
6. Match the SSE event format precisely — dashboard consumers depend on it
7. Prefix all custom elements with `shallai-`

## Project Structure & Boundaries

### Complete Project Directory Structure

```
Shallai.UmbracoAgentRunner/
│
├── Shallai.UmbracoAgentRunner.csproj      # Microsoft.NET.Sdk.Razor, .NET 10
├── GlobalUsings.cs                         # Shared using statements
│
├── Composers/
│   └── AgentRunnerComposer.cs             # IComposer — all DI registration, endpoint mapping
│
├── Configuration/
│   ├── AgentRunnerOptions.cs              # appsettings.json options (data root, default profile, workflow path)
│   └── AgentRunnerConfigurationExtensions.cs  # IServiceCollection extension for options binding
│
├── Endpoints/
│   ├── WorkflowEndpoints.cs              # GET /workflows
│   ├── InstanceEndpoints.cs              # CRUD /instances
│   ├── ExecutionEndpoints.cs             # POST start/message/retry (SSE streams)
│   ├── ArtifactEndpoints.cs              # GET artifacts, conversation history
│   └── SseHelper.cs                      # Shared SSE response writing utilities
│
├── Engine/
│   ├── IStepExecutor.cs                  # Interface — swappable for v2 Agent runtime
│   ├── StepExecutor.cs                   # Manual tool loop via IChatClient
│   ├── IPromptAssembler.cs               # Interface — builds agent prompt from markdown + context
│   ├── PromptAssembler.cs                # Loads agent .md, injects runtime context, artifact refs
│   ├── IProfileResolver.cs              # Interface — step → workflow → site default fallback
│   ├── ProfileResolver.cs               # Resolves Umbraco.AI profile Guid from alias chain
│   ├── ICompletionChecker.cs            # Interface — checks if step output files exist
│   ├── CompletionChecker.cs             # Verifies files_exist from completion_check
│   ├── ToolLoop.cs                      # FunctionCallContent detection, tool dispatch, FunctionResultContent
│   └── Events/
│       ├── ISseEventEmitter.cs          # Interface — emits AG-UI-compatible events
│       ├── SseEventEmitter.cs           # Writes SSE events to response stream
│       └── SseEventTypes.cs             # Event type constants and payload models
│
├── Workflows/
│   ├── IWorkflowRegistry.cs             # Interface — discovers and holds workflow definitions
│   ├── WorkflowRegistry.cs              # Scans workflow folders, parses YAML, validates schema
│   ├── WorkflowDefinition.cs            # Model — parsed workflow.yaml
│   ├── StepDefinition.cs                # Model — single step within a workflow
│   └── WorkflowValidator.cs             # JSON Schema validation of workflow.yaml
│
├── Instances/
│   ├── IInstanceManager.cs              # Interface — create, read, update instance state
│   ├── InstanceManager.cs               # Disk-based state management, atomic writes
│   ├── InstanceState.cs                 # Model — instance.yaml schema
│   ├── StepStatus.cs                    # Enum — pending, active, complete, error
│   ├── IConversationStore.cs            # Interface — JSONL conversation persistence
│   └── ConversationStore.cs             # Append-only JSONL read/write
│
├── Tools/
│   ├── IWorkflowTool.cs                 # Interface — all tools implement this
│   ├── ToolExecutionContext.cs           # Context passed to tools (instance folder, IDs)
│   ├── ReadFileTool.cs                  # read_file — sandboxed file reading
│   ├── WriteFileTool.cs                 # write_file — sandboxed file writing
│   ├── ListFilesTool.cs                 # list_files — sandboxed directory listing
│   └── FetchUrlTool.cs                  # fetch_url — SSRF-protected HTTP fetch
│
├── Security/
│   ├── PathSandbox.cs                   # Static helpers — path validation, symlink check
│   ├── SsrfProtection.cs               # DNS resolution + IP blocklist for fetch_url
│   └── MarkdownSanitiser.cs             # Server-side markdown sanitisation (defence-in-depth)
│
├── Models/
│   ├── ApiModels/
│   │   ├── WorkflowSummary.cs           # API response model
│   │   ├── InstanceSummary.cs           # API response model
│   │   ├── InstanceDetail.cs            # API response model
│   │   ├── CreateInstanceRequest.cs     # API request model
│   │   ├── SendMessageRequest.cs        # API request model
│   │   └── ErrorResponse.cs             # Standard error response
│   └── Exceptions/
│       ├── AgentRunnerException.cs      # Base exception
│       ├── WorkflowNotFoundException.cs
│       ├── StepExecutionException.cs
│       ├── ToolExecutionException.cs
│       └── ProfileNotFoundException.cs
│
├── Client/                               # Frontend source (excluded from NuGet)
│   ├── src/
│   │   ├── index.ts                     # UmbEntryPointOnInit
│   │   ├── manifests.ts                 # Extension manifest registration
│   │   ├── components/
│   │   │   ├── shallai-dashboard.element.ts
│   │   │   ├── shallai-workflow-list.element.ts
│   │   │   ├── shallai-instance-list.element.ts
│   │   │   ├── shallai-instance-detail.element.ts
│   │   │   ├── shallai-chat-panel.element.ts
│   │   │   ├── shallai-step-progress.element.ts
│   │   │   └── shallai-artifact-viewer.element.ts
│   │   ├── contexts/
│   │   │   ├── workflow.context.ts
│   │   │   ├── instance.context.ts
│   │   │   └── chat.context.ts
│   │   ├── api/
│   │   │   ├── api-client.ts            # Fetch wrapper with auth
│   │   │   └── types.ts                 # TypeScript interfaces
│   │   └── utils/
│   │       ├── sse-client.ts            # EventSource wrapper
│   │       └── markdown-sanitiser.ts    # XSS-safe markdown rendering
│   ├── public/
│   │   └── umbraco-package.json         # Copied to wwwroot on build
│   ├── package.json
│   ├── vite.config.ts
│   └── tsconfig.json
│
├── wwwroot/                              # Compiled output (included in NuGet)
│   └── App_Plugins/Shallai.UmbracoAgentRunner/
│       ├── shallai-umbraco-agent-runner.js
│       ├── shallai-umbraco-agent-runner.js.map
│       └── umbraco-package.json
│
└── Schemas/
    └── workflow-schema.json              # JSON Schema for workflow.yaml — ships in NuGet

Shallai.UmbracoAgentRunner.Tests/
├── Shallai.UmbracoAgentRunner.Tests.csproj  # NUnit 4, NSubstitute
├── Engine/
│   ├── StepExecutorTests.cs
│   ├── PromptAssemblerTests.cs
│   ├── ProfileResolverTests.cs
│   ├── CompletionCheckerTests.cs
│   └── ToolLoopTests.cs
├── Workflows/
│   ├── WorkflowRegistryTests.cs
│   └── WorkflowValidatorTests.cs
├── Instances/
│   ├── InstanceManagerTests.cs
│   └── ConversationStoreTests.cs
├── Tools/
│   ├── ReadFileToolTests.cs
│   ├── WriteFileToolTests.cs
│   ├── ListFilesToolTests.cs
│   └── FetchUrlToolTests.cs
├── Security/
│   ├── PathSandboxTests.cs
│   └── SsrfProtectionTests.cs
└── Fixtures/
    ├── SampleWorkflows/                  # Test workflow YAML + agent markdown
    └── TestHelpers.cs

Workflows/content-quality-audit/          # Example workflow (ships with package)
├── workflow.yaml
└── agents/
    ├── scanner.md
    ├── analyser.md
    └── reporter.md
```

### Architectural Boundaries

**Engine Boundary (no Umbraco dependencies):**
- `Engine/`, `Workflows/`, `Instances/`, `Tools/`, `Security/`, `Models/` — pure .NET, no `using Umbraco.*`
- Depends on: `Microsoft.Extensions.AI` (for `AIFunction`, `ChatMessage`, `FunctionCallContent`), `YamlDotNet`, `System.Text.Json`
- This is the code that moves cleanly to a separate project in a future multi-project split

**Umbraco Integration Boundary:**
- `Composers/`, `Endpoints/`, `Engine/ProfileResolver.cs` — these reference Umbraco types
- `ProfileResolver` bridges the engine to Umbraco.AI's `IAIChatService` and `IAIProfileService`
- Endpoints map HTTP requests to engine service calls

**Frontend Boundary:**
- `Client/` is entirely independent — communicates only via HTTP API + SSE
- No shared types between C# and TypeScript (types defined independently in both)
- `api/types.ts` mirrors `Models/ApiModels/` — kept in sync by convention

### Requirements to Structure Mapping

| FR Category | Primary Location | Key Files |
|-------------|-----------------|-----------|
| FR1-5: Workflow Discovery & Registry | `Workflows/` | `WorkflowRegistry.cs`, `WorkflowDefinition.cs`, `WorkflowValidator.cs` |
| FR6-10: Instance Management | `Instances/` | `InstanceManager.cs`, `InstanceState.cs` |
| FR11-16: Step Execution | `Engine/` | `StepExecutor.cs`, `PromptAssembler.cs`, `ProfileResolver.cs`, `ToolLoop.cs` |
| FR17-24: Tool System | `Tools/` | `IWorkflowTool.cs`, individual tool implementations |
| FR25-28: Artifact Handoff | `Engine/` + `Instances/` | `PromptAssembler.cs` (reads_from), `CompletionChecker.cs` |
| FR29-31: Workflow Modes | `Engine/` | `StepExecutor.cs` (mode logic) |
| FR32-37: Dashboard UI | `Client/src/components/` | All `shallai-*.element.ts` files |
| FR38-42: Chat Interface | `Client/src/components/` + `Endpoints/` | `shallai-chat-panel.element.ts`, `ExecutionEndpoints.cs` |
| FR43-44: Conversation Persistence | `Instances/` | `ConversationStore.cs` |
| FR45-49: Error Handling | `Engine/` + `Models/Exceptions/` | `StepExecutor.cs`, exception types |
| FR50-52: Instance Lifecycle | `Instances/` + `Endpoints/` | `InstanceManager.cs`, `InstanceEndpoints.cs` |
| FR53-55: Provider Prerequisites | `Endpoints/` + `Client/` | Workflow endpoints (profile check), dashboard (guidance UI) |
| FR56-62: Security | `Security/` + `Tools/` | `PathSandbox.cs`, `SsrfProtection.cs`, tool implementations |
| FR63-64: Artifact Browsing | `Endpoints/` + `Client/` | `ArtifactEndpoints.cs`, `shallai-artifact-viewer.element.ts` |
| FR65-66: Example Workflow | `Workflows/content-quality-audit/` | `workflow.yaml`, agent markdown files |
| FR67-69: Documentation & Schema | `Schemas/` + repo root | `workflow-schema.json`, markdown docs |

### Data Flow

```
[Dashboard UI] ←→ HTTP/SSE ←→ [Endpoints]
                                    ↓
                            [Engine Services]
                           ↙        ↓        ↘
              [WorkflowRegistry] [InstanceManager] [StepExecutor]
                                                    ↓        ↓
                                          [PromptAssembler] [ToolLoop]
                                                    ↓        ↓
                                    [IAIChatService/IChatClient] [IWorkflowTool implementations]
                                                    ↓
                                          [Umbraco.AI Pipeline]
                                                    ↓
                                            [LLM Provider]
```

### Development Workflow

1. `dotnet run` on test Umbraco site (references the RCL project)
2. `npm run watch` in `Client/` folder for frontend hot-rebuild
3. `dotnet test` for backend tests
4. Frontend changes: edit component → Vite rebuilds → refresh backoffice (10s manifest cache in dev)
5. Backend changes: edit service → `dotnet run` restart → test via dashboard or API

## Architecture Validation Results

### Coherence Validation ✅

**Decision Compatibility:**
All technology choices work together without conflicts. IAIChatService via `GetChatClientAsync()` + manual tool loop + SSE streaming are fully compatible. `IWorkflowTool` wrapping via `AIFunctionFactory.Create()` is confirmed compatible with M.E.AI's `ChatOptions.Tools`. Disk-based state + IHostedService has no HttpContext dependency. Single RCL + Vite + Bellissima is a proven community pattern.

**Pattern Consistency:**
No contradictions. `snake_case` in YAML, `camelCase` in JSON APIs, `PascalCase` in C# — each follows its domain convention. SSE event names (`dot.notation`) align with AG-UI format. Frontend `shallai-` prefix is globally unique.

**Structure Alignment:**
Project structure maps cleanly to all decisions. Engine boundary (no Umbraco deps) is clearly separated. Every interface has a corresponding implementation. Test structure mirrors source structure.

### Requirements Coverage ✅

All 69 functional requirements and 26 non-functional requirements have architectural support. Full coverage verified against the PRD — every FR mapped to specific files and directories in the project structure.

**One partial gap identified:**
- **NFR5 (SSE reconnection with replay):** EventSource auto-reconnects but messages during disconnection are lost. Accepted for v1 — the conversation history endpoint provides recovery. Full replay buffering deferred to v2.

### Implementation Readiness ✅

**Decision Completeness:** All critical decisions documented with investigation evidence. The IAIChatService vs Agent runtime decision was investigated at decompiled source level — no assumptions remain.

**Structure Completeness:** Every file and directory defined. All FRs mapped to locations. Data flow and development workflow documented.

**Pattern Completeness:** Naming, error handling, file I/O, SSE events, logging, frontend — all specified with concrete examples and enforcement rules.

### Architecture Completeness Checklist

**✅ Requirements Analysis**
- [x] Project context thoroughly analysed
- [x] Scale and complexity assessed (medium-high)
- [x] Technical constraints identified (Umbraco 17, .NET 10, Umbraco.AI 1.0.0)
- [x] Cross-cutting concerns mapped (5 areas)

**✅ Architectural Decisions**
- [x] Critical integration layer decision made with deep investigation
- [x] Streaming protocol decided (SSE + POST, AG-UI-compatible)
- [x] Tool system designed (IWorkflowTool + per-step AIFunction creation)
- [x] Data architecture specified (YAML + JSONL, atomic writes)
- [x] API structure defined (11 endpoints)
- [x] Security architecture layered (sandboxing, SSRF, XSS, tool control, auth)

**✅ Implementation Patterns**
- [x] C# naming and async conventions established
- [x] DI registration patterns specified
- [x] Error handling approach defined
- [x] YAML schema conventions locked
- [x] API response formats standardised
- [x] SSE event format specified (AG-UI-compatible)
- [x] Frontend component naming and file organisation defined
- [x] Logging patterns with structured fields specified
- [x] File I/O patterns documented

**✅ Project Structure**
- [x] Complete directory tree with every file
- [x] Engine boundary clearly separated from Umbraco integration
- [x] All 69 FRs mapped to specific files/directories
- [x] Data flow diagram defined
- [x] Development workflow documented

### Architecture Readiness Assessment

**Overall Status:** READY FOR IMPLEMENTATION

**Confidence Level:** High — core engine patterns are proven (ported from Node.js runner), Umbraco.AI integration surface deeply investigated (not assumed), platform conventions (Bellissima, RCL, Vite) well-documented.

**Key Strengths:**
- IAIChatService integration investigated at decompiled source level — no assumptions
- Agent runtime investigation documented exactly why it was rejected and what changes would enable v2 adoption
- Per-step tool isolation is architecturally clean
- AG-UI-compatible event format provides natural migration path
- Engine boundary discipline enables future multi-project split

**Areas for Future Enhancement (v2):**
- SSE reconnection with message replay (NFR5 full compliance)
- Agent runtime adoption when IHostedService support matures
- Context eviction for longer workflows
- Copilot integration via Custom Tools

### Implementation Handoff

**AI Agent Guidelines:**
- Follow all architectural decisions exactly as documented
- Use implementation patterns consistently across all components
- Respect project structure and boundaries — engine code has no Umbraco dependencies
- Refer to this document for all architectural questions

**First Implementation Priority:**
1. Scaffold the RCL project via `dotnet new umbracopackage-rcl -n Shallai.UmbracoAgentRunner`
2. Set up Vite + Lit + TypeScript in `Client/`
3. Create the `AgentRunnerComposer` with DI skeleton
4. Implement `WorkflowRegistry` + `WorkflowDefinition` (foundation for everything else)
