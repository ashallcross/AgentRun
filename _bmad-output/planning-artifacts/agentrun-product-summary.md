# AgentRun.Umbraco — Product Summary

## What It Is

AgentRun.Umbraco is a NuGet package that adds an AI-powered workflow engine to the Umbraco CMS backoffice. It lets developers and agencies define multi-step agent workflows in YAML that run directly inside Umbraco — reading content, analysing it, producing structured reports, and (in future versions) writing results back into the CMS.

It is installed as a standard Umbraco package. No external infrastructure, no separate servers, no API gateway. The package runs in-process inside the Umbraco host, which means it has direct access to all Umbraco services — content trees, document types, media libraries, property values — without HTTP calls or authentication tokens.

## What It Does Today (v1.0.0-beta.1)

### Core Engine

- **YAML-defined workflows** — each workflow is a folder containing a `workflow.yaml` definition and markdown agent prompt files. No C# required to create a workflow.
- **Multi-step execution** — workflows chain multiple AI agent steps. Each step gets its own LLM conversation, its own tool set, and visibility of artifacts produced by prior steps.
- **Two execution modes** — Interactive (agent pauses for user input between actions, user drives the workflow) and Autonomous (steps run back-to-back without human intervention).
- **Real-time streaming** — SSE (Server-Sent Events) streams agent responses, tool calls, and progress to the backoffice UI in real time.
- **Backoffice UI** — a dedicated "Agent Workflows" section in the Umbraco backoffice with workflow listing, instance management, live chat panel, tool call display, and artifact viewing.

### Tool System

Workflows declare which tools each step can use. The engine enforces strict whitelisting — an agent can only call tools explicitly listed in its step definition.

**Generic tools (work anywhere):**
- `read_file` / `write_file` / `list_files` — file I/O within the workflow instance folder
- `fetch_url` — HTTP fetching with full SSRF protection (blocks internal networks, validates redirects, enforces size limits)

**Umbraco-native tools (read from this Umbraco instance):**
- `list_content` — returns the published content tree with document types, URLs, publish status, last edited dates. Supports filtering by content type or parent node.
- `get_content` — returns full property values for a specific content node. Rich Text is returned as plain text. Block List / Block Grid content is extracted and readable. Media references include alt text and URLs.
- `list_content_types` — returns all document type definitions including properties, editors, compositions, and allowed child types.

These Umbraco tools run in-process via Umbraco's own DI services (`IContentService`, `IContentTypeService`, `IPublishedContentCache`). No Management API, no OAuth tokens, no external calls. The agent reads the same content that editors see in the backoffice.

### Security

- **Path sandboxing** — all file I/O is canonicalised and contained within the instance folder. Traversal attacks are rejected at workflow load time and again at runtime.
- **SSRF protection** — `fetch_url` resolves DNS before connecting, blocks RFC1918/loopback/link-local addresses, validates each redirect hop independently.
- **Tool whitelisting** — only tools declared in the workflow step's `tools` array can execute. Defence-in-depth: the engine validates tool names even though the LLM only sees declared tools.
- **Workflow definition validation** — malformed, missing, or unsafe workflow definitions are rejected at load time and never appear in the UI.
- **Backoffice authentication** — all endpoints require Umbraco backoffice access. No public API surface.

### Shipped Workflows

The package ships with three example workflows:

| Workflow | What It Does |
|----------|-------------|
| **Umbraco Content Audit** | Reads the content tree in this Umbraco instance, analyses completeness and structure against the content model, produces a prioritised audit report. No external URLs — it reads your actual CMS content. |
| **Content Quality Audit** | Takes one or more external URLs, fetches and analyses page content, scores quality across multiple dimensions, produces an audit report with actionable recommendations. |
| **Accessibility Quick-Scan** | Takes one or more external URLs, identifies WCAG 2.1 AA accessibility issues, produces a prioritised fix list. |

### Configuration

- **LLM provider agnostic** — works with any provider supported by Umbraco.AI (Anthropic, OpenAI, etc.). Each workflow specifies a profile alias; the LLM configuration lives in Umbraco.AI's standard settings.
- **Configurable tool limits** — site administrators set default and ceiling values for tool parameters (fetch size limits, timeouts, read file sizes). Workflow authors can tune within those ceilings. No workflow can exceed the site administrator's limits.
- **Zero database footprint** — all state is persisted as YAML + JSONL files in `App_Data/AgentRun.Umbraco/`. Uninstalling the package leaves no database tables, no configuration entries, no orphaned data.

### Workflow Authoring

