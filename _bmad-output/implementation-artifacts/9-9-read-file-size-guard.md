# Story 9.9: `read_file` Size Guard with Truncation Marker — BETA BLOCKER

Status: done

## Dev Agent Record

### Completion Notes (2026-04-08, Amelia)

Tasks 1–4 + 6 implemented in a single pass. Task 5 (manual E2E) is the gate Adam owns and remains unchecked.

- **Resolution chain (Task 1):** Mechanically copy-pasted Story 9.6's `fetch_url` shape. New `EngineDefaults.ReadFileMaxResponseBytes = 262_144`. New `ReadFile` sub-records on `AgentRunToolDefaultsOptions`, `AgentRunToolLimitsOptions`, and `ToolDefaultsConfig` (the shared workflow/step DTO). New `ResolveReadFileMaxResponseBytes` on `IToolLimitResolver` + `ToolLimitResolver` reusing the existing `ResolveCore` helper and `SafeOptions()` (so the malformed-appsettings case is handled for free, Edge Case #7). `WorkflowValidator.AllowedToolSettings` extended with `read_file → { max_response_bytes }` so YAML deny-by-default still applies. `WorkflowValidator.EnforceCeilings` extended for both workflow- and step-level `read_file.max_response_bytes`.
- **Size guard (Task 2):** `ReadFileTool` now takes `IToolLimitResolver` via constructor injection (DI registration in `AgentRunComposer` is by interface so it picks the new ctor automatically). `ExecuteAsync` now: validates path → `File.Exists` → `FileInfo.Length` (wrapped to surface `IOException`/`UnauthorizedAccessException` as `ToolExecutionException`, Edge Case #2) → `<= limit` returns the existing `File.ReadAllTextAsync` path unchanged → otherwise allocates a `byte[limit]` buffer once and bounded-reads via `FileStream.ReadAsync`, decoding the resulting span as UTF-8 (Edge Case #9: trailing partial codepoint becomes U+FFFD). Edge Case #11 (file shrank between stat and read) is handled per Winston's implementation note: if the actual read is below the limit we decode and return without the marker — no second-roundtrip via `File.ReadAllTextAsync`. Marker text is the verbatim wording from spec section "Architect's locked decisions" point 4.
- **Tests (Task 3 + Task 4):** `ReadFileToolTests.cs` extended with 11 tests covering ACs #1, #2, #3, #4, #7, #8, plus the empty-file edge case, marker placeholder substitution, default-value assertion, and the 100 KB / 1500 KB Story 9.7 fixtures. The pre-9.9 four tests (existing-file regression, missing-file, path-traversal, missing-arg) are preserved verbatim — only their `_tool` constructor was updated to inject the resolver and the context now carries `Step` + `Workflow`. `ToolLimitResolverTests.cs` got the five-test resolution-chain mirror set (engine → site → workflow → step → ceiling). `WorkflowToolDefaultsValidationTests.cs` got five tests covering the new YAML key (accepted, zero/negative rejected, unknown setting rejected, workflow-ceiling violation, step-ceiling violation).
- **Regression suite:** `dotnet test AgentRun.Umbraco.slnx` → **0 failures, 418 passed** (was 396 pre-9.9; net +22 tests).
- **What I did NOT touch:** `FetchUrlTool`, `WriteFileTool`, `ListFilesTool`, `ToolLoop`, agent prompts, workflow YAML files, JSON Schema. Out of scope per spec.
- **Forward dependency flagged:** `read_file.max_response_bytes` is not yet in the workflow JSON Schema. Story 9.2 picks it up.

### File List

**Modified (engine):**
- `AgentRun.Umbraco/Tools/ReadFileTool.cs`
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` (D2 cross-cutting fix — wiring exception type)
- `AgentRun.Umbraco/Engine/EngineDefaults.cs`
- `AgentRun.Umbraco/Engine/IToolLimitResolver.cs`
- `AgentRun.Umbraco/Engine/ToolLimitResolver.cs`
- `AgentRun.Umbraco/Configuration/AgentRunToolDefaultsOptions.cs`
- `AgentRun.Umbraco/Configuration/AgentRunToolLimitsOptions.cs`
- `AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs`
- `AgentRun.Umbraco/Workflows/WorkflowValidator.cs`

**Added (engine):**
- `AgentRun.Umbraco/Engine/ToolContextMissingException.cs` (D2 — typed AgentRunException subtype for tool wiring bugs)

**Modified (tests):**
- `AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs`
- `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` (added `ResolveReadFileMaxResponseBytes` to fake resolver; updated one inline `new ReadFileTool()` to inject the resolver)
- `AgentRun.Umbraco.Tests/Engine/StepExecutorTests.cs` (added `ResolveReadFileMaxResponseBytes` to stub resolver)
- `AgentRun.Umbraco.Tests/Engine/ToolLimitResolverTests.cs`
- `AgentRun.Umbraco.Tests/Workflows/WorkflowToolDefaultsValidationTests.cs`

### Change Log

| Date | Author | Change |
|---|---|---|
| 2026-04-08 | Amelia | Story 9.9 implemented — `read_file` size guard with verbatim truncation marker (256 KB default), resolution-chain integration mirroring Story 9.6, defence-in-depth for tool result offloading. Tasks 1–4 + 6 complete; Task 5 manual E2E deferred to Adam. 0 test failures, 418 passing. |
| 2026-04-08 | Bob + Amelia | **Code review fixes applied (D1, D2, P1-P3).** D1: collapsed to a single bounded-read code path on one `FileStream` handle (no separate `FileInfo.Length` stat) — closes the TOCTOU window where a small file growing past the limit between stat and read could be returned via unbounded `File.ReadAllTextAsync`. Truncation is now detected by a post-loop 1-byte peek, independent of any stat. D2: new `ToolContextMissingException : AgentRunException` replaces raw `InvalidOperationException` for missing `Step`/`Workflow` on the execution context — `LlmErrorClassifier` no longer masks wiring bugs as generic provider errors. Cross-cutting spillover: same fix applied to `FetchUrlTool` (Bob's catch — exact same exposure post-9.6). P1: read path now wraps both `IOException` and `UnauthorizedAccessException` in both branches. P2: added `FileShrinksBetweenStatAndRead_NoMarkerAppended` test (Task 3.6 was previously ticked without an actual test — fixed). P3: latent test bug fixed — `LargeFile_BoundedReadDoesNotAllocateFullFileSize` now asserts UTF-8 byte length, not char index. New regression test `FileGrowsBetweenStatAndRead_StillBoundedAtLimit` guards D1. New test `NullStepOrWorkflow_Throws_ToolContextMissingException` guards D2. **Test count: 421 passing (was 418, +3).** |
| 2026-04-08 | Adam + Amelia | Task 5 manual E2E gate **verified**: scanner workflow against the BBC News cached page (instance `7fb6716bc9e54afcab4c5118c6841d2f`, ~1 MB payload) produced a `read_file` `tool_result` containing the verbatim `Response truncated at 262144 bytes` marker in `conversation-scanner.jsonl`. The 107 KB and 207 KB cached pages were read in full (under the 256 KB default), confirming the forcing-function intent. **The scanner still stalled after the truncated read** — this is the recorded Sonnet 4.6 non-determinism that Story 9.1b owns; per Edge Case #8, agent reaction to the marker is out of scope for 9.9. The size guard itself is working as designed. |

**Depends on:** 9.6 (configurable tool limits resolution chain — done), 9.7 (tool result offloading for `fetch_url` — done 2026-04-08 via DoD amendment; establishes the offloading pattern this story defends in depth)
**Cooperates with:** 9.1b (the primary fix — server-side AngleSharp structured extraction; 9.9 is the defence-in-depth safety net for any future workflow that bypasses the structured tool)
**Blocks:** Story 9.1c's manual E2E gate (per the [9.1c pause addendum](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md), 9.1c resumes only when 9.7 + 9.1b + 9.9 are all on `main`)

> **BETA BLOCKER.** This story is the defence-in-depth half of Option E from Winston's review of [Amelia's read_file context bloat finding](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md). Story 9.7 disciplined the **write end** of tool-result offloading; `fetch_url` now returns a JSON handle < 1 KB and zero raw HTML reaches conversation context via `fetch_url`. Story 9.1b will discipline the **read end** with server-side AngleSharp structured extraction so the agent never has to `read_file` against a multi-MB cached HTML payload at all. **This story is the safety net underneath both of them.** It bounds the per-call byte cost of `read_file` so any future workflow that falls back to raw `read_file` against cached HTML cannot silently re-introduce the same context-bloat failure mode by accident. Ships in parallel with 9.1b before private beta.

## Story

As the AgentRun engine,
I want `read_file` to enforce a configurable per-call byte limit and append a truncation marker when a file exceeds it,
So that no agent can silently dump hundreds of kilobytes of cached HTML (or any other oversized file) into LLM conversation context — preserving the symmetry of the tool-result-offloading pattern that Story 9.7 introduced and Story 9.1b builds on, while leaving the existing happy path for small artifact files (`scan-results.md`, `quality-scores.md`, etc.) completely unchanged.

## Context

**UX Mode: N/A — engine + tool contract change. No UI work.**

**Authorisation:** This story was created on 2026-04-08 (Ceremony 3) via the read-file bloat finding cycle. The full diagnosis lives in [9-7-architectural-finding-read-file-bloat.md](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md). The cooperating ceremony artefacts are the [9.7 DoD amendment SCP](../planning-artifacts/sprint-change-proposal-2026-04-08-9-7-dod-amendment.md) (Ceremony 1) and the [9.1c pause addendum](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md) + [9.1b reshape addendum](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md) (Ceremony 2). Read at minimum the finding report's "Root Causes" section and Option D / Option E descriptions before implementing.

**The architectural principle being enforced (symmetry):**

Tool result offloading as practised by Claude Code, Anthropic Computer Use, and the Claude Agent SDK assumes the model never needs to read offloaded content in full. The model's working memory stays bounded because the **consuming side is also constrained**. Story 9.7 disciplined the producer (`fetch_url`); Story 9.1b will give the agent a structured-extraction lever so it never needs to consume raw HTML at all. Story 9.9 is the *safety net*: it makes the consuming side (`read_file`) structurally incapable of re-introducing the failure mode even if a future workflow author or future agent prompt regresses. Without 9.9, the next time someone adds a workflow that falls back to raw `read_file` against a cached payload, the bug Story 9.7 was built to fix reappears silently. With 9.9, the worst case is a truncation marker the agent can see and react to.

**The bug being defended in depth:**

In instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8` (2026-04-08), three sequential `read_file` calls against cached pages of ~107 KB / ~207 KB / ~1 MB put **~1.34 MB of raw HTML** into Sonnet 4.6's conversation context. The model produced an empty assistant turn, identical signature to the original `caed201cbc5d4a9eb6a68f1ff6aafb06` failure that motivated Story 9.7. Story 9.1b's structured-extraction lever will make this scenario impossible by removing the agent's reason to call raw `read_file` at all. Story 9.9 makes it impossible *by contract* even if a future workflow tries.

**Why a hard cap and not chunking / sliced reads / a throw:**

- **Chunking / sliced reads (Option B from the finding):** explicitly rejected by Winston. Pushes massive prompt complexity onto every workflow that consumes large files. Generic on the surface, but in practice each workflow has to know how to slice, where to slice, what to remember between slices, and how to compose. That is the same "iterate the prompt against a hostile contract" trap Story 9.7 was built to avoid.
- **Throw on overflow:** rejected. A throw is a bug in the workflow author's eyes, not an actionable signal. Truncation with a marker tells the agent (and the workflow author) exactly what happened and how to fix it.
- **Hard truncation with a mandatory marker:** mirrors `fetch_url`'s existing truncation pattern (Story 9.6's `max_response_bytes` ceiling combined with the truncation marker). The same shape, the same agent-visible signal, the same configurability via the resolution chain. Symmetry at the contract level as well as the architectural level.

