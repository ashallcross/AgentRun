---
project_name: 'Umbraco AI'
user_name: 'Adam'
date: '2026-03-30'
sections_completed: ['technology_stack', 'language_specific_rules', 'framework_specific_rules', 'code_quality_style', 'development_workflow', 'critical_dont_miss']
status: 'complete'
rule_count: 62
optimized_for_llm: true
---

# Project Context for AI Agents

_This file contains critical rules and patterns that AI agents must follow when implementing code in this project. Focus on unobvious details that agents might otherwise miss._

---

## Technology Stack & Versions

### Backend
- .NET 10 / Microsoft.NET.Sdk.Razor (RCL)
- Umbraco CMS 17.2.2
- Umbraco.AI 1.7.0 (LLM integration via IAIChatService)
- Umbraco.AI.Anthropic 1.2.2 / Umbraco.AI.OpenAI 1.1.3 (providers)
- Microsoft.Extensions.AI (ChatMessage, AIFunction, ChatOptions, FunctionCallContent) — transitive via Umbraco.AI
- YamlDotNet (YAML workflow parsing)
- NUnit 4.5.1 / NSubstitute 5.3.0

### Frontend
- TypeScript 5.9.3 (strict mode, ESNext target, experimentalDecorators: true, useDefineForClassFields: false)
- Vite 7.1.9 (library mode, ES modules, sourcemaps enabled)
- Lit 3.3.2 (web components)
- @umbraco-cms/backoffice 17.2.2 (Bellissima extension framework)
- @web/test-runner 0.20.2 / @open-wc/testing 4.0.0

### Version Constraints
- **DO NOT use Umbraco.AI.Agent runtime** — it stores scope in HttpContext.Items, which is null inside IHostedService. Use IAIChatService.GetChatClientAsync() directly with a manual tool loop.
- **DO NOT bundle @umbraco imports** — they are externalized in vite.config.ts and provided by the Bellissima host at runtime via import maps. Import lit via `@umbraco-cms/backoffice/external/lit` (covered by the `@umbraco` external pattern).
- **Umb.Condition.SectionUserPermission requires `match` property** — set to the section alias (e.g. `match: "Shallai.UmbracoAgentRunner.Section"`). Without it, the condition always blocks. New sections also need manual permission grant in Users > User Groups.

## Critical Implementation Rules

### Language-Specific Rules

#### C#
- Nullable reference types are enabled — all reference types must have explicit nullability
- `System.Text.Json` and `Microsoft.Extensions.Logging` are global usings — do not re-import
- Async methods: always accept `CancellationToken` as last parameter, always use `Async` suffix
- Use `System.Text.Json` for serialization — never Newtonsoft
- Tool execution errors: return as error results to the LLM, do not throw exceptions
- Private fields: `_camelCase` prefix (e.g., `_workflowRegistry`)
- Namespaces follow folder structure: `Shallai.UmbracoAgentRunner.{Folder}` (e.g., `Shallai.UmbracoAgentRunner.Configuration`)
- All DI registration goes through `AgentRunnerComposer : IComposer` — never Program.cs or extension methods
- Use `string.Empty` for empty string defaults, not `""`
- YAML deserialization: workflow files use snake_case — configure YamlDotNet naming convention or use `[YamlMember(Alias = "...")]`

#### TypeScript
- `experimentalDecorators: true` + `useDefineForClassFields: false` — required for Lit decorators, do not change
- Import paths for local modules must use `.js` extension (ES module resolution, even when source is .ts)
- **NEVER import from bare `lit` or `lit/decorators.js`** — Bellissima's import map does NOT include them. Use `@umbraco-cms/backoffice/external/lit` instead (re-exports all of lit + decorators). Bare `lit` imports cause "Failed to resolve module specifier" at runtime.
- Vite externals: only `/^@umbraco/` — do NOT add `/^lit/` (we import lit via the `@umbraco-cms/backoffice/external/lit` path which is already covered)
- Local imports use relative paths with `.js` extension
- Test files (*.test.ts) are excluded from tsconfig — compiled separately by esbuild via web-test-runner
- Strict mode enforced: no unused locals, no unused parameters, no fallthrough cases
- Frontend tests use `describe`/`it` with `expect` from `@open-wc/testing` — not assert, not jest globals
- **Lit state updates MUST be immutable** — never mutate `@state()` or `@property()` objects/arrays directly. Always create new objects via spread (`{ ...msg, content: newContent }`) and new arrays via slice/spread (`[...arr.slice(0, i), newItem, ...arr.slice(i + 1)]`). Direct mutation breaks Lit's dirty-checking reactivity.

#### Testing Conventions
- Backend: NUnit 4 attributes only — `[TestFixture]`, `[Test]`, `Assert.That()`. Never xUnit or MSTest.
- Test file paths mirror source paths (e.g., `Configuration/AgentRunnerOptionsTests.cs` mirrors `Configuration/AgentRunnerOptions.cs`)

### Framework-Specific Rules

