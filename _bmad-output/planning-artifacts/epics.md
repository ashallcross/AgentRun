---
stepsCompleted:
  - "step-01-validate-prerequisites"
  - "step-02-design-epics"
  - "step-03-create-stories"
  - "step-04-final-validation"
status: complete
completedAt: '2026-03-30'
inputDocuments:
  - "prd.md"
  - "architecture.md"
  - "ux-design-specification.md"
---

# Shallai.UmbracoAgentRunner - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for Shallai.UmbracoAgentRunner, decomposing the requirements from the PRD, UX Design, and Architecture requirements into implementable stories.

## Requirements Inventory

### Functional Requirements

FR1: The engine can discover workflow definitions from folders on disk at startup without code registration
FR2: The engine can parse workflow.yaml files and validate them against the JSON Schema
FR3: The engine can load agent prompt files (markdown) referenced by workflow steps
FR4: A developer can add a new workflow by dropping a folder containing workflow.yaml and agent markdown files into the workflows directory
FR5: The engine can report validation errors for malformed workflow.yaml files with clear, actionable messages
FR6: A developer or editor can create a new workflow instance from an available workflow definition
FR7: The engine can persist instance state to disk so it survives app pool restarts
FR8: A developer or editor can view the list of existing workflow instances and their current status
FR9: The engine can track which step an instance is on and what artifacts have been produced
FR10: A developer or editor can resume an interrupted workflow instance from where it left off
FR11: The engine can assemble an agent prompt from the step's markdown file, sidecar instructions (if present), and runtime context (step info, tool declarations, prior artifact references)
FR12: The engine can load sidecar instruction files (sidecars/{step-id}/instructions.md) as supplementary read-only context injected into the agent prompt alongside the main agent markdown
FR13: The engine can execute a step by sending the assembled prompt to Umbraco.AI's IAIChatService using the resolved profile
FR14: The engine can resolve which Umbraco.AI profile to use via the fallback chain: step-level → workflow-level → site-level default
FR15: The engine can execute steps as background tasks via IHostedService, not within web request pipelines
FR16: The engine can detect step completion by verifying required output files exist (completion checking)
FR17: An agent can read files within the instance folder via the read_file tool
FR18: An agent can write files within the instance folder via the write_file tool
FR19: An agent can list files within the instance folder via the list_files tool
FR20: An agent can fetch content from external URLs via the fetch_url tool
FR21: The engine can restrict tool access to only tools declared in the workflow step definition
FR22: The engine can sandbox file operations to the instance folder, preventing path traversal
FR23: The engine can register tools through a pluggable tool interface — new tools can be added by implementing a standard interface and registering them, without modifying the engine core
FR24: A workflow author can declare any registered tool in a step's tool list — the engine resolves tool names to registered implementations at runtime
FR25: A workflow step can declare which artifacts it reads from previous steps (reads_from)
FR26: A workflow step can declare which artifacts it produces (writes_to)
FR27: The engine can make prior step artifacts available to the current step's agent context
FR28: Any step can read artifacts from any prior step, not just the immediately preceding one
FR29: A developer or editor can advance a workflow step-by-step in interactive mode, reviewing intermediate artifacts before proceeding
FR30: The engine can auto-advance steps in autonomous mode when completion checks pass
FR31: A workflow author can set the execution mode (interactive or autonomous) per workflow in workflow.yaml
FR32: A developer or editor can view all available workflows in the Bellissima dashboard
FR33: A developer or editor can create a new workflow instance from the dashboard
FR34: A developer or editor can view the progress of a running workflow (current step, total steps, status)
FR35: A developer or editor can view intermediate artifacts produced by completed steps
FR36: A developer or editor can advance to the next step in interactive mode from the dashboard
FR37: The dashboard can display workflow metadata (name, description, number of steps, mode)
FR38: A developer or editor can view agent messages in real-time via a chat panel during step execution
FR39: The chat panel can stream messages via SignalR as the agent works
FR40: A developer or editor can see tool call activity in the chat panel (what tool was called, what it produced)
FR41: A developer or editor can send messages to an agent during step execution — agents are conversational and work interactively with humans, not as batch processors
FR42: The engine can maintain multi-turn conversation context within a step, enabling back-and-forth dialogue between the user and the agent across multiple exchanges
FR43: The engine can persist agent conversation history for each step execution
FR44: A developer or editor can review the full conversation history of a completed step
FR45: The engine can detect and handle LLM provider errors (rate limits, timeouts, API failures) with clear error reporting to the user
FR46: The engine can retry a failed LLM call when the user requests a retry from the chat interface
FR47: The engine can roll back the failed message from conversation context on error, so the agent doesn't see its own broken response on retry
FR48: The engine can preserve error context so the agent can learn from failures (error results are not discarded from context)
FR49: The engine can track granular step status (pending, active, complete, error) and display the current status in the dashboard
FR50: A developer or editor can cancel a running workflow instance
FR51: A developer or editor can delete completed or cancelled workflow instances
FR52: The engine can prevent concurrent execution on the same instance
FR53: The dashboard can detect whether Umbraco.AI has at least one provider configured and display a clear guidance message if not
FR54: A developer can configure the site-level default profile for the runner
FR55: The engine can report a clear error when a step references a profile that doesn't exist
FR56: The engine can validate that workflow.yaml files only reference declared tools from the allowed tool set
FR57: The engine can sanitise agent-produced markdown content before rendering in the dashboard to prevent XSS
FR58: The engine can reject file tool operations that attempt to access paths outside the instance folder
FR59: The engine can restrict agents to only the tools declared in their step definition, ignoring undeclared tool call attempts
FR60: The fetch_url tool can block requests to private/internal IP ranges (RFC1918, loopback, link-local) to prevent SSRF attacks
FR61: The fetch_url tool can enforce response size limits to prevent memory exhaustion from large responses
FR62: The fetch_url tool can enforce request timeouts to prevent hanging connections
FR63: A developer or editor can browse all files produced by a workflow instance in a file browser view
FR64: A developer or editor can view artifact content with markdown rendering in the dashboard
FR65: The package can ship with a pre-built Content Quality Audit workflow (2-3 steps) that works out-of-the-box with a single configured profile
FR66: The example workflow can produce a useful, actionable content audit report as its final artifact
FR67: The package can ship with a JSON Schema for workflow.yaml that provides IDE validation and autocomplete
FR68: The package can ship with a getting started guide covering installation, profile configuration, and first run
FR69: The package can ship with a workflow authoring guide covering YAML schema, agent prompts, artifact handoff, and profile configuration

### NonFunctional Requirements

NFR1: Chat panel messages must appear within 500ms of the agent generating them — perceptible lag kills the real-time experience
NFR2: Dashboard workflow list and instance list must load within 2 seconds on a standard Umbraco 17 development site
NFR3: Step transitions (completion detection → next step start) must occur within 5 seconds in autonomous mode
NFR4: The engine must not block the Umbraco web request pipeline — all step execution runs in background tasks via IHostedService
NFR5: SignalR connection must recover automatically after temporary disconnection without losing message history (reconnect and replay missed messages)
NFR6: File tool operations (read_file, write_file, list_files) must enforce path sandboxing using canonicalised path comparison — no path traversal via ../, symlinks, or URL encoding
NFR7: fetch_url must block all requests to RFC1918 (10.x, 172.16-31.x, 192.168.x), loopback (127.x), link-local (169.254.x), and IPv6 equivalents at the socket level before the connection is established
NFR8: fetch_url must enforce a 15-second request timeout and response size limits (200KB for JSON, 100KB for HTML/XML) to prevent resource exhaustion
NFR9: Agent-produced content rendered in the dashboard must be sanitised to prevent XSS — no raw HTML execution, script injection, or event handler attributes in rendered markdown
NFR10: The engine must validate workflow.yaml against the JSON Schema before loading — malformed or unexpected properties must be rejected with clear error messages, not silently ignored
NFR11: Tool call requests from agents must be validated against the step's declared tool list — undeclared tool calls must be rejected and logged, never executed
NFR12: Prompt injection mitigations must be applied: agent system prompts must clearly delineate system instructions from user content, and tool results must be treated as untrusted input
NFR13: Workflow execution must run under the Umbraco application's existing authentication and authorisation — only authenticated backoffice users can access the dashboard and trigger workflows
NFR14: The Bellissima dashboard extension must follow Umbraco's existing backoffice accessibility patterns — keyboard navigable, screen reader compatible for core navigation and workflow controls
NFR15: Chat panel content must be accessible — agent messages must be announced to screen readers, progress indicators must have appropriate ARIA labels
NFR16: All LLM calls must go through Umbraco.AI's IAIChatService — no direct HTTP calls to provider APIs. This ensures the middleware pipeline (logging, caching, rate limiting, governance) applies automatically
NFR17: The package must not interfere with other Umbraco.AI consumers — it must not modify global AI configuration, shared profiles, or middleware pipeline behaviour
NFR18: The engine must handle Umbraco.AI provider unavailability gracefully — if a provider is down or rate-limited, surface the error clearly in the chat panel rather than failing silently or crashing the background task
NFR19: The package must be removable without side effects — uninstalling the NuGet package should not leave behind database tables, configuration entries, or orphaned files that affect the Umbraco site
NFR20: Instance state must persist to disk atomically — partial writes must never corrupt instance state (use temp file + rename pattern)
NFR21: A workflow instance interrupted by app pool restart must be resumable from the last completed step without data loss
NFR22: Conversation history must be persisted incrementally (append-only) — a crash mid-step must not lose the conversation up to that point
NFR23: The engine must not crash the Umbraco application — all exceptions in background step execution must be caught, logged, and surfaced as step errors, never propagated to the host
NFR24: Failed steps must leave the instance in a recoverable state — the user can retry the failed step or cancel the workflow, never stuck in an unrecoverable limbo
NFR25: The tool system architecture must be extensible — adding new tools (including Umbraco API tools in v2) must require only implementing a tool interface and registering it, not modifying the engine core
NFR26: The engine must support configurable network access policies for fetch_url — v1 blocks private IP ranges by default, but the architecture must allow v2 to whitelist specific local endpoints (e.g., the Umbraco Management API) without bypassing SSRF protection globally

