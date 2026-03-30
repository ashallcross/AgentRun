---
title: "Product Brief: Shallai.UmbracoAgentRunner"
status: "complete"
created: "2026-03-27"
updated: "2026-03-27"
inputs:
  - "_bmad-output/planning-artifacts/umbraco-agent-workflow-ideas.md"
  - "Cogworks BMAD Agent Runner — docs/architecture.md, docs/workflow-authoring.md"
  - "Cogworks Audit Agents — workflow definitions (audit, content-pipeline, upgrade, consent-compliance)"
  - "Umbraco.AI GitHub repository and API documentation"
  - "Matt Brailsford — Introducing Umbraco AI (technical deep dive)"
  - "Umbraco.AI seed data component (Matt Brailsford gist)"
  - "Umbraco Marketplace AI category analysis"
  - "Competitive analysis: Sitecore Agentic Studio, Kontent.ai, Acquia AI Agents"
  - "Microsoft Agent Framework (MAF) documentation"
---

# Product Brief: Shallai.UmbracoAgentRunner

## Executive Summary

Agencies running Umbraco sites spend days on work that AI agents could handle in minutes — content audits done page by page, brand compliance checks done by reading guidelines and eyeballing every page, media libraries with hundreds of images missing alt text that nobody has time to fix. These are multi-step analytical processes, and Umbraco has no way to automate them.

Umbraco.AI gives editors single-action AI features: generate alt text, suggest a meta description, chat with the Copilot. But real content operations need structured pipelines — Agent A crawls the site, Agent B analyses the findings, Agent C scores every page, Agent D produces a prioritised action plan. Each step reads the previous step's output, uses the right model for the job, and produces a concrete deliverable.

**Shallai.UmbracoAgentRunner** is a .NET-native multi-agent workflow engine, installable as a NuGet package into any Umbraco 14+ site. It loads workflow definitions from YAML files and agent prompts from markdown, executes them through Umbraco.AI's provider system, and surfaces them in a Bellissima dashboard. The engine ships once. Every new capability is a folder of markdown and YAML dropped into the project. No code. No rebuild.

This architecture is already production-proven: it is a .NET port of an existing Node.js runner that has executed structured agent workflows across multiple client engagements, including 8-step audit delivery, content research pipelines, and compliance assessments.

## The Problem

Umbraco's AI features solve single-action problems well. But real content operations require multi-step processes:

- **Content quality audits** need a crawler, an analyser, a scorer, and a report writer working in sequence — agencies currently spend 2-5 days doing this manually per client
- **Brand compliance checks** need to ingest guidelines, scan the content corpus, check every page against the rules, and produce a remediation plan — most organisations never do this comprehensively because the labour cost is prohibitive
- **Media library cleanup** needs to inventory all assets, identify gaps (missing alt text, unused files, undersized images), and generate fixes in bulk — Umbraco.AI.Prompt can generate alt text for one image at a time, but a 500-image library needs a pipeline, not a button

Today, there is no way to orchestrate these inside Umbraco. Agencies do it manually, build bespoke external tooling, or simply don't do it. Editors have no visibility into the process. Nobody can reuse someone else's workflow.

Meanwhile, competing platforms are moving fast. Sitecore launched Agentic Studio with 20 pre-built agents. Kontent.ai branded itself as an "Agentic CMS." The entire open-source .NET CMS segment — Umbraco included — has nothing equivalent.

## The Solution

A NuGet package that brings multi-agent workflow orchestration into the Umbraco backoffice.

**For developers:** Define workflows in YAML. Write agent prompts in markdown. Drop them into a folder. The engine discovers them on startup — no code changes, no rebuild. Each step declares which Umbraco.AI profile to use (enabling per-step model selection), which tools the agent can access, what artifacts it reads from previous steps, and what it writes. Workflows live in source control — version-controlled, diffable, reviewable in pull requests, deployable through CI/CD.

**For editors:** A Bellissima dashboard shows available workflows. Start a run, watch agents work through each step, review intermediate outputs at each stage before advancing. Every step produces a readable markdown artifact — inspect what the agent found before letting the next agent act on it.

**How a workflow runs (example: 3-step content quality audit):**

1. **Step 1 — Scanner agent** reads all content nodes via the Content Delivery API, catalogues page titles, meta descriptions, headings, image alt text, word counts. Writes `scan-results.md`. Uses an affordable model (e.g., GPT-4o-mini via the "bulk" profile).
2. **Step 2 — Analyser agent** reads `scan-results.md`, scores each page on SEO completeness, readability, accessibility, and content freshness. Writes `quality-scores.md`. Uses a capable model (e.g., Claude Sonnet via the "analysis" profile).
3. **Step 3 — Reporter agent** reads `quality-scores.md`, produces a prioritised action plan with executive summary, page-by-page findings, and recommended fixes. Writes `audit-report.md`.