#### Umbraco Backend (RCL Package)
- Register services via `IComposer`, not startup extension methods — the package has no access to Program.cs
- Minimal API endpoints: use `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` for all routes
- API route prefix: `/umbraco/api/shallai/` — all endpoints must sit under this path
- JSON API responses use `camelCase` property naming (System.Text.Json default)
- Background execution uses `IHostedService` — no HttpContext available, no request-scoped services
- Configuration binding: `builder.Config.GetSection("Shallai:AgentRunner")` maps to `AgentRunnerOptions`

#### Umbraco Frontend (Bellissima Extensions)
- Custom elements use `shallai-` prefix with `Shallai{Name}Element` class naming (e.g., `<shallai-dashboard>` / `ShallaiDashboardElement`)
- Register extensions via the `manifests` array exported from `manifests.ts` — this is the Bellissima extension entry point
- State management: use Umbraco Context API with `UmbObjectState` / `UmbArrayState` observables — not Redux, not standalone stores
- UI components: use UUI (Umbraco UI) component library — do not create custom design system components
- Bundle ID in umbraco-package.json: `Shallai.UmbracoAgentRunner` — JS output path: `/App_Plugins/ShallaiUmbracoAgentRunner/`
- SSE streaming for real-time chat — not SignalR, not WebSockets directly
- **Interactive mode is the primary UX model** — the human drives step progression, the agent responds. Autonomous mode is secondary. All UI labels, buttons, status text, and placeholders must reflect the interactive model by default. Autonomous-specific UI (Running badges, Cancel buttons, spinning icons between turns) must be gated on `workflowMode === "autonomous"`. Story specs with UI work must state the UX mode explicitly.

#### Lit Web Components
- Components extend `LitElement` with Lit decorators (`@customElement`, `@property`, `@state`)
- Templates use `html` tagged template literals — no JSX
- Styles use `css` tagged template literals in static `styles` property
- Reactive properties trigger re-render; use `@state()` for internal state, `@property()` for public API

#### Data Persistence (Disk-Based)
- Instance metadata: YAML files (`instance.yaml`) — workflow ref, current step, statuses, timestamps
- Conversation history: JSONL files (`conversation-{stepId}.jsonl`) — append-only, crash-safe
- Atomic writes everywhere: write to `.tmp` file, then `File.Move(temp, target, overwrite: true)`
- Default data root: `{ContentRootPath}/App_Data/Shallai.UmbracoAgentRunner/instances/`

### Code Quality & Style Rules

#### Naming Conventions
- C# classes/methods/properties: `PascalCase`
- C# local variables/parameters: `camelCase`
- C# private fields: `_camelCase`
- C# interfaces: `I` prefix (e.g., `IWorkflowTool`)
- C# constants: `PascalCase` (not SCREAMING_CASE)
- YAML workflow files: `snake_case` throughout
- JSON API responses: `camelCase`
- SSE event names: `dot.notation` (e.g., `text.delta`, `tool.start`, `run.error`)
- Frontend custom elements: `shallai-{name}` tag, `Shallai{Name}Element` class

#### Error Handling
- Typed exceptions inheriting from `AgentRunnerException` (base class)
- Exceptions caught at the API boundary (endpoints), not in internal services
- Tool errors returned to LLM as error results — never thrown
- Structured logging via `ILogger<T>` with exact field names: `WorkflowAlias`, `InstanceId`, `StepId`, `ToolName`

#### Code Organisation
- Engine folder (`Engine/`) must have zero Umbraco dependencies — pure .NET only, testable in isolation
- Services folder for cross-cutting concerns that bridge Umbraco and Engine
- One class per file, file name matches class name
- Interfaces defined in the same folder as their primary implementation

### Development Workflow Rules

#### Build & Run
- Backend: `dotnet run` from TestSite project for local development
- Frontend: `npm run watch` from `Client/` folder — Vite rebuilds on change
- **CRITICAL: The repo root contains multiple projects** — always specify the solution file explicitly: `dotnet test Shallai.UmbracoAgentRunner.slnx` (not bare `dotnet test`). Bare `dotnet test` fails with MSB1011.
- Frontend build output: `wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/` (no dots in folder name)
- Umbraco has a 10-second manifest cache in development — frontend changes may not appear instantly
- Run `npm run build` in `Client/` before committing to ensure `wwwroot/` output is up to date

#### Testing
- Backend tests: `dotnet test` from solution root
- Frontend tests: `npm test` from `Client/` folder
- All tests must pass before a story is considered complete

#### Project Structure
- Solution uses `.slnx` format (modern .NET 10) — not `.sln`
- Three projects: `Shallai.UmbracoAgentRunner` (package), `Shallai.UmbracoAgentRunner.Tests` (tests), `Shallai.UmbracoAgentRunner.TestSite` (dev host)
- TestSite references the main package via project reference — used for manual integration testing
- NuGet packages added to main .csproj only — TestSite inherits transitively unless test-site-specific

### Critical Don't-Miss Rules