### Additional Requirements

- Starter template: `dotnet new umbracopackage-rcl -n Shallai.UmbracoAgentRunner` — official Umbraco RCL template selected for project scaffolding
- Frontend build toolchain: Vite with lib mode, outputting ES modules to wwwroot/App_Plugins/Shallai.UmbracoAgentRunner/, with Rollup externalising all @umbraco imports
- Testing infrastructure: NUnit 4 + NSubstitute for backend, @web/test-runner + @open-wc/testing for frontend, Playwright for E2E
- Single RCL project structure with clean engine boundary (no Umbraco dependencies in Engine/, Workflows/, Instances/, Tools/, Security/, Models/)
- Step executor uses IAIChatService with manual tool loop via GetChatClientAsync(profileId) — NOT the Umbraco.AI.Agent runtime (HttpContext dependency blocks IHostedService usage)
- Streaming protocol: SSE + POST (not SignalR) — simpler setup, AG-UI-compatible event format for future alignment
- Tool system: custom IWorkflowTool interface with per-step AIFunction creation via AIFunctionFactory.Create()
- Instance state: disk-based YAML + JSONL, atomic writes via temp file + rename, data root at {ContentRootPath}/App_Data/Shallai.UmbracoAgentRunner/instances/
- API structure: 11 Minimal API endpoints behind Umbraco backoffice auth policy
- Frontend state: Umbraco Context API with UmbObjectState/UmbArrayState observables (WorkflowContext, InstanceContext, ChatContext)
- Security: layered architecture — path sandboxing (canonical path + symlink check), SSRF protection (DNS resolve + IP blocklist), XSS sanitisation, tool access control, prompt injection mitigations
- DI registration: single AgentRunnerComposer : IComposer for all service registration
- Error handling: typed exceptions inheriting from AgentRunnerException, caught at API boundary, tool errors returned to LLM
- C# patterns: PascalCase naming, Async suffix on async methods, CancellationToken as last parameter, ConfigureAwait(false) in library code
- YAML schema: snake_case field naming throughout workflow.yaml
- SSE events: AG-UI-compatible dot.notation event names (run.started, text.delta, tool.start, step.finished, etc.)
- Frontend patterns: shallai- prefix for all custom elements, Shallai{Name}Element class naming, one component per file
- Logging: structured logging via ILogger<T> with consistent field names (WorkflowAlias, InstanceId, StepId, ToolName)
- Implementation sequence: scaffolding → YAML parsing/registry → instance management → step executor → tools → SSE streaming → dashboard UI → example workflow → packaging

### UX Design Requirements

UX-DR1: Dashboard shell component (shallai-dashboard) using umb-body-layout and umb-router-slot with three internal routes: /workflows, /workflows/{alias}, /instances/{id}
UX-DR2: Workflow list component (shallai-workflow-list) using uui-table with columns for name (clickable), step count, and mode badge — entire row clickable for navigation
UX-DR3: Instance list component (shallai-instance-list) using uui-table with columns for run number, status badge, step progress, relative timestamp, and overflow menu actions
UX-DR4: Instance detail component (shallai-instance-detail) using CSS Grid layout with 240px step progress sidebar and 1fr main content area, managed activeView state switching between chat and artifact views
UX-DR5: Step progress sidebar component (shallai-step-progress) with vertical step list — status icons (pending=circle-outline, active=sync animated, complete=check, error=close), step name, subtitle, click behaviour for completed/active steps
UX-DR6: Chat panel component (shallai-chat-panel) with uui-scroll-container for message list, auto-scroll with pause-on-scroll-up logic and "New messages" indicator, bottom-pinned input area with uui-textarea and send button
UX-DR7: Chat message component (shallai-chat-message) with role-based styling — agent (surface background), user (surface-emphasis background), system (no background, muted text, centred) — with streaming cursor indicator
UX-DR8: Tool call component (shallai-tool-call) with collapsed/expanded states — compact inline block showing tool name + summary, expandable to show arguments and results in monospace, keyboard accessible
UX-DR9: Markdown renderer component (shallai-markdown-renderer) with XSS sanitisation via HTML whitelist, UUI typography tokens for all heading/paragraph/list/table/code rendering
UX-DR10: Artifact viewer component (shallai-artifact-viewer) using uui-box and uui-scroll-container, displaying rendered markdown artifacts from completed steps
UX-DR11: Single primary action button pattern — "Start" (pending), "Continue" (step complete, interactive), "Retry" (error) in umb-body-layout header, buttons hidden when not applicable (not disabled)
UX-DR12: All colours via UUI design tokens — semantic mapping defined for status icons (--uui-color-disabled/current/positive/danger), chat message roles, surfaces, text, and borders
UX-DR13: Typography exclusively via UUI tokens — --uui-font-size-* and --uui-font-weight-* for all text, monospace only in expanded tool call detail and inline code
UX-DR14: Spacing exclusively via UUI tokens — --uui-size-space-* for internal padding, --uui-size-layout-* for section spacing
UX-DR15: Empty state patterns: single line explanatory text + actionable suggestion for no workflows, no instances, pre-start chat, and no-step-selected artifact viewer
UX-DR16: Error feedback pattern: plain-language error messages in chat panel with "[What happened]. [What to do]." format, no stack traces/HTTP codes/raw exceptions in UI
UX-DR17: Loading state patterns: uui-loader for data fetches, system messages for SSE connection, spinner-inside-button for POST actions with double-submit prevention
UX-DR18: Destructive action confirmation via uui-dialog for cancel and delete operations only — no confirmation for non-destructive actions (start, continue, retry)
UX-DR19: Navigation pattern: drill-down list→detail via umb-router-slot routes, back navigation via browser back or explicit back link, contextual title in umb-body-layout header
UX-DR20: Overflow menu pattern for instance row actions (View, Delete) and instance detail header secondary actions (Cancel, Delete)
UX-DR21: Chat panel accessibility: role="log" with aria-live="polite" on message list, aria-label on input, Enter to send/Shift+Enter for newline
UX-DR22: Step progress accessibility: role="list" with role="listitem" per step, aria-current="step" on active, status in aria-label
UX-DR23: Tool call accessibility: role="group", aria-label="Tool call: {toolName}", aria-expanded state, keyboard toggle via Enter/Space
UX-DR24: Streaming indicator accessibility: aria-live="polite" announcement for "Agent is responding..." and "Response complete"
UX-DR25: Keyboard navigation: Tab through step progress → chat area → input → send button → header actions, visible focus via --uui-color-focus
UX-DR26: Focus management: move focus to new content area when switching between chat and artifact views
UX-DR27: Auto-present final artifact: when last step completes, artifact viewer auto-activates to show final report without user action
UX-DR28: Provider prerequisite check: detect on instance creation whether Umbraco.AI has a provider configured, show inline guidance message if not
UX-DR29: SSE event consumption: EventSource API for streaming, events mapped to chat messages (text.delta), tool call blocks (tool.start/tool.end/tool.result), step transitions (step.started/step.finished), and error states (run.error)
UX-DR30: Custom event communication between components: step-selected from step progress, message-sent from chat panel, parent (instance-detail) manages SSE and distributes events to children

### FR Coverage Map

