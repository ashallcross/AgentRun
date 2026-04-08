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

### Epic 9: Beta Release Preparation
_Restructured 2026-04-07._ Two polished example workflows (Content Quality Audit + Accessibility Quick-Scan), a beta-blocking ToolLoop stall fix, sample target data, JSON Schema, documentation, and a private beta distribution plan. Ships as `1.0.0-beta.1` pre-release NuGet to a curated invite list — NOT listed on Umbraco Marketplace.
**Stories:** 9.6 Workflow-Configurable Tool Limits (beta blocker, architectural), 9.0 ToolLoop Stall Recovery (beta blocker), 9.1a CQA Working Skeleton, 9.7 Tool Result Offloading for fetch_url (beta blocker — added 2026-04-08 via SCP, done 2026-04-08), 9.9 read_file Size Guard with Truncation Marker (beta blocker — added 2026-04-08 via read_file bloat finding, defence-in-depth sibling to 9.1b), 9.1c First-Run UX (URL input, no canned dataset — paused pending 9.7 + 9.1b + 9.9), 9.1b CQA Polish & Quality (reframed 2026-04-08 as second reliability gate), 9.2 JSON Schema, 9.3 Documentation & Pre-Release Packaging, 9.4 Accessibility Quick-Scan Workflow, 9.5 Private Beta Distribution Plan
**FRs covered:** FR65, FR66, FR67, FR68, FR69

### Epic 10: Ship Readiness & Public Launch
_Updated 2026-04-07 — Story 10.3 moved to 9.0._ _Updated 2026-04-08 — Story 10.6 (Retry-Replay Degeneration Recovery) added as a fast-follower; promoted from a Story 9.1b edge case bullet via [SCP 2026-04-08 — 9.1b Rescope](sprint-change-proposal-2026-04-08-9-1b-rescope.md). Includes explicit beta-blocker escalation criteria in the spec — may be pulled forward into Epic 9 if private-beta telemetry shows the failure mode firing more than rarely._ Post-beta stability work (instance concurrency locking, context management for long conversations, retry-replay degeneration recovery), open source licence decision, and public 1.0 launch via Umbraco Marketplace and community channels.
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

## Epic 9: Beta Release Preparation

_Updated 2026-04-07 — restructured for private beta release. Previously titled "Example Workflow, Documentation & Packaging". The goal is no longer "ship to public NuGet / Marketplace" — that moves to Epic 10.5. The goal is now "produce a meaningful private beta" with two polished example workflows, a beta-blocker engine fix, sample data so workflows run out-of-the-box, and distribution to a curated invite list._

**Beta definition:** NuGet pre-release version `1.0.0-beta.1`, distributed privately to a curated invite list (Cogworks colleagues, selected Umbraco community contacts, Discord). NOT listed on the Umbraco Marketplace. NOT announced publicly. Purpose is to gather feedback and find bugs before the public 1.0 launch.

### Story 9.6: Workflow-Configurable Tool Limits (BETA BLOCKER — architectural)

_Added 2026-04-07 — surfaced during live testing of the Content Quality Audit example. The fetch_url tool was hardcoded to truncate HTML responses at 100 KB, which silently broke real-world page audits (modern homepages routinely exceed 300 KB-2 MB). The investigation revealed the deeper architectural problem: the engine has been making policy decisions that belong to workflow authors. This story establishes the pattern that fixes it._

**Architectural principle being established (record in v2-future-considerations.md catalogue):**

The engine separates **mechanism** from **policy**. Mechanism (tool dispatch, sandboxing behaviour, prompt assembly, state persistence, SSE protocol, authentication, the *existence* of safety limits) stays in the engine. Policy (the *values* of safety limits, timeouts, iteration caps) belongs to the workflow YAML, with site-level defaults and hard ceilings configurable via appsettings.json. Safety invariants (SSRF blocking, path sandboxing, the *fact* that limits exist) stay hardcoded.

**Resolution chain (canonical, parallels existing profile resolution):**

```
effective_value = (
    step.tool_overrides[tool].setting
    ?? workflow.tool_defaults[tool].setting
    ?? site.tool_defaults[tool].setting       // appsettings.json AgentRun:ToolDefaults
    ?? engine_default                          // hardcoded fallback
)
// Then apply hard cap:
if site.tool_limits[tool].setting_max is set:
    effective_value = min(effective_value, site.tool_limits[tool].setting_max)
```

**Hard caps are hard.** Workflow YAML cannot exceed the site-level ceiling under any circumstances. Decision recorded 2026-04-07: tighten in, loosen later, never the reverse.

As a workflow author,
I want to configure tool tuning values (response size limits, timeouts, user-message wait windows) at the workflow or step level,
So that my workflows are not blocked by engine assumptions about what counts as "too big" or "too long" for unrelated use cases.

**Acceptance Criteria:**

**Given** a workflow.yaml file
**When** the workflow author declares `tool_defaults` at the workflow level
**Then** the WorkflowValidator accepts the new optional key without rejecting the file
**And** the values are persisted in the parsed `WorkflowDefinition` model
**And** the values flow through to the relevant engine components at execution time

**Given** a workflow.yaml step
**When** the workflow author declares `tool_overrides` at the step level
**Then** the WorkflowValidator accepts the new optional key
**And** at runtime the step-level value takes precedence over the workflow-level value for that step only
**And** other steps in the same workflow are unaffected

**Given** appsettings.json contains `AgentRun:ToolDefaults` and/or `AgentRun:ToolLimits` sections
**When** the engine resolves a tool tuning value
**Then** the resolution chain is applied: step → workflow → site default → engine default
**And** the resolved value is then capped by the site-level ceiling (if any)
**And** if a workflow attempts to declare a value above the ceiling, the workflow load fails with a clear validation error naming the offending field, the attempted value, and the ceiling

**Given** the resolution chain
**When** at least one of the values in the chain is missing
**Then** resolution proceeds to the next level without error
**And** the engine never crashes due to a missing config value (engine default is the last-resort fallback)

**Three values migrated in this story:**

1. **`fetch_url.max_response_bytes`** (replaces `JsonSizeLimitBytes` and `HtmlSizeLimitBytes`)
   - Engine default: 1 MB (1_048_576) — sensible default for general fetches
   - The content-type-aware split (HTML 100 KB / JSON 200 KB) is **collapsed into a single value**. Decision recorded 2026-04-07: content-type-aware limits were a layer of magic the workflow author should not have to reason about.
   - The `GetSizeLimit(string? mediaType)` helper in `FetchUrlTool.cs` is removed.

2. **`fetch_url.timeout_seconds`** (replaces `client.Timeout = TimeSpan.FromSeconds(15)` in `AgentRunComposer.cs:60`)
   - Engine default: 15 seconds (preserves current behaviour)
   - Per-call override mechanism: the resolved value must be applied at fetch time, not at HttpClient construction time, because the HttpClient is shared. Implementation pattern: use `CancellationTokenSource.CancelAfter(timeout)` per request rather than `HttpClient.Timeout`.

3. **`tool_loop.user_message_timeout_seconds`** (replaces `UserMessageTimeout = TimeSpan.FromMinutes(5)` in `ToolLoop.cs:12`)
   - Engine default: 300 seconds (preserves current behaviour)
   - This setting cooperates with Story 9.0: Story 9.0 fixes stall *detection* (no more waiting on a stalled LLM); 9.6 makes the legitimate user-input wait window *tunable* for workflows where 5 minutes isn't right.

**Values explicitly NOT migrated in this story (deferred to v2):**

- `MaxIterations = 100` in `ToolLoop.cs:11` — not blocking the beta. Migrate later using the same pattern.
- Any other hardcoded values found during the v2 catalogue audit. The full list is in `v2-future-considerations.md` under "Engine Flexibility Audit — Hardcoded Policy Values".

**Example workflow.yaml after the story ships:**

```yaml
name: "Content Quality Audit"
default_profile: anthropic-sonnet-4-6

tool_defaults:
  fetch_url:
    max_response_bytes: 2097152      # 2 MB — generous for marketing site audits
    timeout_seconds: 30
  tool_loop:
    user_message_timeout_seconds: 600  # 10 minutes

steps:
  - id: scanner
    name: "Content Scanner"
    agent: "agents/scanner.md"
    tools: [fetch_url, write_file]
    tool_overrides:
      fetch_url:
        max_response_bytes: 5242880    # 5 MB just for this step
    writes_to: [scan-results.md]
    completion_check:
      files_exist: [scan-results.md]
```

**Example appsettings.json:**