Anyone can create a custom workflow by:

1. Creating a folder in `App_Data/AgentRun.Umbraco/workflows/`
2. Writing a `workflow.yaml` that declares steps, tools, and completion checks
3. Writing markdown agent prompt files that tell each step's AI agent what to do
4. Optionally adding a JSON Schema directive for IDE autocomplete

No C# code, no compilation, no deployment pipeline. Drop the folder in, restart the site, and the workflow appears in the backoffice.

## What's Coming (Planned Roadmap)

### Near-term (Epic 10 — Stability & Public Launch)

- Context management for long-running workflows (prevents stalls on large content sets)
- Cancel wiring (currently cosmetic — will actually stop in-flight LLM calls)
- SSE disconnect resilience (browser close won't kill a running workflow)
- Instance resume after step completion
- Concurrency locking
- Open source licence decision
- Umbraco Marketplace listing

### Medium-term (Epic 11 — Adoption)

- Additional Umbraco-connected workflow templates (Content Model Health Check, SEO Metadata Audit, Content Freshness Report)
- Data files runtime (workflows can ship with style guides, checklists, configuration data that agents read at runtime)
- `list_media` tool (browse the Umbraco media library)
- Tree navigation UX (workflows in the Umbraco section tree)
- Token usage logging

### Longer-term (Epics 12-14 — Pro / Enterprise)

- **Storage provider abstraction** — swap disk persistence for database storage (enables multi-node, load-balanced, containerised deployments)
- **Write-back tools** — `create_content`, `update_content`, `upload_media` — agents that can create draft content in Umbraco, not just read and report
- **Sidecar memory** — agents that learn across workflow runs (remember client patterns, avoid repeating work, accumulate knowledge)
- **Per-user isolation** — each backoffice user sees only their own workflow runs
- **Phases within steps** — pause mid-step for human review, upload documents, resume with new context
- **Sub-agent spawning** — parallel per-page or per-section analysis
- **Variant system** — one workflow definition with multiple configurations (e.g. different audit types, different content specialisations)

## Architecture

- **.NET 10 / Umbraco 17.3.2** — ships as a Razor Class Library (RCL) NuGet package
- **Umbraco.AI 1.8.0** — uses `IAIChatService` for LLM access. Provider agnostic.
- **Bellissima frontend** — Lit web components using Umbraco's UUI component library. Native look and feel.
- **Engine has zero Umbraco dependencies** — the core execution engine (`Engine/` folder) references only pure .NET types. Umbraco integration lives in `Tools/`, `Composers/`, and `Endpoints/`. This enables isolated unit testing and potential future extraction.
- **521 backend tests, 162 frontend tests** — NUnit 4 + NSubstitute backend, @web/test-runner + @open-wc/testing frontend.

## What Makes It Different

**It runs inside Umbraco.** Unlike external tools (MCP servers, CLI utilities, browser extensions), AgentRun has direct in-process access to Umbraco's services. No HTTP overhead, no authentication complexity, no tool-definition token costs. An agent calling `list_content` gets the published content tree via `IPublishedContentCache` — the same data source the Umbraco backoffice uses.

**Workflows are YAML, not code.** A developer, a technical consultant, or even a sufficiently motivated editor can create a workflow without writing C#. The agent prompts are markdown. The tool declarations are YAML. The engine handles execution, streaming, persistence, and security.

**It's workflow-generic.** The engine doesn't know what an audit is, what content quality means, or how to analyse accessibility. All domain knowledge lives in the workflow YAML and agent prompts. The engine provides the execution infrastructure — tool dispatch, conversation management, artifact handoff, completion checking — and the workflow author provides the expertise.

**It's an Umbraco package, not a platform.** Install via NuGet, configure an AI profile, open the backoffice section. No separate infrastructure, no cloud dependency, no vendor lock-in beyond the LLM provider you choose.

**Cost-governed by design.** Each workflow step can use a different LLM profile — cheap models for bulk scanning, capable models for analysis. A 3-step content audit across 200 pages might cost $2-5 using a mix of models. Without per-step control, the same workflow on an expensive model could cost $50+. This is the only system in the Umbraco ecosystem with per-step model policy.

## Who It's For

### Agency Technical Leads
The person who decides what tools and packages go into client projects. They're looking for ways to deliver more value with less labour. A workflow engine that turns markdown into billable audit reports changes their service economics. Content quality audits that currently take 2-5 days of manual work per client become automated deliverables.

### Editors and Content Managers
Who consume pre-built workflows to solve operational problems — content audits, freshness checks, accessibility scans, editorial research. They interact through the dashboard, reviewing and advancing steps, without needing to understand the underlying agent architecture. The output needs to be trustworthy and actionable.

### Developers Building Custom Workflows
Anyone who can write YAML and markdown can create a workflow. The tool system is extensible via `IWorkflowTool` — developers can register custom tools in their Umbraco project's DI container and reference them in workflow definitions. No engine modification needed.

## Workflow Use Case Categories

The package supports four categories of workflow, ranging from what's possible today to what's on the roadmap:

### Category 1: Read-Only Analysis & Reporting (Available Now)

Workflows that read content (from the Umbraco instance or external URLs), analyse it, and produce structured reports as markdown artifacts. No content modification.

**Examples that work today:**
- Content quality auditing (completeness, structure, metadata gaps)
- Accessibility scanning (WCAG 2.1 AA compliance checking)
- Content model health checks (unused document types, empty required fields, orphaned content)
- SEO metadata auditing (missing titles, descriptions, duplicate metadata)
- Content freshness reporting (stale pages, pages not updated in 6/12/24 months)

### Category 2: External Research & Content Production (Near-term — needs data files)

Workflows that fetch external information, analyse it, and produce content artifacts. The scout/analyst/writer pattern.

**Examples planned:**
- Editorial content research pipeline — scout news sources on a topic, analyse for themes, draft an article using a style guide
- Competitor analysis — fetch competitor pages, compare against your content, identify gaps
- Regulatory monitoring — scan regulatory feeds, flag relevant changes, draft impact assessments

### Category 3: Content Modification & Publishing (Longer-term — needs write-back tools)

Workflows that not only analyse content but write results back into Umbraco — creating draft content nodes, updating metadata, uploading media.

**Examples planned:**
- Content pipeline with auto-publish to draft — research, write, create a draft blog post in Umbraco with hero image attached
- SEO metadata remediation — find missing meta descriptions, generate them, write them back as drafts for editor review
- Media library cleanup — identify images missing alt text, generate descriptions, update the media items
- Content migration — read from an external source, transform, create content nodes in the target Umbraco instance

### Category 4: Structured Consulting Delivery (Longer-term — needs phases, uploads, memory)

Multi-phase workflows with human review gates, client input stages, and cross-session learning. The 8-beat audit delivery pattern.

**Examples planned:**
- Full-service site audit — qualification → setup → investigation → synthesis session → prioritisation session → deliverable → handover
- Brand governance — ingest brand guidelines → scan content → check compliance → generate remediation plan
- Content strategy development — discovery interview → content inventory → gap analysis → editorial calendar

## Competitive Positioning

| Platform | Approach | AgentRun Difference |
|----------|----------|-------------------|
| **Sitecore Agentic Studio** | 20 pre-built agents, SaaS-only, enterprise pricing | Open-source, self-hosted, any LLM provider, per-step cost control |
| **Kontent.ai Agentic CMS** | Built-in governance/translation agents, headless-only | Native to Umbraco, open architecture, extensible via YAML |
| **Umbraco.AI Copilot** | Conversational AI assistant in the backoffice | Complementary — Copilot is real-time editorial assistance; AgentRun is structured multi-step workflow execution |
| **Umbraco MCP Server** | 315 Management API tools for IDE-based AI clients | Different use case — MCP is for developer IDE chat; AgentRun is for editor-facing backoffice workflows |
| **External AI tools** (ChatGPT, Claude, etc.) | Copy-paste content into a chat interface | AgentRun reads content directly from the CMS, maintains context across steps, produces structured artifacts, runs inside the backoffice |

## Production Heritage

This architecture is not theoretical. It is a .NET port of a production Node.js runner that has executed structured agent workflows across multiple client engagements — 8-step audit delivery, 4-step content pipelines, compliance assessments, and Umbraco upgrade analysis. The workflow patterns, tool system, and execution model are battle-tested.

## Commercial Context

The v1 package is open-source (licence pending — MIT or Apache 2.0). The primary goal is community adoption and ecosystem positioning. The commercial model follows the uSync playbook:

- **Free package** (NuGet.org) — full engine, all current tools, unlimited workflows, disk storage
- **Pro package** (planned, licensed feed) — database storage for multi-node deployments, write-back tools, sidecar memory, per-user isolation, advanced workflow features (phases, uploads, sub-agents)

The free version is genuinely useful, not artificially limited. The Pro version adds enterprise capabilities that single-instance installations don't need but agency and multi-editor deployments do.
