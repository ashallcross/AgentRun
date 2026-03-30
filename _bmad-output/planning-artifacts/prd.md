---
stepsCompleted:
  - "step-01-init"
  - "step-02-discovery"
  - "step-02b-vision"
  - "step-02c-executive-summary"
  - "step-03-success"
  - "step-04-journeys"
  - "step-05-domain"
  - "step-06-innovation"
  - "step-07-project-type"
  - "step-08-scoping"
  - "step-09-functional"
  - "step-10-nonfunctional"
  - "step-11-polish"
  - "step-12-complete"
vision:
  summary: "First community package bringing multi-agent workflow orchestration to the Umbraco backoffice — proof of work for personal positioning and community innovation"
  differentiator: "Agents chain together in a native backoffice chat interface, passing artifacts step to step. New workflows are markdown and YAML, not C#. Accessible authoring via BMAD Agent Builder."
  coreInsight: "The barrier isn't AI capability — it's orchestration plumbing. The Node.js runner already proved the architecture; porting to .NET + Umbraco.AI removes the last excuse."
  whyNow: "First-mover in community package space. Umbraco.AI just shipped, MAF just went GA. Be the person who shipped it or started the trend."
  primaryAudience: "Umbraco developers — discover, install, experiment, then potentially introduce to their workplace/agency"
  audienceNote: "Agency adoption is a downstream consequence, not the target. Developer-first framing throughout."
classification:
  projectType: "Developer Tool (NuGet package)"
  domain: "CMS / Content Operations"
  complexity: "medium"
  projectContext: "greenfield"
inputDocuments:
  - "product-brief-shallai-umbraco-agent-runner.md"
  - "product-brief-shallai-umbraco-agent-runner-distillate.md"
  - "umbraco-agent-workflow-ideas.md"
documentCounts:
  briefs: 2
  research: 1
  brainstorming: 0
  projectDocs: 0
workflowType: 'prd'
---

# Product Requirements Document - Shallai.UmbracoAgentRunner

**Author:** Adam
**Date:** 2026-03-28

## Executive Summary

Umbraco.AI gives editors single-action AI features — generate alt text, suggest a meta description, chat with the Copilot. But real content operations need structured pipelines: one agent crawls a site, another analyses the findings, a third scores every page, a fourth produces a prioritised action plan. Each step reads the previous step's output, uses the right model for the job, and produces a concrete artifact. Today, there is no way to orchestrate this inside Umbraco.

**Shallai.UmbracoAgentRunner** is a .NET-native multi-agent workflow engine, installable as a NuGet package into any Umbraco 17+ site. It loads workflow definitions from YAML files and agent prompts from markdown, executes them through Umbraco.AI's provider system, and surfaces them in a Bellissima backoffice dashboard with a chat interface. The engine ships once. Every new capability is a folder of markdown and YAML dropped into the project — no C#, no rebuild.

The architecture is a .NET port of a production Node.js runner that has executed multi-step agent workflows across multiple engagements, including 8-step audit delivery, content pipelines, and compliance assessments. The YAML schema, artifact handoff pattern, tool system, and prompt assembly are battle-tested.

This is the first community package to bring multi-agent workflow orchestration to Umbraco's backoffice. Competing CMS platforms are moving fast — Sitecore launched Agentic Studio with 20 pre-built agents, Kontent.ai branded itself as an "Agentic CMS" — while the entire open-source .NET CMS segment has nothing equivalent.

## What Makes This Special

**Workflows are data, not code.** A developer creates a new AI workflow by adding a folder with YAML and markdown. An editor runs it from the dashboard and watches agents work through each step via a chat interface, passing artifacts from one agent to the next. The barrier to multi-agent orchestration isn't the AI — it's the plumbing. This package removes that barrier entirely.

**Native backoffice experience.** Workflows run inside the Umbraco backoffice through a Bellissima dashboard. Editors see agents chain together in real-time via SignalR streaming, review intermediate outputs at each stage, and advance steps manually or let them run autonomously. This isn't an external tool — it's part of the CMS.

**Cost-governed by design.** Per-step Umbraco.AI Profile selection makes multi-agent workflows economically viable. A 3-step content audit might cost $2-5 using a mix of affordable and capable models. Without per-step control, the same workflow on a single expensive model could cost $50+.

**Open-source, provider-neutral.** All LLM calls go through Umbraco.AI's `IAIChatService`. Whatever provider the site has configured — OpenAI, Anthropic, Google, Bedrock — the runner uses it. No vendor lock-in. Workflows live in source control — version-controlled, diffable, deployable through CI/CD.

## Project Classification

- **Project Type:** Developer Tool (NuGet package)
- **Domain:** CMS / Content Operations
- **Complexity:** Medium — porting a proven architecture, but integrating with newly-shipped Umbraco.AI APIs and Bellissima extension system
- **Project Context:** Greenfield (.NET package is net-new; architectural patterns proven in existing Node.js runner)

## Success Criteria

### User Success

