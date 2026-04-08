# Story 9.1c: First-Run UX — User-Provided URL Input

Status: paused

**Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done), 9.1a (CQA working skeleton — done), **9.7 (Tool Result Offloading for `fetch_url` — NEW BETA BLOCKER, not yet created at the time of this pause)**
**Blocks:** 9.1b (CQA polish), 9.4 (Accessibility Quick-Scan — reuses the prompt patterns established here)

## Pause Reason & Partial Milestone

**Paused:** 2026-04-08
**Reason:** Architectural finding from manual E2E (Task 7.4) on 2026-04-07 evening — `FetchUrlTool` returns raw HTTP response bodies into LLM conversation context, which causes a context-bloat-induced empty turn from Sonnet 4.6 after multi-fetch sequences against real-world pages. The prompt-side mitigations from Tasks 1–6 of this story are proven correct by the failing instance's conversation log; the bottleneck is the tool contract, not the prompt.
**Sprint Change Proposal:** [_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md](_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md)
**Architectural finding:** [_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md](_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md)
**Failing instance:** `caed201cbc5d4a9eb6a68f1ff6aafb06` (Content Quality Audit) — see `conversation-scanner.jsonl`

### What is committed as the partial milestone

The strengthened scanner.md prompt that landed during Tasks 1–6 of this story is committed to `main` and stays committed. It is correct work, proven by the conversation log, and represents real progress against this story's acceptance criteria. The five hard invariants, the verbatim opening line, the verbatim re-prompt, the URL extraction rules (including protocol prepending and deduplication notes), the failure-bucketing logic ("Failed URLs" vs "Skipped — non-HTML content"), and the truncation handling are all locked in scanner.md and are not relitigated by this pause.

### What is blocked until Story 9.7 ships

The acceptance criteria that require multi-URL end-to-end fetching to be reliable cannot be validated against the current tool contract. Specifically AC #3 (multi-URL extraction) and AC #6 (5+ URLs in sequence) — and Task 7's full manual E2E walkthrough — are blocked pending Story 9.7's tool result offloading fix. AC #7 and AC #8 are met in the prompt instructions but cannot be fully E2E-validated until 9.7 lands either. The other ACs (#1, #2, #4, #5, #9) are met by the partial milestone.

### How this story resumes

Once Story 9.7 ships and the new tool contract is in place:

1. The dev agent (or Adam) re-runs Task 7.1 through 7.10 against the new tool contract.
2. If the trace looks clean, the BLOCKED ACs are revalidated, this pause section is removed, the story status changes from `paused` to `review`, and the story moves to its normal completion path.
3. If new problems surface during the re-run, escalate to Winston (architect) before iterating.

### What this pause does NOT mean

- ❌ Story 9-1c is **not** descoped, deferred to v2, or cancelled. It remains in scope for the private beta.
- ❌ The trustworthiness gate criterion is **unchanged**. The mechanism for hitting that gate has changed (multi-URL flows now go through the new tool contract from Story 9.7), but the gate itself — "the example workflow's first-run experience is welcoming, clear, and works on real user-provided URLs" — is what it always was.
- ❌ The scanner.md prompt is **not** going to be reverted, weakened, or rewritten. It is correct as committed.
- ❌ Tasks 1–6 are **not** going to be re-done. They are complete. Only Task 7 is paused.

## Story

As a developer running Content Quality Audit for the first time,
I want the scanner agent to gracefully and unambiguously ask me for URLs to audit,
so that my first interaction with the package is welcoming, clear, and successful on real content.

## Context

**UX Mode: Interactive (primary). The user drives step progression.**

This story is **pure prompt/agent work**. No engine changes. No new tools. No new workflow YAML keys. No bundled sample data, no `sample-content/` folder, no example URLs file. The principle is non-negotiable: the example workflow asks the user for URLs, the user pastes URLs, the agent audits real content. There is nowhere to hide behind a stacked deck — this is what makes the Story 9.1b trustworthiness gate the *actual* gate the package must pass.

