# Architectural Finding: `fetch_url` Context Bloat

**Status:** Open — for architect review
**Discovered:** 2026-04-07 during Story 9-1c manual E2E (Task 7.4)
**Discovered by:** Adam (manual browser test) + dev agent (log analysis)
**Affects:** Story 9-1b scope, Content Quality Audit reliability, all future workflows that fetch large web pages
**Severity:** High — blocks the Story 9-1b trustworthiness gate as currently scoped, and is a latent reliability bug for any multi-fetch workflow

---

## TL;DR

The `fetch_url` tool returns raw response bodies directly into the LLM conversation. After 4 sequential fetches against real-world pages, the scanner step is sitting on **~1.8 MB of raw HTML in conversation context**, all of which is re-sent to Anthropic on every subsequent turn. When a 5th fetch returns a small error string in this degenerate context, Sonnet 4.6 produces an empty assistant turn — `StallDetectedException` fires and the step fails.

The prompt-side mitigations from Story 9-1c (sequential-fetch invariant, post-fetch → write_file invariant, no parallel tool calls) are **all working as designed and proven by the conversation logs**. The bottleneck is the **tool contract**, not the prompt. No amount of further prompt iteration will fix this.

The fix is a small architectural change to `FetchUrlTool`: stop returning raw HTML to the conversation. Either offload the response to a workspace path and return a structured handle, or extract structured fields server-side. This is the **standard "tool result offloading" pattern** used by Claude Code, the Anthropic Computer Use demo, and the Claude Agent SDK.

---

## Evidence

### The failing instance

`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/caed201cbc5d4a9eb6a68f1ff6aafb06/conversation-scanner.jsonl`

### Conversation trace (per-line summary)

| # | Role | Content | Size |
|---|---|---|---|
| 1 | system | "Starting Content Scanner…" | 99 B |
| 2 | assistant | Verbatim opening line | 217 B |
| 3 | user | 5 URLs (newline + comma + no-protocol mix) | 243 B |
| 4 | assistant | `fetch_url` umbraco.com | 218 B |
| 5 | tool | `HTTP 301: Moved Permanently` | 161 B |
| 6 | assistant | `Fetched 1 of 5…` | 95 B |
| 7 | assistant | `fetch_url` wearecogworks.com | 224 B |
| 8 | tool | raw HTML | **159,868 B (~160 KB)** |
| 9 | assistant | `Fetched 2 of 5…` | 95 B |
| 10 | assistant | `fetch_url` umbraco.com/products/cms | 235 B |
| 11 | tool | raw HTML | **258,410 B (~258 KB)** |
| 12 | assistant | `Fetched 3 of 5…` | 95 B |
| 13 | assistant | `fetch_url` bbc.co.uk/news | 221 B |
| 14 | tool | raw HTML | **1,394,712 B (~1.39 MB)** |
| 15 | assistant | `Fetched 4 of 5…` | 95 B |
| 16 | assistant | `fetch_url` wikipedia | 250 B |
| 17 | tool | `HTTP 403: Forbidden` | 153 B |
| 18 | system | `The agent stopped responding mid-task.` | — |

### Two findings from this trace

**Finding A — The prompt mitigations from Story 9-1c are working.** Lines 4–17 show five separate `fetch_url` calls, each in its own assistant turn, each preceded by a one-line progress marker that is in the same turn as the next tool call. This is exactly what `scanner.md` Invariants #2 and #3 specify. Sonnet 4.6 followed the prompt. The prompt-only ceiling has been reached.

**Finding B — The conversation context at line 17 contains ~1.8 MB of raw HTML.** Lines 8, 11, and 14 alone total ~1.81 MB. Every assistant turn from line 9 onwards re-sent that entire payload to Anthropic. When the 5th fetch returned a tiny error string in this degenerate context, the model produced an empty turn instead of either a 6th fetch or `write_file`. This is the failure mode.

### Why the model went empty

This is consistent with known Sonnet 4.6 behaviour patterns: very large prior tool results combined with a small/error final tool result can produce a degenerate state where the model essentially "gives up" and returns end_turn with empty content. There is no public documentation of this exact failure but it is reproducible in our environment and is the same family as the non-determinism captured in `memory/project_sonnet_46_scanner_nondeterminism.md`.

It is **not** a stall in the original 9-0 sense (text-only narration mid-task). It is a context-bloat-induced empty turn.

---

## Why this happened — root causes

### 1. Tool contract: `fetch_url` returns raw response bodies into the conversation

`AgentRun.Umbraco/Tools/FetchUrlTool.cs` returns the HTTP response body as a string. The engine wraps that string into a `tool_result` block and appends it to the conversation. There is no preprocessing, no offloading, no on-disk staging. Whatever the page returned (up to the configured `max_response_bytes` ceiling from Story 9-6) is now permanently in the model's working memory for the rest of the step.

For a single small page this is fine. For a multi-fetch workflow against real-world pages it is a reliability cliff.

### 2. Default `max_response_bytes` is generous

