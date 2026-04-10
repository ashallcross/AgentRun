# Bug Finding: Instance resume after step completion fails

**Found:** 2026-04-10 during Story 9.1c Task 7.4 manual E2E
**Found by:** Adam (manual exploration after completing Task 7.4)
**Severity:** Medium — affects basic UX of the workflow runner; not a beta blocker but should be fixed before public 1.0
**Sprint impact:** None on 9.1c. 9.1c Task 7 manual E2E continues as planned.

## Symptom

After completing Task 7.4 (5-URL multi-fetch run, instance `f95d49e1ca1c485faeb79313d453e958`), Adam navigated away from the instance and then returned to it via the instance list. Attempting to send any chat message — `ok whats next`, `hello`, `hi` — produced the UI error **"Failed to send message. Try again."** for every send.

The instance had already completed the scanner step at the time he left:

```
[00:37:25] Updated step scanner (index 0) to Complete for instance f95d49e1ca1c485faeb79313d453e958
[00:37:25] Step scanner completed for workflow content-quality-audit instance f95d49e1ca1c485faeb79313d453e958
[00:37:25] Advanced CurrentStepIndex to 1 for instance f95d49e1ca1c485faeb79313d453e958
```

The orchestrator advanced `CurrentStepIndex` to 1 (the analyser) but the analyser step never visibly started — the UI on resume showed the scanner's chat history (post-write summary) and offered a chat input that did not deliver messages.

## Expected behaviour

Re-entering an in-progress instance should resume the workflow from wherever it was, with the correct active step in focus:

- If the active step is **interactive** (e.g. scanner waiting for URLs), the chat input should post into that step and the conversation continues.
- If the active step is **non-interactive** (e.g. analyser/reporter, which don't ask the user anything), the orchestrator should automatically run that step on instance reopen and surface its progress, or — if it has already completed in the background — show the next active step.
- If the entire workflow has completed, the instance should display its final artifacts and the chat input should be hidden or clearly marked read-only.

## Actual behaviour

Chat input is still rendered, but `Failed to send message. Try again.` fires on every send attempt. No state advances. No telemetry warning in the engine log when Adam tried to send messages — the failures appear to be UI-side, not engine-side.

## Hypothesis (un-verified — needs investigation)

Two distinct things may be wrong:

1. **Workflow advancement is paused on instance suspend.** The scanner completed and `CurrentStepIndex` advanced, but the analyser was never executed because the orchestrator only runs steps while the instance is "active" in the UI. When Adam returned, the orchestrator did not pick up the analyser step.
2. **Chat surface posts to the wrong step.** When Adam reopened the instance, the chat UI may have been wired to a completed step (scanner index 0) rather than the active step (analyser index 1). Sending a message into a completed step's input is rejected, hence "Failed to send message" with no engine log entry.

Both are plausible. Either could exist independently of the other. Needs a code-path trace through the UI's instance-load handler and the orchestrator's step-advancement loop on instance resume.

## Reproduction steps

1. Start a Content Quality Audit instance.
2. Paste 5 URLs and let the scanner run to completion (until it produces the post-write summary).
3. Navigate away from the instance via the back button or the instance list.
4. Reopen the same instance from the instance list.
5. Try to send any chat message.
6. Observe: `Failed to send message. Try again.`

## Sprint placement

- **Not** a 9.1c blocker. 9.1c is about scanner first-run UX; this bug is about instance lifecycle on resume.
- **Not** a 9.1b regression. 9.1b's structured-extraction work is unrelated to instance lifecycle.
- Candidate epic: Epic 10 (Ship Readiness & Public Launch) or Epic 8 retrospective follow-up. Suggest a new story like **10.X — Instance resume after step completion**.
- Severity: Medium. Affects basic UX expectations ("I left and came back") but does not block any happy path that runs to completion in one sitting. Should be fixed before public 1.0; safe to ship private beta with this open if it's documented.

## Files to look at first

- The UI instance-detail component (chat input wiring + step focus on instance load)
- The orchestrator's instance-resume / step-advancement loop
- Anywhere the UI determines "which step's chat is this"

## Out of scope for this finding

- Designing the fix. Just capturing the symptom and the hypothesis.
- Reproducing across all step types — only verified on the scanner → analyser handoff in the CQA workflow.
