# Phase 1 Stall — Investigation Report for Architect Review

**Date:** 2026-04-09 (morning)
**Author:** Amelia (dev agent)
**Audience:** Winston (architect)
**Story:** 9.1b — Content Quality Audit Polish & Quality
**Status:** Investigation only — no code changes pending architect ruling
**Branch:** `main` @ `e904050` (engine stall-recovery carve-out), tests 442/442 green, working tree clean

## The problem in one paragraph

CQA scanner step deterministically stalls after the final `fetch_url` in a multi-URL batch on Sonnet 4.6. Verified at N=4 and N=5 (stalls), N=1 (succeeds). Threshold is somewhere in 2–4. The model fetches all URLs cleanly, receives the structured handle results correctly, then emits an empty assistant turn instead of `write_file`. The Story 9.0 stall recovery cannot help (completion check fails because the file doesn't exist). The Phase 1 carve-out engine nudge fires correctly, the model receives the synthetic "your task isn't complete" user message, and **still** produces an empty turn — confirming the model is genuinely stuck, not just confused about what to do next.

## What we know works (proven)

- 1-URL fetch → write_file → success path: **proven on every test run**.
- AngleSharp structured extraction at all sizes (100kb, 207kb, 1mb fixtures, real Wikipedia/BBC/Umbraco/Cogworks pages): **proven**.
- SSRF closure including manual redirect loop: **proven**.
- User-Agent default unblocking Wikipedia 403: **proven**.
- The 8 P1–P8 patches from code review: **proven, all tested**.
- Engine stall-recovery nudge logic: **proven** (it correctly fires, correctly logs, correctly throws on second-stall, generic across workflows).

## What does NOT work

- 4-URL or 5-URL batch ending with `write_file`. Deterministic stall after the final fetch on Sonnet 4.6.

## What we tried that did NOT fix the stall

| Attempt | Commit | Outcome |
|---|---|---|
| Phase 2 pass #1: drop `read_file` mention from Invariant #4 (intent: model was paralysed by stale instruction) | `55a7472` | Didn't help. Model still stalled in identical position. Hypothesis ruled out. |
| Phase 2 pass #2: explicit `## Your Task` section at top of prompt with task framing instead of pure prohibitions (intent: model anchoring on prohibitions instead of task) | `f4088b1` | Didn't help. Model still stalled in identical position. Hypothesis ruled out. |
| Phase 1 engine carve-out: synthetic user nudge after empty stall (intent: give the model an explicit recovery prompt) | `e904050` | Engine fix worked exactly as designed (nudge fired, recovery path tested). **Model received the nudge and produced another empty response.** This is the crucial diagnostic — the stuck-ness is not a "missed instruction" problem, it's a deeper model state issue. |

## The historical regression — root-cause hypothesis

Adam noted overnight that the **original CQA design (Story 9.7 era)** had a per-fetch file operation rhythm: after every `fetch_url` the agent called `read_file` against the cached HTML, then moved to the next fetch. The tool sequence looked like:

```
fetch_url → read_file → fetch_url → read_file → fetch_url → read_file → … → write_file
```

**11 alternating tool calls for a 5-URL batch.**

**Story 9.1b** eliminated `read_file` against cached HTML because that was causing a *different* context-bloat problem (the 9.7 addendum architectural finding — `read_file` brought the full HTML back into context). The fix was to move parsing server-side via `fetch_url(extract: "structured")`. The new shape is:

```
fetch_url → fetch_url → fetch_url → fetch_url → fetch_url → write_file
```

**6 tool calls, all fetches in a row, single write at the end.**

We **deliberately** removed the bloat-causing `read_file` step. We **accidentally** removed the interleaved-tool rhythm as a side effect. Sonnet 4.6 tolerated 11 alternating tool calls fine in 9.7. It's stalling at 5 homogeneous fetches in 9.1b. **Total tool count went down but homogeneity went up.**

The hypothesis Adam wants validated with the architect:

> The interleaved-tool rhythm was load-bearing for agent behaviour, not just for context bloat. We preserved the bloat fix and accidentally regressed the rhythm. Sonnet 4.6's stall is the symptom; the regression is the cause.

**Supporting evidence:**

1. The 1-URL canary works → the fetch→write transition itself is fine at minimum scale.
2. The threshold is small (somewhere in 2–4 fetches) → it's not a context-window issue, it's a turn-pattern issue.
3. Two prompt-side rewordings (clearer instructions, explicit task statement) didn't move the needle → the model isn't confused about what to do, it's *stuck*.
4. The engine nudge ALSO didn't move the needle → the model isn't even able to act on a direct external prompt to continue. This rules out "model just needs reminding."
5. Original 9.7-era CQA worked reliably with the same 5-URL batch (confirmed by Adam's recollection of the original design behaviour).

## Other hypotheses to consider before committing to the rhythm-regression theory

1. **Context size at the stall point.** Even though the structured handles are small (~700 bytes each), 5 of them plus the system prompt plus the conversation history plus the assistant progress markers might still be hitting some specific Sonnet 4.6 inflection point. A measurement run that logged total token count at each turn would prove or disprove. Quick to do; we haven't done it.
2. **Anthropic API / streaming layer issue.** The stalls all show "empty turn" — model returns no text, no tool call. Worth checking whether the stop reason in the API response is `end_turn`, `tool_use`, `max_tokens`, or something unusual. Would need to add temporary engine instrumentation. We haven't checked.
3. **Sonnet 4.6 model regression specifically.** Project memory already records that "Sonnet 4.6 scanner non-determinism" was the original justification for Story 9.1b. Tonight's tests established the stall is *deterministic*, not flaky — so the original "non-determinism" framing may have been incomplete and the underlying issue may be a Sonnet 4.6 batch-handling regression that needs to be reported to Anthropic regardless of how we work around it locally.
4. **Tool result message shape.** The structured handle JSON we return might have a specific shape Sonnet 4.6 doesn't terminate cleanly on. Worth diffing against the 9.7-era raw-mode handle (`{url, status, content_type, size_bytes, saved_to, truncated}`) to see if anything changed beyond the field set.

## The proposed fix (NOT applied yet, awaiting architect sign-off)

**Restore the per-fetch file-operation rhythm by interleaving `write_file` calls between fetches.**

After every `fetch_url`, the agent immediately calls `write_file` to append the latest result into `artifacts/scan-results.md`. The file grows cumulatively across the batch. The final state of the file is identical to today's design — same template, same content — so **the analyser and reporter agents downstream require no changes**, and the workflow.yaml `requires` contract is preserved.

The tool sequence becomes:

```
fetch_url URL1 → write_file (1 page in file)
fetch_url URL2 → write_file (2 pages in file)
fetch_url URL3 → write_file (3 pages in file)
fetch_url URL4 → write_file (4 pages in file)
fetch_url URL5 → write_file (5 pages in file)
```

**10 tool calls, alternating fetch/write.** Every step looks like the 1-URL canary that already works.

### Why this should work

- Mirrors the proven-working 9.7-era rhythm at the *behavioural* level (interleaved tool variety) without re-introducing the *structural* problem (read_file bringing HTML back into context).
- The agent never has more than one fetch result in flight at a time before the next file operation.
- The "post-fetch → write_file" instruction becomes the dominant pattern in the prompt instead of the rare exception. The model is being told to do the same shape of thing on every step.
- Every individual fetch+write pair is shape-equivalent to the 1-URL run, and we have empirical proof the 1-URL run succeeds reliably.

### Why this is *not* obviously a workaround

- It's restoring a design property the original workflow had on purpose. Per Adam's note: this was a documented requirement when CQA was first designed, not an emergent property.
- The cumulative-write pattern is also a *content safety* improvement: if the workflow crashes at fetch #4, the user still has `scan-results.md` with 3 valid pages on disk. Today, a crash at fetch #4 loses all the work because nothing has been written yet.
- It moves work earlier and reduces blast radius. Aligns with the "write to disk often, clear context fast" principle in Adam's original design philosophy.

### Risks / things to think through with architect

1. **`write_file` cost.** Calling it 5 times instead of once means 5 disk writes and 5 LLM-generated prompt expansions. Marginal; the file is small. Unlikely to matter.
2. **Atomic-write vs append.** Easiest is full overwrite each time (the agent rewrites the whole file with all entries so far). Simpler than append semantics, no race conditions, file always reflects exactly what the agent thinks the state is. The disk write cost is trivial at this file size.
3. **Prompt complexity.** scanner.md needs reworking. Specifically: Invariant #4 changes from "after the LAST fetch → write_file" to "after EVERY fetch → write_file". Some of the existing prose becomes obsolete (e.g. the "Reminder — Post-fetch invariant" paragraph). The five hard invariants count goes from 5 to either 4 or 5 depending on whether we keep the post-fetch invariant in a different shape. **This is a verbatim-lock change** — the architect explicitly gated invariant rewordings on his approval, and pass #1 (which only relaxed the read_file mention) was already a Phase 2 polish change. A bigger restructure of Invariant #4 needs the same gate.
4. **Failure handling.** Today, a failed fetch is recorded "after the batch" in the failed-URLs section of the final write. With per-fetch writes, the agent has to integrate failures into the cumulative file as it goes. Slightly more complex agent logic but matches how a human would naturally work.
5. **Atomicity of "success" semantics.** Today, a step is "complete" iff `scan-results.md` exists with all entries. With per-fetch writes, the file exists from fetch #1 onwards but is incomplete until fetch #N. The completion check at the workflow level still works (the file exists when the step completes) but the *meaning* of "file exists" changes. Worth a note in the spec.
6. **Phase 2 polish budget impact.** Pass #1 and #2 (verbatim-locked invariant rewordings) are already consumed. This restructure would be either pass #3 (if it's framed as a continuation of the polish loop) or a new Phase 1 carve-out (if the architect agrees the rhythm regression is an architectural finding, not a polish target). My instinct is **architect-finding → Phase 1 carve-out**, because:
   - It's a structural workflow change, not prompt-iteration.
   - The diagnosis is rooted in a regression introduced by Story 9.1b's own scope, not in test-input variability.
   - Phase 2's job is "iterate the prompt against real test inputs to improve trustworthiness." This is "fix a structural shape regression that broke the workflow at any input size." Different surface.

## Recommended next move

**Architect (Winston) reviews this report and rules on:**

1. **Is the rhythm-regression hypothesis correct, or is one of the alternative hypotheses (context size, API stop_reason, model regression, tool result shape) more likely?** If a cheaper diagnostic would prove or disprove the hypothesis before we commit to the restructure, it's worth running first (e.g. logging stop_reason from the API response — that's ~5 lines of engine instrumentation and would conclusively distinguish "model decided it was done" from "model genuinely stalled mid-thought").
2. **If the restructure is the right call, should it be classified as a Phase 1 architectural carve-out or a Phase 2 polish pass?** This affects budget tracking and the spec change-log narrative.
3. **What's the verbatim-lock disposition for Invariant #4?** Pass #1 already touched it (with architect-escalation approval). Restructuring it again — and possibly removing it entirely in favour of "after every fetch, write_file" — needs the same explicit approval per Locked Decision #9 / AC #10.
4. **Is the per-fetch write cumulative-overwrite shape correct, or is there a better option (e.g. one file per page in a directory, then a roll-up step at the end)?** The directory-per-page option is closer to Adam's original recollection but adds workflow complexity. Cumulative-overwrite-into-one-file is simpler and preserves the downstream contract. Architect call.
5. **Are there any other alternative hypotheses worth eliminating before committing to this fix?** I have not run a model swap test (Sonnet 4.6 → Sonnet 4.5 / Opus 4.6). That test would take 5 minutes and could conclusively prove or disprove "this is a Sonnet 4.6 specific quirk." Worth doing before the architect review even if the architectural finding stands either way.

## Current branch state

- Branch: `main`
- Last commit: `e904050` (engine stall-recovery carve-out)
- Tests: 442/442 green
- Working tree: clean (no uncommitted changes)
- Phase 2 polish budget: 2 of 5 consumed
- Phase 1 manual E2E gate: still failing on 4-URL and 5-URL batches; passing on 1-URL

**Nothing changed since "stop" was called last night.** No code modified this morning. Awaiting architect input.

---

## Postscript — Resolution (2026-04-09 09:23)

**Root cause was `MaxOutputTokens` unset on `ChatOptions`, defaulting to ~4096 from the M.E.AI Anthropic provider.** Amelia's rhythm-regression hypothesis (and Winston's instruction-anchoring counter-hypothesis) were both wrong. The diagnostic gate caught it.

