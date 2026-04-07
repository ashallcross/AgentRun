---
stepsCompleted: [1, 2, 3, 4, 5, 6]
inputDocuments: []
workflowType: 'research'
lastStep: 1
research_type: 'market'
research_topic: 'Umbraco AI tooling landscape — packages, HQ initiatives, competitor CMS approaches'
research_goals: 'Map existing solutions, identify positioning opportunities, inform V2 planning and naming'
user_name: 'Adam'
date: '2026-04-01'
web_research_enabled: true
source_verification: true
---

# Market Research: Umbraco AI Tooling Landscape

## Executive Summary

The Umbraco AI tooling market is nascent, fragmented, and wide open for a workflow orchestration play. Every existing community package is a point solution (text generation, metadata suggestions, translation). Umbraco HQ is building foundational layers (Umbraco.AI, MCP Server) and commercial cloud products (Compose), but has not yet shipped a self-hosted workflow engine — though insider intelligence confirms one is in development with unknown scope and timeline.

**The opportunity:** Be the first self-hosted, YAML-defined AI workflow orchestration engine for Umbraco. No one else occupies this space. Competitor CMSs (Optimizely Opal, Kentico AIRA) have agent orchestration, but they're cloud-only enterprise SaaS — wrong model for the Umbraco community.

**The risk:** HQ's unknown agent runner could land and supersede the free tier. Mitigation: ship first, build community adoption, and differentiate on pro features (database storage, cost tracking, MCP support) that HQ is unlikely to include in a free offering.

**The commercial model:** Follow the uSync playbook — generous free package on NuGet.org, paid pro package on a licensed feed. £499-999 per project aligns with ecosystem pricing norms.

**The launch plan:** Marketplace listing + sample workflow + DEV Community blog post + Discord/LinkedIn push + personal demos to Advisory Board contacts. Codegarden 2026 (June 10-11, Copenhagen) CFP is open — submit a talk.

---

## Research Initialization

### Research Understanding Confirmed

**Topic**: Umbraco AI tooling landscape — packages, HQ initiatives, competitor CMS approaches
**Goals**: Map existing solutions, identify positioning opportunities, inform V2 planning and naming
**Research Type**: Market Research
**Date**: 2026-04-01

### Research Scope

**Primary Focus (Umbraco Ecosystem):**

- Umbraco HQ's AI strategy and existing packages (Umbraco.AI, preview features, roadmap signals)
- Community-built Umbraco AI packages on NuGet / Marketplace
- How Umbraco partners and agencies are currently using AI with Umbraco
- Community sentiment and demand signals (forums, Discord, blog posts, conf talks)

**Secondary Focus (Competitor CMS Approaches):**

- Optimizely's AI features and how they position them
- Kentico's AI integration approach
- Other .NET CMS platforms with AI capabilities
- Ideas and patterns worth stealing for the Umbraco context

**Light Touch (AI Frameworks):**

- Semantic Kernel, AutoGen, CrewAI — awareness only, not deep dive
- How these relate to/compete with a CMS-embedded agent runner

**Out of Scope:**

- Deep technical architecture comparison of frameworks
- Non-.NET CMS platforms (WordPress, Drupal etc.)
- General AI/LLM market sizing

**Research Methodology:**

- Current web data with source verification
- Multiple independent sources for critical claims
- Confidence level assessment for uncertain data
- Lightweight and actionable — not academic

### Next Steps

**Research Workflow:**

1. Initialization and scope setting (current step)
2. Customer Insights and Behavior Analysis
3. Competitive Landscape Analysis
4. Strategic Synthesis and Recommendations

Scope confirmed by user on 2026-04-01

**Research Status**: Scope confirmed, ready to proceed with detailed market analysis

---

## Customer Segments and Behaviour Patterns

### Segment 1: Umbraco Agency Developers (Primary Buyer)

**Profile:** Senior .NET developers at Umbraco Gold/Platinum partner agencies. They evaluate and recommend packages to their teams. Typically 5-15 years of Umbraco experience. They build sites for clients and are looking for tools that make delivery faster and more impressive.

**Behaviour Patterns:**
- Discover packages via Umbraco Marketplace, NuGet search, Discord, and community blog posts
- Evaluate based on: does it work with our existing stack? Is the code quality good? Will HQ break it in the next major version?
- Strong preference for packages that follow Umbraco conventions (IComposer registration, backoffice UI integration, NuGet distribution)
- Will trial free packages on side projects before recommending to clients
- Price-sensitive on a per-project basis but will pay for tools that save significant dev time

**Decision Drivers:**
- "Will this save me enough time to justify the learning curve?"
- "Is this maintained? Will it keep up with Umbraco major versions?"
- "Does this overlap with what HQ is building? Will it become redundant?"
- Trust signals: GitHub activity, Marketplace presence, author reputation in the community

**Interaction with AI:** Currently using Claude, Copilot, and ChatGPT for general development assistance. Warren Buckley's January 2026 Discord poll showed Claude and Copilot as popular choices among active community members. Agencies are experimenting but few have productised AI workflows for clients yet.

