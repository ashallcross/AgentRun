# Story 9.1c: First-Run UX — User-Provided URL Input

Status: done (Task 7 manual E2E green 2026-04-10; all 10 sub-steps complete; ACs #1–#9 MET; no code changes since the 2026-04-08 partial milestone, manual E2E was the only outstanding gate)

**Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done), 9.1a (CQA working skeleton — done), 9.7 (Tool Result Offloading for `fetch_url` — done), 9.9 (read_file size guard — done), 9.1b (CQA Polish & Quality, including Phase 1 carve-out #3 `MaxOutputTokens=32768` — done). **All resume conditions are now met.**
**Blocks:** 9.4 (Accessibility Quick-Scan — reuses the prompt patterns established here)

## Pause History & Resume Conditions (RESOLVED 2026-04-09)

> **Resume status (2026-04-09):** All blocking conditions are met. Story is ready-for-dev. Amelia's job on resume is Task 7.1–7.10 against the current `scanner.md` on `main`. The pause history below is preserved as the historical record.

### Resume conditions met (2026-04-09)

- **9.7 done** (Tool Result Offloading for `fetch_url`) — `fetch_url` no longer inlines raw HTTP response bodies; structured handles + cached payloads landed.
- **9.9 done** (read_file size guard) — defence-in-depth size guard against unbounded `read_file` payloads.
- **9.1b done** (CQA Polish & Quality, including Phase 1 carve-out #3) — root cause of the multi-URL stall was finally diagnosed as `MaxOutputTokens` unset on `ChatOptions` (defaulting to ~4096), surfacing as `FinishReason=length` on Sonnet 4.6 with empty `accumulatedTextLength`. Fix: hardcoded `MaxOutputTokens=32768` on `StepExecutor.cs:160`. Architect-authored ruling and the diagnostic-gate paper trail are in 9-1b's spec change-log.
- **End-to-end green E2E run** on 2026-04-09 09:22 (instance `64dc770a0d6b4ae2aea9a3f97454aa02`) — scanner → analyser → reporter all completed cleanly on a multi-URL CQA run. This is the live evidence the underlying mechanism that broke 9-1c is fixed.
- **scanner.md verified intact post-9.1b on 2026-04-09** — Architect (Winston) audited the diff from the 9-1c Tasks 1–6 baseline (commit `bfe317a`) to HEAD. Verbatim opening line and verbatim re-prompt are byte-identical. All operational rules from Tasks 2–5 are functionally intact. The Task 3.2 protocol-prepending rule had its "this is a confirmed working behaviour — preserve it" tagline trimmed (cosmetic — the LLM-facing rule is preserved). The "Skipped — non-HTML content" detection mechanism was adapted to the structured-extraction error contract introduced by 9-1b (functionally intact, AC #8's intent honoured). The truncation rule was compressed to the typed `truncated` boolean from the structured handle (functionally intact, AC #7's intent honoured). **No restoration commit required.**

### Important: scanner.md has evolved underneath this spec since the pause

The following 9-1b changes are forward changes, not regressions, but Amelia must be aware of them when comparing this spec against the current `scanner.md` on disk:

1. **A new "## Your Task" section** was added to the top of `scanner.md` by 9-1b Phase 2 polish pass #2 (commit `f4088b1`). It states the task in two numbered steps and an emphatic "task is not complete until `write_file`" reminder. This section is part of the locked structure now.
2. **Invariant #4 was substantively rewritten** from `post-fetch → read_file → write_file` to `post-fetch → write_file` (commits `55a7472` then `f4088b1`). `read_file` was removed entirely from the scanner loop because 9-1b's structured extraction means there is no cached HTML for the scanner to parse client-side. This was an architect-approved rewording during 9-1b. **Implication for Task 7.4:** the expected tool sequence for a 5-URL run is now `fetch_url × 5 → write_file × 1` (six tool calls total), **not** the old `fetch_url → read_file → fetch_url → read_file → … → write_file` shape Task 7.4 was originally written against. If the run produces six tool calls in that order and the workflow completes, that is a pass.
3. **The "Fetching and Parsing" section was renamed to "Fetching"** and the in-prompt HTML-parsing instructions were removed because 9-1b moved that work server-side via `fetch_url(extract: "structured")`. The scanner now consumes typed fields (`title`, `meta_description`, `headings.h1`, `word_count`, `images.*`, `links.*`, `truncated`) directly from the structured handle.
4. **The Output Template field references** were updated to cite structured handle fields (e.g. `[headings.h1 list]`, `[images.with_alt]`). Behaviourally identical to the user; just different placeholders in the template.
5. **The end-of-file Reminder paragraph** was rewritten to match the new Invariant #4 (no longer mentions `read_file`).

### Original pause (preserved as historical record)

**Paused:** 2026-04-08
**Original reason (now superseded):** Architectural finding from manual E2E (Task 7.4) on 2026-04-07 evening attributed the multi-URL stall to `FetchUrlTool` returning raw HTTP response bodies into LLM conversation context, causing context bloat on Sonnet 4.6. **This diagnosis turned out to be only partially correct.** Story 9.7's tool-result-offloading fix (structured handles + on-disk cache) was a real and necessary improvement, but it was not the load-bearing fix. The actual root cause of the stall — discovered during 9-1b's investigation on 2026-04-09 — was `MaxOutputTokens` being unset on `ChatOptions`, defaulting to ~4096, causing Sonnet 4.6 to hit the API output ceiling mid-thought (manifesting as `FinishReason=length` with `accumulatedTextLength=0`). The full diagnostic paper trail is in 9-1b's spec change-log under Phase 1 carve-out #3. **Both fixes — 9.7's structured handles and 9-1b's `MaxOutputTokens=32768` bump — are now on `main`, and the joint result is the green E2E run on 2026-04-09 09:22.**
**Sprint Change Proposal:** [_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md](_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md)
**Architectural finding (historical, partially superseded):** [_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md](_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md)
**Failing instance (historical):** `caed201cbc5d4a9eb6a68f1ff6aafb06` (Content Quality Audit) — see `conversation-scanner.jsonl`

### What is committed as the partial milestone

The strengthened scanner.md prompt that landed during Tasks 1–6 of this story is committed to `main` and stays committed. It is correct work, proven by the conversation log, and represents real progress against this story's acceptance criteria. The five hard invariants, the verbatim opening line, the verbatim re-prompt, the URL extraction rules (including protocol prepending and deduplication notes), the failure-bucketing logic ("Failed URLs" vs "Skipped — non-HTML content"), and the truncation handling are all locked in scanner.md and are not relitigated by this pause.

### Resume instructions (2026-04-09)

The blockers are gone. To resume:

1. Amelia re-runs Task 7.1 through 7.10 against the current `scanner.md` on `main`. **No file edits expected.** This is a manual E2E gate, not an implementation pass.
2. AC status flags below have been pre-flipped from `[BLOCKED]` to `[MET — Task 7 manual E2E green 2026-04-10]` — those ACs become `[MET]` once Task 7 passes.
3. If Task 7 passes end-to-end, the story status changes from `ready-for-dev` → `review` → `done`.
4. If new problems surface during the re-run, escalate to Winston (architect) before iterating. The diagnostic-gate lesson from 9-1b applies: when the cost of a diagnostic is single-digit minutes and the cost of a wrong fix is hours, run the diagnostic first.

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

> **AC status legend (updated 2026-04-09 — all blockers cleared):**
> - `[MET — partial milestone, committed 2026-04-08]` — validated by the strengthened scanner.md committed during Tasks 1–6 (verified intact post-9.1b on 2026-04-09)
> - `[MET — Task 7 manual E2E green 2026-04-10]` — formerly BLOCKED or MET-in-prompt; the underlying mechanism is now fixed (9.7 + 9.1b carve-out #3) and the green E2E run on 2026-04-09 09:22 is the live evidence. Becomes `[MET]` once Task 7 passes.

1. `[MET — partial milestone, committed 2026-04-08]` **Given** a developer has installed the package, configured a profile, and started a Content Quality Audit instance, **When** the scanner step begins, **Then** the agent's opening message uses **exactly this verbatim wording**: _"Paste one or more URLs you'd like me to audit, one per line. You can paste URLs from your own site or any public site you'd like a second opinion on."_ **And** the message contains no preamble, no greeting fluff, and no narration before or after the prompt.

2. `[MET — partial milestone, committed 2026-04-08]` **Given** the user pastes a single well-formed URL into the chat, **When** the scanner receives it, **Then** the agent immediately calls `fetch_url` in the same turn (no narration, no "Let me fetch that page now…") — Story 9.0's stall detection enforces this and the prompt must reinforce it.

3. `[MET — Task 7 manual E2E green 2026-04-10]` **Given** the user pastes multiple URLs separated by **newlines, commas, spaces, or any mixture** within a single message, **When** the scanner processes the message, **Then** the agent extracts all URLs correctly and processes them in sequence. _2026-04-09: the underlying multi-URL stall (originally diagnosed as fetch_url context bloat, actually `MaxOutputTokens=4096` ceiling) is fixed by 9.7 + 9.1b carve-out #3. Green E2E run on 2026-04-09 09:22 demonstrates the mechanism works; Task 7 re-run is the formal validation gate._

4. `[MET — partial milestone, committed 2026-04-08]` **Given** the user pastes a domain without an explicit protocol (e.g. `www.wearecogworks.com` or `wearecogworks.com`), **When** the scanner processes it, **Then** the agent prepends `https://` and proceeds. _This case was confirmed working in the live test on 2026-04-07 — the new prompt must preserve the behaviour and not regress it._

5. `[MET — partial milestone, committed 2026-04-08]` **Given** the user's message contains **no recognisable URLs** (e.g. "hello", "what should I do?", "can you audit my homepage"), **When** the scanner processes it, **Then** the agent re-prompts politely with the verbatim opening line plus a one-line example (e.g. `https://example.com/about`). The agent does NOT guess URLs, does NOT proceed with zero URLs, and does NOT stall.

6. `[MET — Task 7 manual E2E green 2026-04-10]` **Given** the user pastes 5 or more URLs at once, **When** the scanner processes them, **Then** the agent fetches each URL in sequence, **And** all results are aggregated into the same `artifacts/scan-results.md` file, **And** the agent does not stall between fetches (Story 9.0 prerequisite — verify the prompt doesn't introduce text-only turns mid-batch). _2026-04-09: this is the AC the original failing instance directly broke. Root cause was `MaxOutputTokens=4096` (not context bloat as originally diagnosed); fix shipped in 9.1b Phase 1 carve-out #3. The 2026-04-09 09:22 green E2E run is direct evidence of resolution. Task 7.4 is now the regression gate, not a discovery test._

7. `[MET — Task 7 manual E2E green 2026-04-10]` **Given** a fetch succeeds but the response is truncated at the configured `fetch_url.max_response_bytes` ceiling, **When** the scanner processes the truncated response, **Then** the agent notes the truncation in `scan-results.md` for that page, **And** the agent still processes the content that was returned (does not retry or abort the page). _2026-04-09: detection mechanism is now the typed `truncated` boolean from the structured handle (post-9.1b), not a marker string in the body. Behavioural rule unchanged._

8. `[MET — Task 7 manual E2E green 2026-04-10]` **Given** any of the following failure cases — `fetch_url` returns an error string, the URL is rejected by SSRF protection, the URL returns a non-HTML content type, or the URL requires authentication — **When** the scanner encounters them, **Then** the agent records the URL and the error/reason in a "Failed URLs" section of `scan-results.md` and continues processing remaining URLs. _2026-04-09: non-HTML detection now triggers via the structured-extraction error contract (`Cannot extract structured fields from content type '<type>'`) and is recorded under "Skipped — non-HTML content", not "Failed URLs"._

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

- [x] **Task 7: Manual E2E validation** (AC: all) **[PAUSED 2026-04-08 — resume after Story 9.7 ships, see [sprint-change-proposal-2026-04-08-9-1c-pause.md](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md). Validation steps below remain authoritative — do NOT delete them. Re-run all of 7.1–7.10 against the new tool contract once Story 9.7 lands.]**
  - [x] 7.1 Start the TestSite (`dotnet run` from the TestSite project).
  - [x] 7.2 Start a fresh Content Quality Audit instance and verify the scanner's opening line is **exactly verbatim** (AC #1). Compare character-for-character.
  - [x] 7.3 Paste a single well-formed URL (`https://www.umbraco.com`) — verify the agent calls `fetch_url` immediately with no narration (AC #2).
  - [x] 7.4 Start a new instance and paste 5+ URLs, mixing newlines and commas — verify all are fetched and aggregated into `scan-results.md` with no mid-batch stalls (AC #3, AC #6). **Expected tool sequence post-9.1b:** `fetch_url × N → write_file × 1` (N+1 tool calls total, one fetch per assistant turn per Invariant #3, single final write per the rewritten Invariant #4). There is **no** `read_file` in the scanner loop anymore — structured extraction moved that work server-side. **This sub-step is now a regression gate, not a discovery test:** the original failing instance (`caed201cbc5d4a9eb6a68f1ff6aafb06`) was resolved by 9.7 + 9.1b carve-out #3, and instance `64dc770a0d6b4ae2aea9a3f97454aa02` on 2026-04-09 09:22 is the live evidence the path is green. If this sub-step stalls, treat it as a regression and **escalate to Winston immediately** — do not iterate.
  - [x] 7.5 Start a new instance and paste `www.wearecogworks.com` (no protocol) — verify the agent prepends `https://` and proceeds (AC #4 — regression-critical, this was working on 2026-04-07).
  - [x] 7.6 Start a new instance and reply with "hello, please audit my homepage" — verify the agent re-prompts with the exact re-prompt template and does not guess URLs (AC #5). Reply with another non-URL message and verify the re-prompt repeats.
  - [x] 7.7 Start a new instance and paste an SSRF-blocked URL (`http://192.168.1.1`) plus a real URL — verify the blocked URL appears under "Failed URLs" with a security reason and the real URL is still scanned (AC #8).
  - [x] 7.8 Start a new instance and paste a URL to a PDF (any public PDF link) plus a real URL — verify the PDF is recorded under "Skipped — non-HTML content" (NOT "Failed URLs") and the real URL is still scanned (AC #8).
  - [x] 7.9 Start a new instance and paste a URL that returns a very large page (e.g. a long Wikipedia article) — verify truncation is noted in `scan-results.md` and the page is still processed (AC #7). _Note: this test depends on Story 9.6's configured limits — use whatever ceiling is currently configured for the TestSite. Story 9.9's `read_file` size guard also covers this path defensively (defence-in-depth), though scanner.md no longer calls `read_file` post-9.1b._
  - [x] 7.10 Confirm `scan-results.md`, `quality-scores.md`, and `audit-report.md` all generate correctly through Steps 2 and 3 (no regression in downstream agents — the prompt change is scanner-only but verify the artifact contract is intact).

## Dev Notes

### Why this story is small but high-leverage

This is a ~one-file change, but it locks the first-run product positioning. The verbatim opening line is intentional product copy — "you can paste URLs from your own site or any public site you'd like a second opinion on" frames AgentRun as honest, useful, and unapologetically real, in a market full of slick fake demos. Story 9.3's README pull-quote leans on this exact framing. Do not paraphrase.

### What the engine already gives you (do not re-implement) — updated 2026-04-09

- **`FetchUrlTool` (post-9.7 + 9.1b)** handles SSRF protection, HTTP errors, content-type sniffing, truncation, timeouts, **and server-side structured extraction via `extract: "structured"`**. Responses are cached to disk under the per-instance `.fetch-cache/` folder; the tool returns a small JSON handle with typed fields (`title`, `meta_description`, `headings.h1[]`, `headings.h2[]`, `headings.h3_h6_count`, `word_count`, `images.{total,with_alt,missing_alt}`, `links.{internal,external}`, `truncated`). HTML parsing is no longer done in-prompt — the scanner consumes the structured fields directly. Non-HTML content surfaces as a typed extraction error (`Cannot extract structured fields from content type '<type>'`). The prompt must trust this and not duplicate validation.
- **Story 9.0's stall detection** raises an error if the agent produces a text-only turn when work is clearly outstanding. The prompt must reinforce the contract — text without a tool call is forbidden mid-task — but the engine is the safety net.
- **Story 9.1b Phase 1 carve-out #3 — `MaxOutputTokens=32768`** is now hardcoded on `StepExecutor.cs:160`. This unblocks any workflow whose final turn writes a multi-page artefact. Without this, Sonnet 4.6 hits the API output ceiling silently (`FinishReason=length`, `accumulatedTextLength=0`) and presents as a stall. **Do not assume "stall = prompt bug" — check `engine.empty_turn.finish_reason` telemetry first.** Follow-up Story 9.6.1 will wire this through `IToolLimitResolver` for per-step overrides.
- **Story 9.6's configurable limits** expose `fetch_url.max_response_bytes`, `fetch_url.timeout_seconds`, and `tool_loop.user_message_timeout_seconds`. The TestSite's `workflow.yaml` does not currently override defaults; this story does not touch `workflow.yaml`.
- **Story 9.9's `read_file` size guard** is defence-in-depth for the read path, mirroring `fetch_url`'s truncation contract. Scanner does not call `read_file` post-9.1b, but the guard protects analyser/reporter steps from oversized payloads.
- **`engine.empty_turn.finish_reason` telemetry (post-9.1b)** is a permanent structured engine event. Any time an assistant turn ends with no visible content, the `FinishReason` is logged. This is the diagnostic that caught the `MaxOutputTokens=4096` silent-truncation bug. Trust it on future stalls.
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
- **2026-04-10 — STORY RESUMED AND TASK 7 GREEN.** All blockers cleared by 9.7 (done) + 9.9 (done) + 9.1b (done, including Phase 1 carve-out #3 `MaxOutputTokens=32768`). Task 7.1–7.10 walked end-to-end against the current `scanner.md` on `main` (no file edits required — Winston's 2026-04-09 audit confirmed the prompt was preserved through 9.1b's evolution). Results:
  - **7.2 PASS (AC #1)** — verbatim opening line, char-for-char.
  - **7.3 PASS (AC #2)** — single URL → immediate `fetch_url`, zero narration.
  - **7.4 PASS (AC #3, AC #6)** — instance `f95d49e1ca1c485faeb79313d453e958`, 5 URLs with mixed separators (newlines, comma-separated, no-protocol). Tool sequence exactly `fetch_url × 5 → write_file × 1` (post-9.1b shape, no `read_file` in scanner loop). Between-fetches progress markers fired correctly co-located with the next fetch turn (Invariant #2). The original failing instance (`caed201cbc5d4a9eb6a68f1ff6aafb06`) is now ancient history — the joint fix of 9.7 (structured handles) + 9.1b carve-out #3 (`MaxOutputTokens=32768`) closed the multi-URL stall path. **Regression gate cleared.**
  - **7.5 PASS (AC #4)** — bare `www.wearecogworks.com` → `https://www.wearecogworks.com` in the `fetch_url` arguments. Protocol-prepending preserved.
  - **7.6 PASS (AC #5)** — verbatim re-prompt fired char-for-char on first non-URL message ("hello, please audit my homepage") AND repeated correctly on second non-URL message ("what do you mean?"). Loop is stable.
  - **7.7 PASS (AC #8, SSRF half)** — instance with `http://192.168.1.1` + `https://www.umbraco.com`. Blocked URL recorded under "Failed URLs" with verbatim security reason "Internal/private addresses are blocked for security."; real URL still scanned cleanly.
  - **7.8 PASS (AC #8, non-HTML half)** — PDF URL + real URL. PDF correctly bucketed under "Skipped — non-HTML content" (NOT Failed URLs) with content type recorded. The structured-extraction error contract from 9.1b is wired through the scanner correctly.
  - **7.9 PASS (AC #7)** — `workflow.yaml` `max_response_bytes` temporarily lowered to force truncation. Wikipedia "World War II" article fetched, structured handle returned with `truncated: true`, scanner recorded the truncation in the page entry and processed the partial facts (word_count ~251, 1 H2, partial image/link counts). Did not retry, did not abort. Behavioural rule preserved exactly. **`workflow.yaml` reverted to default 2 MB after the test.**
  - **7.10 PASS (full pipeline regression)** — instance `9e5dcad4121d4beba027ab9f62e3ae1c`, clean end-to-end run. scanner → `fetch_url × 5 → write_file × 1` → Complete. analyser → `read_file × 1 → write_file × 1` → Complete. reporter → `read_file × 2 → write_file × 1` → Complete (the second read_file is the new `scan-results.md` read added in 9.1b's reporter prompt rewrite — wired through correctly). Three artifacts produced: `scan-results.md`, `quality-scores.md`, `audit-report.md`. Instance status → **Completed**. No regressions in the downstream agents.
- **All 9 ACs now MET.** Story moves to `review` and then `done`.
- **Bug surfaced during exploration (out of 9.1c scope, not a regression):** Resuming a CQA instance after the scanner step has completed leaves the chat input rejecting all messages with "Failed to send message. Try again." Logged at [bug-finding-2026-04-10-instance-resume-after-step-completion.md](../planning-artifacts/bug-finding-2026-04-10-instance-resume-after-step-completion.md) and added to [deferred-work.md](deferred-work.md) under "Deferred from: Story 9.1c Task 7.4 manual E2E exploration (2026-04-10)". Suggested home: a new Epic 10 story (e.g. **10.X — Instance resume after step completion**). Severity Medium — affects "I left and came back" UX but not the happy path; safe for private beta if documented in known-issues.

### File List

- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` (modified)
