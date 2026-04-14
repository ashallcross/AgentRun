# Story 9.5: Private Beta Distribution Plan

Status: done

**Depends on:** All Epic 9 stories (9.6, 9.0, 9.1a, 9.1b, 9.1c, 9.2, 9.3, 9.4, 9.7, 9.9, 9.10, 9.11, 9.12, 9.13, 9.14) — all confirmed done
**Blocks:** Epic 10 (public launch path)

> This is the final Epic 9 story. It is a **process story**, not a code story. There are no C# changes, no TypeScript changes, and no test changes. The deliverables are: a verified NuGet package on NuGet.org, a beta invitation with structured instructions, and a feedback tracking location. Adam executes this story directly — it is not delegated to a dev agent for autonomous implementation.

## Story

As a product owner,
I want the beta release packaged, published, and distributed to a curated invite list,
So that real users exercise the package before public launch.

## Acceptance Criteria

**AC1 — NuGet package built and verified**
Given Stories 9.6 through 9.14 are all complete,
When the package is built with `dotnet pack`,
Then the `.nupkg` is version `1.0.0-beta.1`,
And the package contains all three example workflows (content-quality-audit, accessibility-quick-scan, umbraco-content-audit),
And the package contains the workflow JSON Schema,
And the package contains the README as the NuGet gallery readme,
And the fresh-install smoke test (docs/beta-test-plan.md) passes on a clean Umbraco 17 site.

**AC2 — Published to NuGet.org as pre-release**
Given the verified `.nupkg` from AC1,
When pushed to NuGet.org,
Then it is listed as a pre-release package (the `-beta.1` suffix ensures this),
And it does NOT appear in default stable-package search results,
And it is NOT listed on the Umbraco Marketplace.

**AC3 — Beta invite sent to curated list**
Given the beta invite list is finalised,
When invitations are sent,
Then each invitee receives:
- Installation instructions (README quick start — Steps 1-4)
- A link to the workflow authoring guide (docs/workflow-authoring-guide.md)
- A recommendation to start with the **Umbraco Content Audit** workflow first (reads their own content — no URLs needed, immediate value demonstration)
- The known-issues list (see below)
- A request for structured feedback (what worked, what didn't, what's confusing)
- Explicit language that this is a private beta not for re-sharing

**AC4 — Invite list includes target audiences**
Given the invite list is being composed,
Then it includes:
- Cogworks internal engineering contacts
- 3–5 Umbraco community contacts (people Adam knows who run Umbraco sites)
- Selected Umbraco Discord members (coordinated with Discord moderators as appropriate)

**AC5 — Feedback tracking established**
Given a beta user reports an issue or gives feedback,
When the feedback is triaged,
Then it is captured in a dedicated tracking location with classification: bug, enhancement, or question,
And the triage decides whether it blocks the public 1.0 launch or is deferred.

**AC6 — Beta period defined**
Given the beta is distributed,
Then a defined beta period is agreed (Adam to decide — recommended 2–4 weeks),
And at the end of the period a beta retrospective is held,
And a decision is made about readiness for public launch (Epic 10).

## Known Issues to Ship With Beta

Include in the beta invitation. These are documented limitations, not bugs:

1. **Content audit on large sites (100+ nodes) may stall.** The scanner accumulates content in conversation context. Workaround: use content type or subtree filters to audit in sections. Fix planned for Story 10.2.
2. **Cancel is cosmetic.** Clicking Cancel updates the status label but does not stop the in-flight LLM call or tool execution. The run may continue after cancellation. Fix planned for Epic 10.
3. **SSE disconnect marks runs as Failed.** If the browser tab is closed or the network drops during a running step, the run is marked Failed even though work may have partially completed. Fix planned for Epic 10.
4. **Instance resume after step completion does not work.** If you navigate away and return after a step completes, the chat input rejects all messages. Workaround: stay on the instance page until the workflow completes. Fix planned for Epic 10.
5. **Single concurrent instance per workflow.** No instance-level locking — running two instances simultaneously may corrupt state. Fix planned for Story 10.1.

