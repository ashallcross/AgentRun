# Sprint Change Proposal — Story 8.2 Artifact UX Rework

**Date:** 2026-04-02
**Triggered by:** Story 8-2 (Artifact Viewer & Step Review) — failed E2E validation
**Status:** Approved
**Scope classification:** Moderate

---

## Section 1: Issue Summary

Story 8-2 was fully implemented and passed all automated tests (154 FE / 328 BE), but was rejected during manual E2E validation due to a fundamental UX limitation.

**Core problem:** The design ties artifact viewing to step sidebar clicks with a 1:1 step-to-artifact assumption (`writesTo[0]`). In practice, workflow steps can produce multiple artifacts. The "click step to replace chat panel with artifact viewer" model:
- Only shows one artifact per step, ignoring additional outputs
- Has no way to dismiss the artifact viewer on completed instances (no active step to click back to)
- Restricts artifact browsing to step-by-step navigation rather than providing a unified view of all workflow outputs

**Evidence:**
- `StepDetailResponse.writesTo` is typed as `string[] | null` — the data model supports multiple artifacts, but the UX only reads `[0]`
- E2E testing found no path back to chat on completed workflows
- Prior working implementation (Adam's original app) used a flat artifact list with popover — proven UX

**Resolution:** Code reverted to last commit. Story returned to backlog for rewrite.

---

## Section 2: Impact Analysis

### Epic Impact
- **Epic 8 (Artifact Browsing & Review):** Goal unchanged. Story 8-1 (done) is fully reusable — API endpoint, path encoding, and markdown renderer are UX-agnostic. Story 8-2 needs a complete rewrite with the new UX model.
- **All other epics:** No impact.

### Story Impact
- **8-1 (done):** No changes. `ArtifactEndpoints.cs`, `getArtifact()`, `encodeArtifactPath()`, and `shallai-markdown-renderer` are all reusable in the new model.
- **8-2 (backlog):** Full rewrite. New acceptance criteria, new component structure, new interaction model.
- **Future stories:** No impact.

### Artifact Conflicts
- **PRD:** No conflict. FR63 ("browse all files produced by a workflow instance in a file browser view") and FR64 ("view artifact content with markdown rendering") are satisfied by the new approach — arguably better than before.
- **Architecture:** Minor update needed to frontend component descriptions. Backend architecture unchanged.
- **UX Design Specification:** Significant updates needed. The view-switching state model, step click interactions, auto-activation behaviour, and several UX design requirements (UX-DR4, UX-DR15, UX-DR26, UX-DR27) need rewriting for the popover model.

### Technical Impact
- No backend changes required
- No API changes required
- Frontend only: new components (artifact list, popover), removal of view-switching state from instance-detail

---

## Section 3: Recommended Approach

**Selected:** Direct Adjustment — rewrite story 8-2 within existing epic structure.

**Rationale:**
- Story 8-1's backend and renderer work is clean and fully reusable
- The change is isolated to frontend UX — no ripple effects across the architecture
- The new model (flat list + popover) is simpler to implement than the old one (view-state switching in a 995-line component)
- Low risk: no backend changes, no data model changes, existing API serves the new UX

**Effort estimate:** Medium (comparable to original 8-2 — new components but simpler state management)
**Risk level:** Low
**Timeline impact:** Minimal — one story rewrite, no epic restructuring

---

## Section 4: Detailed Change Proposals

### 4.1 Epic 8 — Story 8.2 Rewrite

**Old approach:** Click completed steps in sidebar to replace chat panel with embedded artifact viewer. One artifact per step (`writesTo[0]`). Auto-activate on workflow completion.

**New approach:** Dedicated artifact list showing all files across all steps. Click filename to open popover overlay with rendered markdown, close button, and download. Chat panel always visible. Support multiple artifacts per step (`writesTo[]` iterated fully).

**Action:** Scrum Master to rewrite story 8-2 spec via `/bmad-create-story` with:
- New acceptance criteria reflecting list + popover model
- Updated task breakdown for new components
- "What NOT to Build" section excluding the old view-switching model
- Failure & Edge Cases for popover interactions (close, download errors, rapid clicks)

### 4.2 UX Design Specification Updates

**Sections to update:**
- Remove `activeView: 'chat' | 'artifact'` state model — chat panel stays always visible
- Replace step click → artifact viewer switching with: step click → conversation history only
- New artifact list component description (`shallai-artifact-list`): lists all artifacts across all steps with filename, step name, timestamp
- New artifact popover description: overlay with rendered markdown, close button, download button
- Update auto-activate: on `run.finished`, popover auto-opens for final artifact (closeable)
- Update empty state: "No artifacts yet." in artifact list during workflow execution
- Update UX-DR4, UX-DR15, UX-DR26, UX-DR27 to reflect popover model

**Action:** UX designer or architect to update spec sections during story rewrite.

### 4.3 Architecture Document — Frontend Components

**Section to update:** Frontend artifact viewing component descriptions.

**Changes:**
- `shallai-artifact-list` component: renders list of all artifacts for an instance
- `shallai-artifact-popover` component (or UUI dialog): renders single artifact in overlay
- `shallai-markdown-renderer` (unchanged, reused)
- Chat panel no longer conditionally replaced
- Architect to decide: derive artifact list client-side from `StepDetailResponse.writesTo[]` arrays, or add a new listing endpoint

**Action:** Architect to update during story rewrite.

---

## Section 5: Implementation Handoff

**Scope classification:** Moderate — requires backlog reorganisation and spec updates before development.

### Handoff Plan

| Role | Responsibility |
|------|---------------|
| **Architect (Winston)** | Update architecture doc frontend section. Decide on artifact listing approach (client-side derivation vs new API endpoint). Review new story spec for technical feasibility. |
| **UX Designer (Sally)** | Update UX design specification sections for artifact list + popover model. Define popover layout, download interaction, and responsive behaviour. |
| **Scrum Master (Bob)** | Rewrite story 8-2 spec with new acceptance criteria and task breakdown via `/bmad-create-story`. Update sprint status when story is ready-for-dev. |
| **Developer (Amelia)** | Implement rewritten story 8-2 once spec is ready-for-dev. |

### Success Criteria
- Story 8-2 rewritten with popover model and marked ready-for-dev
- UX spec updated to reflect new artifact browsing pattern
- Architecture doc updated with new component descriptions
- All workflow artifacts browsable via flat list regardless of which step produced them
- Multiple artifacts per step fully supported
- Popover closeable without losing chat context

---

## Additional Context

### Tree Navigation UX (Captured — Not In Scope)

During this change discussion, a broader UX improvement was identified: moving the workflow listing into Umbraco's section tree navigation (left panel). This would follow Umbraco's native navigation pattern and free the main panel for richer workflow detail pages.

This has been documented in `_bmad-output/planning-artifacts/v2-future-considerations.md` under Ideas Backlog for future scoping. It is deliberately excluded from this change proposal to keep the scope lean.