### How we got the answer

Winston's pre-commit ruling required adding 5 lines of `FinishReason` instrumentation to `ToolLoop.cs` BEFORE committing to any of the three hypothesised fixes. The first run with the instrumentation captured the smoking gun:

```
engine.empty_turn.finish_reason: length (accumulatedTextLength=0, updateCount=12)
engine.empty_turn.finish_reason: length (accumulatedTextLength=0, updateCount=11)  ← post-nudge
```

`FinishReason: length` = the model hit its `max_tokens` output ceiling and was truncated by the API mid-thought. With `accumulatedTextLength=0`, that means Sonnet 4.6 with thinking enabled consumed the entire output budget on internal thinking tokens before any visible response tokens were produced.

This explains every observed symptom perfectly:
- **1 URL works:** small write_file content, fits under the limit.
- **3 URLs sometimes works, sometimes not:** the limit is being approached but variance in thinking-token consumption decides whether the cap is crossed.
- **4 and 5 URLs always fail:** the limit is consistently exceeded.
- **Engine nudge didn't help:** the model wasn't stuck — it was generating, hitting the cap, being truncated, and the nudge gave it another turn to do the same thing and hit the same cap.
- **No prompt rewording helped:** the prompt was fine. The plumbing was strangling the model.

### The fix

Single line on [StepExecutor.cs:160](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L160):