In interactive mode, the editor reviews each step's output before advancing. In autonomous mode, steps chain automatically — the engine checks completion and advances without human intervention. The workflow author chooses which mode suits the use case. The final report is a downloadable deliverable either way.

**Built on Umbraco.AI's foundations:** All LLM calls go through `IAIChatService` and Umbraco.AI Profiles. Whatever provider the site has configured — OpenAI, Anthropic, Google, Bedrock — the runner uses it. The middleware pipeline (logging, token tracking, governance) applies automatically.

**Cost-governed AI workflows:** Per-step profile selection is not a technical detail — it is what makes multi-agent workflows economically viable. A 3-step content audit across 200 pages might cost $2-5 using a mix of cheap and capable models. Without per-step control, the same workflow on an expensive model could cost $50+. The workflow author controls the cost profile, and the Umbraco admin controls which models are available.

**Aligned with Microsoft Agent Framework:** The step executor leverages MAF patterns for agent-tool interaction, ensuring alignment with the direction Umbraco.AI.Agent is heading. This is not a competing framework — it is a structured orchestration layer that complements the Copilot's conversational paradigm.

## What Makes This Different

**First of its kind for Umbraco.** No package, no tool, and no HQ product delivers multi-step agent orchestration. The Copilot is conversational. Prompts are single-action. This is structured, sequential, artifact-driven workflow execution — with the choice of human review at every stage or fully autonomous end-to-end processing.

**Workflows are data, not code.** Adding a new capability means adding a folder with YAML and markdown. A workflow.yaml JSON Schema provides IDE validation and autocomplete. An agency technical lead can create a new AI-powered service offering in an afternoon without writing C#, deploy it to every client site, and charge for the output. The unit economics of turning a markdown file into a billable deliverable is transformative for agency business models.

**The open alternative to closed agentic CMS platforms.** Sitecore Agentic Studio and Kontent.ai's agentic features are proprietary, locked to their platforms and pricing tiers. Shallai.UmbracoAgentRunner is open-source — workflows you own as files in your repo, running on any LLM provider, with no vendor lock-in. This positions Umbraco as the open-source CMS that takes AI workflows seriously.

**Cost-governed by design.** Per-step profile selection is the single feature that makes this viable for production use. Enterprises are concerned about uncontrolled AI spend. This is the only system in the Umbraco ecosystem that lets you set a model policy per step and predict costs before execution.

**Proven architecture.** This is a .NET port of a production runner that has executed multi-step agent workflows across client engagements — 8-step audit delivery, 4-step content pipelines, 8-step Umbraco upgrade analysis, and 4-step compliance assessments. The YAML schema, artifact handoff pattern, tool system, and prompt assembly are battle-tested.

## Who This Serves

**Primary: Umbraco agency technical leads** — the person who decides what tools and packages go into client projects. They are looking for ways to deliver more value with less labour. A workflow engine that turns markdown into billable audit reports changes their service economics. They evaluate on: does it install cleanly, does the example work out of the box, can I modify it and see results.

**Secondary: Umbraco editors and content managers** who consume pre-built workflows to solve operational problems. They interact through the dashboard, reviewing and advancing steps, without needing to understand the underlying agent architecture. They evaluate on: does it give me something useful, is the output trustworthy, can I act on the results.

**Tertiary: The Umbraco developer community** — an open-source workflow engine that anyone can contribute workflows to creates a shared library of capabilities that grows independently of HQ's roadmap.

## Business Model

The engine is **open-source** (MIT or Apache 2.0). The primary goal is community positioning and authority-building, not direct revenue.

Potential commercial extensions (v2+):
- **Premium workflow packs** — curated, tested, documented workflows for specific use cases (comprehensive SEO audit, WCAG compliance, brand governance) sold as one-time purchases
- **Agency services** — the creator's consultancy uses the engine internally to deliver fixed-fee productised services, with the engine as an efficiency multiplier

The open-source engine creates a funnel: developers discover the package, install it, see the value, and either build their own workflows or purchase pre-built ones. Either way, the creator is positioned as the Umbraco AI workflow authority.

## Technical Approach

**Hosting model:** The workflow engine runs in-process within Umbraco using `IHostedService` for background step execution. Steps execute as background tasks, not within web request pipelines, avoiding timeout and recycling issues. Instance state persists to disk, so interrupted steps can be resumed after app pool restarts.

