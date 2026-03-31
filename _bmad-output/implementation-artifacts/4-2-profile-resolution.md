# Story 4.2: Profile Resolution

Status: done

## Story

As a developer,
I want the engine to resolve the correct Umbraco.AI profile for each step via a fallback chain,
so that each step uses the appropriate model and configuration without requiring explicit profile declaration on every step.

## Acceptance Criteria

1. **Given** a step is about to execute, **when** the ProfileResolver resolves the profile, **then** it checks for a step-level profile alias first
2. If no step-level profile exists, it falls back to the workflow's `default_profile`
3. If no workflow `default_profile` exists, it falls back to the site-level default profile from `AgentRunnerOptions.DefaultProfile` (FR14)
4. The resolved profile alias is used to call `IAIChatService.GetChatClientAsync(profileId)` to obtain a middleware-wrapped `IChatClient`
5. If the resolved profile alias does not exist in Umbraco.AI configuration, a `ProfileNotFoundException` is thrown with message: `"Profile '{alias}' not found in Umbraco.AI configuration"` (FR55)
6. A provider prerequisite check detects whether Umbraco.AI has at least one provider configured, returning a clear status for the dashboard to display guidance if not (FR53)
7. The `IProfileResolver` interface is registered in `AgentRunnerComposer`
8. Unit tests verify the 3-tier fallback chain, missing profile error, and provider prerequisite detection

## What NOT to Build

- No step execution or tool loop (Story 4.3)
- No prompt assembly changes (Story 4.1 — done)
- No completion checking or artifact validation (Story 4.4)
- No SSE streaming or chat events (Story 4.5)
- No conversation persistence (Story 6.1)
- No actual tool implementations (Epic 5)
- No dashboard UI for provider prerequisite display (Story 4.5 handles the UX)
- Do NOT modify Engine/ boundary rules — ProfileResolver is the **documented exception** that references Umbraco types from Engine/

## Tasks / Subtasks

- [x] Task 1: Add Umbraco.AI package reference (AC: all)
  - [x] 1.1: Add `<PackageReference Include="Umbraco.AI" Version="1.7.0" />` to `Shallai.UmbracoAgentRunner.csproj`
  - [x] 1.2: Verify `dotnet build Shallai.UmbracoAgentRunner.slnx` succeeds with new dependency

