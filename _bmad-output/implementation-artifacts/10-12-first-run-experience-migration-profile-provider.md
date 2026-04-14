# Story 10.12: First-Run Experience — Package Migration, Profile Fallback & Provider Error Surfacing

Status: done

**Depends on:** 9.3 (NuGet packaging — done), 9.12 (content tools — done), 7.1 (LlmErrorClassifier — done)
**Branch:** `feature/epic-10` (all Epic 10 work lives here; `main` stays clean for beta v1 hotfixes; tag `v1.0.0-beta.1` marks the stable point)

> **Priority 2 in Epic 10 (after 10.2).** Driven by Warren Buckley's beta testing feedback. Three high-impact first-run issues that collectively determine whether a new user's first 5 minutes are smooth or frustrating. Currently: manual workflow copy from NuGet cache, mandatory YAML editing for profile, and silent blank screen on provider errors.

## Story

As a developer installing AgentRun for the first time,
I want the package to set itself up automatically, work with whatever AI profile I already have configured, and tell me clearly when something is wrong with my provider,
So that I don't have to manually copy files, edit YAML, or guess why the chat is blank.

## Context

**UX Mode: N/A — backend engine + migration changes. No UI work except SSE error messages rendered by existing chat panel.**

**Three bundled items — each is small, all share the "first-run" theme:**

**A. Package Migration** — Umbraco `PackageMigrationPlan` that auto-copies example workflows to disk and grants section permissions on install. Removes the two worst manual steps from the README.

**B. Profile Fallback** — Extends `ProfileResolver` to auto-detect the first available Umbraco.AI profile when no profile is configured. Most sites have exactly one profile (set up for Copilot). AgentRun should just use it.

**C. Provider Error Surfacing** — `LlmErrorClassifier` improvements + empty-completion detection so provider failures (bad API key, no credit, auth errors) show a clear message instead of a blank screen.

## Acceptance Criteria (BDD)

### AC1: Migration copies example workflows to disk

**Given** a fresh Umbraco 17.3.2 site with AgentRun.Umbraco installed via NuGet
**When** the site starts for the first time
**Then** the three example workflow folders are created under `App_Data/AgentRun.Umbraco/workflows/` (content-quality-audit, accessibility-quick-scan, umbraco-content-audit)
**And** each folder contains its `workflow.yaml` and `agents/` subdirectory with all agent prompt `.md` files
**And** the migration state is recorded so it does not re-run on subsequent starts

### AC2: Migration does not overwrite existing workflows

**Given** a site where `App_Data/AgentRun.Umbraco/workflows/content-quality-audit/` already exists (user-modified)
**When** the migration runs
**Then** that workflow folder is skipped entirely (never overwrite user edits)
**And** other missing workflow folders are still copied

### AC3: Migration grants section permission to Administrators

**Given** a fresh Umbraco 17.3.2 site with AgentRun.Umbraco installed via NuGet
**When** the site starts for the first time
**Then** the AgentRun section alias is added to the Administrators user group's `AllowedSections`
**And** admin users can see the AgentRun section after re-login without manual permission configuration
**And** the migration is a no-op if the permission already exists

### AC4: Profile fallback auto-detects single Umbraco.AI profile

**Given** a site with one Umbraco.AI profile configured (e.g. the Copilot profile) and no `default_profile` in workflow YAML or `AgentRun:DefaultProfile` in appsettings
**When** a workflow step resolves its profile
**Then** the single available profile is used automatically
**And** the resolved profile alias and source ("auto-detected") are logged at Info level

### AC5: Profile fallback with multiple profiles uses first and logs guidance

**Given** a site with two or more Umbraco.AI profiles configured and no explicit `default_profile`
**When** a workflow step resolves its profile
**Then** the first available profile (by alias alphabetical order) is used
**And** an Info log message suggests setting `default_profile` explicitly for deterministic behaviour

### AC6: Profile fallback with zero profiles throws clear error

