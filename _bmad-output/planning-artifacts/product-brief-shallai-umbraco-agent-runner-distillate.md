---
title: "Product Brief Distillate: Shallai.UmbracoAgentRunner"
type: llm-distillate
source: "product-brief-shallai-umbraco-agent-runner.md"
created: "2026-03-27"
purpose: "Token-efficient context for downstream PRD creation"
---

## Creator Context

- Adam Shallcross, Cogworks Consulting — building under personal brand, not Cogworks brand
- Not a professional .NET developer — builds using Claude Code and BMAD agent workflows
- All implementation stories must be structured so an AI coding agent can execute them: clear acceptance criteria, explicit file paths, small verifiable increments
- Favour established .NET patterns with extensive documentation (Claude Code training data matters)
- Targeting ~1 week to a working PoC/demo — speed matters
- The runner engine working is the priority; example workflows are fast followers

## Source Architecture (Node.js Runner to Port)

- **Repo:** `/Users/adamshallcross/Documents/Cogworks BMAD Agent Runner` — Hono backend, React 19 frontend, pnpm monorepo
- **Architecture docs:** `docs/architecture.md` (600+ lines), `docs/workflow-authoring.md` (600+ lines) — these are the authoritative references for the port
- **workflow.yaml schema:** name, description, icon, mode (interactive|autonomous), variants, autonomous_timeout_idle (120s default), autonomous_timeout_max (600s default), steps[] — each step has id, name, description, agent path, model (optional), tools[], reads_from[], writes_to[], completion_check (files_exist[]), data_files, uploads, phases
- **Agent prompt assembly order:** (1) agent .md file, (2) sidecars/{id}/instructions.md, (3) sidecars/{id}/memories.md, (4) runtime context block with identity, step, tools, data file paths with {variant} substitution, prior artifact paths with existence flags, config values
- **Model resolution chain (current):** step.model > DEFAULT_MODEL env > claude-sonnet fallback. In .NET port: step declares a profile alias > workflow default profile > site default profile
- **Provider routing (current):** claude-* prefix → Anthropic adapter, everything else → OpenAI-compatible. In .NET port: replaced entirely by Umbraco.AI's IAIChatService + Profiles
- **Tool system (current):** 8 tools — read_file, write_file, list_files, read_manifest, update_manifest, write_sidecar, spawn_sub_agent, fetch_url. Plus undocumented run_command in upgrade workflow
- **Context eviction (current):** Two-layer — between turns (prune on writes_to), within turns (write-triggered eviction + 80% budget safety net). Token estimation: JSON.stringify length / 3. Eviction stub: "[Content saved to file and removed from context]"
- **Instance state:** Filesystem-based, no DB. instance.yaml is single source of truth. Atomic writes via tmp+rename. Advisory locking. JSONL conversation persistence
- **Execution modes:** Interactive (human-driven, manual advance) and Autonomous (API-triggered, auto-advances on completion check)
- **Streaming:** SSE events for real-time chat (status, delta, tool, retry, done, error)

## Existing Workflows (4 production, portable markdown + YAML)

- **Audit Delivery:** 8-beat, 7 agents, 4 audit type variants, phases with uploads, interactive mode. Uses Qwen for qualification step
- **Content Pipeline:** 4-step autonomous, scout/analyst/writer/publisher, variant-based content types, fetch_url
- **Umbraco Upgrade:** 8-step, version boundary variants (v13→v14→v15→v17), run_command tool, human gate at step 8. INTERNAL ONLY — powers £9,999 fixed-fee upgrade service, never open-source this
- **Consent Compliance:** 4-step, Playwright scan uploads, spawn_sub_agent, fetch_url

## Target Umbraco Environment

- Umbraco 17.1.0 on .NET 10.0
- Test site already has: Umbraco.AI 1.0.0, Umbraco.AI.Agent 1.0.0, Umbraco.AI.Agent.Copilot 1.0.0-alpha1, Umbraco.AI.Anthropic 1.0.0, Umbraco.AI.OpenAI 1.0.0
- Test site location: `/Users/adamshallcross/Documents/Umbraco AI/UmbracoAI/`