- [x] Task 2: Create `IProfileResolver` interface in Engine/ (AC: #1-#5)
  - [x] 2.1: Create `Engine/IProfileResolver.cs` with two methods:
    - `Task<IChatClient> ResolveAndGetClientAsync(StepDefinition step, WorkflowDefinition workflow, CancellationToken cancellationToken)` — resolves profile alias via fallback chain, then calls `IAIChatService.CreateChatClientAsync()` to return a configured `IChatClient`
    - `Task<bool> HasConfiguredProviderAsync(CancellationToken cancellationToken)` — checks whether at least one Umbraco.AI provider is configured
  - [x] 2.2: Return type for `ResolveAndGetClientAsync` is `IChatClient` (from `Microsoft.Extensions.AI`)

- [x] Task 3: Create `ProfileNotFoundException` in Engine/ (AC: #5)
  - [x] 3.1: Create `Engine/ProfileNotFoundException.cs` inheriting from `AgentRunnerException`
  - [x] 3.2: Constructor takes `string profileAlias` and generates message: `"Profile '{profileAlias}' not found in Umbraco.AI configuration"`

- [x] Task 4: Implement `ProfileResolver` in Engine/ (AC: #1-#6)
  - [x] 4.1: Create `Engine/ProfileResolver.cs` that implements `IProfileResolver`
  - [x] 4.2: Constructor injects: `IAIChatService`, `IOptions<AgentRunnerOptions>`, `ILogger<ProfileResolver>`
  - [x] 4.3: Implement `ResolveProfileAlias` private method with 3-tier fallback: `step.Profile` → `workflow.DefaultProfile` → `options.DefaultProfile`
  - [x] 4.4: If all three are null/empty, throw `ProfileNotFoundException` with alias `"(none configured)"`
  - [x] 4.5: Call `IAIChatService.CreateChatClientAsync(chat => chat.WithProfile(resolvedAlias))` — if the profile doesn't exist, Umbraco.AI throws; catch and wrap in `ProfileNotFoundException`
  - [x] 4.6: Implement `HasConfiguredProviderAsync` — call `IAIChatService.CreateChatClientAsync(_ => { })` wrapped in try/catch. If it succeeds, at least one provider is configured. If it throws, no provider available. Log the result.
  - [x] 4.7: Add structured logging: `"Profile resolved for step {StepId}: {ProfileAlias} (source: {ProfileSource})"` where source is "step", "workflow", or "site-default"

- [x] Task 5: Register in DI (AC: #7)
  - [x] 5.1: Add `builder.Services.AddSingleton<IProfileResolver, ProfileResolver>();` to `AgentRunnerComposer.Compose()`

- [x] Task 6: Write unit tests (AC: #8)
  - [x] 6.1: Test step-level profile takes priority over workflow and site defaults
  - [x] 6.2: Test workflow-level fallback when step has no profile
  - [x] 6.3: Test site-level fallback when neither step nor workflow have a profile
  - [x] 6.4: Test `ProfileNotFoundException` thrown when all three levels are empty/null
  - [x] 6.5: Test `ProfileNotFoundException` thrown when resolved alias doesn't exist in Umbraco.AI (mock `IAIChatService.CreateChatClientAsync` to throw)
  - [x] 6.6: Test error message contains the profile alias
  - [x] 6.7: Test `HasConfiguredProviderAsync` returns true when provider is available
  - [x] 6.8: Test `HasConfiguredProviderAsync` returns false when no provider configured (mock throws)
  - [x] 6.9: Test that `CreateChatClientAsync` is called with the correct resolved alias
  - [x] 6.10: Verify structured log output includes StepId, ProfileAlias, ProfileSource fields

## Dev Notes

### Architecture Boundary — ProfileResolver Is the Documented Exception

The architecture explicitly states that `Engine/ProfileResolver.cs` lives in Engine/ but belongs to the **Umbraco Integration Boundary**:

> **Umbraco Integration Boundary:** Composers/, Endpoints/, Engine/ProfileResolver.cs — these reference Umbraco types
> ProfileResolver bridges the engine to Umbraco.AI's IAIChatService and IAIProfileService

This means:
- `IProfileResolver.cs` — interface in Engine/, returns `IChatClient` from `Microsoft.Extensions.AI` (NOT an Umbraco type)
- `ProfileResolver.cs` — implementation in Engine/, imports `Umbraco.AI` types (this is the **only** file in Engine/ allowed to do so)
- All other Engine/ files remain zero-Umbraco-dependency

### Package Reference Required

The main `Shallai.UmbracoAgentRunner.csproj` currently has NO reference to `Umbraco.AI`. You must add:

```xml
<PackageReference Include="Umbraco.AI" Version="1.7.0" />
```

The test site already references `Umbraco.AI 1.7.0` — match this version exactly. Do NOT add `Umbraco.AI.Agent`, `Umbraco.AI.Anthropic`, or `Umbraco.AI.OpenAI` — those are provider packages installed by the end user, not dependencies of this package.

### IAIChatService API Surface

`IAIChatService` is the mandated LLM entry point (NFR16). Key method:

```csharp
Task<IChatClient> GetChatClientAsync(string profileId, CancellationToken cancellationToken = default);
```

- Returns a middleware-wrapped `IChatClient` (from `Microsoft.Extensions.AI`)
- The middleware pipeline includes: logging, governance, token tracking
- If `profileId` doesn't match a configured profile, it throws an exception
- The returned `IChatClient` is what Story 4.3's StepExecutor will use for the tool loop

**DO NOT use Umbraco.AI.Agent runtime** — it stores scope in `HttpContext.Items`, which is null inside `IHostedService`. This is a critical constraint documented in project-context.md and the retrospective.

### Fallback Chain Logic

```
Step.Profile → WorkflowDefinition.DefaultProfile → AgentRunnerOptions.DefaultProfile
```

Example from the test site's `workflow.yaml`:
```yaml
default_profile: anthropic-claude
steps:
  - id: gather_content
    # No profile → uses workflow default "anthropic-claude"
  - id: analyse_quality
    profile: openai-gpt4  # Step override → uses "openai-gpt4"
```

If `AgentRunnerOptions.DefaultProfile` is also empty, throw `ProfileNotFoundException` — the system cannot function without at least one profile.

### Provider Prerequisite Check (FR53)

This is a safety net for the first-run experience. The dashboard (Story 4.5) needs to know whether ANY Umbraco.AI provider is configured so it can show guidance like: *"Configure an AI provider in Umbraco.AI before workflows can run."*

Implementation approach: attempt `GetChatClientAsync` with a known or default profile. If it succeeds, a provider exists. If it throws due to no configured providers, return false. This is a best-effort check — it doesn't need to be perfect, just catch the obvious "no providers at all" case.

### Existing Types to Use

| Type | Location | Usage |
|------|----------|-------|
| `StepDefinition` | `Workflows/StepDefinition.cs` | Has `Profile` (nullable string) |
| `WorkflowDefinition` | `Workflows/WorkflowDefinition.cs` | Has `DefaultProfile` (nullable string) |
| `AgentRunnerOptions` | `Configuration/AgentRunnerOptions.cs` | Has `DefaultProfile` (string, defaults to `string.Empty`) |
| `AgentRunnerException` | `Engine/AgentRunnerException.cs` | Base exception class |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | DI registration point |

### Testing Approach

- Mock `IAIChatService` using NSubstitute — it's an interface
- Mock returns a substitute `IChatClient` for valid profiles
- Mock throws for invalid profiles to test error wrapping
- Use `IOptions<AgentRunnerOptions>` with `Options.Create(new AgentRunnerOptions { ... })`
- Use `NullLogger<ProfileResolver>` for logger
- No file system interaction needed — this is pure service logic
- Verify mock was called with correct profile alias using `Received(1).GetChatClientAsync(expectedAlias, Arg.Any<CancellationToken>())`

### Namespace

- Interface: `Shallai.UmbracoAgentRunner.Engine`
- Implementation: `Shallai.UmbracoAgentRunner.Engine`
- Exception: `Shallai.UmbracoAgentRunner.Engine`
- Tests: `Shallai.UmbracoAgentRunner.Tests.Engine`

### Project Structure Notes

- All new files in Engine/ — consistent with architecture's project structure
- `ProfileNotFoundException` co-located in Engine/ (same pattern as `AgentFileNotFoundException` and `AgentRunnerException` from Story 4.1)
- Test file: `Shallai.UmbracoAgentRunner.Tests/Engine/ProfileResolverTests.cs`

### File Placement

| File | Path | Action |
|------|------|--------|
| Shallai.UmbracoAgentRunner.csproj | `Shallai.UmbracoAgentRunner/Shallai.UmbracoAgentRunner.csproj` | Modified (add Umbraco.AI reference) |
| IProfileResolver.cs | `Shallai.UmbracoAgentRunner/Engine/IProfileResolver.cs` | New |
| ProfileResolver.cs | `Shallai.UmbracoAgentRunner/Engine/ProfileResolver.cs` | New |
| ProfileNotFoundException.cs | `Shallai.UmbracoAgentRunner/Engine/ProfileNotFoundException.cs` | New |
| AgentRunnerComposer.cs | `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` | Modified (add DI registration) |
| ProfileResolverTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/ProfileResolverTests.cs` | New |

### Retrospective Intelligence

From Epics 1-3 retrospective (2026-03-31):

- **Simplest fix first** — try the obvious solution before iterating. The fallback chain is straightforward; don't overcomplicate it with abstract strategy patterns or chain-of-responsibility
- **Project-context.md is load-bearing** — it documents the critical constraint about not using Umbraco.AI.Agent runtime. Follow it
- **Live provider testing** — this story should include a manual E2E validation step: configure a real Anthropic profile in the test site, call `ProfileResolver`, verify it returns a working `IChatClient`. This catches integration issues that mocks miss
- **Test target ~10 per story** — the 10 test cases above align with this guideline
- **Code review catches what planning misses** — expect the 3-layer review to verify the Umbraco.AI integration details are correct

### Manual E2E Validation

After automated tests pass, manually verify with the test site:

1. Ensure `Shallai.UmbracoAgentRunner.TestSite` has at least one Umbraco.AI profile configured (e.g., "anthropic-claude")
2. Set `Shallai:AgentRunner:DefaultProfile` to that profile alias in `appsettings.json`
3. Verify the test site builds and starts without errors
4. The real integration test is in Story 4.3 when the StepExecutor uses the resolved `IChatClient` — but confirming the package reference doesn't cause build/startup issues is the validation gate for this story

### Build Verification

Run `dotnet test Shallai.UmbracoAgentRunner.slnx` — never bare `dotnet test`.

### References

- [Source: _bmad-output/planning-artifacts/architecture.md — Decision 1: Step Executor Integration Layer]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure & Boundaries]
- [Source: _bmad-output/planning-artifacts/epics.md — Epic 4, Story 4.2]
- [Source: _bmad-output/planning-artifacts/prd.md — FR14, FR53, FR55, NFR16]
- [Source: _bmad-output/project-context.md — IAIChatService rules]
- [Source: _bmad-output/implementation-artifacts/epic-1-2-3-retro-2026-03-31.md — Process rules]

### Review Findings

- [x] [Review][Patch] `OperationCanceledException` swallowed in `ResolveAndGetClientAsync` — added explicit `catch (OperationCanceledException) { throw; }` before generic catch
- [x] [Review][Patch] `OperationCanceledException` swallowed in `HasConfiguredProviderAsync` — added explicit `catch (OperationCanceledException) { throw; }` before generic catch
- [x] [Review][Patch] `IChatClient` leaked in `HasConfiguredProviderAsync` — added `(client as IDisposable)?.Dispose()` after probe call
- [x] [Review][Patch] Whitespace-only profile strings — changed `IsNullOrEmpty` to `IsNullOrWhiteSpace` in all three fallback checks
- [x] [Review][Patch] Added 3 tests: cancellation propagation for both methods + inner exception preservation
- [x] [Review][Defer] `ProfileNotFoundException` missing typed `ProfileAlias` property — alias only in message string [Engine/ProfileNotFoundException.cs] — deferred, nice-to-have not required by spec

## Dev Agent Record

### Agent Model Used
Claude Opus 4.6

### Debug Log References
- Story spec referenced `IAIChatService.GetChatClientAsync(string profileId)` but actual Umbraco.AI 1.7.0 API uses `Guid? profileId` (obsolete) or `CreateChatClientAsync(Action<AIChatBuilder>)` (current). Used the non-obsolete `CreateChatClientAsync` with `AIChatBuilder.WithProfile(string alias)` builder pattern.
- `IAIChatService` lives in `Umbraco.AI.Core.Chat`, `AIChatBuilder` lives in `Umbraco.AI.Core.InlineChat` — discovered via decompilation of `Umbraco.AI.Core.dll`.
- `Umbraco.AI` 1.7.0 is a meta-package (no DLLs) — actual types come from transitive `Umbraco.AI.Core` 1.7.0.

### Completion Notes List
- Task 1: Added `Umbraco.AI 1.7.0` package reference. Build succeeds with 0 errors.
- Task 2: Created `IProfileResolver` interface with `ResolveAndGetClientAsync` and `HasConfiguredProviderAsync` methods.
- Task 3: Created `ProfileNotFoundException` inheriting from `AgentRunnerException` with profile alias in message.
- Task 4: Implemented `ProfileResolver` with 3-tier fallback chain (step → workflow → site-default), structured logging, and error wrapping. Used `CreateChatClientAsync` (non-obsolete API) instead of `GetChatClientAsync`.
- Task 5: Registered `IProfileResolver` as singleton in `AgentRunnerComposer`.
- Task 6: 10 unit tests covering all ACs — 3-tier fallback, missing profile errors, provider prerequisite check, alias verification, structured logging. All 107 tests pass (10 new + 97 existing, 0 regressions).

### File List
- `Shallai.UmbracoAgentRunner/Shallai.UmbracoAgentRunner.csproj` — Modified (added Umbraco.AI 1.7.0 reference)
- `Shallai.UmbracoAgentRunner/Engine/IProfileResolver.cs` — New
- `Shallai.UmbracoAgentRunner/Engine/ProfileResolver.cs` — New
- `Shallai.UmbracoAgentRunner/Engine/ProfileNotFoundException.cs` — New
- `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` — Modified (added DI registration)
- `Shallai.UmbracoAgentRunner.Tests/Engine/ProfileResolverTests.cs` — New
