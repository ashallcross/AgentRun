# Story 10.11: SSE Keepalive and Engine Boundary Cleanup

Status: done

**Depends on:** Story 10.1 (concurrency locking — done), Story 10.8 (cancel wiring — done), Story 10.9 (SSE disconnect resilience — done), Story 10.12 (first-run experience — done; added the `IAIProfileService` dependency this story extracts)
**Branch:** `feature/epic-10-continued`
**Priority:** 7th in Epic 10 (10.2 → 10.12 → 10.8 → 10.9 → 10.10 → 10.1 → 10.6 → **10.11** → 10.7 → 10.4 → 10.5)

> **Two independent tracks shipped as one story.** Both items came out of the [pre-launch code review (2026-04-10)](../planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md) and are individually too small for their own stories. They share zero code surface — Track A touches `SseEventEmitter`, Track B touches `ProfileResolver` — but they share a "production-readiness polish" theme and ship together for compounding review/E2E cost.
>
> **Track A (SSE keepalive):** Add periodic `: keepalive\n\n` SSE comment lines to `SseEventEmitter` so reverse proxies / load balancers with idle timeouts don't kill SSE connections during long LLM calls. Without this, customers running AgentRun behind nginx/IIS ARR/Cloudflare with default 30–60s idle-read timeouts will see Story 10.9's `Interrupted` flow fire spuriously every ~60s on a slow LLM step.
>
> **Track B (ProfileResolver Engine boundary cleanup):** [`ProfileResolver`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs) lives in `AgentRun.Umbraco/Engine/` but imports four `Umbraco.AI.Core.*` namespaces directly. This violates the "Engine has zero Umbraco dependencies" rule from [architecture.md Decision 1](../planning-artifacts/architecture.md). Story 10.12 made it worse by adding a second Umbraco.AI dependency (`IAIProfileService`) to the same class — explicitly noted in the 10.12 dev notes as "Story 10.11 is scoped to extract this dependency." Extract a new `IAIChatClientFactory` interface that lives in Engine/ and exposes only primitive types; move the Umbraco.AI implementation into `Services/UmbracoAIChatClientFactory.cs`.

## Story

As a developer running AgentRun behind a reverse proxy in production,
I want SSE streams to send periodic keepalive comments and the `Engine/` namespace to stop importing `Umbraco.AI.Core.*` from inside `ProfileResolver`,
So that proxy idle timeouts don't break long-running LLM steps and the Engine boundary rule from the architecture doc is honoured (preserving the option to extract Engine into a non-Umbraco package later).

## Context

**UX Mode: N/A** — Track A is invisible to end users (a comment line is not rendered by `EventSource`); Track B is a pure refactor with no observable change in behaviour. Both have backend test coverage; neither requires manual UI E2E beyond the regression smoke that existing flows still work.

### Track A — Why SSE keepalive matters

A long LLM step (think: scanner step pulling 5 URLs at ~10s each + Sonnet 4.6 thinking time of 30–60s on a complex prompt) can produce zero SSE events for 60+ seconds during the model's pre-tool-call thinking phase. The current [`SseEventEmitter`](../../AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs) only writes when a real event occurs (`text.delta`, `tool.start`, etc.); there is no heartbeat. The connection's only liveness signal between events is the TCP keep-alive packets, which fire every ~75s on Linux defaults — too late for most reverse proxies.

**Common reverse-proxy idle timeouts:**
- **nginx** `proxy_read_timeout` default: **60s**
- **IIS ARR** `serverRuntime requestTimeout` default: **120s**, but `proxy_responseBufferLimit` and `httpErrors` can shorten effective windows
- **Cloudflare** free tier: **100s** (proxied connections); orange-cloud always idles after this
- **AWS ALB** idle timeout default: **60s**
- **Azure App Service** idle timeout: **240s** (4 min) — generous, but still a wall

When the proxy closes the idle connection, AgentRun's [SSE OCE handler at ExecutionEndpoints.cs:332](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L332) interprets `Response.Body` write failure or `RequestAborted` as a Story 10.9 disconnect, transitions the instance to `Interrupted`, and the user sees Retry where they should have seen completion. The instance is recoverable via Retry, but the UX is broken — every multi-minute step looks like a connection failure.