- A developer installs the package, runs the example workflow, and watches agents chain together from start to finish — producing a tangible deliverable (report, content artifact) in the backoffice. The reaction is "this is a game changer" — they see the power of multi-agent orchestration without having written any C#.
- A developer creates or modifies a workflow by editing YAML and markdown files, runs it, and sees their changes work. The realisation that new AI capabilities are a folder drop, not a code change, is the second "wow" moment.
- An editor runs a workflow from the dashboard, follows the agents' progress through the chat interface, reviews intermediate outputs, and receives a useful final artifact. They don't need to understand what's happening under the hood — it just works.

### Business Success

- This is a personal positioning play, not a revenue product. Success is measured by visibility and community impact, not commercial metrics.
- **Primary signal:** Contact from Umbraco HQ — whether that's interest in the architecture, a conversation about alignment with their roadmap, or even being told they're already building the same thing. Any of these validates the positioning.
- **Secondary signal:** Invitation to present at Codegarden, a community meetup, or an Umbraco online event. Being asked to talk about it is stronger validation than download numbers.
- **Tertiary signal:** Invitation to the Umbraco AI Community Team for the 2027 intake — the explicit goal stated in the product brief.

### Technical Success

- The example workflow runs successfully out-of-the-box on a fresh Umbraco 17+ install with Umbraco.AI configured
- Workflows execute reliably — >80% of started workflows run to completion without errors
- Step artifacts are correctly passed between agents, with completion checking working as expected
- SignalR streaming delivers real-time chat updates to the dashboard without lag or dropped messages
- The package installs cleanly via NuGet with no manual configuration beyond Umbraco.AI profile setup

### Measurable Outcomes

| Signal | Target (first 6 months) |
|--------|------------------------|
| NuGet installs | 20-30+ unique installs (quality over quantity) |
| HQ engagement | At least one substantive conversation with Umbraco HQ about the package or architecture |
| Community visibility | Featured in UMB.FYI newsletter or discussed in Umbraco community channels |
| Speaking opportunity | Invited to present at Codegarden, a meetup, or an Umbraco online session |
| Working example | Example workflow runs end-to-end out-of-the-box |
| Community contributions | At least 1 community-contributed workflow definition |

**Kill criterion:** If after 90 days there is zero community engagement AND zero HQ interest, reassess whether the approach needs fundamental changes or whether the timing is wrong.

## User Journeys

### Journey 1: The Developer — "From NuGet Install to Custom Workflow"

**Meet Marcus.** He's a mid-level Umbraco developer who's been following the AI buzz in the CMS space. He's seen the Sitecore Agentic Studio demos, he's played with Umbraco.AI's Copilot, and he's thinking "there must be more we can do with this." He browses the Umbraco Marketplace AI category, or sees a post on UMB.FYI, or catches a mention in the Umbraco Discord.

**Discovery.** Marcus finds Shallai.UmbracoAgentRunner. The description says "multi-agent workflow orchestration" and "workflows defined in YAML and markdown." He's curious but sceptical — he's seen plenty of packages that promise the world and break on install. He checks the README, sees there's an example workflow included, and decides to give it 30 minutes.

**Installation.** He runs `dotnet add package Shallai.UmbracoAgentRunner` on his local Umbraco 17 dev site that already has Umbraco.AI configured. The package installs cleanly. He sees new files appear in his project — an example workflow folder with a `workflow.yaml` and a few markdown agent files. He checks his backoffice and there's a new "Agent Workflows" dashboard section.

**First Run.** Marcus opens the dashboard, sees the example "Content Quality Audit" workflow listed. He clicks it, creates a new instance, and hits start. The chat panel opens. He watches as the first agent begins scanning — messages stream in via the chat interface showing what it's doing. It writes a `scan-results.md`. The step completes. He can read the intermediate output right there in the dashboard. He advances to the next step. The analyser agent picks up the scan results, scores pages, writes `quality-scores.md`. The final reporter agent produces a prioritised audit report. The whole thing took 3 minutes.

**The Wow Moment.** Marcus reads the final report. It's structured, actionable, and covers every page on his dev site. Three agents just chained together, passed work between each other, and produced a real deliverable — and he didn't write a single line of C#. He opens the `workflow.yaml` and sees how simple the orchestration definition is. He opens the agent markdown files and sees they're just prompts. He thinks: "I could build one of these for [specific thing his team needs]."

**Building His Own.** Marcus duplicates the example workflow folder, renames it, edits the YAML to define 2 new steps, writes two agent prompt files in markdown. He restarts the site and his new workflow appears in the dashboard. He runs it. It works. He's now a workflow author, and it took him 20 minutes. He configures different Umbraco.AI profiles for each step — a cheap model for the bulk scanning, a capable model for the analysis. He pushes the workflow folder to his team's repo.

**What could go wrong:** The package doesn't install cleanly, or requires manual configuration steps that aren't documented. The example workflow fails on first run due to missing Umbraco.AI profile configuration. The dashboard doesn't appear. Any of these kill the 30-minute evaluation window.

**Requirements revealed:** Clean NuGet installation, zero-config example workflow (or minimal config with clear error messages), discoverable dashboard, real-time chat streaming, readable intermediate artifacts, intuitive workflow.yaml schema, clear documentation for creating custom workflows, profile configuration guidance.

---

### Journey 2: The Editor — "From Dashboard to Deliverable"