## What NOT to Build

- Do NOT list on Umbraco Marketplace during the beta
- Do NOT announce publicly (LinkedIn, DEV Community, Twitter/X, etc.) — that's the public launch (Epic 10.5)
- Do NOT build telemetry or usage analytics — feedback is manual for the beta
- Do NOT include pricing, licensing discussions, or Pro-tier messaging — free open-source beta
- Do NOT make any C# or TypeScript code changes in this story

## Tasks / Subtasks

- [ ] Task 1: Rebuild and verify the NuGet package (AC: #1)
  - [ ] 1.1 Run `npm run build` in `Client/` to ensure frontend output is current
  - [ ] 1.2 Run `dotnet pack AgentRun.Umbraco/AgentRun.Umbraco.csproj -c Release -o nupkg` from repo root
  - [ ] 1.3 Verify the `.nupkg` version is `1.0.0-beta.1`
  - [ ] 1.4 Inspect the nupkg contents: confirm `contentFiles/` contains all three workflow folders + JSON schema, and root contains `README.md`
  - [ ] 1.5 Run the fresh-install smoke test per `docs/beta-test-plan.md` on a clean Umbraco 17 site (can reuse the TestInstall site from Story 9.3 — wipe `App_Data/` and `bin/` first)
  - [ ] 1.6 Verify all three workflows appear in the backoffice after install
  - [ ] 1.7 Run the Umbraco Content Audit end-to-end and confirm it produces an audit report referencing real content

- [ ] Task 2: Publish to NuGet.org (AC: #2)
  - [ ] 2.1 Push to NuGet.org: `dotnet nuget push nupkg/AgentRun.Umbraco.1.0.0-beta.1.nupkg --api-key {key} --source https://api.nuget.org/v3/index.json`
  - [ ] 2.2 Verify the package appears on nuget.org as a pre-release listing
  - [ ] 2.3 Verify searching "AgentRun" without the pre-release checkbox does NOT surface the package
  - [ ] 2.4 Verify the NuGet gallery renders the README correctly

- [ ] Task 3: Compose and send beta invitations (AC: #3, #4)
  - [ ] 3.1 Finalise the invite list (Cogworks internal + community contacts + Discord members)
  - [ ] 3.2 Draft the invitation message including: install instructions, authoring guide link, "start with Content Audit" recommendation, known-issues list, feedback request, private-beta confidentiality note
  - [ ] 3.3 Send invitations via appropriate channel (email, Discord DM, or Slack — Adam's discretion)

- [ ] Task 4: Set up feedback tracking (AC: #5)
  - [ ] 4.1 Create a feedback tracking location (GitHub Issues on the repo, a dedicated file, or a simple spreadsheet — Adam's discretion based on whether the repo is public yet)
  - [ ] 4.2 Define triage categories: bug, enhancement, question
  - [ ] 4.3 Define escalation criteria: does it block 1.0 launch or is it deferred to Epic 10+?

- [ ] Task 5: Define beta period and retrospective (AC: #6)
  - [ ] 5.1 Decide beta duration (recommended: 2–4 weeks)
  - [ ] 5.2 Set a calendar date for the beta retrospective
  - [ ] 5.3 Document the beta period and retro date (update sprint-status.yaml comment or a dedicated note)

## Dev Notes

### This is a human-executed story

Unlike all previous Epic 9 stories, this story is executed by Adam directly. The tasks are packaging, publishing, composing invitations, and setting up processes — not code implementation. A dev agent can assist with the `dotnet pack` and `dotnet nuget push` commands, but the invite composition, list finalisation, and feedback tracking setup require human judgment.

### Package is already built

The `.nupkg` already exists at `nupkg/AgentRun.Umbraco.1.0.0-beta.1.nupkg` from Story 9.3. However, Stories 9.10–9.14 have shipped since then, so **Task 1 rebuilds the package** to include all final changes. The fresh-install smoke test must pass on this rebuilt package.

### Key files and paths

| Artifact | Path |
|----------|------|
| Package project | `AgentRun.Umbraco/AgentRun.Umbraco.csproj` |
| Existing nupkg | `nupkg/AgentRun.Umbraco.1.0.0-beta.1.nupkg` |
| README | `README.md` |
| Workflow authoring guide | `docs/workflow-authoring-guide.md` |
| Beta test plan | `docs/beta-test-plan.md` |
| Content Quality Audit workflow | `AgentRun.Umbraco/Workflows/content-quality-audit/` |
| Accessibility Quick-Scan workflow | `AgentRun.Umbraco/Workflows/accessibility-quick-scan/` |
| Umbraco Content Audit workflow | `AgentRun.Umbraco/Workflows/umbraco-content-audit/` |
| JSON Schema | `AgentRun.Umbraco/Schemas/workflow-schema.json` |

### Install instructions for invitees

The README Quick Start (Steps 1–4) is the canonical install guide. Key points to emphasise in the invitation:

1. **Run the Umbraco install wizard first** — Umbraco.AI crashes on startup if no database exists
2. **Install Umbraco.AI as a direct dependency** — `dotnet add package Umbraco.AI` separately so it survives AgentRun uninstall
3. **Copy workflows from NuGet cache** — NuGet links contentFiles from cache, doesn't copy to disk; the `cp -r` step is required
4. **Grant section permissions** — Users > User Groups > Administrators: grant both AI and AgentRun sections, then log out and back in
5. **Start with Umbraco Content Audit** — no URLs needed, reads their own content, immediate value

### Previous story intelligence

Story 9.14 (the immediate predecessor) was documentation-only. Story 9.3 was the last story that touched packaging — it established the `.csproj` Pack configuration, contentFiles layout, and the beta test plan. The fresh-install smoke test in 9.3 passed on Umbraco 17.3.2 with Umbraco.AI 1.8.0. Since then, only workflow YAML files and agent prompts were added (9.13), plus doc updates (9.14) and security/tool stories (9.10–9.12) — all of which are included in `dotnet pack` output.

### References

- [Epic 9 spec: epics.md lines 1950–2004](../../_bmad-output/planning-artifacts/epics.md)
- [Beta test plan: docs/beta-test-plan.md](../../docs/beta-test-plan.md)
- [README: README.md](../../README.md)
- [Workflow authoring guide: docs/workflow-authoring-guide.md](../../docs/workflow-authoring-guide.md)
- [Package .csproj: AgentRun.Umbraco/AgentRun.Umbraco.csproj](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj)

## Failure & Edge Cases

- **NuGet push fails with 409 Conflict** — the version `1.0.0-beta.1` may already be reserved if a previous push attempt was made. If so, bump to `1.0.0-beta.2` in the `.csproj`, rebuild, and push.
- **Fresh-install smoke test fails** — do NOT push to NuGet.org until the smoke test passes. Diagnose and fix before publishing. Common causes: missing contentFiles in nupkg, workflow discovery path mismatch, Umbraco.AI version incompatibility.
- **NuGet gallery doesn't render README** — verify `PackageReadmeFile` in `.csproj` points to the correct path and the `<None Include="..\README.md" Pack="true" PackagePath="/" />` item is present.
- **Invitee can't install** — the README Quick Start is the first-line support doc. If an invitee hits an issue not covered by the README or known-issues list, capture it as beta feedback (bug or docs-enhancement).
- **No feedback received** — if zero feedback arrives within the first week, follow up individually. Silence usually means the install was too hard, not that everything worked perfectly.

## Dev Agent Record

### Agent Model Used

N/A — human-executed story

### Completion Notes List

### File List