Story 9-6 made the limit configurable but the default ceiling is high enough to admit a 1.39 MB BBC News page in full. The limit is correct as a *safety bound* against runaway responses, but it is the wrong knob for managing conversation size — even at 100 KB per page, four fetches puts 400 KB in context, which is still pathological for a workflow that intends to scale.

### 3. No prompt caching at the engine layer

`AgentRun.Umbraco/Engine/ToolLoop.cs` uses `Microsoft.Extensions.AI.IChatClient` to call providers. M.E.AI does not currently expose Anthropic's prompt caching headers. Every turn re-tokenises and re-bills the entire conversation. Even if the tool contract were unchanged, prompt caching alone would substantially mitigate the cost (and possibly the reliability) of large repeated context — it is not a fix for the failure mode but it amplifies the cost of the bug.

### 4. Tool contract was never stress-tested with real-world pages

`FetchUrlTool` was built generically in Story 5-3 before the example workflow existed. Its tests use small mock responses. The first time it ran against multi-page real content was Story 9-1c's manual E2E — tonight. The bug has been latent in the codebase since Epic 5 and surfaced only when the example workflow exercised it as designed.

---

## What is NOT the cause (and how we know)

- **Not the prompt.** Conversation log proves Sonnet 4.6 followed every invariant in `scanner.md` exactly. Sequential fetches, in-turn progress markers, no parallel batching. The prompt did its job.
- **Not parallel tool calls.** Earlier in the same evening (instance `c6072f5f569948e69c47e9becbe67bd4`) Sonnet 4.6 *did* batch fetches in parallel. The new sequential-fetch invariant in `scanner.md` eliminated that pattern. The current failure happens despite — not because of — sequential fetching.
- **Not the engine's stall detector.** Story 9-0's stall detector is firing correctly. The model genuinely produced an empty turn after the tool result. 9-0 is doing its job.
- **Not Microsoft.Extensions.AI's tool/message translation.** The conversation log shows the tool calls and results going through cleanly. M.E.AI's lack of prompt caching is a contributing cost factor but is not the *cause* of the empty turn.
- **Not the model "being lazy".** Sonnet 4.6 followed five increasingly explicit prompt rules across the night. This is a degenerate state, not a motivation problem.

---

## The fix — proposed

### Pattern: tool-result offloading (the "scratchpad" / "file-handle" pattern)

`fetch_url` should not return raw response bodies into the conversation. It should return a **small structured handle** and write any large payload to a workspace-scoped path that the agent can read on demand.

Two viable shapes — they are not mutually exclusive.

#### Option 1 — Offloaded raw response (minimal change, generic)

```
fetch_url(url) →
  - Fetches as today (HttpClient, SSRF protection unchanged, truncation unchanged)
  - Writes the raw response body to a workspace scratch path:
      _scratch/fetch-{instanceId}/{sha1(url)}.html
  - Returns a structured handle:
      {
        "url": "https://...",
        "status": 200,
        "content_type": "text/html",
        "size_bytes": 1394712,
        "saved_to": "_scratch/fetch-abc123.html",
        "truncated": false
      }
```

The model now sees ~200 bytes per fetch instead of 1.39 MB. If a downstream step actually needs to look at the content, it calls `read_file` on the returned path — and that read can be sliced (`read_file(path, offset, length)`) so even then context stays bounded.

**Pros:** Generic. Works for any future workflow that fetches large content. Preserves SSRF, truncation, and cancellation behaviour unchanged. Backwards compatibility is a non-issue because we own the only consumer (the example workflow under TestSite).

**Cons:** The model still needs to *parse HTML* if a downstream step wants structured fields. In the scanner case it would have to call `read_file` and then mentally extract title/headings/etc., which is exactly the work that just hallucinated under context bloat.

#### Option 2 — Server-side extraction (more aggressive, scanner-specific)

Add a second tool — or a mode on `fetch_url` — that extracts structured content server-side using a real HTML parser (`AngleSharp` or `HtmlAgilityPack`, both well-trodden in .NET) and returns *only the fields the scanner needs*:

```
fetch_url_structured(url) →
  - Fetches the page (same SSRF / truncation / cancellation as today)
  - Parses HTML server-side
  - Returns:
      {
        "url": "...",
        "status": 200,
        "title": "BBC News - Home",
        "meta_description": "...",
        "headings": { "h1": [...], "h2": [...], "h3-h6_count": 47 },
        "word_count": 2341,
        "images": { "total": 84, "with_alt": 79, "missing_alt": 5 },
        "links": { "internal": 312, "external": 89 },
        "truncated": false
      }
```

**Pros:** Context cost per page collapses to ~1–2 KB. The model never sees raw HTML. Eliminates the entire class of "model hallucinated headings because it couldn't re-parse the truncated HTML correctly" bugs that Story 9-1b would otherwise have to police via prompt iteration. The scanner.md prompt becomes dramatically simpler.

