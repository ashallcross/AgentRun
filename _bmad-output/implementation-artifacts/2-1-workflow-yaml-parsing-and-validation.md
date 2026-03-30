# Story 2.1: Workflow YAML Parsing & Validation

Status: done

## Story

As a developer,
I want the engine to parse workflow.yaml files and validate them against a JSON Schema,
so that malformed workflow definitions are caught early with clear error messages.

## Acceptance Criteria

1. **Given** a workflow folder containing a valid workflow.yaml file, **When** the engine parses the file using YamlDotNet, **Then** a `WorkflowDefinition` model is populated with `name`, `description`, `mode`, `default_profile`, and `steps` array.

2. **Given** a workflow.yaml with steps, **When** parsed, **Then** each step is parsed into a `StepDefinition` model with `id`, `name`, `agent`, `profile`, `tools`, `reads_from`, `writes_to`, and `completion_check`.

3. **Given** a workflow.yaml file, **When** validated, **Then** validation runs against the JSON Schema and produces a list of validation errors (empty list = valid).

4. **Given** a malformed workflow.yaml (missing required fields, wrong types, unknown properties), **When** validated, **Then** each violation produces a clear, actionable error message identifying the specific failure (field name, expected type/value, actual value).

5. **Given** a workflow.yaml with unexpected/undeclared properties, **When** validated, **Then** the workflow is rejected — unknown properties must not be silently ignored (NFR10).

6. **Given** a valid workflow.yaml, **When** parsed and validated, **Then** snake_case field naming is correctly mapped to PascalCase C# properties.

7. **Given** the `WorkflowValidator` class, **When** unit tests run, **Then** tests verify: parsing of valid workflows, rejection of invalid workflows (missing name, missing steps, empty steps array, unknown properties, wrong types), and error message clarity.

## Tasks / Subtasks

- [x] Task 1: Add YamlDotNet NuGet package (AC: 1, 6)
  - [x] Add `YamlDotNet` package to `Shallai.UmbracoAgentRunner.csproj`
- [x] Task 2: Create `WorkflowDefinition` and `StepDefinition` models (AC: 1, 2, 6)
  - [x] Create `Workflows/WorkflowDefinition.cs` with properties mapped from YAML
  - [x] Create `Workflows/StepDefinition.cs` with step properties
  - [x] Create `Workflows/CompletionCheckDefinition.cs` for the `completion_check` object
  - [x] Configure YamlDotNet snake_case naming convention via attributes or deserializer settings
- [x] Task 3: Create `WorkflowValidator` (AC: 3, 4, 5)
  - [x] Create `Workflows/WorkflowValidator.cs` with `IWorkflowValidator` interface
  - [x] Implement structural validation: required fields, correct types, valid enum values for `mode`
  - [x] Implement strict mode: reject any YAML properties not in the schema
  - [x] Return `WorkflowValidationResult` containing a list of `WorkflowValidationError` (field path + message)
- [x] Task 4: Create JSON Schema file (AC: 3)
  - [x] Create `Schemas/workflow-schema.json` defining the workflow.yaml structure
  - [x] Schema must match the `WorkflowDefinition`/`StepDefinition` model exactly
  - [x] Mark as embedded resource so it ships with the NuGet package
- [x] Task 5: Create YAML parsing service (AC: 1, 2, 6)
  - [x] Create `Workflows/IWorkflowParser.cs` interface with `Parse(string yamlContent)` method
  - [x] Create `Workflows/WorkflowParser.cs` implementing deserialization with YamlDotNet
  - [x] Handle YAML parsing exceptions and convert to validation errors
- [x] Task 6: Write unit tests (AC: 7)
  - [x] Create `Workflows/WorkflowParserTests.cs` in test project
  - [x] Create `Workflows/WorkflowValidatorTests.cs` in test project
  - [x] Add sample workflow YAML files to `Fixtures/SampleWorkflows/`
  - [x] Test valid workflow parsing (all fields populated correctly)
  - [x] Test missing required fields (name, steps)
  - [x] Test empty steps array
  - [x] Test unknown/unexpected properties are rejected
  - [x] Test wrong types (e.g. mode: 123 instead of string)
  - [x] Test error messages contain field path and actionable description
  - [x] Test snake_case → PascalCase mapping works correctly

## Dev Notes

### Architecture & Boundaries

- **All files go in `Workflows/` folder** — this is within the engine boundary (pure .NET, zero Umbraco dependencies)
- `Workflows/` can depend on: `YamlDotNet`, `System.Text.Json` — nothing else
- Do NOT add any `using Umbraco.*` statements in any file created by this story

### YamlDotNet Configuration

- Use `DeserializerBuilder` with `WithNamingConvention(UnderscoredNamingConvention.Instance)` for automatic snake_case → PascalCase mapping
- Alternatively, use `[YamlMember(Alias = "field_name")]` attributes on properties — pick one approach, not both
- **YAML date coercion warning:** YamlDotNet silently coerces date-like strings (e.g., `2026-03-30`) into DateTime objects. All string properties that might contain date-like values need explicit string typing. This is unlikely in workflow.yaml but be aware.
- Configure the deserializer to **reject unmapped properties** to satisfy NFR10 — use `WithEnforceNullability()` and handle unmapped keys

