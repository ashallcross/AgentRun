# Umbraco Agent Workflow Ideas

Buildable agent workflows and packages that leverage the Cogworks Agent Runner and Umbraco.AI's extension points to solve real Umbraco pain points and drive consultancy business.

---

## What Umbraco.AI Already Provides (Do Not Rebuild)

Before designing anything, it is essential to understand what Umbraco HQ has already shipped. Building packages that duplicate existing functionality is a waste of time and positions you as behind, not ahead.

### Umbraco.AI (Core)

The foundation layer, built on Microsoft.Extensions.AI (M.E.AI). Provides:

- **Provider-agnostic AI infrastructure** with 5 providers: OpenAI, Anthropic, Google Gemini, Amazon Bedrock, Microsoft AI Foundry
- **Profiles** that configure which model is used for what purpose, switchable at any time
- **Middleware pipeline** for cross-cutting concerns: logging, caching, rate limiting, policy enforcement (mirrors ASP.NET Core middleware patterns)
- **Contexts** as configurable knowledge sources that agents and prompts draw from
- **Governance controls** defining where AI can operate, what tone it uses, what data it can access

### Umbraco.AI.Prompt (Shipped, on Marketplace)

Predefined AI actions attached to specific document types and fields. An editor clicks a button next to a field and the prompt executes. Uses Mustache syntax to reference property values. Can include the full page as context.

**Already handles:** Alt text generation, SEO title generation, content summarisation, tag generation, per-field AI actions. Do not rebuild any of these.

### Umbraco.AI.Agent (Shipped)

Built on the Microsoft Agent Framework (MAF). Agents use tools (functions) to interact with systems and make multi-step decisions. Exposed via AG-UI (HTTP streaming protocol) for real-time web experiences.

**Key extension points for package developers:**
- **Custom Tools** that agents can call to interact with your systems
- **Custom Context Resource Types** that introduce new knowledge sources
- **Custom Middleware** for the AI pipeline

### Umbraco.AI.Agent.Copilot (Alpha/Prerelease)

A chat sidebar in the backoffice. Context-aware: knows what page you are editing, what document type you are on. Editors interact conversationally to get help with content tasks.

**Already handles:** Context-aware editorial assistance, content suggestions, structure help, content comparison and analysis. Do not rebuild a generic editorial chat assistant.

### Developer MCP Server (Shipped)

A Node.js app exposing 315+ Management API endpoints to external AI tools (Claude Desktop, Cursor, GitHub Copilot). Uses API Users for authentication. Supports content creation, document type management, media operations, log inspection, and architecture review via natural language.

An Editor MCP for content teams is in development.

### Agent Skills (Shipped, 66+ Skills)

Structured knowledge files (not code) that provide AI coding tools with deep Umbraco backoffice extension expertise. Covers 40+ extension types with progressive discovery. These are context documents for external AI tools, not runtime packages.

**Already handles:** Helping AI tools build Bellissima extensions correctly. Do not rebuild Umbraco development guidance tooling.

### What Umbraco HQ Has Explicitly Flagged as Partner Opportunities

From the "AI, Intentionally" blog post (February 2026), Umbraco HQ explicitly invites partners to build:

- Predictive search and intent modelling
- Intelligent asset tagging (beyond basic alt text)
- Advanced AI-powered image editing
- Super-personalised content recommendation
- Dynamic A/B test and optimisation
- Industry-specific assistants
- Advanced workflow automation
- Custom marketing flows

---

## Genuine Extension Points for Umbraco.AI Packages

Packages that extend Umbraco.AI rather than compete with it. These plug into the existing infrastructure and add capabilities the platform does not have.

---

### 1. Content Quality Score -- Content App Package

**What it is:** A Bellissima content app (the tab strip next to Content/Info on each page) that runs multiple specialist checks against the current content node and presents a unified quality score with per-dimension breakdowns.

