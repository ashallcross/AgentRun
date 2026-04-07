# Epic 9 — Spec Writing Notes

**Created:** 2026-04-07
**Purpose:** Decisions and context that aren't in `epics.md` or `v2-future-considerations.md` but are load-bearing for the remaining Epic 9 story specs. Captured from the SM clarifying round on 2026-04-07 after the Epic 9 re-scope.
**Lifecycle:** Delete (or archive) when Epic 9 ships. This file is intentionally scoped to Epic 9 only and should NOT survive into Epic 10+ planning.

## How to use this file

When invoking `bmad-create-story` for any of the seven remaining Epic 9 stories, pass this file as additional context and point the skill at the relevant section. Example:

> "Create the story for 9.0. Read `_bmad-output/planning-artifacts/epic-9-spec-writing-notes.md`, section 'Story 9.0', and incorporate every bullet into the spec. Also use `_bmad-output/implementation-artifacts/9-6-workflow-configurable-tool-limits.md` as the canonical reference for the tool-limits resolution chain pattern."

## Project-wide rules to bake into every Epic 9 spec

- Test command is **`dotnet test AgentRun.Umbraco.slnx`** — never bare `dotnet test`. Specify the slnx in every Test Plan and Definition of Done.
- Test target ~10 per story as a guideline, not a ceiling. 9.6 justifies ~15 because it's an architectural foundation; smaller stories may have fewer.
- **"Failure & Edge Cases" section is mandatory** and must be specific to the story, not generic.
- **"What NOT to Build" section is mandatory** to prevent scope creep.
- **Production smoke test in Definition of Done** — install the package into a fresh Umbraco 17 site and run end-to-end. Dev hot-reload state masks packaging bugs.
- **Manual E2E validation as a refinement loop** for prompt-tuning stories (9.1b, 9.1c, 9.4); **as a gate** for engine/config stories (9.0, 9.6, 9.2, 9.3); **as the entire story** for 9.5.
- **Deny-by-default statement** required in Failure & Edge Cases for any story touching security-relevant code: 9.0 (engine state machine), 9.1c (user-provided URLs / SSRF surface), 9.4 (same SSRF surface). NOT needed for 9.1b, 9.5, 9.2, 9.3.
- **Top-of-file dependency declaration** — every spec lists `Depends on:` and `Blocks:` near the title so the dev agent and sprint-planning see it at a glance.
- **Story 9.6's spec file is the canonical reference** for the resolution-chain pattern, the YAML/JSON/C# naming map, and the `WorkflowConfigurationException` shape. Any story that touches tool tuning values must point at it rather than re-deriving the pattern.

## Story 9.0 — ToolLoop Stall Recovery

**Depends on:** 9.6 (uses configurable `user_message_timeout_seconds`)
**Blocks:** 9.1c, 9.1b, 9.4 (all need stall fix to test end-to-end)

- **Trichotomy for LLM response classification** (locked decision): empty content → stall (fail fast), content ending in `?` → waiting for user (continue), anything else including narration like "Let me now process that…" → stall. The "narration is stall" call is deliberate — it forces the prompt-quality fix in 9.1b rather than papering over weak prompts at the engine layer.
- **Embed the live test trace** from instance `642b6c583e3540cda11a8d88938f37e1`, scanner step. Quote turn 4 (tool call: `fetch_url({"url":"https://www.wearecogworks.com"})` at `2026-04-07T13:21:47.982Z`), turn 5 truncated to ~200 chars of the HTML body, a one-line gap annotation `(~5 minutes 47 seconds of LLM silence — no tool call, no text)`, and turn 6 (system "Step failed" at `2026-04-07T13:27:34.699Z`). Place in the Context section.
- **Fail-fast architectural decision is locked.** Call out prominently in Context AND a dedicated "Architectural Decision" subsection in Dev Notes. Retry-with-nudge alternative is documented in `v2-future-considerations.md` and must NOT be implemented. Why: simpler to build, easier to debug, prevents hidden retries from masking weak prompts that 9.1b will fix instead.
- **Stall detection ≤ 10 seconds**, not after the user-message timeout. Without this the UX is broken.
- **Use the resolved `user_message_timeout_seconds` from Story 9.6's `IToolLimitResolver`.** Do NOT use the deleted hardcoded 5-minute constant. If 9.6 has not shipped, 9.0 cannot start.
- **Stall detection logic must be cleanly separable from the recovery action** — leaves the door open for retry-with-nudge as a pluggable strategy in V2 without a rewrite.
- **Reuses Story 7.2 retry flow** — do NOT build new retry plumbing.
- Expected length: ~350-400 lines.

## Story 9.1c — First-Run UX (User-Provided URL Input)