FR1: Epic 2 - Workflow discovery from folders on disk
FR2: Epic 2 - Parse and validate workflow.yaml against JSON Schema
FR3: Epic 2 - Load agent prompt markdown files
FR4: Epic 2 - Add workflow by dropping folder
FR5: Epic 2 - Report validation errors for malformed workflow.yaml
FR6: Epic 3 - Create new workflow instance
FR7: Epic 3 - Persist instance state to disk
FR8: Epic 3 - View list of workflow instances and status
FR9: Epic 3 - Track current step and produced artifacts
FR10: Epic 3 - Resume interrupted workflow instance
FR11: Epic 4 - Assemble agent prompt from markdown + sidecar + runtime context
FR12: Epic 4 - Load sidecar instruction files
FR13: Epic 4 - Execute step via IAIChatService with resolved profile
FR14: Epic 4 - Resolve profile via fallback chain (step → workflow → site default)
FR15: Epic 4 - Execute steps as background tasks via IHostedService
FR16: Epic 4 - Detect step completion via output file verification
FR17: Epic 5 - read_file tool within instance folder
FR18: Epic 5 - write_file tool within instance folder
FR19: Epic 5 - list_files tool within instance folder
FR20: Epic 5 - fetch_url tool for external URLs
FR21: Epic 5 - Restrict tool access to declared tools per step
FR22: Epic 5 - Sandbox file operations to instance folder
FR23: Epic 5 - Pluggable tool interface for extensibility
FR24: Epic 5 - Resolve tool names to registered implementations at runtime
FR25: Epic 4 - Declare reads_from for artifact consumption
FR26: Epic 4 - Declare writes_to for artifact production
FR27: Epic 4 - Make prior step artifacts available in agent context
FR28: Epic 4 - Any step can read artifacts from any prior step
FR29: Epic 4 - Interactive mode step-by-step advancement
FR30: Epic 4 - Autonomous mode auto-advance on completion check
FR31: Epic 4 - Set execution mode per workflow in workflow.yaml
FR32: Epic 2 - View all available workflows in Bellissima dashboard
FR33: Epic 3 - Create new workflow instance from dashboard
FR34: Epic 3 - View progress of running workflow
FR35: Epic 8 - View intermediate artifacts from completed steps
FR36: Epic 8 - Advance to next step in interactive mode from dashboard
FR37: Epic 2 - Display workflow metadata (name, description, steps, mode)
FR38: Epic 6 - View agent messages in real-time via chat panel
FR39: Epic 6 - Stream messages via SSE as agent works
FR40: Epic 6 - See tool call activity in chat panel
FR41: Epic 6 - Send messages to agent during step execution
FR42: Epic 6 - Maintain multi-turn conversation context within a step
FR43: Epic 6 - Persist agent conversation history per step
FR44: Epic 6 - Review full conversation history of completed step
FR45: Epic 7 - Detect and handle LLM provider errors
FR46: Epic 7 - Retry failed LLM call from chat interface
FR47: Epic 7 - Roll back failed message from conversation context on retry
FR48: Epic 7 - Preserve error context for agent learning
FR49: Epic 3 - Track granular step status (pending, active, complete, error)
FR50: Epic 3 - Cancel a running workflow instance
FR51: Epic 3 - Delete completed or cancelled workflow instances
FR52: Epic 3 - Prevent concurrent execution on same instance
FR53: Epic 4 - Detect whether Umbraco.AI has a provider configured
FR54: Epic 3 - Configure site-level default profile
FR55: Epic 4 - Report error when step references missing profile
FR56: Epic 5 - Validate workflow.yaml only references allowed tools
FR57: Epic 6 - Sanitise agent-produced markdown before rendering
FR58: Epic 5 - Reject file tool operations outside instance folder
FR59: Epic 5 - Restrict agents to declared tools only
FR60: Epic 5 - Block fetch_url to private/internal IP ranges (SSRF)
FR61: Epic 5 - Enforce response size limits on fetch_url
FR62: Epic 5 - Enforce request timeouts on fetch_url
FR63: Epic 8 - Browse all files produced by a workflow instance
FR64: Epic 8 - View artifact content with markdown rendering
FR65: Epic 9 - Ship pre-built Content Quality Audit workflow
FR66: Epic 9 - Example workflow produces useful audit report
FR67: Epic 9 - Ship JSON Schema for workflow.yaml
FR68: Epic 9 - Ship getting started guide
FR69: Epic 9 - Ship workflow authoring guide

## Epic List

### Epic 1: Project Foundation & Developer Onboarding
A developer can install the NuGet package into an Umbraco 17 site and see the Agent Workflows dashboard section in the backoffice — confirming the package is alive and working.
**FRs covered:** (Scaffolding — enables all FRs, no direct FR mapping)

### Epic 2: Workflow Discovery & Browsing
A developer can drop a workflow folder into their project and see it listed in the backoffice dashboard with its name, description, step count, and mode.
**FRs covered:** FR1, FR2, FR3, FR4, FR5, FR32, FR37

### Epic 3: Instance Management & Navigation
A developer or editor can create workflow runs, see their list with status, navigate into instance detail, and delete/manage previous runs.
**FRs covered:** FR6, FR7, FR8, FR9, FR10, FR33, FR34, FR49, FR50, FR51, FR52, FR54

### Epic 4: Step Execution Engine
A developer or editor can start a workflow and watch an agent execute a step — sending the assembled prompt to Umbraco.AI, using the correct profile, and producing output artifacts.
**FRs covered:** FR11, FR12, FR13, FR14, FR15, FR16, FR25, FR26, FR27, FR28, FR29, FR30, FR31, FR53, FR55

### Epic 5: Tool System
Agents can interact with files and fetch external URLs through a sandboxed, extensible tool system — with per-step tool access control.
**FRs covered:** FR17, FR18, FR19, FR20, FR21, FR22, FR23, FR24, FR56, FR58, FR59, FR60, FR61, FR62

### Epic 6: Real-Time Chat Experience
A developer or editor can watch agent output stream in real-time in a chat panel, see tool call activity, send messages to the agent mid-step, and review conversation history.
**FRs covered:** FR38, FR39, FR40, FR41, FR42, FR43, FR44, FR57

### Epic 7: Error Handling & Recovery
When something goes wrong, users see clear plain-language errors and can retry failed steps or cancel workflows — never stuck in an unrecoverable state.
**Stories:** 7.0 Interactive mode label/UX cleanup (retro), 7.1 LLM error detection, 7.2 Step retry with context management
**FRs covered:** FR45, FR46, FR47, FR48

### Epic 8: Artifact Browsing & Review
A developer or editor can browse all files produced by a workflow, view rendered markdown artifacts inline in the dashboard, and step through each step's output.
**FRs covered:** FR35, FR36, FR63, FR64

### Pre-Epic 9: Rename Shallai to AgentRun (Story R1)
The entire codebase is renamed from Shallai/UmbracoAgentRunner to AgentRun/Umbraco — solution, projects, namespaces, classes, frontend components, config, API routes, and build output. Zero logic changes, one commit.
**FRs covered:** (Naming — prerequisite for Epic 9 packaging and Marketplace listing)

### Epic 9: Example Workflow, Documentation & Packaging
The package ships with a working Content Quality Audit workflow, JSON Schema for IDE support, and documentation — ready to install from NuGet and run out-of-the-box.
**FRs covered:** FR65, FR66, FR67, FR68, FR69

### Epic 10: Ship Readiness & Stability
Known bugs and gaps are fixed before community launch — instance concurrency locking, context management for long conversations, multi-turn interactive fix, and open source licence decision.
**FRs covered:** FR52 (full implementation), plus stability improvements beyond original FR scope

### Epic 11: Adoption Accelerators
Additional workflow templates, tree navigation UX overhaul, and basic token usage logging drive adoption after launch.
**FRs covered:** (Post-V1 adoption — extends beyond original FR scope)

### Epic 12: Pro Foundation
Storage provider abstraction, database storage provider, and package split lay the architectural foundation for the commercial Pro package.
**FRs covered:** (Commercial foundation — extends beyond original FR scope)

## Epic 1: Project Foundation & Developer Onboarding

A developer can install the NuGet package into an Umbraco 17 site and see the Agent Workflows dashboard section in the backoffice — confirming the package is alive and working.

### Story 1.1: Scaffold RCL Package Project

As a developer,
I want a properly structured .NET RCL package project with a test project,
So that I have the foundation to build the workflow engine as a distributable NuGet package.

**Acceptance Criteria:**

**Given** the Umbraco package RCL template is installed
**When** the project is scaffolded with `dotnet new umbracopackage-rcl -n Shallai.UmbracoAgentRunner`
**Then** a Shallai.UmbracoAgentRunner.csproj exists targeting .NET 10 with Microsoft.NET.Sdk.Razor SDK
**And** a Shallai.UmbracoAgentRunner.Tests.csproj exists with NUnit 4 and NSubstitute dependencies
**And** the test Umbraco site project references the RCL package project
**And** the solution builds successfully with `dotnet build`
**And** the RCL project contains the initial folder structure: Composers/, Services/, Models/, Tools/, Engine/, Workflows/, Instances/, Security/, Configuration/, Endpoints/
**And** a GlobalUsings.cs file contains shared using statements
**And** an AgentRunnerComposer.cs exists implementing IComposer with an empty DI registration skeleton
**And** an AgentRunnerOptions.cs exists with placeholder configuration properties (DataRootPath, DefaultProfile, WorkflowPath)

### Story 1.2: Frontend Build Toolchain

As a developer,
I want the Vite + Lit + TypeScript frontend toolchain configured in the Client/ folder,
So that I can build Bellissima dashboard components that compile to the correct output location.

**Acceptance Criteria:**