**Meet Sarah.** She's a content manager at a mid-sized organisation running Umbraco. She doesn't know what YAML is and doesn't care. A developer on her team installed something new and told her "there's a content audit tool in the backoffice now — give it a try."

**Discovery.** Sarah logs into the Umbraco backoffice and sees a new "Agent Workflows" section in the navigation. She clicks it. There's a dashboard showing available workflows — one called "Content Quality Audit" with a short description of what it does.

**Running a Workflow.** Sarah clicks the workflow and hits "Start." A chat panel opens. She sees messages appearing — an agent is scanning the content tree. Status updates stream in: "Scanning Homepage... Scanning About Us... Scanning Blog section..." She doesn't fully understand what's happening under the hood, but she can see progress. The first step finishes and shows her a summary of what was scanned. In interactive mode, she clicks "Continue" to advance to the next step.

**Following Along.** The analysis agent starts working. Sarah watches it score pages on SEO, readability, and accessibility. She can see intermediate outputs at each stage — not raw data, but readable markdown summaries. She feels in control because she can review what each agent produced before the next one starts.

**The Deliverable.** The final step produces an audit report — a prioritised action plan telling her which pages need attention first and why. She can read it directly in the dashboard. It's actionable: "Homepage is missing a meta description. Blog posts from Q1 2025 have no alt text on 12 images. The About page scores 45/100 on readability." She takes this to her next content planning meeting.

**What could go wrong:** The chat interface is confusing or overly technical. Agent messages are full of jargon she doesn't understand. There's no clear indication of progress or how many steps remain. The final report is too technical to be useful to a non-developer.

**Requirements revealed:** Non-technical dashboard UI, clear progress indication (step X of Y), human-readable agent messages (not raw tool calls), readable artifact previews in the dashboard, intuitive step advancement controls, final artifacts that are useful to non-technical users.

---

### Journey 3: Umbraco HQ — "From Discovery to Engagement"

**Meet the HQ team.** Someone on the Umbraco product or community team spots Shallai.UmbracoAgentRunner — maybe via a Marketplace submission, a community forum post, a mention in the Discord, or through the AI Community Team network. They're interested because they know multi-agent orchestration is a gap in the ecosystem, and they're curious whether someone in the community is solving it well.

**First Impression.** They look at the Marketplace listing and the GitHub repo. They're evaluating: Is this well-built? Does it integrate properly with Umbraco.AI's APIs? Does it follow Bellissima extension conventions? Is the code quality something they'd be comfortable being associated with? They check the README, the documentation, and the example workflow.

**Technical Evaluation.** They clone the repo or install the package on a test site. They look at how it uses `IAIChatService` — is it doing it correctly? Does it respect the middleware pipeline? They check the Bellissima dashboard — does it follow the UI conventions and look native? They run the example workflow and evaluate the experience. They look at the workflow.yaml schema and assess whether the abstraction is sound.

**Architecture Assessment.** They're asking: Does this align with where we're taking Umbraco.AI.Agent? Could this pattern inform our own roadmap? Is this someone who understands our platform deeply enough to contribute at a higher level? They compare the approach to MAF patterns and their internal plans.

**Decision Point.** If the package is well-built and the approach is sound, they reach out. Maybe it's "we'd love to chat about your architecture." Maybe it's "we're building something similar — let's compare notes." Maybe it's an invitation to present at a community event or join the AI Community Team. Any of these is a win.

**What could go wrong:** The code doesn't follow .NET/Umbraco conventions, making it look amateur. The Umbraco.AI integration is shallow or incorrect. The dashboard looks bolted-on rather than native. Documentation is thin. The package doesn't install cleanly on a fresh site. Any of these signals "community experiment" rather than "serious contribution."

**Requirements revealed:** Clean, idiomatic .NET code following Umbraco conventions. Correct and deep Umbraco.AI integration (not just surface-level). Bellissima dashboard that looks and feels native. Comprehensive documentation. Professional Marketplace listing. Clear architecture documentation that demonstrates understanding of the platform.

---

### Journey Requirements Summary

| Capability Area | Developer Journey | Editor Journey | HQ Journey |
|----------------|-------------------|----------------|------------|
| **Installation & Setup** | Clean NuGet install, minimal config, clear error messages | N/A (developer handles) | Installs cleanly on fresh test site |
| **Dashboard UI** | Discoverable, shows available workflows | Non-technical, clear progress, intuitive controls | Follows Bellissima conventions, looks native |
| **Chat Interface** | Real-time streaming, shows tool calls and agent reasoning | Human-readable messages, clear progress indication | Demonstrates proper SignalR integration |
| **Workflow Execution** | Reliable step chaining, artifact handoff visible | Step-by-step advancement with review gates | Correct IAIChatService usage, middleware pipeline respected |
| **Workflow Authoring** | Intuitive YAML schema, markdown prompts, JSON Schema validation | N/A | Sound abstraction, well-designed schema |
| **Artifact Management** | Readable intermediate outputs, downloadable final artifacts | Actionable deliverables in plain language | Clean file management, proper sandboxing |
| **Documentation** | Getting started guide, workflow authoring guide, API reference | N/A (guided by dashboard UI) | Architecture docs, integration docs, code quality |
| **Profile Configuration** | Per-step profile selection, clear profile setup guidance | N/A | Correct Umbraco.AI profile integration |