**Why this is not what Prompt or Copilot already do:** Prompt runs a single action on a single field when an editor clicks a button. Copilot is a conversational chat. Neither provides an always-visible, automatic, multi-dimensional quality assessment that updates as the editor works. This is a passive monitoring panel, not an action or a conversation.

**What it checks (each dimension scored independently):**

- **SEO completeness** -- meta title length, meta description presence and length, heading hierarchy, keyword density, internal link count, structured data presence
- **Readability** -- Flesch-Kincaid score, sentence length, passive voice ratio, jargon detection
- **Accessibility** -- alt text on all images, heading order (no skipped levels), link text quality (no "click here"), colour contrast warnings on inline styles
- **Content freshness** -- last modified date relative to content type expectations, stale link detection
- **Brand compliance** -- checks against configurable terminology rules, forbidden phrases, tone guidelines (loaded as a Custom Context Resource Type)

**How it integrates with Umbraco.AI:**

- Registers as a Custom Tool pack so the Copilot can reference quality scores ("What's wrong with this page's SEO?")
- Uses Umbraco.AI's provider abstraction for LLM-powered checks (readability suggestions, brand tone analysis)
- Rule-based checks (heading order, meta length, alt text presence) run without LLM calls to keep costs predictable
- Brand rules stored as a Custom Context Resource Type, editable in the backoffice

**Technical approach:** Bellissima content app extension (Web Component, Lit, TypeScript). Backend .NET service implementing `INotificationHandler<ContentSavingNotification>` to trigger checks. Results cached per content node, invalidated on save. Scoring displayed as a visual gauge with expandable per-dimension details.

**What competitors ship that Umbraco lacks:** Sitecore Stream includes real-time content quality scoring. Kentico AIRA scores content against brand strategy. Optimizely Opal has a Content Refresh Analysis agent. No Umbraco package provides this.

---

### 2. Media Library Intelligence -- Bulk Processing Package

**What it is:** A backoffice dashboard and media upload lifecycle extension that provides bulk AI processing of the entire media library, not just single-field alt text generation.

**Why this is not what Prompt already does:** Umbraco.AI.Prompt can generate alt text for one image field when an editor clicks a button on one content node. It cannot process 500 existing media items that are missing alt text. It cannot auto-tag images against the site's existing media taxonomy. It cannot assess image quality or suggest optimal crops. It does not hook into the media upload lifecycle to run checks automatically.

**What it does:**

- **Bulk retroactive processing** -- dashboard view showing all media items with missing alt text, missing descriptions, no tags. One-click queue to process all of them with human review before committing
- **Upload lifecycle hooks** -- when new images are uploaded, automatically queue vision analysis. Generate alt text, description, and suggested tags. Store as draft metadata for editor approval
- **Taxonomy-aware tagging** -- reads the site's existing media type structure, folder hierarchy, and any tag properties. Maps AI-generated tags to the existing taxonomy rather than inventing new ones
- **Image quality assessment** -- checks dimensions against configured image crop definitions, flags images that are too small for their intended crops, detects duplicate/near-duplicate images in the library
- **Bulk operations UI** -- filterable, sortable dashboard showing processing queue, pending reviews, and completed items. Batch approve/reject controls

**How it integrates with Umbraco.AI:**

- Uses Umbraco.AI's provider abstraction for vision model access (GPT-4o, Claude, Gemini vision)
- Registers Custom Tools so the Copilot can trigger bulk operations ("Process all images missing alt text in the Blog folder")
- Middleware integration for token tracking and cost monitoring on bulk operations

**Technical approach:** Bellissima dashboard extension. Backend uses Hangfire or similar for background job processing. Hooks into `INotificationHandler<MediaSavedNotification>` for upload lifecycle. Results stored in a custom database table. Management API extensions for queue status and batch operations.

---

### 3. Custom Context Resource Types -- Analytics & Search Console Bridge

**What it is:** A package that brings external performance data into Umbraco.AI's context system, so agents and the Copilot can reference real analytics when helping editors.

**Why this does not exist:** Umbraco.AI's context system is designed to be extended with custom resource types, but no packages currently bridge external analytics platforms into it. The Copilot knows what page you are editing but has no idea how that page performs.