**Given** the RCL project from Story 1.1 exists
**When** the Client/ folder is set up with package.json, vite.config.ts, and tsconfig.json
**Then** `npm install` in Client/ installs all dependencies including lit, @umbraco-cms/backoffice (devDependency), and vite
**And** vite.config.ts is configured in lib mode outputting ES modules to wwwroot/App_Plugins/Shallai.UmbracoAgentRunner/
**And** Rollup config externalises all @umbraco imports (provided by host at runtime)
**And** `npm run build` compiles a shallai-umbraco-agent-runner.js bundle to the wwwroot output directory
**And** a public/umbraco-package.json exists and is copied to wwwroot on build
**And** `npm run watch` enables hot-rebuild during development
**And** the Client/src/ folder contains the initial file structure: index.ts, manifests.ts, components/, contexts/, api/, utils/

### Story 1.3: Dashboard Shell & Empty Views

As a developer,
I want to see an "Agent Workflows" section in the Umbraco backoffice with navigable views,
So that I can confirm the package is installed and the dashboard extension is registered correctly.

**Acceptance Criteria:**

**Given** the frontend toolchain from Story 1.2 is configured and umbraco-package.json registers a backofficeEntryPoint extension
**When** the Umbraco site starts and a user navigates to the backoffice
**Then** an "Agent Workflows" entry appears in the backoffice section sidebar
**And** clicking it loads the shallai-dashboard component using umb-body-layout
**And** the dashboard uses umb-router-slot with three internal routes: /workflows, /workflows/{alias}, /instances/{id}
**And** placeholder components exist for shallai-workflow-list, shallai-instance-list, and shallai-instance-detail
**And** each placeholder displays a simple message confirming the route is active (e.g. "Workflow list view")
**And** navigating between routes works via internal routing without full page reload
**And** the dashboard follows UUI design token conventions for colours, typography, and spacing

## Epic 2: Workflow Discovery & Browsing

A developer can drop a workflow folder into their project and see it listed in the backoffice dashboard with its name, description, step count, and mode.

### Story 2.1: Workflow YAML Parsing & Validation

As a developer,
I want the engine to parse workflow.yaml files and validate them against a JSON Schema,
So that malformed workflow definitions are caught early with clear error messages.

**Acceptance Criteria:**

**Given** a workflow folder containing a workflow.yaml file
**When** the engine parses the file using YamlDotNet
**Then** a WorkflowDefinition model is populated with name, description, mode, default_profile, and steps array
**And** each step is parsed into a StepDefinition model with id, name, agent, profile, tools, reads_from, writes_to, and completion_check
**And** the YAML uses snake_case field naming throughout
**And** the workflow.yaml is validated against the JSON Schema
**And** a malformed workflow.yaml produces a clear, actionable error message identifying the specific validation failure
**And** a workflow.yaml with unexpected properties is rejected, not silently ignored (NFR10)
**And** a WorkflowValidator class encapsulates all validation logic
**And** unit tests verify parsing of valid workflows, rejection of invalid workflows, and error message clarity

### Story 2.2: Workflow Registry & Discovery

As a developer,
I want the engine to automatically discover workflow definitions from folders on disk at startup,
So that I can add new workflows by dropping a folder without any code registration.

**Acceptance Criteria:**

**Given** a configured workflows directory path in AgentRunnerOptions
**When** the Umbraco application starts
**Then** the WorkflowRegistry scans all subdirectories for workflow.yaml files
**And** valid workflows are loaded and registered with their alias (derived from folder name)
**And** agent prompt markdown files referenced by workflow steps are verified to exist
**And** workflows with validation errors are logged with clear messages but do not prevent other workflows from loading
**And** the IWorkflowRegistry interface exposes methods to list all workflows and get a workflow by alias
**And** the WorkflowRegistry is registered as a singleton in AgentRunnerComposer
**And** unit tests verify discovery of multiple workflows, handling of missing agent files, and graceful handling of invalid workflows alongside valid ones

### Story 2.3: Workflow List API & Dashboard

As a developer or editor,
I want to see all available workflows listed in the Bellissima dashboard,
So that I can browse what workflows are available and select one to run.

**Acceptance Criteria:**

**Given** the WorkflowRegistry has discovered one or more valid workflows
**When** the user navigates to the Agent Workflows dashboard
**Then** a GET /umbraco/api/shallai/workflows endpoint returns a JSON array of workflow summaries (alias, name, description, stepCount, mode)
**And** the endpoint requires backoffice authentication (NFR13)
**And** the shallai-workflow-list component displays workflows in a uui-table with columns: Name (clickable), Steps (count), Mode (badge)
**And** clicking a workflow row navigates to the instance list view for that workflow (/workflows/{alias})
**And** the entire row is clickable, not just the name column
**And** when no workflows are found, an empty state message is displayed: "No workflows found. Add a workflow folder to your project's workflows directory."
**And** the workflow list loads within 2 seconds (NFR2)
**And** all colours, typography, and spacing use UUI design tokens

## Epic 3: Instance Management & Navigation

A developer or editor can create workflow runs, see their list with status, navigate into instance detail, and delete/manage previous runs.

### Story 3.1: Instance State Management

As a developer,
I want the engine to manage workflow instance state on disk with atomic writes,
So that instance state survives app pool restarts and is never corrupted by partial writes.

**Acceptance Criteria:**

**Given** a valid workflow definition exists in the registry
**When** a new instance is created via the InstanceManager
**Then** an instance folder is created at {DataRootPath}/{workflowAlias}/{instanceId}/
**And** an instance.yaml file is written containing workflowAlias, currentStepIndex (0), status (pending), steps array with per-step status (all pending), createdAt, updatedAt, and createdBy
**And** all writes to instance.yaml use atomic temp file + rename pattern (NFR20)
**And** the InstanceManager can read back instance state from disk
**And** the InstanceManager can update step statuses (pending, active, complete, error) and persist changes atomically
**And** the InstanceManager prevents concurrent execution on the same instance by checking/setting a running flag (FR52)
**And** a site-level default profile can be configured via AgentRunnerOptions (FR54)
**And** an interrupted instance (status: running with a completed step) can be resumed from the last completed step (FR10, NFR21)
**And** the IInstanceManager interface is registered as a singleton in AgentRunnerComposer
**And** unit tests verify creation, read, update, atomic writes, concurrent execution prevention, and resume scenarios

### Story 3.2: Instance API Endpoints

As a developer or editor,
I want API endpoints to create, list, view, cancel, and delete workflow instances,
So that the dashboard can manage workflow runs through a standard REST interface.

**Acceptance Criteria:**

**Given** the InstanceManager from Story 3.1 is available via DI
**When** the API endpoints are registered in AgentRunnerComposer
**Then** POST /umbraco/api/shallai/instances creates a new instance for a given workflowAlias and returns JSON with id, workflowAlias, status, currentStepIndex, createdAt
**And** GET /umbraco/api/shallai/instances returns a JSON array of all instances with status, current step, and timestamps
**And** GET /umbraco/api/shallai/instances?workflowAlias={alias} filters instances by workflow
**And** GET /umbraco/api/shallai/instances/{id} returns full instance detail including per-step statuses
**And** POST /umbraco/api/shallai/instances/{id}/cancel sets instance status to cancelled and stops any running execution (FR50)
**And** DELETE /umbraco/api/shallai/instances/{id} removes the instance folder from disk, only allowed for completed or cancelled instances (FR51)
**And** all endpoints require backoffice authentication via [Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)] (NFR13)
**And** error responses use the standard format: { error, message } with appropriate HTTP status codes
**And** JSON responses use camelCase field naming via System.Text.Json

### Story 3.3: Instance List Dashboard

As a developer or editor,
I want to see all runs for a workflow listed in the dashboard with their status,
So that I can track previous runs, start new ones, and manage completed runs.

**Acceptance Criteria:**

**Given** the user has navigated to a workflow's instance list view (/workflows/{alias})
**When** the shallai-instance-list component loads
**Then** it fetches instances from GET /instances?workflowAlias={alias} and displays them in a uui-table
**And** the table has columns: Run (sequential number), Status (uui-badge — Pending/Running/Complete/Error/Cancelled), Step ("2 of 3" or "Complete"), Started (relative time with full datetime tooltip), Actions (overflow menu)
**And** clicking a row navigates to the instance detail view (/instances/{id})
**And** a "New Run" primary button in the umb-body-layout header creates a new instance via POST /instances and navigates to its detail view
**And** the overflow menu shows "View" (navigates to detail) and "Delete" (for completed/cancelled instances only)
**And** clicking "Delete" shows a uui-dialog confirmation: "Delete this run? This cannot be undone." (UX-DR18)
**And** when no instances exist, an empty state message is displayed: "No runs yet. Click 'New Run' to start."
**And** the header shows contextual title: workflow name (e.g. "Content Quality Audit")
**And** back navigation returns to the workflow list
**And** all styling uses UUI design tokens

### Story 3.4: Instance Detail View & Step Progress Sidebar

As a developer or editor,
I want to see the full detail of a workflow run with a step progress sidebar,
So that I can see which step is active, which are complete, and what comes next.

**Acceptance Criteria:**