## Umbraco.AI Integration Points

- **IAIChatService** — primary LLM interface. Inject, call GetChatResponseAsync() with profile alias. Returns ChatResponse with Message.Text
- **Profiles** — combine connection + model + temperature + system prompt. Code requests by alias. Per-step profile aliases replace per-step model overrides
- **IChatClient** — M.E.AI standard abstraction underlying IAIChatService. Supports streaming via GetStreamingChatResponseAsync()
- **Composers** — IComposer implementations auto-register on startup. Use AddNotificationAsyncHandler for seed data
- **Custom Tools** — register with Umbraco.AI.Agent tool system. V2 feature: register workflow triggers as Custom Tools for Copilot integration
- **Custom Context Resource Types** — v2 feature: surface workflow artifacts as context for Copilot
- **Middleware pipeline** — logging, caching, rate limiting, governance applied automatically
- **Providers installed via NuGet:** Umbraco.AI.OpenAI, Umbraco.AI.Anthropic, Umbraco.AI.Google, Umbraco.AI.Amazon, Umbraco.AI.MicrosoftFoundry
- **Connections** support $ prefix for appsettings.json/env var resolution (avoid storing keys in DB)

## Bellissima UI Context

- Backoffice built with Lit Web Components, TypeScript, Vite
- Dashboards are "the simplest extension type" — good starting point
- Registration via umbraco-package.json in App_Plugins/{PackageName}/
- Official tutorials: Creating Your First Extension, Creating a Custom Dashboard (docs.umbraco.com)
- Umbraco 17 Backoffice Extensions for Beginners article on Medium (Girish Sasikumar)
- Real-time updates via SignalR (already used by Umbraco internally)

## Technical Decisions Made

- Use IAIChatService (not raw IChatClient) for all LLM calls — inherits middleware pipeline
- Per-step profile aliases in workflow.yaml (replaces model identifiers)
- YAML parsing via YamlDotNet NuGet package
- IHostedService for background step execution (not web request pipeline)
- Disk-based instance state (matching current runner pattern) — resumable after app pool restart
- SignalR hub for real-time chat streaming to dashboard
- JSON Schema for workflow.yaml — IDE validation and autocomplete
- Align with MAF patterns for future-proofing, even though more complex
- v1 tools: read_file, write_file, list_files only (sandboxed to instance folder)
- v1 is read-only (no content modification tools) — safety first
- Both interactive and autonomous modes in v1

## Competitive Landscape

- **Sitecore Agentic Studio** — 20 pre-built agents, SaaS-only, enterprise pricing, launched Nov 2025. Validates market but inaccessible to mid-market
- **Kontent.ai Agentic CMS** — built-in governance/compliance/translation agents. Headless-only, locked to Kontent.ai
- **Acquia AI Agents** — Drupal-based, SaaS-only
- **Microsoft Agent Framework (MAF)** — RC/GA, merges AutoGen + Semantic Kernel. General-purpose, zero CMS awareness. Umbraco.AI.Agent is built on MAF
- **AutoGen & Semantic Kernel** — now in maintenance mode (bug fixes only), Microsoft pushing everyone to MAF
- **Umbraco marketplace AI packages:** Perplex AI Assistant (per-field meta suggestions), Growcreate AI Toolkit (tone/SEO/accessibility/multilingual), Cyber-Solutions AI Essentials Toolkit (content generation), AI Log Analyser (Spark hackathon). None do multi-step orchestration
- **Paul Seal** — built content migration tool at Spark 2026 (URL scraping → block mapping → import via Copilot tool), also PackageScriptWriter.Cli adopted as official Umbraco AI Skill. Occupies developer tooling + content import lane. Does NOT occupy content operations/quality/governance lane

## Umbraco AI Community Team (27 members)

- Advisory Board: Paul Seal, Ollie Picton, Kim Gordon Taanning, Emma Garland, Andy Eva-Dale, Callum Whyte, Joana Knobbe, Steve Hart, Matthew Wise
- Plus 18 community team members
- Working with HQ on shaping official AI packages — advisors, not product builders
- Adam missed 2026 application window — goal is to be invited for 2027 by demonstrating shipped work