## Innovation & Novel Patterns

### Detected Innovation Areas

**New paradigm: Declarative agent orchestration inside a CMS.** No CMS — open-source or commercial — allows users to define multi-agent AI workflows as data files (YAML + markdown) and run them natively in the backoffice. Sitecore Agentic Studio and Kontent.ai's agentic features are proprietary, code-driven, and locked to their platforms. This is a fundamentally different model: the engine is code, but every workflow is configuration.

**DSL creation: workflow.yaml as a domain-specific language for agent orchestration.** The workflow.yaml schema defines a structured vocabulary for multi-step agent pipelines — steps, artifact handoff, completion checks, tool grants, profile selection. Combined with a JSON Schema for IDE validation, this becomes a lightweight DSL that any developer can learn in minutes. The schema is already battle-tested across 4 production workflows in the Node.js runner.

**Architecture port as innovation strategy.** Porting a proven Node.js architecture to .NET-native Umbraco integration is itself a novel approach — the patterns are validated, but the target platform has never seen anything like it. This de-risks the innovation (the orchestration model works) while maximising the novelty (nobody has done this in Umbraco).

### Market Context & Competitive Landscape

- No Umbraco Marketplace package provides multi-step agent orchestration — existing AI packages (Perplex AI Assistant, Growcreate AI Toolkit, AI Essentials Toolkit) are single-action tools
- Sitecore Agentic Studio (20 pre-built agents) validates the market demand but is SaaS-only and enterprise-priced
- Kontent.ai's "Agentic CMS" branding proves the narrative resonates but is locked to their headless platform
- Microsoft Agent Framework (MAF) provides general-purpose agent infrastructure but has zero CMS awareness
- The open-source .NET CMS segment (Umbraco, Orchard Core) has no equivalent capability

### Validation Approach

- **Example workflow proves the concept:** A 2-3 step content quality audit that runs end-to-end out-of-the-box is the minimum viable validation. If a developer can install, run, and understand it in 30 minutes, the paradigm works.
- **Custom workflow proves the model:** If a developer can create a new workflow by duplicating a folder and editing YAML/markdown — without writing C# — the DSL is validated.
- **Community reaction validates the positioning:** Engagement from the Umbraco community (Discord, forum, UMB.FYI) and interest from HQ validates that this fills a real gap.

## Developer Tool Specific Requirements

### Project-Type Overview

Shallai.UmbracoAgentRunner is a .NET NuGet package targeting Umbraco 17+ on .NET 10. There is no backwards compatibility target — Umbraco.AI support starts at v17, so there's no value in supporting earlier versions. Distribution is via NuGet. The package provides a workflow engine with a Bellissima dashboard UI, not a library or SDK — developers interact with it primarily through YAML and markdown files, not through a C# API surface.

### Platform & Distribution

- **Target framework:** .NET 10
- **Minimum Umbraco version:** 17.0.0 (Umbraco.AI dependency)
- **Required dependencies:** Umbraco.AI 1.0.0+, YamlDotNet (YAML parsing)
- **Distribution:** NuGet package (`dotnet add package Shallai.UmbracoAgentRunner`)
- **Marketplace:** Umbraco Marketplace listing (future, not v1 blocker)

### Developer Interaction Model

Developers interact with the package at two levels:

1. **Installation & configuration** — NuGet install, Umbraco.AI profile setup, workflow folder path configuration. Standard .NET developer workflow.
2. **Workflow authoring** — creating and editing YAML workflow definitions and markdown agent prompts. This is the primary interaction surface. No C# required. Agent authoring (crafting effective prompts) is a separate skill — the runner doesn't prescribe how agents are written, it just executes them.

The BMAD Agent Builder is a separate tool for authoring agent prompts. It is not part of this package and should not be coupled to it. The runner consumes markdown agent files regardless of how they were authored.

### IDE Integration

- **JSON Schema for workflow.yaml** — provides validation, autocomplete, and inline documentation in VS Code, Rider, and any editor supporting JSON Schema. This is a first-class deliverable, not an afterthought.
- **No other IDE integration in v1** — no VS Code extension, no project templates, no CLI tooling. The JSON Schema is sufficient for v1.

### LLM Provider Prerequisites

**The package does not provide AI capabilities — it orchestrates them through Umbraco.AI.** This means the user must have:

1. **An active account with at least one LLM provider** — OpenAI, Anthropic, Google, Amazon Bedrock, or Microsoft AI Foundry
2. **A valid API key** configured as a Connection in Umbraco.AI
3. **At least one Umbraco.AI Profile** configured that the workflow steps can reference

**This must be explicit in documentation and in the dashboard UI.** If a user installs the package but has no Umbraco.AI provider configured, the dashboard should detect this and show a clear message: "You need to configure at least one AI provider in Umbraco.AI before workflows can run" — with a link to the Umbraco.AI configuration section. Not a cryptic error, not a silent failure.

**Reasonable assumption:** If someone has installed Umbraco.AI and this package, they've likely already configured a provider. But "likely" isn't "certainly" — the first-run experience must handle the case where they haven't, gracefully.

### Profile Resolution Chain