**Given** the user has navigated to an instance detail view (/instances/{id})
**When** the shallai-instance-detail component loads
**Then** it fetches instance detail from GET /instances/{id} and renders a CSS Grid layout with 240px left sidebar and 1fr main content area
**And** the shallai-step-progress sidebar displays a vertical list of all steps with status icons: pending (circle-outline, --uui-color-disabled), active (sync animated, --uui-color-current), complete (check, --uui-color-positive), error (close, --uui-color-danger)
**And** each step shows its human-readable name from workflow.yaml and a subtitle (Pending/Running.../artifact filename/Failed)
**And** clicking a completed step fires a step-selected custom event (for future artifact viewing)
**And** clicking the active step fires a step-selected event (for future chat panel display)
**And** clicking a pending step has no action
**And** the selected step has a --uui-color-current left border highlight
**And** the umb-body-layout header shows contextual title: "{workflow name} — Run #{number}"
**And** the header shows the appropriate primary action button: "Start" when status is pending (button hidden when no action is available)
**And** a secondary "Cancel" button appears in the overflow menu when the instance is running, requiring uui-dialog confirmation
**And** the step progress sidebar is accessible: role="list", role="listitem" per step, aria-current="step" on active, status in aria-label (UX-DR22)
**And** the main content area shows a placeholder message: "Click 'Start' to begin the workflow."
**And** back navigation returns to the instance list

## Epic 4: Step Execution Engine

A developer or editor can start a workflow and watch an agent execute a step — sending the assembled prompt to Umbraco.AI, using the correct profile, and producing output artifacts.

### Story 4.1: Prompt Assembly

As a developer,
I want the engine to assemble a complete agent prompt from markdown files, sidecar instructions, and runtime context,
So that each step's agent receives all the information it needs to execute its task.

**Acceptance Criteria:**

**Given** a workflow step with an agent markdown file path and optional sidecar instructions
**When** the PromptAssembler builds the prompt for a step execution
**Then** the agent's markdown file is loaded from the path specified in the step definition
**And** if a sidecars/{step-id}/instructions.md file exists, its content is injected as supplementary context alongside the agent markdown (FR12)
**And** runtime context is appended including: step name, step description, declared tool names with descriptions, and a list of prior artifact file paths with existence flags (FR11)
**And** prior artifacts from any completed step (not just the immediately preceding one) are referenced with their file paths and existence status (FR27, FR28)
**And** the prompt structure follows the architecture's defined order: [Agent Instructions] --- [Sidecar Instructions] --- [Runtime Context] --- [Tool Results Are Untrusted] (NFR12)
**And** tool results are clearly delimited as untrusted input in the prompt structure
**And** the IPromptAssembler interface is registered in AgentRunnerComposer
**And** unit tests verify prompt assembly with and without sidecars, with multiple prior artifacts, and with missing agent files

### Story 4.2: Profile Resolution

As a developer,
I want the engine to resolve the correct Umbraco.AI profile for each step via a fallback chain,
So that each step uses the appropriate model and configuration without requiring explicit profile declaration on every step.

**Acceptance Criteria:**

**Given** a step is about to execute
**When** the ProfileResolver resolves the profile
**Then** it checks for a step-level profile alias first
**And** if no step-level profile exists, it falls back to the workflow's default_profile
**And** if no workflow default_profile exists, it falls back to the site-level default profile from AgentRunnerOptions (FR14)
**And** the resolved profile alias is used to call IAIChatService.GetChatClientAsync(profileId) to obtain a middleware-wrapped IChatClient
**And** if the resolved profile alias does not exist in Umbraco.AI configuration, a ProfileNotFoundException is thrown with a clear message: "Profile '{alias}' not found in Umbraco.AI configuration" (FR55)
**And** a provider prerequisite check detects whether Umbraco.AI has at least one provider configured, returning a clear status for the dashboard to display guidance if not (FR53)
**And** the IProfileResolver interface is registered in AgentRunnerComposer
**And** unit tests verify the 3-tier fallback chain, missing profile error, and provider prerequisite detection

### Story 4.3: Step Executor & Tool Loop

As a developer,
I want the engine to execute a step by calling IAIChatService with the assembled prompt and handling tool calls in a loop,
So that agents can use tools iteratively until their task is complete.

**Acceptance Criteria:**

**Given** a prompt has been assembled and a profile resolved for a step
**When** the StepExecutor executes the step
**Then** it calls GetChatClientAsync(profileId) to obtain an IChatClient with the full Umbraco.AI middleware pipeline (NFR16)
**And** it sends the assembled prompt as messages with declared tools passed via ChatOptions.Tools
**And** when the response contains FunctionCallContent, each tool call name is validated against the step's declared tools list (NFR11)
**And** undeclared tool call attempts are rejected with an error logged and error result returned to the LLM (FR59)
**And** valid tool calls are dispatched to the corresponding IWorkflowTool implementation via the ToolLoop
**And** tool results are sent back as FunctionResultContent and the loop continues until no tool calls remain in the response
**And** step execution runs as a background task via IHostedService, never blocking the web request pipeline (FR15, NFR4)
**And** all exceptions during step execution are caught, logged, and surfaced as step errors — never crashing the host application (NFR23)
**And** tool execution errors are returned to the LLM as error results, not thrown — allowing the LLM to decide recovery strategy
**And** the IStepExecutor interface wraps the implementation for future swapability
**And** structured logging uses consistent field names: WorkflowAlias, InstanceId, StepId, ToolName
**And** unit tests verify the tool loop with mocked IChatClient, tool call validation, undeclared tool rejection, and error handling

### Story 4.4: Artifact Handoff & Completion Checking

As a developer,
I want steps to declare their input and output artifacts with automatic completion checking,
So that the engine knows when a step is done and can make artifacts available to subsequent steps.

**Acceptance Criteria:**

**Given** a step definition declares reads_from and writes_to artifact lists
**When** the step executes
**Then** files listed in reads_from are verified to exist in the instance folder before execution begins
**And** the PromptAssembler includes the content or paths of reads_from artifacts in the agent context (FR25, FR27)
**And** writes_to declarations inform the agent what files it is expected to produce (FR26)
**And** after the agent completes its response (no more tool calls), the CompletionChecker verifies all files listed in completion_check.files_exist are present in the instance folder (FR16)
**And** if completion check passes, step status transitions to complete and instance state is updated atomically
**And** if completion check fails, step status transitions to error with a message indicating which expected files are missing
**And** instance state (currentStepIndex, step statuses, updatedAt) is persisted after each step status transition
**And** the ICompletionChecker interface is registered in AgentRunnerComposer
**And** unit tests verify completion checking with all files present, missing files, and state transitions

### Story 4.5: Workflow Modes & Step Advancement

As a developer or editor,
I want to choose between interactive mode (manual step advancement) and autonomous mode (automatic advancement),
So that I can review intermediate outputs before proceeding or let the workflow run unattended.

**Acceptance Criteria:**

**Given** a workflow defines mode as "interactive" or "autonomous" in workflow.yaml (FR31)
**When** a step completes in interactive mode
**Then** the engine waits for the user to click "Continue" before starting the next step (FR29)
**And** the dashboard header shows a "Continue" primary action button when the current step is complete
**And** POST /umbraco/api/shallai/instances/{id}/start advances to the next step and opens an SSE stream for the step execution
**And** the SSE stream emits AG-UI-compatible events: run.started, text.delta, tool.start, tool.args, tool.end, tool.result, step.started, step.finished

**Given** a workflow defines mode as "autonomous"
**When** a step completes and the completion check passes
**Then** the engine automatically starts the next step within 5 seconds (FR30, NFR3)
**And** a system message is emitted: "Auto-advancing to {next step name}..."
**And** the process continues until all steps are complete or an error occurs

**Given** the final step of any workflow completes
**When** all steps show complete status
**Then** the instance status is set to completed
**And** the primary action button is removed from the header (workflow is done)
**And** a run.finished SSE event is emitted

**Given** a user sends a POST to /instances/{id}/start for a pending instance
**When** the provider prerequisite check detects no configured provider
**Then** the endpoint returns an error response and the dashboard displays an inline guidance message: "Configure an AI provider in Umbraco.AI before workflows can run." (FR53, UX-DR28)

## Epic 5: Tool System

Agents can interact with files and fetch external URLs through a sandboxed, extensible tool system — with per-step tool access control.

### Story 5.1: Tool Interface & Registration

As a developer,
I want an extensible tool interface with DI-based registration and per-step tool filtering,
So that new tools can be added without modifying the engine and each step only has access to its declared tools.

**Acceptance Criteria:**

**Given** the IWorkflowTool interface defines Name (string), Description (string), and ExecuteAsync(arguments, context, cancellationToken)
**When** a tool implementation is registered in DI via AgentRunnerComposer
**Then** each tool is registered individually as IWorkflowTool: services.AddSingleton<IWorkflowTool, ReadFileTool>()
**And** the engine resolves tools by matching IWorkflowTool.Name against the step's declared tools[] list at runtime (FR24)
**And** resolved tools are wrapped as AIFunction instances via AIFunctionFactory.Create(tool.ExecuteAsync, tool.Name, tool.Description)
**And** only AIFunction instances for declared tools are passed in ChatOptions.Tools for that step (FR21)
**And** ToolExecutionContext provides: instance folder path, instance ID, step ID, and CancellationToken — no access to engine internals
**And** tool execution errors throw ToolExecutionException which the engine catches and returns to the LLM as error results
**And** adding a new tool requires only implementing IWorkflowTool and registering in DI — no engine modification needed (NFR25)
**And** unit tests verify tool resolution by name, per-step filtering, AIFunction wrapping, and error handling

