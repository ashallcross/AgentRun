# Story 10.7: Code Shape Cleanup — Hotspot Refactoring (MOVED)

Status: moved

**This story was split on 2026-04-15** per Adam's quality-risk concern on a 4-day single-PR delivery. The original seven-track spec (FetchUrlTool + ToolLoop + StepExecutor + instance-detail + content-tool DRY + chat cursor + comment hygiene) has been re-shaped into three sequenced child stories so each track-group gets its own focused code review and manual E2E gate:

| Child story | Tracks | Status |
|---|---|---|
| [Story 10.7a — Backend Hotspot Refactors](./10-7a-backend-hotspot-refactors.md) | A (FetchUrlTool split) + C (StepExecutor partial) + B (ToolLoop split) | **ready-for-dev** |
| [Story 10.7b — Frontend Instance-Detail + Chat Cursor](./10-7b-frontend-instance-detail-and-chat-cursor.md) | D (instance-detail 3-module split) + F (chat cursor 1-line fix) | backlog (→ ready-for-dev when 10.7a is done) |
| [Story 10.7c — Content-Tool DRY + Comment Hygiene](./10-7c-content-tool-dry-and-comment-hygiene.md) | E (ContentToolHelpers + binary-search truncation) + G (comment archaeology strip) | backlog (→ ready-for-dev when 10.7b is done) |

**The 18 locked decisions from the original parent spec are preserved verbatim**, distributed across the three child stories by relevance. Universal decisions (fewest-necessary-moves principle, behaviour preservation, Engine-boundary invariant) appear in all three; track-specific decisions live where their track lives. No scope has been dropped.

**Split rationale:** bundling rationale (shared review/E2E ceremony cost) was reasonable when "this'll all flow" was the assumption — Adam's quality concern flipped that assumption. Each track-group now gets a different code-review LLM + its own manual E2E gate so Track A bugs surface before Track D work begins, not weeks later during final DoD. Locked decision 18 in the original parent spec already authorised this exact staged split ("Tracks A+C+B (backend refactor) land first; Tracks D+F (frontend) land second; Tracks E+G (cross-cutting) land third").

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-15 | Parent Story 10.7 spec created (7 tracks, 18 locked decisions, 15 ACs, 11 tasks, 11 F-cases). Bundling rationale: shared review/E2E ceremony cost dominates per-track code overhead. Test budget ~25–35 new tests; baseline 679 backend + 183 frontend → expected ~705–715 + ~195–200. | Bob (SM) |
| 2026-04-15 | Parent split into three sequenced child stories (10.7a → 10.7b → 10.7c) per Adam's quality-risk concern on 4-day single-PR delivery. All 18 locked decisions preserved and distributed. All 15 ACs, 11 tasks, 11 F-cases distributed. Test budget rebalanced: 10.7a ~14 new, 10.7b ~11 new, 10.7c ~5 new. This file becomes a redirect note; 10.7a is ready-for-dev, 10.7b + 10.7c stay backlog until upstream dependency lands. | Bob (SM) |