**Depends on:** 9.6, 9.0
**Blocks:** 9.1b, 9.4 (both lean on the prompt patterns 9.1c establishes)

- **Pure URL input, NO shipped sample data.** No `sample-content/` folder, no example URLs file, nothing the user has to delete or replace. The principle wins. The friendliness is delivered through the welcoming agent prompt + the Story 9.3 README framing. If the spec proposes bundled data, drift alarm.
- **Welcoming opening line (verbatim):** *"Paste one or more URLs you'd like me to audit, one per line. You can paste URLs from your own site or any public site you'd like a second opinion on."* Lock this in the spec — the dev agent should not paraphrase.
- **Domain-without-protocol case** (`www.wearecogworks.com`) was confirmed working in the live test on 2026-04-07. The agent prepends `https://`. Preserve, don't regress. Worth a regression test in the spec.
- **Deny-by-default applies** — user-provided URLs are the SSRF surface. The spec's Failure & Edge Cases section must include the explicit "Unrecognised or unspecified inputs must be denied/rejected" statement and lean on the existing `SsrfProtection` validator.
- **No engine changes** — pure prompt/agent work plus the existing `fetch_url` tool. If the spec proposes engine work, drift alarm.
- Expected length: ~200-250 lines.

## Story 9.1b — Content Quality Audit Polish & Quality