### Story 5.2: File Tools with Path Sandboxing

As a developer,
I want read_file, write_file, and list_files tools that are sandboxed to the instance folder,
So that agents can interact with files safely without risk of path traversal attacks.

**Acceptance Criteria:**

**Given** an agent calls the read_file tool with a file path argument
**When** the ReadFileTool executes
**Then** it reads and returns the file content from within the instance folder
**And** the requested path is resolved via Path.GetFullPath and verified to start with the instance folder path (NFR6)
**And** symlinks are detected via File.GetAttributes checking FileAttributes.ReparsePoint and rejected
**And** path traversal attempts using ../, URL encoding, or other techniques are blocked with a ToolExecutionException (FR58)
**And** a missing file returns a clear error message to the LLM

**Given** an agent calls the write_file tool with a path and content
**When** the WriteFileTool executes
**Then** it writes the content to the specified path within the instance folder
**And** the same path sandboxing validation is applied before writing
**And** parent directories are created if they don't exist

**Given** an agent calls the list_files tool
**When** the ListFilesTool executes
**Then** it returns a list of files in the instance folder (optionally filtered by a subdirectory argument)
**And** the same path sandboxing validation is applied

**And** PathSandbox is implemented as a static helper class in Security/ used by all file tools
**And** unit tests verify: valid reads/writes, path traversal rejection (../, symlinks, encoded paths), missing file handling, and directory listing

### Story 5.3: fetch_url Tool with SSRF Protection

As a developer,
I want a fetch_url tool that can retrieve external URLs with SSRF protection,
So that agents can access external content without risk of server-side request forgery.

**Acceptance Criteria:**

**Given** an agent calls the fetch_url tool with a URL argument
**When** the FetchUrlTool executes
**Then** the URL's hostname is resolved via Dns.GetHostAddressesAsync() before any connection is made
**And** resolved IP addresses are checked against the blocklist: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 127.0.0.0/8, 169.254.0.0/16, ::1, fe80::/10 (NFR7)
**And** if the resolved IP matches any blocked range, the request is rejected with a ToolExecutionException before the connection is established
**And** a 15-second HttpClient timeout is enforced (NFR8)
**And** response size is limited to 200KB for JSON content and 100KB for HTML/XML content (NFR8)
**And** responses exceeding the size limit are truncated with a message indicating truncation
**And** the tool returns the response body as a string to the LLM
**And** SsrfProtection is implemented as a class in Security/ encapsulating DNS resolution and IP validation
**And** the network access policy is configurable to allow v2 whitelisting of specific local endpoints without bypassing global SSRF protection (NFR26)
**And** unit tests verify: successful external fetch, SSRF blocking for all blocked ranges, timeout enforcement, size limit enforcement, and IPv6 blocking

### Story 5.4: Workflow Tool Validation

As a developer,
I want workflow.yaml validation to check that all referenced tools exist in the registered tool set,
So that invalid tool references are caught at load time rather than at runtime.

**Acceptance Criteria:**

**Given** a workflow.yaml file declares tools in a step's tools[] array
**When** the WorkflowRegistry loads and validates the workflow
**Then** each declared tool name is checked against the set of registered IWorkflowTool implementations
**And** if a step references a tool name that is not registered, the workflow is rejected with a clear error message: "Step '{stepId}' references unknown tool '{toolName}'. Available tools: {list}" (FR56)
**And** workflows with invalid tool references are logged as validation errors but do not prevent other valid workflows from loading
**And** the validation runs after DI registration is complete so all tools are available for checking
**And** unit tests verify: valid tool references pass, unknown tool references are rejected with correct error messages, and partial failures don't block other workflows

## Epic 6: Real-Time Chat Experience

A developer or editor can watch agent output stream in real-time in a chat panel, see tool call activity, send messages to the agent mid-step, and review conversation history.

### Story 6.1: Conversation Persistence

As a developer,
I want conversation history persisted incrementally per step in append-only JSONL files,
So that conversations survive crashes and can be reviewed after step completion.

**Acceptance Criteria:**

**Given** a step is executing and the agent produces messages
**When** each message (agent response, user message, tool call, tool result) is generated
**Then** the ConversationStore appends a JSON object per line to conversation-{stepId}.jsonl in the instance folder (FR43)
**And** each JSON object contains: role (assistant/user/system/tool), content, timestamp, and optional tool call data
**And** the append-only format ensures a crash mid-step loses at most the last partial line — all prior messages are preserved (NFR22)
**And** one JSONL file is created per step, keeping conversations isolated

**Given** a completed or errored step exists with conversation history
**When** the GET /umbraco/api/shallai/instances/{id}/conversation/{stepId} endpoint is called
**Then** it returns a JSON array of all messages for that step in chronological order (FR44)
**And** the endpoint requires backoffice authentication (NFR13)

**And** the IConversationStore interface is registered in AgentRunnerComposer
**And** unit tests verify: append writes, crash-safe partial write handling, per-step isolation, and history retrieval

### Story 6.2: Chat Panel & Message Streaming

As a developer or editor,
I want to watch agent output stream in real-time in a chat panel during step execution,
So that I can see what the agent is doing as it works.

**Acceptance Criteria:**

**Given** a step is executing and SSE events are streaming
**When** the shallai-chat-panel component is active in the instance detail main area
**Then** it connects to the SSE stream via the EventSource API (UX-DR29)
**And** text.delta events are rendered as streaming text in a shallai-chat-message component with role "agent"
**And** the streaming message shows a blinking cursor at the end while text is actively arriving (UX-DR7)
**And** step.started events produce a system message: "Starting {step name}..."
**And** step.finished events produce a system message: "{step name} completed — {artifact filename} created"
**And** the chat panel uses uui-scroll-container with auto-scroll to follow new content
**And** when the user scrolls up, auto-scroll pauses
**And** a "New messages" floating indicator appears at the bottom when auto-scrolled is paused and new messages arrive
**And** clicking the indicator or scrolling to bottom resumes auto-scroll (UX-DR6)
**And** agent messages use --uui-color-surface background, system messages use no background with --uui-color-text-alt and --uui-font-size-s (UX-DR7)
**And** the chat message list has role="log" and aria-live="polite" for screen reader announcements (UX-DR21)
**And** a streaming indicator aria-live region announces "Agent is responding..." during streaming and "Response complete" when done (UX-DR24)
**And** when reviewing a completed step's conversation, messages are loaded from the conversation history endpoint in read-only mode

### Story 6.3: Tool Call Display

As a developer or editor,
I want to see tool call activity displayed inline in the chat panel,
So that I can understand what actions the agent is taking during execution.

**Acceptance Criteria:**

**Given** the SSE stream emits tool.start, tool.args, tool.end, and tool.result events
**When** a tool call occurs during step execution
**Then** a shallai-tool-call component is rendered inline within the current agent message (UX-DR8)
**And** the collapsed view shows: a wrench icon (uui-icon), tool name, and a one-line summary (e.g. filename or URL)
**And** clicking the component expands to show arguments and results in monospace font (--uui-font-size-s)
**And** the expand/collapse toggle is keyboard accessible via Enter/Space

**Given** a tool call is in progress
**When** tool.start has been received but tool.result has not
**Then** the tool call block shows a uui-loader spinner replacing the expand icon and --uui-color-border outline

**Given** a tool call has completed
**When** tool.result has been received
**Then** the spinner is replaced with a uui-icon check indicator and the block becomes expandable

**Given** a tool call has errored
**When** tool.result contains an error
**Then** the block shows --uui-color-danger border and the expanded view shows the error message

**And** tool call blocks have role="group", aria-label="Tool call: {toolName}", and aria-expanded state (UX-DR23)
**And** expanding a tool call does not affect chat scroll position

### Story 6.4: User Messaging & Multi-Turn Conversation

As a developer or editor,
I want to send messages to an agent during step execution and have a back-and-forth conversation,
So that I can guide, clarify, or provide additional context to the agent while it works.

**Acceptance Criteria:**

**Given** a step is actively executing
**When** the user types a message in the chat input (uui-textarea) and presses Enter or clicks the Send button
**Then** the message is sent via POST /umbraco/api/shallai/instances/{id}/message (FR41)
**And** the user's message appears in the chat panel as a shallai-chat-message with role "user" and --uui-color-surface-emphasis background
**And** the agent receives the message in context and responds, maintaining multi-turn conversation within the step (FR42)
**And** the agent's response streams via SSE as with any other agent output
**And** Shift+Enter inserts a newline instead of sending (UX-DR21)
**And** the Send button is disabled when the input is empty
**And** the input has aria-label="Message to agent" (UX-DR21)

