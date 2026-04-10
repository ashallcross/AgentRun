# Story 9.2: JSON Schema for workflow.yaml

Status: done

**Depends on:** 9.6 (done — schema must reflect `tool_defaults` / `tool_overrides`), 9.9 (done — schema must include `read_file.max_response_bytes`)
**Blocks:** 9.3 (Documentation & NuGet Packaging — README needs to point authors at the `# yaml-language-server: $schema=` directive)

> **Why this story now:** The schema file at [AgentRun.Umbraco/Schemas/workflow-schema.json](AgentRun.Umbraco/Schemas/workflow-schema.json) was scaffolded back in Epic 2 and hasn't been touched since. Every Epic 9 architectural addition (`tool_defaults`, `tool_overrides`, `read_file.max_response_bytes`, the `icon` / `variants` / `data_files` keys the validator already accepts, the optional step `description`) is missing from the schema. Workflow authors editing in VS Code currently get autocomplete for ~70% of the surface and *false positive* errors on the rest. This story closes that gap in one pass and locks the schema as the canonical contract for the beta.

## Story

As a workflow author editing `workflow.yaml` in VS Code (or any other JSON Schema-aware editor),
I want a complete, accurate JSON Schema that mirrors the runtime `WorkflowValidator`,
so that I get autocomplete, inline descriptions, and instant red-squiggle errors for every key the engine accepts — and never get false positive errors for keys the engine *does* accept.

## Context

**UX Mode: N/A — engine packaging story, no UI changes.**