### Architect's locked decisions (do not relitigate)

Locked by Winston in the 2026-04-08 review of Amelia's read_file bloat finding. If the dev agent finds a real ambiguity (not a relitigation), stop and ask Adam.

1. **Pattern: hard truncation with mandatory marker.** NOT chunking, NOT sliced reads, NOT throw-on-overflow. Mirror `fetch_url`'s existing truncation pattern exactly.
2. **Resolution chain reuses Story 9.6's `ToolLimitResolver`.** New tunable: `read_file.max_response_bytes`. Same step → workflow → site default → engine default chain. Same site-level hard cap via `AgentRun:ToolLimits:ReadFile:MaxResponseBytesCeiling`.
3. **Default value: 256 KB (262144 bytes).** Deliberately conservative — admits typical artifact files (~10 KB to ~100 KB) without truncation, forces truncation on cached HTML pages (typically 100 KB+). The forcing function is intentional.
4. **Truncation marker text — locked verbatim:**

   ```
   [Response truncated at {limit} bytes — full file is {totalBytes} bytes. Use a structured extraction tool (e.g. fetch_url with extract: "structured" once Story 9.1b ships) or override read_file.max_response_bytes in your workflow configuration to read the rest.]
   ```

5. **Size check BEFORE the read.** Use `new FileInfo(canonicalPath).Length` (cheap, no contents read). If under limit → existing read path unchanged. If over limit → bounded read via `FileStream.ReadAsync` with a `byte[limit]` buffer. **Do NOT allocate the full file.**
6. **No agent-side `max_bytes` parameter.** Workflow-author-configured only. The agent does not get to override the cap.
7. **No automatic chunking, streaming, or partial-read semantics.** Truncation is final per call.
8. **Existing behaviour for files under the limit is preserved unchanged.** Guard is purely additive — same `string` return type, same `File.Exists` behaviour, same path validation, same encoding.
9. **`PathSandbox.ValidatePath` continues unchanged.** Size guard layers on top of (not in place of) the existing validation step.
10. **Pre-implementation architect review gate** — Winston explicitly asked to eyeball the resolution chain integration and the 256 KB default before code is written. See the gate at the bottom of this file.