**Given** no step is currently active (step complete, pending, or error)
**When** the user views the chat panel
**Then** the input is disabled with an appropriate placeholder: "Step complete" (after completion), "Click 'Start' to begin the workflow." (before start)

**Given** agent messages may contain markdown formatting
**When** agent message content is rendered in the chat panel
**Then** the content is sanitised before rendering to prevent XSS — no raw HTML, script tags, or event handler attributes (FR57, NFR9)
**And** sanitisation uses a whitelist approach permitting only standard markdown-generated HTML elements

### Story 6.5: Interactive Mode UX Correction

As a developer or editor using an interactive workflow,
I want the UI to reflect that I am in control of step progression — not watching an automated process,
So that the experience matches the human-driven interactive model (PRD: "Human-in-the-loop is the safe, impressive default").

**Acceptance Criteria:**

**Given** an interactive workflow instance is loaded and no SSE stream is active
**When** the user views the instance detail
**Then** the active step shows "In progress" (not "Running...") with a static icon (no spin animation)
**And** no Cancel button or Running/Failed status badges are shown
**And** the chat input is enabled for the user to send a message

**Given** the user sends a message between turns (no active SSE stream)
**When** the message is submitted
**Then** the message is queued via POST /message and a new SSE stream opens automatically
**And** the step icon animates during streaming and returns to static when the response completes
**And** the chat input re-enables after the stream ends

**Given** the step's completion check passes after an agent response
**When** the user views the chat panel
**Then** a "Continue to next step" banner appears above the chat input
**And** clicking it advances to the next step, clears the chat, and activates the next step

**Note:** Frontend-only corrective story. No backend execution changes. Autonomous mode unchanged.

## Epic 7: Error Handling & Recovery

When something goes wrong, users see clear plain-language errors and can retry failed steps or cancel workflows — never stuck in an unrecoverable state.

### Story 7.0: Interactive Mode Label & UX Cleanup

As a developer or editor using an interactive workflow,
I want all UI labels, buttons, status text, and placeholders to reflect the interactive-first model,
So that the interface feels like a collaborative workspace rather than an autonomous process monitor.

**Acceptance Criteria:**

**Given** the instance list displays interactive workflow instances
**When** status tags, buttons, empty states, and confirmation modals are rendered
**Then** all labels use interactive-first language: "In progress" (not "Running"), "New session" (not "New Run"), "Error" subtitle (not "Failed")
**And** the "Failed" backend status displays as "In progress" in interactive mode (user can return and continue)
**And** the actions column uses a three-dot menu icon (not the drag/reorder icon)
**And** the Start button reads "Start" (not "Start conversation")
**And** the default chat placeholder reads "Send a message to start."
**And** autonomous mode labels are unchanged

**Note:** Frontend-only corrective story from Epic 6 retrospective. No backend execution changes. Autonomous mode unchanged.

### Story 7.1: LLM Error Detection & Reporting

As a developer or editor,
I want clear plain-language error messages when an LLM provider fails,
So that I know what went wrong and what to do next without needing technical knowledge.

**Acceptance Criteria:**

**Given** the step executor is calling IAIChatService and the LLM provider returns an error
**When** a rate limit, timeout, or API failure occurs
**Then** the engine catches the exception and sets the step status to "error" (NFR24)
**And** instance state is persisted atomically with the error status
**And** a run.error SSE event is emitted with a machine-readable error code and human-readable message
**And** the chat panel displays a system message in plain language: "[What happened]. [What to do]." (UX-DR16)
**And** example messages: "The AI provider returned a rate limit error. Wait a moment and retry." / "The AI provider timed out. Check your provider configuration and retry."
**And** no stack traces, HTTP status codes, or raw exception messages are shown in the UI
**And** the step progress sidebar updates to show error status (red cross icon, --uui-color-danger)
**And** the error does not crash the Umbraco application — the exception is caught and logged in the background task (NFR23)
**And** the instance remains in a recoverable state — the user can retry or cancel (NFR24)
**And** structured logging records the error with WorkflowAlias, InstanceId, StepId, and the original exception details at Error level
**And** unit tests verify error detection for rate limit, timeout, and generic API failure scenarios

### Story 7.2: Step Retry with Context Management

As a developer or editor,
I want to retry a failed step with the broken response rolled back from context,
So that the agent gets a clean retry without seeing its own failed output.

**Acceptance Criteria:**

**Given** a step is in error state
**When** the user clicks the "Retry" button in the dashboard header
**Then** a POST /umbraco/api/shallai/instances/{id}/retry request is sent
**And** the "Retry" button shows a uui-loader spinner during the request (UX-DR17)
**And** the failed assistant message is rolled back from the conversation context so the agent doesn't see its broken response on retry (FR47)
**And** the error result from the failed tool call (if applicable) is preserved in context so the agent can learn from the failure (FR48)
**And** the step status transitions from error back to active
**And** step execution resumes from the last good conversation state
**And** the SSE stream reopens and the agent continues working, with new messages streaming in the chat panel
**And** if the retry also fails, the step returns to error state and the user can retry again or cancel

**Given** a step is in error state
**When** the user views the instance detail
**Then** the primary action button in the header shows "Retry" (replacing Start/Continue) (UX-DR11)
**And** the "Cancel" option remains available in the overflow menu

**Given** a user navigates away from an errored instance and returns later
**When** the instance detail loads
**Then** the error state is preserved, the error message is visible in the chat history, and the "Retry" button is available in the header

## Epic 8: Artifact Browsing & Review

A developer or editor can browse all files produced by a workflow, view rendered markdown artifacts inline in the dashboard, and step through each step's output.

### Story 8.1: Artifact API & Markdown Renderer

As a developer or editor,
I want to retrieve artifact content via API and render markdown safely in the dashboard,
So that agent-produced documents are displayed as polished, readable content without security risks.

**Acceptance Criteria:**

**Given** a workflow instance has completed steps that produced artifact files
**When** the GET /umbraco/api/shallai/instances/{id}/artifacts/{filename} endpoint is called
**Then** it returns the artifact file content with Content-Type: text/markdown
**And** the endpoint requires backoffice authentication (NFR13)
**And** the filename is validated against path traversal (no ../, symlinks, or encoded paths)
**And** a missing artifact returns a 404 with a clear error message

**Given** raw markdown content from an artifact
**When** the shallai-markdown-renderer component renders it
**Then** the content is sanitised before rendering using a whitelist approach — only standard markdown-generated HTML elements are permitted: p, h1-h6, ul, ol, li, table, tr, td, th, a, strong, em, code, pre, blockquote, hr (NFR9)
**And** all raw HTML, script tags, event handler attributes, iframes, and javascript: URLs are stripped
**And** headings use UUI typography tokens (--uui-font-size-* for h1-h6)
**And** paragraphs use --uui-font-size-default with --uui-color-text
**And** tables use --uui-color-border for borders and --uui-color-surface-emphasis for header rows
**And** code blocks use monospace font with --uui-color-surface-emphasis background and --uui-size-space-3 padding
**And** links use --uui-color-interactive and open in new tab (target="_blank" rel="noopener")
**And** the rendered content looks like a professional document, not a code preview — proportional font for body text, proper heading hierarchy
**And** the markdown-sanitiser.ts utility is implemented in Client/src/utils/

### Story 8.2: Artifact Viewer & Step Review

As a developer or editor,
I want to view step artifacts inline in the dashboard by clicking completed steps,
So that I can review what each agent produced without leaving the backoffice.

**Acceptance Criteria:**

**Given** a step has completed and produced an artifact
**When** the user clicks the completed step in the step progress sidebar
**Then** the instance detail main area switches from chat panel to shallai-artifact-viewer (UX-DR4)
**And** the artifact viewer loads the step's artifact via GET /instances/{id}/artifacts/{filename}
**And** the artifact content is rendered using shallai-markdown-renderer within a uui-box and uui-scroll-container (UX-DR10)
**And** focus moves to the artifact viewer content area (UX-DR26)
**And** the selected step shows a --uui-color-current highlight in the sidebar

**Given** the user is viewing an artifact and clicks the active step in the sidebar
**When** the main area switches back to the chat panel
**Then** the chat panel resumes showing the current conversation with scroll position preserved
**And** focus moves to the chat panel area

**Given** the final step of a workflow completes
**When** all steps show complete status
**Then** the artifact viewer auto-activates to show the final step's artifact without user action (UX-DR27)
**And** a run.finished system message appears in the chat history: "Workflow complete."

**Given** no step has been selected for review
**When** the artifact viewer area is shown
**Then** an empty state message is displayed: "Select a completed step to view its output." (UX-DR15)

**Given** a completed instance is loaded from the instance list
**When** the user clicks any completed step
**Then** the artifact for that step is displayed, allowing full review of all step outputs (FR63)
**And** the step progress sidebar shows file browsing capability — each completed step's subtitle shows the artifact filename

## Pre-Epic 9: Rename Shallai to AgentRun

The entire codebase is renamed from Shallai/UmbracoAgentRunner to AgentRun/Umbraco before documentation, packaging, and Marketplace listing begin.

### Story R1: Rename Shallai to AgentRun