**What it provides:**

- **Google Analytics context** -- page views, bounce rate, average time on page, traffic sources for the current content node. Available to any agent or prompt via the context system
- **Google Search Console context** -- impressions, clicks, CTR, average position, top queries for the current URL. Available to any agent or prompt
- **Content performance scoring** -- combines analytics data with content metadata to produce actionable context ("This page gets 5,000 views/month but has a 78% bounce rate and no meta description")

**How it integrates with Umbraco.AI:**

- Implements Umbraco.AI's Custom Context Resource Type interface
- Data appears automatically in the Copilot's context when an editor is working on a page
- Prompts can reference performance data using Mustache syntax ("Write a meta description for this page. It currently ranks for: {{searchConsoleQueries}}")
- Registered as Custom Tools so agents can query analytics ("Which pages have the highest bounce rate?")

**Technical approach:** OAuth2 integration with Google APIs. Background sync job caches analytics data per URL in a custom table. URL matching maps analytics URLs to Umbraco content nodes. Context resource type surfaces cached data to the AI layer on demand.

---

### 4. Umbraco.AI Agent Tool Packs

**What it is:** Collections of Custom Tools that extend what the Copilot and custom agents can do. These are the "plugins" for Umbraco.AI.Agent, packaged as NuGet packages.

**Why this matters:** Umbraco.AI.Agent ships with a basic set of tools. The tool system is explicitly designed for extension. Each tool pack adds domain-specific capabilities that any agent (including the Copilot) can call.

**Tool Pack A: SEO Tools**

- `analyse_page_seo` -- runs a comprehensive SEO check on a content node, returns structured findings
- `suggest_internal_links` -- analyses the content tree to find relevant internal linking opportunities for the current page
- `check_structured_data` -- validates JSON-LD/schema.org markup on the published page
- `compare_competitor_serp` -- fetches search results for target keywords and compares the current page's approach

**Tool Pack B: Accessibility Tools**

- `audit_page_accessibility` -- runs WCAG checks against published HTML, returns structured violations
- `suggest_aria_improvements` -- analyses the page's component structure and suggests ARIA enhancements
- `check_reading_level` -- scores content against target reading levels (useful for public sector clients targeting B1/A2)
- `generate_accessible_alternatives` -- produces simplified versions of complex content sections

**Tool Pack C: Content Governance Tools**

- `check_brand_compliance` -- validates content against configured brand rules (tone, terminology, forbidden phrases)
- `detect_stale_content` -- identifies content nodes not updated within configurable thresholds
- `find_orphaned_content` -- locates published pages unreachable from navigation
- `check_legal_disclaimers` -- validates required disclaimers/cookie notices/accessibility statements are present

**How they integrate:** Each tool pack is a NuGet package that registers tools with Umbraco.AI.Agent's tool system on startup. Once installed, the tools are automatically available to the Copilot and any custom agents. No additional configuration required beyond installing the package.

---

## Runner Workflows -- Structured Multi-Step Processes Outside the Backoffice

These run on the Cogworks Agent Runner platform. They solve problems that are fundamentally different from real-time editorial assistance. They are analytical, planning, and delivery workflows that happen before, after, or alongside the editorial process. They do not compete with Umbraco.AI's in-backoffice capabilities.

---

### 5. Content Migration Planner Workflow

**The problem it solves:** Content migration is the #1 community pain point. Forum threads about upgrade failures number in the hundreds. Developers report spending weeks on migration planning alone. Umbraco HQ has called AI-assisted migration "truly transformative" but has no product or timeline. No tooling exists for the planning phase.

**What it produces:** Not the migration itself, but the migration plan, mapping documents, and transformation scripts that a developer uses to execute the migration.

**Step 1: Source Analyser Agent** -- Client uploads an export from the source CMS (WordPress XML, Sitecore serialisation, CSV dumps, or a sitemap). Agent catalogues every content type, field, relationship, and taxonomy. Produces `source-schema.md`.