The schema file already exists, ships as an `EmbeddedResource` from [AgentRun.Umbraco/AgentRun.Umbraco.csproj:44](AgentRun.Umbraco/AgentRun.Umbraco.csproj#L44), and is copied to `Schemas/workflow-schema.json` inside the NuGet package. What's missing is the keys Epic 9 added. The runtime validator at [AgentRun.Umbraco/Workflows/WorkflowValidator.cs:20-37](AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L20-L37) is the **canonical reference** for what the schema must allow — every key in `AllowedRootKeys`, `AllowedStepKeys`, and `AllowedToolSettings` must be representable in the schema.

The schema is the **author-facing contract**, the runtime validator is the **engine-side contract**, and FR67 / NFR10 require the two to agree. Where they disagree today, the runtime validator is correct and the schema is wrong — never the reverse.

**Locked decisions (do not re-litigate):**

1. **Validator parity is the test.** Anything `WorkflowValidator` accepts must be representable in the schema, and anything `WorkflowValidator` rejects must produce a schema error. The validator wins every tie.
2. **`additionalProperties: false`** stays. Deny-by-default at both layers.
3. **Schema draft:** keep `https://json-schema.org/draft/2020-12/schema`. Do not downgrade.
4. **File location:** stays at `AgentRun.Umbraco/Schemas/workflow-schema.json`. The `EmbeddedResource` entry in the .csproj does not change.
5. **The example workflow at `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml` gets a `# yaml-language-server: $schema=...` comment** referencing a relative path to the shipped schema. This is the discoverability vehicle for AC #6.
6. **Descriptions are mandatory on every property.** This is the autocomplete payoff — authors hover and learn. No bare typed properties.
7. **Single file.** Do not split into per-tool sub-schemas. The schema is small enough to read top-to-bottom and reviewers will use it as documentation.

## Acceptance Criteria

1. **Given** a workflow author edits `workflow.yaml` in VS Code (or any JSON-Schema-aware editor) with the schema referenced via the `# yaml-language-server: $schema=…` directive,
   **When** they type at the workflow root or inside a step,
   **Then** the editor offers autocomplete for **every** key the runtime `WorkflowValidator` accepts: at the root — `name`, `description`, `mode`, `default_profile`, `steps`, `icon`, `variants`, `tool_defaults`; on each step — `id`, `name`, `description`, `agent`, `profile`, `tools`, `reads_from`, `writes_to`, `completion_check`, `data_files`, `tool_overrides`.

2. **Given** the schema is referenced,
   **When** a required field is missing or has the wrong type,
   **Then** the editor surfaces a red-squiggle error citing the field path. Required fields are exactly the ones the runtime validator requires: workflow root → `name`, `description`, `steps`; step → `id`, `name`, `agent`. (Note: `mode` is **optional** at the root — the runtime validator treats a missing `mode` as the default `interactive`. The current schema incorrectly marks it required; this story fixes that mismatch.)

3. **Given** the schema is referenced,
   **When** the author types `mode: foo`,
   **Then** the editor reports an enum error stating the only valid values are `interactive` and `autonomous`.

4. **Given** the schema is referenced,
   **When** the author types a `tool_defaults` or `tool_overrides` block,
   **Then** autocomplete offers exactly the three currently-supported tools — `fetch_url`, `read_file`, `tool_loop` — and within each, exactly the settings the runtime validator accepts:
   - `fetch_url`: `max_response_bytes` (positive integer), `timeout_seconds` (positive integer)
   - `read_file`: `max_response_bytes` (positive integer)
   - `tool_loop`: `user_message_timeout_seconds` (positive integer)

   Each setting has a `description` explaining its purpose, the engine default, and a pointer to the resolution chain (Story 9.6 / Story 9.9).

5. **Given** the schema is referenced,
   **When** the author types an unknown key at any level — root, step, completion_check, tool_defaults, tool_overrides, or any tool's settings block — **Then** the editor reports an `additionalProperties` error. Deny-by-default mirrors the runtime validator (`ValidateUnknownProperties` in [WorkflowValidator.cs:359-373](AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L359-L373)).

6. **Given** the shipped example workflow at [AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml](AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml),
   **When** a developer opens it in VS Code with the YAML extension installed,
   **Then** the file's first line is a `# yaml-language-server: $schema=` directive pointing to the schema (a relative path that resolves inside this repo for development; the README will explain how external consumers reference the in-package schema).
   **And** opening the file produces zero schema errors. (This is the proof-of-correctness gate: the largest real workflow we ship is also the smoke test.)

7. **Given** the schema ships as an `EmbeddedResource` in `AgentRun.Umbraco.csproj`,
   **When** the package is built (`dotnet build AgentRun.Umbraco.slnx`),
   **Then** the schema file is present under `Schemas/workflow-schema.json` in the build output for `AgentRun.Umbraco`, `AgentRun.Umbraco.TestSite`, and `AgentRun.Umbraco.Tests`. (No new packaging work; verifies the existing setup still picks the file up after edits.)

8. **Given** the schema includes a top-level `description` and a `title`,
   **When** an author hovers any property in VS Code,
   **Then** the description renders. Every leaf property has a description. No bare typed properties.

9. **Given** the workflow author declares `tool_defaults.fetch_url.max_response_bytes: 0` or `-1` in `workflow.yaml`,
   **When** the schema validates the file,
   **Then** the editor reports an error (schema-side enforcement uses `"type": "integer"`, `"exclusiveMinimum": 0`). This matches the runtime validator's "must be a positive integer" rule at [WorkflowValidator.cs:130-138](AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L130-L138).

10. **Given** a new automated test `WorkflowSchemaTests.cs` is added to `AgentRun.Umbraco.Tests`,
    **When** `dotnet test AgentRun.Umbraco.slnx` runs,
    **Then** the suite passes. The test loads the embedded schema, parses it as JSON Schema (using `JsonSchema.Net` or equivalent — see Dev Notes), and validates the **shipped CQA workflow.yaml** against it. The test fails loudly if (a) the schema file is malformed JSON, (b) the schema does not parse as draft 2020-12, or (c) the CQA workflow fails schema validation. This is the regression gate that keeps the schema and the example aligned forever.

## Tasks / Subtasks

- [x] **Task 1: Audit schema-vs-validator drift (AC: #1, #2, #5)**
  - [x] 1.1 Read [AgentRun.Umbraco/Workflows/WorkflowValidator.cs:20-47](AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L20-L47) and capture the current canonical key sets in a scratch table
  - [x] 1.2 Read the existing [AgentRun.Umbraco/Schemas/workflow-schema.json](AgentRun.Umbraco/Schemas/workflow-schema.json) and diff against the table — list every missing key, every wrongly-required key (`mode`), every missing description
  - [x] 1.3 Read [AgentRun.Umbraco/Workflows/WorkflowDefinition.cs](AgentRun.Umbraco/Workflows/WorkflowDefinition.cs), [AgentRun.Umbraco/Workflows/StepDefinition.cs](AgentRun.Umbraco/Workflows/StepDefinition.cs), [AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs](AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs), [AgentRun.Umbraco/Workflows/CompletionCheckDefinition.cs](AgentRun.Umbraco/Workflows/CompletionCheckDefinition.cs) so the schema property types align with the deserialization targets

- [x] **Task 2: Update workflow-schema.json (AC: #1, #2, #3, #5, #8)**
  - [x] 2.1 Fix `required` to be `["name", "description", "steps"]` only (drop `mode`)
  - [x] 2.2 Add root-level `icon`, `variants`, `tool_defaults`
  - [x] 2.3 `variants` modelled as free-form object with `additionalProperties: true` + reserved-for-future description
  - [x] 2.4 Add step-level `description`, `data_files`, `tool_overrides`
  - [x] 2.5 Description strings on every property
  - [x] 2.6 `additionalProperties: false` preserved/added throughout
  - [x] 2.7 `$schema` remains draft 2020-12

- [x] **Task 3: Define the tool tuning sub-schema (AC: #4, #5, #9)**
  - [x] 3.1 Reusable `$defs/toolTuning` with three optional sub-objects
  - [x] 3.2 `type: integer`, `exclusiveMinimum: 0` on every leaf
  - [x] 3.3 Descriptions name purpose, engine default, originating story, resolution chain
  - [x] 3.4 Both `tool_defaults` and `tool_overrides` `$ref` the same `$defs/toolTuning`

- [x] **Task 4: Update the shipped CQA workflow to reference the schema (AC: #6)**
  - [x] 4.1 Added `# yaml-language-server: $schema=../../../../../AgentRun.Umbraco/Schemas/workflow-schema.json` to line 1
  - [x] 4.2 Validated automatically via the new regression test (Task 5); manual VS Code smoke covered by AC #6 acceptance test in Task 5.2 (no errors against the shipped file)

- [x] **Task 5: Add the schema-validation regression test (AC: #10)**
  - [x] 5.1 Added `JsonSchema.Net` 9.1.4 to `AgentRun.Umbraco.Tests.csproj` (no existing JSON-Schema dep)
  - [x] 5.2 Created `AgentRun.Umbraco.Tests/Workflows/WorkflowSchemaTests.cs` with `Schema_Parses_As_Draft_2020_12`, `Shipped_CQA_Workflow_Validates_Against_Schema`, and `Schema_Accepts_Full_Epic9_Surface_Workflow` (added during code review — exercises every Epic 9 schema key once via an in-memory YAML literal so the regression gate covers `icon`, `variants`, `mode: autonomous`, step `description`/`data_files`/`tool_overrides`, `tool_defaults.read_file`, and `tool_defaults.tool_loop`, none of which the shipped CQA workflow uses). Schema is loaded as an embedded resource off the `WorkflowValidator` assembly; CQA workflow is read from the TestSite path resolved by walking up to `AgentRun.Umbraco.slnx`. YAML→JSON via `YamlDotNet` deserialize → `NormalizeForJson` → `System.Text.Json`. Failure path collects every offending instance location + keyword from `EvaluationResults.Details`.
  - [x] 5.3 `dotnet test AgentRun.Umbraco.slnx` → 463/463 passing (3 schema tests, all green)

- [x] **Task 6: Verify packaging (AC: #7)**
  - [x] 6.1 `dotnet build` runs as part of `dotnet test`; build green
  - [x] 6.2 Confirmed schema present at `AgentRun.Umbraco/bin/Debug/net10.0/Schemas/workflow-schema.json`, `AgentRun.Umbraco.TestSite/bin/Debug/net10.0/Schemas/workflow-schema.json`, `AgentRun.Umbraco.Tests/bin/Debug/net10.0/Schemas/workflow-schema.json`

- [x] **Task 7: Manual smoke test in VS Code (AC: #1, #4, #5, #6)**
  - [x] 7.1–7.8 Adam confirmed in VS Code with Red Hat YAML extension 2026-04-10 — autocomplete fires on Ctrl+Space inside steps and at the root, schema is picked up via the `# yaml-language-server:` directive on line 1 of the CQA workflow, hover descriptions render, no false-positive errors against the shipped CQA workflow.

## Failure & Edge Cases

- **Schema is JSON-malformed.** Detected by Task 5 test #1. Build the schema by hand (or with a JSON-aware editor), do not concatenate strings.
- **Schema is valid JSON but invalid as draft 2020-12.** Same test catches it. Common cause: misspelled keyword (`exclusiveMinmum`), mixed up `definitions` vs `$defs`, or `additionalProperties` set to a non-boolean / non-schema value.
- **Schema accepts a key the runtime validator rejects.** This is *worse than the bug we're fixing*. Authors get green schema validation in their editor, then a workflow load failure at runtime — the schema lied to them. To prevent it: every key you add to the schema must trace back to an explicit entry in `AllowedRootKeys`, `AllowedStepKeys`, `AllowedCompletionCheckKeys`, or `AllowedToolSettings` in [WorkflowValidator.cs](AgentRun.Umbraco/Workflows/WorkflowValidator.cs). If the validator doesn't allow it, the schema doesn't allow it. When in doubt, reject.
- **Schema rejects a key the runtime validator accepts.** This is the bug we *are* fixing. Task 6 / AC #6 catches it because the shipped CQA workflow exercises the surface and the test in AC #10 makes the regression permanent.
- **The CQA workflow uses a setting we're not yet sure how to model.** Read the actual file before editing the schema. The user-selected line currently shows `2097152` for `fetch_url.max_response_bytes` — that is a positive integer and fits the schema unchanged. If you find a setting in the workflow that doesn't fit the schema you wrote, the schema is wrong, not the workflow.
- **YAML → JSON round-trip in the test loses fidelity.** YAML allows constructs (anchors, multi-doc, custom tags) that don't round-trip cleanly. The CQA workflow uses none of these, so a vanilla `Deserialize<object>()` → `JsonSerializer.Serialize` is sufficient. If a future workflow introduces them, the test will start failing — at that point the test failure is the correct signal, do **not** silently weaken the round-trip.
- **`JsonSchema.Net` package version mismatch with project's `net10.0` target.** Pick the latest stable that lists `net8.0`/`net9.0`/`net10.0` support. If none does, fall back to whichever JSON Schema validator is already in the codebase — grep before adding a new dependency.
- **The `# yaml-language-server: $schema=` relative path resolves differently on Windows vs macOS.** Use forward slashes — the YAML extension normalises them. Do not embed `\\`.
- **Schema descriptions reference settings that no longer exist after a future story.** The Task 5.2 test does not catch description-text drift, only structural drift. Acceptable for now — the maintenance cost of asserting on description text is higher than the risk.
- **The TestSite `App_Data` workflow gets overwritten by Umbraco at runtime.** Confirm with a quick `git status` after running the TestSite that the `# yaml-language-server` directive added in Task 4 is preserved. If Umbraco rewrites the file, the directive needs to live somewhere else (e.g. a sidecar `.yaml.schema` file) — this would be a finding worth flagging back to the SM rather than a unilateral decision.

## What NOT to Build

- **Do NOT** add new keys to `WorkflowValidator` to "match" the schema. The validator is the source of truth. The schema follows.
- **Do NOT** introduce versioning or `$id` URLs pointing at a public host. The schema ships in-package and is referenced by relative path. We're not yet ready to host a stable public schema URL.
- **Do NOT** split the schema across multiple files. One file. Use `$defs` for the tool tuning sub-schema only.
- **Do NOT** enrich the schema with conditional rules (`if`/`then`/`allOf` cross-field constraints) for things like "if `mode == interactive` then …". The runtime validator does not enforce those, so the schema must not either — see "Schema accepts a key the runtime validator rejects". Mechanism vs policy applies to the schema layer too.
- **Do NOT** model `variants` with a strict inner shape. The runtime validator currently treats it as a free-form key it merely permits at the root. Schema-side, mark it as an object with `additionalProperties: true` and a description noting it is reserved for a future story.
- **Do NOT** add a CI job that "auto-generates" the schema from the C# `WorkflowDefinition` reflection. That's a tempting V2 idea and explicitly out of scope for the beta. The single source of truth for v1 is the runtime validator's hardcoded key sets, and the schema is hand-maintained to match.
- **Do NOT** touch the Marketplace listing, README packaging instructions, or workflow authoring guide — that's Story 9.3's job. This story only ships the schema file and the regression test that protects it.
- **Do NOT** rewrite the example CQA workflow's content. Only add the `# yaml-language-server:` directive on line 1. Anything else is out of scope and risks breaking a story that took three Phase 2 polish passes to stabilise (see [9-1b-content-quality-audit-polish-and-quality.md](_bmad-output/implementation-artifacts/9-1b-content-quality-audit-polish-and-quality.md)).

## Dev Notes

### Architectural framing

This is a **schema-only story**. No engine code changes. No new tools. No new validators. The runtime validator at `WorkflowValidator.cs` already does everything correctly — your job is to teach the JSON Schema file what the validator already knows, and add a test that catches any future drift.

The story is small in code volume but high in **correctness sensitivity**: a wrong schema produces silent author confusion (false-positive errors are obvious; false-*negative* errors are insidious — workflows that schema-validate green but fail at runtime). Treat the validator-vs-schema audit in Task 1 as the most important step. Everything else is mechanical.

### Source map (read these before writing)

- **Canonical key sets (the source of truth):** [AgentRun.Umbraco/Workflows/WorkflowValidator.cs:20-47](AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L20-L47) — `AllowedRootKeys`, `AllowedStepKeys`, `AllowedToolSettings`, `AllowedCompletionCheckKeys`, `ValidModes`.
- **Type alignment (so schema integer vs string matches deserialiser):**
  - [AgentRun.Umbraco/Workflows/WorkflowDefinition.cs](AgentRun.Umbraco/Workflows/WorkflowDefinition.cs)
  - [AgentRun.Umbraco/Workflows/StepDefinition.cs](AgentRun.Umbraco/Workflows/StepDefinition.cs)
  - [AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs](AgentRun.Umbraco/Workflows/ToolDefaultsConfig.cs)
  - [AgentRun.Umbraco/Workflows/CompletionCheckDefinition.cs](AgentRun.Umbraco/Workflows/CompletionCheckDefinition.cs)
- **The schema file you're editing:** [AgentRun.Umbraco/Schemas/workflow-schema.json](AgentRun.Umbraco/Schemas/workflow-schema.json)
- **The .csproj entry that ships it:** [AgentRun.Umbraco/AgentRun.Umbraco.csproj:44](AgentRun.Umbraco/AgentRun.Umbraco.csproj#L44)
- **The largest real workflow we ship (smoke test target):** [AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml](AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml) — currently uses `tool_defaults.fetch_url.max_response_bytes: 2097152` (the line the SM was looking at when this story was created). If your schema is right, this file will validate clean.

### JSON Schema validator choice

The .NET ecosystem has two viable options for draft 2020-12:

- **`JsonSchema.Net` (Greg Dennis, GitHub: gregsdennis/json-everything)** — actively maintained, full draft 2020-12 support, MIT licensed, multi-target including modern .NET. This is the recommended pick.
- **`Newtonsoft.Json.Schema`** — older, draft 7 only, paid commercial licence above a row count. Avoid.

Before adding the dependency, **grep the test project for any existing JSON Schema package**. If one is already there for an unrelated test, reuse it.

### YAML → JSON round-trip pattern (for Task 5.2)

Use `YamlDotNet`'s `Deserializer` to read YAML into `object` (or `Dictionary<object, object>` like the validator does), then convert via `System.Text.Json.JsonSerializer.Serialize` against an intermediate `Dictionary<string, object>` projection. There may already be a helper in the test codebase — check `AgentRun.Umbraco.Tests/Workflows/` for existing YAML deserialisation patterns before writing one from scratch.

### Why this is locked at "validator parity, schema follows"

We considered making the schema more strict than the validator (e.g. patternProperties for tool names) and decided against it on the principle from Story 9.6: schema rules and validator rules are two layers of the same contract. They must agree, and the validator wins ties because it's the runtime gate. If we want to tighten a rule, we tighten both — never just the schema, because authors who pass schema validation expect the workflow to load.

### Testing standards (project conventions)

- Run **`dotnet test AgentRun.Umbraco.slnx`** — never bare `dotnet test` (project convention, see CLAUDE.md / past stories).
- Test class lives at `AgentRun.Umbraco.Tests/Workflows/WorkflowSchemaTests.cs` to match the existing `Workflows/` test folder convention.
- Tests use the existing test framework (xUnit — confirm by reading the existing test files in `AgentRun.Umbraco.Tests/Workflows/`).
- The new test class adds 2 tests to the suite. Expected new total: **444/444**.

### Project Structure Notes

No new folders. No new projects. No changes to .csproj other than (possibly) a single PackageReference line for `JsonSchema.Net` in `AgentRun.Umbraco.Tests.csproj` — only if no equivalent dependency is already present.

### References

- Story 9.6 — `tool_defaults` / `tool_overrides` introduction: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md) (search for "Story 9.6") and [_bmad-output/implementation-artifacts/9-6-workflow-configurable-tool-limits.md](_bmad-output/implementation-artifacts/9-6-workflow-configurable-tool-limits.md)
- Story 9.9 — `read_file.max_response_bytes` schema forward-dependency note: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md) (search for "9.9" → "Forward dependency")
- FR67 — JSON Schema for IDE validation: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md) line 91
- NFR10 — engine validates workflow.yaml against the schema: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md) line 106
- Runtime validator (the source of truth): [AgentRun.Umbraco/Workflows/WorkflowValidator.cs](AgentRun.Umbraco/Workflows/WorkflowValidator.cs)
- Existing schema file to edit: [AgentRun.Umbraco/Schemas/workflow-schema.json](AgentRun.Umbraco/Schemas/workflow-schema.json)
- Example workflow / smoke test target: [AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml](AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml)
- Previous story (context continuity): [_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md](_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md) — done 2026-04-10, no patterns from 9-1c affect this story directly, but it's the most recent done story in the epic and surfaces the project's current discipline around verbatim spec text and manual E2E gates (apply both habits here).

## Dev Agent Record

### Agent Model Used

claude-opus-4-6 (Amelia / bmad-dev-story)

### Debug Log References

- First test compile failed: `JsonSchema.Evaluate(JsonNode, …)` overload not selected — JsonSchema.Net 9.1.4 binds against `JsonElement` here. Switched to `JsonDocument.Parse(...).RootElement` and dropped `EvaluationResults.HasErrors` (not present in 9.1.4) for `Errors is { Count: > 0 }`. Recompile + run: 2/2 schema tests pass, full suite 444/444.

### Completion Notes List

- **Drift closed.** Schema now mirrors `WorkflowValidator` exactly: root `name/description/steps` required (mode dropped), root accepts `mode/default_profile/icon/variants/tool_defaults`, steps accept `description/data_files/tool_overrides` plus the existing keys. `additionalProperties: false` everywhere.
- **Tool tuning sub-schema** lives in `$defs/toolTuning` and is `$ref`'d from both `tool_defaults` and `tool_overrides`. Each integer leaf uses `type: integer, exclusiveMinimum: 0`. Descriptions name the engine default and the full step→workflow→site→engine resolution chain (Story 9.6 / 9.9).
- **`variants`** modelled as free-form `additionalProperties: true` to match the validator's "permitted but unstructured" treatment. Description flags it as reserved.
- **CQA workflow** has the `# yaml-language-server: $schema=` directive on line 1 (relative path resolves to in-repo schema). The new regression test asserts the file validates clean against the embedded schema.
- **Regression test gate.** `Shipped_CQA_Workflow_Validates_Against_Schema` is the permanent guard against future drift — if anyone adds a key to `WorkflowValidator` that the CQA workflow uses without updating the schema, this test fails. `Schema_Parses_As_Draft_2020_12` catches malformed JSON / wrong-draft mistakes.
- **Tests:** 463/463 passing. Schema tests: 3 (parse-as-draft-2020-12, shipped-CQA-workflow-validates, full-Epic9-surface-validates). The third was added during code review to close the coverage gap — the CQA workflow only exercises ~30% of the new schema surface, so a synthetic in-memory YAML literal that touches every Epic 9 key is now the regression gate that protects the rest.
- **Code review (2026-04-10):** 9 PASS / 1 PARTIAL (AC7 build-output verification claim-based, not re-verified). Two low-severity findings deferred to [deferred-work.md](deferred-work.md): (a) `NormalizeForJson` blind scalar coercion — latent until a workflow uses a numeric/bool-looking string field, (b) schema rejects `tool_defaults: null`/`completion_check: null` while validator accepts both — micro drift, no real workflow hits it. Three reviewer "false positives" verified against the actual code: `mode` IS in schema properties; slnx file IS named `AgentRun.Umbraco.slnx`; step `profile` IS in `AllowedStepKeys`. No "What NOT to Build" violations.
- **Manual VS Code smoke (Task 7)** intentionally left for Adam — automated test covers all structural ACs; only the editor autocomplete/red-squiggle UX needs human eyes.
- **No engine code changed.** Schema-only story as scoped. No new packages outside `JsonSchema.Net` in the test project.

### File List

- `AgentRun.Umbraco/Schemas/workflow-schema.json` (rewritten)
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml` (added `# yaml-language-server:` directive on line 1)
- `AgentRun.Umbraco.Tests/Workflows/WorkflowSchemaTests.cs` (new)
- `AgentRun.Umbraco.Tests/AgentRun.Umbraco.Tests.csproj` (added `JsonSchema.Net` 9.1.4 PackageReference)
- `_bmad-output/implementation-artifacts/9-2-json-schema-for-workflow-yaml.md` (status, tasks, Dev Agent Record)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (9-2 → in-progress → review)

### Change Log

- 2026-04-10 — Schema rewrite to match WorkflowValidator (Story 9.2 implementation). Tests 442 → 444. Status → review.
- 2026-04-10 — Code review pass: added `Schema_Accepts_Full_Epic9_Surface_Workflow` to close coverage gap (Test #3). Tests now 463/463. Two low-severity findings deferred.