As a developer,
I want the entire codebase renamed from Shallai/UmbracoAgentRunner to AgentRun/Umbraco,
So that the package ships under its final product name before Epic 9.

**Acceptance Criteria:**

**Given** the rename is applied across all surfaces (solution, projects, namespaces, classes, frontend components, config, API routes, build output)
**When** `dotnet build AgentRun.Umbraco.slnx` and `npm run build` are run
**Then** everything builds with zero errors and zero references to old names
**And** `dotnet test AgentRun.Umbraco.slnx` and `npm test` pass all tests
**And** `grep -ri "shallai"` (excluding `_bmad-output/`, `.git/`, `node_modules/`) returns zero matches
**And** the TestSite runs and all existing functionality works under the new names
**And** `project-context.md` is regenerated with all new naming conventions

Full story spec: `_bmad-output/implementation-artifacts/R1-rename-shallai-to-agentrun.md`

## Epic 9: Example Workflow, Documentation & Packaging

The package ships with a working Content Quality Audit workflow, JSON Schema for IDE support, and documentation — ready to install from NuGet and run out-of-the-box.

### Story 9.1: Content Quality Audit Example Workflow

As a developer,
I want a pre-built example workflow that runs out-of-the-box after package installation,
So that I can see multi-agent orchestration in action without writing any workflow files myself.

**Acceptance Criteria:**

**Given** the package is installed and Umbraco.AI has at least one provider configured with a profile
**When** the developer opens the Agent Workflows dashboard
**Then** a "Content Quality Audit" workflow appears in the workflow list with description "Scans, analyses, and reports on content quality", 3 steps, interactive mode

**Given** the developer starts the Content Quality Audit workflow
**When** Step 1 (Content Scanner) executes
**Then** the scanner agent uses read_file and write_file tools to catalogue content (page titles, meta descriptions, headings, image alt text, word counts)
**And** it produces scan-results.md as its output artifact
**And** the agent prompt instructs it to produce structured, readable markdown output

**Given** Step 1 has completed and the developer advances to Step 2
**When** Step 2 (Quality Analyser) executes
**Then** the analyser agent reads scan-results.md and scores each page on SEO completeness, readability, accessibility, and content freshness
**And** it produces quality-scores.md as its output artifact

**Given** Step 2 has completed and the developer advances to Step 3
**When** Step 3 (Report Generator) executes
**Then** the reporter agent reads quality-scores.md and produces a prioritised action plan with executive summary, page-by-page findings, and recommended fixes
**And** it produces audit-report.md as its final artifact (FR66)
**And** the final report is structured, actionable, and understandable by a non-technical content manager

**And** the workflow.yaml uses a single default_profile (no per-step profile overrides) so it works with any single configured profile (FR65)
**And** the workflow folder is located at Workflows/content-quality-audit/ with workflow.yaml and agents/scanner.md, agents/analyser.md, agents/reporter.md
**And** all three agent prompts are well-crafted markdown files that produce high-quality output

### Story 9.2: JSON Schema for workflow.yaml

As a developer,
I want a JSON Schema for workflow.yaml that provides IDE validation and autocomplete,
So that I get instant feedback on schema errors and discover available fields while authoring workflows.

**Acceptance Criteria:**

**Given** a developer is editing a workflow.yaml file in VS Code or another JSON Schema-aware editor
**When** the JSON Schema is referenced (via $schema directive or editor configuration)
**Then** the IDE provides autocomplete for all workflow.yaml fields: name, description, mode, default_profile, steps
**And** the IDE provides autocomplete for step fields: id, name, agent, profile, tools, reads_from, writes_to, completion_check
**And** the IDE validates required fields and reports errors for missing or incorrectly typed values
**And** enum values are validated: mode must be "interactive" or "autonomous"
**And** the schema file is located at Schemas/workflow-schema.json and ships in the NuGet package (FR67)
**And** the schema includes descriptions for all fields to aid discoverability
**And** the example workflow.yaml references the schema via a comment or $schema property

### Story 9.3: Documentation & NuGet Packaging

As a developer,
I want clear documentation and a properly configured NuGet package,
So that I can install, configure, and extend the workflow engine with confidence.

**Acceptance Criteria:**

**Given** the NuGet package is published
**When** a developer runs `dotnet add package AgentRun.Umbraco`
**Then** the package installs cleanly into an Umbraco 17+ site with no manual configuration beyond Umbraco.AI profile setup
**And** the .csproj is configured with correct NuGet metadata: package ID, version, description, authors, license, project URL, tags
**And** the package includes compiled frontend assets in wwwroot/App_Plugins/
**And** the package includes the example workflow files
**And** the package includes the JSON Schema
**And** the package is removable without side effects — no database tables, configuration entries, or orphaned files left behind (NFR19)

**Given** a developer wants to get started with the package
**When** they read the getting started guide
**Then** the guide covers: installation via NuGet, Umbraco.AI provider and profile configuration prerequisites, running the example workflow for the first time, and what to expect (FR68)
**And** the guide is concise and task-oriented — not a reference manual

**Given** a developer wants to create their own workflow
**When** they read the workflow authoring guide
**Then** the guide covers: workflow.yaml schema and field reference, writing agent prompt markdown files, artifact handoff (reads_from/writes_to), completion checking, profile configuration (per-step and defaults), available tools, and a walkthrough of creating a simple 2-step workflow (FR69)
**And** the guide references the JSON Schema for IDE validation setup

## Epic 10: Ship Readiness & Stability

Known bugs and gaps are fixed before community launch — the concurrency and context bugs will be hit by real users immediately.

### Story 10.1: Instance Concurrency Locking

As a developer,
I want the engine to prevent concurrent step execution on the same instance using SemaphoreSlim per-instance,
So that simultaneous requests don't corrupt instance state.

_Full story spec to be created by SM before development begins._

### Story 10.2: Context Management for Long Conversations

As a developer,
I want the engine to manage conversation context size for multi-step workflows,
So that token waste is reduced and long workflows don't hit context limits.

_Full story spec to be created by SM before development begins._

### Story 10.3: Multi-Turn Interactive Fix

As a developer,
I want the ToolLoop to continue when the LLM asks a question instead of exiting,
So that multi-turn interactive conversations work correctly.

_Full story spec to be created by SM before development begins._

### Story 10.4: Open Source Licence Decision

As a product owner,
I want the open source licence decided (MIT vs Apache 2.0) and applied,
So that the package can be published with clear licensing.

_Note: This is a product/legal decision, not a code task. Zero code dependency — it's a LICENSE file and PackageProjectUrl in the .csproj._

_Full story spec to be created by SM before development begins._

### Story 10.5: Marketplace Listing & Community Launch

As a product owner,
I want the package listed on NuGet.org and Umbraco Marketplace with community launch posts,
So that developers can discover and install AgentRun.

_Full story spec to be created by SM before development begins._

## Epic 11: Adoption Accelerators

Additional workflow templates, tree navigation, and token usage logging drive adoption after launch. Market research found that the skill gap (developers don't know how to design AI workflows) is the #1 adoption barrier. Templates solve this.

### Story 11.1: Additional Workflow Templates

As a developer,
I want 2-3 additional workflow templates (SEO metadata generator, content translation pipeline, content model auditor),
So that I have more starting points for building AI workflows.

_Full story spec to be created by SM before development begins._

### Story 11.2: Tree Navigation UX Overhaul

As a developer or editor,
I want workflows and instances organised in the Umbraco section tree (following Umbraco's native pattern),
So that navigation feels native and familiar.

_Note: This is a significant UX refactor — V2 scope assessment from the brief is accurate._

_Full story spec to be created by SM before development begins._

### Story 11.3: Token Usage Logging

As a developer,
I want structured log events emitted per-step with token usage data,
So that I can monitor LLM costs via existing log infrastructure.

_Note: Not a dashboard — just structured log events. Pro prerequisite without being a Pro feature._

_Full story spec to be created by SM before development begins._

## Epic 12: Pro Foundation

Storage provider abstraction, database storage provider, and package split lay the architectural foundation for the commercial Pro package.

### Story 12.1: Storage Provider Abstraction

As a developer,
I want instance storage abstracted behind an `IInstanceStorageProvider` interface with the current disk implementation refactored behind it,
So that alternative storage backends can be plugged in without modifying the engine.

_Note: The `IInstanceManager` and `IConversationStore` interfaces are the natural seam points — the storage provider wraps the I/O beneath them._

_Full story spec to be created by SM before development begins._

### Story 12.2: Database Storage Provider

As a developer,
I want a `DatabaseInstanceStorageProvider` that stores instances using Umbraco's DB connection,
So that enterprise deployments can use database storage instead of disk.

_Note: This is the #1 enterprise blocker identified in market research._

_Full story spec to be created by SM before development begins._

### Story 12.3: Package Split

As a developer,
I want the codebase split into `AgentRun.Umbraco` (free) and `AgentRun.Umbraco.Pro` (paid),
So that the commercial model can ship as a separate NuGet package.

_Note from architect: The Pro project references the free project (never the other way around). The free project's public API surface must be reviewed before the split to ensure stability. Breaking changes after the split affect two packages._

_Full story spec to be created by SM before development begins._