**Tool system (v1):** Agents interact with the CMS and filesystem through a sandboxed tool registry. v1 ships with:
- `read_file` / `write_file` / `list_files` — scoped to the instance folder, no path traversal
- Future tools (v2): `read_content_node`, `query_content`, `read_media`, `fetch_url`, `spawn_sub_agent`

**Artifact model:** Steps communicate via markdown files written to the instance folder. Each step declares `reads_from` (files it consumes) and `writes_to` (files it produces). Any step can read any prior step's output — not just the immediately preceding step. Completion checking verifies required output files exist before a step is considered done.

**Security:** Workflows execute under the Umbraco application's identity. v1 tools are read/write to the instance folder only — no content modification. A dry-run mode allows previewing what a workflow would do without executing LLM calls.

## Success Criteria

| Signal | Target (first 6 months) |
|--------|------------------------|
| NuGet installs | 300+ unique installs |
| Working example | Example workflow runs successfully out-of-the-box on a fresh Umbraco 17 install |
| Community workflows | At least 3 community-contributed workflow definitions |
| Production usage | At least 2 agencies running workflows against real client sites |
| Community visibility | Featured in UMB.FYI newsletter, discussed in Umbraco community channels |
| Completion rate | >80% of started workflows run to completion without errors |

**Kill criterion:** If after 90 days the package has fewer than 50 installs and zero community engagement, reassess whether the approach needs fundamental changes or whether the market is not ready.

## Scope

### v1 (MVP)

- Workflow engine: YAML parsing (with JSON Schema for validation), agent prompt assembly, step execution via IAIChatService
- Tool system: read_file, write_file, list_files (sandboxed to instance folder)
- Artifact handoff between steps with completion checking
- Per-step Umbraco.AI Profile selection
- Instance management (create, track progress, store state on disk)
- Bellissima dashboard: workflow picker, instance list, step progress view, chat panel showing agent messages and tool calls in real-time via SignalR
- Step advancement: manual (user reviews and advances) or autonomous (steps chain automatically via completion checks) — configurable per workflow via a `mode` flag
- One practical example workflow (content quality audit — 2-3 steps) that works out-of-the-box
- JSON Schema for workflow.yaml enabling IDE validation and autocomplete
- Getting-started documentation and workflow authoring guide
- NuGet package installable into Umbraco 17+

### v1 Explicitly Excludes

- Sub-agent spawning (v2)
- Scheduled/triggered pipeline execution via API (v2)
- Context eviction and token budget management (v2 — v1 targets short workflows)
- Phase system within steps (v2)
- Sidecar memories (v2)
- Variant system (v2)
- File uploads (v2)
- Copilot integration (v2 — register workflows as Custom Tools)
- Context bridge (v2 — surface artifacts as Custom Context Resource Types)
- Writing results back into Umbraco content nodes (v2)
- Content modification tools (v2 — v1 is read-only analysis and reporting)

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Umbraco HQ builds native multi-step orchestration | High | Ship fast, build community adoption, position as the reference implementation they may adopt rather than compete with |
| Umbraco.AI APIs change in breaking ways | Medium | Pin to stable API surface (IAIChatService), maintain compatibility tests, engage with HQ on roadmap |
| LLM non-determinism undermines trust in outputs | Medium | Intermediate artifacts at each step let users review before advancing — the human-in-the-loop is a feature, not a limitation |
| YAML authoring hits complexity ceiling | Medium | JSON Schema for validation, comprehensive examples, and a workflow authoring guide reduce friction; complex orchestration remains a v2 concern |
| Long-running workflows fail mid-execution | Medium | Background task execution with disk-based state persistence; steps are resumable after interruption |

## Vision

If this works, it becomes the **workflow layer for Umbraco AI**. The engine is the platform; every workflow is a product.

**Near-term (6 months):** A growing library of community and commercial workflow packs — content audit, brand compliance, SEO analysis, accessibility checks, media intelligence. Developers share workflows the way they share uSync presets or Starter Kits today.

**Medium-term (12 months):** Scheduled execution via API triggers enables recurring workflows — weekly content health checks, monthly brand compliance scans, daily content freshness reports. The Copilot can trigger workflows conversationally ("run a content audit on the Blog section"). Workflow artifacts feed back into the Copilot's context, creating a flywheel where the runner produces intelligence and the Copilot surfaces it to editors in real time.

**Long-term (18-24 months):** A recognised standard for how structured AI workflows run inside Umbraco. A community workflow repository modelled on GitHub Actions or Umbraco Starter Kits. Conference talks at Codegarden and Spark. A reference implementation that Umbraco HQ may choose to align with or absorb — either outcome is a win for the creator's positioning in the ecosystem.