```json
{
  "AgentRun": {
    "ToolDefaults": {
      "FetchUrl": {
        "MaxResponseBytes": 2097152,
        "TimeoutSeconds": 30
      },
      "ToolLoop": {
        "UserMessageTimeoutSeconds": 600
      }
    },
    "ToolLimits": {
      "FetchUrl": {
        "MaxResponseBytesCeiling": 20971520,
        "TimeoutSecondsCeiling": 300
      },
      "ToolLoop": {
        "UserMessageTimeoutSecondsCeiling": 3600
      }
    }
  }
}
```

**Documentation requirements:**

- The workflow authoring guide (Story 9.3) gets a new section: "Configuring Tool Tuning Values" covering the resolution chain, the three migrated values, the available settings, and the security rationale for hard caps.
- The README "First Run" section mentions sensible defaults: "AgentRun ships with sensible defaults — most workflows won't need to tune these. If your workflow audits large pages or runs long-form interactions, see the workflow authoring guide for how to adjust."
- The JSON Schema (Story 9.2) is updated to include the new optional `tool_defaults` and `tool_overrides` keys with validation and autocomplete.

**Failure & Edge Cases:**

- Workflow declares `max_response_bytes: 0` or negative → validation error at workflow load
- Workflow declares `max_response_bytes` above the site-level ceiling → validation error at workflow load with clear "value X exceeds site-level ceiling Y" message
- Workflow declares an unknown tool name in `tool_defaults` (e.g. `tool_defaults.fake_tool`) → validation error at workflow load (deny by default)
- Workflow declares an unknown setting name (e.g. `tool_defaults.fetch_url.unknown_setting`) → validation error at workflow load
- appsettings.json has malformed `AgentRun:ToolDefaults` section → engine logs a clear error and falls back to engine defaults (do not crash startup)
- Resolved timeout value of 0 or negative → engine treats as "use engine default" and logs a warning
- The fetch_url request takes longer than the resolved timeout → cancellation triggers, surfaces as a normal timeout error to the agent (existing behaviour)
- The user_message_timeout fires legitimately (user took too long) → existing behaviour preserved (exits cleanly, step remains resumable)

**What NOT to Build:**

- Do NOT migrate any of the v2-deferred values (MaxIterations, sidecar paths, completion check strategies, etc.) — scope creep
- Do NOT add new tunable values that don't exist as hardcoded constants today
- Do NOT introduce a "disable limit entirely" option for any safety value (max_response_bytes cannot be set to "unlimited" — it must always be a finite number)
- Do NOT change SSRF protection, path sandboxing, or atomic write semantics — these are safety invariants
- Do NOT make this a generic "workflow config" subsystem — it's specifically the resolution chain for tool tuning values. Other future config needs use the same pattern but get their own scoping.

**Architectural notes for the dev agent:**

- The resolution chain logic should live in a single helper class (`ToolLimitResolver` or similar) so it can be reused for future migrations without copy-paste
- The helper takes the step, workflow, site config, and engine defaults; returns the resolved + capped value
- ToolLoop, FetchUrlTool, and any other consumer call the helper rather than reading config directly
- The helper is unit-testable in isolation against synthesised inputs
- Site-level config binding: extend `AgentRunOptions` (or add a sibling options class) for the new sections
- Backwards compatibility: workflows without `tool_defaults` / `tool_overrides` continue to work unchanged using engine defaults

_Full story spec to be created by SM before development begins._

### Story 9.0: ToolLoop Stall Recovery & Completion Logic (BETA BLOCKER)

_Added 2026-04-07 — pulled forward from Story 10.3 and expanded in scope after live testing of Content Quality Audit revealed the scanner stalling silently after a successful fetch_url call. This is the beta-blocking bug. The original 10.3 ("ToolLoop exits when LLM asks a question") was a related but narrower framing of the same root cause: the ToolLoop's exit conditions are too eager and collapse multiple distinct states into "wait for user input"._

_Cooperates with Story 9.6: Story 9.0 fixes stall **detection** (engine logic — distinguish stall from "waiting for user" from "complete"). Story 9.6 makes the legitimate user-input wait window **tunable** (configurable timeout). Both ship in the beta. The stall detection in this story must respect the configured timeout from 9.6 rather than the hardcoded 5-minute constant._

As a developer running an interactive workflow,
I want the engine to correctly distinguish between "agent is waiting for user input", "agent has completed its work", and "agent has stalled mid-task",
So that workflows fail fast and visibly when the agent stops responding, instead of hanging silently until a timeout.

**Problem statement (from live test 2026-04-07):**

Live test of the Content Quality Audit example workflow, instance `642b6c583e3540cda11a8d88938f37e1`, produced the following conversation trace in the Content Scanner step:

1. System: "Starting Content Scanner..."
2. Assistant: "Paste the URLs of the pages you'd like me to audit, one per line."
3. User: "www.wearecogworks.com"
4. Assistant (tool call): `fetch_url({"url":"https://www.wearecogworks.com"})`
5. Tool result: full HTML of the target page returned successfully (~150KB)
6. **— LLM produced no further output. No tool call. No text. Nothing. —**
7. (~5 minutes later) User message wait timed out, ToolLoop exited, completion check failed, step marked Error.

The engine treated "LLM produced empty response after tool result" as "step is awaiting user input" and sat idle for the full user-message timeout window. This is wrong on two counts: (a) the scanner's own instructions require it to call `write_file` after parsing tool results, so an empty turn is definitionally incomplete; (b) waiting silently for 5 minutes before surfacing an error is the worst possible UX for a first-run example.

**Acceptance Criteria:**

**Given** a workflow step is executing in interactive mode
**When** the LLM produces no tool calls AND no text content after a tool result
**Then** the engine detects this as a stall (NOT as "waiting for user input")
**And** the step is marked Error within 10 seconds (not after the user-message timeout)
**And** the chat panel surfaces a clear error message: "The agent stopped responding mid-task. Click retry to try again."
**And** the step can be retried from the chat panel using the existing retry flow (Story 7.2)
**And** the error is logged with the instance ID, step ID, and last tool call

**Given** a workflow step is executing in interactive mode
**When** the LLM produces a text message that reads as a question to the user (e.g. ending with "?", or no tool calls and an unambiguously conversational message)
**Then** the engine correctly enters the "waiting for user input" state
**And** the ToolLoop resumes when the user sends a reply, without exiting the step (fixes the original Story 10.3 symptom)
**And** the multi-turn conversation continues until the LLM signals completion (either via writing the expected output file, or via the completion check passing)

**Given** a workflow step is executing
**When** the LLM's output completes the expected work (completion check files written)
**Then** the step is marked Complete
**And** the ToolLoop exits cleanly
**And** in autonomous mode the next step starts

**Architectural decision (fail-fast, not retry-with-nudge):**

When a stall is detected, the engine marks the step errored immediately. It does NOT automatically inject a "continue your task" nudge prompt and retry. Recovery is via the user clicking retry (existing Story 7.2 flow).

Rationale: fail-fast is simpler to build, easier to debug, and prevents hidden retries from masking genuine prompt quality issues. The prompt-quality strengthening in Story 9.1b should substantially reduce stall frequency regardless. The retry-with-nudge alternative is documented in `v2-future-considerations.md` and is a trigger-based revisit if beta telemetry shows stalls happening more than occasionally.

**Design note:** Keep the stall detection logic cleanly separable from the recovery action, so a future retry-with-nudge strategy could swap in as a pluggable policy rather than requiring a ToolLoop rewrite.

**Failure & Edge Cases:**