### JSON Schema Approach

- The formal JSON Schema is delivered in Epic 9 (Story 9.2), but we need a working schema now for validation
- Create `Schemas/workflow-schema.json` with `additionalProperties: false` at all levels to enforce strict validation
- Use `System.Text.Json` to load the schema at runtime — do NOT add a JSON Schema validation library (JsonSchema.Net etc.) unless absolutely necessary
- **Pragmatic approach:** The `WorkflowValidator` can implement structural validation in C# code (required fields, types, enums, no unknown properties) rather than requiring a full JSON Schema validation library. The `workflow-schema.json` file ships for IDE autocomplete; runtime validation is code-based.
- If code-based validation is used, the `workflow-schema.json` must still accurately describe the same rules for consistency

### Model Design

```
WorkflowDefinition
├── Name (string, required)
├── Description (string, required)
├── Mode (string, required — "interactive" or "autonomous")
├── DefaultProfile (string, optional — maps from default_profile)
└── Steps (List<StepDefinition>, required, min 1)

StepDefinition
├── Id (string, required — snake_case)
├── Name (string, required)
├── Agent (string, required — relative path to .md file)
├── Profile (string, optional — overrides workflow default_profile)
├── Tools (List<string>, optional — tool names)
├── ReadsFrom (List<string>, optional — artifact file paths)
├── WritesTo (List<string>, optional — artifact file paths)
└── CompletionCheck (CompletionCheckDefinition, optional)

CompletionCheckDefinition
└── FilesExist (List<string>, required — maps from files_exist)
```

### Validation Rules

Required fields with actionable error messages:
- `name` missing → "Workflow is missing required field 'name'"
- `steps` missing → "Workflow is missing required field 'steps'"
- `steps` empty → "Workflow must have at least one step"
- `mode` invalid → "Workflow 'mode' must be 'interactive' or 'autonomous', got '{value}'"
- Step missing `id` → "Step at index {i} is missing required field 'id'"
- Step missing `name` → "Step '{id}' is missing required field 'name'"
- Step missing `agent` → "Step '{id}' is missing required field 'agent'"
- Unknown property → "Unexpected property '{key}' in {location}" (where location = "workflow root" or "step '{id}'")
- Duplicate step IDs → "Duplicate step id '{id}' found at index {i}"

### Existing Code to Build On

- `AgentRunnerOptions.cs` already has `WorkflowPath` property at `Configuration/AgentRunnerOptions.cs` — the parser doesn't need this (it takes raw YAML content), but it confirms the config exists for Story 2.2
- `AgentRunnerComposer.cs` exists at `Composers/AgentRunnerComposer.cs` — do NOT register services yet (that's Story 2.2 when the registry is built)
- `GlobalUsings.cs` already has `System.Text.Json` and `Microsoft.Extensions.Logging`
- Test project has empty `Workflows/` and `Fixtures/SampleWorkflows/` folders ready

### Deferred Work Awareness

From prior code review: `AgentRunnerOptions` string properties don't guard against null. Not this story's concern — noted for when options binding is wired up.

### What NOT to Build

- **NOT** `WorkflowRegistry` or folder scanning/discovery — that's Story 2.2
- **NOT** DI registration of parser/validator — Story 2.2 registers everything
- **NOT** API endpoints — Story 2.3
- **NOT** agent markdown file loading or existence checking — Story 2.2
- **NOT** tool name validation against registered tools — Story 5.4
- **NOT** profile resolution or validation — Story 4.2
- Keep models, parser, and validator as standalone classes that can be instantiated directly in tests without DI

### Test Fixtures

Create sample YAML files in `Fixtures/SampleWorkflows/`:
- `valid-workflow.yaml` — complete valid workflow with 2+ steps
- `minimal-workflow.yaml` — valid workflow with only required fields
- `missing-name.yaml` — missing name field
- `missing-steps.yaml` — missing steps array
- `empty-steps.yaml` — steps array is empty
- `unknown-properties.yaml` — has extra undeclared properties
- `invalid-mode.yaml` — mode field has wrong value

### Project Structure Notes

- Files align with architecture: `Workflows/WorkflowDefinition.cs`, `Workflows/StepDefinition.cs`, `Workflows/WorkflowValidator.cs`
- Namespace: `Shallai.UmbracoAgentRunner.Workflows`
- Test namespace: `Shallai.UmbracoAgentRunner.Tests.Workflows`
- No conflicts with existing structure — `Workflows/` folder exists but is empty

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 2, Story 2.1]
- [Source: _bmad-output/planning-artifacts/architecture.md — Workflow YAML Schema Conventions, lines 416-442]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure, lines 620-626]
- [Source: _bmad-output/planning-artifacts/architecture.md — Engine Boundary, lines 734-750]
- [Source: _bmad-output/planning-artifacts/prd.md — FR2, FR5, NFR10]
- [Source: _bmad-output/project-context.md — YAML date coercion warning, YamlDotNet naming]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

N/A — clean implementation, no debugging required.