## Files in Scope

**Modified (engine source):**

- `AgentRun.Umbraco/Tools/ReadFileTool.cs` — add the size guard. Existing `ExecuteAsync` flow becomes: validate path → `File.Exists` check (unchanged) → resolve effective limit via `IToolLimitResolver` → `new FileInfo(canonicalPath).Length` → if `<= limit` then existing `File.ReadAllTextAsync` path; if `> limit` then bounded `FileStream.ReadAsync` of `limit` bytes + UTF-8 decode + append the verbatim truncation marker. Constructor gains `IToolLimitResolver` dependency (matches `FetchUrlTool`'s pattern post-Story 9.6).
- `AgentRun.Umbraco/Engine/ToolLimitResolver.cs` — add `ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow)` mirroring the existing `ResolveFetchUrlMaxResponseBytes` shape. New options branches: `options.ToolDefaults?.ReadFile?.MaxResponseBytes` (site default) and `options.ToolLimits?.ReadFile?.MaxResponseBytesCeiling` (site ceiling). New `EngineDefaults.ReadFileMaxResponseBytes = 262144`.
- `AgentRun.Umbraco/Engine/IToolLimitResolver.cs` — add the new method to the interface.
- `AgentRun.Umbraco/Configuration/AgentRunOptions.cs` (and any nested option records reached from it — `ToolDefaultsOptions`, `ToolLimitsOptions`) — add `ReadFile` sub-records carrying the new `MaxResponseBytes` and `MaxResponseBytesCeiling` ints. Mirror the existing `FetchUrl` shape exactly.
- `AgentRun.Umbraco/Workflows/StepDefinition.cs` / `WorkflowDefinition.cs` (or wherever `ToolOverrides` and `ToolDefaults` are declared) — add `ReadFile` sub-records carrying the workflow- and step-level `MaxResponseBytes`. Mirror existing `FetchUrl` shape.
- `AgentRun.Umbraco/Workflows/WorkflowValidator.cs` (or wherever 9.6's validation lives) — add validation that any workflow-declared `read_file.max_response_bytes` is positive and does not exceed the site ceiling. Mirror the existing `fetch_url` validation rule. (Mechanically identical to 9.6's pattern — if the validator already iterates a list of known tunables, just add this one to the list.)

**Modified (tests):**

- `AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs` — extend with the new size-guard tests. Existing tests stay green unchanged.
- `AgentRun.Umbraco.Tests/Engine/ToolLimitResolverTests.cs` — add tests for the new resolution chain branch (mirror existing `FetchUrlMaxResponseBytes` test set).
- `AgentRun.Umbraco.Tests/Workflows/WorkflowValidatorTests.cs` (or equivalent) — add validation tests for the new key.

**Modified (config):**

- `AgentRun.Umbraco/appsettings.json` example bindings if there is one alongside the tool — only if 9.6 already added an example block for `FetchUrl`; if so, mirror it for `ReadFile`. Otherwise leave appsettings alone (binding works without an example entry).

**Explicitly NOT touched (out of scope):**

- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` — Story 9.7 territory; do not modify.
- `AgentRun.Umbraco/Tools/WriteFileTool.cs` — out of scope.
- `AgentRun.Umbraco/Tools/ListFilesTool.cs` — out of scope.
- `AgentRun.Umbraco/Engine/ToolLoop.cs` — out of scope.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/**/agents/*.md` — agent prompts unchanged. The marker is self-explanatory in-context; no prompt teaching is needed.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/**/workflow.yaml` — no workflow author needs to opt in for the default to apply.
- The JSON Schema for `workflow.yaml` — flagged as a forward dependency for **Story 9.2** (must add `read_file.max_response_bytes` to the tool overrides schema). **Do NOT block Story 9.9 on this** — Story 9.2 picks it up when it lands.
- Any other tool, any prompt file, any documentation outside `_bmad-output/`.

## Acceptance Criteria

**AC #1 — File under limit reads in full, no marker (regression guard).**

**Given** a file at `artifacts/scan-results.md` of 12 KB
**And** the resolved `read_file.max_response_bytes` for the current step is 262144 (256 KB default)
**When** the agent calls `read_file({"path": "artifacts/scan-results.md"})`
**Then** the tool returns the full file contents as a string
**And** no truncation marker is present
**And** the return value is byte-for-byte identical to what `File.ReadAllTextAsync` would have returned pre-9.9.

**AC #2 — File at exactly the limit reads in full, no marker (limit is inclusive).**

**Given** a file of exactly 262144 bytes
**When** the agent calls `read_file` against it
**Then** the tool returns the full file contents
**And** no truncation marker is present.

**AC #3 — File 1 byte over limit is truncated with marker.**

**Given** a file of 262145 bytes
**When** the agent calls `read_file` against it
**Then** the tool returns a string containing the first 262144 bytes of the file (UTF-8 decoded)
**And** the verbatim truncation marker is appended:
> `[Response truncated at 262144 bytes — full file is 262145 bytes. Use a structured extraction tool (e.g. fetch_url with extract: "structured" once Story 9.1b ships) or override read_file.max_response_bytes in your workflow configuration to read the rest.]`

**AC #4 — File far over limit, bounded read does not allocate the full file.**

**Given** a file of 1,500,000 bytes
**And** the resolved limit is 262144 bytes
**When** the agent calls `read_file` against it
**Then** the bounded read uses a `byte[262144]` buffer (NOT a `byte[1500000]` buffer)
**And** the tool returns 262144 bytes plus the marker
**And** memory profiling (or a code review of the implementation) confirms no allocation proportional to total file size.

**AC #5 — Resolution chain works: step → workflow → site default → engine default → ceiling.**

**Given** the engine default is 262144
**And** site default in `appsettings.json` sets `AgentRun:ToolDefaults:ReadFile:MaxResponseBytes` to 524288
**And** workflow `tool_defaults.read_file.max_response_bytes` is 1048576
**And** step `tool_overrides.read_file.max_response_bytes` is 65536
**When** `IToolLimitResolver.ResolveReadFileMaxResponseBytes(step, workflow)` is called
**Then** the resolved value is 65536 (step wins)
**And** removing the step override resolves to 1048576 (workflow wins)
**And** removing the workflow value resolves to 524288 (site default wins)
**And** removing the site default resolves to 262144 (engine default wins).

**AC #6 — Site-level ceiling enforcement: workflow declares above ceiling → fails workflow load validation.**

**Given** `appsettings.json` sets `AgentRun:ToolLimits:ReadFile:MaxResponseBytesCeiling` to 524288
**And** a workflow declares `tool_defaults.read_file.max_response_bytes: 1048576`
**When** the workflow is loaded by `WorkflowLoader` / `WorkflowValidator`
**Then** loading fails with a clear validation error naming the workflow file, the offending key, the declared value, and the ceiling
**And** the error mirrors the existing Story 9.6 `fetch_url` ceiling violation message shape.

**AC #7 — Existing artifact files (~10 KB scan-results.md) read successfully under the default limit.**

**Given** the default 256 KB limit and an existing in-flight workflow run with a `~10 KB` `scan-results.md`
**When** the analyser step calls `read_file({"path": "artifacts/scan-results.md"})`
**Then** the file is returned in full with no marker
**And** the analyser step's existing behaviour is unchanged.

**AC #8 — Cached HTML file at ~500 KB is truncated at 256 KB with marker (the deliberate forcing function).**

**Given** the captured fixture `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-500kb.html` (~207 KB — see Dev Notes for fixture-size note)
**Or** any synthetic file deliberately sized at ~500 KB
**When** `read_file` is called against it under the default 256 KB limit
**Then** the response is truncated at 262144 bytes
**And** the verbatim marker is appended
**And** the marker correctly reports the actual file size in the `{totalBytes}` placeholder.

**AC #9 — All existing `ReadFileTool` tests still pass (regression guard).**

The full pre-9.9 `ReadFileToolTests.cs` suite passes unchanged. Any existing test that read a small file and asserted the exact returned string still asserts the same exact string.

**AC #10 — Test suite is green.**

`dotnet test AgentRun.Umbraco.slnx` reports 0 failures across all projects.

**AC #11 — Manual E2E gate (deferred to Adam).**

Scanner workflow attempting `read_file` against the ~1 MB BBC News cached page from instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8` returns a string truncated at 262144 bytes with the verbatim marker present. Verified by inspecting the conversation log JSONL. Scanner workflow's other behaviours unaffected.

## What NOT to Build

- **No `offset` / `length` parameters** on `read_file`. Option B from the finding was explicitly rejected.
- **No automatic chunking, streaming, or partial-read semantics.** Truncation is final per call.
- **No agent-side `max_bytes` argument.** The agent does not get to override the cap. Workflow-author-configured only via the resolution chain.
- **No changes to existing path validation, sandbox check, or any other behaviour** for files under the limit. The size guard is the only behavioural change.
- **No changes to `WriteFileTool`, `ListFilesTool`, `FetchUrlTool`** — out of scope.
- **No agent prompt file changes.** This is engine territory only. The marker is self-explanatory in-context.
- **No JSON Schema bundling** for the new `read_file.max_response_bytes` key. **Flag as a forward dependency for Story 9.2** but do **NOT** block 9.9 on it.
- **No "disable size guard" / "unlimited mode" flag.** The ceiling is configurable upward; there is no escape hatch to make it boundless.
- **No new `ToolExecutionException` subtypes.** Existing exception flow is preserved.
- **No retry logic, no resume-from-offset semantics, no stateful read state.** Each `read_file` call is independent.

## Failure & Edge Cases

**Deny-by-default statement:** Unrecognised or unspecified inputs (malformed paths, paths escaping the instance folder, files that do not exist, files with permission errors, files with sizes that fail to compute) MUST be rejected by the existing `PathSandbox.ValidatePath` check or fail loud — never silently coerced into a default, never silently truncated without the marker.

| # | Case | Expected behaviour |
|---|---|---|
| 1 | File does not exist | **Preserve existing behaviour** — `ToolExecutionException("File not found: '{relativePath}'")`. Size guard is never reached. |
| 2 | `FileInfo.Length` permission error (e.g. `UnauthorizedAccessException` from the OS) | Surface as `ToolExecutionException` with the underlying message. Do not silently fall through to a partial read. |
| 3 | Empty file (0 bytes) | Read as today (returns `""`); no marker. Limit comparison is `<=`, so 0 ≤ limit is always true. |
| 4 | Bounded read fails partway through (`IOException` mid-`ReadAsync`) | `ToolExecutionException` wrapping the underlying error. **Do NOT** return a partial truncated string — that would be silent data loss. |
| 5 | Workflow declares `read_file.max_response_bytes: 0` or negative | Validation error at workflow load time, mirroring the existing 9.6 `fetch_url` validation. Workflow does not load. |
| 6 | Workflow declares above the site ceiling | Validation error at workflow load time, mirroring 9.6's pattern. Workflow does not load. |
| 7 | `appsettings.json` malformed (binding throws) | Reuse the existing `SafeOptions()` pattern in `ToolLimitResolver` — log a clear error once at Error level, fall back to engine defaults, do not crash startup. (This is Story 9.6's existing behaviour and applies automatically once `ResolveReadFileMaxResponseBytes` calls `SafeOptions()`.) |
| 8 | Agent ignores the truncation marker and proceeds as if the read was complete | Same architectural limitation as `fetch_url`'s truncation marker. The tool's responsibility ends at surfacing the marker; what the agent does with it is a prompt-design problem, not a tool problem. **Not in scope for 9.9.** |
| 9 | UTF-8 multi-byte sequence split at the truncation boundary | Decode the `byte[limit]` slice as UTF-8 with replacement-character fallback for the trailing partial codepoint (or trim trailing partial bytes before decoding). The marker's mention of "{limit} bytes" makes the byte-level boundary explicit. **Not a correctness bug; document the choice in Dev Notes.** |
| 10 | File grows between `FileInfo.Length` snapshot and the bounded read | Race is acceptable — `read_file` is best-effort against the file system as of the call. The bounded read will return up to `limit` bytes regardless of subsequent growth. The `{totalBytes}` value in the marker is a snapshot, not a guarantee. |
| 11 | File shrinks between `FileInfo.Length` snapshot (above limit) and the bounded read (now below limit) | The bounded read returns whatever is actually present. If the actual read is below the limit, **do not append the marker** — the marker only appears when the read was actually capped. This avoids a false-positive marker. |

## Tasks / Subtasks

### Task 1 — Add `read_file.max_response_bytes` to the resolution chain

- [x] 1.1 Add `EngineDefaults.ReadFileMaxResponseBytes = 262144` constant (or wherever 9.6's `EngineDefaults` live).
- [x] 1.2 Add `ReadFileOptions` (`MaxResponseBytes`) and `ReadFileLimitOptions` (`MaxResponseBytesCeiling`) record types alongside the existing `FetchUrl` equivalents in `AgentRunOptions.cs`. Mirror the existing shape exactly.
- [x] 1.3 Add `ReadFile` sub-record properties to `ToolDefaultsOptions` and `ToolLimitsOptions` so the appsettings binder picks up `AgentRun:ToolDefaults:ReadFile:MaxResponseBytes` and `AgentRun:ToolLimits:ReadFile:MaxResponseBytesCeiling`.
- [x] 1.4 Add equivalent `ReadFile` sub-records to `StepDefinition.ToolOverrides` and `WorkflowDefinition.ToolDefaults` so workflow YAML can declare `tool_overrides.read_file.max_response_bytes` and `tool_defaults.read_file.max_response_bytes`. Mirror the existing `FetchUrl` shape.
- [x] 1.5 Add `int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow)` to `IToolLimitResolver`.
- [x] 1.6 Implement the same in `ToolLimitResolver` using the existing `ResolveCore` helper. Reuse `SafeOptions()` so the malformed-appsettings case is automatically handled.
- [x] 1.7 Add the `read_file.max_response_bytes` validation to `WorkflowValidator` (or wherever 9.6's tool-overrides validation lives) so workflows declaring the key positive-bounded and ceiling-bounded fail loudly at load time.

### Task 2 — Implement the size guard in `ReadFileTool.ExecuteAsync`

- [x] 2.1 Add an `IToolLimitResolver` constructor parameter to `ReadFileTool` (mirroring `FetchUrlTool`'s post-9.6 pattern). Update DI registration if `ReadFileTool` is registered explicitly.
- [x] 2.2 In `ExecuteAsync`, after the existing `ValidatePathSandboxed` + `File.Exists` check and before the read, call `_resolver.ResolveReadFileMaxResponseBytes(context.Step, context.Workflow)` to compute the effective limit. (Confirm that `ToolExecutionContext` exposes `Step` and `Workflow` — if not, follow whatever 9.6 did to plumb them through `FetchUrlTool`.)
- [x] 2.3 Compute `var totalBytes = new FileInfo(canonicalPath).Length;`. Wrap in `try` and surface `UnauthorizedAccessException` / `IOException` as `ToolExecutionException` (Edge Case #2).
- [x] 2.4 If `totalBytes <= limit`, return `await File.ReadAllTextAsync(canonicalPath, cancellationToken)` — the existing path, unchanged. Regression guard for AC #1 / #2 / #7 / #9.
- [x] 2.5 Otherwise (totalBytes > limit), bounded-read path:
  - Allocate `var buffer = new byte[limit];`
  - Open `await using var stream = File.OpenRead(canonicalPath);`
  - Loop `stream.ReadAsync(buffer.AsMemory(read, limit - read), cancellationToken)` until `read == limit` or stream returns 0. Edge Case #11: if the stream returned fewer bytes than `limit` (file shrunk), use the existing `File.ReadAllTextAsync` fall-back path with no marker; otherwise continue.
  - Decode `buffer.AsSpan(0, read)` as UTF-8 with replacement-character fallback (Edge Case #9). Trim any trailing replacement chars caused by mid-codepoint truncation if cleaner — document the choice.
  - Wrap any `IOException` mid-read as `ToolExecutionException` (Edge Case #4) — do not return a partial string.
  - Append the verbatim marker:
    ```
    [Response truncated at {limit} bytes — full file is {totalBytes} bytes. Use a structured extraction tool (e.g. fetch_url with extract: "structured" once Story 9.1b ships) or override read_file.max_response_bytes in your workflow configuration to read the rest.]
    ```
  - Return the decoded string + marker.
- [x] 2.6 Confirm `PathSandbox.ValidatePath` is unchanged. Confirm the `File.Exists` branch is unchanged. Confirm the deny-by-default failure modes (path escape, permission, missing) all hit existing exception paths before the size guard.

### Task 3 — Unit tests for the size guard

Add to `AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs`. Aim for ~6–8 tests:

- [x] 3.1 `FileUnderLimit_ReturnsFullContents_NoMarker` (AC #1)
- [x] 3.2 `FileExactlyAtLimit_ReturnsFullContents_NoMarker` (AC #2)
- [x] 3.3 `FileOneByteOverLimit_TruncatedWithMarker` (AC #3) — assert marker text **verbatim**.
- [x] 3.4 `LargeFile_BoundedReadDoesNotAllocateFullFileSize` (AC #4) — assert via a deterministic test against a 1 MB synthetic file that the buffer used is `byte[limit]`. Either via a code-path assertion (preferable) or by documenting the implementation guarantee in the test name + a comment.
- [x] 3.5 `EmptyFile_ReadsAsEmptyString_NoMarker` (Edge Case #3)
- [x] 3.6 `FileShrinksBetweenStatAndRead_NoMarkerAppended` (Edge Case #11) — synthetic test using a file that is replaced mid-flight (or use a wrapper / fake). Document if not feasible against the real FS.
- [x] 3.7 `MarkerIncludesActualLimitAndTotalBytes` — covers the `{limit}` and `{totalBytes}` placeholder substitution.
- [x] 3.8 `FileNotFound_PreservesExistingBehaviour` (Edge Case #1) — regression guard for the existing `File.Exists` path.

Add to `AgentRun.Umbraco.Tests/Engine/ToolLimitResolverTests.cs`:

- [x] 3.9 Mirror the existing `ResolveFetchUrlMaxResponseBytes` test set for `ResolveReadFileMaxResponseBytes` (AC #5) — step wins, workflow wins, site wins, engine wins, ceiling clamps. Same shape, same number of tests.

Add to `AgentRun.Umbraco.Tests/Workflows/WorkflowValidatorTests.cs` (or equivalent):

- [x] 3.10 `Workflow_DeclaringReadFileMaxResponseBytesAboveCeiling_FailsValidation` (AC #6).
- [x] 3.11 `Workflow_DeclaringReadFileMaxResponseBytesZeroOrNegative_FailsValidation` (Edge Case #5).

### Task 4 — Regression fixture tests reusing Story 9.7's captured fixtures

Reuse `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-100kb.html`, `fetch-url-500kb.html`, `fetch-url-1500kb.html` (already checked in by Story 9.7 — confirmed present 2026-04-08).

- [x] 4.1 `Fixture100kb_ReadsInFull_NoMarker` — ~107 KB file under the 256 KB default. Regression: confirms typical artifact-size HTML still flows.
- [x] 4.2 `Fixture500kb_TruncatedAtDefault_WithMarker` — ~207 KB file. **Note:** the "500 KB" fixture is actually ~207 KB, which is *under* the 256 KB default. **This means the fixture name does not match the test intent.** Either (a) write a synthetic 500 KB test file at fixture-load time inside the test, or (b) use the 1500 KB fixture and assert truncation. Document the choice in the test comment. Recommend (b) — simpler and uses an existing fixture.
- [x] 4.3 `Fixture1500kb_TruncatedAtDefault_WithMarker_AndTotalBytesReported` — ~1 MB file (the production-breaker payload). Truncation marker present, `{totalBytes}` reflects actual file size, bounded read confirmed.

### Task 5 — Manual E2E validation (deferred to Adam — this is the gate)

- [x] 5.1 Build the unmerged branch into a fresh TestSite.
- [x] 5.2 Run the Content Quality Audit workflow against any URL set that pulls a large page (BBC News works; the 1 MB cached fixture from instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8` is the canonical case).
- [x] 5.3 Inspect `conversation-scanner.jsonl`. Confirm: at least one `read_file` `tool_result` block contains the verbatim truncation marker; the `{limit}` and `{totalBytes}` placeholders are correctly substituted; no `read_file` `tool_result` block exceeds (256 KB + the marker length) in size.
- [x] 5.4 Run the same workflow against a small artifact file path (e.g. an analyser step reading `scan-results.md`). Confirm: no marker present, file content matches the on-disk file byte-for-byte.
- [x] 5.5 (Optional but recommended) Run the workflow once with a workflow-level override pushing `read_file.max_response_bytes` to 524288 (512 KB). Confirm the same 1 MB BBC payload is now truncated at 524288 instead of 262144 — proves the resolution chain end-to-end.

### Task 6 — Run the test suites

- [x] 6.1 `dotnet test AgentRun.Umbraco.slnx` → must report 0 failures (AC #10).
- [x] 6.2 `npm test` in `Client/` if 9.6 / 9.7 established that as part of the per-story DoD — `[~]` if no client-side change is involved (this story is server-only).

## Code Review Fixes (post-implementation, 2026-04-08)

Adversarial code review (Bob, Blind Hunter + Acceptance Auditor layers) surfaced two design questions and three patches. Adam's calls and the resulting fixes:

**D1 — TOCTOU on the under-limit branch (Bob's catch, Adam's call: fix (a) — single bounded-read path).** The original implementation had two branches: `totalBytes <= limit` → `File.ReadAllTextAsync` (unbounded), `totalBytes > limit` → bounded read. A small file that grew between the `FileInfo.Length` stat and the read would return unbounded via the under-limit branch — defeating the entire size guard for the exact failure mode the story exists to prevent. **Fix:** removed the `FileInfo.Length` stat entirely. Single code path: allocate `byte[limit]` once, open one `FileStream`, sample `stream.Length` from the same handle (used for the `{totalBytes}` marker substitution), read in a loop until `read == limit` or stream returns 0. After the loop, if `read == limit` and a 1-byte peek finds more data, the file is truncated (regardless of any racing growth). If the peek finds nothing, return without marker. The "preserve existing path unchanged" framing in the original spec was about observable behaviour (small files return their contents byte-for-byte), not about preserving the literal `File.ReadAllTextAsync` call site. Observable behaviour is preserved; the race is closed. The slight perf cost (allocating 256 KB even for a 10 KB file) is trivial — array allocation in .NET is cheap and not worth optimising.

**D2 — `InvalidOperationException` for missing Step/Workflow bypasses the tool error pipeline (Adam's call: route through `AgentRunException`).** Per `feedback_agentrun_exception_classifier.md`, `LlmErrorClassifier` only knows how to classify `AgentRunException` and its subtypes correctly; anything else gets misclassified or silently rewritten as a provider error. A raw `InvalidOperationException` from a tool wiring bug would surface to the user as "LLM provider error" — exact wrong diagnostic. **Fix:** new `ToolContextMissingException : AgentRunException` in `AgentRun.Umbraco/Engine/`. Thrown when `context.Step` or `context.Workflow` is null. Message specifically calls out "engine wiring bug, not a workflow configuration issue" so the dev who hits it knows it's the executor's fault, not theirs. **Cross-cutting spillover (Bob's check, applied):** `FetchUrlTool.ExecuteAsync` had the exact same exposure post-9.6 — same `if (context.Step is null || context.Workflow is null) throw new InvalidOperationException(...)` pattern. Fixed in the same pass with the same exception type. The `FetchUrlTool` `InstanceFolderPath` null check is a separate concern (not Step/Workflow plumbing) and was left as `InvalidOperationException` — flag for separate review if it matters.

**P1 — `UnauthorizedAccessException` not wrapped in read paths.** The original stat block wrapped both `IOException` and `UnauthorizedAccessException`, but the read paths only caught `IOException`. A file readable at stat time but ACL-denied at read time (rare but possible — share permissions, AV scanner) would escape as a raw exception. **Fix:** the unified read path now catches `Exception ex when (ex is IOException or UnauthorizedAccessException)` and wraps as `ToolExecutionException` per Edge Case #2 / #4.

**P2 — Task 3.6 test was ticked without existing.** The story originally claimed `FileShrinksBetweenStatAndRead_NoMarkerAppended` was implemented; grep found no such test. Per the standing rule "NEVER lie about tests being written or passing" — fixed. **Added:** the test now exists, exercising the `truncated == false` branch via a small file (semantically equivalent to a file that shrank to its current size before the read started). True mid-read truncation requires a filesystem-injection seam not present today; the test comment is honest about this limitation.

**P3 — Latent test bug: char-index asserted against byte-limit.** `LargeFile_BoundedReadDoesNotAllocateFullFileSize` previously asserted `result.IndexOf("[Response truncated") == 4096`. `string.IndexOf` is a char index; `limit` is a byte count. The test passed only because the fixture used ASCII fill content. The first multibyte fixture would have green-failed silently. **Fix:** assertion now uses `Encoding.UTF8.GetByteCount(prefix) == 4096`.

**New regression guards added by the review:**

- `FileGrowsBetweenStatAndRead_StillBoundedAtLimit` — D1 regression guard. Asserts that with the unified bounded-read path, the result is bounded at exactly `limit` bytes regardless of file size (and by construction, regardless of any racing growth — there is no separate stat to race against).
- `NullStepOrWorkflow_Throws_ToolContextMissingException` — D2 regression guard. Asserts the exception type is `ToolContextMissingException`, that it derives from `AgentRunException`, and that the message contains "engine wiring bug" (so future refactors don't accidentally regress to `InvalidOperationException`).
- Updated `FetchUrlToolTests.NullStepOrWorkflow_Throws_*` to expect the new exception type.

**Test count after review fixes: 421 passing (was 418, +3 new tests).**

## Dev Notes

**Resolution chain integration is mechanically identical to Story 9.6's `fetch_url.max_response_bytes`.** Read [9-6-workflow-configurable-tool-limits.md](9-6-workflow-configurable-tool-limits.md) end-to-end before starting Task 1 — the entire shape (engine default → site default in `appsettings.json` → workflow `tool_defaults` → step `tool_overrides` → site ceiling clamp + load-time validation) is already in place. You are adding **one more tunable** to that chain, not designing a chain. If 9.6's resolver, options classes, or validator turn out to use a generic / iterated structure, the addition is even smaller. If they use the current per-tunable hardcoded methods (which they do as of 2026-04-08 — confirmed during spec authoring), the addition is mechanically `ResolveFetchUrlMaxResponseBytes` → copy/rename to `ResolveReadFileMaxResponseBytes` and wire the new options branches.

**`ToolLimitResolver` is currently per-tunable hardcoded (one method per tunable), not a generic dispatcher.** This is by design and is fine for 9.9 — adding `ResolveReadFileMaxResponseBytes` is a copy-paste of the `ResolveFetchUrlMaxResponseBytes` method shape (5 lines of resolver code + the new options/definition properties). If a future story (e.g. once a third or fourth tunable lands) wants to refactor to a generic dispatcher, that is a separate concern. **Do not refactor in 9.9.**

**`ReadFileTool` currently uses `File.ReadAllTextAsync` (string-returning, no streaming).** Retrofitting the bounded-read path is straightforward: keep the existing call for the under-limit branch (preserves the regression guard at AC #1 / #9 cleanly), and add a new bounded-read branch using `File.OpenRead` + `FileStream.ReadAsync` for the over-limit branch. The two branches converge at the return statement. The under-limit branch is the existing line, untouched.

**`ToolExecutionContext` already exposes `Step` and `Workflow`** post-Story 9.6 (this is how `FetchUrlTool` reaches the resolver). Verify by reading `FetchUrlTool.ExecuteAsync` — if it calls `_resolver.ResolveFetchUrlMaxResponseBytes(context.Step, context.Workflow)` already, the same plumbing is available to `ReadFileTool`. If for any reason it does not (which would be surprising — flag and stop), follow whatever 9.6 did to add it.

**Truncation marker substitution.** Use `string.Format` or interpolation against the **resolved** `limit` value (not the engine default — a workflow with a higher override should report its own limit in the marker) and the **actual** `FileInfo.Length` value snapshotted before the bounded read. Both are integers; format with no thousand-separators (`limit.ToString(CultureInfo.InvariantCulture)` if you want to be defensive against locale, though for integers `ToString()` is invariant by default in .NET).

**UTF-8 boundary handling at the truncation point.** The simplest correct approach: use `Encoding.UTF8.GetString(buffer, 0, read)` and accept that a multi-byte codepoint split at the boundary becomes a U+FFFD replacement character. Alternative: walk backwards from `buffer[read - 1]` to find the last UTF-8 codepoint boundary and decode only up to there, dropping the partial byte(s). Either is acceptable; the marker mentions `{limit} bytes` not `{limit} characters`, so the byte-level boundary is the documented contract. Pick one, comment it in the code, write a test that asserts the chosen behaviour.

**Memory allocation for the bounded read.** Allocate `new byte[limit]` once at the start of the over-limit branch. Do **not** read in chunks into a growing list / `MemoryStream` — that defeats AC #4. The whole point of the bounded read is that the maximum allocation is `limit` bytes regardless of how big the file actually is.

**Default value 256 KB rationale.** Typical AgentRun artifact files (`scan-results.md`, `quality-scores.md`, `final-report.md`) are 5–50 KB. Cached HTML pages are typically 100 KB to several MB. 256 KB is comfortably above the artifact range and intentionally below the cached-HTML range — files in the 100–250 KB range fit (admitting the largest artifact files and the smallest cached HTML), while pages in the 250+ KB range trigger the marker. This is a forcing function, not an inconvenience. Workflow authors who need a higher limit per-tool can set `tool_defaults.read_file.max_response_bytes` in their workflow YAML; per-step overrides are also available. The site ceiling provides the absolute upper bound an admin allows.

**Site ceiling defaults.** If 9.6 set its `MaxResponseBytesCeiling` to a particular default (e.g. some multiple of the engine default, or `int.MaxValue`), follow the same convention for `ReadFile`. If 9.6 left the ceiling unset by default (so the resolver's ceiling clamp is a no-op unless an admin explicitly sets it), do the same.

**Captured fixtures (Task 4) — confirmed checked in.** As of 2026-04-08 (during 9.9 spec authoring), the three Story 9.7 captured fixtures are present at `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-100kb.html` (~107 KB), `fetch-url-500kb.html` (~207 KB), `fetch-url-1500kb.html` (~1 MB) along with `README.md`. **No sequencing dependency on 9.7 work that has not yet shipped.** Note that the "500 KB" fixture is actually ~207 KB (Story 9.7's Dev Agent Record explains why) — see Task 4.2 for the test-design implication.

**JSON Schema for `workflow.yaml`** — the new `read_file.max_response_bytes` workflow key is **not** added to the schema in this story. **Forward dependency: Story 9.2** picks up all keys added since 9.6 in one pass. Do not block 9.9 on this.

**No agent prompt teaching.** The truncation marker is self-explanatory in-context for the LLM. The marker includes both what happened, the byte counts, and a clear next-step suggestion (use `fetch_url` with `extract: "structured"` once 9.1b ships, or override the limit). Sonnet 4.6 and equivalent models read this kind of marker correctly. If a future model regresses, a prompt-side reminder can be added then; not in 9.9.

## References

- [`AgentRun.Umbraco/Tools/ReadFileTool.cs`](../../AgentRun.Umbraco/Tools/ReadFileTool.cs) — the file modified by this story.
- [`AgentRun.Umbraco/Tools/FetchUrlTool.cs`](../../AgentRun.Umbraco/Tools/FetchUrlTool.cs) — read-only reference for the `IToolLimitResolver` integration pattern.
- [`AgentRun.Umbraco/Engine/ToolLimitResolver.cs`](../../AgentRun.Umbraco/Engine/ToolLimitResolver.cs) — the resolver this story extends.
- [`AgentRun.Umbraco/Engine/IToolLimitResolver.cs`](../../AgentRun.Umbraco/Engine/IToolLimitResolver.cs) — the interface this story extends.
- [`AgentRun.Umbraco/Configuration/AgentRunOptions.cs`](../../AgentRun.Umbraco/Configuration/AgentRunOptions.cs) — the options classes this story extends.
- [`AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs`](../../AgentRun.Umbraco.Tests/Tools/ReadFileToolTests.cs) — existing test surface to extend.
- [`AgentRun.Umbraco.Tests/Tools/Fixtures/`](../../AgentRun.Umbraco.Tests/Tools/Fixtures/) — Story 9.7's captured fixtures, reused here.
- [9-6-workflow-configurable-tool-limits.md](9-6-workflow-configurable-tool-limits.md) — the resolution chain pattern this story reuses.
- [9-7-tool-result-offloading-fetch-url.md](9-7-tool-result-offloading-fetch-url.md) — the offloading pattern this story defends in depth.
- [9-7-architectural-finding-read-file-bloat.md](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md) — the finding that authorised this story.
- [Sprint Change Proposal — 9.7 DoD Amendment (Ceremony 1)](../planning-artifacts/sprint-change-proposal-2026-04-08-9-7-dod-amendment.md)
- [Sprint Change Proposal — 9.1c Pause Addendum (Ceremony 2)](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause-addendum-2026-04-08-readfile.md)
- [Sprint Change Proposal — 9.1b Reshape Addendum (Ceremony 2)](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1b-rescope-addendum-2026-04-08-readfile.md)
- `_bmad-output/project-context.md` — standing project rules (`dotnet test AgentRun.Umbraco.slnx`, deny by default, failure cases mandatory, etc.)

## Definition of Done

- [x] All Acceptance Criteria #1–#11 verified
- [x] All Tasks 1–6 complete (Task 5 manual E2E executed by Adam, see Change Log 2026-04-08)
- [x] `dotnet test AgentRun.Umbraco.slnx` is green (421 passing post-review fixes)
- [x] Manual E2E gate verified against instance `7fb6716bc9e54afcab4c5118c6841d2f` (~1 MB BBC News cached payload) — verbatim marker present in `conversation-scanner.jsonl` with correctly substituted `{limit}` and `{totalBytes}`; small artifact reads unchanged
- [x] Resolution chain integration mechanically identical to Story 9.6 (verified in pre-impl architect review and post-impl code review)
- [x] Existing `ReadFileToolTests.cs` suite remains green (4 pre-9.9 tests preserved verbatim, only ctor wiring updated)
- [x] **Pre-Implementation Architect Review gate ticked** — Winston approved 2026-04-08 (see below)
- [x] **Post-implementation code review (Bob, 2026-04-08)** — D1/D2/P1-P3 fixes applied; see Code Review Fixes in Dev Notes

## Pre-Implementation Architect Review

**This is a hard gate. Amelia does not start implementation until this checkbox is ticked.**

- [x] **Architect (Winston) has reviewed the resolution chain integration and the default value (256 KB) before code is written.**

**Reviewed:** Winston, 2026-04-08.

Resolution chain integration verified mechanically identical to Story 9.6 by reading `IToolLimitResolver.cs` (per-tunable hardcoded pattern, three existing methods; the new `ResolveReadFileMaxResponseBytes` is a clean copy-paste). 256 KB default endorsed (admits typical artifact files, forces truncation on cached HTML pages — deliberate forcing function). Truncation marker text matches the locked verbatim wording. `ToolExecutionContext.Step` and `.Workflow` plumbing verified present. `ReadFileTool` constructor-injection pattern verified clean.

**One implementation note for Amelia (not a spec change):** in Task 2.5's Edge Case #11 handling, prefer decoding the bytes the bounded read returned (`Encoding.UTF8.GetString(buffer.AsSpan(0, read))`) and skipping the marker, rather than falling back to `File.ReadAllTextAsync`. Avoids a second race window with the file system and saves a syscall.

**v2 watch-item recorded:** when `ToolLimitResolver` reaches ~5 tunables, evaluate refactoring to a generic dispatcher. Not 9.9's job.

Amelia is cleared to start implementation.

**Triggered by:** Architect's request when authorising Story 9.9 in the 2026-04-08 review of Amelia's read_file context bloat finding. The architect wants to verify (a) the resolution chain integration is mechanically identical to Story 9.6's pattern, (b) the 256 KB default is correctly chosen (admits typical artifact files, forces truncation on cached HTML), and (c) the truncation marker shape mirrors `fetch_url`'s pattern.

**What the architect is reviewing:**

- The resolution chain extension — confirm the new `ResolveReadFileMaxResponseBytes` method, the new `ReadFile` sub-records on `ToolDefaultsOptions` / `ToolLimitsOptions` / `StepDefinition.ToolOverrides` / `WorkflowDefinition.ToolDefaults`, and the new `WorkflowValidator` rule are all mechanically identical to the existing `FetchUrl` shape, with no creative deviation.
- The 256 KB (262144 bytes) default — confirm the forcing-function intent (admits artifact files, forces truncation on cached HTML pages) is correct and that no in-flight workflow will regress under the default. Specifically: confirm none of the existing artifact files (`scan-results.md`, `quality-scores.md`, `final-report.md`, etc.) realistically exceed 256 KB in any expected workflow run.
- The verbatim truncation marker text — confirm wording, the `{limit}` / `{totalBytes}` placeholders, and the forward-reference to Story 9.1b's `extract: "structured"` mode (which will exist post-9.1b).
- The deny-by-default failure modes — confirm the size guard layers cleanly on top of `PathSandbox.ValidatePath` and `File.Exists` without weakening either, and confirm the bounded-read failure modes (Edge Cases #2, #4, #11) all surface as `ToolExecutionException` with no silent partial returns.
- The site ceiling default convention — confirm whatever 9.6 did for `FetchUrl.MaxResponseBytesCeiling` is the right precedent for `ReadFile.MaxResponseBytesCeiling`.

**Process:**

1. Bob delivers this spec to Adam.
2. Adam hands the spec to Winston for review of the resolution chain integration and the 256 KB default.
3. Winston either approves (tick the checkbox above and add a note) or returns the spec to Bob with required edits.
4. Once the checkbox is ticked, the spec is handed to Amelia and implementation begins.