The engine follows a fallback chain for model selection, matching the existing Node.js runner pattern:

1. **Step-level profile** — if the workflow.yaml step declares a specific profile alias, use it
2. **Workflow-level default profile** — if the workflow.yaml declares a default profile, use it for any step that doesn't override
3. **Site-level default profile** — a single Umbraco.AI profile configured as the runner's default (set once during initial setup)

**Out-of-the-box experience:** A developer configures one default profile pointing at their preferred provider and model. Every agent uses it. Done. No per-agent model selection required. The example workflow ships without step-level profile overrides so it works immediately with whatever single profile the developer configures.

**Per-step overrides are opt-in for cost optimisation.** A workflow author who wants to use a cheap model for bulk scanning and a capable model for analysis can declare step-level profiles — but this is an advanced optimisation, not a requirement. The getting started guide should present this as "once you're comfortable, here's how to optimise costs" — not as a setup step.

### Documentation Strategy

Two documents for v1:

1. **Getting Started Guide** — installation, Umbraco.AI provider and profile configuration, running the example workflow, creating your first custom workflow. Optimised for the 30-minute evaluation window from the developer journey.
2. **Workflow & Agent Authoring Guide** — how to structure workflows (YAML schema reference), how to write agent prompts (markdown conventions), how artifact handoff works, how to configure per-step profiles, how completion checking works. This is "how to use the system," not "how the system is built internally."

Architecture documentation (how the engine works internally) is not a v1 priority. The focus is on enabling workflow authors, not on explaining the engine's internals.

### Example Workflow Requirements

The example workflow must be a **real-world, useful scenario** — not a hello world demo. It must solve an actual problem that a developer or editor would recognise and care about.

**Content Quality Audit (2-3 steps):**
- Step 1: Scanner agent catalogues content nodes — page titles, meta descriptions, headings, image alt text, word counts
- Step 2: Analyser agent scores each page on SEO completeness, readability, and accessibility
- Step 3: Reporter agent produces a prioritised action plan with page-by-page findings and recommended fixes

The output must be a deliverable someone would actually use — something an editor could take to a content planning meeting. If the example workflow produces output that makes someone say "yeah, what next?" then it has failed. If it produces output that makes someone say "I can see exactly how to apply this to my site," it has succeeded.

Each step uses a different Umbraco.AI profile to demonstrate the per-step cost governance model — but the example ships without step-level overrides so it works with a single default profile out of the box.

### Implementation Considerations

- **Package must install cleanly** with zero manual steps beyond `dotnet add package` and Umbraco.AI profile configuration. If it requires editing `Program.cs` or adding manual registrations, the first-run experience fails.
- **Auto-discovery of workflows** — the engine discovers workflow folders on startup. No registration code. Drop a folder, restart, it appears in the dashboard.
- **Umbraco Composer pattern** — use `IComposer` for automatic service registration on startup, consistent with how Umbraco packages are expected to behave.
- **No external dependencies beyond NuGet packages** — no Node.js, no npm, no separate process. Everything runs in-process within the Umbraco application.

## Project Scoping & Phased Development

### MVP Strategy & Philosophy

**MVP Approach:** Problem-solving MVP — the smallest implementation that proves multi-agent workflow orchestration works natively in Umbraco and produces a real-world deliverable. The goal is not feature completeness but a convincing demonstration that this paradigm works and is useful.

**Resource model:** Solo developer using Claude Code and BMAD agent workflows. All implementation stories must be structured for AI-assisted development — clear acceptance criteria, explicit file paths, small verifiable increments. Target approximately 1 week to a working PoC.

### MVP Feature Set (Phase 1)

**Core User Journeys Supported:**
- Developer journey: install → configure → run example → build custom workflow
- Editor journey: open dashboard → run workflow → follow progress → receive deliverable
- HQ journey: discover → evaluate code quality → assess architecture → engage

**Must-Have Capabilities:**

| Capability | Justification |
|-----------|---------------|
| Workflow engine (YAML parsing, prompt assembly, step execution via IAIChatService) | Core engine — without this, nothing works |
| Tool system: read_file, write_file, list_files (sandboxed), fetch_url | Agents need to read/write artifacts and fetch external data. fetch_url enables workflows that reach outside the instance |
| Artifact handoff with completion checking | Steps must pass work to each other reliably — this is the orchestration model |
| Profile resolution chain (site default → workflow default → step override) | Configure once, override when needed. Critical for OOTB experience |
| Instance management with disk-based state | Track workflow runs, persist state, survive app pool restarts |
| Bellissima dashboard (workflow list, instance management, step progress) | The user-facing surface — must look native and feel part of Umbraco |
| Chat panel with real-time SignalR streaming | The "wow" moment — watching agents work in real-time |
| Interactive mode (manual step advancement with review) | Human-in-the-loop is the safe, impressive default |
| Autonomous mode (auto-advance on completion check) | Required for workflows that should run unattended |
| Example workflow: Content Quality Audit (2-3 steps) | The product demo. Must solve a real problem, not be demo-ware |
| JSON Schema for workflow.yaml | IDE validation and autocomplete — first-class developer experience |
| Provider prerequisite detection | Clear error messaging if Umbraco.AI isn't configured |
| Getting started guide + workflow authoring guide | Enables the 30-minute evaluation window |
| NuGet package for Umbraco 17+ | Distribution channel |