```csharp
var chatOptions = new ChatOptions
{
    Tools = aiTools,
    MaxOutputTokens = 32768
};
```

Sonnet 4.6 documented max output is 64k. 32k leaves comfortable headroom for thinking-token bursts plus a multi-page markdown write while still bounded enough to prevent runaway generation. Architect chose 32k over Amelia's 16k recommendation for the headroom.

### Verification gate

5-URL batch on Sonnet 4.6 (`umbraco.com`, `wearecogworks.com`, `umbraco.com/products/umbraco-cms`, `bbc.co.uk/news`, `wikipedia.org/wiki/Content_management_system`) — the same batch that deterministically stalled on the previous 4 attempts — **passed cleanly on the first run after the fix landed**. write_file fired, scan-results.md written, step Complete, workflow advanced to step 2.

The post-write-file empty turn now reports `FinishReason: stop (1217 chars, 24 updates)` — the model voluntarily completing its turn after a real visible response, exactly what the success path looks like.

### What this teaches

The reusable lesson Winston wanted captured in the change-log:

> When you have multiple plausible hypotheses and a 5-line diagnostic can partition them, run the diagnostic before you commit to a fix. Always. Even when — especially when — you're under time pressure and the appealing narrative is right there.

We were 30 minutes from shipping a fix (the per-fetch write_file rhythm restructure) that would have *appeared* to work — because smaller per-turn outputs would have dodged the cap as a side effect — and reintroduced the underlying bug elsewhere later, almost certainly at a worse moment. The diagnostic gate paid for itself many times over.