### Review Pass 2 Notes (2026-03-30)

- Three-layer adversarial review (Blind Hunter, Edge Case Hunter, Acceptance Auditor) produced 25 raw findings across layers, collapsed to 10 unique after dedup — 8 dismissed, 2 patched
- Parser/Validator design split (IgnoreUnmatchedProperties vs strict rejection) flagged by all 3 layers — confirmed as deliberate, documented in pass 1 notes
- List item type validation (tools, reads_from, writes_to accepting non-string items) re-flagged — already deferred to Story 5.4 in pass 1
- Two deserializer instances (parser uses UnderscoredNamingConvention, validator uses raw Dictionary) is inherent to the design — parser needs typed deserialization, validator needs raw key access for unknown property detection
- YamlDotNet scalar resolution in `Dictionary<object, object>` context: unquoted integers (e.g. `mode: 123`) are kept as strings, confirmed by passing tests — worth knowing for future YAML work

### Completion Notes List

- Added YamlDotNet 16.3.0 NuGet package to main project
- Created three model classes (WorkflowDefinition, StepDefinition, CompletionCheckDefinition) in Workflows/ with PascalCase properties
- Created WorkflowValidator with IWorkflowValidator interface — code-based structural validation checking required fields, types, valid mode enum values, duplicate step IDs, and unknown properties (NFR10)
- Created WorkflowValidationResult/WorkflowValidationError for structured error reporting with field paths
- Created workflow-schema.json as embedded resource for IDE autocomplete — mirrors validator rules exactly
- Created WorkflowParser with IWorkflowParser interface — uses YamlDotNet DeserializerBuilder with UnderscoredNamingConvention for automatic snake_case → PascalCase mapping
- Parser uses IgnoreUnmatchedProperties() since strict checking is the validator's responsibility
- 19 new tests (6 parser + 13 validator) covering all ACs: valid parsing, missing fields, empty steps, unknown properties, invalid mode, wrong types, duplicate IDs, snake_case mapping, malformed YAML, error message clarity
- 7 sample YAML fixture files created for test coverage
- All 21 tests pass (2 existing + 19 new), zero regressions

### Review Findings

- [x] [Review][Decision] Mode validation case sensitivity — changed to `StringComparer.Ordinal` (strict lowercase), matches JSON schema
- [x] [Review][Patch] Root-level `ValidateRequiredString` now rejects whitespace-only strings — added `IsNullOrWhiteSpace` check
- [x] [Review][Patch] `completion_check` with non-mapping type now reports error
- [x] [Review][Patch] `completion_check` missing or empty `files_exist` now reports error
- [x] [Review][Patch] Step `id` now rejects empty/whitespace strings
- [x] [Review][Patch] Removed dead `UnderscoredNamingConvention` from validator deserializer
- [x] [Review][Defer] YAML date coercion on string fields — unquoted date-like values (e.g. `id: 2026-03-30`) silently coerced by YamlDotNet. Project context warns about this. Unlikely in workflow YAML but affects broader YamlDotNet usage — deferred, cross-cutting concern
- [x] [Review][Defer] List item type validation — `tools`, `reads_from`, `writes_to` accept non-string items silently. Validator scope is structural; semantic tool validation is Story 5.4 — deferred, future story scope

#### Review Pass 2 (2026-03-30)

- [x] [Review][Patch] Validator catches bare `Exception` — narrowed to `YamlException` to avoid swallowing/misreporting unrelated exceptions [WorkflowValidator.cs:40]
- [x] [Review][Patch] `WorkflowParser.Parse` null input throws unguarded `ArgumentNullException` — added `ArgumentNullException.ThrowIfNull(yamlContent)` guard [WorkflowParser.cs:16]

### File List

- `Shallai.UmbracoAgentRunner/Shallai.UmbracoAgentRunner.csproj` (modified — added YamlDotNet package, embedded resource)
- `Shallai.UmbracoAgentRunner/Workflows/WorkflowDefinition.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/StepDefinition.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/CompletionCheckDefinition.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/IWorkflowValidator.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/WorkflowValidator.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/WorkflowValidationResult.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/WorkflowValidationError.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/IWorkflowParser.cs` (new)
- `Shallai.UmbracoAgentRunner/Workflows/WorkflowParser.cs` (new)
- `Shallai.UmbracoAgentRunner/Schemas/workflow-schema.json` (new)
- `Shallai.UmbracoAgentRunner.Tests/Shallai.UmbracoAgentRunner.Tests.csproj` (modified — YAML fixture copy)
- `Shallai.UmbracoAgentRunner.Tests/Workflows/WorkflowParserTests.cs` (new)
- `Shallai.UmbracoAgentRunner.Tests/Workflows/WorkflowValidatorTests.cs` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/SampleWorkflowLoader.cs` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/valid-workflow.yaml` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/minimal-workflow.yaml` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/missing-name.yaml` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/missing-steps.yaml` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/empty-steps.yaml` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/unknown-properties.yaml` (new)
- `Shallai.UmbracoAgentRunner.Tests/Fixtures/SampleWorkflows/invalid-mode.yaml` (new)