Story 9.1a delivered the working skeleton (`scanner.md`, `analyser.md`, `reporter.md`, `workflow.yaml`). Story 9.0 fixed the ToolLoop stall and Story 9.6 made fetch limits configurable so real-world page sizes work. This story tightens the **scanner.md prompt** so the first-run UX is welcoming, deterministic, and resilient to messy human input.

### Files in scope

- **EDIT only:** `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`

That is the entire surface area. No other files, no C#, no TypeScript, no YAML changes, no new artifacts.

## Acceptance Criteria

> **AC status legend (added 2026-04-08 by Sprint Change Proposal):**
> - `[MET — partial milestone, committed 2026-04-08]` — validated by the strengthened scanner.md committed during Tasks 1–6
> - `[MET in prompt — partial milestone, committed 2026-04-08; full E2E validation paused pending Story 9.7]` — instructions are in scanner.md but cannot be end-to-end validated against the current tool contract
> - `[BLOCKED — see Sprint Change Proposal 2026-04-08; cannot be validated until Story 9.7 ships and the tool contract is fixed]` — explicitly broken by the `fetch_url` context-bloat bug; resumes after 9.7

1. `[MET — partial milestone, committed 2026-04-08]` **Given** a developer has installed the package, configured a profile, and started a Content Quality Audit instance, **When** the scanner step begins, **Then** the agent's opening message uses **exactly this verbatim wording**: _"Paste one or more URLs you'd like me to audit, one per line. You can paste URLs from your own site or any public site you'd like a second opinion on."_ **And** the message contains no preamble, no greeting fluff, and no narration before or after the prompt.

2. `[MET — partial milestone, committed 2026-04-08]` **Given** the user pastes a single well-formed URL into the chat, **When** the scanner receives it, **Then** the agent immediately calls `fetch_url` in the same turn (no narration, no "Let me fetch that page now…") — Story 9.0's stall detection enforces this and the prompt must reinforce it.

3. `[BLOCKED — see Sprint Change Proposal 2026-04-08; cannot be validated until Story 9.7 ships and the tool contract is fixed]` **Given** the user pastes multiple URLs separated by **newlines, commas, spaces, or any mixture** within a single message, **When** the scanner processes the message, **Then** the agent extracts all URLs correctly and processes them in sequence. _Note: prompt instructions for extraction are in place; the failure mode is downstream — the model goes empty after multiple raw HTML payloads accumulate in conversation context. See architectural finding report for evidence._

4. `[MET — partial milestone, committed 2026-04-08]` **Given** the user pastes a domain without an explicit protocol (e.g. `www.wearecogworks.com` or `wearecogworks.com`), **When** the scanner processes it, **Then** the agent prepends `https://` and proceeds. _This case was confirmed working in the live test on 2026-04-07 — the new prompt must preserve the behaviour and not regress it._

5. `[MET — partial milestone, committed 2026-04-08]` **Given** the user's message contains **no recognisable URLs** (e.g. "hello", "what should I do?", "can you audit my homepage"), **When** the scanner processes it, **Then** the agent re-prompts politely with the verbatim opening line plus a one-line example (e.g. `https://example.com/about`). The agent does NOT guess URLs, does NOT proceed with zero URLs, and does NOT stall.