### Post-MVP Features

**Phase 2 — Growth (Umbraco API integration is day-one priority):**
- **Umbraco API integration tools** (read_content_node, query_content, update_content, manage_media) — highest priority v2 feature. This unlocks agents that can manage the CMS, not just analyse it. Configurable network access policies allow whitelisting local Umbraco endpoints while maintaining SSRF protection
- Variant system for workflow parameterisation
- Sub-agent spawning
- Context eviction and token budget management for longer workflows
- Phase system within steps
- File uploads
- Content modification tools (moving beyond read-only)
- Sidecar memories

**Phase 3 — Expansion:**
- Scheduled/triggered execution via API
- Copilot integration — register workflows as Custom Tools
- Context bridge — surface artifacts as Custom Context Resource Types
- Community workflow repository
- Premium workflow packs (potential commercial extension)

### Risk Mitigation Strategy

**Technical Risks:**

| Risk | Severity | Mitigation |
|------|----------|------------|
| IAIChatService doesn't support tool use / function calling as expected | High | Verify early in E3 (Step Executor epic). If it doesn't, evaluate IChatClient directly or MAF agent types as alternatives |
| Umbraco.AI.Agent runtime may be the better integration layer than IAIChatService directly (see Open Architectural Questions below) | High | Investigate during architecture phase. Must not compromise the file-based authoring model |
| SignalR integration with Bellissima dashboard extensions is undocumented | Medium | Prototype the SignalR hub early in E6 (Dashboard UI epic). Umbraco uses SignalR internally — patterns exist to follow |
| Long-running IHostedService steps get killed by app pool recycling | Medium | Disk-based state persistence means steps can resume. Design for interruption from the start |
| Umbraco.AI APIs change in breaking ways | Medium | Pin to stable API surface (IAIChatService), maintain compatibility tests |

**Market Risks:**

| Risk | Severity | Mitigation |
|------|----------|------------|
| Umbraco HQ ships native orchestration | High | Ship fast, build community adoption. Being the reference implementation they align with is a win |
| Nobody cares about multi-agent workflows in a CMS | Medium | The example workflow must produce a deliverable that's obviously useful. If the concept doesn't resonate, the market isn't ready |
| Another community member ships something similar first | Low | Focus on quality and depth of integration, not speed alone. Being second with a better implementation is fine |

**Resource Risks:**

| Risk | Severity | Mitigation |
|------|----------|------------|
| Solo developer, AI-assisted — if Claude Code can't generate reliable Umbraco/Bellissima code, velocity drops | Medium | Favour established .NET patterns with extensive documentation (training data coverage matters). Keep stories small and verifiable |
| Scope creep beyond v1 boundaries | Medium | This scoping section is the contract. If it's not in the MVP table, it doesn't go in v1 |

## Open Architectural Questions

The following questions must be resolved during the architecture phase before implementation begins:

### Should the step executor use Umbraco.AI.Agent's runtime instead of calling IAIChatService directly?

**Context:** Umbraco.AI.Agent (already shipped, v1.0.0) provides agent management, AG-UI streaming protocol, profile integration, context injection, and a backoffice UI. Building the runner's step executor directly on IAIChatService means rebuilding streaming, agent-to-UI communication, and potentially the tool execution loop from scratch. Building on Umbraco.AI.Agent's runtime could simplify the build significantly and ensure deeper integration with the platform.

**The constraint:** The file-based authoring model (YAML + markdown folders) is non-negotiable. Workflow authors must be able to drop a folder into the project and have it work. If using Umbraco.AI.Agent's runtime requires storing agent definitions via its Management API (CRUD operations in the database), this breaks the core value proposition. The investigation must determine whether the runner can use the Agent runtime for execution and streaming while keeping agent definitions as files on disk — or whether the two models are fundamentally incompatible.

**What to evaluate:**
- Can Umbraco.AI.Agent execute an agent from a markdown prompt without storing it via the Management API?
- Does the Agent runtime support the tool execution loop (function calling, tool results, continuation)?
- Can the AG-UI streaming protocol replace the planned SignalR implementation?
- Does using the Agent runtime give us free integration with the Copilot ecosystem (a v2/v3 goal)?
- Would depending on Umbraco.AI.Agent constrain the runner's ability to manage its own conversation context, artifact handoff, and step lifecycle?

**Decision criteria:** Use Umbraco.AI.Agent's runtime if it simplifies the build without compromising file-based authoring or the tool execution model. Build directly on IAIChatService if the Agent runtime forces an incompatible authoring model or doesn't support the tool loop.

## Functional Requirements

### Workflow Discovery & Registry

- **FR1:** The engine can discover workflow definitions from folders on disk at startup without code registration
- **FR2:** The engine can parse workflow.yaml files and validate them against the JSON Schema
- **FR3:** The engine can load agent prompt files (markdown) referenced by workflow steps
- **FR4:** A developer can add a new workflow by dropping a folder containing workflow.yaml and agent markdown files into the workflows directory
- **FR5:** The engine can report validation errors for malformed workflow.yaml files with clear, actionable messages