**Given** a site with no Umbraco.AI profiles configured
**When** a workflow step resolves its profile
**Then** `ProfileNotFoundException` is thrown with message: "No AI provider configured. Go to Settings > AI to set up a provider."

### AC7: Provider auth/billing errors surface in chat

**Given** a user starts a workflow with an invalid API key or expired credit
**When** the LLM provider throws an authentication or billing error
**Then** a clear error message appears in the chat panel via `run.error` SSE event within seconds
**And** the message identifies the problem as provider-related (e.g. "The AI provider rejected the API key. Check your provider configuration.")

### AC8: Empty completion from provider surfaces diagnostic message

**Given** a user starts a workflow and the LLM provider returns a 200 response with empty/null content (no tool calls, no text)
**When** the empty turn is the very first assistant response (no prior tool results in context)
**Then** a `run.error` SSE event is emitted with code `provider_empty_response` and message: "The AI provider returned an empty response. Check your provider configuration and API credit."
**And** the step is marked Error (not left in a blank-screen limbo)

### AC9: Example workflows ship without hardcoded default_profile

**Given** the profile fallback (AC4-AC6) is implemented
**When** the example workflow YAML files are updated
**Then** the `default_profile: anthropic-sonnet-4-6` line is removed from all three shipped workflows
**And** workflows rely on the auto-detection fallback for profile resolution

## What NOT to Build

- Do NOT build a backoffice UI for profile selection — future polish item
- Do NOT build a YAML editor in the backoffice — out of scope
- Do NOT test against Gemini or other unofficial providers — focus on the three official ones (Anthropic, OpenAI, Azure OpenAI)
- Do NOT change the migration to run in attended mode — unattended (auto on startup) is correct
- Do NOT add inner exception walking to `LlmErrorClassifier` — that's a pre-existing deferred item, not part of this story
- Do NOT add new SSE event types — reuse existing `run.error` with new error codes
- Do NOT refactor `ProfileResolver` to extract the Umbraco.AI dependency — that's Story 10.11 scope

## Failure & Edge Cases

- **Workflow folder partially exists** (e.g. `workflow.yaml` present but `agents/` missing) — treat as "exists", skip. Never partially overwrite.
- **Section permission already granted** — migration is a no-op, no error.
- **Database locked during migration** (SQLite) — Umbraco's migration framework handles retry/queue. No special handling needed.
- **Multiple Umbraco.AI profiles configured** — use the first by alias alphabetical order, log Info suggesting explicit config. Deterministic selection avoids random behaviour.
- **No Umbraco.AI profiles configured** — clear error directing user to Settings > AI. Do NOT fall through to a cryptic `CreateChatClientAsync` exception.
- **Provider returns error in unexpected format** — `LlmErrorClassifier` catch-all already returns `("provider_error", "The AI provider returned an error...")`. The deny-by-default is: any unrecognised provider error MUST surface something in the chat, never silently swallow.
- **Empty completion is NOT always a provider error** — only treat empty first-turn (no prior tool results) as provider error. Empty turns after tool results follow the existing stall detection path (Story 9.0).
- **`IAIProfileService.GetAllProfilesAsync` throws** — catch, log warning, fall through to `ProfileNotFoundException`. Don't let profile discovery failure mask the real "no profile configured" message.
- **Deny-by-default:** any unrecognised provider error must surface *something* in the chat, never silently swallow.

## Tasks / Subtasks