6. `[BLOCKED — see Sprint Change Proposal 2026-04-08; cannot be validated until Story 9.7 ships and the tool contract is fixed]` **Given** the user pastes 5 or more URLs at once, **When** the scanner processes them, **Then** the agent fetches each URL in sequence, **And** all results are aggregated into the same `artifacts/scan-results.md` file, **And** the agent does not stall between fetches (Story 9.0 prerequisite — verify the prompt doesn't introduce text-only turns mid-batch). _Note: this is the AC the failing instance directly broke. The scanner correctly issues sequential fetches per the prompt invariants; the stall happens in the model layer due to context bloat from accumulated raw HTML tool results, not in the prompt._

7. `[MET in prompt — partial milestone, committed 2026-04-08; full E2E validation paused pending Story 9.7]` **Given** a fetch succeeds but the response is truncated at the configured `fetch_url.max_response_bytes` ceiling, **When** the scanner processes the truncated response, **Then** the agent notes the truncation in `scan-results.md` for that page, **And** the agent still processes the content that was returned (does not retry or abort the page).

8. `[MET in prompt — partial milestone, committed 2026-04-08; full E2E validation paused pending Story 9.7]` **Given** any of the following failure cases — `fetch_url` returns an error string, the URL is rejected by SSRF protection, the URL returns a non-HTML content type, or the URL requires authentication — **When** the scanner encounters them, **Then** the agent records the URL and the error/reason in a "Failed URLs" section of `scan-results.md` and continues processing remaining URLs.

9. `[MET — partial milestone, committed 2026-04-08]` **And** all existing tests still pass (`dotnet test AgentRun.Umbraco.slnx` and `npm test` in `AgentRun.Umbraco/Client/`). This story is content-only — no test changes expected.

## What NOT to Build

- ❌ **No bundled sample data.** No `sample-content/` folder. No example URLs file. No demo site. No inline mock content. **Drift alarm if any of these appear in the implementation.**
- ❌ **No engine changes.** Pure prompt work. If the dev agent proposes editing any file under `AgentRun.Umbraco/Engine/`, `AgentRun.Umbraco/Tools/`, or any C#/TypeScript file — **stop and re-read this spec.**
- ❌ **No new tools or new workflow YAML keys.** `fetch_url` and `write_file` already do everything needed.
- ❌ **No new completion logic.** The scanner is "done" when it has written `artifacts/scan-results.md`. The existing `files_exist` completion check covers this.
- ❌ **No URL allow-list, no engine-side URL pre-validation.** The agent handles malformed input gracefully via re-prompting; SSRF is already enforced inside `FetchUrlTool` via the existing `SsrfProtection` validator. Do not add a second layer.
- ❌ **No paraphrasing the verbatim opening line.** It is locked. (See AC #1.)
- ❌ **No edits to `analyser.md`, `reporter.md`, or `workflow.yaml`.** Scanner only.
- ❌ **No "automated tests" for the prompt.** Validation is manual E2E.

## Failure & Edge Cases

The dev agent implements what's specified — anything missing here becomes a bug.

- **Empty / non-URL input** → Re-prompt with the verbatim line + a one-line example. Never proceed with zero URLs. Never guess. Never stall. (AC #5)
- **URL behind authentication (HTTP 401/403)** → `fetch_url` returns the HTTP status string; record under "Failed URLs" with the status, continue with remaining URLs.
- **URL behind SSRF protection (RFC1918, loopback, link-local)** → `FetchUrlTool` throws `ToolExecutionException`, the engine surfaces the message as a tool error result; record under "Failed URLs" with a clear security reason like _"Internal/private addresses are blocked for security."_ Continue with remaining URLs.
- **URL returns non-HTML content type (PDF, JSON, image)** → Note the content type, skip detailed analysis for that URL, record it in `scan-results.md` (not under "Failed URLs" — it didn't fail, it just isn't analysable).
- **Same URL pasted twice** → Either deduplicate or process both. Both are acceptable. Must not crash or loop.
- **Domain without protocol** → Prepend `https://` and proceed. Regression-critical (see AC #4).
- **Truncated response** → Note truncation in `scan-results.md`, process what was returned, do not retry, do not abort the page. (AC #7)
- **Mid-batch stall risk** → The scanner must not produce text-only turns between fetches in a multi-URL batch. Brief progress markers are allowed only when accompanied by the next tool call in the same turn. (AC #2, AC #6, Story 9.0 invariant)
- **User cancels mid-fetch** → Existing cancellation behaviour, no new requirements. The prompt does not need to handle this.
- **Security: deny by default** → User-provided URLs are the SSRF surface. Unrecognised or unspecified inputs must be denied/rejected — the prompt re-prompts on no recognisable URLs (AC #5), and `SsrfProtection` denies anything else at the tool boundary. Do not soften either layer.

## Tasks / Subtasks

- [x] **Task 1: Lock the verbatim opening line** (AC: #1)
  - [x] 1.1 Open `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`
  - [x] 1.2 Replace the current "Greet the user and ask them to provide…" instruction with an explicit directive: _"Your first message in any new scanner step must be exactly this, with no preamble or additions: `Paste one or more URLs you'd like me to audit, one per line. You can paste URLs from your own site or any public site you'd like a second opinion on.`"_
  - [x] 1.3 Add: _"Do not greet, do not introduce yourself, do not narrate. The opening line above is the entire first message."_

- [x] **Task 2: Reinforce no-stall behaviour after URLs are received** (AC: #2, #6)
  - [x] 2.1 Strengthen the existing "Critical: Interactive Mode Behaviour" section so the rule is unmissable: as soon as URLs are extracted, the next assistant turn MUST contain a `fetch_url` call. No acknowledgement, no "let me fetch that", no progress narration without an accompanying tool call.
  - [x] 2.2 Add: _"Between fetches in a multi-URL batch, you may include a one-line progress marker (e.g. `Fetched 3 of 5…`) ONLY if it is in the same turn as the next `fetch_url` call. A standalone progress message is forbidden — it will stall the workflow."_ (Story 9.0 invariant.)

- [x] **Task 3: URL extraction rules** (AC: #3, #4)
  - [x] 3.1 Update the URL extraction instruction to explicitly enumerate the supported separators: newlines, commas, spaces, mixed delimiters within a single message, URLs embedded in prose.
  - [x] 3.2 Add explicit protocol-prepending rule: _"If the user provides a host without a scheme (e.g. `www.wearecogworks.com` or `wearecogworks.com`), prepend `https://` before passing it to `fetch_url`. This is a confirmed working behaviour — preserve it."_
  - [x] 3.3 Add explicit deduplication note: _"If the same URL appears more than once in the user's message, you may fetch it once or fetch it twice — both are acceptable, but never crash or loop."_

- [x] **Task 4: Re-prompt on zero URLs** (AC: #5)
  - [x] 4.1 Replace the current "ask them to provide URLs before proceeding" instruction with an explicit re-prompt template:
    > _"If the user's message contains no recognisable URLs, your next message must be exactly: `I didn't catch any URLs in that message. Paste one or more URLs you'd like me to audit, one per line — for example: https://example.com/about`. Do not guess URLs. Do not proceed with zero URLs. Do not narrate."_
  - [x] 4.2 Add: _"Apply this re-prompt every time the user replies without recognisable URLs, including after a previous re-prompt."_

- [x] **Task 5: Failure cases tightened in the prompt** (AC: #7, #8)
  - [x] 5.1 Reword the existing "Failed URLs" instruction so the dev agent and the scanner LLM both know the categories: HTTP error status, SSRF rejection, connection failure, and authentication required (401/403).
  - [x] 5.2 Add explicit non-HTML handling: _"If a response is non-HTML (PDF, JSON, image), record it under a 'Skipped — non-HTML content' section in `scan-results.md` with the URL and content type. This is not a failure — do not put it under 'Failed URLs'."_
  - [x] 5.3 Reword the truncation instruction: _"If a response includes the truncation marker, note the truncation against that page in `scan-results.md` and process whatever content was returned. Do not retry. Do not abort the page."_

- [x] **Task 6: Verify existing tests still pass** (AC: #9)
  - [x] 6.1 Run `dotnet test AgentRun.Umbraco.slnx` — expect no regressions (this story does not touch any C#).
  - [x] 6.2 Run `npm test` in `AgentRun.Umbraco/Client/` — expect no regressions.

- [ ] **Task 7: Manual E2E validation** (AC: all) **[PAUSED 2026-04-08 — resume after Story 9.7 ships, see [sprint-change-proposal-2026-04-08-9-1c-pause.md](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md). Validation steps below remain authoritative — do NOT delete them. Re-run all of 7.1–7.10 against the new tool contract once Story 9.7 lands.]**
  - [ ] 7.1 Start the TestSite (`dotnet run` from the TestSite project).
  - [ ] 7.2 Start a fresh Content Quality Audit instance and verify the scanner's opening line is **exactly verbatim** (AC #1). Compare character-for-character.
  - [ ] 7.3 Paste a single well-formed URL (`https://www.umbraco.com`) — verify the agent calls `fetch_url` immediately with no narration (AC #2).
  - [ ] 7.4 Start a new instance and paste 5+ URLs, mixing newlines and commas — verify all are fetched and aggregated into `scan-results.md` with no mid-batch stalls (AC #3, AC #6).
  - [ ] 7.5 Start a new instance and paste `www.wearecogworks.com` (no protocol) — verify the agent prepends `https://` and proceeds (AC #4 — regression-critical, this was working on 2026-04-07).
  - [ ] 7.6 Start a new instance and reply with "hello, please audit my homepage" — verify the agent re-prompts with the exact re-prompt template and does not guess URLs (AC #5). Reply with another non-URL message and verify the re-prompt repeats.
  - [ ] 7.7 Start a new instance and paste an SSRF-blocked URL (`http://192.168.1.1`) plus a real URL — verify the blocked URL appears under "Failed URLs" with a security reason and the real URL is still scanned (AC #8).
  - [ ] 7.8 Start a new instance and paste a URL to a PDF (any public PDF link) plus a real URL — verify the PDF is recorded under "Skipped — non-HTML content" (NOT "Failed URLs") and the real URL is still scanned (AC #8).
  - [ ] 7.9 Start a new instance and paste a URL that returns a very large page (e.g. a long Wikipedia article) — verify truncation is noted in `scan-results.md` and the page is still processed (AC #7). _Note: this test depends on Story 9.6's configured limits — use whatever ceiling is currently configured for the TestSite._
  - [ ] 7.10 Confirm `scan-results.md`, `quality-scores.md`, and `audit-report.md` all generate correctly through Steps 2 and 3 (no regression in downstream agents — the prompt change is scanner-only but verify the artifact contract is intact).

## Dev Notes

### Why this story is small but high-leverage

This is a ~one-file change, but it locks the first-run product positioning. The verbatim opening line is intentional product copy — "you can paste URLs from your own site or any public site you'd like a second opinion on" frames AgentRun as honest, useful, and unapologetically real, in a market full of slick fake demos. Story 9.3's README pull-quote leans on this exact framing. Do not paraphrase.

### What the engine already gives you (do not re-implement)

- **`FetchUrlTool`** handles SSRF protection, HTTP errors, content-type sniffing, truncation, and timeouts. It returns either the response body string or throws `ToolExecutionException` (which the engine surfaces as a tool error result to the LLM). The prompt must trust this and not duplicate validation.
- **Story 9.0's stall detection** raises an error if the agent produces a text-only turn when work is clearly outstanding. The prompt must reinforce the contract — text without a tool call is forbidden mid-task — but the engine is the safety net.
- **Story 9.6's configurable limits** expose `fetch_url.max_response_bytes`, `fetch_url.timeout_seconds`, and `tool_loop.user_message_timeout_seconds`. The TestSite's `workflow.yaml` does not currently override defaults; this story does not touch `workflow.yaml`.
- **Interactive mode `WaitToReadAsync`** is how the scanner waits for the user to paste URLs. The prompt does not need to mention this mechanism — just produce a text-only turn (the verbatim opening line) and the engine handles the rest.

### Lessons from previous work

- **Manual E2E finds seam bugs.** The scanner code paths look fine in isolation; the bugs always live in the seam between prompt instructions and ToolLoop behaviour. Don't skip Task 7.
- **Browser testing shortcut.** If a re-prompt or extraction rule misbehaves in dev, ask Adam to verify in the browser rather than guessing repeatedly. Cheaper than another iteration loop.
- **Stories must include "Failure & Edge Cases."** This story's `Failure & Edge Cases` section is binding — if a case isn't listed there, the dev agent isn't expected to handle it, and an unspecified failure mode becomes a bug, not a missed requirement.
- **Security: deny by default.** User-provided URLs are the SSRF surface. The deny-by-default statement is in `Failure & Edge Cases` above and the tool boundary already enforces it. Do not soften.
- **Simplest fix first.** This story is one prompt file. If the dev agent starts proposing engine changes or new tools, that is drift — re-read "What NOT to Build."

### Project Structure Notes

- File touched: `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` (edit only)
- No changes to any file under `AgentRun.Umbraco/`, `AgentRun.Umbraco.Tests/`, or `AgentRun.Umbraco.TestSite/` outside the path above.
- Existing scanner.md is ~63 lines; the rewrite will likely land in the 70–110 line range. Keep it tight.

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Story 9.1c (lines 1367–1434)]
- [Source: _bmad-output/implementation-artifacts/9-1a-content-quality-audit-working-skeleton.md — current scanner.md design and the URL-based scanning rationale]
- [Source: _bmad-output/implementation-artifacts/9-0-toolloop-stall-recovery.md — text-only turn invariant the prompt must respect]
- [Source: _bmad-output/implementation-artifacts/9-6-workflow-configurable-tool-limits.md — configurable `fetch_url` limits the scanner now relies on]
- [Source: _bmad-output/project-context.md — Critical Don't-Miss Rules § Security (SSRF, deny by default, prompt injection delineation)]
- [Source: AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md — file under edit]
- [Source: AgentRun.Umbraco/Tools/FetchUrlTool.cs — error string contract and SSRF rejection behaviour]

## Dev Agent Record

### Agent Model Used

claude-opus-4-6 (1M context)

### Debug Log References

- `dotnet test AgentRun.Umbraco.slnx` — 386 passed, 0 failed (17s)
- `npm test` (AgentRun.Umbraco/Client) — 162 passed, 0 failed (43s)

### Completion Notes List

- Tasks 1–6 complete. Scanner.md fully rewritten in line with the spec: verbatim opening line locked, verbatim re-prompt locked, no-stall rules strengthened (incl. same-turn progress-marker carve-out), URL extraction rules with explicit protocol-prepending and dedup notes, failure handling split into "Failed URLs" vs "Skipped — non-HTML content", truncation rule clarified.
- Output template updated to include the new "Skipped — non-HTML content" section and a per-page **Notes** field for truncation. Backwards compatible with downstream `analyser.md` / `reporter.md` (which were not edited per the spec) — they read the file as markdown so the additional section is additive, not breaking.
- No engine, tool, YAML, C# or TypeScript changes. Pure prompt edit, single file.
- **2026-04-08 — STORY PAUSED.** Task 7 manual E2E (run 2026-04-07 evening, instance `caed201cbc5d4a9eb6a68f1ff6aafb06`) surfaced an architectural finding: `FetchUrlTool` returns raw HTTP response bodies into LLM conversation context, causing context-bloat-induced empty turns from Sonnet 4.6 after multi-fetch sequences against real-world pages. The prompt-side mitigations from Tasks 1–6 are proven correct by the conversation log; the bottleneck is the tool contract. Story is paused pending the new beta-blocker Story 9.7 (Tool Result Offloading for `fetch_url`) which Bob will create immediately after the course-correction ceremony. Tasks 1–6 stay committed as the partial milestone. ACs #3 and #6 are BLOCKED, ACs #7 and #8 are met-in-prompt-only-pending-9.7, all other ACs are MET. Task 7 will resume once 9.7 ships. See [sprint-change-proposal-2026-04-08-9-1c-pause.md](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md) and [9-1c-architectural-finding-fetch-url-context-bloat.md](../planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md).

### File List

- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` (modified)