**Cons:** Less generic — the structured output is opinionated about what an audit needs. Future non-audit workflows might want different fields. Mitigation: keep `fetch_url` (Option 1 form) for general use, and add `fetch_url_structured` as a sibling tool optimised for the audit pattern. Future workflow types can grow their own specialised tools or fall back to the generic one.

### Recommendation

**Both, in two passes:**

1. **Immediate (this story or its successor):** Implement Option 1. It is the smaller change, fully generic, fixes the *reliability* bug for every workflow, and unblocks Story 9-1c's manual E2E. Estimated scope: ~150–250 lines C#, ~50 lines test, 1 prompt update in `scanner.md`.

2. **Story 9-1b scope adjustment:** Implement Option 2 as part of the trustworthiness gate work. Server-side extraction lets the scanner agent stop guessing at HTML structure entirely and is the most direct route to "outputs trustworthy enough to send a paying client" (the trustworthiness gate). Estimated scope: ~200–300 lines C# (parser + tests), 1 new tool registration, scanner.md rewrite (much shorter), workflow.yaml update.

If only one of the two ships before private beta, **ship Option 1**. It is the bug fix. Option 2 is the quality improvement.

---

## What this means for Story 9-1c

**Story 9-1c cannot move to review as scoped.**

The acceptance criteria explicitly require multi-URL handling (AC #3, AC #6) and the manual E2E task (Task 7.4) requires verifying it. Tonight's evidence is that **the scenario the AC describes is not reliably achievable with the current tool contract, regardless of prompt quality**. The prompt is correct; the tool is the bottleneck.

The story spec is also explicit that engine/tool changes are out of scope (`What NOT to Build → No engine changes. Pure prompt work.`). So 9-1c cannot fix its own blocker without scope drift.

Three viable paths:

- **Path A — Course-correct 9-1c.** Use `bmad-correct-course` to amend 9-1c's scope: prompt-only changes (Tasks 1–6) ship as "done with known limitation"; multi-URL behaviour is explicitly deferred to the story that lands the tool fix. AC #3 and AC #6 get a "blocked on `fetch_url` redesign — see finding doc" annotation. Story moves to review with reduced ACs.

- **Path B — Block 9-1c on the tool fix.** Pull a new story forward (or expand 9-1b) to redesign `fetch_url` (Option 1). Land that. Then resume 9-1c's Task 7 manual E2E against the fixed tool. 9-1c stays in-progress for as long as that takes — probably one focused day plus testing.

- **Path C — Ship 9-1c as-is and accept multi-URL flake.** Mark 9-1c done. Document the limitation. Rely on retries until 9-1b lands the structural fix. **Not recommended** — the trustworthiness gate is the whole point of the example workflow, and shipping a known-flaky example to private beta invitees undermines it before they have run it once.

**Recommended path: B**, but only because the underlying fix is small (~1 day) and the alternative is shipping a workflow that fails on the very test scenario the story spec requires. Path A is acceptable if there is calendar pressure to ship 9-1c this week and the 9-1b expansion is genuinely ready to inherit the work.

---

## Recommendations for the architect

1. **Decide path A vs path B** (or hybrid: A for 9-1c housekeeping, B for the actual fix).
2. **Approve Option 1** (offloaded raw response) as the immediate tool contract change. Confirm the workspace scratch path strategy — recommended `_scratch/fetch-{instanceId}/{sha1(url)}.html` to scope per instance and prevent collisions, with cleanup on instance completion.
3. **Decide on Option 2** (server-side extraction). Recommend including in 9-1b's revised scope so the trustworthiness gate work is on the new contract from day one rather than re-iterating prompts on the old one.
4. **Update Story 9-1b's scope** if Option 2 is approved. Current spec is "iterate prompts until trustworthy"; the evidence says that is the wrong lever. Real scope is "redesign tool contract, then iterate prompts against the new contract."
5. **Decide on prompt caching as a follow-up.** Not in scope for the immediate fix, but worth scheduling — likely Epic 11 territory. M.E.AI does not currently expose Anthropic prompt caching; either contribute upstream, drop to native `Anthropic.SDK` for high-load steps via a thin escape hatch, or accept the cost.
6. **Backfill `FetchUrlTool` stress tests.** The bug went latent from Epic 5 to Epic 9 because tests used small mock responses. Add tests against representative real-world payloads (≥500 KB) before the redesign so the new contract is regression-protected.

---

## Appendix — files referenced

- **Failing instance log:** `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/caed201cbc5d4a9eb6a68f1ff6aafb06/conversation-scanner.jsonl`
- **Tool implementation:** `AgentRun.Umbraco/Tools/FetchUrlTool.cs`
- **Tool loop / stall detector:** `AgentRun.Umbraco/Engine/ToolLoop.cs`
- **Scanner agent prompt:** `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`
- **Story 9-1c spec:** `_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md`
- **Story 9-1b epic spec:** `_bmad-output/planning-artifacts/epics.md` lines 1295–1366
- **Memory note (Sonnet 4.6 nondeterminism):** `~/.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/project_sonnet_46_scanner_nondeterminism.md`