_Sources: [Umbraco Discord — AI tool poll](https://discord-chats.umbraco.com/t/33010651/what-ai-tool-service-you-using), [Umbraco AI Community Team announcement](https://umbraco.com/blog/announcing-the-umbraco-in-ai-community-team-the-ai-in-umbraco-advisory-board)_

### Segment 2: Umbraco Content Editors / Marketing Teams (End User)

**Profile:** Non-technical content editors working within Umbraco backoffice daily. They manage pages, media, and content across potentially hundreds of nodes. Often under pressure to produce more content faster.

**Behaviour Patterns:**
- Don't choose packages — they use what the dev team installs
- Evaluate AI tools on: "Does this make my day easier?" and "Can I trust the output?"
- Want inline assistance (generate text, suggest metadata, translate) not workflow orchestration
- Sceptical of AI quality — need to review and edit before publishing
- Most interested in: content generation, SEO metadata, translation, image alt text

**Decision Drivers:**
- Ease of use within existing Umbraco UI (no separate dashboards or new tools to learn)
- Quality of AI output for their specific domain/industry
- Editorial control — ability to review, edit, and approve before publishing

**Interaction with AI:** Existing packages like Perplex AI Assistant (meta title/description generation), Growcreate AI Toolkit (content creation, metadata, accessibility), and umContentCreator (text generation per property) target this segment directly. These are point-solution tools, not workflow orchestrators.

_Sources: [Perplex AI Assistant](https://marketplace.umbraco.com/package/perplex.ai.assistant), [Growcreate AI Toolkit](https://marketplace.umbraco.com/package/growcreate.aitoolkit), [umContentCreator](https://marketplace.umbraco.com/package/umcontentcreator)_

### Segment 3: Umbraco Solution Architects / CTOs (Strategic Decision Maker)

**Profile:** Technical leaders at agencies or in-house teams who make platform and architecture decisions. They evaluate whether AI capabilities justify investment and manage risk around vendor lock-in.

**Behaviour Patterns:**
- Watching HQ's roadmap closely — Umbraco Compose (commercial cloud product) and the Agent Skills initiative signal HQ's direction
- Concerned about building on community packages that HQ might supersede
- Evaluating multi-vendor AI strategies — don't want to be locked to a single LLM provider
- Need to justify AI investment to non-technical stakeholders

**Decision Drivers:**
- "Does this align with where Umbraco is heading, or will HQ build this natively?"
- "Can we use this across multiple client projects?"
- "What's the total cost of ownership including LLM API costs?"
- Enterprise concerns: multi-node support, audit trails, data governance

**Interaction with AI:** Following HQ's "AI, intentionally" strategy closely. Aware of the two-track approach (AI in Umbraco vs Umbraco in AI). The formation of the AI in Umbraco Advisory Board (9 members including Paul Seal, Callum Whyte, Matt Wise) and the Umbraco in AI Community Team (23 members total) signals that the community's most influential voices are actively engaged in shaping direction.

_Sources: [Umbraco AI strategy](https://umbraco.com/blog/ai-intentionally/), [Umbraco AI Advisory Board](https://umbraco.com/blog/announcing-the-umbraco-in-ai-community-team-the-ai-in-umbraco-advisory-board), [Umbraco Q1 2026 Product Update](https://umbraco.com/blog/umbraco-product-update-q1-2026/)_

### Segment 4: Umbraco HQ & Inner Circle (Influencer / Potential Competitor)

**Profile:** Umbraco HQ product team and the closely connected community leaders who shape ecosystem direction. Not a customer segment, but a critical stakeholder group that influences all other segments.

**Behaviour Patterns:**
- HQ is pursuing a platform strategy — "CMS as a platform for AI" not "CMS with AI bolted on"
- Open-sourcing foundational packages (Umbraco.AI, Umbraco.Agent, Umbraco.Prompt, Umbraco.Copilot) while building commercial offerings on top (Compose)
- Actively inviting community and partners to build on the foundation
- MCP is the strategic bet for external AI connectivity — Developer MCP Server already supports 315+ Management API endpoints
- Agent Skills (Q2 2026 target) will be the structured pattern for guiding AI agent behaviour

**What This Means for Your Package:**
- HQ is building the *foundation* (Umbraco.AI, MCP) and *cloud commercial products* (Compose)
- There's a clear gap for *self-hosted workflow orchestration* — HQ's agent play is cloud-first
- Your package builds ON their foundation (Umbraco.AI) not AGAINST it — this is a complementary position, not competitive
- The "Umbraco in AI Community Team" is literally building tools to help the community — you're one of the people doing exactly that

_Sources: [Umbraco AI GitHub](https://github.com/umbraco/Umbraco.AI), [Agent Skills blog](https://umbraco.com/blog/the-agent-ready-cms-what-are-agent-skills/), [Umbraco Compose launch](https://www.enterprisetimes.co.uk/2026/02/05/umbraco-launches-new-data-orchestration-solution-umbraco-compose/), [Umbraco Product Roadmap](https://umbraco.com/products/knowledge-center/roadmap/)_

### Behaviour Drivers Across All Segments

**Emotional Drivers:**
- Fear of being left behind — AI is moving fast, Umbraco community doesn't want to feel like a laggard
- Excitement about productivity gains — the developer community is genuinely curious and experimental
- Trust in community-built tools — Umbraco's community culture values packages built by practitioners over corporate solutions

**Rational Drivers:**
- Time-to-value — agencies bill by the project, anything that accelerates delivery is worth money
- Client expectations — enterprise clients are asking "what can you do with AI?" in RFPs
- Risk mitigation — nobody wants to build on something that gets deprecated or superseded

**Social Influences:**
- Community leaders (the Advisory Board members, MVPs, Codegarden speakers) are the primary trust vectors
- Discord and the Umbraco forum are where opinions form
- Blog posts and talks at Codegarden/local meetups drive package adoption more than marketing

**Economic Influences:**
- LLM API costs are real — orgs need visibility into what AI workflows actually cost to run
- Agency model means packages need to work across multiple client projects (not per-site cost for the agency, even if per-site for end clients)
- Free/open-source packages get trialled; paid packages need clear ROI justification

### Insider Intelligence Note

Adam has direct contact with a developer at Umbraco HQ who has confirmed they are building something similar to the agent runner package. Specifics and timeline are unknown. This means the first-mover advantage has a countdown — ship V1, build community adoption, and establish the practitioner-built alternative before HQ's version lands. The complementary positioning (builds on Umbraco.AI, not against it) remains valid, but awareness that a direct competitor from HQ is in development is critical context for all strategic decisions.

---

## Customer Pain Points and Unmet Needs

### Pain Point 1: No Workflow Orchestration Layer (Critical Gap)

Every AI package in the Umbraco Marketplace today is a **point solution** — generate text for this property, suggest metadata for that page, translate this content. There is no package that lets you define a multi-step AI workflow (e.g., "audit all blog posts for SEO, then generate missing meta descriptions, then flag posts that need content updates") and run it as a repeatable, observable process.

**Who feels this:** Agencies trying to sell AI-powered content services to clients. They can demo a text generator, but they can't demo a *workflow* that solves a real operational problem.

**Severity:** High — this is the gap your package fills directly.

_Sources: [Umbraco Marketplace AI category](https://marketplace.umbraco.com/category/artificial-intelligence), [Phases.io — Exploring AI in Umbraco](https://www.phases.io/insights/exploring-ai-in-umbraco-ideas-and-practical-implementations/)_

### Pain Point 2: LLM Cost Opacity

When Phil Whittaker wrote about choosing LLMs for the Umbraco Developer MCP, he highlighted that most teams had *zero visibility* into what AI operations actually cost. Subscription models (Claude Pro, ChatGPT Plus) hide per-operation costs; API usage is tracked at the provider level but not at the Umbraco level.

For enterprise clients, this is a blocker — they can't approve AI workflows they can't budget for.

**Who feels this:** CTOs, finance teams, enterprise clients in regulated industries.

**Severity:** Medium-High — not a blocker for trials, but a blocker for production rollout at scale.

_Sources: [Phil Whittaker — LLM Cost Analysis for Umbraco MCP](https://dev.to/phil-whittaker/choosing-the-right-llm-for-the-umbraco-cms-developer-mcp-an-initial-cost-and-performance-analysis-50g6), [Umbraco Sustainability Agent Profile](https://umbraco.com/blog/introducing-the-umbraco-sustainability-agent-profile/)_

### Pain Point 3: "How Optional Is AI, Really?"

A forum thread titled "Umbraco and its AI integration, how optional will it really be?" captured real community anxiety. Developers worried about:
- AI adding weight to the backoffice, slowing down already-sluggish performance
- Hosting costs increasing if AI features require more server resources
- Being pressured into AI features they don't need or want

This is a trust issue, not a technical issue. HQ's messaging ("AI, intentionally" — layered, modular, optional) addresses it directly, but the anxiety persists.

**Who feels this:** Developers running lean Umbraco sites, agencies with cost-sensitive clients, sceptics.

**Severity:** Medium — your package is *opt-in by design* (install the NuGet or don't), which is actually a strength. But you'll need to message this clearly.

_Sources: [Forum — How optional will AI be?](https://forum.umbraco.com/t/umbraco-and-its-ai-integration-how-optional-will-it-really-be/7605), [Umbraco — AI, intentionally](https://umbraco.com/blog/ai-intentionally/)_

### Pain Point 4: Skill Gap — Developers Don't Know How to Build AI Workflows

The broader industry data is stark: insufficient worker skills are the #1 barrier to AI adoption in enterprise content operations. Only ~10% of organisations have successfully scaled AI agents in any function. The Umbraco community is technically strong but AI workflow design is a new discipline.

**Who feels this:** Mid-level developers, agencies without dedicated AI expertise, content teams who want AI but don't know where to start.

**Severity:** High for adoption — this is where *workflow templates* and *documentation* become the product, not just the engine.

_Sources: [Averi — AI Agents Transforming Content Ops 2026](https://www.averi.ai/how-to/ai-agent-marketing-how-autonomous-ai-is-changing-content-ops-in-2026), [Deloitte — State of AI in the Enterprise 2026](https://www.deloitte.com/us/en/what-we-do/capabilities/applied-artificial-intelligence/content/state-of-ai-in-the-enterprise.html)_

### Pain Point 5: Package Maintenance Across Umbraco Majors

The community's perennial concern: will this package survive the next major version? Umbraco 13 → 17 was particularly painful for backoffice extensions (the Bellissima rewrite). Developers evaluate packages not just on features but on perceived maintenance commitment.

**Who feels this:** Everyone who's been burned by an abandoned package. Solution architects making long-term bets.

**Severity:** Medium — this isn't unique to your package, but your credibility will depend on shipping updates for Umbraco majors promptly.

_Sources: [Umbraco — Maintaining Packages docs](https://docs.umbraco.com/umbraco-cms/extending/packages/maintaining-packages), [DEV Community — Upgrade journey 13 to 17](https://www.debasish.tech/blogs/from-umbraco-13-to-17-my-upgrade-journey-and-lessons-learned)_

### Pain Point 6: Enterprise Deployment Gaps

Self-hosted Umbraco in load-balanced, containerised, or cloud environments can't use disk-based state storage. This is already documented in your V2 considerations. The market confirms it — the industry is moving to containers and cloud-native, and any package that requires local disk access for runtime state is immediately disqualified from these environments.

**Who feels this:** Enterprise IT teams, Umbraco Cloud users at scale, agencies deploying to Azure/AWS.

**Severity:** Blocker for enterprise — which is exactly why the database storage provider is the right V2 lead feature.

_Sources: [Umbraco Compose — cloud-native launch](https://www.enterprisetimes.co.uk/2026/02/05/umbraco-launches-new-data-orchestration-solution-umbraco-compose/), [Fishtank — CMS Strategy Under AI](https://www.getfishtank.com/insights/why-your-cms-strategy-will-break-under-ai)_

### Pain Point Prioritisation

| Pain Point | Severity | Your V1 Addresses? | V2 Opportunity? |
|-----------|----------|-------------------|----------------|
| No workflow orchestration | Critical | **Yes — core value prop** | Expand with templates |
| LLM cost opacity | Medium-High | Partial (logging) | Pro feature: analytics |
| AI optionality anxiety | Medium | Yes (NuGet opt-in) | Messaging |
| Skill gap for AI workflows | High | Partial (YAML examples) | Templates, docs, community |
| Package maintenance trust | Medium | Prove over time | Track record |
| Enterprise deployment gaps | Blocker (enterprise) | No (disk only) | **Database provider** |

---

## Customer Decision Journey

### How Umbraco Developers Evaluate and Adopt Packages

The decision journey for an Umbraco AI package is fundamentally different from a B2C purchase funnel. It's a **developer-to-developer trust chain** that flows through community channels, not marketing funnels.

### Stage 1: Awareness — "This Exists"

**How they find out:**
- Codegarden talks are the #1 launch pad. Phil Whittaker and Matt Wise's "From Clicks to Commands" talk at Codegarden 2025 single-handedly put the MCP Server on the community radar. A talk or demo at Codegarden 2026 (June) would be the highest-impact awareness event for your package.
- Community blog posts on DEV Community, personal sites, and agency blogs. Timotie Lens wrote about building a custom MCP server in Umbraco; Phil Whittaker wrote about LLM cost analysis — these practical posts drive more discovery than marketing content.
- The weekly [UMB.FYI newsletter](https://umb.fyi/2026-03-04) curates community content and regularly features new packages and AI developments.
- Umbraco Marketplace AI category — currently lists ~8 packages. Presence here is table stakes.
- Discord #social and #help-with-umbraco channels — casual mentions spread quickly in this tight community.

**Key insight:** The Umbraco community is small enough (~5,000-10,000 active developers) that personal reputation matters enormously. Your connections at HQ and with community leads are a genuine distribution channel.

_Sources: [Codegarden 2025 highlights](https://www.readingroom.com/news-insights/umbraco-codegarden-2025-new-ai-integrations-content-hubs-umbraco-17), [UMB.FYI newsletter](https://umb.fyi/2026-03-04), [Umbraco Marketplace AI category](https://marketplace.umbraco.com/category/artificial-intelligence)_

### Stage 2: Evaluation — "Should I Try This?"

**What they check (in order):**
1. **Marketplace listing** — version compatibility, download count, "most active installs" metric (Umbraco's telemetry-driven ranking)
2. **GitHub repo** — recent commits, open issues, responsiveness to PRs, code quality at a glance
3. **NuGet page** — download trends, dependency chain, target framework
4. **README / docs** — "can I get this running in 15 minutes?" is the real bar
5. **Author reputation** — do I know this person? Have they spoken at events? Are they in the community teams?

**Decision criteria (ranked by developer priority):**
1. Does it work with my Umbraco version? (Compatibility is binary — yes or no)
2. Is it easy to install and configure? (NuGet install + minimal config = good)
3. Does it do something I can't easily build myself? (Unique value)
4. Is it maintained? (Recent commits, issues responded to)
5. Does it overlap with HQ's roadmap? (Risk of being superseded)
6. What's the cost? (Free to try is strongly preferred; paid needs clear ROI)

**The HQ overlap question is the biggest unique risk factor for your package.** Every Umbraco developer evaluating a community package asks: "Will Umbraco HQ build this into the core and make this package redundant?" The answer needs to be in your messaging from day one.

_Sources: [Umbraco Marketplace metrics](https://umbraco.com/blog/marketplace-update-may-2023), [5 things your Umbraco package source needs](https://dev.to/d_inventor/5-things-your-umbraco-package-source-needs-21dl)_

### Stage 3: Trial — "Let Me Try It on a Side Project"

**Behaviour pattern:**
- Developers almost never install an unfamiliar package on a client project first
- They spin up a test site (often using the Umbraco dotnet template) and install via NuGet
- They want a "hello world" workflow running within 30 minutes
- If setup is painful or docs are incomplete, they abandon — silently, without feedback
- If it works, they tell 1-2 colleagues; if it's impressive, they blog or Discord-post about it

**Critical success factor:** The out-of-box experience with a sample workflow. If your package ships with a "content review" workflow that actually does something useful on a fresh Umbraco install, that's worth more than pages of documentation.

### Stage 4: Adoption — "Let Me Use This on a Real Project"

**What triggers the jump from trial to production:**
- A specific client project where AI workflow automation would add clear value
- Confidence that the package will be maintained through at least the next Umbraco LTS
- Sign-off from a tech lead / solution architect (Segment 3)
- For paid features: a business case showing time saved vs. cost

**Barriers at this stage:**
- Enterprise deployment limitations (disk storage = blocker for scaled environments)
- No cost visibility for LLM usage (can't budget what you can't measure)
- Lack of workflow templates for common scenarios (have to design workflows from scratch)

### Stage 5: Advocacy — "You Should Check This Out"

**What turns users into advocates:**
- The package solved a real problem they couldn't solve easily another way
- The author is responsive to issues and engaged in the community
- They can show a demo to colleagues that gets a "wow" reaction
- They feel like they're part of something early — being a community contributor, not just a consumer

**Advocacy channels in the Umbraco world:**
- Codegarden talks (highest prestige)
- Blog posts on DEV Community or personal sites
- Discord recommendations (highest volume)
- Umbraco meetup presentations (local reach)
- UMB.FYI newsletter mentions (curated reach)

### Decision Influencers

| Influencer Type | Who | How They Influence |
|----------------|-----|-------------------|
| **Community MVPs** | Paul Seal, Callum Whyte, Kevin Jump, Warren Buckley | Blog posts, talks, Discord presence — if they endorse, developers trust |
| **AI Advisory Board** | 9 members inc. Matt Wise, Emma Garland, Andy Eva-Dale | Direct input into HQ's AI direction; their opinions carry institutional weight |
| **AI Community Team** | 23 members total — partners implementing AI with Umbraco | Building tools and sharing patterns; peer influence among agencies |
| **Agency tech leads** | Decision makers at Gold/Platinum partners | Gate client project adoption; need enterprise signals |
| **HQ product team** | Umbraco's own product and engineering teams | Roadmap signals determine whether community packages feel safe or risky |

_Sources: [Umbraco AI Advisory Board](https://umbraco.com/blog/announcing-the-umbraco-in-ai-community-team-the-ai-in-umbraco-advisory-board), [Codegarden 2025](https://www.msqdx.com/en/insights/umbraco-s-codegarden-2025-highlights-learnings-and-what-s-next)_

### Implications for Your Launch Strategy

1. **Get on the Marketplace immediately** — it's the first place developers look
2. **Ship a sample workflow that works on a fresh install** — the 30-minute trial experience is make-or-break
3. **Write a DEV Community post** — practical, "here's what I built and why", not marketing
4. **Get a Codegarden 2026 talk slot** — if the CFP is still open, submit. If not, target a community meetup
5. **Address the HQ overlap question proactively** — "This builds on Umbraco.AI, it's complementary, and here's why it exists alongside HQ's tools"
6. **Engage the Advisory Board / Community Team members directly** — you know some of them already. A personal demo is worth more than a press release
7. **LinkedIn and Discord are your launch channels** — the Umbraco community lives there

---

## Competitive Landscape

### Direct Competitors (Umbraco AI Packages)

None of these are direct competitors to a workflow orchestration engine — they're all point solutions. But they define the landscape your package enters.

| Package | Author | What It Does | Model | Threat Level |
|---------|--------|-------------|-------|-------------|
| **Umbraco.AI** | Umbraco HQ | Foundation layer — IAIChatService, provider abstraction, Microsoft.Extensions.AI integration | Free / open source | **Not a threat — it's your foundation.** You build on this. |
| **Umbraco.AI.Agent** | Umbraco HQ | Agent runtime with tool calling. Uses HttpContext.Items (broken in IHostedService). | Free / open source | **Low — you've already identified its architectural limitations and built around them.** |
| **Perplex AI Assistant** | Perplex Digital | Meta title/description generation from page content. ChatGPT-powered. | Free (BYO OpenAI key) | None — different problem space |
| **Growcreate AI Toolkit** | Growcreate | Content creation, metadata, accessibility tools. Azure OpenAI / Foundry integration. | Tiered: Community (free), Professional, Enterprise | Low — point solution, not orchestration |
| **umContentCreator** | Kirill (community) | Text generation button per content property. ChatGPT API. | Free / MIT | None — single-feature package |
| **MetaMate AI** | Community | Metadata-focused AI assistance | Unknown | None — niche |
| **pTools Umbraco AI** | pTools | AI-driven content assist, WAI/GDPR reporting, engagement analytics | Part of pTools platform | None — different ecosystem (pTools CMS V8) |
| **Umbraco.Community.AI.LogAnalyser** | Community | AI-powered log analysis in backoffice log viewer | Free | None — complementary, different domain |
| **AI Essentials Toolkit** | Cyber-Solutions | Content generation, text optimisation, editorial workflow support | Free with Cyber-Solutions projects | Low — agency-specific, not distributed |

_Sources: [Umbraco Marketplace AI category](https://marketplace.umbraco.com/category/artificial-intelligence), [Growcreate AI Toolkit](https://marketplace.umbraco.com/package/growcreate.aitoolkit), [Perplex AI Assistant](https://marketplace.umbraco.com/package/perplex.ai.assistant), [umContentCreator GitHub](https://github.com/Kirill19837/umContentCreator), [AI LogAnalyser](https://forum.umbraco.com/t/new-package-umbraco-community-ai-loganalyser/7727)_

### Strategic Competitor: Umbraco HQ

This deserves its own section because HQ is simultaneously your platform provider, your foundation, and your most significant competitive threat.

**What HQ is building:**

| Initiative | Status | Relevance |
|-----------|--------|-----------|
| **Umbraco.AI** | Shipped (open source) | Your foundation. You depend on it. |
| **Umbraco MCP Server** | Released Q3 2025, docs MCP + content types MCP live | External AI tool connectivity — complementary, not competing |
| **Agent Skills** | In development, Q2 2026 target | Structured patterns for AI agent behaviour. Could overlap with workflow definitions. |
| **Umbraco Compose** | Launched Feb 2026 (commercial SaaS) | Data orchestration platform with AI features (Prompts, Agents/co-pilot in preview). Cloud-only. |
| **Unknown agent runner** | Confirmed in development (Adam's insider intel) | **Direct competitor. Unknown scope, unknown timeline.** |

**HQ's strategic pattern:**
- Open-source the foundation (Umbraco.AI, MCP)
- Build commercial cloud products on top (Compose)
- Invite community to build on the open foundation
- Gradually expand HQ's own feature set

**Your positioning relative to HQ:**
- You build ON Umbraco.AI — complementary, not forked
- You're self-hosted — HQ's commercial play is cloud (Compose)
- You ship now — HQ's agent capabilities target Q2 2026+ and their track record is shipping later than announced
- You're practitioner-built — you solve problems you've actually encountered, not product roadmap items
- **Risk:** If HQ ships a self-hosted agent runner as part of the free Umbraco.AI package, your free tier loses differentiation. Your pro tier (database storage, MCP, analytics) would still have value.

_Sources: [Umbraco Q1 2026 Product Update](https://umbraco.com/blog/umbraco-product-update-q1-2026/), [Umbraco Product Roadmap](https://umbraco.com/products/knowledge-center/roadmap/), [Umbraco Compose launch](https://www.cmswire.com/the-wire/umbraco-launches-compose-to-maximize-value-delivered-by-composable-digital-platforms/), [Umbraco.AI GitHub](https://github.com/umbraco/Umbraco.AI)_

### Competitor CMS Approaches (Ideas to Steal)

#### Optimizely Opal

**What they do:** Agent orchestration platform with a library of specialised AI agents, custom agent building, and drag-and-drop workflow orchestration. ~900 companies adopted since May 2025 launch.

**Commercial model:** Credit-based — Opal credits consumed per LLM API call, pooled across Optimizely products. Enterprise pricing (starts ~$100-500/mo, custom for large orgs).

**What to steal:**
- **Specialised agent library** — pre-built agents for specific tasks (content modelling, SEO audit, translation). Your equivalent: pre-built workflow YAML templates.
- **Credit-based cost visibility** — users see exactly what AI operations cost. Validates your V2 cost tracking feature.
- **Drag-and-drop workflow builder** — they have a visual builder for non-developers. You don't need this for V1 (YAML is fine for developers), but for V2/V3 a visual workflow editor would lower the bar for content teams.

**What NOT to copy:**
- Their pricing model is enterprise SaaS — wrong for the Umbraco community (too expensive, too opaque)
- Their cloud-only approach excludes self-hosted — your self-hosted position is a differentiator

_Sources: [Optimizely Opal](https://www.optimizely.com/ai/), [Opal agent orchestration](https://www.cmswire.com/digital-experience/optimizely-enhances-opal-with-ai-agent-orchestration-tools/), [Opal pricing](https://www.selecthub.com/p/ai-marketing-agent-tools/optimizely-opal/)_

#### Kentico AIRA & KentiCopilot

**What they do:** Two-pronged approach — AIRA for marketers (agentic marketing suite with Content Strategist, Customer Journey Optimisation agents) and KentiCopilot for developers (MCP servers, code migration assistance, AI-assisted upgrades).

**What to steal:**
- **Content Strategist agent** — audits structure, taxonomy, and brand compliance. Brilliant use case for a workflow template in your package.
- **KentiCopilot for upgrades** — using AI to help with CMS version migrations. A workflow template that helps audit content models during Umbraco upgrades would be extremely valuable.
- **MCP servers for specific tasks** — Kentico ships a Documentation MCP Server (Kentico knowledge) and a Content Types MCP Server (content model operations). Targeted, single-purpose MCP servers are more useful than one giant one.

**What NOT to copy:**
- AIRA is baked into the platform — not a separate package. Umbraco's modular approach (packages on top of core) is healthier for the ecosystem.
- Their SaaS-only model. Self-hosted is your advantage.

_Sources: [Kentico AIRA](https://www.kentico.com/platform/aira), [Kentico AI Vision 2026](https://www.mcbeev.com/kentico-ai-vision-2026), [AIRA Agentic Marketing Suite](https://www.kentico.com/platform/agentic-marketing-suite)_

### Adjacent Competitors (AI Frameworks)

| Framework | Relevance | Threat Level |
|-----------|-----------|-------------|
| **Microsoft Agent Framework** (Semantic Kernel + AutoGen) | The .NET standard for AI agent orchestration. 27K+ GitHub stars. GA targeted Q1 2026. | **Medium** — not CMS-specific, but a technically sophisticated developer could build their own workflow engine on top of it instead of using your package. Your value is that you've already done the CMS integration work. |
| **CrewAI** | Python-based multi-agent framework. Not .NET. | **None** — wrong ecosystem |
| **LangChain / LangGraph** | Python-first, some .NET support. General-purpose agent framework. | **Low** — possible for polyglot teams but not natural in the Umbraco world |

**Key insight:** Microsoft Agent Framework (Semantic Kernel) is the only real adjacent threat because it's .NET-native. But it requires significant custom work to integrate with Umbraco — you'd need to build the storage layer, the backoffice UI, the SSE streaming, the workflow definition model, and all the CMS-specific tooling yourself. That's exactly what your package provides out of the box.

_Sources: [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/), [Semantic Kernel overview](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/)_

### Competitive Positioning Map

```
                    Self-Hosted ←————————————→ Cloud-Only
                         |                        |
    Point Solution       |  umContentCreator      |
         ↑               |  Perplex AI Assistant  |
         |               |  Growcreate AI Toolkit  |
         |               |                        |
         |               |                        |  Umbraco Compose
         |               |                        |  (data orchestration + AI preview)
         |               |                        |
         |     ★ YOUR PACKAGE                     |  Optimizely Opal
         |     (workflow orchestration,            |  (agent orchestration,
         |      self-hosted, YAML-defined)         |   credit-based, enterprise)
         |               |                        |
         ↓               |                        |  Kentico AIRA
    Workflow Engine       |                        |  (agentic marketing suite,
                         |                        |   platform-integrated)
```

**Your unique position:** You're the only self-hosted workflow orchestration engine in the Umbraco ecosystem. Everything else is either a point solution (left side) or a cloud-only platform play (right side).

### Commercial Model Benchmarks

| Package/Product | Model | Price Point | Notes |
|----------------|-------|------------|-------|
| **uSync.Complete** | Per-project licence (perpetual) or annual subscription | £1,045/project (domain), £2,750 (agency/unlimited) | Proven model in Umbraco ecosystem. Free 60-day trial. Licence covers all versions up to Umbraco 20. |
| **Optimizely Opal** | Credit-based SaaS | ~$100-500+/mo (enterprise custom) | Too expensive for Umbraco market |
| **Kentico AIRA** | Platform-bundled | Included in Xperience licence | Not separable |
| **Growcreate AI Toolkit** | Tiered (Community free, Professional, Enterprise) | Unknown | Agency-bundled model |
| **Umbraco Cloud** | Monthly subscription | $55-900/mo | Platform hosting, not package |

**uSync.Complete is your pricing template.** Kevin Jump's model is the most directly comparable:
- Free core (uSync) on NuGet.org, genuinely useful
- Paid complete package (uSync.Complete) with power features
- Per-project perpetual licence at ~£1,000
- Free 60-day trial, no forms
- Agency licence option for unlimited projects

Your V2 future-considerations doc estimated £499-£999 — this aligns perfectly with the uSync benchmark. The lower end (£499) for a domain licence, higher end for agency, feels right for a newer package building its reputation.

_Sources: [uSync.Complete pricing](https://jumoo.co.uk/usync/buy/), [uSync.Complete Marketplace](https://marketplace.umbraco.com/package/usync.complete), [Umbraco Cloud pricing](https://umbraco.com/products/umbraco-cloud/pricing/)_

### Competitive Threats Summary

| Threat | Severity | Mitigation |
|--------|----------|-----------|
| HQ ships a free self-hosted agent runner | **High** | Ship first, build community adoption, differentiate on pro features |
| HQ's Agent Skills overlap with your workflow model | **Medium** | Position as complementary — Agent Skills guide AI behaviour, your package orchestrates multi-step workflows |
| Microsoft Agent Framework makes DIY easy | **Low-Medium** | Your value is CMS integration out of the box, not raw AI orchestration |
| Community apathy ("I'll wait for HQ's version") | **Medium** | Demonstrate real value with compelling sample workflows; make the 30-min trial irresistible |
| Package maintenance burden across Umbraco majors | **Medium** | Plan for this in V2; building on stable interfaces (Umbraco.AI, Microsoft.Extensions.AI) reduces surface area |

### Opportunities

| Opportunity | Why It Matters | How to Capture |
|------------|----------------|----------------|
| **First self-hosted workflow engine** | Nobody else is doing this in the Umbraco ecosystem | Ship V1, claim the space |
| **Workflow templates as adoption driver** | Developers don't know how to design AI workflows | Ship 3-5 real-world templates (content review, SEO audit, translation pipeline) |
| **Community credibility** | Umbraco community trusts practitioners over corporations | Engage Advisory Board, write blog posts, speak at events |
| **Enterprise readiness (V2 Pro)** | Database storage + cost tracking unlocks enterprise market | V2 Pro package at uSync-level pricing |
| **Complementary to HQ's MCP strategy** | HQ is pushing MCP for external AI tool connectivity; you provide internal workflow orchestration | Position as the "run AI workflows inside Umbraco" counterpart to "connect AI tools to Umbraco via MCP" |

---

## Strategic Synthesis and Recommendations

### Market Position Statement

You are building the only self-hosted AI workflow orchestration engine for Umbraco CMS. Your package sits in white space — between the point solutions (Perplex, Growcreate, umContentCreator) that do single tasks and the cloud platforms (Optimizely Opal, Kentico AIRA, Umbraco Compose) that require enterprise SaaS commitments. You build on Umbraco.AI's foundation rather than competing with it, making you complementary to HQ's strategy while filling a gap they haven't yet filled.

### Strategic Recommendations

#### 1. Ship V1 and Claim the Space (Immediate — April 2026)

**Why now:** HQ's agent runner is in development but unshipped. The Codegarden 2026 CFP is open. The AI Advisory Board and Community Team are active and engaged. The window to establish yourself as the community's AI workflow package is open but time-limited.

**Actions:**
- Publish to NuGet.org and list on Umbraco Marketplace
- Ship with 2-3 sample workflow YAML files that work on a fresh Umbraco install (content review, SEO audit are high-value candidates)
- Write a practical DEV Community post: "Building AI Workflows in Umbraco — Here's How"
- Submit to Codegarden 2026 CFP (June 10-11, Copenhagen) — [CFP is live](https://sessionize.com/codegarden-2026/)
- Reach out to Advisory Board contacts for personal demos

#### 2. Message the HQ Relationship Proactively (Immediate)

**The one question every developer will ask:** "Won't Umbraco build this natively?"

**Your answer should be:**
- "This builds ON Umbraco.AI — it's a package that extends the official foundation, not a fork or competitor"
- "HQ's MCP strategy connects external AI tools to Umbraco. This package runs AI workflows *inside* Umbraco. They're complementary."
- "The free tier is genuinely useful. The pro tier adds enterprise capabilities (database storage, cost tracking, MCP) that make sense as a community package, not a core feature."

Avoid being defensive. Frame it as ecosystem collaboration.

#### 3. Workflow Templates Are the Product (V1 and Ongoing)

**The skill gap is the biggest adoption barrier** — developers don't know how to design AI workflows. Your engine is powerful, but an engine without templates is like a CMS without starter kits.

**Priority templates to ship or develop quickly:**
- **Content Review**: Audit published pages for quality, tone, and completeness
- **SEO Metadata Generator**: Bulk-generate meta titles and descriptions (directly competes with Perplex AI Assistant, but as part of a workflow rather than one-at-a-time)
- **Content Translation Pipeline**: Multi-step workflow for translating and reviewing content
- **Content Model Auditor**: Inspired by Kentico's Content Strategist — review content types for best practices (great for upgrade scenarios)

#### 4. Pricing Strategy (V2 Pro — When Ready)

Follow the uSync model exactly:
- **Free package** on NuGet.org: unlimited workflows, disk storage, built-in tools, full backoffice UI
- **Pro package** on licensed feed: database storage, MCP support, HTTP API tool, analytics, cost tracking
- **Per-project perpetual licence**: £499 (domain) / £999 (agency)
- **Free 60-day trial**, no forms — just download
- Consider an annual support/update subscription as an add-on, not a replacement for perpetual

#### 5. Build Community Trust Through Engagement (Ongoing)

**The Umbraco community rewards practitioners, not marketers.**

- Be active on Discord — help people, don't just promote
- Write practical blog posts about what you learned building the package, not marketing content
- Engage with the AI Community Team (you're already connected to this world)
- Respond to GitHub issues quickly — responsiveness is the #1 trust signal for package authors
- Target UMB.FYI newsletter coverage — it's the most-read curated Umbraco content

#### 6. Plan V2 Pro Features Based on Market Signals (Q3-Q4 2026)

The research validates your V2 priorities:

| Feature | Market Signal | Priority |
|---------|--------------|----------|
| Database storage provider | Enterprise deployment blocker — every containerised/load-balanced site needs this | **#1** |
| LLM cost tracking | Confirmed gap — Phil Whittaker's analysis + Deloitte enterprise survey | **#2** |
| MCP client support | Industry converging on MCP; HQ and Kentico both investing heavily | **#3** |
| Generic HTTP/API tool | Covers the "talk to our CRM/PIM/DAM" use case agencies need | **#4** |
| Workflow analytics | Operational visibility for production deployments | **#5** |

### Naming Considerations

The research surfaced naming patterns in the ecosystem worth considering:

**Ecosystem naming conventions:**
- HQ products: Umbraco + descriptor (Umbraco.AI, Umbraco Compose, Umbraco Engage)
- Community packages: descriptive name + Umbraco context (uSync, Contentment, ContentBlocks)
- AI packages: function-forward names (AI Assistant, AI Toolkit, ContentCreator)

**"Shallai" doesn't communicate value.** The name needs to signal:
1. What it does (AI workflows / agent orchestration)
2. Where it runs (Umbraco)
3. That it's a community/practitioner tool (not corporate)

**Name ideas to explore** (these are starting points, not final — brand naming deserves its own session):

- **Umbraco.Workflows.AI** — follows HQ naming convention, immediately clear what it does. Risk: might look like an official HQ product.
- **AgentFlow** — clean, modern, describes what it does. Risk: generic, may conflict with other products.
- **Umbraco Agent Runner** — literal and clear. Risk: dry, not memorable.
- **Conductor** — orchestration metaphor, memorable. "Conductor for Umbraco" or "Umbraco Conductor"
- **RunBook** — operations term for automated procedures. "RunBook AI" or "Umbraco RunBook"
- **Weave** — threads together AI steps into workflows. "Weave for Umbraco"

**Recommendation:** The name should be discoverable via search ("Umbraco AI workflow") and self-explanatory in a Marketplace listing. Clever/abstract names work for established brands; new packages need clarity. A dedicated naming session with a shortlist and community feedback would be the right next step.

### Risk Matrix

| Risk | Likelihood | Impact | Mitigation | Owner |
|------|-----------|--------|-----------|-------|
| HQ ships free agent runner | Medium-High | High (free tier differentiation loss) | Ship first, build adoption, pro features retain value | Adam — speed of V1 launch |
| Community apathy / "I'll wait" | Medium | Medium (slow adoption) | Compelling sample workflows, personal outreach | Adam — content + network |
| Package maintenance burden | Medium | Medium (trust erosion) | Build on stable interfaces (Umbraco.AI, M.E.AI), plan for Umbraco majors | Adam — architecture decisions |
| LLM cost concerns deter adoption | Low-Medium | Low (affects enterprise, not early adopters) | V2 cost tracking, document expected costs in README | V2 feature |
| Naming confusion with HQ products | Low | Low-Medium | Clear naming + "community package" positioning | Naming session |

---

## Research Methodology

**Data sources:** Umbraco official blog, product roadmap, and documentation; Umbraco Marketplace; Umbraco community forum and Discord archives; NuGet package data; DEV Community blog posts; CMS Critic; CMSWire; Optimizely and Kentico official sources; Microsoft Learn; Deloitte enterprise AI survey.

**Verification approach:** All market claims cross-referenced with at least one primary source. Confidence levels noted where data is uncertain or based on limited sources. Insider intelligence (HQ agent runner development) noted as unverifiable but from a trusted source.

**Limitations:**
- Umbraco community is small — limited public data on adoption metrics for individual packages
- Discord conversations are not publicly searchable — some community sentiment may be missing
- HQ's unreleased agent runner scope and timeline are unknown — risk assessment is based on pattern analysis, not confirmed information
- Pricing data for competitor CMS products is often enterprise-custom and not publicly disclosed

---

**Research completed:** 2026-04-01
**Research type:** Lightweight market analysis — Umbraco AI tooling landscape
**Confidence level:** High for ecosystem mapping, medium for competitive timing predictions
**Recommended next actions:** Ship V1, submit Codegarden CFP, run naming session