## Market Data

- AI agents market: $7.84B (2025) → projected $52.62B by 2030 (CAGR 46.3%)
- 85% of organisations have adopted agents in at least one workflow (2026)
- Gartner: 40% of enterprise apps will embed task-specific AI agents by end of 2026
- Umbraco Marketplace: no payment processing, commercial packages self-managed. uSync Complete uses one-time project licences. Niche tooling can generate $1K-5K/month
- 93% of IT leaders plan autonomous agents within 2 years

## Rejected/Deferred Ideas

- **Runner as SaaS** — rejected for v1. Hosting costs, SaaS complexity, too much to take on. The .NET-native NuGet approach eliminates this entirely
- **Competing with Umbraco.AI.Agent/Copilot** — rejected. This complements, not competes. Copilot is conversational; this is structured orchestration
- **Open-sourcing the upgrade workflow** — rejected permanently. It's commercial IP powering the £9,999 fixed-fee service
- **Building all 10 ideas from the research doc** — deferred. Focus on the engine first; workflows are fast followers
- **Migration Planner as standalone product** — deprioritised after Paul Seal built content migration at Spark. The planning/analysis layer could complement but is thinner as a standalone product
- **Approaching Umbraco HQ before shipping** — rejected for now. Build and demo first, let the work speak
- **Sub-agent spawning in v1** — deferred to v2 for complexity reasons
- **Context eviction in v1** — deferred. V1 targets short workflows (2-4 steps) where context limits are unlikely to be hit

## Epic Structure (from scoping conversation)

| Epic | What It Delivers | Depends On |
|---|---|---|
| E1: Project Scaffolding | Solution structure, NuGet package project, test site with Umbraco.AI | Nothing |
| E2: Workflow Engine Core | YAML parsing, workflow registry, agent prompt loader, instance manager | E1 |
| E3: Step Executor | Prompt assembly, LLM call via IAIChatService, tool execution loop | E2 |
| E4: Tool System | read_file, write_file, list_files with path sandboxing | E2 |
| E5: Artifact Handoff | Steps read/write artifacts, completion checking, autonomous mode | E3 + E4 |
| E6: Dashboard UI (Basic) | Bellissima dashboard — workflow list, instance creation, step progress | E1 |
| E7: Chat Interface | Chat panel — send messages, see responses, stream via SignalR | E6 + E3 |
| E8: Integration Testing | Run the example 3-step content audit workflow end to end | All above |
| E9: Packaging | NuGet package, marketplace metadata, installation docs, JSON Schema | E8 |

## Open Questions for PRD

- How exactly does the tool execution loop work in .NET with IAIChatService? Need to verify that IAIChatService supports function calling / tool use and how results are returned
- Does Umbraco.AI's OpenAI provider support custom base URLs (for Qwen via DeepInfra, Ollama)? Critical for the cost-governance story
- What's the exact MAF integration surface? Should step execution use MAF's agent types directly, or just align with the patterns?
- How does SignalR integrate with Bellissima dashboard extensions? Need to verify the plumbing for real-time streaming
- What's the file path convention for workflow folders in an Umbraco NuGet package? App_Plugins? wwwroot? Custom configurable path?
- How does the package handle first-run experience? Auto-create example workflow folder? Seed via Composer?

## Workflow Ideas for v1 Example and Fast Followers

From the research doc and conversation:
- **Content Quality Audit (v1 example):** 3 steps — scan content tree, analyse quality, produce report
- **Brand/Tone of Voice Checker:** Ingest brand guidelines, scan content, score compliance
- **Stale Content Finder:** Inventory content tree, flag pages not updated in X months, report
- **Media Library Gap Analysis:** Inventory media, identify missing alt text/descriptions, report
- **SEO Completeness Check:** Scan meta titles, descriptions, headings, structured data, report

All of these are read-only analysis workflows — safe, useful, and immediately demonstrable.