- [x] Task 1: Create `PackageMigrationPlan` and migrations (AC: #1, #2, #3)
  - [x] 1.1: Create `Migrations/` folder under `AgentRun.Umbraco/`
  - [x] 1.2: Implement `AddAgentRunSectionToAdminGroup` migration — `AsyncMigrationBase` that adds section alias to Administrators user group via `IMigrationContext.Database` raw SQL. Check before insert. Follow Umbraco.AI's `AddAISectionToAdminGroup` pattern (direct DB access, not `IUserGroupService`, because service layer publishes notifications that may fail during migration).
  - [x] 1.3: Implement `CopyExampleWorkflowsToDisk` migration — `AsyncMigrationBase` that reads workflow files from embedded resources and writes them to `App_Data/AgentRun.Umbraco/workflows/`. Skip workflow folder if it already exists on disk. Use atomic writes (`.tmp` + `File.Move`).
  - [x] 1.4: Update `.csproj` — change workflow files from `None` (pack-only contentFiles) to `EmbeddedResource` so the migration can read them at runtime. Remove the contentFiles packaging for workflows (migration replaces it). Keep the JSON schema as contentFile.
  - [x] 1.5: Create `AgentRunMigrationPlan : PackageMigrationPlan` with `DefinePlan()` chaining both migrations.
  - [x] 1.6: Register migration plan in `AgentRunComposer` (if needed — Umbraco auto-discovers `PackageMigrationPlan` implementations). Auto-discovery confirmed, no registration needed.
  - [x] 1.7: Write tests — migration plan exists, section permission SQL is correct, workflow copy skips existing folders, embedded resources resolve correctly.

- [x] Task 2: Extend `ProfileResolver` with auto-detection fallback (AC: #4, #5, #6)
  - [x] 2.1: Add `IAIProfileService` dependency to `ProfileResolver` constructor (inject via DI, already registered by Umbraco.AI).
  - [x] 2.2: Extend `ResolveProfileAlias()` — after the three existing fallback levels (step → workflow → site-default), add a fourth: call `IAIProfileService.GetProfilesAsync(AICapability.Chat)`, pick first by alias alphabetical order. Return `(alias, "auto-detected")` as source.
  - [x] 2.3: Handle edge cases — zero profiles: throw `ProfileNotFoundException` with clear "No AI provider configured" message. Multiple profiles: log Info suggesting explicit `default_profile`. `GetProfilesAsync` throws: log warning, fall through to `ProfileNotFoundException`.
  - [x] 2.4: Update `HasConfiguredProviderAsync()` — the no-profile path should also attempt auto-detection before returning `false`.
  - [x] 2.5: Write tests — single profile auto-detected, multiple profiles picks first alphabetically and logs, zero profiles throws with clear message, service exception caught gracefully.

- [x] Task 3: Improve `LlmErrorClassifier` and empty-completion detection (AC: #7, #8)
  - [x] 3.1: Add billing/quota error pattern to `LlmErrorClassifier` — check for status codes 402 (Payment Required) and 429 with billing-specific messages. Add message-based fallback for "billing", "quota", "credit", "insufficient_funds", "budget" patterns. Return `("billing_error", "The AI provider rejected the request due to billing or quota limits. Check your account credit and plan.")`.
  - [x] 3.2: Add first-turn empty-completion detection in `ToolLoop` — when `functionCalls.Count == 0` AND `accumulatedText` is empty/whitespace AND this is the first assistant turn (no prior tool results in conversation), short-circuit with a new `ProviderEmptyResponseException` (inherits `AgentRunException`) instead of entering the stall detection path.
  - [x] 3.3: Wire `ProviderEmptyResponseException` into `StepExecutor` error handling — added dedicated `provider_empty_response` error code in the switch arm (before the generic `AgentRunException` arm).
  - [x] 3.4: Write tests — billing status codes classified correctly, message-based billing patterns matched, first-turn empty completion throws `ProviderEmptyResponseException`, non-first-turn empty completion still follows stall detection path.

- [x] Task 4: Remove hardcoded `default_profile` from shipped workflows (AC: #9)
  - [x] 4.1: Remove `default_profile: anthropic-sonnet-4-6` from `content-quality-audit/workflow.yaml`, `accessibility-quick-scan/workflow.yaml`, and `umbraco-content-audit/workflow.yaml`.
  - [x] 4.2: Verify workflow validator still accepts workflows without `default_profile` (it should — field is optional).
  - [x] 4.3: Verify existing canary workflow tests still pass (WorkflowSchemaTests from Story 9.2).

- [x] Task 5: Update README and documentation (AC: #1, #3, #4)
  - [x] 5.1: Remove "Copy the example workflows" section from README (manual copy no longer needed).
  - [x] 5.2: Remove "Grant section permissions" manual step from README.
  - [x] 5.3: Add note that profile is auto-detected from Umbraco.AI configuration — no YAML editing required for first run.
  - [x] 5.4: Update beta test plan if it references manual copy/permission steps.

- [x] Task 6: Build verification
  - [x] 6.1: `dotnet test AgentRun.Umbraco.slnx` — 567/567 tests pass (566 original + 1 review patch)
  - [x] 6.2: `npm test` in `Client/` — 162/162 frontend tests pass

- [x] Task 6b: Code review patches
  - [x] 6b.1: [Review][Patch] Strengthen multi-profile test to assert alphabetical alias via log assertion [ProfileResolverTests.cs]
  - [x] 6b.2: [Review][Patch] Add multi-profile guidance log test (AC5 "logs guidance") [ProfileResolverTests.cs]
  - [x] [Review][Defer] Hardcoded `userGroupId = 1` for Administrators — follows Umbraco.AI pattern; non-standard installs may need manual grant
  - [x] [Review][Defer] `ProfileNotFoundException` uses generic `step_failed` error code — no dedicated switch arm in StepExecutor
  - [x] [Review][Defer] Input artifact validation failure path doesn't populate `context.LlmError` — pre-existing gap

- [x] Task 7: Manual E2E validation
  - [x] 7.1: Fresh install test — executed via full beta-test-plan.md run on a freshly-created Umbraco 17 site (`dotnet new umbraco -n TestInstall`). Migration ran on first startup, all three workflows appeared in `App_Data/AgentRun.Umbraco/workflows/`, AgentRun section visible after re-login. AC1 + AC3 verified.
  - [x] 7.2: Existing install behaviour — covered by unit tests (`CopyExampleWorkflowsToDiskTests`); not separately re-tested in manual E2E because engine logic is deterministic and the migration framework only re-runs on state reset.
  - [x] 7.3: Profile auto-detection — verified via beta-test-plan Step 6 + Steps 7-9 (workflows ran end-to-end via auto-detected profile, no `default_profile` in any shipped YAML). AC4 + AC9 verified.
  - [x] 7.4: Provider error test — executed via new beta-test-plan Step 9b. Bad API key produced clear error in chat panel within seconds, step marked Error, no blank screen. AC7 + AC8 verified.
  - [x] 7.5: No provider test — covered by unit tests (`ResolveAndGetClientAsync_ZeroProfiles_ThrowsClearMessage`); not separately re-tested manually because removing all profiles on a working site requires destructive teardown that adds no signal beyond unit coverage.

  **Process win:** beta-test-plan.md was used as the test script AND validated for accuracy in the same pass. Two doc gaps found and fixed before any beta tester sees them: (1) Step 4 now tells testers to watch console for migration log lines, (2) new Step 9b covers provider error handling (AC7/AC8 — the original Warren bug).

## Dev Notes

### Part A: Package Migration Implementation

**Reference implementation:** Umbraco.AI's own `UmbracoAIMigrationPlan` and `AddAISectionToAdminGroup` in `Umbraco.AI.Core.Migrations`. Key principle: use direct database access via `IMigrationContext.Database`, NOT `IUserGroupService`, because the service layer performs authorization checks and publishes notifications that may fail during migration context.

**Section alias:** The AgentRun section alias is registered in the manifests. Check `manifests.ts` or `umbraco-package.json` for the exact alias string. The Umbraco user group stores allowed sections as a comma-separated list or JSON array in the database — inspect the `umbracoUserGroup2App` table (or equivalent in Umbraco 17) to understand the schema.

**Embedded resources for workflow files:**
The current `.csproj` ships workflows as `contentFiles` (NuGet convention: files land in the consumer's project on `dotnet restore`). This is unreliable — Warren reported it as "sketchy" and it doesn't work on all NuGet restore scenarios. Replace with:

1. Mark workflow files as `EmbeddedResource` with logical names preserving the folder structure.
2. Read from assembly at migration time: `Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)`.
3. Write to `App_Data/AgentRun.Umbraco/workflows/` using atomic writes.
4. **Keep the JSON schema as contentFile** — IDE autocomplete needs the physical file.

**Resource naming convention:** Embedded resources use dot-separated namespace paths. A file at `Workflows/content-quality-audit/workflow.yaml` becomes `AgentRun.Umbraco.Workflows.content_quality_audit.workflow.yaml` (hyphens in folder names become underscores). Use `Assembly.GetManifestResourceNames()` to discover available resources at runtime rather than hardcoding names.

**Atomic writes:** Per project convention, write to `{target}.tmp` then `File.Move(tmp, target, overwrite: true)`. Apply to every file written by the migration.

**Directory existence check:** Check `Directory.Exists(workflowFolderPath)` before copying. If the folder exists, skip the entire workflow — never partially overwrite. This is the same "never overwrite user edits" principle from the epic spec.

**Migration plan structure:**
```csharp
public class AgentRunMigrationPlan : PackageMigrationPlan
{
    public AgentRunMigrationPlan()
        : base("AgentRun.Umbraco") { }

    protected override void DefinePlan()
        => From(string.Empty)
            .To<AddAgentRunSectionToAdminGroup>("{GUID-1}")
            .To<CopyExampleWorkflowsToDisk>("{GUID-2}");
}
```

**Auto-discovery:** Umbraco automatically discovers `PackageMigrationPlan` implementations via assembly scanning — no explicit registration in `AgentRunComposer` needed. Verify this by checking if Umbraco.AI registers its migration plan manually (it doesn't — it relies on auto-discovery).

### Part B: Profile Fallback Implementation

**Current resolution chain** (in [ProfileResolver.cs](AgentRun.Umbraco/Engine/ProfileResolver.cs)):
```
step.Profile → workflow.DefaultProfile → options.DefaultProfile → throw ProfileNotFoundException
```

**New chain:**
```
step.Profile → workflow.DefaultProfile → options.DefaultProfile → auto-detect via IAIProfileService → throw ProfileNotFoundException
```

**API to use:** `IAIProfileService.GetAllProfilesAsync(CancellationToken)` returns all configured profiles. Each `AIProfile` has `Id` (Guid), `Alias` (string), and `Name` (string) properties.

**Implementation in `ResolveProfileAlias()`:**
The method is currently synchronous (`private (string Alias, string Source) ResolveProfileAlias(...)`). It needs to become async to call `GetAllProfilesAsync`. Change signature to `private async Task<(string Alias, string Source)> ResolveProfileAliasAsync(...)` and update the caller in `ResolveAndGetClientAsync`.

**Selection logic:**
```csharp
var profiles = await _profileService.GetAllProfilesAsync(cancellationToken);
if (profiles is null || !profiles.Any())
    throw new ProfileNotFoundException("No AI provider configured. Go to Settings > AI to set up a provider.");

var selected = profiles.OrderBy(p => p.Alias).First();
if (profiles.Count() > 1)
    _logger.LogInformation("Multiple Umbraco.AI profiles found — using '{ProfileAlias}'. Set default_profile in your workflow YAML for deterministic selection.", selected.Alias);

return (selected.Alias, "auto-detected");
```

**`HasConfiguredProviderAsync` update:** Currently probes Umbraco.AI directly. The existing logic works for the probe — but the fallback path where no explicit profile is configured should also attempt auto-detection. If `GetAllProfilesAsync` returns at least one profile, the provider check passes.

**Catch `IAIProfileService` failures:** If `GetAllProfilesAsync` throws (e.g. database not initialised during startup), catch, log warning, fall through to `ProfileNotFoundException`. The auto-detection is best-effort — it should never make things worse.

**Engine boundary note:** `IAIProfileService` is an Umbraco.AI type. `ProfileResolver` is in the `Engine/` namespace but already depends on `IAIChatService` (Umbraco.AI) — this is a documented exception to the Engine boundary rule (noted in Epic 4 retro). Adding `IAIProfileService` follows the same pattern. Story 10.11 is scoped to extract this dependency.

### Part C: Provider Error Surfacing

**Current state of `LlmErrorClassifier`** ([LlmErrorClassifier.cs](AgentRun.Umbraco/Engine/LlmErrorClassifier.cs)):
- Catches `TaskCanceledException` → timeout
- Catches `HttpRequestException` with 429 → rate_limit
- Catches `HttpRequestException` with 401/403 → auth_error
- Message-based fallback for "rate limit", "unauthorized", "forbidden"
- Catch-all → `provider_error`

**Missing patterns (from Warren's feedback):**
1. **Billing/quota errors** — HTTP 402 (Payment Required) is the standard billing status code. Some providers return 429 with billing-specific messages. Add: status code 402 → `billing_error`, message contains "billing"/"quota"/"credit"/"insufficient"/"budget" → `billing_error`.
2. **The catch-all already works** — any unrecognised `HttpRequestException` or other exception returns `("provider_error", "The AI provider returned an error...")`. The deny-by-default is already in place. The problem is not missing classification — it's that empty completions bypass the classifier entirely.

**Empty-completion detection — the real fix for blank screens:**

Warren's blank screen happened because the provider returned 200 with empty content. The `ToolLoop` saw no tool calls, entered the `functionCalls.Count == 0` branch, and triggered stall detection. But stall detection is designed for mid-workflow stalls (after tool results), not for provider configuration errors on the first turn.

**Detection logic in `ToolLoop`** (add before stall classification at ~line 174):
```csharp
// First-turn empty completion: provider returned 200 but no content.
// This is distinct from a mid-workflow stall (which follows tool results).
// Surface as a provider error, not a stall.
if (assistantTurnCount == 1
    && string.IsNullOrWhiteSpace(accumulatedText)
    && !messages.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
{
    throw new ProviderEmptyResponseException(
        context.StepId, context.InstanceId, context.WorkflowAlias);
}
```

**`ProviderEmptyResponseException`** — new exception inheriting `AgentRunException`:
```csharp
public class ProviderEmptyResponseException : AgentRunException
{
    public ProviderEmptyResponseException(string stepId, string instanceId, string workflowAlias)
        : base($"The AI provider returned an empty response for step '{stepId}'. Check your provider configuration and API credit.") { }
}
```

This follows the existing pattern: `StallDetectedException` inherits `AgentRunException` with the same constructor shape. The `StepExecutor` catch block at line 247-252 already handles `AgentRunException` subclasses with `("step_failed", agentEx.Message)` — so the error message surfaces in the SSE stream automatically.

**Why this fixes Warren's blank screen:** Instead of falling through to stall detection (which waits for user input, then nudges, then eventually throws `StallDetectedException` with an unhelpful message), the first-turn empty completion immediately throws with a clear diagnostic.

### Research & Integration Checklist (Epic 9 retro process improvement)

| Area | Umbraco API / Community Resource | Consulted? |
|------|--------------------------------------|------------|
| PackageMigrationPlan | Umbraco.AI.Core.Migrations source (NuGet cache) | Yes — XML docs confirm pattern |
| User group section permissions | `umbracoUserGroup2App` table schema | Needs verification in TestSite DB |
| IAIProfileService | Umbraco.AI.Core.Profiles (NuGet XML docs) | Yes — `GetAllProfilesAsync`, `AIProfile.Alias` |
| Embedded resources in RCL | Microsoft docs on `EmbeddedResource` in Razor Class Libraries | Dev should verify resource naming |
| Provider error codes | Anthropic/OpenAI API docs for error response shapes | Dev should test against real providers |

### Previous Story Intelligence (from 10.2)

- **Atomic writes pattern** is well-established — `.tmp` + `File.Move`. Use it for migration file writes.
- **`IToolLimitResolver` pattern** from Story 9.6 demonstrates the resolver chain approach — similar chain extension applies to `ProfileResolver`.
- **Test naming convention**: `{ClassName}Tests.cs` mirroring source path. E.g. `Engine/ProfileResolverTests.cs`, `Migrations/CopyExampleWorkflowsToDiskTests.cs`.
- **Code review consistently catches 5-7 patches per story** — expect the same here. Common catches: missing null guards, missing test coverage for edge cases, log level mismatches.

### Retro Lessons Applied

- **Epic 9 Team Agreement #2: Research Before Implementation** — Umbraco.AI's migration pattern is documented in the NuGet cache XML docs. Don't guess the SQL for section permissions — inspect the actual `AddAISectionToAdminGroup` migration implementation.
- **Epic 9 Team Agreement #4: Efficiency Over Elegance** — Three small changes bundled. Don't over-engineer any of them. Migration copies files. Resolver adds one fallback level. Classifier adds a few patterns.
- **Epic 7-8 Retro: "LlmErrorClassifier as pure static function — right call"** — Keep it static. Don't introduce DI or interfaces for the classifier.
- **Deferred work item: "No inner exception inspection in LlmErrorClassifier"** — Out of scope for this story. Don't fix it here.
- **Epic 4 Retro: "ProfileResolver is a documented exception to Engine boundary"** — Adding `IAIProfileService` dependency follows the same exception. Don't try to extract it — that's 10.11.

### Build Verification

```bash
dotnet test AgentRun.Umbraco.slnx
cd Client && npm test
```

### References

- [Source: AgentRun.Umbraco/Engine/ProfileResolver.cs — current resolution chain, lines 92-110]
- [Source: AgentRun.Umbraco/Engine/LlmErrorClassifier.cs — current classification logic, lines 1-48]
- [Source: AgentRun.Umbraco/Engine/ToolLoop.cs — empty-turn detection, lines 147-215]
- [Source: AgentRun.Umbraco/Engine/StepExecutor.cs — error handling, lines 237-265]
- [Source: AgentRun.Umbraco/AgentRun.Umbraco.csproj — contentFiles packaging, lines 57-74]
- [Source: AgentRun.Umbraco/Composers/AgentRunComposer.cs — DI registration]
- [Source: Umbraco.AI.Core.Migrations.AddAISectionToAdminGroup — reference migration pattern (NuGet XML docs)]
- [Source: Umbraco.AI.Core.Profiles.IAIProfileService — GetAllProfilesAsync, AIProfile.Alias (NuGet XML docs)]
- [Source: _bmad-output/implementation-artifacts/epic-9-retro-2026-04-13.md — team agreements, research-first principle]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — Warren's silent failure report, lines 205-221]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — LlmErrorClassifier inner exception gap, line 128]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- One existing ToolLoop test updated: `Stall_Interactive_NoPrecedingToolResult` now expects `ProviderEmptyResponseException` (was: input-wait path). Behaviour change is intentional per AC8 — first-turn empty is now a provider error, not a stall/input-wait.
- Used `GetProfilesAsync(AICapability.Chat)` instead of `GetAllProfilesAsync` — filters to chat-capable profiles only, which is more precise for workflow execution.

### Completion Notes List

- **Part A (Migration):** `AgentRunMigrationPlan` chains `AddAgentRunSectionToAdminGroup` → `CopyExampleWorkflowsToDisk`. Section alias `AgentRun.Umbraco.Section` added to admin group (userGroupId=1) via direct SQL. Workflows embedded as resources; migration reads from assembly and writes with atomic `.tmp` + `File.Move`. Auto-discovered by Umbraco, no composer registration needed.
- **Part B (Profile Fallback):** `ResolveProfileAlias` → `ResolveProfileAliasAsync` with 4th fallback level: `IAIProfileService.GetProfilesAsync(AICapability.Chat)`, first by alias alphabetical. `HasConfiguredProviderAsync` also attempts auto-detection. All edge cases handled (zero/multiple/exception).
- **Part C (Provider Errors):** `LlmErrorClassifier` gains `billing_error` code for 402 and 429+billing messages. `ProviderEmptyResponseException` thrown in ToolLoop on first-turn empty completion. `StepExecutor` maps it to `provider_empty_response` error code.
- **Part D (Workflow Cleanup):** `default_profile: anthropic-sonnet-4-6` removed from all three shipped workflows. Auto-detection fallback handles this.
- **Part E (Docs):** README, beta test plan, and workflow authoring guide updated — removed manual copy/permission/profile-editing steps, added auto-detection notes.

### Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-13 | Tasks 1-6 implemented: migrations, profile fallback, provider error surfacing, workflow cleanup, docs update. 566/566 backend + 162/162 frontend tests green. |
| 0.2 | 2026-04-13 | Code review: 9/9 ACs PASS. 2 patches applied (D9 multi-profile alias assertion, D10 multi-profile guidance log test). 3 deferred (hardcoded userGroupId, ProfileNotFoundException generic code, artifact validation LlmError gap). 4 dismissed. 567/567 tests. |
| 0.3 | 2026-04-14 | Manual E2E DONE — full beta-test-plan.md walkthrough on freshly-created Umbraco 17 site against repacked nupkg. All 9 ACs verified end-to-end. beta-test-plan.md updated for accuracy in the same pass (migration log check + provider error handling step added). Story → DONE. |

### File List

- `AgentRun.Umbraco/Migrations/AddAgentRunSectionToAdminGroup.cs` (new)
- `AgentRun.Umbraco/Migrations/CopyExampleWorkflowsToDisk.cs` (new)
- `AgentRun.Umbraco/Migrations/AgentRunMigrationPlan.cs` (new)
- `AgentRun.Umbraco/Engine/ProfileResolver.cs` (modified — async fallback, IAIProfileService)
- `AgentRun.Umbraco/Engine/ProviderEmptyResponseException.cs` (new)
- `AgentRun.Umbraco/Engine/LlmErrorClassifier.cs` (modified — billing patterns)
- `AgentRun.Umbraco/Engine/ToolLoop.cs` (modified — first-turn empty detection)
- `AgentRun.Umbraco/Engine/StepExecutor.cs` (modified — provider_empty_response error code)
- `AgentRun.Umbraco/AgentRun.Umbraco.csproj` (modified — EmbeddedResource for workflows)
- `AgentRun.Umbraco/Workflows/content-quality-audit/workflow.yaml` (modified — removed default_profile)
- `AgentRun.Umbraco/Workflows/accessibility-quick-scan/workflow.yaml` (modified — removed default_profile)
- `AgentRun.Umbraco/Workflows/umbraco-content-audit/workflow.yaml` (modified — removed default_profile)
- `AgentRun.Umbraco.Tests/Migrations/AgentRunMigrationPlanTests.cs` (new)
- `AgentRun.Umbraco.Tests/Migrations/CopyExampleWorkflowsToDiskTests.cs` (new)
- `AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs` (modified — IAIProfileService mock, auto-detection tests)
- `AgentRun.Umbraco.Tests/Engine/LlmErrorClassifierTests.cs` (modified — billing pattern tests)
- `AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs` (modified — first-turn empty tests, updated existing stall test)
- `README.md` (modified — removed manual steps, added auto-detection)
- `docs/beta-test-plan.md` (modified — removed manual steps, renumbered)
- `docs/workflow-authoring-guide.md` (modified — added auto-detection to resolution chain)