### Instance Management

- **FR6:** A developer or editor can create a new workflow instance from an available workflow definition
- **FR7:** The engine can persist instance state to disk so it survives app pool restarts
- **FR8:** A developer or editor can view the list of existing workflow instances and their current status
- **FR9:** The engine can track which step an instance is on and what artifacts have been produced
- **FR10:** A developer or editor can resume an interrupted workflow instance from where it left off

### Step Execution

- **FR11:** The engine can assemble an agent prompt from the step's markdown file, sidecar instructions (if present), and runtime context (step info, tool declarations, prior artifact references)
- **FR12:** The engine can load sidecar instruction files (`sidecars/{step-id}/instructions.md`) as supplementary read-only context injected into the agent prompt alongside the main agent markdown
- **FR13:** The engine can execute a step by sending the assembled prompt to Umbraco.AI's IAIChatService using the resolved profile
- **FR14:** The engine can resolve which Umbraco.AI profile to use via the fallback chain: step-level → workflow-level → site-level default
- **FR15:** The engine can execute steps as background tasks via IHostedService, not within web request pipelines
- **FR16:** The engine can detect step completion by verifying required output files exist (completion checking)

### Tool System

- **FR17:** An agent can read files within the instance folder via the read_file tool
- **FR18:** An agent can write files within the instance folder via the write_file tool
- **FR19:** An agent can list files within the instance folder via the list_files tool
- **FR20:** An agent can fetch content from external URLs via the fetch_url tool
- **FR21:** The engine can restrict tool access to only tools declared in the workflow step definition
- **FR22:** The engine can sandbox file operations to the instance folder, preventing path traversal
- **FR23:** The engine can register tools through a pluggable tool interface — new tools can be added by implementing a standard interface and registering them, without modifying the engine core
- **FR24:** A workflow author can declare any registered tool in a step's tool list — the engine resolves tool names to registered implementations at runtime

### Artifact Handoff

- **FR25:** A workflow step can declare which artifacts it reads from previous steps (reads_from)
- **FR26:** A workflow step can declare which artifacts it produces (writes_to)
- **FR27:** The engine can make prior step artifacts available to the current step's agent context
- **FR28:** Any step can read artifacts from any prior step, not just the immediately preceding one

### Workflow Modes

- **FR29:** A developer or editor can advance a workflow step-by-step in interactive mode, reviewing intermediate artifacts before proceeding
- **FR30:** The engine can auto-advance steps in autonomous mode when completion checks pass
- **FR31:** A workflow author can set the execution mode (interactive or autonomous) per workflow in workflow.yaml

### Dashboard UI

- **FR32:** A developer or editor can view all available workflows in the Bellissima dashboard
- **FR33:** A developer or editor can create a new workflow instance from the dashboard
- **FR34:** A developer or editor can view the progress of a running workflow (current step, total steps, status)
- **FR35:** A developer or editor can view intermediate artifacts produced by completed steps
- **FR36:** A developer or editor can advance to the next step in interactive mode from the dashboard
- **FR37:** The dashboard can display workflow metadata (name, description, number of steps, mode)

### Chat Interface

- **FR38:** A developer or editor can view agent messages in real-time via a chat panel during step execution
- **FR39:** The chat panel can stream messages via SignalR as the agent works
- **FR40:** A developer or editor can see tool call activity in the chat panel (what tool was called, what it produced)
- **FR41:** A developer or editor can send messages to an agent during step execution — agents are conversational and work interactively with humans, not as batch processors
- **FR42:** The engine can maintain multi-turn conversation context within a step, enabling back-and-forth dialogue between the user and the agent across multiple exchanges

### Conversation Persistence

- **FR43:** The engine can persist agent conversation history for each step execution
- **FR44:** A developer or editor can review the full conversation history of a completed step

### Error Handling & Recovery

- **FR45:** The engine can detect and handle LLM provider errors (rate limits, timeouts, API failures) with clear error reporting to the user
- **FR46:** The engine can retry a failed LLM call when the user requests a retry from the chat interface
- **FR47:** The engine can roll back the failed message from conversation context on error, so the agent doesn't see its own broken response on retry
- **FR48:** The engine can preserve error context so the agent can learn from failures (error results are not discarded from context)
- **FR49:** The engine can track granular step status (pending, active, complete, error) and display the current status in the dashboard

### Instance Lifecycle

- **FR50:** A developer or editor can cancel a running workflow instance
- **FR51:** A developer or editor can delete completed or cancelled workflow instances
- **FR52:** The engine can prevent concurrent execution on the same instance

### Provider Prerequisites & Configuration

- **FR53:** The dashboard can detect whether Umbraco.AI has at least one provider configured and display a clear guidance message if not
- **FR54:** A developer can configure the site-level default profile for the runner
- **FR55:** The engine can report a clear error when a step references a profile that doesn't exist

### Security & Sandboxing

