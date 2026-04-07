# Post-V1 Handoff Brief

**Date:** 2026-04-02
**Author:** Adam (via market research + naming session with Mary/Analyst)
**For:** Architect (Winston) + SM (Bob)
**Status:** Ready for architect review

---

## Context

Market research was conducted on 2026-04-01 to map the Umbraco AI tooling landscape, identify positioning opportunities, and inform V2 planning. A naming session followed. This brief captures the decisions and actions that affect the immediate development plan.

**Full research:** `_bmad-output/planning-artifacts/research/market-umbraco-ai-tooling-research-2026-04-01.md`
**Naming session:** `_bmad-output/brainstorming/brainstorming-session-2026-04-01-naming.md`
**V2 considerations (existing):** `_bmad-output/planning-artifacts/v2-future-considerations.md`

---

## Decision 1: Rename from Shallai to AgentRun

### Rationale

"Shallai" was a placeholder that doesn't communicate value. Market research confirmed that discoverability and self-description are critical for Umbraco Marketplace adoption. "AgentRun" was selected after conflict-checking against NuGet, active products, and trademarks.

- "Agent" signals AI, "Run" signals execution — zero ambiguity
- No NuGet package conflict
- No active product/trademark conflict
- Doesn't sound like an Umbraco HQ product (important — avoids confusion with Umbraco Compose, Engage, etc.)
- Community/practitioner feel

### Rename Scope (Architect to Validate)

This is the analyst's best understanding of the rename surface area. The architect should review, correct, and expand before the SM creates a story.

| What | From | To | Notes |
|------|------|-----|-------|
| Solution file | `Shallai.UmbracoAgentRunner.slnx` | `AgentRun.Umbraco.slnx` | |
| Main project folder + .csproj | `Shallai.UmbracoAgentRunner/` | `AgentRun.Umbraco/` | |
| Test project folder + .csproj | `Shallai.UmbracoAgentRunner.Tests/` | `AgentRun.Umbraco.Tests/` | |
| TestSite folder + .csproj | `Shallai.UmbracoAgentRunner.TestSite/` | `AgentRun.Umbraco.TestSite/` | |
| All C# namespaces | `Shallai.UmbracoAgentRunner.*` | `AgentRun.Umbraco.*` | Global find-replace |
| Custom element prefix | `shallai-` | `agentrun-` | HTML tags, element registrations |
| Element class names | `Shallai{Name}Element` | `AgentRun{Name}Element` | TypeScript classes |
| App_Plugins output path | `ShallaiUmbracoAgentRunner/` | `AgentRunUmbraco/` | vite.config.ts, umbraco-package.json |
| umbraco-package.json bundle ID | `Shallai.UmbracoAgentRunner` | `AgentRun.Umbraco` | |
| API route prefix | `/umbraco/api/shallai/` | `/umbraco/api/agentrun/` | All endpoint definitions + frontend API client |
| Config section (appsettings) | `Shallai:AgentRunner` | `AgentRun:Umbraco` | AgentRunnerOptions binding + TestSite appsettings |
| App_Data runtime path | `Shallai.UmbracoAgentRunner/` | `AgentRun.Umbraco/` | Default data root for instance storage |
| Composer class name | `AgentRunnerComposer` | Review — keep or rename? | Functional, architect's call |
| Structured logging fields | `WorkflowAlias`, `InstanceId`, `StepId`, `ToolName` | No change | These are domain terms, not brand |
| Test assertions | Any string literals referencing old names | Update | grep for "Shallai", "shallai", "ShallaiUmbraco" |
| Manifest section alias | `Shallai.UmbracoAgentRunner.Section` | `AgentRun.Umbraco.Section` | + user group permissions need re-granting |
| Workflow YAML files (TestSite) | Check for any hardcoded references | Update if found | |
| BMAD/planning docs | References to old name | Leave as-is (historical) | Don't rewrite planning artifacts |

### Architect Questions

- [ ] Is the TestSite `App_Data/Shallai.UmbracoAgentRunner/` folder referenced anywhere beyond the config default? Any migration concern for existing test instances?
- [ ] Should the Composer class rename from `AgentRunnerComposer` to `AgentRunComposer`? Or leave as-is since it's internal?
- [ ] `AgentRunnerOptions` — rename to `AgentRunOptions`? Or keep the "Runner" suffix for the options class?
- [ ] Any cross-project references in .csproj files that need updating beyond ProjectReference paths?
- [ ] Does the rename affect the `project-context.md` or should that be regenerated after the rename?
- [ ] The `Umb.Condition.SectionUserPermission` match property will change — does this need a migration or just documentation?