**Step 2: Umbraco Schema Mapper Agent** -- Reads the source schema and proposes Umbraco document types, data types, and compositions. Flags where source patterns do not translate cleanly (WordPress flat taxonomy vs. Umbraco content tree, Sitecore multi-site architecture, Drupal's entity reference system). Produces `mapping-document.md` with a source-to-target field mapping table.

**Step 3: Transformation Rules Agent** -- For each content type mapping, defines the transformation logic: field renames, data format conversions (Gutenberg blocks to Block List JSON, Sitecore Rich Text to Umbraco TipTap RTE, WordPress shortcodes to Umbraco macros or Block List items), media path rewriting rules. Produces `transformation-rules.md`.

**Step 4: Migration Script Generator Agent** -- Reads the mapping and transformation rules, generates C# migration code using Umbraco's `IContentService` / `IMediaService` APIs. Alternatively generates uSync-compatible import files or Umbraco Deploy import archives. Produces actual `.cs` files or serialisation files.

**Step 5: QA Checklist Agent** -- Produces a test plan covering: content count validation, internal link integrity, media presence, rich text rendering fidelity, SEO metadata preservation, redirect mapping. Produces `migration-qa-checklist.md`.

**Runner features exploited:**

- Phase pause/resume lets the client review the schema mapping, consult their team, upload corrections, and resume days later
- The variant system creates source-CMS-specific workflows: WordPress-to-Umbraco, Sitecore-to-Umbraco, Drupal-to-Umbraco, each with tailored agent prompts and reference data
- Sub-agents process per-content-type transformations in parallel
- Sidecar memories accumulate migration patterns across client engagements

**Business value:** A productised service. Charge for migration assessments. The artefacts become the spec for the migration project itself, naturally leading to implementation work.

---

### 6. Upgrade Impact Analysis Workflow

**The problem it solves:** The v7/v8/v10/v13 to v14+ upgrade path causes immense community frustration. The "migration plan does not support migrating from state" error is one of the most-reported issues. Developers spend days just figuring out what will break before writing any code.

**Step 1: Codebase Scanner Agent** -- Client uploads their `.csproj`, `Startup.cs`/`Program.cs`, controllers, composers, and custom code. Agent catalogues every Umbraco API usage, identifying deprecated APIs, removed features, and breaking changes for the target version. Produces `codebase-analysis.md`.

**Step 2: Breaking Change Mapper Agent** -- Reads the analysis against a known breaking changes database (maintained in the workflow's `data/` directory, sourced from Umbraco's release notes and GitHub breaking change labels). Maps each code usage to the specific breaking change and the recommended replacement. Produces `breaking-changes-map.md`.

**Step 3: Migration Path Planner Agent** -- Determines the optimal upgrade route (direct jump vs. stepping stones), estimates effort per component, identifies which custom code can be automated vs. needs manual rewriting. Considers database migration steps, package compatibility, and hosting requirements (.NET version changes). Produces `upgrade-plan.md`.

**Step 4: Code Transformer Agent** -- Uses `spawn_sub_agent` per file to generate transformed versions of the client's code. Conversions include: AngularJS backoffice extensions to Lit/Web Components, Surface Controllers to Management API controllers, Composers to the v14+ DI patterns, `UmbracoApiController` to standard ASP.NET Core controllers with Umbraco auth policies. Produces files in a `transformed-code/` directory.

**Runner features exploited:**

- File uploads receive the client's codebase
- The `data/` directory holds version-specific breaking change databases that are maintained as reference data
- Sub-agents handle per-file transformation, keeping context tight
- Phase system lets the client review the analysis before committing to code transformation

**Business value:** Sell upgrade assessments as a fixed-price product. The workflow produces the deliverable in hours. The artefacts become the spec for the upgrade project.

---

### 7. Site Audit & Recommendations Workflow

**The problem it solves:** Producing a professional site audit (SEO, accessibility, content quality) takes days of manual work. This workflow automates the analysis and produces a client-ready deliverable.

**Why the runner, not the backoffice:** This is an external analytical process, not an editorial tool. It crawls the live site from outside, analyses it holistically, and produces a standalone report. It does not require backoffice access or Umbraco.AI integration.

**Step 1: Site Crawler Agent** -- Uses `fetch_url` to hit the client's live site, crawling key pages and pulling HTML. Produces `crawl-results.md` with page inventory, meta tags, headings, image alt text presence, response times.

**Step 2: SEO Analyst Agent** -- Reads the crawl results, evaluates meta descriptions, title tags, heading hierarchy, internal linking, structured data. Produces `seo-audit.md` with page-by-page scores and recommendations.

**Step 3: Accessibility Auditor Agent** -- Reads the crawl results, checks for missing alt text, heading order violations, colour contrast issues, ARIA usage. References European Accessibility Act requirements (effective June 2025). Produces `accessibility-audit.md`.

**Step 4: Content Quality Agent** -- Evaluates readability, thin content, duplicate content, stale pages, broken patterns. Uses `spawn_sub_agent` to run per-page quality checks in parallel. Produces `content-quality-audit.md`.

**Step 5: Recommendations Synthesiser Agent** -- Reads all previous artefacts, prioritises findings by impact and effort, produces a client-ready `audit-report.md` with executive summary, prioritised action plan, and estimated effort.

**Runner features exploited:**

- After the crawl step, the workflow pauses to let the client upload additional context (brand guidelines, content strategy docs, competitor URLs) before the analysis steps run
- The variant system creates different audit types (SEO-focused, accessibility-focused, full audit)
- Sub-agents parallelise per-page quality checks

**Business value:** A productised service sold repeatedly. Each run produces a professional deliverable in hours rather than days. The variant system lets you price different audit depths differently.

---

### 8. Umbraco Project Scaffolding Workflow

**The problem it solves:** The first 2-3 weeks of every new Umbraco project involve the same discovery and setup work: understanding the client's content needs, designing document types, generating boilerplate code. This workflow turns that into a guided, repeatable process.

**Why the runner, not MCP:** The Developer MCP can create document types one at a time via conversational commands. This workflow is a structured, multi-step process that goes from business requirements through to deployable code, with human review gates at each stage. It produces a complete project skeleton, not individual API calls.

**Step 1: Discovery Agent** -- Interviews the client about their business, content types, audiences, integrations. Produces `project-brief.md`.

**Step 2: Content Model Architect Agent** -- Reads the brief, designs document types, compositions, data types, and the content tree structure. Uses `spawn_sub_agent` to validate the model against known Umbraco anti-patterns (deep nesting, circular compositions, over-use of Nested Content when Block List is better, property editor mismatches). Produces `content-model.md` with full schema definitions.

**Step 3: Schema Generator Agent** -- Reads the content model and generates actual uSync-compatible serialisation files for document types, data types, and templates. These files can be dropped into a fresh Umbraco project's uSync folder and imported on startup. Uses `spawn_sub_agent` per document type to keep context tight. Produces files in a `usync-export/` directory.

**Step 4: Template & Component Agent** -- Generates Razor views, partial views, and view components for each document type. Uses Umbraco's `IPublishedContent` API, Models Builder patterns, and v17 conventions. Produces files in a `templates/` directory.

**Step 5: Configuration Agent** -- Generates `appsettings.json` sections, composition root registrations, Umbraco.AI configuration (if AI features are in scope), and any custom middleware. Produces `configuration/` files.

**Runner features exploited:**

- Phase pause/resume lets the client review the content model with their team before code generation begins
- Sub-agents produce one uSync file per document type, preventing context pollution
- Sidecar memories let the Discovery Agent learn client preferences across projects

**Business value:** Run this for every new engagement. Offer it as a paid "project discovery" productised service. The artefacts become the foundation of the implementation project.

---

### 9. Brand & Content Governance Workflow

**The problem it solves:** Ensuring hundreds of content pages conform to brand guidelines is manual, tedious, and typically never done comprehensively. This workflow scans an entire site against extracted brand rules and produces a prioritised remediation plan.

**Why the runner, not a backoffice package:** The Content Quality Score content app (idea #1 above) handles per-page checking during editing. This workflow handles the bulk, site-wide retrospective audit. Different use cases, different tools. The runner produces a one-off or periodic report; the content app provides ongoing monitoring.

**Step 1: Brand Ingestion Agent** -- Client uploads brand guidelines (PDF/DOCX via the upload system). Agent extracts tone of voice rules, terminology preferences, forbidden phrases, style rules, required disclaimers. Produces `brand-rules.md`.

**Step 2: Content Scanner Agent** -- Uses `fetch_url` to pull published content from the Umbraco Content Delivery API. Produces `content-corpus.md`.

**Step 3: Compliance Checker Agent** -- Reads brand rules against the content corpus, using `spawn_sub_agent` per content section. Scores each page on brand compliance, flags specific violations with the offending text and the rule it breaks. Produces `compliance-report.md`.

**Step 4: Remediation Agent** -- For each violation, produces suggested rewrites that maintain the original meaning while conforming to brand guidelines. Produces `remediation-suggestions.md`.

**Runner features exploited:**

- After the brand ingestion step, the workflow pauses to let the client review and refine the extracted rules before the scan runs
- Sidecar memories accumulate brand knowledge across client engagements

---

### 10. Content Health Monitor Workflow

**The problem it solves:** Content rot. Sites accumulate stale pages, orphaned content, broken links, and incomplete metadata over time. Nobody notices until it affects SEO or a client complains. This workflow connects to a live Umbraco instance and produces a health report.

**Step 1: Inventory Agent** -- Uses `fetch_url` to hit the Content Delivery API, enumerates the full content tree. Produces `content-inventory.md` with every page, its type, last modified date, and publication status.

**Step 2: Staleness Detector Agent** -- Reads the inventory, flags pages not updated in 6+ months, identifies orphaned content (published but unreachable from navigation), finds media items not referenced by any content. Produces `staleness-report.md`.

**Step 3: Quality Scorer Agent** -- For each content node, evaluates completeness (empty required fields, missing meta descriptions, images without alt text), scoring each page. Uses sub-agents to process in batches. Produces `quality-scores.md`.

**Step 4: Action Plan Agent** -- Synthesises findings into a prioritised remediation plan, grouped by content type and editorial team responsibility. Produces `action-plan.md`.

**Business value:** A recurring service run quarterly for clients. Demonstrates ongoing value and keeps the consultancy engaged between projects.

---

## The Bridge: Runner Artefacts Feeding Into Umbraco.AI

The most powerful play is connecting the two worlds. Runner workflows produce detailed analytical artefacts. Umbraco.AI's context system is designed to consume external knowledge sources. A bridge package could:

- **Ingest runner audit artefacts** as a Custom Context Resource Type. When an editor asks the Copilot "What should I fix on this page?", it references the latest audit findings for that specific content node.
- **Ingest runner brand rules** as a Custom Context Resource Type. The Content Quality Score content app and the Copilot both reference the brand rules extracted by the runner's Brand Ingestion Agent.
- **Surface migration plan data** so the Copilot can help developers during an active migration ("What's the mapping for the WordPress 'post' type?").

This creates a flywheel: the runner produces intelligence, the bridge package makes it available in the backoffice, and editors benefit from it in real time without knowing or caring where it came from.

---

## The Meta-Opportunity: Runner-as-a-Service for Umbraco Agencies

The runner is generic. Any workflow folder with a `workflow.yaml` and agent files creates a new capability with no code changes. The opportunity:

Build a library of Umbraco-specific workflows and offer the runner as a **managed service for Umbraco agencies**. They log in, pick a workflow (Migration Planner, Upgrade Analysis, Site Audit, Project Scaffolding), create an instance for their client, and walk through it. The agents have deep Umbraco expertise baked into their prompts and sidecar instructions.

The variant system maps naturally: "Content Migration" workflow with variants for WordPress-to-Umbraco, Sitecore-to-Umbraco, Drupal-to-Umbraco, each with source-specific agent prompts and reference data in the variant folders.

This positions the offering not as just Umbraco development but as the AI-powered delivery platform for Umbraco work.

---

## Prioritisation

### Tier 1: Build First

**Migration Planner Workflow (Runner)** -- The #1 community pain point, zero existing tooling, every agency needs it. Umbraco HQ has flagged it as transformative but has no product. The variant system around different source CMS platforms creates a defensible position. Immediately sellable as a productised service.

**Content Quality Score Content App (Umbraco.AI Package)** -- Extends Umbraco.AI rather than competing with it. Fills the gap between single-field Prompts and conversational Copilot. Visible to every editor on every page. Demonstrates deep understanding of both Umbraco.AI's extension model and editorial workflows.

### Tier 2: Build Next

**Site Audit Workflow (Runner)** -- The audit runner already exists, so extending it with Umbraco-specific agents is the lowest-effort play. Produces client-ready deliverables. Immediately billable.

**Media Library Intelligence (Umbraco.AI Package)** -- Bulk processing is something Prompt fundamentally cannot do. Upload lifecycle hooks are a clear gap. Vision model integration via Umbraco.AI's provider abstraction keeps it provider-neutral.

### Tier 3: Build When Tier 1 and 2 Prove the Model

**Upgrade Impact Analysis (Runner)** -- High value but requires maintaining version-specific breaking change databases, which is ongoing work.

**Agent Tool Packs (Umbraco.AI Packages)** -- SEO, accessibility, and governance tool packs extend the Copilot's capabilities. Lower effort individually but require the Copilot to be out of alpha for maximum impact.

**Analytics Context Bridge (Umbraco.AI Package)** -- High value for clients who care about content performance, but requires OAuth integration with Google APIs and ongoing maintenance.

**Project Scaffolding Workflow (Runner)** -- Most impressive demo of agent orchestration applied to CMS work, but lower urgency than migration and audit workflows that solve immediate client pain.

---

## Technical Notes

### Runner Capabilities Leveraged

| Runner Feature | How It's Used |
|---|---|
| Multi-step artefact handoff | Each agent reads the previous agent's output, building cumulative intelligence |
| `fetch_url` | Connects to live Umbraco instances via Content Delivery API and Management API |
| `spawn_sub_agent` | Parallelises per-page or per-content-type processing within a step |
| Phase pause/resume | Lets clients review intermediate outputs and upload additional context |
| File uploads | Receives client assets (code exports, brand guidelines, CMS exports) |
| Sidecar memories | Agents learn patterns across sessions and client engagements |
| Variant system | Source-CMS-specific agents (WordPress, Sitecore, Drupal) without code changes |
| Model mixing | Use Claude for creative/analytical steps, cheaper models for bulk processing |

### Umbraco.AI Extension Points Used

| Extension Point | Package That Uses It |
|---|---|
| Custom Tools | Agent Tool Packs, Content Quality Score, Media Library Intelligence |
| Custom Context Resource Types | Analytics Bridge, Runner Artefact Bridge |
| Middleware | Token tracking on bulk operations (Media Library Intelligence) |
| Provider abstraction | All packages use this for LLM calls, staying provider-neutral |

### Umbraco Platform Integration Points

| API / System | Use Case |
|---|---|
| Content Delivery API | Runner workflows read published content for auditing and governance |
| Management API (v15+) | Runner workflows and packages read/write content, document types, media via API users |
| uSync file format | Project Scaffolding generates importable schema files |
| Bellissima Extension API | Content apps, dashboards, and workspace views for in-backoffice packages |
| Content lifecycle notifications | Media Intelligence hooks into `MediaSavedNotification`; Quality Score hooks into `ContentSavingNotification` |
| Umbraco Deploy import/export | Migration Planner can generate Deploy-compatible archives as an alternative to uSync |