The class of failure (silent truncation presenting as "model stalled") is nearly invisible without instrumentation and trivially diagnosable with it. Per Winston's ruling, the diagnostic logging has been promoted from temporary scaffolding to a permanent structured engine telemetry event (`engine.empty_turn.finish_reason`) and stays in the codebase as protection against the next time a `MaxOutputTokens` ceiling bites a different workflow.

### Follow-ups filed

- **Story 9.6.1** — Wire `MaxOutputTokens` through `IToolLimitResolver` for per-step / per-workflow override (replaces the hardcoded literal). Filed in `deferred-work.md`.
- **Backlog: CQA scanner incremental writes for crash-resilience.** The rhythm restructure that we did NOT ship is still a real durability improvement for an unrelated reason (crash at fetch #4 of 5 should not lose pages 1-3). Filed in `deferred-work.md` as a future polish item with a motivation that doesn't depend on the stall narrative.

### Final state

- Branch: `main`
- Commit: pending — about to commit as Phase 1 carve-out #3
- Tests: 442/442 green
- Phase 1 manual E2E gate (Task 8): **PASSED on the 5-URL batch** — story can advance from `review` to `done` once Adam ratifies. Phase 2 polish loop budget remains 2 of 5 consumed and is **not retroactively recovered** — passes #1 and #2 (verbatim invariant rewordings) stay applied because the rewordings are still independently correct. The polish loop's job for the rest of 9.1b is now genuine trustworthiness iteration against more test inputs, not stall hunting.
- Diagnostic logging: promoted to permanent structured event `engine.empty_turn.finish_reason`.