### Execution Plan

1. **Architect validates scope** — reviews table above, answers open questions, identifies anything missed
2. **SM creates story** — full spec with failure cases, "What NOT to Build" section, acceptance criteria
3. **Dev executes rename** — one commit, zero logic changes
4. **Rebuild + test** — `dotnet test AgentRun.Umbraco.slnx`, `npm test`, `npm run build`, manual E2E verify
5. **Epic 9 proceeds with AgentRun naming** — docs, NuGet packaging, Marketplace listing all use final name

---

## Decision 2: Post-V1 Sequencing

Based on market research findings. The architect should validate feasibility and effort estimates.

### Recommended Epic Sequence

**Pre-Epic 9: Rename to AgentRun** (story described above)

**Epic 9: Example Workflow, Documentation & Packaging** (already planned, no changes)
- 9.1: Content Quality Audit example workflow
- 9.2: JSON Schema for workflow.yaml
- 9.3: Documentation + NuGet packaging
- Ship V1 to NuGet.org + Umbraco Marketplace

**Epic 10: Ship Readiness & Stability** (new — fast follower)
- Instance concurrency locking (SemaphoreSlim per-instance — known gap from deferred work)
- Context management for long conversations (token waste on multi-step workflows)
- Multi-turn interactive fix (ToolLoop exits when LLM asks question)
- Open source licence decision (MIT vs Apache 2.0)
- Marketplace listing and community launch (Discord, LinkedIn, DEV Community post)

**Epic 11: Adoption Accelerators** (new)
- 2-3 additional workflow templates (SEO metadata generator, content translation pipeline, content model auditor — ideas from market research)
- Tree navigation UX overhaul (workflows in section tree, follows Umbraco native pattern)
- Basic token usage logging (not a dashboard — just structured log events per-step)

**Epic 12: Pro Foundation** (new — begins the commercial split)
- Storage provider abstraction (`IInstanceStorageProvider` interface, refactor disk implementation behind it)
- Database storage provider (`DatabaseInstanceStorageProvider` using Umbraco's DB connection)
- Package split: `AgentRun.Umbraco` (free) + `AgentRun.Umbraco.Pro` (paid)

### Why This Sequence

- **Rename first** — can't ship docs and Marketplace listing with a placeholder name
- **Epic 10 before community launch** — the concurrency and context bugs will be hit by real users immediately
- **Epic 11 drives adoption** — market research found that the skill gap (developers don't know how to design AI workflows) is the #1 adoption barrier. Templates solve this. Tree nav makes it feel native.
- **Epic 12 unlocks revenue** — storage abstraction is the architectural prerequisite for the Pro package. Database provider is the #1 enterprise blocker.

---

## Key Market Research Findings (Summary for Architect)

Full details in the research document. Highlights that affect architecture decisions:

1. **No direct competitors** — every Umbraco AI package is a point solution. We're the only workflow orchestration engine. Ship fast.

2. **HQ is building something similar** — confirmed via insider contact. Unknown scope and timeline. Our first-mover advantage has a countdown. The Pro feature set (database storage, MCP, analytics) is our differentiation moat.

3. **uSync commercial model is the template** — free package on NuGet.org, paid Pro package on licensed feed. Per-project perpetual licence at £499-999. No DRM, no feature flags. Install the NuGet or don't.

4. **Codegarden 2026 CFP is open** — June 10-11, Copenhagen. Highest-impact launch event. Consider submitting a talk.

5. **Sample workflows are the product** — the 30-minute trial experience (install, run example, see results) is make-or-break for adoption.

---

## What the Architect Should Do

1. **Read this brief** (you're doing it)
2. **Review the rename scope table** — validate, correct, expand
3. **Answer the architect questions** above
4. **Review the post-V1 epic sequence** — flag any architectural concerns with the ordering
5. **Check V2 future considerations doc** — the storage provider abstraction design is already there, confirm it still aligns with what we've built in V1
6. **Hand off to SM** — who creates the rename story and updates the sprint plan

---

_This brief is a point-in-time snapshot. The market research and V2 considerations documents are the living references._