- LLM returns an empty assistant message after a tool result → treat as stall, fail fast
- LLM returns a text message after a tool result that looks like a question to the user (ends with "?") → treat as waiting for user input, NOT a stall
- LLM returns a text message after a tool result that is clearly narrative ("Let me now process that...") without a tool call → this is the ambiguous case; for the beta, treat as stall to enforce the scanner.md "never narrate without calling a tool" rule. Document this decision.
- LLM returns a tool call for an undeclared tool → existing Story 5.4 validation path (reject, don't stall)
- User sends a message while the LLM is mid-response → existing multi-turn queue, no change
- Multiple consecutive stalls on retry → each retry is its own attempt, each can independently stall; the error message should hint at a possible prompt issue if the same step stalls 3+ times in a row (log only, no automatic behaviour change)

**What NOT to Build:**

- Do NOT implement retry-with-nudge (deferred to V2+, see v2-future-considerations.md)
- Do NOT change the user-message timeout value or wait-for-input behaviour for legitimate question-to-user cases
- Do NOT modify the completion-check file detection logic — this story is about ToolLoop exit detection, not completion detection
- Do NOT rework the Story 7.2 retry flow — reuse it as-is

_Full story spec to be created by SM before development begins, using the above as input._

### Story 9.1a: Content Quality Audit — Working Skeleton

_Was the original Story 9.1 (mostly complete). Renamed for clarity after the polish split. The workflow file structure, workflow.yaml, agent markdown files, and step wiring all exist and run end-to-end (modulo the Story 9.0 stall bug). This story captures what has been built._

As a developer,

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

### Story 9.1b: Content Quality Audit — Polish & Quality

_Added 2026-04-07 — this story is the iterative "make the output undeniable" work that turns a technically-working example into a showcase-quality one._

_**Scope reshaped 2026-04-08** via [Sprint Change Proposal 2026-04-08 — Story 9.1b Rescope](sprint-change-proposal-2026-04-08-9-1b-rescope.md). Was: pure prompt iteration loop ("iterate prompts until trustworthy"). Now: two-phase build-and-tune sequence — Phase 1 lands Option 2 (server-side structured extraction via AngleSharp), Phase 2 iterates prompts on top of the new contract. The reshape was triggered by the same architectural finding ([9-1c-architectural-finding-fetch-url-context-bloat.md](9-1c-architectural-finding-fetch-url-context-bloat.md)) that paused Story 9.1c. The trustworthiness gate criterion, the 5-pass soft cap, and the signoff artefact location are all unchanged. AngleSharp is the locked parser choice (MIT-licensed)._

_**Reframed 2026-04-08 (evening)** via [SCP 2026-04-08 — Story 9.1b Rescope Addendum (read_file finding)](sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md). Locked design unchanged; what changes is the weight of the lock. After Story 9.7's manual E2E surfaced a second context-bloat path on `read_file`, 9.1b is reframed from "quality improvement on top of an already-working scanner" to "second reliability gate without which the scanner cannot complete a single end-to-end run." Story 9.9 (read_file size guard — Option D from the [read_file bloat finding](9-7-architectural-finding-read-file-bloat.md)) is added as a parallel sibling — no order dependency between 9.1b and 9.9._

**Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done), 9.1a (CQA working skeleton — done), 9.7 (Tool Result Offloading for `fetch_url` — done 2026-04-08 via DoD amendment), 9.1c (First-Run UX — paused, must reach `done` before 9.1b starts implementation work), 9.9 (read_file size guard — to be created in Ceremony 3; parallel sibling, no order dependency — 9.1b touches FetchUrlTool.cs, 9.9 touches ReadFileTool.cs)
**Blocks:** 9.4 (Accessibility Quick-Scan — inherits the structured extraction pattern established here)

As a developer evaluating AgentRun,
I want the Content Quality Audit example workflow to produce an output I would happily paste into a client deck,
So that my first experience of the package proves its value, not just its plumbing.

The story is a **two-phase build-and-tune** sequence:

**Phase 1 (build):** Implement Option 2 — server-side structured extraction. Add an optional `extract: "raw" | "structured"` parameter to the existing `fetch_url` tool (default: `"raw"`, preserving backwards compatibility with Story 9.7's offloading shape). When `extract: "structured"` is set, fetch the page as today (SSRF / truncation / cancellation / Story 9.6 ceiling all preserved), parse it server-side using AngleSharp (MIT-licensed), and return a structured handle with title, meta description, headings, word count, image counts, and link counts. Update scanner.md / analyser.md / reporter.md to consume the structured return shape and strip the prompt complexity that the new contract makes obsolete (specifically: the "defending against context bloat" framing of the current scanner.md, which is no longer needed because the parsing happens server-side and the model never sees raw HTML).

**Phase 2 (tune):** Iterate prompts on top of the new contract until the trustworthiness gate passes against ≥3 representative real test inputs. The 5-pass soft cap with architect escalation applies to **Phase 2 (the polish phase) only** — the build phase (Phase 1) has no soft cap and ships when AngleSharp is integrated and the agents consume the structured return shape correctly. Bounded by the parser library, not by iteration cycles. _(Reaffirmed in the [9.1b reshape addendum 2026-04-08](sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md) — the polish-phase-only scope of the cap matters more after the reframing made Phase 1 strictly beta-blocking.)_

**Implementation Approach (Phase 1 — Build)**

_Locked decisions from Winston's 2026-04-08 architect response. Do not relitigate during the formal-spec writing pass._

- **Parser:** AngleSharp. MIT-licensed (verified 2026-04-08). Actively maintained, modern .NET targeting, DOM-faithful API. Not currently in the project's dependency tree — adding the NuGet reference is part of Phase 1.
- **Implementation shape:** Add an optional `extract` parameter to `fetch_url`'s schema — enum `"raw" | "structured"`, default `"raw"`. The architect explicitly **rejected** adding a sibling tool like `fetch_url_structured`. One tool, one concept, two return shapes. The scanner's `tools: [fetch_url, write_file]` declaration in workflow.yaml does not change; the scanner.md prompt explicitly tells the agent to use `extract: "structured"` for audit work.
- **Structured return shape:**
  ```json
  {
    "url": "https://example.com/page",
    "status": 200,
    "title": "Example - Home",
    "meta_description": "...",
    "headings": { "h1": [...], "h2": [...], "h3_h6_count": 47 },
    "word_count": 2341,
    "images": { "total": 84, "with_alt": 79, "missing_alt": 5 },
    "links": { "internal": 312, "external": 89 },
    "truncated": false,
    "truncated_during_parse": false
  }
  ```
  - `truncated` cooperates with Story 9.6's `max_response_bytes` ceiling at the byte-stream layer (same field as in Story 9.7's `extract: "raw"` handle).
  - `truncated_during_parse` is NEW and `extract: "structured"`-specific. It is `true` if AngleSharp had to parse a document that hit the byte ceiling before parsing, in which case the structured fields may be incomplete and the agent needs to know.
- **AngleSharp selectors map cleanly to every field:**
  - `title` → `document.Title`
  - `meta_description` → `document.QuerySelector("meta[name=description]")?.GetAttribute("content")`
  - `headings.h1` / `h2` → `document.QuerySelectorAll("h1").Select(e => e.TextContent.Trim())`
  - `headings.h3_h6_count` → `document.QuerySelectorAll("h3, h4, h5, h6").Length` (CSS selector lists work)
  - `word_count` → `document.Body.TextContent.Split(WhitespaceChars, RemoveEmpty).Length`
  - `images.total` / `with_alt` / `missing_alt` → `document.QuerySelectorAll("img")` then count by `string.IsNullOrEmpty(img.GetAttribute("alt"))`
  - `links.internal` / `external` → `document.QuerySelectorAll("a[href]")` then classify by parsing the source URL's host