#### Architecture Boundaries
- **Engine has ZERO Umbraco dependencies** — the Engine/ folder must only reference pure .NET types. This enables isolated unit testing and potential future extraction.
- **No request-scoped services in IHostedService** — the step executor runs in a background service. Injecting `IHttpContextAccessor`, `IUmbracoContextAccessor`, or any scoped Umbraco service will fail at runtime with no compile-time warning.
- **IAIChatService is the LLM entry point** — call `GetChatClientAsync(profileId)` to get a configured `IChatClient`. Do not instantiate chat clients directly.

#### Security (Non-Negotiable)
- **Path sandboxing**: resolve canonical paths and check symlinks before any file I/O. All tool file access must be within the configured data root.
- **SSRF protection**: DNS-resolve URLs before fetching. Block RFC1918 (10.x, 172.16-31.x, 192.168.x), loopback (127.x, ::1), and link-local (169.254.x) addresses.
- **Tool whitelisting**: only tools explicitly listed in the workflow step's `tools` array may execute. Never auto-discover or auto-register tools.
- **XSS sanitisation**: sanitise all markdown content before rendering in the backoffice UI.
- **Prompt injection**: system prompts must be clearly delineated from user-provided content in all LLM calls.

#### State & Persistence
- **Atomic writes are mandatory** for all instance state changes — write to `{path}.tmp`, then `File.Move(tmp, path, overwrite: true)`. Never write directly to the target file.
- **JSONL conversation files are append-only** — never rewrite or truncate. Each line is a self-contained JSON object.
- **Instance concurrency prevention** — only one step may execute per instance at a time. Guard against duplicate start requests.

#### Common Agent Mistakes to Avoid
- Do not use `[AITool]` attribute from Umbraco.AI — use custom `IWorkflowTool` interface with `AIFunctionFactory.Create()` for per-step tool filtering
- Do not use SignalR for streaming — use SSE (Server-Sent Events) with POST endpoints
- Do not create custom UI components when UUI equivalents exist — check UUI library first
- Do not put Umbraco-specific types in the Engine/ folder
- Do not use `HttpContext.Items` for state — it's null in IHostedService

### Lessons from Previous Projects

#### Process Rules (Proven Across 12 Retrospectives)
- **Commit-per-story** — one commit per completed story, never batch an entire epic
- **Code reviewer checks epic plan first** — prevents flagging future work as bugs
- **Stories must include "What NOT to Build"** — prevents agent scope creep
- **Stories must include "Failure & Edge Cases" section** — list non-obvious failure modes: what happens if external dependencies misbehave (LLM loops, API timeouts), what happens on cancellation (OCE propagation, cleanup), and boundary conditions (empty lists, not-found, concurrent access). The dev agent implements what's specified — unspecified failure paths become bugs.
- **Test targets ~10 per story** — set after story breakdown, treat as guidelines not ceilings
- **Manual E2E validation in story specs** — it's a refinement loop, not just a gate
- **Production smoke test in Definition of Done** — dev servers mask runtime differences
- **Deferred items need explicit triage** — record disposition (fix now / defer / won't fix) and track volume
- **Simplest fix first** — always try the simplest, most robust solution before iterating. Don't overcomplicate bug fixes.
- **Browser testing shortcut** — when stuck on frontend issues, ask Adam to verify in the browser rather than guessing repeatedly
- **Live provider testing** — Epic 4+ stories should include manual E2E validation steps exercising a real Anthropic provider
- **Security code: deny by default** — for any story touching security-relevant code (sandboxing, access control, input validation, network access), the Failure & Edge Cases section must explicitly state: "Unrecognised or unspecified inputs must be denied/rejected." The dev agent implements what's specified — a missing deny-by-default statement results in permissive implementation.
- **Validate complex fixes with architect** — when E2E reveals an architecture-level bug, validate the fix approach with the architect before the dev agent implements. Prevents iterating on wrong approaches.

#### Technical Rules (Learned the Hard Way)
- **YAML Date coercion** — YAML parsers silently coerce date-like strings (e.g., `2026-03-30` becomes a Date object). Quote date-like values or configure the deserializer to treat them as strings. Critical for workflow YAML files.
- **Error handling edge cases are the blind spot** — optimistic state mutations before async operations need explicit rollback paths. A corruption bug went undetected through 3 stories in a previous project.
- **SDK import boundary is a hard rule** — never leak Umbraco.AI SDK types across the Engine boundary. Use project-owned interfaces at the seam.
- **Validate integration seams during planning** — trace a real end-to-end request through the architecture before implementation begins. Subsystems designed independently will have tensions at their boundaries.

---

## Usage Guidelines

**For AI Agents:**
- Read this file before implementing any code
- Follow ALL rules exactly as documented
- When in doubt, prefer the more restrictive option
- Refer to `_bmad-output/planning-artifacts/architecture.md` for detailed architectural rationale

**For Humans:**
- Keep this file lean and focused on agent needs
- Update when technology stack changes
- Review periodically for outdated rules
- Remove rules that become obvious over time

Last Updated: 2026-04-02