- **FR56:** The engine can validate that workflow.yaml files only reference declared tools from the allowed tool set
- **FR57:** The engine can sanitise agent-produced markdown content before rendering in the dashboard to prevent XSS
- **FR58:** The engine can reject file tool operations that attempt to access paths outside the instance folder
- **FR59:** The engine can restrict agents to only the tools declared in their step definition, ignoring undeclared tool call attempts
- **FR60:** The fetch_url tool can block requests to private/internal IP ranges (RFC1918, loopback, link-local) to prevent SSRF attacks
- **FR61:** The fetch_url tool can enforce response size limits to prevent memory exhaustion from large responses
- **FR62:** The fetch_url tool can enforce request timeouts to prevent hanging connections

### Artifact Browsing

- **FR63:** A developer or editor can browse all files produced by a workflow instance in a file browser view
- **FR64:** A developer or editor can view artifact content with markdown rendering in the dashboard

### Example Workflow

- **FR65:** The package can ship with a pre-built Content Quality Audit workflow (2-3 steps) that works out-of-the-box with a single configured profile
- **FR66:** The example workflow can produce a useful, actionable content audit report as its final artifact

### Documentation & Schema

- **FR67:** The package can ship with a JSON Schema for workflow.yaml that provides IDE validation and autocomplete
- **FR68:** The package can ship with a getting started guide covering installation, profile configuration, and first run
- **FR69:** The package can ship with a workflow authoring guide covering YAML schema, agent prompts, artifact handoff, and profile configuration

## Non-Functional Requirements

### Performance

- **NFR1:** Chat panel messages must appear within 500ms of the agent generating them — perceptible lag kills the real-time experience
- **NFR2:** Dashboard workflow list and instance list must load within 2 seconds on a standard Umbraco 17 development site
- **NFR3:** Step transitions (completion detection → next step start) must occur within 5 seconds in autonomous mode
- **NFR4:** The engine must not block the Umbraco web request pipeline — all step execution runs in background tasks via IHostedService
- **NFR5:** SignalR connection must recover automatically after temporary disconnection without losing message history (reconnect and replay missed messages)

### Security

- **NFR6:** File tool operations (read_file, write_file, list_files) must enforce path sandboxing using canonicalised path comparison — no path traversal via `../`, symlinks, or URL encoding
- **NFR7:** fetch_url must block all requests to RFC1918 (10.x, 172.16-31.x, 192.168.x), loopback (127.x), link-local (169.254.x), and IPv6 equivalents at the socket level before the connection is established
- **NFR8:** fetch_url must enforce a 15-second request timeout and response size limits (200KB for JSON, 100KB for HTML/XML) to prevent resource exhaustion
- **NFR9:** Agent-produced content rendered in the dashboard must be sanitised to prevent XSS — no raw HTML execution, script injection, or event handler attributes in rendered markdown
- **NFR10:** The engine must validate workflow.yaml against the JSON Schema before loading — malformed or unexpected properties must be rejected with clear error messages, not silently ignored
- **NFR11:** Tool call requests from agents must be validated against the step's declared tool list — undeclared tool calls must be rejected and logged, never executed
- **NFR12:** Prompt injection mitigations must be applied: agent system prompts must clearly delineate system instructions from user content, and tool results must be treated as untrusted input
- **NFR13:** Workflow execution must run under the Umbraco application's existing authentication and authorisation — only authenticated backoffice users can access the dashboard and trigger workflows

### Accessibility

- **NFR14:** The Bellissima dashboard extension must follow Umbraco's existing backoffice accessibility patterns — keyboard navigable, screen reader compatible for core navigation and workflow controls
- **NFR15:** Chat panel content must be accessible — agent messages must be announced to screen readers, progress indicators must have appropriate ARIA labels

### Integration

- **NFR16:** All LLM calls must go through Umbraco.AI's IAIChatService — no direct HTTP calls to provider APIs. This ensures the middleware pipeline (logging, caching, rate limiting, governance) applies automatically
- **NFR17:** The package must not interfere with other Umbraco.AI consumers — it must not modify global AI configuration, shared profiles, or middleware pipeline behaviour
- **NFR18:** The engine must handle Umbraco.AI provider unavailability gracefully — if a provider is down or rate-limited, surface the error clearly in the chat panel rather than failing silently or crashing the background task
- **NFR19:** The package must be removable without side effects — uninstalling the NuGet package should not leave behind database tables, configuration entries, or orphaned files that affect the Umbraco site

### Reliability

- **NFR20:** Instance state must persist to disk atomically — partial writes must never corrupt instance state (use temp file + rename pattern)
- **NFR21:** A workflow instance interrupted by app pool restart must be resumable from the last completed step without data loss
- **NFR22:** Conversation history must be persisted incrementally (append-only) — a crash mid-step must not lose the conversation up to that point
- **NFR23:** The engine must not crash the Umbraco application — all exceptions in background step execution must be caught, logged, and surfaced as step errors, never propagated to the host
- **NFR24:** Failed steps must leave the instance in a recoverable state — the user can retry the failed step or cancel the workflow, never stuck in an unrecoverable limbo

### Extensibility

- **NFR25:** The tool system architecture must be extensible — adding new tools (including Umbraco API tools in v2) must require only implementing a tool interface and registering it, not modifying the engine core
- **NFR26:** The engine must support configurable network access policies for fetch_url — v1 blocks private IP ranges by default, but the architecture must allow v2 to whitelist specific local endpoints (e.g., the Umbraco Management API) without bypassing SSRF protection globally