- **Test sizing for the parser layer is the same three sizes as Story 9.7:** ~100 KB / ~500 KB / ~1.5 MB. **Reuse the same captured fixtures** that Story 9.7 will check in from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl`. Do NOT capture new fixtures.
- **Story 9.6 cooperation preserved:** the byte-stream layer still applies `max_response_bytes` truncation before AngleSharp sees the document. AngleSharp parses whatever bytes the byte-stream layer delivered, and the `truncated_during_parse` flag is set when truncation occurred.

**Quality gate (the trustworthiness gate):** Adam, acting as the reviewer, would be willing to run this workflow against a paying client's live site without supervision and trust the output. This is a subjective gate but it is the gate — the story is not done until the gate is passed.

_Decision recorded 2026-04-07: This trustworthiness gate replaces the earlier "structural consistency" gate. Structural consistency was a proxy for trustworthiness — replacing the proxy with the real thing is more honest. Since Story 9.1c removed the canned dataset, the workflow now runs against arbitrary user-provided URLs, so the gate cannot be measured by repeated runs against fixed input. It is measured by Adam picking representative real test inputs and judging the output directly._

**Acceptance Criteria:**

**Given** Stories 9.0 (stall fix), 9.6 (configurable limits), 9.7 (tool result offloading), and 9.1c (first-run UX) have all shipped
**And** Phase 1 (Option 2 build via AngleSharp) is complete
**When** Content Quality Audit is run against 3-5 representative real test inputs of Adam's choice (e.g. a small Cogworks page, a Wikipedia article, an Umbraco community blog post, a deliberately problematic page picked to exercise edge cases)
**Then** the scanner agent completes every run without stalling
**And** the agent never produces a broken, empty, or obviously degraded report
**And** the report identifies real issues specific to the actual fetched content — not generic advice that could apply to any page
**And** the recommendations are tied to evidence the agent observed in the page (not invented)
**And** the report is useful enough that Adam would be willing to send it to a paying client without rewriting

**Given** five runs of the workflow against the same real URL
**When** the outputs are compared
**Then** the extracted *facts* in `scan-results.md` are byte-identical (AngleSharp determinism property: identical input HTML produces identical extracted fields)
**And** the analyser's flagged issues are consistent across runs (the model's reasoning over deterministic facts is stable, even if the phrasing varies)
**And** the reporter's structural sections are present and predictably ordered (executive summary → top findings → page-by-page breakdown → prioritised recommendations)
**And** the report length stays within a reasonable band (~500-2000 words, not pathologically short or long)
**And** no run produces a structurally broken document (missing executive summary, no findings section, etc.)

**Given** a non-technical reviewer (simulate: Adam reading as if he were a marketing director receiving the report)
**When** they evaluate `audit-report.md`
**Then** they can understand it without technical context
**And** they can identify the top 3 issues without re-reading
**And** they perceive the recommendations as credible and worth acting on
**And** they would forward the report to a colleague without embarrassment

**Given** scanner.md is rewritten as part of the Phase 1 Option 2 implementation
**When** reviewed post-rewrite
**Then** it correctly consumes the structured return shape from `fetch_url(extract: "structured")` (title, meta_description, headings, word_count, images, links, truncated, truncated_during_parse)
**And** the five hard invariants from Story 9.1c's partial milestone are preserved verbatim (verbatim opening line, between-fetches rule, sequential-fetch invariant, post-fetch → write_file invariant, standalone-text invariant)
**And** the contract-defence verbosity (the "defending against context bloat" framing of the current scanner.md) is stripped because the new contract eliminates the failure mode upstream — the polish loop produces simpler prompts than today, not more complex ones
**And** the analyser.md and reporter.md prompts are updated to consume the structured fields directly rather than re-parsing the agent's text
**And** the failing instance reference in the original 9.1b draft (`642b6c583e3540cda11a8d88938f37e1`) is corrected — the relevant instance for the 2026-04-07 finding that motivated this reshape is `caed201cbc5d4a9eb6a68f1ff6aafb06`

**Process:**

**Phase 1 (build) — bounded by parser library, not by iteration cycles:**

1. Add AngleSharp NuGet reference to `AgentRun.Umbraco.csproj`
2. Add `extract: "raw" | "structured"` optional parameter to `fetch_url`'s schema and `IWorkflowTool` implementation
3. Implement the `extract: "structured"` code path: stream bytes (preserving Story 9.6 ceiling and truncation), feed AngleSharp the resulting document, build the structured handle from the selector mappings above
4. Reuse the captured test fixtures from Story 9.7 (~100 KB / ~500 KB / ~1.5 MB from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`); add parser-layer regression tests at the same three sizes
5. Rewrite scanner.md / analyser.md / reporter.md to consume the structured return shape and strip the contract-defence verbosity. Preserve the five hard invariants from Story 9.1c's partial milestone.
6. Run `dotnet test AgentRun.Umbraco.slnx` and `npm test`; expect green

**Phase 2 (tune) — bounded by the 5-pass soft cap with architect escalation:**