**Depends on:** 9.6, 9.0, 9.1c
**Blocks:** 9.4 (run 9.1b's loop first so the prompt conventions are settled before 9.4 reuses them)

- **Iterative refinement loop, NOT a build story.** The spec's task structure should reflect the loop: pick inputs → run → review → iterate prompts → re-run → repeat. The spec is more about process discipline than implementation surface.
- **Trustworthiness gate criterion** (locked decision): Adam runs the workflow against ≥3 real test inputs of his choice, signs off in writing in `_bmad-output/qa-signoffs/9-1b-cqa-trustworthiness-signoff.md`. The signoff file's existence is the AC. The gate is the file existing, not a numeric metric.
- **5-pass soft cap** with architect (Winston) escalation. Five passes is not a failure — it's a checkpoint for "if we're still not passing, the prompt structure is wrong, not the prompt content." Bake into the Process section.
- **No deny-by-default needed** — no security surface (it's prompt tuning of files in a sandboxed location).
- **Manual E2E IS the entire story** — there's no separate "build then validate" phase.
- **Lock the model recommendation** to Anthropic Sonnet for the beta (already the default in workflow.yaml). Document in the workflow authoring guide via Story 9.3.
- Expected length: ~200-250 lines.

## Story 9.4 — Accessibility Quick-Scan Workflow

**Depends on:** 9.6, 9.0, 9.1c, 9.1b
**Blocks:** 9.5 (beta distribution needs both example workflows)

- **Same gate shape as 9.1b, different criterion.** WCAG 2.1 AA conformance plausibility, evidence-grounded findings (the agent must be able to quote the offending HTML for every cited criterion — no hallucinated criteria), useful prioritisation by severity (critical / major / minor).
- **Signoff file:** `_bmad-output/qa-signoffs/9-4-accessibility-trustworthiness-signoff.md`. Same 5-pass soft cap with architect escalation.
- **Reuses fetch_url, write_file, read_file. NO new tools, NO new engine features.** If the spec proposes engine work, drift alarm.
- **Deny-by-default applies** — same SSRF surface as 9.1c. Add the statement to Failure & Edge Cases.
- **Workflow shape:** 2-3 steps (fetcher/analyser + reporter, or similar), interactive mode, same welcoming-prompt pattern as 9.1c.
- **Spec it AFTER 9.1b is done** so the prompt-quality conventions are settled and reusable. Running two refinement loops in parallel will produce divergent conventions you'd have to reconcile.
- Expected length: ~250-300 lines.

## Story 9.2 — JSON Schema for workflow.yaml

**Depends on:** 9.6 (must include the new keys)
**Blocks:** 9.3 (docs reference the schema)

- **Must include the 9.6 keys** (`tool_defaults`, `tool_overrides` with the `fetch_url.*` and `tool_loop.*` sub-keys). epics.md was updated 2026-04-07 to make this dependency explicit in 9.2's AC.
- **Sequence after 9.6 is at `review` minimum.** If you spec 9.2 before 9.6 lands, the schema surface might shift and you'll re-do it.
- **Manual E2E as gate:** test the schema in a real IDE (VS Code with the YAML extension is the obvious test bed). Open the example workflow.yaml, deliberately type an invalid value, confirm IDE flags it. Confirm autocomplete suggests the tool_defaults keys.
- **No deny-by-default needed** — the schema is a developer-time validator, not a runtime security boundary.
- Expected length: ~150-200 lines.

## Story 9.3 — Documentation & NuGet Packaging

**Depends on:** 9.6 (resolution chain section), 9.1c (first-run framing), 9.2 (schema reference)
**Blocks:** 9.5 (beta distribution needs the docs to point at)

- **"First Run" framing is product-positioning content, not just docs.** Both the README front matter and the getting-started guide must use the "paste your own URLs" framing. The pull-quote from epics.md is intentional: *"Most AI tool launches in 2026 are slick fake demos. AgentRun being unapologetically real is differentiation."* This phrasing matters — protect it from the dev agent over-polishing it into bland documentation copy.
- **New section required:** "Configuring Tool Tuning Values" — covers the resolution chain, the three migrated values, the available settings, and the security rationale for hard caps. This is the *user-facing* documentation of the 9.6 mechanism. Reference 9.6's spec file as the technical source of truth.
- **NuGet pre-release only** (`1.0.0-beta.1`). Public Marketplace listing moves to Story 10.5. Don't let the dev agent drift into Marketplace work.
- **Manual E2E as gate:** install the package locally into a fresh Umbraco 17 site, follow the getting-started guide as a new user would, confirm every step in the doc actually works against the real package.
- **Documentation portion ships in beta; full 1.0 packaging is Story 10.5.** Scope discipline.
- **No deny-by-default needed** — documentation does not have a runtime security surface.
- Expected length: ~250-300 lines.

## Story 9.5 — Private Beta Distribution Plan

**Depends on:** 9.6, 9.0, 9.1a, 9.1b, 9.1c, 9.2, 9.3, 9.4 (everything else in the epic)
**Blocks:** Story 10.5 (public 1.0 launch)

- **Process story, not code.** Checklist-based DoD. May have ZERO automated test surface — that's expected, do not fake test coverage.
- **NuGet `1.0.0-beta.1` pre-release**, pushed to NuGet.org as pre-release, NOT listed on Umbraco Marketplace, NOT publicly announced.
- **Invite list shape:** ~8–15 testers, drawn from three groups — 3–5 Cogworks engineering colleagues, 3–5 Umbraco community contacts Adam has direct relationships with, 2–5 selected Umbraco Discord members coordinated quietly with mods where appropriate. No public call for testers. DoD = "list compiled with at least 8 named recipients spanning the three groups, reviewed by Adam, invitations sent."
- **Feedback channel:** GitHub issues on the (now-public) repo, labelled `beta-feedback`. Plus an explicit "if you'd rather not file a public issue, email adam@cogworks.[whatever]" fallback for sensitive feedback. DoD = issue template exists, label exists, fallback email documented in invite communication.
- **Beta retrospective** at end of beta period. Decision gate for "ready for public 1.0 (Story 10.5)?".
- **No telemetry, no analytics, no Pro-tier messaging, no pricing.** Free OSS beta only.
- **No deny-by-default needed** — no runtime security surface.
- **Production smoke test in DoD** = "the published `1.0.0-beta.1` package installs cleanly into a fresh Umbraco 17 site and a workflow runs end-to-end against a real LLM provider." That IS the smoke test for this story; it's not separate.
- Expected length: ~150-200 lines.

## Cross-cutting reminders

- **Architect spike NOT required for any of these.** Winston already made the architectural calls during the 2026-04-07 session: fail-fast for stall recovery, hard caps for tool limits, single `max_response_bytes` (collapsed from HTML/JSON split), no canned data, two workflows in beta, private invite list. All recorded in `epics.md` and `v2-future-considerations.md`. The SM's job is to convert these into dev-agent-ready specs, not re-litigate.
- **`v2-future-considerations.md` is read-only during spec writing.** After 9.6 ships, catalogue items 1, 2, 3, and 5 should be marked "shipped in 9.6" — but that's a post-merge update by the architect/SM, not part of any 9.x story.
- **Story 9.1a is done** (the working skeleton). It's blocked from full validation only by the 9.0 stall fix. Do not re-spec it.
- **No new stories beyond the eight in scope.** If a gap is spotted during spec writing, flag it to Adam first — do not silently add stories.
- **Sprint-status.yaml file order reflects execution dependency**, not story-number order. When sequencing work, read top-to-bottom in the Epic 9 section.

## When this file is no longer needed

Delete (or move to `_bmad-output/archive/`) when Story 9.5 reaches `done` and Epic 9 is closed. At that point the decisions are either (a) implemented in code and self-evident, (b) recorded in the per-story spec files in `_bmad-output/implementation-artifacts/`, or (c) captured in the Epic 9 retrospective. This file's job is done.