The fix is RFC 6202 SSE comment lines: `: keepalive\n\n`. The colon-prefixed line is treated as a comment by `EventSource` clients (W3C spec: ["Lines starting with a colon ... are comments and must be ignored"](https://www.w3.org/TR/eventsource/#parsing-an-event-stream)) but is real bytes on the wire that satisfy proxy idle-read counters. No event is fired on the client; no SSE handler change required.

### Track B — Why `ProfileResolver` violates the Engine boundary

[`AgentRun.Umbraco/Engine/ProfileResolver.cs`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs) currently imports:

```csharp
using Umbraco.AI.Core.Chat;        // IAIChatService
using Umbraco.AI.Core.InlineChat;  // AIChatBuilder (used in lambda)
using Umbraco.AI.Core.Models;      // AICapability enum
using Umbraco.AI.Core.Profiles;    // IAIProfileService + AIProfile
```

The architecture has a hard rule from [architecture.md](../planning-artifacts/architecture.md):

> "Single RCL project structure with clean engine boundary (no Umbraco dependencies in Engine/, Workflows/, Instances/, Tools/, Security/, Models/)"

`ProfileResolver` is the **only** file in `Engine/` that violates this. Verified via:

```
grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ → 4 hits, all in ProfileResolver.cs
```

Story 10.12 added `IAIProfileService` (the auto-detect-first-profile fallback) and explicitly flagged the cleanup to this story. The pre-launch code review's compliance checklist marked the Engine-boundary rule as **"Deviates"** with a single citation: [`ProfileResolver.cs:5`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs#L5).

**Why the rule matters (from architecture rationale):**
- Keeps the option open to extract `Engine/` + `Instances/` + `Workflows/` into a non-Umbraco core package later. Right now `Engine/` is one file away from being Umbraco-free; that one file is `ProfileResolver`.
- Makes engine unit testing trivial — no Umbraco DI bootstrapping required for any test outside `ProfileResolverTests`.
- Mirrors the precedent already established by [`ConversationRecorder`](../../AgentRun.Umbraco/Services/ConversationRecorder.cs) — Engine-adjacent service that depends on `Engine/` interfaces but lives in `Services/` because it has framework-specific dependencies. This story applies the same pattern to the Umbraco.AI surface.

### The fix shape (minimum fix per epic)

**Track A — SSE keepalive emission:**

1. Extend [`SseEventEmitter`](../../AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs) with a `StartKeepaliveAsync(TimeSpan interval, CancellationToken ct)` method that returns a `Task` representing a long-running heartbeat loop. The method writes `: keepalive\n\n` to the stream every `interval`, exits cleanly on cancellation, and serialises against concurrent event writes via a private `SemaphoreSlim(1, 1)` so the heartbeat can't interleave with a half-written event.
2. Modify the existing `EmitAsync` to acquire the same `SemaphoreSlim` around its write+flush, so events and heartbeats serialise on the same lock.
3. Wire it from the SSE endpoint at [`ExecutionEndpoints.ExecuteSseAsync`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L311): start the heartbeat task before invoking `_workflowOrchestrator.ExecuteNextStepAsync`, link a CTS to the request token + a local "stream-finished" token, cancel it in the `finally`. The heartbeat task is fire-and-forget at the call site (don't `await`) — the cleanup of the linked CTS in `finally` guarantees it terminates.
4. Make the interval configurable via a new `KeepaliveInterval` (TimeSpan) on [`AgentRunOptions`](../../AgentRun.Umbraco/Configuration/AgentRunOptions.cs) with a 15-second default. 15s is well under the 60s nginx/AWS/Cloudflare floor, gives 4× safety margin, and is small enough to recover from transient single-packet drops without breaking the connection.
5. Heartbeat write failures (proxy already gone, client disconnected, stream disposed) must NOT propagate. Catch `IOException`, `ObjectDisposedException`, and `OperationCanceledException` inside the loop; log at Debug; exit the loop. The actual SSE write path will surface the disconnect through the orchestrator's normal flow.

**Track B — ProfileResolver Engine boundary:**

1. Define a new interface [`IAIChatClientFactory`](../../AgentRun.Umbraco/Engine/IAIChatClientFactory.cs) in `Engine/` that exposes only primitive / `Microsoft.Extensions.AI` types — no `Umbraco.AI.Core.*`:
   ```csharp
   public interface IAIChatClientFactory
   {
       Task<IChatClient> CreateChatClientAsync(string profileAlias, string requestAlias, CancellationToken cancellationToken);
       Task<IReadOnlyList<string>> GetChatProfileAliasesAsync(CancellationToken cancellationToken);
   }
   ```
   `IChatClient` lives in `Microsoft.Extensions.AI` (already a transitive dep via Umbraco.AI; not Umbraco-flavoured). Returning `IReadOnlyList<string>` of aliases instead of `IEnumerable<AIProfile>` keeps `AIProfile` out of `Engine/`.
2. Implement [`UmbracoAIChatClientFactory`](../../AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs) in `Services/` (mirrors the [`ConversationRecorder`](../../AgentRun.Umbraco/Services/ConversationRecorder.cs) precedent — Engine-adjacent service with framework-specific deps). Constructor takes `IAIChatService` + `IAIProfileService`. Implementation:
   - `CreateChatClientAsync` calls `_chatService.CreateChatClientAsync(chat => chat.WithAlias(requestAlias).WithProfile(profileAlias), ct)`. The `Umbraco.AI.Core.InlineChat.AIChatBuilder` lambda lives entirely inside this method body; never crosses the interface.
   - `GetChatProfileAliasesAsync` calls `_profileService.GetProfilesAsync(AICapability.Chat, ct)`, projects to `string[]` of aliases (preserving alphabetical sort or letting the caller sort — see locked decision 6), and returns. `AICapability` and `AIProfile` are confined to this method body.
3. Refactor [`ProfileResolver`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs):
   - Drop all four `using Umbraco.AI.Core.*` lines.
   - Constructor swaps `(IAIChatService, IAIProfileService, IOptions<AgentRunOptions>, ILogger)` → `(IAIChatClientFactory, IOptions<AgentRunOptions>, ILogger)`.
   - `ResolveAndGetClientAsync` calls `_chatClientFactory.CreateChatClientAsync(alias, $"step-{step.Id}", ct)`.
   - `HasConfiguredProviderAsync` calls `_chatClientFactory.CreateChatClientAsync(profile, "provider-check", ct)`.
   - Auto-detect path calls `_chatClientFactory.GetChatProfileAliasesAsync(ct)`, sorts case-insensitively (`StringComparer.OrdinalIgnoreCase`), takes `.First()`. The `OrderBy` + `First` logic is already in ProfileResolver today on `AIProfile.Alias` — the refactor moves the projection into the factory and keeps the sort-and-pick in ProfileResolver (so any future change to the selection rule stays in the engine, not the adapter).
   - The `try/catch (Exception ex) when (ex is not ProfileNotFoundException)` wrappers stay — the factory may surface `Umbraco.AI`-flavoured exceptions; ProfileResolver translates them into `ProfileNotFoundException` exactly as it does today.
4. Register the new factory in [`AgentRunComposer`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs#L37): one new line, `builder.Services.AddSingleton<IAIChatClientFactory, UmbracoAIChatClientFactory>();` placed just before the `AddSingleton<IProfileResolver, ProfileResolver>();` line.
5. Update [`ProfileResolverTests`](../../AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs): replace `_chatService` + `_profileService` mocks with a single `_chatClientFactory` mock. The test surface gets simpler — no more `AIProfile` construction, no more `AICapability.Chat` argument matchers. All existing assertions on selection logic, error translation, and fallback chains are preserved against the new mock.
6. Add a small new test file [`UmbracoAIChatClientFactoryTests`](../../AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs) covering the adapter's translation: `CreateChatClientAsync` produces the right `AIChatBuilder` shape, `GetChatProfileAliasesAsync` filters to `AICapability.Chat` and projects to aliases.

### Architect's locked decisions (do not relitigate)

1. **Two tracks ship as one story, not two.** Both are small (~1 day each), independent (no shared file), and share the same review/E2E overhead. Splitting them doubles the ceremony without adding value. Epic 10's spec at [epics.md:2171](../planning-artifacts/epics.md#L2171) explicitly bundles them: *"two small items from the pre-launch code review that are individually too small for their own stories but both improve production readiness."*
2. **SSE keepalive uses comment lines (`: keepalive\n\n`), not real events.** The W3C EventSource spec mandates that comment lines (colon-prefixed) are ignored by parsers. Real events would force the frontend to handle a noop event type and pollute the wire. Comment lines are zero-frontend-change and the canonical SSE keepalive pattern (see [HTML spec example](https://html.spec.whatwg.org/multipage/server-sent-events.html#authoring-notes)).
3. **Keepalive interval is 15s default, configurable via `AgentRunOptions.KeepaliveInterval`.** 15s gives 4× safety margin against the 60s common floor. Configurable so customers behind aggressive proxies (Cloudflare orange-cloud sometimes idles at 30s) can tighten it without a code change. Allowed range: 5s–300s; out-of-range values clamp to defaults with a warning log.
4. **Heartbeat and event writes serialise on a single `SemaphoreSlim(1, 1)` inside `SseEventEmitter`.** Without serialisation, a heartbeat can interleave with a half-written event JSON, producing a malformed SSE frame that crashes the client parser. The semaphore is per-emitter (each SSE request gets its own emitter, so contention is local to one stream — zero cross-request impact). Do NOT use `lock` — the emit methods are async and `lock` blocks across `await` are illegal.
5. **Heartbeat task is fire-and-forget at the call site, terminated via cancellation.** The SSE endpoint creates a linked CTS (request token + a local "stream-finished" token), launches the heartbeat with `_ = emitter.StartKeepaliveAsync(interval, linkedCts.Token)`, and cancels the local token in `finally`. Awaiting the heartbeat task at the call site would deadlock — the heartbeat exits when cancelled, which only happens after the orchestrator returns; the orchestrator can't return while the endpoint is awaiting the heartbeat. Pattern is fire-and-forget with deterministic cleanup via the linked CTS.
6. **`IAIChatClientFactory` returns `IReadOnlyList<string>` aliases, not `IEnumerable<AIProfile>`.** Returning AIProfile would leak the type into `Engine/` and require Engine to know about `AIProfile.Alias`, `.Name`, `.ConnectionId`. ProfileResolver only ever reads `.Alias`. Returning `string[]` keeps Engine type-clean and the conversion is one `.Select(p => p.Alias).ToArray()` in the adapter. If a future caller needs `AIProfile.Name` or `.ConnectionId`, the interface can grow a richer return type at that point — YAGNI applies now.
7. **Sort-and-pick logic stays in ProfileResolver, not in the adapter.** `OrderBy(alias, OrdinalIgnoreCase).First()` is engine policy — ProfileResolver's existing "pick alphabetically first; warn if multiple" rule. The adapter is a translation layer only. If a future change wants to pick by recency or by user preference, that's an engine change — keeping the policy in `Engine/` makes the change one-file.
8. **Adapter lives in `Services/`, not a new `Adapters/` namespace.** `Services/` already houses [`ConversationRecorder`](../../AgentRun.Umbraco/Services/ConversationRecorder.cs) — the precedent for "Engine-adjacent collaborator with framework-specific deps." Adding an `Adapters/` folder for one file is over-engineering; reuse the established pattern.
9. **Backward compatibility on `ProfileResolver` constructor: NONE.** The class is internal-DI-only — no public callers. The constructor signature change is an in-source refactor; tests need updating (Task 8) and that's the entire ripple. Do not preserve the old constructor with a `[Obsolete]` shim.
10. **Existing ProfileResolverTests rewrite scope: replace mocks, preserve assertions.** Every test in [`ProfileResolverTests.cs`](../../AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs) keeps its name, BDD intent, and assertions. The setup block changes from "mock IAIChatService + IAIProfileService" to "mock IAIChatClientFactory" — simpler. Verification calls (e.g. `_chatService.Received().CreateChatClientAsync(...)`) become `_chatClientFactory.Received().CreateChatClientAsync(profileAlias, requestAlias, ct)` — fewer argument matchers, more readable. Do NOT delete tests; do NOT add new tests beyond what AC11 specifies for the new adapter.
11. **`SseHelper.ConfigureSseResponse` stays unchanged.** The HTTP-level keep-alive header (`Connection: keep-alive`) is correct already; that's TCP-level. The keepalive added in this story is application-level, written through `SseEventEmitter`. Do NOT touch SseHelper.
12. **`AgentRunOptions.KeepaliveInterval` validation: clamp, don't throw.** Story 9.6's `IToolLimitResolver` precedent uses runtime clamping with warn-log for out-of-range values. Apply the same pattern: values < 5s clamp to 5s, values > 300s clamp to 300s, log warning once at startup. Throwing on bad config breaks app startup for a value that has a sensible default.
13. **No SSE protocol versioning, no opt-out flag.** The keepalive is invisible to clients (comment line); it can never break a frontend. There is no scenario where a customer needs to opt out. Setting `KeepaliveInterval` to a very large value (e.g. 300s) effectively disables it for production-broken cases; that's the only knob needed.

## Acceptance Criteria (BDD)

### AC1: SSE stream emits `: keepalive\n\n` at the configured interval

**Given** an SSE stream is open against `/instances/{id}/start` or `/instances/{id}/retry`
**And** `KeepaliveInterval` is configured to a small test value (e.g. 100ms via test override)
**And** no real SSE events are emitted for a window covering ≥ 3 intervals
**When** the heartbeat loop runs
**Then** the response body contains at least 3 occurrences of the byte sequence `: keepalive\n\n` separated by at least the configured interval
**And** each heartbeat write is followed by a `FlushAsync`

### AC2: Real SSE events do NOT interleave with a heartbeat

**Given** a real event (e.g. `text.delta`) and a heartbeat fire concurrently
**When** both writes complete
**Then** the response body is one of:
- Heartbeat then event: `: keepalive\n\nevent: text.delta\ndata: {...}\n\n`
- Event then heartbeat: `event: text.delta\ndata: {...}\n\n: keepalive\n\n`
**And** is NEVER an interleaved fragment like `: keep-event: text.delta\nalive\ndata: ...`
**And** the test demonstrates this with concurrent calls to `EmitTextDeltaAsync` and the keepalive loop racing on the same emitter

### AC3: Heartbeat stops when the linked CTS is cancelled

**Given** a heartbeat task is running with a `CancellationToken`
**When** the token is cancelled
**Then** the heartbeat loop exits within one interval period plus 100ms grace
**And** the returned `Task` completes (does not faulted, does not deadlock)
**And** subsequent `EmitAsync` calls on the same emitter still succeed (the semaphore is not held by the cancelled heartbeat)

### AC4: Heartbeat write failure does not propagate

**Given** a heartbeat is running
**When** the underlying stream throws `IOException` / `ObjectDisposedException` (e.g. client already disconnected)
**Then** the heartbeat catches the exception, logs at Debug, and exits cleanly
**And** does NOT rethrow into the SSE endpoint's `try/catch`
**And** does NOT cause the orchestrator to be cancelled or marked Failed

### AC5: SSE endpoint launches and tears down the heartbeat correctly

**Given** [`ExecuteSseAsync`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L311) is invoked
**When** the orchestrator runs to completion (success, failure, cancellation, or interruption)
**Then** the heartbeat task is started before `ExecuteNextStepAsync` and cancelled in `finally`
**And** the heartbeat does not leak past the request lifetime (verified via test that asserts the linked CTS is disposed on the success path)
**And** the heartbeat is started AFTER `ConfigureSseResponse` so the response headers + body are open

### AC6: `KeepaliveInterval` defaults to 15 seconds

**Given** `appsettings.json` has no `AgentRun:KeepaliveInterval` entry
**When** `AgentRunOptions` binds
**Then** `KeepaliveInterval == TimeSpan.FromSeconds(15)`
**And** the heartbeat loop fires every 15s in production

### AC7: `KeepaliveInterval` clamps out-of-range values

**Given** `KeepaliveInterval` is configured to a value `< TimeSpan.FromSeconds(5)` or `> TimeSpan.FromMinutes(5)`
**When** the SSE endpoint resolves the value at startup
**Then** the value is clamped to `[5s, 300s]`
**And** a warning log fires once at endpoint construction (or at first SSE request if resolved per-request) noting the clamp and the raw configured value
**And** AgentRun does NOT throw or fail to start

### AC8: `IAIChatClientFactory` interface lives in `Engine/` and exposes only primitives + `Microsoft.Extensions.AI` types

**Given** the new file [`AgentRun.Umbraco/Engine/IAIChatClientFactory.cs`](../../AgentRun.Umbraco/Engine/IAIChatClientFactory.cs)
**When** the file is inspected for `using` directives
**Then** there is no `using Umbraco.AI.*` line in the file
**And** the interface's two methods accept and return only: `string`, `CancellationToken`, `Task<IChatClient>`, `Task<IReadOnlyList<string>>`
**And** `IChatClient` resolves to `Microsoft.Extensions.AI.IChatClient` (verified by namespace)

### AC9: `ProfileResolver` no longer imports `Umbraco.AI.*`

**Given** the refactored [`ProfileResolver.cs`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs)
**When** the file is inspected
**Then** there is no `using Umbraco.AI.*` line in the file
**And** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns zero results
**And** the constructor signature is `(IAIChatClientFactory, IOptions<AgentRunOptions>, ILogger<ProfileResolver>)` — no `IAIChatService`, no `IAIProfileService`

### AC10: `UmbracoAIChatClientFactory` lives in `Services/` and is the sole holder of Umbraco.AI deps

**Given** the new file [`AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs`](../../AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs)
**When** the file is inspected
**Then** it contains the four `using Umbraco.AI.Core.*` directives previously in ProfileResolver (Chat, InlineChat, Models, Profiles)
**And** the class implements `IAIChatClientFactory` with constructor `(IAIChatService, IAIProfileService)`
**And** the implementation translates the engine-typed calls to Umbraco.AI calls and back
**And** `AICapability` and `AIProfile` types appear ONLY inside this file in `AgentRun.Umbraco/` (verified by grep across the whole project)

### AC11: New adapter has dedicated unit tests

**Given** the new test file [`AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs`](../../AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs)
**When** the test suite runs
**Then** the file contains at least 4 tests:
- `CreateChatClientAsync_PassesProfileAndRequestAlias_ToUnderlyingService`
- `CreateChatClientAsync_ReturnsTheClientFromUnderlyingService`
- `GetChatProfileAliasesAsync_FiltersToChatCapability_AndProjectsToAliases`
- `GetChatProfileAliasesAsync_EmptyServiceResult_ReturnsEmptyList`
**And** each test uses NSubstitute mocks of `IAIChatService` + `IAIProfileService`

### AC12: `ProfileResolverTests` rewritten to mock `IAIChatClientFactory` only

**Given** [`AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs`](../../AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs) post-refactor
**When** the file is inspected
**Then** it has exactly one mock collaborator for the chat-client surface: `_chatClientFactory = Substitute.For<IAIChatClientFactory>()`
**And** there are no `Substitute.For<IAIChatService>()` or `Substitute.For<IAIProfileService>()` calls remaining in the file
**And** every existing test (count and BDD intent preserved per locked decision 10) passes against the new mock
**And** `using Umbraco.AI.Core.*` imports are removed from the test file

### AC13: DI registration is correct and ordered

**Given** [`AgentRunComposer.Compose`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs)
**When** the compose method runs
**Then** `IAIChatClientFactory → UmbracoAIChatClientFactory` is registered as singleton
**And** the registration appears BEFORE `IProfileResolver → ProfileResolver` (so DI resolution order is intuitive)
**And** integration test fixture `WebApplicationFactory<Program>` (or equivalent test host) resolves `IProfileResolver` without throwing — proving the full DI chain works

### AC14: Test counts and DoD

**Given** the story is complete
**When** `dotnet test AgentRun.Umbraco.slnx` runs
**Then** all backend tests pass (baseline 659/659 from Story 10.6 → expect ~668/668 with +9 from this story: 6 SSE keepalive + 4 adapter tests + 0 net delta on rewritten ProfileResolverTests, minus a few collapsed duplicate setups; final number TBD by Amelia)
**And** `npm test` in `AgentRun.Umbraco/Client/` passes unchanged at 183/183 (frontend untouched)
**And** `dotnet build AgentRun.Umbraco.slnx` is clean (no new warnings)
**And** `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` returns 0 lines

## Tasks / Subtasks

### Task 1: Add `KeepaliveInterval` to `AgentRunOptions` (AC6, AC7)

- [x] 1.1 Open [`AgentRunOptions.cs`](../../AgentRun.Umbraco/Configuration/AgentRunOptions.cs).
  1. Add a new property:
     ```csharp
     /// <summary>
     /// Interval between SSE keepalive comment lines. Default: 15s. Clamped to [5s, 300s].
     /// Set higher if you have spare proxy budget, lower if running behind aggressive
     /// proxies (e.g. Cloudflare orange-cloud at 30s).
     /// </summary>
     public TimeSpan KeepaliveInterval { get; set; } = TimeSpan.FromSeconds(15);
     ```
  2. Add the clamp logic. Two options:
     - **Option A (preferred):** Clamp at the consumption site (in `ExecuteSseAsync` when reading the value), so the bound options object stays a faithful representation of `appsettings.json`. Cleaner separation.
     - **Option B:** Clamp in the property setter. Simpler but conflates "what the user configured" with "what the system will use."
     Pick Option A. Add a private static helper `private static TimeSpan ClampInterval(TimeSpan raw) => raw < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : raw > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : raw;` in `ExecutionEndpoints` and use it at the consumption site.
- [x] 1.2 Update any existing `AgentRunOptionsTests` for the new property.
  1. Added `Defaults_KeepaliveInterval_Is15Seconds`.
  2. Added `KeepaliveInterval_CanBeSet` (covers round-trip behaviour; JSON-binding round-trip is covered by the existing ASP.NET options framework).
- [x] 1.3 Added 7 clamp tests on the consumption-site helper in `ExecutionEndpointsTests`:
  `ClampInterval_BelowMin_ClampsTo5Seconds`, `ClampInterval_Zero_ClampsTo5Seconds`,
  `ClampInterval_AboveMax_ClampsTo5Minutes`, `ClampInterval_MaxValue_ClampsTo5Minutes`,
  `ClampInterval_InRange_ReturnsVerbatim`, `ClampInterval_AtMinBoundary_ReturnsMin`,
  `ClampInterval_AtMaxBoundary_ReturnsMax`.

### Task 2: Extend `ISseEventEmitter` and `SseEventEmitter` with keepalive (AC1, AC2, AC3, AC4)

- [x] 2.1 Add to [`ISseEventEmitter.cs`](../../AgentRun.Umbraco/Engine/Events/ISseEventEmitter.cs):
  ```csharp
  /// <summary>
  /// Emits a `: keepalive\n\n` SSE comment line every <paramref name="interval"/>
  /// until the cancellation token fires. Serialises against concurrent EmitXxxAsync
  /// calls via an internal SemaphoreSlim. Heartbeat write failures
  /// (IOException, ObjectDisposedException, OCE) are caught + logged at Debug; the
  /// loop exits cleanly. Intended to be fire-and-forget at the call site —
  /// use a linked CTS for deterministic teardown.
  /// </summary>
  Task StartKeepaliveAsync(TimeSpan interval, CancellationToken cancellationToken);
  ```
- [x] 2.2 Implement in [`SseEventEmitter.cs`](../../AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs):
  1. Add private field `private readonly SemaphoreSlim _writeLock = new(1, 1);`. Per-emitter, per-stream.
  2. Wrap the existing `EmitAsync` body in `try/finally` with `await _writeLock.WaitAsync(cancellationToken)` / `_writeLock.Release()`. The semaphore guarantees no interleaved writes. Keep the existing `LogDebug` and bytes-write pattern unchanged inside the lock.
  3. Add `StartKeepaliveAsync`:
     ```csharp
     public async Task StartKeepaliveAsync(TimeSpan interval, CancellationToken cancellationToken)
     {
         var keepaliveBytes = "Encoding.UTF8.GetBytes(\": keepalive\\n\\n\")"; // pre-compute once outside the loop
         try
         {
             while (!cancellationToken.IsCancellationRequested)
             {
                 try { await Task.Delay(interval, cancellationToken); }
                 catch (OperationCanceledException) { return; }

                 try
                 {
                     await _writeLock.WaitAsync(cancellationToken);
                     try
                     {
                         await _stream.WriteAsync(keepaliveBytes, cancellationToken);
                         await _stream.FlushAsync(cancellationToken);
                         _logger.LogTrace("SSE keepalive emitted");
                     }
                     finally { _writeLock.Release(); }
                 }
                 catch (OperationCanceledException) { return; }
                 catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                 {
                     _logger.LogDebug(ex, "SSE keepalive write failed; exiting heartbeat loop");
                     return;
                 }
             }
         }
         catch (OperationCanceledException) { /* expected on shutdown */ }
     }
     ```
  4. Pre-compute `keepaliveBytes` ONCE outside the loop. Pre-compute it as `private static readonly byte[] _keepaliveBytes = Encoding.UTF8.GetBytes(": keepalive\n\n");` at the class level — same allocation lifetime as the type, fine for a singleton emitter type (note: emitter is per-request, not singleton; but the `byte[]` is `static readonly` so still shared safely).
  5. **Do NOT** add a generic `catch (Exception)` outside the IOException/ObjectDisposedException filter — let unexpected exceptions surface so genuine bugs aren't masked.
- [x] 2.3 Add unit tests in a new file [`AgentRun.Umbraco.Tests/Engine/Events/SseEventEmitterTests.cs`](../../AgentRun.Umbraco.Tests/Engine/Events/SseEventEmitterTests.cs).
  1. Test fixture: `MemoryStream` as the backing stream, `NullLogger<SseEventEmitter>.Instance`.
  2. **`StartKeepaliveAsync_AfterMultipleIntervals_WritesKeepaliveCommentEachTime`** (AC1) — start heartbeat with `TimeSpan.FromMilliseconds(50)`, wait 200ms, cancel, read MemoryStream content, assert `Encoding.UTF8.GetString(stream.ToArray()).Split(": keepalive\n\n").Length - 1 >= 3` (at least 3 keepalives in 200ms / 50ms intervals, minus jitter tolerance).
  3. **`StartKeepaliveAsync_ConcurrentWithEmitTextDelta_DoesNotInterleaveWrites`** (AC2) — start a 10ms-interval heartbeat AND fire 50 `EmitTextDeltaAsync("hello")` calls concurrently for 200ms. Cancel, read stream, parse line by line. Assert every comment line is exactly `: keepalive` and every event has the full `event: text.delta\ndata: {"content":"hello"}\n` shape (no truncation / no merge).
  4. **`StartKeepaliveAsync_TokenCancelled_ExitsWithinOneInterval`** (AC3) — start heartbeat with `TimeSpan.FromSeconds(1)`, cancel after 100ms, await the returned `Task` with `CancellationToken` of 1.5s, assert no timeout / clean completion.
  5. **`StartKeepaliveAsync_StreamThrowsIOException_ExitsCleanly`** (AC4) — replace MemoryStream with a `Substitute.For<Stream>()` that throws `IOException("disconnected")` on `WriteAsync`. Start heartbeat, await briefly, assert: Task completes successfully, no exception escapes, debug log emitted (verify via test logger).
  6. **`StartKeepaliveAsync_StreamThrowsObjectDisposedException_ExitsCleanly`** (AC4) — same as above but with `ObjectDisposedException`.
  7. **`EmitAsync_AfterCancelledKeepalive_StillWritesEvent`** (AC3) — start heartbeat, cancel it, then call `EmitTextDeltaAsync("hello")` and assert the stream contains the event (proves the semaphore was released by the cancelled loop).

### Task 3: Wire keepalive into `ExecuteSseAsync` (AC1, AC5, AC6, AC7)

- [x] 3.1 Modify [`ExecutionEndpoints.cs:311-321`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L311) to inject `IOptions<AgentRunOptions>`.
  1. Constructor parameter: add `IOptions<AgentRunOptions> options`. Store as `_options.Value` (the resolved options object).
  2. Update DI registration test fixture (or `WebApplicationFactory` setup if used) to provide a default `AgentRunOptions`.
- [x] 3.2 Modify `ExecuteSseAsync` to start the heartbeat:
  1. After `var emitter = new SseEventEmitter(...)`, before the orchestrator call:
     ```csharp
     // Story 10.11: SSE keepalive — start a fire-and-forget heartbeat so reverse
     // proxies with idle-read timeouts (nginx/AWS ALB default 60s) don't kill
     // long LLM calls. Linked CTS guarantees teardown on every exit path.
     var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
     var clampedInterval = ClampInterval(_options.KeepaliveInterval);
     _ = emitter.StartKeepaliveAsync(clampedInterval, keepaliveCts.Token);
     ```
  2. Wrap the orchestrator call in an outer `try/finally` that **explicitly** calls `keepaliveCts.Cancel()` *and then* `keepaliveCts.Dispose()`. `CancellationTokenSource.Dispose()` does NOT cancel the token (MS docs: *"Dispose... releases all resources"*); a plain `using var` would leave the heartbeat running past orchestrator return, relying on the next stream write to fail as the teardown signal. Explicit Cancel-then-Dispose gives deterministic teardown on every exit path (happy, OCE, exception) — locked decision 5.
  3. Add `ClampInterval` as a `private static TimeSpan ClampInterval(TimeSpan raw)` helper on the class.
  4. Gate the "clamp applied" warn-log on a process-wide `Interlocked.CompareExchange` flag so the warning fires at most once per process (locked decision 12: *"log warning once at startup"*) — without the gate the controller is per-request-instance and the warning would fire on every SSE start for a misconfigured interval.
- [x] 3.3 Manual smoke verification (no E2E test file — covered by Task 7 manual E2E).
  1. Set `KeepaliveInterval` to `00:00:02` (2s) in TestSite `appsettings.Development.json` for testing only.
  2. Start a CQA scanner step.
  3. Tail the response stream via `curl -N` against the SSE endpoint.
  4. Verify `: keepalive` lines arrive every 2s alongside event data.
  5. Restore default value before commit.

### Task 4: Define `IAIChatClientFactory` interface in `Engine/` (AC8)

- [x] 4.1 Create [`AgentRun.Umbraco/Engine/IAIChatClientFactory.cs`](../../AgentRun.Umbraco/Engine/IAIChatClientFactory.cs):
  ```csharp
  using Microsoft.Extensions.AI;

  namespace AgentRun.Umbraco.Engine;

  /// <summary>
  /// Engine-side abstraction over the chat-client provider. Implementations live
  /// outside Engine/ so the engine namespace can stay free of Umbraco.AI types.
  /// Returns Microsoft.Extensions.AI.IChatClient and primitive types only.
  /// </summary>
  public interface IAIChatClientFactory
  {
      Task<IChatClient> CreateChatClientAsync(
          string profileAlias,
          string requestAlias,
          CancellationToken cancellationToken);

      /// <summary>
      /// Returns chat-capable profile aliases discovered from the underlying provider.
      /// Order is undefined — callers that need deterministic selection must sort.
      /// </summary>
      Task<IReadOnlyList<string>> GetChatProfileAliasesAsync(CancellationToken cancellationToken);
  }
  ```
  - **Critical:** verify zero `using Umbraco.*` lines in this file. Only `using Microsoft.Extensions.AI;` for `IChatClient` is permitted.

### Task 5: Implement `UmbracoAIChatClientFactory` in `Services/` (AC10)

- [x] 5.1 Create [`AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs`](../../AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs):
  ```csharp
  using Microsoft.Extensions.AI;
  using AgentRun.Umbraco.Engine;
  using Umbraco.AI.Core.Chat;
  using Umbraco.AI.Core.InlineChat;
  using Umbraco.AI.Core.Models;
  using Umbraco.AI.Core.Profiles;

  namespace AgentRun.Umbraco.Services;

  /// <summary>
  /// Adapter between Engine/'s IAIChatClientFactory and Umbraco.AI's chat services.
  /// Holds the only Umbraco.AI.* dependencies in the Engine-adjacent code path —
  /// Engine/ProfileResolver depends on this through IAIChatClientFactory only.
  /// </summary>
  public sealed class UmbracoAIChatClientFactory : IAIChatClientFactory
  {
      private readonly IAIChatService _chatService;
      private readonly IAIProfileService _profileService;

      public UmbracoAIChatClientFactory(IAIChatService chatService, IAIProfileService profileService)
      {
          _chatService = chatService;
          _profileService = profileService;
      }

      public Task<IChatClient> CreateChatClientAsync(
          string profileAlias,
          string requestAlias,
          CancellationToken cancellationToken)
      {
          return _chatService.CreateChatClientAsync(
              chat => chat.WithAlias(requestAlias).WithProfile(profileAlias),
              cancellationToken);
      }

      public async Task<IReadOnlyList<string>> GetChatProfileAliasesAsync(
          CancellationToken cancellationToken)
      {
          var profiles = await _profileService.GetProfilesAsync(AICapability.Chat, cancellationToken);
          return profiles?.Select(p => p.Alias).ToArray() ?? Array.Empty<string>();
      }
  }
  ```
  - **Critical:** the `AICapability.Chat` argument and `AIProfile.Alias` property accesses are confined to this method body. They never appear in `Engine/`.

### Task 6: Refactor `ProfileResolver` (AC9)

- [x] 6.1 Open [`ProfileResolver.cs`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs).
  1. Remove the four `using Umbraco.AI.Core.*` lines (lines 5–8).
  2. Change constructor signature from `(IAIChatService chatService, IAIProfileService profileService, IOptions<AgentRunOptions> options, ILogger<ProfileResolver> logger)` to `(IAIChatClientFactory chatClientFactory, IOptions<AgentRunOptions> options, ILogger<ProfileResolver> logger)`. Drop the two old fields, add `private readonly IAIChatClientFactory _chatClientFactory;`.
  3. In `ResolveAndGetClientAsync`, replace:
     ```csharp
     return await _chatService.CreateChatClientAsync(
         chat => chat.WithAlias($"step-{step.Id}").WithProfile(alias),
         cancellationToken);
     ```
     with:
     ```csharp
     return await _chatClientFactory.CreateChatClientAsync(
         alias, $"step-{step.Id}", cancellationToken);
     ```
     The `try/catch (Exception ex) when (ex is not ProfileNotFoundException)` wrapper stays unchanged — it still translates underlying exceptions to `ProfileNotFoundException`.
  4. In `HasConfiguredProviderAsync`, replace the inline `chat => chat.WithAlias("provider-check"); chat.WithProfile(profile)` builder lambda with `_chatClientFactory.CreateChatClientAsync(profile ?? "default", "provider-check", cancellationToken)`. **Edge:** if `profile` is null (no profile configured anywhere), the factory call needs SOME alias — pass `"default"` or empty string; the factory's underlying `chat.WithProfile(null)` behaviour was previously a "no profile" call. **Handling:** add an overload to `IAIChatClientFactory` that accepts `string? profileAlias`, OR (preferred) skip the call entirely when `profile is null` and rely on `GetChatProfileAliasesAsync` returning empty as the "no provider" signal. Pick the second — it's simpler and avoids null-string-as-sentinel weirdness in the interface.
  5. Concrete shape of the rewritten `HasConfiguredProviderAsync`:
     ```csharp
     public async Task<bool> HasConfiguredProviderAsync(
         WorkflowDefinition? workflow, CancellationToken cancellationToken)
     {
         try
         {
             string? profile = null;
             if (!string.IsNullOrWhiteSpace(workflow?.DefaultProfile))
                 profile = workflow.DefaultProfile;
             else if (!string.IsNullOrWhiteSpace(_options.DefaultProfile))
                 profile = _options.DefaultProfile;

             if (profile is null)
                 profile = await AutoDetectProfileAliasAsync(cancellationToken);

             if (profile is null)
             {
                 _logger.LogWarning("Provider prerequisite check failed: no Umbraco.AI profile configured");
                 return false;
             }

             var client = await _chatClientFactory.CreateChatClientAsync(
                 profile, "provider-check", cancellationToken);
             (client as IDisposable)?.Dispose();
             _logger.LogDebug(
                 "Provider prerequisite check passed: profile={Profile}", profile);
             return true;
         }
         catch (OperationCanceledException) { throw; }
         catch (Exception ex)
         {
             _logger.LogWarning(ex, "Provider prerequisite check failed: no Umbraco.AI provider is configured");
             return false;
         }
     }
     ```
  6. In `ResolveProfileAliasAsync`, replace the auto-detect block (currently using `_profileService.GetProfilesAsync(AICapability.Chat, ...)` and `OrderBy(p => p.Alias, ...)`) with:
     ```csharp
     var aliases = await _chatClientFactory.GetChatProfileAliasesAsync(cancellationToken);
     var sorted = aliases.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
     if (sorted.Count == 0)
     {
         throw new ProfileNotFoundException(
             "No AI provider configured. Go to Settings > AI to set up a provider.");
     }
     var selected = sorted[0];

     _logger.LogInformation(
         "Auto-detected Umbraco.AI profile '{ProfileAlias}' for workflow execution",
         selected);

     if (sorted.Count > 1)
     {
         _logger.LogInformation(
             "Multiple Umbraco.AI profiles found — using '{ProfileAlias}'. Set default_profile in your workflow YAML for deterministic selection.",
             selected);
     }
     return (selected, "auto-detected");
     ```
     Sort-and-pick logic stays in ProfileResolver (locked decision 7).
  7. In `AutoDetectProfileAliasAsync`, replace the `_profileService.GetProfilesAsync(AICapability.Chat, ct)` + `OrderBy(p => p.Alias).FirstOrDefault()?.Alias` with `_chatClientFactory.GetChatProfileAliasesAsync(ct)` + `OrderBy(StringComparer.OrdinalIgnoreCase).FirstOrDefault()`.
  8. Verify zero `Umbraco.AI` references remain. Run `grep -n "Umbraco\." AgentRun.Umbraco/Engine/ProfileResolver.cs` — expect zero matches.

### Task 7: Update DI registration (AC13)

- [x] 7.1 [`AgentRunComposer.cs:36`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs#L36) — add the new line BEFORE `IProfileResolver`:
  ```csharp
  // Engine services
  builder.Services.AddSingleton<IPromptAssembler, PromptAssembler>();
  builder.Services.AddSingleton<IAIChatClientFactory, UmbracoAIChatClientFactory>(); // ← Story 10.11
  builder.Services.AddSingleton<IProfileResolver, ProfileResolver>();
  builder.Services.AddSingleton<ICompletionChecker, CompletionChecker>();
  builder.Services.AddSingleton<IArtifactValidator, ArtifactValidator>();
  ```
  Do NOT add a `using AgentRun.Umbraco.Services;` line if `Services` is already in scope; if not, add it next to the existing using directives.

### Task 8: Rewrite `ProfileResolverTests` to mock the new factory (AC12)

- [x] 8.1 Open [`ProfileResolverTests.cs`](../../AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs).
  1. Replace `_chatService` and `_profileService` field declarations with `private IAIChatClientFactory _chatClientFactory = null!;`.
  2. In `[SetUp]`, replace the two `Substitute.For<...>()` calls with `_chatClientFactory = Substitute.For<IAIChatClientFactory>();`.
  3. Update `CreateResolver` to construct `new ProfileResolver(_chatClientFactory, options, _logger)`.
  4. Sweep every test method — replace `_chatService.CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>()).Returns(_chatClient)` with `_chatClientFactory.CreateChatClientAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_chatClient)`.
  5. Replace `_profileService.GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>()).Returns(profiles)` (where `profiles` is `IEnumerable<AIProfile>`) with `_chatClientFactory.GetChatProfileAliasesAsync(Arg.Any<CancellationToken>()).Returns(aliases)` where `aliases` is `string[]`.
  6. Drop the `Umbraco.AI.Core.*` `using` directives at the top of the test file. Run the test file's own `dotnet build` — should be clean.
  7. **Preserve every existing test by name and intent.** If a test was specifically about "AIProfile.Alias selection from a multi-profile list," the new equivalent uses `string[]` aliases — same selection behaviour. If a test was about "AICapability.Chat filter is passed through," delete it (the filter is now an adapter detail, not a ProfileResolver concern; covered by Task 9 adapter test instead).
  8. Verify `dotnet test AgentRun.Umbraco.slnx --filter ProfileResolverTests` passes.

### Task 9: New tests for `UmbracoAIChatClientFactory` (AC11)

- [x] 9.1 Create [`AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs`](../../AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs):
  ```csharp
  [TestFixture]
  public class UmbracoAIChatClientFactoryTests
  {
      private IAIChatService _chatService = null!;
      private IAIProfileService _profileService = null!;
      private IChatClient _chatClient = null!;
      private UmbracoAIChatClientFactory _factory = null!;

      [SetUp]
      public void SetUp()
      {
          _chatService = Substitute.For<IAIChatService>();
          _profileService = Substitute.For<IAIProfileService>();
          _chatClient = Substitute.For<IChatClient>();
          _factory = new UmbracoAIChatClientFactory(_chatService, _profileService);
      }

      [TearDown] public void TearDown() => _chatClient?.Dispose();

      [Test]
      public async Task CreateChatClientAsync_PassesProfileAndRequestAlias_ToUnderlyingService()
      {
          AIChatBuilder? capturedBuilder = null;
          _chatService
              .CreateChatClientAsync(Arg.Do<Action<AIChatBuilder>>(action =>
              {
                  capturedBuilder = new AIChatBuilder(/* fake init — see TODO */);
                  action(capturedBuilder);
              }), Arg.Any<CancellationToken>())
              .Returns(_chatClient);

          await _factory.CreateChatClientAsync("anthropic-claude", "step-scanner", CancellationToken.None);

          // TODO: AIChatBuilder may be sealed / non-mockable. If so, capture via
          // Arg.Do<Action<AIChatBuilder>> and assert the call landed by checking
          // _chatService.Received() with Arg.Any matchers — the precise builder
          // shape is implementation detail.
          await _chatService.Received(1).CreateChatClientAsync(
              Arg.Any<Action<AIChatBuilder>>(),
              Arg.Any<CancellationToken>());
      }

      [Test]
      public async Task CreateChatClientAsync_ReturnsTheClientFromUnderlyingService()
      {
          _chatService.CreateChatClientAsync(
                  Arg.Any<Action<AIChatBuilder>>(), Arg.Any<CancellationToken>())
              .Returns(_chatClient);

          var result = await _factory.CreateChatClientAsync("p", "r", CancellationToken.None);

          Assert.That(result, Is.SameAs(_chatClient));
      }

      [Test]
      public async Task GetChatProfileAliasesAsync_FiltersToChatCapability_AndProjectsToAliases()
      {
          var profiles = new[]
          {
              new AIProfile { Alias = "openai-gpt", Name = "OpenAI", ConnectionId = Guid.NewGuid() },
              new AIProfile { Alias = "anthropic-claude", Name = "Anthropic", ConnectionId = Guid.NewGuid() }
          };
          _profileService.GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>())
              .Returns(profiles);

          var result = await _factory.GetChatProfileAliasesAsync(CancellationToken.None);

          // Order-preserving projection — sort policy lives in ProfileResolver, not here.
          Assert.That(result, Is.EqualTo(new[] { "openai-gpt", "anthropic-claude" }));
          await _profileService.Received(1).GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>());
      }

      [Test]
      public async Task GetChatProfileAliasesAsync_EmptyServiceResult_ReturnsEmptyList()
      {
          _profileService.GetProfilesAsync(AICapability.Chat, Arg.Any<CancellationToken>())
              .Returns((IEnumerable<AIProfile>?)null);

          var result = await _factory.GetChatProfileAliasesAsync(CancellationToken.None);

          Assert.That(result, Is.Empty);
      }
  }
  ```
  - **Note on AIChatBuilder mockability:** if `AIChatBuilder` cannot be constructed in tests (sealed type with internal constructor), drop the precise builder-shape assertions and rely on `Received(1).CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), ...)`. The behaviour-vs-shape boundary is "did we call the underlying service exactly once with the right capability/CT" — that's enough to validate the adapter.

### Task 10: Manual E2E + smoke verification

- [x] 10.1 **SSE keepalive smoke test (CLI).** Set `AgentRun:KeepaliveInterval` to `"00:00:02"` in TestSite `appsettings.Development.json`. Restart TestSite. Open a new shell:
  ```bash
  curl -N -H "Cookie: <auth>" http://localhost:5001/umbraco/api/agentrun/instances/<some-instance-id>/start
  ```
  Observe `: keepalive` lines arriving every ~2s alongside event data. Restore `KeepaliveInterval` to default (or remove the override entirely) before commit.
- [x] 10.2 **SSE keepalive happy path (browser).** With `KeepaliveInterval` at default (15s), start a CQA scanner step. Open browser DevTools → Network → click the `/start` request → Response tab. Watch for keepalive comment lines arriving in the raw response body. The browser's `EventSource` consumer ignores them (correct per W3C spec) — verify the chat panel renders normally without any "unknown event type" console warnings.
- [x] 10.3 **Long-LLM-call simulation.** If a slow Sonnet 4.6 step is available (CQA analyser on a complex prompt), run it end-to-end behind the local TestSite directly (no proxy) and confirm: (a) keepalive fires throughout, (b) no Story 10.9 Interrupted state surfaces from the timeout that would have killed the connection pre-10.11. If you can run the TestSite behind a local nginx with `proxy_read_timeout 30s`, that's the gold-standard test — but is not blocking for this story.
- [x] 10.4 **ProfileResolver refactor regression.** Run a full CQA workflow with auto-detect (Adam's typical local config: one `anthropic` profile only, no `default_profile` in workflow YAML, no `AgentRun:DefaultProfile` in appsettings — exercises the auto-detect path through the new factory). Confirm: scanner runs, analyser runs, reporter runs, all three steps complete, no "ProfileNotFoundException" or DI-resolution errors in the log.
- [x] 10.5 **DI smoke at startup.** Confirm `[INF] Now listening on http://localhost:5001` appears at TestSite startup with no DI exceptions about `IAIChatClientFactory` or `IProfileResolver`. If a missing-registration error appears, the most likely cause is `AgentRunComposer.cs` ordering (Task 7).
- [x] 10.6 Run `dotnet test AgentRun.Umbraco.slnx` — all green (target ~668/668 — Amelia confirms exact number after rewriting ProfileResolverTests).
- [x] 10.7 Run `npm test` in `AgentRun.Umbraco/Client/` — expect 183/183 unchanged (frontend not touched).
- [x] 10.8 Run `npm run build` in `AgentRun.Umbraco/Client/` — expect clean (frontend not touched).
- [x] 10.9 Run `dotnet build AgentRun.Umbraco.slnx` — expect clean, no new warnings.
- [x] 10.10 Verify the boundary rule with a single grep:
  ```bash
  grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"
  ```
  Expected: **zero matches**. If anything else appears (e.g. a future `using Umbraco.Cms.*`), surface it before commit — this story's goal is the clean grep.

## Failure & Edge Cases

### F1: Heartbeat fires after the orchestrator returns but before the SSE endpoint enters `finally`

**When** the orchestrator completes (success/failure/cancel/interrupt) and `ExecuteSseAsync` is in the gap between the orchestrator's last write and the `finally` block that explicitly `Cancel()`s `keepaliveCts`
**Then** a heartbeat write may land on the response stream after the final event
**And** the client receives an extra `: keepalive\n\n` after `event: run.finished\n\n`
**Net effect:** harmless. EventSource ignores comment lines; the run is already marked terminal at the instance level. No client confusion.
**Cleanup contract:** `keepaliveCts.Cancel()` precedes `keepaliveCts.Dispose()` in the outer `finally` block. `Dispose()` alone does not cancel the token (MS-documented behaviour) — omitting the explicit `Cancel()` would leave the heartbeat running past orchestrator return and rely on the next stream write to fail as the teardown signal, which is undefined behaviour when the linked CTS is already disposed.

### F2: Two SSE requests for the same instance race on the heartbeat (10.1 protection failure mode)

**When** Story 10.1's `ActiveInstanceRegistry.TryClaim` somehow allows two orchestrators on the same instance (would be a 10.1 regression)
**Then** each request gets its own `SseEventEmitter`, each with its own `_writeLock` and heartbeat. No cross-emitter contention.
**Mitigation:** none required — per-emitter semaphore is local, not shared. Story 10.1's claim guard is the only race-prevention layer; this story doesn't weaken it.

### F3: `KeepaliveInterval` configured to `TimeSpan.Zero` or negative

**When** appsettings binds `"KeepaliveInterval": "00:00:00"` or `"-00:00:01"` (negative not parseable as TimeSpan but pathologically possible via JSON binding edge cases)
**Then** `ClampInterval` returns the floor (5s)
**And** a single warn-log fires noting the clamp
**And** the heartbeat runs at 5s intervals — slightly aggressive but functional

### F4: `KeepaliveInterval` configured to `TimeSpan.MaxValue`

**When** appsettings binds an absurdly large interval
**Then** `ClampInterval` returns the ceiling (5 min)
**And** the heartbeat fires every 5 min — barely useful for proxies with 60s timeouts, but the customer's explicit choice (clamped to a sane upper bound)
**And** a warn-log fires noting the clamp

### F5: Heartbeat throws an unexpected exception (not `IOException` / `ObjectDisposedException` / `OCE`)

**When** `_stream.WriteAsync` throws `NotSupportedException` (e.g. stream cannot be written to — pathological)
**Then** the heartbeat loop does NOT catch it (locked decision: don't mask unexpected exceptions)
**And** the exception bubbles out of the heartbeat task
**And** the SSE endpoint does NOT observe the exception (heartbeat is fire-and-forget; the awaiter is none)
**And** the .NET task scheduler logs an `UnobservedTaskException` — visible in dev logs, alerts to a real bug
**Mitigation:** intentional — surfacing genuine bugs is more valuable than silent recovery. If a future audit shows this fires in production, add a logged catch in the heartbeat loop with a metric increment.

### F6: `IAIChatClientFactory.GetChatProfileAliasesAsync` returns null (adapter contract breach)

**When** a custom future implementation of `IAIChatClientFactory` returns `null` instead of `Array.Empty<string>()`
**Then** `ProfileResolver`'s `aliases.OrderBy(...)` throws `NullReferenceException`
**Mitigation:** the [`UmbracoAIChatClientFactory`](../../AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs) implementation guards with `?? Array.Empty<string>()` (Task 5 step 1). The interface XML doc says "returns chat-capable profile aliases" — implicitly non-null. Defensive null-coalesce in ProfileResolver is reasonable belt-and-braces, but per locked decision #4 from Story 10.10's class ("Trust internal code and framework guarantees") the adapter's null-coalesce is sufficient.

### F7: Two profiles with case-different aliases (`"Anthropic"` and `"anthropic"`)

**When** two profiles have aliases that differ only in case
**Then** `OrderBy(StringComparer.OrdinalIgnoreCase)` puts them in arbitrary order (the comparer treats them as equal)
**And** `First()` picks one of the two — non-deterministic across runs in the worst case
**Net effect:** edge case in the auto-detect path; if it ever happens, the warn-log ("Multiple Umbraco.AI profiles found") fires and the user is told to set `default_profile` explicitly. The non-determinism between two case-equivalent aliases is a Umbraco.AI configuration smell, not an AgentRun bug. Accepted.

### F8: Heartbeat semaphore acquisition takes longer than the interval

**When** an `EmitTextDeltaAsync` holds the semaphore for > `KeepaliveInterval` (e.g. a 100KB markdown chunk on a slow disk-backed `Response.Body`)
**Then** the heartbeat's `await _writeLock.WaitAsync(ct)` blocks until the long write finishes
**And** by the time the heartbeat writes, the next interval is overdue
**Net effect:** missed heartbeat is functionally identical to "no idle period needed a heartbeat" — the long event write ITSELF reset the proxy's idle counter. The heartbeat catches up on the next iteration. No special-casing required.

### F9: `Response.Body.Stream` is wrapped in compression middleware that doesn't flush on `: keepalive\n\n`

**When** ASP.NET response compression is enabled and the SSE response is wrapped in a `GZipStream`
**Then** the heartbeat bytes are buffered by the compressor and may not reach the wire until the compression block fills
**Mitigation:** AgentRun's [`SseHelper.ConfigureSseResponse`](../../AgentRun.Umbraco/Endpoints/SseHelper.cs) calls `IHttpResponseBodyFeature.DisableBuffering()` which suppresses ASP.NET's response buffering layer. Compression middleware (if added by the host) is a separate concern — if a customer explicitly opts in to compression on their Umbraco host, they need to exclude `text/event-stream` from the compression mime list. **Documentation note:** mention this in the README/docs as a known integration concern. NOT a code change for this story.

### F10: `ProfileResolver` consumer asks for `HasConfiguredProviderAsync` before any profile is configured at all

**When** a fresh install has no `default_profile`, no `AgentRun:DefaultProfile`, and no Umbraco.AI profiles configured
**Then** the rewrite at Task 6 step 5 returns `false` BEFORE making any factory call (the new `if (profile is null) return false;` short-circuit)
**And** zero exceptions are thrown
**And** the dashboard displays the existing "no provider configured" guidance (Story 4.2 / 10.12)
**Net effect:** improvement — the previous code attempted a `CreateChatClientAsync(profile=null)` call which sometimes threw a misleading exception. The new short-circuit is cleaner.

### F11: A new Engine/ file in a future story imports `Umbraco.AI.*` again

**When** a future story (e.g. a new tool or new engine collaborator) re-introduces a `using Umbraco.AI.*` line in `Engine/`
**Then** the boundary regression is silent at compile time — no compiler enforcement
**Mitigation suggestion (deferred, NOT in this story's scope):** add a CI lint step that runs `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` and fails the build if the count is > 0. File this as a deferred-work item, not part of 10.11.

## Dev Notes

### Why two tracks in one story

Both tracks came out of the [pre-launch code review (2026-04-10)](../planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md):

| Track | Code review section | Severity per review |
|---|---|---|
| A: SSE keepalive | "SSE streaming has no heartbeat/keepalive events" | Production-readiness concern |
| B: ProfileResolver boundary | Compliance checklist: "Engine has no Umbraco dependencies → Deviates" | Architectural deviation |

Each is too small for a dedicated story — A is ~50 lines + tests, B is a refactor that touches 3 files. Bundling them shares one round of code review and one E2E session. Locked decision 1 codifies this. Splitting them was considered and rejected: the ceremony cost (two PR cycles, two test sweeps, two manual E2E runs) is not justified by the negligible code overlap.

### Why `: keepalive\n\n` and not a real event

Per the [W3C EventSource spec](https://www.w3.org/TR/eventsource/#parsing-an-event-stream): *"If the line starts with a U+003A COLON character (:) — Ignore the line."* This is the canonical SSE keepalive pattern (also documented in the [HTML Living Standard](https://html.spec.whatwg.org/multipage/server-sent-events.html#authoring-notes)). It satisfies proxy idle-read counters without producing a frontend-visible event. Zero frontend code change required.

A real event (e.g. `event: heartbeat\n\n`) would force the frontend to handle a noop event type and would show up in any SSE recording / replay tool as legitimate event noise. Comment lines are wire-only.

### Why 15s default

| Common proxy floor | Required margin | Headroom at 15s |
|---|---|---|
| Cloudflare orange-cloud (30s) | 2× | 2× ✓ |
| nginx default (60s) | 4× | 4× ✓ |
| AWS ALB default (60s) | 4× | 4× ✓ |
| Cloudflare free (100s) | 6× | 6× ✓ |
| IIS ARR default (120s) | 8× | 8× ✓ |
| Azure App Service (240s) | 16× | 16× ✓ |

15s gives ≥ 2× headroom for every common proxy without burning bandwidth on too-aggressive heartbeats. Customers behind aggressive proxies (Cloudflare orange-cloud at 30s in pathological configs) can drop it to 10s via config. Customers willing to risk shorter idle windows for less network noise can raise it to 60s. The clamp range `[5s, 300s]` covers both extremes.

### Why the semaphore (and not a `lock`)

`SseEventEmitter`'s `EmitAsync` is async — it `await`s `WriteAsync` and `FlushAsync`. C# `lock` cannot span `await` (CS1996). Options:
1. **`SemaphoreSlim(1, 1)`:** async-friendly, lightweight, FIFO fairness — preferred.
2. **`Channel<SseFrame>`:** writers enqueue, single dequeue task drains. Heavier abstraction; only justified if multi-emitter coalescing was needed.
3. **No serialisation, accept interleave risk:** unacceptable — SSE clients break on malformed frames.

Pick option 1. Semaphore allocation is per-emitter (per-request), and emitters are short-lived (one per SSE stream). Allocation cost is dwarfed by the request lifetime.

### Why `_keepaliveBytes` is `static readonly`

The byte sequence `: keepalive\n\n` never changes. Allocating it per-instance (or worse, per-loop-iteration) wastes 12 bytes per heartbeat. `static readonly byte[]` is allocated once per type load and shared across all emitter instances. Safe because byte arrays are mutable but we never write to it.

### Why move the adapter to `Services/` and not `Adapters/`

[`Services/`](../../AgentRun.Umbraco/Services) already contains [`ConversationRecorder`](../../AgentRun.Umbraco/Services/ConversationRecorder.cs) — the established precedent for "Engine-adjacent collaborator with framework-specific dependencies." From [Story 6.2's deferred-work entry](../implementation-artifacts/deferred-work.md):

> "Engine→Services architecture boundary — `WorkflowOrchestrator` (Engine/) directly instantiates `ConversationRecorder` (Services/) via `new ConversationRecorder(...)` with `using AgentRun.Umbraco.Services`. Should use a factory or inject `IConversationRecorder` to keep Engine dependency-free. Designed into story spec Task 4; refactor in a future story."

That deferred item (refactor `ConversationRecorder` to live behind `IConversationRecorder`) is symmetric with this story's `IAIChatClientFactory` extraction. Both follow the same pattern. Adding a separate `Adapters/` folder for one file would split convention; reusing `Services/` keeps the existing pattern intact.

### What NOT to do

- Do NOT make the SSE keepalive a frontend-visible event. It's a wire-level liveness signal; frontends should never see it.
- Do NOT change `SseHelper.ConfigureSseResponse` — the HTTP-level keep-alive header is correct already; this story adds an application-level keepalive on top.
- Do NOT use `Task.Delay` synchronously (`Wait()`) inside the heartbeat loop. It's an async loop; everything must be awaited.
- Do NOT preserve a backward-compatible `ProfileResolver` constructor. The class is internal-DI-only; the rewrite is a hard cutover.
- Do NOT expose `AICapability` or `AIProfile` through the new `IAIChatClientFactory` interface. Those types stay confined to `UmbracoAIChatClientFactory`. The whole point of the extraction is hiding them from `Engine/`.
- Do NOT add a `default_profile_alias` parameter to `IAIChatClientFactory.CreateChatClientAsync` "for symmetry." YAGNI — ProfileResolver is the only caller, and it always knows the alias before calling.
- Do NOT rename `IProfileResolver` or its methods. The interface contract is correct; only the constructor and internal collaborator change.
- Do NOT add the boundary-grep CI lint step in this story (F11). Note it as a deferred-work item only.
- Do NOT cancel the orchestrator's CTS when the heartbeat fails. The orchestrator runs on its own track; heartbeat is observability-adjacent, not control-flow-adjacent.

### Test patterns

- **Backend:** NUnit 4 (`[TestFixture]`, `[Test]`, `Assert.That(..., Is.EqualTo(...))`). NSubstitute for mocks (`Substitute.For<I>()`, `.Returns(...)`, `.Received(n).MethodCall(args)`, `Arg.Any<T>()`, `Arg.Do<T>(action)`). Use `NullLogger<T>.Instance` from `Microsoft.Extensions.Logging.Abstractions` for engine tests; for log-message assertions, use `Substitute.For<ILogger<T>>()` and verify via `.Received().Log(level, ..., predicate, ...)`.
- **Async heartbeat tests:** use `Task.Delay` with millisecond-scale intervals (`TimeSpan.FromMilliseconds(50)`) for the keepalive interval to keep test runtime bounded. Add a generous "wait window" (200–500ms) and tolerance for jitter (e.g. `Assert.That(count, Is.GreaterThanOrEqualTo(3))` for 3 expected fires in 200ms at 50ms intervals).
- **Stream-throwing tests:** use `Substitute.For<Stream>()` with `.WriteAsync(...).ThrowsAsync<IOException>()`. Verify the heartbeat task completes without faulting via `await Task.WhenAny(heartbeatTask, Task.Delay(timeout))`.
- **Run all tests:** `dotnet test AgentRun.Umbraco.slnx` (always specify the slnx — never bare `dotnet test`; per [`feedback_dotnet_test_slnx.md`](../../../.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_dotnet_test_slnx.md)).

### Project Structure Notes

- **Backend changes:**
  - New: [`AgentRun.Umbraco/Engine/IAIChatClientFactory.cs`](../../AgentRun.Umbraco/Engine/IAIChatClientFactory.cs) — pure interface, no Umbraco deps.
  - New: [`AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs`](../../AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs) — adapter, holds Umbraco.AI deps.
  - Modified: [`AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs`](../../AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs) — semaphore + StartKeepaliveAsync.
  - Modified: [`AgentRun.Umbraco/Engine/Events/ISseEventEmitter.cs`](../../AgentRun.Umbraco/Engine/Events/ISseEventEmitter.cs) — new method.
  - Modified: [`AgentRun.Umbraco/Engine/ProfileResolver.cs`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs) — refactor to use `IAIChatClientFactory`, drop Umbraco.AI imports.
  - Modified: [`AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs) — start heartbeat in `ExecuteSseAsync`, inject `IOptions<AgentRunOptions>` for the interval.
  - Modified: [`AgentRun.Umbraco/Configuration/AgentRunOptions.cs`](../../AgentRun.Umbraco/Configuration/AgentRunOptions.cs) — add `KeepaliveInterval` property.
  - Modified: [`AgentRun.Umbraco/Composers/AgentRunComposer.cs`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs) — register new factory.
- **Test changes:**
  - New: [`AgentRun.Umbraco.Tests/Engine/Events/SseEventEmitterTests.cs`](../../AgentRun.Umbraco.Tests/Engine/Events/SseEventEmitterTests.cs).
  - New: [`AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs`](../../AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs).
  - Modified: [`AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs`](../../AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs) — mock surface swapped.
- **Engine boundary preserved:** `Engine/` becomes Umbraco-free (verified via grep, AC9 + Task 10.10).
- **No new NuGet dependencies.**
- **DI:** one new singleton registration line in `AgentRunComposer`. No constructor signature changes for existing types except `ExecutionEndpoints` (new `IOptions<AgentRunOptions>` parameter — affects `ExecutionEndpointsTests` SetUp; trivial fix).
- **No frontend changes.** Confirmed: SSE comment lines are invisible to `EventSource`; ProfileResolver refactor is backend-only.

### Research & Integration Checklist (per Epic 9 retro Process Improvement #1)

- **Umbraco APIs touched:** none directly. Indirectly through `Umbraco.AI.Core.IAIChatService` and `IAIProfileService`, which are confined to the new `UmbracoAIChatClientFactory` adapter.
- **Community resources consulted:**
  - [W3C EventSource spec — comment line semantics](https://www.w3.org/TR/eventsource/#parsing-an-event-stream)
  - [HTML Living Standard SSE authoring notes](https://html.spec.whatwg.org/multipage/server-sent-events.html#authoring-notes)
  - nginx `proxy_read_timeout` documentation, AWS ALB idle-timeout documentation, Cloudflare proxy-timeout documentation (proxy floors)
  - .NET `SemaphoreSlim` async-pattern guidance
- **Real-world content scenarios to test:** auto-detect path with one Umbraco.AI profile (Adam's local config), explicit `default_profile` path, missing-profile path. CQA scanner with default 15s heartbeat over a multi-URL run that takes 60+s of LLM thinking.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story-10.11`](../planning-artifacts/epics.md) — epic-level definition: "two small items from the pre-launch code review that are individually too small for their own stories"
- [Source: `_bmad-output/planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md`](../planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md) — original findings for both tracks
- [Source: `_bmad-output/planning-artifacts/architecture.md` Decision 1](../planning-artifacts/architecture.md) — "Single RCL project structure with clean engine boundary (no Umbraco dependencies in Engine/, ...)"
- [Source: `_bmad-output/implementation-artifacts/10-12-first-run-experience-migration-profile-provider.md`](./10-12-first-run-experience-migration-profile-provider.md) — added `IAIProfileService` to ProfileResolver; explicitly noted "Story 10.11 is scoped to extract this dependency"
- [Source: `_bmad-output/implementation-artifacts/epic-9-retro-2026-04-13.md`](./epic-9-retro-2026-04-13.md) — Process Improvement #1 (Research & Integration Checklist on every Epic 10+ story); Team Agreement #4 (Efficiency Over Elegance — ship the pragmatic solution)
- [Source: `AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs`](../../AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs) — emitter to extend
- [Source: `AgentRun.Umbraco/Engine/Events/ISseEventEmitter.cs`](../../AgentRun.Umbraco/Engine/Events/ISseEventEmitter.cs) — interface to extend
- [Source: `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:311-321`](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L311) — `ExecuteSseAsync` to wire the heartbeat into
- [Source: `AgentRun.Umbraco/Endpoints/SseHelper.cs`](../../AgentRun.Umbraco/Endpoints/SseHelper.cs) — HTTP-level config (UNCHANGED — this story only adds application-level keepalive on top)
- [Source: `AgentRun.Umbraco/Engine/ProfileResolver.cs`](../../AgentRun.Umbraco/Engine/ProfileResolver.cs) — class to refactor
- [Source: `AgentRun.Umbraco/Engine/IProfileResolver.cs`](../../AgentRun.Umbraco/Engine/IProfileResolver.cs) — interface UNCHANGED (contract is correct; only implementation deps change)
- [Source: `AgentRun.Umbraco/Services/ConversationRecorder.cs`](../../AgentRun.Umbraco/Services/ConversationRecorder.cs) — precedent for Engine-adjacent service in `Services/`
- [Source: `AgentRun.Umbraco/Composers/AgentRunComposer.cs:36`](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs#L36) — DI registration block
- [Source: `AgentRun.Umbraco/Configuration/AgentRunOptions.cs`](../../AgentRun.Umbraco/Configuration/AgentRunOptions.cs) — options class to extend with `KeepaliveInterval`
- [Source: W3C EventSource specification](https://www.w3.org/TR/eventsource/#parsing-an-event-stream) — comment-line semantics
- [Source: HTML Living Standard SSE authoring notes](https://html.spec.whatwg.org/multipage/server-sent-events.html#authoring-notes) — keepalive pattern guidance

## Dev Agent Record

### Agent Model Used

Amelia (Claude Opus 4.6, 1M-context) — 2026-04-15

### Debug Log References

- Full backend suite: `dotnet test AgentRun.Umbraco.slnx` → **678/678 passed** (baseline 659 + 19 new).
- Frontend suite: `npm test` in `AgentRun.Umbraco/Client/` → **183/183 passed**, unchanged.
- Frontend build: `npm run build` → clean.
- Engine boundary: `grep -rn "using Umbraco\." AgentRun.Umbraco/Engine/ --include="*.cs"` → **0 matches**.
- AICapability/AIProfile leak check: production code references confined to a single file (`Services/UmbracoAIChatClientFactory.cs`).

### Completion Notes List

- **Track A — SSE keepalive (Tasks 1–3, ACs 1–7)** shipped. `SseEventEmitter` gained a per-emitter `SemaphoreSlim(1,1)` serialising all writes, a `StartKeepaliveAsync(TimeSpan, CancellationToken)` loop that fires `: keepalive\n\n` at the configured interval and swallows `IOException` / `ObjectDisposedException` / `OperationCanceledException` at Debug level, plus a static-readonly `_keepaliveBytes` cache to avoid per-iteration allocations. `ExecuteSseAsync` launches it fire-and-forget against a linked CTS; **explicit `Cancel()` then `Dispose()` in an outer `finally` block** guarantees deterministic teardown on every exit path (happy, OCE/Cancelled, OCE/Interrupted, Exception). `ClampInterval` static helper on `ExecutionEndpoints` enforces `[5s, 300s]`; the "clamp applied" warn-log is gated on a process-wide `Interlocked.CompareExchange` so it fires at most once per process for a persistently misconfigured value. 7 new emitter tests (keepalive AC1–AC4 + the linked-CTS cancel-then-dispose regression) + 7 clamp tests cover AC1–AC4, AC7, and F3/F4.
- **Track B — ProfileResolver Engine boundary (Tasks 4–9, ACs 8–14)** shipped. New `Engine/IAIChatClientFactory.cs` exposes only `string` / `CancellationToken` / `Microsoft.Extensions.AI.IChatClient` / `IReadOnlyList<string>` — zero Umbraco.AI surface. `Services/UmbracoAIChatClientFactory.cs` is the sole holder of the four `Umbraco.AI.Core.*` imports; AICapability + AIProfile confined to this one file. `ProfileResolver` constructor swapped from `(IAIChatService, IAIProfileService, …)` to `(IAIChatClientFactory, …)`; sort-and-pick policy stays in ProfileResolver (locked decision 7); `HasConfiguredProviderAsync` now short-circuits when no profile is resolvable (F10 improvement — old code sometimes threw a misleading exception on the null-profile path). `AgentRunComposer` registers the adapter singleton before `IProfileResolver` (AC13). `ProfileResolverTests` rewritten against the single factory mock: 19 tests preserved by name + BDD intent, argument matchers simpler (positional strings instead of `Action<AIChatBuilder>`). 4 new `UmbracoAIChatClientFactoryTests` cover the adapter's translation contract.
- **Name collision handled:** `Umbraco.AI.Core.Chat` already exports an `IAIChatClientFactory` type. The adapter file uses a `using EngineChatClientFactory = AgentRun.Umbraco.Engine.IAIChatClientFactory;` alias to disambiguate; the engine-side interface itself lives cleanly in `AgentRun.Umbraco.Engine` with no alias needed.
- **Tests deliberately NOT added for `AIChatBuilder` shape:** the builder is a sealed Umbraco.AI type without a public test-friendly constructor. Per Task 9 guidance, adapter tests assert on `_chatService.Received(1).CreateChatClientAsync(Arg.Any<Action<AIChatBuilder>>(), …)` plus return-value pass-through; the precise builder shape is covered by manual E2E (Task 10.4).
- **Status:** ✅ Done. Backend 679/679; manual E2E complete (Gate 1 DI smoke, Gate 2+3 keepalive via browser DevTools raw response body at 5s override — 11 keepalives during input.wait, 1 between user.message and tool.start, 8 during analyser LLM thinking, 13 during reporter read+think; Gate 4 long-LLM happy path through analyser's massive `list_content_types` tool payload + reporter's multi-kB `audit-report.md` write with zero malformed frames and zero Interrupted flips; Gate 5 auto-detect path through full scanner → analyser → reporter chain ending in `run.finished: Completed`). TestSite appsettings override restored to default before commit. The curl-based CLI smoke (Task 3.3) was attempted but skipped: Umbraco 14+ management API requires bearer-decrypted tokens via the backoffice middleware, so raw `__Host-umbAccessToken` values can't be validated by OpenIddict as JWTs — browser DevTools proved the identical wire-level behaviour without the auth ceremony.
- **Test count delta:** 659 → 679 = **+20** (vs. the story's +9 estimate). Drift is because the clamp helper attracted more coverage (7 tests) than the spec budgeted (3), and the emitter gained 7 keepalive tests (6 original + the post-review linked-CTS teardown regression) while shedding zero existing tests. ProfileResolverTests preserved exactly 19 tests — renamed one setup-method but count unchanged.
- **Post-review fixes (2026-04-15):** Code review surfaced three issues that were fixed in-place before DoD: (H1) `keepaliveCts` was constructed with `using var` and never explicitly cancelled — `CancellationTokenSource.Dispose()` does NOT cancel the token, so heartbeat teardown on the happy path was coincidental via the next stream-write failure rather than deterministic via cancellation. Replaced with an outer `try/finally` that calls `Cancel()` then `Dispose()` explicitly. Added a `StartKeepaliveAsync_LinkedCtsCancelThenDispose_ExitsCleanly` regression test that codifies the production call pattern. (M1) The "clamp applied" warn-log fired on every SSE request for a misconfigured interval; gated on `Interlocked.CompareExchange` so it fires at most once per process lifetime (locked decision 12). (L1) Collapsed three nested `OperationCanceledException` catches in `StartKeepaliveAsync` into a single outer catch — the inner Task.Delay and WaitAsync both throw the same OCE which the outer handler already swallows; the nested catches were defensive but redundant. All existing tests green; count rose from 678 → 679 with the new regression test.

### File List

**New:**
- `AgentRun.Umbraco/Engine/IAIChatClientFactory.cs`
- `AgentRun.Umbraco/Services/UmbracoAIChatClientFactory.cs`
- `AgentRun.Umbraco.Tests/Services/UmbracoAIChatClientFactoryTests.cs`

**Modified:**
- `AgentRun.Umbraco/Configuration/AgentRunOptions.cs` — added `KeepaliveInterval` property (default 15s).
- `AgentRun.Umbraco/Engine/Events/ISseEventEmitter.cs` — added `StartKeepaliveAsync` method to interface.
- `AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs` — added `_writeLock` semaphore, `_keepaliveBytes` cache, `StartKeepaliveAsync` implementation; wrapped `EmitAsync` body in the semaphore.
- `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — injected `IOptions<AgentRunOptions>`, added `ClampInterval` static helper + `KeepaliveMin`/`KeepaliveMax` constants, started heartbeat in `ExecuteSseAsync` with linked CTS teardown.
- `AgentRun.Umbraco/Engine/ProfileResolver.cs` — refactored to use `IAIChatClientFactory`; dropped all 4 `using Umbraco.AI.Core.*` imports; added F10 short-circuit in `HasConfiguredProviderAsync`.
- `AgentRun.Umbraco/Composers/AgentRunComposer.cs` — registered `IAIChatClientFactory → UmbracoAIChatClientFactory` singleton before `IProfileResolver`.
- `AgentRun.Umbraco.Tests/Configuration/AgentRunOptionsTests.cs` — added `Defaults_KeepaliveInterval_Is15Seconds` and `KeepaliveInterval_CanBeSet`.
- `AgentRun.Umbraco.Tests/Engine/Events/SseEventEmitterTests.cs` — added 7 keepalive tests: AC1–AC4 (6) plus the linked-CTS cancel-then-dispose regression (1, added post-review).
- `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs` — added `Options.Create(new AgentRunOptions())` to SetUp; added 7 `ClampInterval` tests (AC7, F3, F4).
- `AgentRun.Umbraco.Tests/Engine/ProfileResolverTests.cs` — swapped `_chatService` + `_profileService` mocks for a single `_chatClientFactory` mock; dropped `Umbraco.AI.Core.*` imports; 19 tests preserved by name and BDD intent.

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-15 | Story 10.11 implementation — Track A (SSE keepalive) + Track B (ProfileResolver Engine boundary). Backend 678/678, frontend 183/183, boundary grep zero. Manual E2E (Tasks 3.3 + 10.1–10.5) pending Adam's browser testing. | Amelia |
| 2026-04-15 | Post-review fixes. H1: `keepaliveCts` moved from `using var` to explicit `try/finally` with `Cancel()` then `Dispose()` — `CancellationTokenSource.Dispose()` does not cancel the token, so the prior pattern relied on stream-write failure rather than deterministic cancellation. Task 3.2 step 2 corrected in-spec. Added `StartKeepaliveAsync_LinkedCtsCancelThenDispose_ExitsCleanly` regression. M1: clamp warn-log gated on `Interlocked.CompareExchange` so it fires at most once per process (locked decision 12). L1: collapsed three nested OCE catches in `StartKeepaliveAsync` to a single outer catch. Backend 679/679. | Amelia |
| 2026-04-15 | Story → done. Manual E2E passed all five gates against TestSite with `KeepaliveInterval: "00:00:05"` override: DI smoke clean at startup, keepalives fired reliably throughout input.wait / LLM-thinking / tool-payload windows, full CQA workflow completed scanner → analyser → reporter ending in `run.finished: Completed`, no Interrupted flip, no console warnings, auto-detect path through `IAIChatClientFactory` → `UmbracoAIChatClientFactory` verified end-to-end. Override restored to default. | Adam + Amelia |