7. Adam picks 3-5 representative real test inputs (mix of his own pages, public pages with known issues, and at least one deliberately problematic page)
8. Run the workflow end-to-end against each test input
9. Review each agent's output critically against the trustworthiness gate
10. Identify what's weak, generic, hallucinated, or evidence-free
11. Iterate on the agent prompt markdown files (scanner.md, analyser.md, reporter.md) — note that with deterministic parsing in Phase 1, the iteration target is the model's *reasoning over facts*, not its parsing
12. Re-run. Repeat until the trustworthiness gate passes for every test input. **5-pass soft cap (polish phase only — does not apply to Phase 1's build work).** If 5 passes do not converge, escalate to Winston for prompt structure rethink.
13. Sign off in writing at `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md` (or equivalent — confirm location during formal spec writing). The signoff is the manual E2E artefact and its existence is the AC.
14. Lock the prompts. Record the final versions in git with a clear commit message noting the polish pass is complete.

**What NOT to Build:**

- Do NOT change the workflow.yaml step structure (keep 3 steps)
- Do NOT add new tools as siblings of `fetch_url`. The architect explicitly rejected adding a separate `fetch_url_structured` tool. Option 2 is implemented as a `mode` parameter (`extract: "raw" | "structured"`) on the existing `fetch_url` tool. The scanner/analyser/reporter still declare only `fetch_url` and `write_file` in their step's `tools` list.
- Do NOT add retry logic at the engine level — that's Story 9.0's scope, not this one
- Do NOT add tunable values to the workflow YAML — that's Story 9.6's scope, not this one
- Do NOT generalise the prompts across multiple content types — Content Quality Audit is a single fixed use case for V1
- Do NOT bundle sample content or pre-fetched HTML — Story 9.1c removed that approach
- Do NOT capture new test fixtures. Reuse the captured fixtures from Story 9.7 (extracted from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl`).
- Do NOT re-evaluate the parser choice. AngleSharp is locked. If implementation surfaces a real reason to revisit, escalate to Adam — do not silently substitute another parser.
- Do NOT regress the five hard invariants in scanner.md. The contract-defence verbosity is what gets stripped; the invariants stay verbatim.
- Do NOT include a `content` backup field anywhere in the structured return shape. Trust the parser. If parsing fails, the tool errors — it does not silently fall back to inline raw HTML.
- Do NOT modify Story 9.7's spec, the architectural finding report, the 9-1c spec, the 9-1c SCP, or `v2-future-considerations.md`.

**Failure & Edge Cases:**

- Trustworthiness gate fails after 5 polish iterations (Phase 2 soft cap) → escalate to Winston for prompt structure rethink. The cap is 5 *passes*, not 5 minor edits within a pass.
- Reports are inconsistent between models or providers → lock to Anthropic Sonnet for the beta (note: already the default in workflow.yaml). Document the recommendation in the workflow authoring guide.
- The test input set doesn't cover enough variation → expand the input list and re-run the polish loop. The story is not done until all chosen inputs pass.
- The agent hallucinates issues that don't exist on the page → with AngleSharp providing deterministic facts, hallucination should drop substantially. If it persists, the polish loop must add explicit "only flag issues you can directly cite from the structured fields returned by `fetch_url(extract: 'structured')`" instructions to the analyser prompt.
- A test input is so large it pushes against `fetch_url.max_response_bytes` → the byte ceiling still truncates at the byte-stream layer; AngleSharp parses the truncated document and sets `truncated_during_parse: true` in the handle. The analyser must check this flag and mark its findings as "based on partial content" when set. The polish loop must verify this behaviour.
- AngleSharp encounters genuinely malformed HTML that no browser would render → AngleSharp's behaviour is to recover the way browsers do (HTML5 parsing algorithm). In edge cases where it cannot recover, the structured fields may be sparse (`title: null`, empty heading lists, etc.). The analyser must handle null/empty fields gracefully and the prompt iteration loop must verify this.
- A page returns a content type that AngleSharp does not parse (e.g. PDF, JSON, image) → the existing 9-1c failure-bucketing logic in scanner.md applies — the page goes under "Skipped — non-HTML content" in `scan-results.md` and is not passed to AngleSharp at all. The `extract: "structured"` mode requires `text/html` content; for other content types, fall back to `extract: "raw"` (which Story 9.7 ships) or skip parsing entirely. The formal spec writing pass should clarify which.
- **Sonnet 4.6 parallel-tool-call stall (originally observed 2026-04-07 during 9.1c manual E2E, instances `c6072f5f569948e69c47e9becbe67bd4` and earlier)** → When given a multi-URL batch, Sonnet 4.6 may issue all `fetch_url` calls in parallel within a single assistant turn, then produce an empty turn after the batch returns, tripping `StallDetectedException`. _**[2026-04-08 annotation: This failure mode is now defended in two upstream layers — (i) the sequential-fetch invariant in scanner.md from Story 9.1c's partial milestone, and (ii) the offloaded handle pattern from Story 9.7 which eliminates the context-bloat trigger. The Phase 2 polish loop only needs to spot-check that both defences hold across the trustworthiness gate test set. The two original escalation paths (switch scanner to Opus, revisit Story 9.0's fail-fast decision and add retry-with-nudge) are no longer needed.]**_
- **Retry-replay degeneration (originally observed 2026-04-07, same instance)** → When a step fails mid-tool-loop and the user retries, the conversation history is replayed including all completed `fetch_url` results from the failed attempt. The model then sees a state where "all tools have run, only `write_file` is left" — and stalls again, because there is nothing for it to *do* except produce text or call `write_file`, and it sometimes produces text. This is **not a prompt-fixable bug**: the retry context window starts in a degenerate state. _**[2026-04-08 annotation: This finding is being promoted to its own story and is no longer Story 9.1b's responsibility. Working name: Story 9.8 — Retry-Replay Degeneration Recovery (final name TBD by Bob). Adam will run `bmad-create-story` after the 9.1b course-correction ceremony completes. The retry-replay degeneration is an engine-state-machine bug in the ToolLoop / ConversationStore retry path; it is NOT fixable by Story 9.7 (offloading), Story 9.1b's Option 2 reshape (structured extraction), or polish-loop iteration on prompts. It needs its own story.]**_ → tracked as **Story 10.6 — Retry-Replay Degeneration Recovery** (Epic 10 fast-follower, with explicit beta-blocker escalation criteria) — see [10-6-retry-replay-degeneration-recovery.md](../implementation-artifacts/10-6-retry-replay-degeneration-recovery.md). Bob's recommendation grounded in source reading: Option 3 (restart from scratch leveraging 9.7's `.fetch-cache/`); architect (Winston) to lock the design before Amelia implements.

_Full story spec to be created by SM via `bmad-create-story` after Story 9.7 and Story 9.1c are both `done`._

### Story 9.7: Tool Result Offloading for `fetch_url` (BETA BLOCKER)

_Added 2026-04-08 via [Sprint Change Proposal 2026-04-08 — Story 9-1c Pause](sprint-change-proposal-2026-04-08-9-1c-pause.md). Authorised after the architectural finding from Story 9.1c's manual E2E (2026-04-07 evening) proved the prompt-only ceiling has been reached: `FetchUrlTool` returns raw HTTP response bodies directly into LLM conversation context, which causes a context-bloat-induced empty turn from Sonnet 4.6 after multi-fetch sequences against real-world pages. The full diagnosis lives in [_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md](9-1c-architectural-finding-fetch-url-context-bloat.md). This story implements the architect's locked decision: **offloaded raw response (Option 1)**. Story 9.1b's pending course correction will land Option 2 (server-side AngleSharp extraction) — that is NOT this story's scope._

_**Dependency position:** 9.6 (done) → 9.0 (done) → **9.7 (this story)** → 9.1c (paused — resumes once 9.7 ships) → 9.1b (re-scope pending). Also blocks 9.4 (Accessibility Quick-Scan, which fetches URLs and inherits the same fix)._

_**DoD amended 2026-04-08** via [SCP 2026-04-08 — Story 9.7 DoD Amendment](sprint-change-proposal-2026-04-08-9-7-dod-amendment.md). Story moved to `done`. The DoD was scoped down to the fetch_url phase only (every `tool_result` is a JSON handle < 1 KB, zero raw HTML in conversation context). The full scanner workflow-completion gate (write_file → step Complete) moved to the combined release of **9.7 + 9.1b + 9.9**, gated on the [read_file bloat finding](9-7-architectural-finding-read-file-bloat.md)._

As the AgentRun engine,
I want `fetch_url` to write large response bodies to a workspace-scoped scratch path and return a small structured handle to the LLM instead of inlining the body,
So that multi-fetch workflows do not poison their own conversation context with hundreds of kilobytes of HTML and degrade into empty-turn stalls — while preserving SSRF protection, the configurable response-size ceiling from Story 9.6, the timeout behaviour, the cancellation plumbing, and the existing error string contract for HTTP failures.

**Pattern (locked by Winston):** "Tool result offloading" / "scratchpad handle". `fetch_url` writes the response body to `{instanceFolder}/.fetch-cache/{sha256(url)}.html` and returns a small JSON handle. The agent reads the cached file via the existing `read_file` tool only if it actually needs the content. This is the standard pattern used by Claude Code, Anthropic Computer Use, and the Claude Agent SDK.

**Architect's locked decisions (do not relitigate):**

1. **Scratch path location:** inside the instance folder at `{instanceFolder}/.fetch-cache/{sha256(url)}.html`. Reuses existing `PathSandbox` coverage; cleanup handled by instance deletion; dot-prefix conventionally hides from agent `list_files` output. **No new sandbox surface introduced.**
2. **Hash function:** SHA-256 (NOT SHA-1, NOT MD5). Standing rule: no weakened cryptographic primitives even in non-security contexts.
3. **Handle shape returned to the LLM:**
   ```json
   {
     "url": "https://example.com/page",
     "status": 200,
     "content_type": "text/html",
     "size_bytes": 1394712,
     "saved_to": ".fetch-cache/abc123def456.html",
     "truncated": false
   }
   ```
   Returned as a JSON-serialised string. **Must be < 1 KB**, asserted in unit tests.
4. **`truncated` flag is mandatory** even on successful fetches — cooperates with Story 9.6's `max_response_bytes` ceiling and surfaces whether the cached file is the complete response or has the truncation marker appended.
5. **NO `content` backup field in the handle.** If the file write fails the tool errors — it does NOT silently inline the body as a fallback.
6. **No-body responses** (3xx redirects, 204 No Content, HTTP 200 with `Content-Length: 0`) → handle is `saved_to: null`, `size_bytes: 0`. **No empty file written.** Decision on the empty-200 case confirmed by Adam 2026-04-08.
7. **HTTP 4xx / 5xx errors:** existing string return contract (`HTTP {status}: {reason}`) preserved exactly. No handle, no file write.
8. **Story 9.6 cooperation:** `fetch_url.max_response_bytes` is preserved as the upstream truncation guard. This story composes with the ceiling, it does not replace it.
9. **scanner.md update is in scope** — minimal contract bridge only (≤ 6 sentences). The five hard invariants stay exactly as committed in Story 9.1c's partial milestone.

**Acceptance Criteria (high level — full BDD ACs in the story spec):**

1. Successful HTTP 200 with non-empty body → response written to `.fetch-cache/{sha256}.html`, JSON handle returned, handle < 1 KB.
2. Truncation cooperation: when the body exceeds `max_response_bytes`, the truncated bytes (with the existing marker) are written to disk and the handle's `truncated` field is `true`.
3. No-body responses (204 / zero-length 200) → handle with `saved_to: null`, `size_bytes: 0`, no file written. HTTP errors → existing string contract preserved.
4. Race-safe directory creation; concurrent calls in the same instance succeed.
5. PathSandbox reachability: the agent can `read_file` the dotted-directory path returned by the handle.
6. **Mandatory regression tests at three captured payload sizes** (~100 KB / ~500 KB / ~1.5 MB), extracted from the failing instance `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl`. These three tests are the regression-protection gate.
7. File write failure → `ToolExecutionException` thrown, NO inline body fallback.
8. `dotnet test AgentRun.Umbraco.slnx` is green (note: always specify `.slnx`, never bare `dotnet test`).
9. **Manual E2E gate:** re-run the failing scenario from instance `caed201cbc5d4a9eb6a68f1ff6aafb06` against the new tool contract; scanner step completes without `StallDetectedException`; conversation log contains handles, not raw HTML.
10. **Production smoke test:** install the unmerged branch into a fresh TestSite, run Content Quality Audit against a real-world 5-URL batch including BBC News, confirm `artifacts/scan-results.md` is written.

**What NOT to Build:**

- Do NOT touch SSRF protection, the Story 9.6 `max_response_bytes` resolution chain, the timeout behaviour, the cancellation token plumbing, or the HTTP error string contract — all preserved exactly.
- Do NOT include a `content` backup field in the handle.
- Do NOT add a cleanup pass / TTL / LRU / background sweeper for `.fetch-cache/` files — per-instance cleanup is handled by instance deletion.
- Do NOT introduce a new sandbox surface, a new allowed root, a new sandbox class, or a parallel path validator. Reuse existing `PathSandbox`.
- Do NOT use SHA-1, MD5, or any weaker hash for filename derivation.
- Do NOT rewrite scanner.md — minimal contract bridge only. Five hard invariants and all existing prompt content stay exactly as committed.
- Do NOT change the `IWorkflowTool` interface or `ToolExecutionContext` shape.
- Do NOT add a new tool registration — there is no `fetch_url_handle` or `fetch_url_v2`. The existing tool's contract changes; that is the entire change.
- Do NOT modify Story 9.1c's spec, the architectural finding report, or `v2-future-considerations.md`.

**Failure & Edge Cases:** SHA-256 hash collision (deny by default, second write overwrites), file write failure (surface as `ToolExecutionException`, no fallback), `.fetch-cache/` directory creation race (idempotent `Directory.CreateDirectory`), stale cache files from interrupted runs (overwrite, no error), URL with non-portable characters (eliminated by SHA-256 hashing), cached file deleted between fetch and `read_file` (normal "file not found" from `read_file`), 3xx redirect chains (HttpClient follows by default; final status reported in handle), `.fetch-cache/` rejected by PathSandbox (should not happen — Task 2 verifies; if it does, fix PathSandbox before shipping). **Deny-by-default statement:** unrecognised or unspecified inputs (malformed URLs, paths escaping the instance folder, write targets outside `.fetch-cache/`) MUST be denied/rejected, never silently coerced.

**Pre-implementation architect review gate:** Winston explicitly requested to eyeball the PathSandbox interaction section before code is written. Amelia does not start implementation until Adam hands the spec to Winston and the architect-review checkbox at the bottom of the story spec is ticked.

_Full story spec lives at [_bmad-output/implementation-artifacts/9-7-tool-result-offloading-fetch-url.md](../implementation-artifacts/9-7-tool-result-offloading-fetch-url.md)._

### Story 9.9: `read_file` Size Guard with Truncation Marker (BETA BLOCKER)

_Added 2026-04-08 (Ceremony 3) via the read_file context bloat finding cycle. Authorising artefact: [9-7-architectural-finding-read-file-bloat.md](9-7-architectural-finding-read-file-bloat.md). This story is the **defence-in-depth half of Option E** from Winston's review of Amelia's finding. Story 9.7 disciplined the **write end** of tool-result offloading; Story 9.1b will discipline the **read end** with server-side AngleSharp structured extraction; Story 9.9 is the safety net underneath both — a configurable per-call byte cap on `read_file` with a verbatim truncation marker, so any future workflow that falls back to raw `read_file` against cached HTML cannot silently re-introduce the same context-bloat failure mode by accident._

_**Dependency position:** 9.6 (done — provides resolution chain) and 9.7 (done — establishes the offloading pattern this defends in depth) → **9.9 (this story)** + 9.1b (parallel sibling — primary fix; no order dependency, 9.9 modifies `ReadFileTool.cs`, 9.1b modifies `FetchUrlTool.cs`). Both must ship before Story 9.1c's manual E2E gate can be re-attempted (per the [9.1c pause addendum](sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md))._

As the AgentRun engine,
I want `read_file` to enforce a configurable per-call byte limit and append a truncation marker when a file exceeds it,
So that no agent can silently dump hundreds of kilobytes of cached HTML (or any other oversized file) into LLM conversation context — preserving the symmetry of the tool-result-offloading pattern that Story 9.7 introduced and Story 9.1b builds on, while leaving the existing happy path for small artifact files completely unchanged.

**Pattern (locked by Winston):** hard truncation with mandatory marker. NOT chunking, NOT sliced reads, NOT throw-on-overflow. Mirrors `fetch_url`'s existing truncation pattern at the contract level as well as the architectural level.

**Architect's locked decisions (do not relitigate):**

1. **Resolution chain reuses Story 9.6's `ToolLimitResolver`.** New tunable `read_file.max_response_bytes`. Same step → workflow → site default → engine default chain. Same site-level hard cap via `AgentRun:ToolLimits:ReadFile:MaxResponseBytesCeiling`. Mechanically identical to 9.6's `fetch_url.max_response_bytes` pattern.
2. **Default value: 256 KB (262144 bytes).** Deliberately conservative — admits typical artifact files (~10 KB to ~100 KB) without truncation, forces truncation on cached HTML pages (typically 100 KB+). The forcing function is intentional.
3. **Truncation marker text — locked verbatim:** `[Response truncated at {limit} bytes — full file is {totalBytes} bytes. Use a structured extraction tool (e.g. fetch_url with extract: "structured" once Story 9.1b ships) or override read_file.max_response_bytes in your workflow configuration to read the rest.]`
4. **Size check BEFORE the read** via `new FileInfo(canonicalPath).Length` (cheap, no contents read). If under limit → existing `File.ReadAllTextAsync` path unchanged (regression-safe). If over limit → bounded read via `FileStream.ReadAsync` with a `byte[limit]` buffer. Do **NOT** allocate the full file.
5. **No agent-side `max_bytes` parameter.** Workflow-author-configured only.
6. **No automatic chunking, streaming, or partial-read semantics.** Truncation is final per call.
7. **Existing behaviour for files under the limit is preserved unchanged.** Guard is purely additive.
8. **`PathSandbox.ValidatePath` continues unchanged.** Size guard layers on top.

**Files in scope:** `AgentRun.Umbraco/Tools/ReadFileTool.cs` (the size guard), `AgentRun.Umbraco/Engine/ToolLimitResolver.cs` + `IToolLimitResolver.cs` (new resolver method mirroring `ResolveFetchUrlMaxResponseBytes`), `AgentRun.Umbraco/Configuration/AgentRunOptions.cs` (new `ReadFile` sub-records on `ToolDefaultsOptions` / `ToolLimitsOptions`), `StepDefinition.ToolOverrides` and `WorkflowDefinition.ToolDefaults` (new `ReadFile` sub-records), `WorkflowValidator` (new validation rule mirroring 9.6's), `ReadFileToolTests.cs` + `ToolLimitResolverTests.cs` + `WorkflowValidatorTests.cs`. **Explicitly NOT touched:** `FetchUrlTool.cs`, `WriteFileTool.cs`, `ListFilesTool.cs`, `ToolLoop.cs`, any agent prompt file, any workflow YAML.

**Failure & Edge Cases:** file does not exist (preserve existing behaviour); `FileInfo.Length` permission error → `ToolExecutionException`; empty file → reads as today, no marker; bounded read fails partway → `ToolExecutionException`, no partial truncated string; workflow declares `read_file.max_response_bytes: 0` or negative → validation error at workflow load; workflow declares above the site ceiling → validation error mirroring 9.6; malformed `appsettings.json` → reuse 9.6's `SafeOptions()` pattern, log once, fall back to engine defaults; agent ignores the marker → same architectural limitation as `fetch_url`'s marker, out of scope; UTF-8 multi-byte sequence split at the truncation boundary → decode with replacement character or trim partial bytes (documented in Dev Notes); file shrinks between size snapshot and bounded read → no marker appended (avoid false positive). **Deny-by-default statement:** unrecognised or unspecified inputs (malformed paths, paths escaping the instance folder, files that do not exist, files with permission errors, files with sizes that fail to compute) MUST be rejected by the existing `PathSandbox.ValidatePath` check or fail loud — never silently coerced into a default, never silently truncated without the marker.

**Pre-implementation architect review gate:** Winston explicitly requested to eyeball the resolution chain integration and the 256 KB default before code is written. Amelia does not start implementation until Adam hands the spec to Winston and the architect-review checkbox at the bottom of the story spec is ticked.

**Forward dependency (NOT a blocker on 9.9):** Story 9.2 must add the new `read_file.max_response_bytes` workflow key to the JSON Schema for `workflow.yaml`. Picked up by 9.2 in one pass alongside any other keys added since 9.6.

_Full story spec lives at [_bmad-output/implementation-artifacts/9-9-read-file-size-guard.md](../implementation-artifacts/9-9-read-file-size-guard.md)._

### Story 9.1c: First-Run UX — User-Provided URL Input Handling

_**Status: PAUSED 2026-04-08** pending Stories 9.7 (done via DoD amendment), 9.1b (Option A — server-side AngleSharp structured extraction), and 9.9 (Option D — read_file size guard). The original pause was on 9.7 only; tonight's manual E2E of 9.7 surfaced a downstream context-bloat gap on read_file's code path, which expanded the dependency chain. See [sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md](sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md) and [9-7-architectural-finding-read-file-bloat.md](9-7-architectural-finding-read-file-bloat.md). The dev-agent-ready spec is the source of truth for AC annotations and remains unchanged._

_Rewritten 2026-04-07 after product owner challenged the canned-dataset premise. Original framing assumed the example needed bundled sample content to make the first run work out-of-the-box. Product owner pushed back: faking the data does not provide confidence; the user should be asked for a URL to scan. This is architecturally simpler (no engine changes, no hosting, no spike) and product-honest (the example demonstrates real auditing, not a magic trick). Recorded as a beta scope decision._

**Decision recorded:** No bundled sample dataset. No demo site. No inline mock content. The first run experience is "the agent asks for URLs, the user pastes URLs, the agent audits them, the user sees real results." This makes the trustworthiness gate of Story 9.1b the *actual* gate the package must pass — there is nowhere to hide behind a stacked deck.

As a developer running Content Quality Audit for the first time,
I want the scanner agent to gracefully and unambiguously ask me for URLs to audit,
So that my first interaction with the package is welcoming, clear, and successful on real content.

**Acceptance Criteria:**

**Given** a developer has installed the package, configured a profile, and started a Content Quality Audit instance
**When** the scanner step begins
**Then** the agent's opening message is welcoming and unambiguous: "Paste one or more URLs you'd like me to audit, one per line. You can paste URLs from your own site or any public site you'd like a second opinion on."
**And** the message makes it clear that the user is in control of what gets audited

**Given** the user pastes a single URL into the chat
**When** the URL is well-formed
**Then** the agent immediately calls `fetch_url` and proceeds with the audit
**And** the agent does not narrate ("Let me fetch that page now…") — Story 9.0's stall detection enforces this

**Given** the user pastes a list of URLs
**When** the list is separated by newlines, commas, or spaces
**Then** the agent extracts all URLs correctly and processes them in sequence
**And** the agent handles mixed delimiters within the same message

**Given** the user pastes a domain without an explicit protocol (e.g. `www.wearecogworks.com`)
**When** the agent processes it
**Then** the agent prepends `https://` and proceeds (this case was confirmed working in the live test on 2026-04-07 — preserve the behaviour, do not regress it)

**Given** the user pastes something that contains no recognisable URLs
**When** the agent processes it
**Then** the agent re-prompts politely with an example of valid input rather than guessing or stalling

**Given** the user pastes 5+ URLs at once
**When** the scanner processes them
**Then** the agent fetches each in sequence, providing brief progress updates between fetches (e.g. "Fetched 3 of 5...")
**And** all results are aggregated into the same `scan-results.md` file
**And** the agent does not stall between fetches (Story 9.0 prerequisite)

**Given** the user provides URLs that exceed the configured `fetch_url.max_response_bytes` ceiling
**When** a fetch succeeds but is truncated
**Then** the truncation is surfaced to the agent in a way it cannot miss (Story 9.6 may improve the truncation marker; if not, this story's prompt strengthening must instruct the scanner to verify response completeness before parsing)
**And** the scan-results.md output notes which pages were truncated and why

**What this story is NOT building:**

- No bundled HTML files, no `sample-content/` folder, no demo site, no inline mock content
- No engine changes (the user-input handling is purely agent prompt work plus the existing fetch_url tool)
- No URL allow-list, no URL pre-validation in the engine — the agent handles malformed input gracefully via re-prompting
- No new tools or new workflow YAML keys
- No new completion logic — the scanner is "done" when it has written `scan-results.md` covering all the requested URLs (existing completion check)

**Failure & Edge Cases:**

- User pastes a URL behind authentication (e.g. a private staging site with HTTP basic auth) → fetch_url returns 401, scanner records the failure in the "Failed URLs" section of scan-results.md and continues with other URLs
- User pastes a URL that returns a non-HTML content type (PDF, JSON, image) → scanner notes the content type and skips detailed analysis for that URL
- User pastes the same URL twice → scanner can deduplicate or process both; either is acceptable, must not crash
- User pastes a URL behind SSRF protection (RFC1918, loopback, etc.) → fetch_url rejects, scanner records the failure clearly with the security reason
- User cancels mid-fetch → existing cancellation behaviour, no new requirements

**Dependencies:**

- Story 9.0 (stall fix) MUST ship before this story is testable end-to-end — without it, the scanner stalls on the first fetch and the entire UX is broken
- Story 9.6 (configurable limits) MUST ship for real-world page sizes to work — without it, the 100 KB hardcoded limit truncates everything

_Full story spec to be created by SM after Stories 9.0 and 9.6 are complete (or in flight), since it depends on both._

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
**And** the schema includes the new optional `tool_defaults` (workflow-level) and `tool_overrides` (step-level) keys introduced by Story 9.6, with full validation, autocomplete, and descriptions for `fetch_url.max_response_bytes`, `fetch_url.timeout_seconds`, and `tool_loop.user_message_timeout_seconds` — see Story 9.6 for the canonical semantics and the YAML ↔ JSON ↔ C# naming map

_Dependency: this story cannot be completed until Story 9.6 has shipped, because the schema must reflect the keys 9.6 introduces. Spec the story after 9.6 is at least at `review` status._

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
**And** the "First Run" section explicitly frames the example workflows as user-driven, with language along the lines of: _"AgentRun ships with two example workflows: Content Quality Audit and Accessibility Quick-Scan. After installing the package and configuring an Umbraco.AI profile, open the Agent Workflows section, click a workflow, and click Start. The agent will ask you for one or more URLs to audit — paste URLs from your own site (or any public site you'd like a second opinion on) and watch the workflow run."_ (Story 9.1c decision: the example workflows do not ship with bundled data; the user provides URLs.)
**And** the README front matter uses the same "paste your own URLs" framing as a pull-quote — this is intentional product positioning. Most AI tool launches in 2026 are slick fake demos. AgentRun being unapologetically real is differentiation.

**Given** a developer wants to create their own workflow
**When** they read the workflow authoring guide
**Then** the guide covers: workflow.yaml schema and field reference, writing agent prompt markdown files, artifact handoff (reads_from/writes_to), completion checking, profile configuration (per-step and defaults), available tools, and a walkthrough of creating a simple 2-step workflow (FR69)
**And** the guide references the JSON Schema for IDE validation setup

_Note (2026-04-07): For the beta, the "Documentation" portion of this story ships; the "NuGet Packaging" portion ships as a pre-release NuGet only (`1.0.0-beta.1`), NOT listed on Umbraco Marketplace. Public Marketplace listing and full 1.0 packaging move to Story 10.5._

### Story 9.4: Accessibility Quick-Scan Workflow

_Added 2026-04-07 — second example workflow for the beta. Confirmed by product owner decision: two workflows in beta to make the package feel meaningful, not thin. Accessibility is deliberately chosen over SEO Metadata Generator because (a) it exercises a different engine code path than CQA (less file munging, more fetch-based analysis), proving the engine isn't a one-trick pony; (b) accessibility is a high-salience concern for agencies in 2026; (c) the prompt complexity is moderate and achievable within the beta window._

As a developer evaluating AgentRun,
I want a second pre-built example workflow that showcases accessibility analysis,
So that I can see the engine handle a different kind of task than content quality audit.

**Acceptance Criteria:**

**Given** the package is installed and a profile is configured
**When** the developer opens the Agent Workflows dashboard
**Then** an "Accessibility Quick-Scan" workflow appears alongside Content Quality Audit
**And** it has 2-3 steps (fetcher/analyser + reporter, or similar)
**And** it runs in interactive mode

**Given** the developer runs Accessibility Quick-Scan
**When** the fetcher step begins
**Then** the agent asks the user for one or more URLs to scan, using the same welcoming, unambiguous prompt pattern as Content Quality Audit (Story 9.1c)
**And** when URLs are provided, the agent fetches each via `fetch_url` and extracts accessibility-relevant signals: alt text presence on images, heading order, link text quality, ARIA landmarks, form field labels, colour contrast hints (inline style-based)
**And** writes a structured findings file
**And** the agent does not stall between fetches (Story 9.0 prerequisite)

**Given** the findings step has completed
**When** the reporter step executes
**Then** it produces a prioritised fix list grouped by severity (critical, major, minor)
**And** references specific WCAG 2.1 AA success criteria where appropriate
**And** the output is understandable by a non-developer content manager

**Given** Adam runs the workflow against 3-5 representative real test inputs of his choice
**When** he reviews the output
**Then** the same trustworthiness gate from Story 9.1b applies: he would be willing to send the report to a paying client without rewriting
**And** the agent flags real issues specific to the actual fetched content, not generic WCAG advice
**And** the agent does not hallucinate WCAG criteria references — every cited criterion is one the agent can directly evidence from the page HTML

**What NOT to Build:**

- Do NOT build a full WCAG 2.2 or WCAG 3.0 audit tool — this is a quick-scan that shows value, not a replacement for axe or Pa11y
- Do NOT add browser rendering or JavaScript execution (the fetch_url tool returns raw HTML; that's all we work with)
- Do NOT attempt colour contrast calculations that require rendered CSS — flag inline style colours only as hints
- Do NOT introduce new tools — reuse fetch_url, write_file, read_file as-is
- Do NOT bundle a sample dataset — the workflow asks for user-provided URLs, like Content Quality Audit (Story 9.1c decision)

**Failure & Edge Cases:**

- User provides URLs with no accessibility issues → the report still produces value by confirming what's done well, with a brief "no critical issues found" summary
- Agent conflates issues across pages → prompts must enforce per-page findings before aggregation
- LLM hallucinates WCAG criteria references → prompt must explicitly restrict claims to 2.1 AA and require evidence-based reasoning ("only cite a criterion if you can quote the offending HTML")
- User pastes a URL whose page is too large for the configured fetch limit → truncation handling per Story 9.6, surfaced clearly in the findings file

_Full story spec to be created by SM after Stories 9.0, 9.1c, and 9.6 are complete (or in flight)._

### Story 9.5: Private Beta Distribution Plan

_Added 2026-04-07 — defines the beta release milestone explicitly. Previously there was no distinction between "beta" and "public launch" in the epic plan, which created ambiguity about when Epic 9 was "done"._

As a product owner,
I want the beta release milestone defined, packaged, and distributed to a curated invite list,
So that real users exercise the package before public launch.

**Acceptance Criteria:**

**Given** Stories 9.6, 9.0, 9.1a, 9.1b, 9.1c, 9.2, 9.3, and 9.4 are all complete
**When** the beta is packaged
**Then** the NuGet package is built with version `1.0.0-beta.1`
**And** it is pushed to NuGet.org as a pre-release package (NOT listed on Umbraco Marketplace)
**And** pre-release versioning ensures it does not appear in default search results for stable package consumers

**Given** the beta invite list is finalised
**When** invitations are sent
**Then** each invitee receives: installation instructions, a link to the documentation (Story 9.3 deliverable), a request for structured feedback, and explicit language that this is a private beta not for re-sharing
**And** the invite list includes: Cogworks internal engineering contacts, 3-5 Umbraco community contacts, selected Umbraco Discord members (coordinated with Discord moderators as appropriate)

**Given** a beta user reports an issue or gives feedback
**When** the feedback is triaged
**Then** it is captured in a dedicated tracking location (file, issue tracker, or equivalent) with classification: bug, enhancement, or question
**And** the triage decides whether it blocks the public 1.0 launch or is deferred

**Given** the beta runs for a defined period (architect and product owner to agree duration during story refinement)
**When** the beta period ends
**Then** a beta retrospective is held to capture lessons learned
**And** a decision is made about readiness for public launch (Epic 10.5)

**What NOT to Build:**

- Do NOT list on Umbraco Marketplace during the beta
- Do NOT announce publicly (LinkedIn, DEV Community, Twitter/X, etc.) — that's the public launch
- Do NOT build telemetry or usage analytics — feedback is manual for the beta
- Do NOT include pricing, licensing discussions, or Pro-tier messaging — it's a free open-source beta with no commercial angle yet

_Full story spec to be created by product owner with SM support._

## Epic 10: Ship Readiness & Public Launch

_Updated 2026-04-07 — post-beta stability work and public 1.0 launch. Story 10.3 (Multi-Turn Interactive Fix) was pulled forward into Story 9.0 as a beta blocker after live testing on 2026-04-07 confirmed the bug prevents the scanner from completing the first example workflow. This epic is now the fast-follower stability + launch epic, executed after beta feedback has been collected._

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

### Story 10.3: _MOVED to Story 9.0 — ToolLoop Stall Recovery & Completion Logic_

_This story was originally scoped as "Multi-Turn Interactive Fix" — a narrow fix for the ToolLoop exiting when the LLM asks a question. Live testing on 2026-04-07 (instance 642b6c583e3540cda11a8d88938f37e1) revealed a broader root cause: the ToolLoop's exit conditions are too eager and collapse stall, question-to-user, and completion into a single "wait for input" state. The fix was expanded in scope and pulled forward into Story 9.0 as a beta blocker. See Story 9.0 for the full spec._

### Story 10.4: Open Source Licence Decision

As a product owner,
I want the open source licence decided (MIT vs Apache 2.0) and applied,
So that the package can be published with clear licensing.

_Note: This is a product/legal decision, not a code task. Zero code dependency — it's a LICENSE file and PackageProjectUrl in the .csproj._

_Full story spec to be created by SM before development begins._

### Story 10.6: Retry-Replay Degeneration Recovery

_Added 2026-04-08 via [Sprint Change Proposal 2026-04-08 — Story 9.1b Rescope](sprint-change-proposal-2026-04-08-9-1b-rescope.md). Promoted from a Story 9.1b Failure & Edge Cases bullet to its own story because the bug is engine-state-machine territory (the `StepExecutor` / `ConversationStore` retry path) and is not fixable by Story 9.7 (offloading), Story 9.1b's Option 2 (structured extraction), Story 9.0 (the upstream stall *detector*), or any prompt iteration. Broader context for the 2026-04-07 manual E2E session that surfaced both this finding and its sibling (the context-bloat finding) is in [SCP 2026-04-08 — 9.1c Pause](sprint-change-proposal-2026-04-08-9-1c-pause.md)._

_**Dependency position:** depends on 9.7 (must ship first; the recovery design depends on the `.fetch-cache/` handle pattern) and 7.2 (done; this story extends 7.2's retry path). Cooperates with 9.0 (upstream stall detector). Slotted in Epic 10 as a fast-follower; **explicit beta-blocker escalation criteria are recorded in the spec** — pull forward into Epic 9 if private-beta telemetry shows the failure mode firing more than rarely._

As a developer who has just hit a step failure mid-tool-loop and clicked retry,
I want the engine to reshape the replayed conversation so the model does not see a degenerate "all my tools have already run" state,
So that retries actually recover the workflow instead of stalling again on the same path that just failed.

**The bug being fixed:** When a step fails mid-tool-loop and the user retries via the existing Story 7.2 retry flow, [`StepExecutor.ExecuteStepAsync`](../../AgentRun.Umbraco/Engine/StepExecutor.cs) reloads the JSONL conversation history via `IConversationStore.GetHistoryAsync` and replays the **entire** conversation including all completed `fetch_url` tool_call/tool_result pairs from the failed attempt. The model resumes seeing a tail that looks like "I have already issued N fetches; the last one is done; what should I do next?" — and sometimes produces text instead of calling `write_file`. Story 9.0's `StallDetector` then correctly fires on the retry, and the user's recovery affordance fails silently in front of them. This is **not** fixed by Story 9.7 (handles reduce stall *frequency* but the replayed *shape* is unchanged), **not** fixed by Story 9.1b's Option 2 (same reason), and **not** fixed by Story 9.0 (which is the detector — this story is the recovery layer underneath it). The existing Story 9.0 fix in [`ConversationStore.TruncateLastAssistantEntryAsync`](../../AgentRun.Umbraco/Instances/ConversationStore.cs) at line 136 (skip-truncate when the conversation is at a clean tool_use→tool_result boundary) is **correct** and stays — it prevents a different bug (orphaned tool_result → 400 from the provider). This story adds a new recovery primitive *alongside* it, not as a replacement.

**Three viable design alternatives** are surfaced in the spec for architect lock (Winston) before implementation begins:

1. **Option 1 — Trim the replayed history** at the last clean restart point (last user message). Pros: smallest mental-model change. Cons: on the scanner step's normal conversation shape (one user message at the start, then a long tool-call chain), Option 1 collapses to Option 3 with extra book-keeping.
2. **Option 2 — Inject a system reminder** at retry time explaining the resumption context. Pros: 5-line change, easy revert. Cons: prompt-engineered recovery for a non-prompt-fixable bug; fragile against Sonnet 4.6 non-determinism (see `memory/project_sonnet_46_scanner_nondeterminism.md`); the same layering Story 9.0 explicitly rejected (retry-with-nudge).
3. **Option 3 — Restart from scratch**, wiping the conversation and leveraging Story 9.7's `.fetch-cache/{sha256(url)}.html` cache so the re-issued `fetch_url` calls return existing handles instantly (assuming 9.7's cache lookup is cache-on-hit — flagged for Winston confirmation). Pros: structurally prevents the bug; robust against model non-determinism; composes naturally with 9.7. Cons: discards conversation history (mitigation: archive as `.failed-{timestamp}.jsonl`); hard dependency on 9.7 being on `main`.

**Bob's recommendation:** Option 3, conditional on Winston confirming Story 9.7's cache lookup is cache-on-hit. This is the only option that *prevents* the bug rather than *detecting* or *papering over* it. The total surgery is small (one new `WipeHistoryAsync` primitive on `IConversationStore`, one retry-mode branch in `StepExecutor`). Lock is Winston's call.

**Bob's epic-placement view (fast-follower vs beta-blocker):** Bob agrees with Adam's instinct of fast-follower (Epic 10), reasoning grounded in the source: post-9.7, the dominant trigger for the upstream stall (context bloat) is eliminated, so the retry path will be hit substantially less often. The replayed-degenerate-state failure mode requires a specific shape (several successful tool_call/tool_result pairs followed by a step failure), which post-9.7 is genuinely uncommon. The user has a manual workaround (re-click retry; each attempt is independent and usually succeeds within 1–2 tries). The fix is non-trivial and three viable design alternatives exist — doing the design work under beta-release pressure is the wrong forcing function. **However**, the spec records explicit **Beta-Blocker Escalation Criteria** — if the rate is higher than 1 in 10, or if the workaround fails more than 10% of the time, or if it produces silent data corruption, or if any private beta tester reports the symptom on a happy-path workflow within the first week, this story is escalated via `bmad-correct-course` to be moved into Epic 9 and re-prioritised.

**Pre-implementation gate:** the spec includes a Pre-Implementation Architect Review checkbox at the bottom that **must** be ticked by Winston (with a one-line note recording the locked design alternative) before Amelia starts coding.

_Full story spec lives at [_bmad-output/implementation-artifacts/10-6-retry-replay-degeneration-recovery.md](../implementation-artifacts/10-6-retry-replay-degeneration-recovery.md)._

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
