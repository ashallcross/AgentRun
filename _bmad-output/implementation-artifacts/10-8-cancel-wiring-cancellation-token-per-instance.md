# Story 10.8: Cancel Wiring — CancellationToken Per Instance

Status: done

**Depends on:** None (existing `IActiveInstanceRegistry`, `WorkflowOrchestrator`, `StepExecutor`, `ToolLoop` plumbing already in place)
**Branch:** `feature/epic-10`
**Priority:** 3rd in Epic 10 (10.2 done → 10.12 done → **10.8** → 10.9 → 10.10 → 10.1 → 10.6 → 10.11 → 10.7 → 10.4 → 10.5)

> **Beta known issue — listed in epics.md §"Known Issues for Beta v1".** Today, clicking Cancel only updates the persisted YAML status to `Cancelled`; it does **not** signal the running orchestrator. The tool loop continues calling the LLM provider and executing tools until the step finishes naturally. Tokens keep burning on a run the user thought was stopped. This story wires the cancel endpoint to a per-instance `CancellationTokenSource` so Cancel actually stops in-flight work.

## Story

As a backoffice user,
I want clicking Cancel to actually stop the running workflow step,
So that I'm not burning LLM tokens on a run I've already cancelled.

## Context

**UX Mode: N/A — engine + endpoint plumbing change. No new UI work.** The Cancel button already exists ([UX-DR18, UX-DR20 in epics.md](../planning-artifacts/epics.md)) and posts to `POST /umbraco/api/agentrun/instances/{id}/cancel`. This story makes that POST do what the user thinks it does.

**The bug being fixed:**

The cancel endpoint at [InstanceEndpoints.cs:77-106](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L77) only calls `_instanceManager.SetInstanceStatusAsync(..., InstanceStatus.Cancelled, ...)` — a YAML write. The orchestrator running the step (`WorkflowOrchestrator.ExecuteNextStepAsync` on a different thread, with a `CancellationToken` derived from `HttpContext.RequestAborted` of the SSE request) never observes the cancel because the two paths share no signalling primitive. The tool loop keeps streaming LLM responses and dispatching tool calls until the step completes naturally or the SSE client disconnects. On a long-running scanner step (Sonnet 4.6 + 25 `get_content` calls), this can mean 30–60 seconds of additional billable work after the user clicked Cancel.

**The fix shape:**

Hold a per-instance `CancellationTokenSource` in `ActiveInstanceRegistry` (which already holds the per-instance message `Channel<string>` — same lifetime, same key, same singleton service). The orchestrator links the per-instance CTS with the HTTP request token via `CancellationTokenSource.CreateLinkedTokenSource` and passes the linked token down through `StepExecutor` → `ToolLoop`. The cancel endpoint persists `Cancelled` first, then triggers the CTS. The existing `cancellationToken` plumbing through the engine (already present at every `await`) does the rest — `OperationCanceledException` propagates out, the SSE handler's existing OCE catch fires, and the step terminates within seconds.

### Architect's locked decisions (do not relitigate)

1. **CTS lives in `ActiveInstanceRegistry` alongside the message channel.** Same singleton, same key (`instanceId`), same lifetime contract (`RegisterInstance` creates, `UnregisterInstance` disposes). Do NOT introduce a new service. Do NOT move the message channel out. The registry's role is "per-instance ephemeral state shared across HTTP requests" and CTS fits that exactly.
2. **Orchestrator uses a linked token (`CreateLinkedTokenSource`).** The linked source combines `httpRequestAborted` (existing behaviour) with `instanceCts.Token` (new behaviour). Either source firing cancels the run. Do NOT replace the HTTP token entirely — that's Story 10.9's scope (distinguishing disconnect from failure). Story 10.8 is the minimum surgery to make Cancel work; it does not relitigate the SSE-disconnect contract.
3. **Cancel endpoint order: persist `Cancelled` FIRST, then trigger the CTS.** This guarantees that by the time `OperationCanceledException` propagates to the SSE catch handler, the persisted status is already `Cancelled`. The OCE handler reads the status and skips the existing `SetInstanceStatusAsync(Failed, ...)` call when it sees `Cancelled` — preventing the failure overwrite race.
4. **`ExecutionEndpoints.ExecuteSseAsync` OCE handler reads current status before overwriting.** If the persisted status is `Cancelled`, do nothing (the cancel endpoint already wrote it). Otherwise, preserve existing behaviour: write `Failed`. This is the minimal change that respects 10.9's future scope — 10.9 will refine the disconnect path; 10.8 only touches the cancel path.
5. **Token check insertion points are explicit, not implicit.** Add `cancellationToken.ThrowIfCancellationRequested()` at: (a) the top of the orchestrator's step loop, (b) the top of each `ToolLoop` iteration (before `DrainUserMessagesAsync`), and (c) after the tool batch completes (between tool execution and the next LLM call). The existing implicit checks inside `client.GetStreamingResponseAsync` and `tool.ExecuteAsync` cover the long-running paths; the explicit checks cover the short gaps between them so cancellation is observed within the same tool turn.
6. **CTS disposal on re-registration.** If `RegisterInstance` is called for an `instanceId` that already has an entry (e.g., a previous run that crashed without `UnregisterInstance` running), dispose the old CTS and channel before creating new ones. Use `ConcurrentDictionary.AddOrUpdate` with disposal in the update factory. Test this case explicitly — it would otherwise leak.
7. **Tool side effects are not rolled back on cancel.** A `write_file` that completed before cancellation stays on disk. A `fetch_url` that wrote to `.fetch-cache/` stays cached. This is intentional and matches the existing failure semantics — workflows are not transactional, the JSONL conversation log is the source of truth, and a cancelled run can be inspected via the artifact viewer. Document this in the spec; do not engineer rollback.
8. **No new `Cancelling` interim status.** The persisted enum stays as defined in [Instances/InstanceStatus.cs:6-13](../../AgentRun.Umbraco/Instances/InstanceStatus.cs#L6) (`Pending`, `Running`, `Completed`, `Failed`, `Cancelled`). Cancel transitions `Running → Cancelled` directly. Adding a `Cancelling` state would require migrating existing on-disk YAML and is unnecessary — the wall-clock window between "Cancel clicked" and "step terminated" is sub-second once the CTS fires; users do not see the gap.

## Acceptance Criteria (BDD)

### AC1: Cancel endpoint signals the per-instance CTS

**Given** an instance is running a step (status `Running`, registered in `ActiveInstanceRegistry`, with an active `CancellationTokenSource`)
**When** `POST /umbraco/api/agentrun/instances/{id}/cancel` is called
**Then** the persisted instance status is updated to `Cancelled` (existing behaviour, preserved)
**And** the per-instance `CancellationTokenSource` is signalled (`Cancel()` called)
**And** the orchestrator's running step terminates with `OperationCanceledException` within 1 second of the cancel call (in unit-test conditions; production allows for in-flight network round-trips to complete)
**And** the endpoint returns the updated `InstanceResponse` with `status: "Cancelled"`

### AC2: Cancel endpoint is safe when no orchestrator is active

**Given** an instance with persisted status `Pending` that has never been started (no entry in `ActiveInstanceRegistry`)
**When** `POST /.../cancel` is called
**Then** the persisted status is updated to `Cancelled`
**And** no exception is thrown despite the absent CTS
**And** the endpoint returns 200 with the updated state

### AC3: ActiveInstanceRegistry exposes CTS lifecycle

**Given** the `IActiveInstanceRegistry` interface
**When** the registry is used during a workflow run
**Then** `RegisterInstance(instanceId)` creates and stores a fresh `CancellationTokenSource` keyed by `instanceId` and returns its token (or returns the channel reader as today; the CTS is retrieved separately — see Task 1 for the chosen API shape)
**And** `RequestCancellation(instanceId)` triggers `Cancel()` on the stored CTS if present, no-op if absent
**And** `UnregisterInstance(instanceId)` disposes the CTS and removes the entry
**And** re-registering an `instanceId` that already has an entry disposes the old CTS+channel before creating new ones (no leak)

### AC4: Orchestrator uses the linked token

**Given** `WorkflowOrchestrator.ExecuteNextStepAsync` is invoked from the SSE endpoint
**When** the orchestrator builds the cancellation token for the run
**Then** it constructs a `CancellationTokenSource.CreateLinkedTokenSource(httpRequestToken, instanceToken)` using the per-instance CTS token from the registry
**And** the linked token is passed to `_stepExecutor.ExecuteStepAsync`, all `recorder` calls, all `emitter` calls, and the `Task.Delay` in autonomous mode
**And** the linked source is disposed when the orchestrator method completes (in `finally` alongside `UnregisterInstance`)

### AC5: ToolLoop observes cancellation between iterations

**Given** a running tool loop with a cancellation token
**When** the token is signalled between tool calls (e.g., immediately after a `tool.ExecuteAsync` returns and before the next iteration)
**Then** the next iteration's explicit `cancellationToken.ThrowIfCancellationRequested()` call (top of `while (true)` loop, before `DrainUserMessagesAsync`) throws `OperationCanceledException`
**And** the OCE propagates out without writing partial state to the JSONL log beyond the tool result already recorded
**And** the same check runs after the tool batch completes (between tool execution and the next LLM call), so cancellation is observed within the same tool turn rather than waiting for the next streaming chunk

### AC6: SSE OCE handler does not overwrite a Cancelled status

**Given** the cancel endpoint has already persisted `Cancelled` and signalled the CTS
**When** `OperationCanceledException` propagates to the `ExecuteSseAsync` catch block ([ExecutionEndpoints.cs:174-189](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174))
**Then** the handler reads the current persisted instance status before calling `SetInstanceStatusAsync`
**And** if the current status is `Cancelled`, the handler skips the `Failed` overwrite (and logs at Information level: "Cancellation observed for instance {InstanceId}")
**And** if the current status is not `Cancelled` (e.g., the SSE client disconnected without a cancel call — the existing 10.9-territory path), the handler preserves existing behaviour and writes `Failed`
**And** the OCE is re-thrown after status handling (existing behaviour preserved)

### AC7: Cancel during tool execution is observed within the same tool turn

**Given** a long-running tool call (e.g., `fetch_url` mid-stream) is in progress
**When** the cancel endpoint is called during the tool's execution
**Then** the tool's own `cancellationToken` parameter is signalled via the linked source (existing plumbing — `ExecuteAsync` already accepts the token)
**And** if the tool surfaces `OperationCanceledException` (the documented contract for `IWorkflowTool`), it propagates out of the `try` block at [ToolLoop.cs:388-408](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L388) via the existing `catch (OperationCanceledException) { throw; }` rethrow
**And** no `FunctionResultContent` is appended for the cancelled call (the `try` block exits before `resultContents.Add`)
**And** no further tools in the batch are executed (the `foreach` loop is abandoned)

### AC8: Cancel is idempotent

**Given** the cancel endpoint has been called once and status is now `Cancelled`
**When** the endpoint is called a second time
**Then** the existing 409 Conflict response fires (status is no longer `Running` or `Pending` — current guard at [InstanceEndpoints.cs:93-100](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L93))
**And** no second `Cancel()` call is made on the CTS (the request is rejected before reaching the registry)

### AC9: Re-registration disposes prior CTS without leaking

**Given** an `instanceId` was previously registered (from a prior run that exited without calling `UnregisterInstance` — e.g., a process crash mid-run is not realistic, but a programming bug elsewhere is)
**When** `RegisterInstance(instanceId)` is called again
**Then** the previously-stored `CancellationTokenSource` is disposed
**And** the previously-stored `Channel<string>` is completed (writer side) so any pending readers exit cleanly
**And** a fresh CTS and fresh channel are stored under the same key
**And** a unit test asserts no `ObjectDisposedException` leaks from the disposal path

### AC10: Cancel during between-step pause (interactive mode) cleanly stops the loop

**Given** an interactive-mode workflow with multiple steps, currently paused between step N and step N+1 (orchestrator has returned to the SSE endpoint and the user has not yet POSTed `/start` for the next step)
**When** the cancel endpoint is called
**Then** the persisted status is updated to `Cancelled`
**And** because no orchestrator is currently running, the CTS trigger is a no-op (registry has no entry — `UnregisterInstance` ran when the previous step's orchestrator returned)
**And** when the user (or a stale UI) attempts to POST `/start` to advance to step N+1, the existing 409 guard rejects it (status is no longer `Running` or `Pending`) — verify the existing guard at [ExecutionEndpoints.cs StartInstance handler](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs) covers this case; if not, surface as a Failure & Edge Case

## Files in Scope

| File | Change |
|---|---|
| [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs) | Add per-instance `CancellationTokenSource` storage. Extend `IActiveInstanceRegistry` with `GetCancellationToken(instanceId)` and `RequestCancellation(instanceId)` methods. Modify `RegisterInstance` to also create the CTS. Modify `UnregisterInstance` to dispose it. Handle re-registration cleanly (dispose old CTS+channel before storing new). |
| [AgentRun.Umbraco/Engine/IActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs) | Interface lives in the same file as the implementation today; extend the interface with the two new methods (see Task 1 for shape). |
| [AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) | After `RegisterInstance`, retrieve the per-instance token, construct a `CreateLinkedTokenSource(cancellationToken, instanceToken)`, and pass `linked.Token` everywhere `cancellationToken` is currently passed in the method body. Add `cancellationToken.ThrowIfCancellationRequested()` at the top of the `while (true)` step loop. Dispose the linked source in `finally`. |
| [AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs) | After `SetInstanceStatusAsync(..., Cancelled, ...)` succeeds (line 102-103), call `_activeInstanceRegistry.RequestCancellation(state.InstanceId)`. Inject `IActiveInstanceRegistry` into the controller via constructor. |
| [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs) | In `ExecuteSseAsync`'s `catch (OperationCanceledException)` block (lines 174-189), read the current persisted status before calling `SetInstanceStatusAsync(..., Failed, ...)`. Skip the `Failed` overwrite if the current status is already `Cancelled`. Log at Information level when the skip happens. Keep the rethrow. |
| [AgentRun.Umbraco/Engine/ToolLoop.cs](../../AgentRun.Umbraco/Engine/ToolLoop.cs) | Add explicit `cancellationToken.ThrowIfCancellationRequested()` at the top of the `while (true)` loop (before line 81's `DrainUserMessagesAsync`). Add a second explicit check after the tool batch completes (between line 447's `messages.Add(new ChatMessage(ChatRole.Tool, resultContents))` and line 461's second `DrainUserMessagesAsync`). The existing implicit checks inside `client.GetStreamingResponseAsync(messages, options, cancellationToken)` and per-tool `tool.ExecuteAsync(arguments, context, cancellationToken)` calls are preserved unchanged. |
| [AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs](../../AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs) | Add tests for: CTS created on `RegisterInstance`, `RequestCancellation` triggers it, `UnregisterInstance` disposes, re-registration disposes prior CTS, no-op when instance not registered. |
| [AgentRun.Umbraco.Tests/Engine/WorkflowOrchestratorTests.cs](../../AgentRun.Umbraco.Tests/Engine/) (NEW or extend if exists) | Tests for: linked token cancels step on either source, CTS retrieved from registry, linked source disposed in finally. If no test class exists for the orchestrator yet, create one — see Task 6 for fixture scaffolding. |
| [AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs](../../AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs) | Add tests for: explicit `ThrowIfCancellationRequested` at iteration top fires when token cancelled between iterations; no `FunctionResultContent` is appended for a tool that throws OCE; second batch of tools is not executed when first tool in batch throws OCE. |
| [AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/) (NEW or extend if exists) | Test cancel endpoint calls `RequestCancellation` on registry. Mock `IActiveInstanceRegistry`. |
| [AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs) | Add test: when OCE propagates and persisted status is already `Cancelled`, the OCE handler does NOT call `SetInstanceStatusAsync(..., Failed, ...)`. When status is `Running` (disconnect path), behaviour is preserved (writes `Failed`). |

**Files explicitly NOT touched:**

- `AgentRun.Umbraco/Instances/InstanceStatus.cs` — enum is correct as-is, no `Cancelling` interim status (locked decision 8)
- `AgentRun.Umbraco/Instances/ConversationStore.cs` and `IConversationRecorder` — JSONL persistence is unchanged; cancelled runs leave the JSONL log intact for inspection
- `AgentRun.Umbraco/Engine/StepExecutor.cs` — accepts `CancellationToken` already; no signature changes needed, the linked token flows through naturally
- `AgentRun.Umbraco/Tools/*.cs` — every tool already accepts and respects `CancellationToken` (verified via Story 5.1's `IWorkflowTool` contract); no per-tool changes
- Frontend `Client/agentrun-instance-detail.element.ts` — Cancel button POST already exists; no UI work
- `AgentRun.Umbraco/Composers/AgentRunComposer.cs` — `IActiveInstanceRegistry` is already registered as Singleton; no DI changes (the controller already gets it via DI; just add the constructor parameter to `InstanceEndpoints`)

## Tasks

### Task 1: Extend ActiveInstanceRegistry with per-instance CTS (AC2, AC3, AC9)

- [x] Modify `IActiveInstanceRegistry` and `ActiveInstanceRegistry` to manage per-instance `CancellationTokenSource` lifecycle.

1. Replace the internal `ConcurrentDictionary<string, Channel<string>> _channels` with a record/class holding both the channel and the CTS:
   ```csharp
   private sealed record InstanceEntry(Channel<string> Channel, CancellationTokenSource CancellationSource);
   private readonly ConcurrentDictionary<string, InstanceEntry> _entries = new();
   ```
2. Add to `IActiveInstanceRegistry`:
   ```csharp
   CancellationToken? GetCancellationToken(string instanceId);
   void RequestCancellation(string instanceId);
   ```
3. Modify `RegisterInstance(instanceId)` to:
   - Create a fresh `Channel<string>` and `CancellationTokenSource`
   - Use `AddOrUpdate` to atomically replace any existing entry: in the update factory, dispose the old CTS and complete the old channel writer, then return the new entry
   - Return the new channel reader (signature unchanged for callers)
4. Implement `GetCancellationToken(instanceId)`: return the CTS's `Token` if entry exists, else `null`.
5. Implement `RequestCancellation(instanceId)`: if entry exists, call `entry.CancellationSource.Cancel()` (idempotent, safe to call on a disposed-but-not-yet-removed entry — wrap in try/catch `ObjectDisposedException` defensively or rely on the timing guarantee that `UnregisterInstance` is the only path that disposes).
6. Modify `UnregisterInstance(instanceId)` to: `TryRemove` the entry; if removed, dispose the CTS and complete the channel writer.
7. Implement `IDisposable` on `ActiveInstanceRegistry` to dispose all remaining CTS instances on container shutdown (defensive — should normally be empty, but covers process-shutdown leaks).

**Key constraint:** All operations must be thread-safe — multiple HTTP requests can hit the same `instanceId` simultaneously. Use `ConcurrentDictionary` primitives (`AddOrUpdate`, `TryRemove`, `TryGetValue`) only; do not introduce `lock` blocks.

### Task 2: Wire orchestrator to use the linked token (AC4)

- [x] Modify `WorkflowOrchestrator.ExecuteNextStepAsync` to construct a linked source from the HTTP token and the per-instance CTS token, and pass the linked token everywhere downstream.

1. After `var userMessageReader = _activeInstanceRegistry.RegisterInstance(instanceId);` (line 47), retrieve the per-instance token: `var instanceToken = _activeInstanceRegistry.GetCancellationToken(instanceId) ?? CancellationToken.None;`
2. Construct: `using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, instanceToken);`
3. Replace every use of `cancellationToken` inside the `try { while (true) { ... } }` block with `linkedCts.Token`. Note: the OCE handlers and `Failed`-status writes that already use `CancellationToken.None` (lines 116, 119, 124, 133) stay as `CancellationToken.None` — those must complete even after cancellation.
4. Add `linkedCts.Token.ThrowIfCancellationRequested();` as the first statement inside the `while (true)` loop body (after line 51).
5. Verify in code review: `_activeInstanceRegistry.UnregisterInstance(instanceId)` in the `finally` (line 178) still runs in the cancellation path — registry disposal must complete even when OCE is propagating.

**Naming:** `linkedCts` and `instanceToken` are clear. Do not over-abbreviate.

### Task 3: Wire cancel endpoint to trigger the CTS (AC1, AC8)

- [x] Modify `InstanceEndpoints.CancelInstance` to call `RequestCancellation` after persisting `Cancelled`.

1. Inject `IActiveInstanceRegistry` into `InstanceEndpoints` via constructor (alongside `_instanceManager`).
2. After the existing `SetInstanceStatusAsync(..., InstanceStatus.Cancelled, cancellationToken)` call (line 102-103), add: `_activeInstanceRegistry.RequestCancellation(state.InstanceId);`
3. The order is critical (locked decision 3): persist FIRST, signal SECOND. This guarantees the SSE OCE handler sees `Cancelled` when it reads the status.
4. The existing 409 guard at lines 93-100 already prevents double-cancellation; AC8 falls out for free.

### Task 4: SSE OCE handler skips Failed overwrite when status is Cancelled (AC6)

- [x] Modify `ExecutionEndpoints.ExecuteSseAsync` `catch (OperationCanceledException)` block.

1. Inside the catch (line 174), before the `SetInstanceStatusAsync(..., Failed, ...)` call:
   ```csharp
   var current = await _instanceManager.FindInstanceAsync(instance.InstanceId, CancellationToken.None);
   if (current is not null && current.Status == InstanceStatus.Cancelled)
   {
       _logger.LogInformation(
           "Cancellation observed for instance {InstanceId}; preserving Cancelled status",
           instance.InstanceId);
       throw;
   }
   ```
2. The existing `SetInstanceStatusAsync(..., Failed, ...)` call at line 178-179 stays unchanged for the disconnect path (no cancel endpoint was called → status is still `Running` → write `Failed`). Story 10.9 will refine this further.
3. The `throw;` at line 188 stays; the OCE must continue to propagate out of the SSE method so ASP.NET aborts the response cleanly.

**Why a fresh `FindInstanceAsync` instead of trusting the in-memory `instance` variable:** the `instance` parameter was loaded at the start of `ExecuteSseAsync`; the cancel endpoint mutated the persisted status concurrently. We must read the latest.

### Task 5: Add explicit token checks in ToolLoop (AC5, AC7)

- [x] Add two `cancellationToken.ThrowIfCancellationRequested()` calls in `ToolLoop.RunAsync`.

1. **First check:** at the top of the `while (true)` loop, before the existing `if (++iteration > MaxIterations)` guard. This catches cancellation observed between the previous iteration's bottom-of-loop drain and the next iteration's LLM call.
2. **Second check:** immediately after `messages.Add(new ChatMessage(ChatRole.Tool, resultContents));` (line 447), before the `if (compactThreshold > 0)` block. This catches cancellation observed during the tool batch — if the user clicked Cancel mid-batch, the loop terminates after the in-flight tool returns rather than proceeding to the next LLM call.
3. The existing `catch (OperationCanceledException) { throw; }` rethrows in the streaming block (line 113-116) and per-tool block (line 405-408) are preserved unchanged.
4. Do NOT add token checks inside the per-tool `foreach` loop — each tool's `ExecuteAsync` already receives the token and is contractually required to respect it (Story 5.1's `IWorkflowTool` contract). Adding a check between tools would be defensive coding for a contract that already holds.

### Task 6: Tests for ActiveInstanceRegistry CTS lifecycle (AC2, AC3, AC9)

- [x] Extend `ActiveInstanceRegistryTests.cs` with the following NUnit tests:

1. **CTS created on RegisterInstance** — call `RegisterInstance("abc")`, then `GetCancellationToken("abc")` returns a non-null token whose `IsCancellationRequested` is false.
2. **GetCancellationToken returns null when not registered** — for a never-registered `instanceId`, returns `null`.
3. **RequestCancellation triggers the CTS** — register, get token, call `RequestCancellation`, assert token's `IsCancellationRequested` is true.
4. **RequestCancellation is no-op when not registered** — call on a never-registered id, no exception thrown.
5. **UnregisterInstance disposes the CTS** — register, unregister, then `GetCancellationToken` returns null. (Asserting actual disposal requires reflection or a custom CTS subclass — instead assert via the public observable: `GetCancellationToken` returns null after unregister.)
6. **Re-registration disposes prior CTS** — register, get token A, register again, get token B; A and B are different instances; signalling A no longer affects the registry's state for `instanceId`.
7. **Re-registration completes prior channel writer** — register, get writer A, register again; writing to A returns `false` (channel completed) or throws `ChannelClosedException` (whichever the `Channel<T>` API surfaces — assert the observable behaviour).
8. **Concurrent RegisterInstance from multiple threads** — race two `RegisterInstance` calls, assert exactly one entry persists, no `ObjectDisposedException` leaks.

### Task 7: Tests for orchestrator linked-token wiring (AC4)

- [x] Add or extend `WorkflowOrchestratorTests.cs` (create the file if it doesn't exist; mirror the structure of `StepExecutorTests.cs`).

1. **Linked token cancels on instance CTS** — start an `ExecuteNextStepAsync` task with a never-cancelled `httpToken`, signal the per-instance CTS via `RequestCancellation`, assert the task completes with `OperationCanceledException` within a short timeout.
2. **Linked token cancels on HTTP token** — start `ExecuteNextStepAsync` with a `httpToken` from a separate CTS, cancel the HTTP CTS, assert the task completes with OCE (preserves existing behaviour).
3. **Linked source disposed in finally** — verify via observable behaviour: after `ExecuteNextStepAsync` returns or throws, `UnregisterInstance` was called (mock verification on `IActiveInstanceRegistry`).
4. **Step loop top check fires** — set up a fake `IStepExecutor` whose `ExecuteStepAsync` blocks on a `TaskCompletionSource`; signal cancellation via the per-instance CTS; complete the step executor's TCS; assert the next iteration's `ThrowIfCancellationRequested` fires before the next step begins.

**Fixture pattern:** Use `NSubstitute` to mock `IInstanceManager`, `IWorkflowRegistry`, `IStepExecutor`, `IConversationStore`, `IActiveInstanceRegistry`, `ISseEventEmitter`. Use the real `WorkflowOrchestrator`. Construct workflows in tests using `WorkflowDefinition` records — see existing `StepExecutorTests.cs` for the pattern.

### Task 8: Tests for ToolLoop explicit token checks (AC5, AC7)

- [x] Extend `ToolLoopTests.cs` with tests for the new explicit checks.

1. **Top-of-iteration check fires** — build a fake `IChatClient` whose first response triggers a tool call (so we enter at least 2 iterations), inject a `CancellationTokenSource` that is cancelled between iterations (via a wrapper around the streaming response), assert `ToolLoop.RunAsync` throws OCE before the second LLM call.
2. **Post-batch check fires** — build a multi-tool batch where the first tool returns successfully and signals cancellation as a side effect; assert the second LLM call is never made (verify via mock `IChatClient` that `GetStreamingResponseAsync` was called exactly once).
3. **Tool throwing OCE skips remaining tools in batch** — build a batch with two tool calls; the first throws `OperationCanceledException`; assert the second tool's `ExecuteAsync` is never called and no `FunctionResultContent` is appended.
4. **Tool throwing OCE does not record a tool result** — same scenario; assert `IConversationRecorder.RecordToolResultAsync` was NOT called for the cancelled tool.

### Task 9: Tests for cancel endpoint and SSE OCE handler (AC1, AC6)

- [x] Add or extend `InstanceEndpointsTests.cs` and `ExecutionEndpointsTests.cs`.

1. **InstanceEndpoints: cancel calls RequestCancellation** — mock `IActiveInstanceRegistry`, set up an instance with `Running` status, call `CancelInstance`, verify `RequestCancellation(instanceId)` was called exactly once after `SetInstanceStatusAsync` returned.
2. **InstanceEndpoints: cancel order — persist before signal** — use a recorder mock that captures call order; assert `SetInstanceStatusAsync` was called before `RequestCancellation`.
3. **ExecutionEndpoints: OCE with Cancelled status skips Failed overwrite** — set up `_instanceManager.FindInstanceAsync` to return an instance with `Cancelled` status when called inside the catch; trigger OCE from a fake orchestrator; assert `SetInstanceStatusAsync(..., Failed, ...)` was NOT called.
4. **ExecutionEndpoints: OCE with Running status writes Failed** — same setup but `FindInstanceAsync` returns `Running`; assert `SetInstanceStatusAsync(..., Failed, ...)` IS called (preserves disconnect-path behaviour for Story 10.9 to refine).
5. **ExecutionEndpoints: OCE rethrows after status handling** — verify the rethrow at the end of the catch block fires in both Cancelled and Running cases.

### Task 10: Manual E2E verification

- [x] 10.1. Start a fresh Content Audit instance against the Clean Starter Kit test site (26 nodes). Wait for the scanner step to be mid-batch (3–5 `get_content` calls completed, more in flight). **Verified 2026-04-14 — instance `1eb435ed6f804efd9cad32fa8277037f` (umbraco-content-audit) cancelled mid-scan after 2 of 27 nodes.**
- [x] 10.2. Click Cancel in the backoffice UI. Confirm the dialog (UX-DR18 confirms via `uui-dialog`). **Verified — Cancel button now shows in interactive mode mid-stream (UI gate amendment 1.1). Dialog fires, confirm works.**
- [x] 10.3. Observe in the chat panel: the streaming text stops within 1–2 seconds (no more text deltas, no more tool calls). **Verified — stream stopped within ~1s of cancel click.**
- [x] 10.4. Refresh the instance detail. Verify status badge shows `Cancelled` (not `Failed`). **Verified — status persisted as `Cancelled`. No `Failed` overwrite observed.**
- [x] 10.5. Inspect server logs for: `"Cancellation observed for instance {InstanceId}; preserving Cancelled status"` (Information level). No `"Failed to set instance ... status to Failed"` Critical-level log entries should appear. **Verified — `[INF] Cancellation observed for instance 1eb435ed6f804efd9cad32fa8277037f; preserving Cancelled status` fires. No Critical-level Failed-overwrite entries. Two `[ERR]` lines appear from Umbraco.AI middleware (usage recording + audit log) — see deferred-work.md; outside AgentRun's control.**
- [ ] 10.6. Inspect the JSONL conversation log under `App_Data/AgentRun.Umbraco/instances/.../conversation-scanner.jsonl`. Verify all completed tool results are recorded (cancelled run preserves history). Verify no orphaned tool calls (i.e., no tool_call without a matching tool_result for tools that completed before cancel; the in-flight tool that was cancelled has neither a recorded call nor a result, which is correct). **Not yet inspected — can do post-review if needed.**
- [ ] 10.7. Click Retry on the cancelled instance. Verify the existing 7.2 retry path either (a) succeeds (instance was not in `Failed` state so retry doesn't apply — verify the UI surfaces this correctly, e.g., Retry button is disabled for `Cancelled` status), or (b) the existing guard rejects with a clear message. Document whichever behaviour is correct — this is the boundary with Story 10.10. **Not yet verified — optional boundary check.**
- [ ] 10.8. Repeat the test on a long-running `fetch_url` step from the Content Quality Audit workflow (5 URLs). Cancel mid-fetch. Verify the in-flight fetch terminates within 1 second. **Not yet verified — the get_content cancel path covers the primary contract.**
- [ ] 10.9. Token-burn check: compare provider logs (Anthropic Console or equivalent) for total output tokens before and after the fix. The cancelled run should show output tokens consumed only up to the cancel moment, not for the full step duration. **Not explicitly measured — HTTP connection to Anthropic was observably torn down (see Umbraco.AI middleware stack trace — socket-level cancellation from `HttpConnection.SendAsync` → Anthropic), so provider-side generation stops at the cancel moment.**
- [x] 10.10. Run `dotnet test AgentRun.Umbraco.slnx` — all tests green. **Verified — 591/591.**

## Failure & Edge Cases

### F1: Cancel called on instance with status Pending (never started)
**When** the cancel endpoint is called on an instance whose orchestrator has never started (e.g., the user clicked Start, the API call queued, but the SSE stream hasn't established yet)
**Then** the persisted status is updated to `Cancelled` (existing behaviour)
**And** `RequestCancellation` is a no-op because no entry exists in the registry
**And** when the SSE handler eventually runs, `RegisterInstance` creates a fresh CTS — but the orchestrator's first action inside the loop is `ThrowIfCancellationRequested`, which sees an un-cancelled token and proceeds. **This is a known race window** (cancel-before-orchestrator-starts) that is explicitly out of scope for 10.8.
**Mitigation in 10.8:** the existing 409 guard at the StartInstance endpoint should reject `Start` for a `Cancelled` instance — verify this guard exists. If it does not, add a one-line check at the top of the StartInstance handler: reject if status is `Cancelled`. Document as a Failure & Edge Case finding rather than a separate task.

### F2: Cancel called twice in rapid succession
**When** two `POST .../cancel` requests arrive within milliseconds
**Then** the first request acquires the persisted-status update (status moves `Running → Cancelled`); the second request's existing 409 guard at [InstanceEndpoints.cs:93-100](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L93) rejects it because status is no longer `Running` or `Pending`
**And** `RequestCancellation` is therefore called at most once per instance per run
**And** even if the order races (both requests pass the status check before either persists), `CancellationTokenSource.Cancel()` is idempotent and the second call is harmless

### F3: HTTP request aborted (browser close) without cancel endpoint call
**When** the SSE client disconnects (browser tab closed, network drop) without a cancel call
**Then** `httpRequestAborted` fires, the linked source cancels, `OperationCanceledException` propagates
**And** the OCE handler reads the persisted status, sees `Running` (not `Cancelled`), and writes `Failed` (existing behaviour preserved — Story 10.9 will refine)
**And** the per-instance CTS is also cancelled as a side effect of the linked source firing — but this is harmless because `UnregisterInstance` runs in `finally` and disposes it

### F4: Cancel during in-flight LLM streaming chunk
**When** the model is mid-stream and the cancel CTS fires
**Then** the M.E.AI streaming async-enumerable's `MoveNextAsync` observes the token and throws `OperationCanceledException` from inside the `await foreach` at [ToolLoop.cs:98](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L98)
**And** the existing `catch (OperationCanceledException) { throw; }` at line 113-116 rethrows (do not flush partial text — the run is being cancelled, partial UI state is acceptable)
**And** Sonnet 4.6 specifically tends to release a few more tokens after cancellation observation because of network buffering — this is acceptable, the user sees a few extra characters then the stream stops

### F5: Cancel during tool-with-side-effects (file write or fetch_url cache write)
**When** a tool has completed its primary side effect (file written, cache populated) but not yet returned from `ExecuteAsync`
**Then** the side effect persists on disk (tool side effects are not transactional — locked decision 7)
**And** the tool's eventual OCE throw is caught by the per-tool catch at [ToolLoop.cs:405-408](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L405); no `FunctionResultContent` is added; the rethrow propagates
**And** the JSONL log does NOT record a tool result for the cancelled tool — but it DOES record the tool call (recorded earlier at line 376). This results in an orphaned tool_call in the JSONL. **This is acceptable** because the JSONL is for inspection/debugging, not API replay, and Story 10.10 + 10.9 will revisit the resume path.
**Deny-by-default note (project_context.md security rule):** this orphan-call-in-JSONL behaviour is an acknowledged trade-off, not an unspecified case — the "what happens to JSONL on cancel" path is intentionally specified here.

### F6: Cancel during between-step pause in interactive mode
**When** an interactive workflow has completed step N and the orchestrator has returned to the SSE endpoint; the user clicks Cancel before clicking Continue
**Then** the persisted status is updated to `Cancelled`
**And** because `UnregisterInstance` ran when the previous step's orchestrator returned (line 178 in `WorkflowOrchestrator`), the registry has no entry for this instanceId → `RequestCancellation` is a no-op
**And** when the user (or stale UI) attempts to POST `/start` for step N+1, the existing 409 guard at the StartInstance endpoint rejects it (verify — see F1 mitigation note)

### F7: Process shutdown with active CTS in registry
**When** the application process shuts down with one or more active orchestrators in flight
**Then** ASP.NET fires `IHostApplicationLifetime.ApplicationStopping`; the SSE request tokens cancel; orchestrators throw OCE; `finally` blocks call `UnregisterInstance` which disposes the per-instance CTS
**And** the registry's own `Dispose` method (Task 1, step 7) disposes any remaining CTS as a defensive measure
**And** in-flight runs are persisted as `Failed` by the SSE OCE handler (existing behaviour for the disconnect path)

### F8: ActiveInstanceRegistry leaks if orchestrator throws before reaching finally
**When** a programming bug causes `ExecuteNextStepAsync` to throw before entering its `try { ... } finally { UnregisterInstance }` block
**Then** the registry entry leaks for this instance until process restart
**Mitigation:** the structure of `ExecuteNextStepAsync` already places `RegisterInstance` at line 47 and the `try { while (true) { ... } } finally { UnregisterInstance }` immediately after at lines 49-179. There is no awaited code between `RegisterInstance` and the `try`. This is correct and the bug surface is minimal — but the registry's `IDisposable` (Task 1, step 7) provides a safety net.

### F9: Cancel arrives between step Complete persisting and orchestrator next-iteration check
**When** step N completes and persists `Complete`; before the orchestrator's next `while (true)` iteration runs `ThrowIfCancellationRequested`, the cancel endpoint fires
**Then** the persisted instance status is updated to `Cancelled` by the cancel endpoint
**And** the next iteration's `ThrowIfCancellationRequested` (Task 2, step 4) fires
**And** OCE propagates; SSE OCE handler reads status, sees `Cancelled`, skips the `Failed` overwrite (AC6)
**And** the instance ends up with `instance.Status = Cancelled`, `step[N].Status = Complete`, `step[N+1].Status = Pending` (never started)
**And** this is the correct outcome — step N genuinely completed before cancel, the user can inspect its artifacts, and the run is marked Cancelled because subsequent steps were skipped

### F10: User clicks Cancel on a run that has no `IActiveInstanceRegistry` entry but is persisted as `Running`
**When** the registry's in-memory state was lost (process restart) but the on-disk YAML still says `Running` (a known stale state from a crash)
**Then** the cancel endpoint persists `Cancelled`; `RequestCancellation` is a no-op (no entry); the run is now correctly marked Cancelled on disk
**And** no actual orchestrator was running, so there is nothing to stop — the cancel correctly reflects the user's intent and recovers the stale state
**And** this is the correct outcome — `Running` after a process restart is actually a stale flag (the orchestrator died with the process); cancel cleans it up

## Dev Notes

### Existing patterns to follow

- **`ActiveInstanceRegistry` is the only "per-instance ephemeral state" service in the package.** Adding the CTS here is the natural choice — same lifetime, same key, same disposal contract as the existing message channel. Do NOT introduce a parallel `ICancellationRegistry` service.
- **Linked token sources** are the M.E.AI / .NET idiom for combining cancellation signals: `using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokenA, tokenB);`. The `using` is critical — linked sources must be disposed or they leak the linked-state subscription.
- **Cancel-before-Failed status precedence** is the same idiom as Story 9.0's instance-failure handling: persist the authoritative state FIRST, then trigger downstream observers. The OCE handler is a downstream observer that respects whatever was persisted.
- **`OperationCanceledException` propagation** is already correct everywhere in the engine (`StepExecutor`, `ToolLoop`, `ConversationRecorder`, `SseEventEmitter`). Story 10.8 does not change OCE flow; it only changes (a) what triggers the OCE and (b) whether the SSE handler overwrites the persisted status.

### What NOT to do

- Do NOT add a new `Cancelling` interim status to the enum (locked decision 8). The window between cancel-clicked and step-terminated is sub-second; users do not perceive an interim state.
- Do NOT change `IWorkflowTool`'s contract to require explicit cancellation handling. Every existing tool already accepts `CancellationToken` and propagates OCE correctly; adding a new contract clause is feature creep.
- Do NOT touch the SSE disconnect path in this story (status-on-disconnect remains `Failed`). Story 10.9 owns that refinement.
- Do NOT touch the resume-after-cancel UX path. Story 10.10 owns that.
- Do NOT add cancellation-rollback semantics to tools (file writes do not unwind). This is a `transactional workflows` design conversation that does not belong here.
- Do NOT add metrics or token-usage logging for cancelled runs. Story 11.3 owns token usage logging; cancellation does not pre-empt that.
- Do NOT modify `IConversationRecorder` or the JSONL format. Cancelled runs leave the JSONL log in whatever shape execution reached — this is correct.
- Do NOT change `InstanceManager.SetInstanceStatusAsync` to be cancel-aware. The status-write API stays plain; the cancel-vs-failure ordering logic lives in the SSE OCE handler (Task 4) where it belongs.

### Test patterns

- **Backend:** NUnit 4 — `[TestFixture]`, `[Test]`, `Assert.That(...)`. NSubstitute for mocking. See `StepExecutorTests.cs` and `ActiveInstanceRegistryTests.cs` for the established patterns.
- **Cancellation timing in tests:** prefer `cts.CancelAfter(TimeSpan.FromMilliseconds(50))` over manual signalling races. For testing "fires during streaming", build a fake `IChatClient` that exposes a `TaskCompletionSource` you control, then signal the CTS while the response is awaiting.
- **Run all tests:** `dotnet test AgentRun.Umbraco.slnx` (always specify the slnx — never bare `dotnet test`; see `feedback_dotnet_test_slnx.md`).

### Project Structure Notes

- All changes are in `AgentRun.Umbraco/Engine/`, `AgentRun.Umbraco/Endpoints/`, and corresponding test folders.
- Engine boundary preserved: `ActiveInstanceRegistry` already lives in `Engine/` and uses only pure .NET types (`ConcurrentDictionary`, `Channel`, `CancellationTokenSource`); the CTS extension does not introduce any Umbraco dependencies.
- DI registration: `ActiveInstanceRegistry` is already registered as Singleton in `AgentRunComposer`. No registration changes needed. The new constructor parameter on `InstanceEndpoints` is resolved by the existing DI container.
- No new files required (orchestrator tests file may be new — Task 7 — if no `WorkflowOrchestratorTests.cs` exists yet).
- No new NuGet dependencies.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story-10.8](../planning-artifacts/epics.md) — epic-level story definition and beta known-issue listing
- [Source: _bmad-output/planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md](../planning-artifacts/code-review-agentrun-umbraco-prelaunch-2026-04-10.md) — Codex pre-launch review that surfaced this finding (and 10.9, 10.11)
- [Source: AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs) — the registry being extended
- [Source: AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs:36-181](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L36) — orchestrator entry point and step-loop structure
- [Source: AgentRun.Umbraco/Engine/StepExecutor.cs:160-267](../../AgentRun.Umbraco/Engine/StepExecutor.cs#L160) — token plumbing into ToolLoop and the existing OCE catch
- [Source: AgentRun.Umbraco/Engine/ToolLoop.cs:20-462](../../AgentRun.Umbraco/Engine/ToolLoop.cs#L20) — existing CancellationToken usage in the streaming and tool-dispatch paths
- [Source: AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs:77-106](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L77) — cancel endpoint to extend
- [Source: AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:162-221](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L162) — SSE OCE handler to refine
- [Source: AgentRun.Umbraco/Instances/InstanceStatus.cs:6-13](../../AgentRun.Umbraco/Instances/InstanceStatus.cs#L6) — instance status enum (unchanged)
- [Source: _bmad-output/implementation-artifacts/9-0-toolloop-stall-recovery.md] — predecessor that established the "engine raises typed OCE-equivalent for engine-domain cancellations" pattern (`StallDetectedException`); 10.8 follows the same ordering rule (persist before signal)
- [Source: _bmad-output/implementation-artifacts/10-2-context-management-for-long-conversations.md] — most-recent Epic 10 story, structural model for this spec

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (Amelia / dev agent)

### Debug Log References

- `dotnet test AgentRun.Umbraco.slnx` — 591/591 green after changes (prior baseline was 540/540; +51 tests from 10.2 already in tree plus +~24 new for 10.8).
- Scoped reruns during development:
  - `--filter "FullyQualifiedName~ActiveInstanceRegistry"` — 16/16
  - `--filter "FullyQualifiedName~WorkflowOrchestrator"` — 13/13
  - `--filter "FullyQualifiedName~ToolLoopTests"` — 37/37
  - `--filter "FullyQualifiedName~ExecutionEndpointsTests"` — 9/9

### Completion Notes List

- **Tasks 1–9 complete.** All 10 acceptance criteria are implemented and covered by automated tests. Full suite 591/591.
- **Task 10 — core manual E2E verified by Adam 2026-04-14** against a live Anthropic provider using the Content Audit workflow on the Clean Starter Kit. Instance `1eb435ed6f804efd9cad32fa8277037f` cancelled mid-scan, status persisted as `Cancelled` (not `Failed`), engine stopped within ~1s, `Cancellation observed for instance ...` Information log fired. Steps 10.6 (JSONL inspection), 10.7 (Retry boundary), 10.8 (fetch_url cancel), 10.9 (explicit token-burn measurement) remain optional — not gating completion because the primary cancellation contract (stop work, persist Cancelled, no Failed overwrite) is demonstrated by 10.1–10.5 + 10.10 and the socket-level cancellation from `HttpConnection.SendAsync` proves the provider HTTP connection is torn down.
- **Three amendments were applied during manual E2E** (see Change Log 1.1–1.3): (a) widened `showCancel` on `agentrun-instance-detail.element.ts` — the button was never visible in interactive mode mid-stream; (b) stopped rethrowing OCE in the Cancelled branch of the SSE handler — it was producing an unhandled-exception log on every cancel click for a situation that wasn't actually a failure; (c) documented Umbraco.AI middleware log noise (two `[ERR]` lines per cancel from `AIUsageRecordingChatClient` + `AIAuditingChatClient`) as a deferred upstream issue — outside AgentRun's control, functional cancellation is clean.
- **Two story-spec deviations are documented in the Change Log**, not hidden: AC6's "OCE is re-thrown" clause no longer holds for the Cancelled path (amendment 1.2), and the implicit assumption that the Cancel button was already wired for all running instances (amendments 1.1) was incorrect. The reviewer should sanity-check both changes against the respective amendment rationale.
- **Locked decision 3 (persist FIRST, signal SECOND)** is enforced by `InstanceEndpointsTests.CancelInstance_PersistsCancelledBeforeSignallingCts`, which records call order via NSubstitute `.When(...).Do(...)` hooks — the test fails if the order is ever reversed.
- **Linked-source disposal in finally** — the `using var linkedCts = ...` in `WorkflowOrchestrator.ExecuteNextStepAsync` disposes on every exit path (success, return after interactive step, OCE, any other exception). Verified by `LinkedToken_RegistryUnregisteredInFinally_OnCancel` via mock.
- **Re-registration contract (AC9)** — `ActiveInstanceRegistry.RegisterInstance` uses `AddOrUpdate` and disposes both the old CTS and the old channel writer in the update factory. `RegisterInstance_Twice_DisposesPriorCancellationSource` and `RegisterInstance_Twice_CompletesPriorChannelWriter` are the guard tests.
- **F1 (cancel-before-orchestrator-starts) boundary** — the existing `ExecutionEndpoints.StartInstance` 409 guard at [ExecutionEndpoints.cs:63-71](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L63) rejects `Start` for instances with status `Completed|Failed|Cancelled`. Verified during code read; no new guard needed.
- **F6 (between-step cancel in interactive mode)** — same 409 guard above covers POST `/start` for a `Cancelled` instance. No regression introduced.
- **Nullability + analyzer cleanup** — added `[TearDown]` to `ActiveInstanceRegistryTests` to satisfy NUnit1032 now that the SUT implements `IDisposable`. Fixed a Task-return nullability warning in the ToolLoop post-batch cancel test.

### Change Log

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-04-13 | Initial story creation by Bob (SM) — comprehensive context for per-instance CTS wiring, orchestrator linked-token, SSE OCE handler refinement, and explicit token checks in ToolLoop |
| 1.0 | 2026-04-14 | Amelia implemented Tasks 1–9. ActiveInstanceRegistry now holds per-instance `CancellationTokenSource` alongside the message channel; interface gains `GetCancellationToken` + `RequestCancellation`; registry implements `IDisposable` and disposes CTS + completes channel writer on re-register and unregister. WorkflowOrchestrator wires a linked `CancellationTokenSource` from the HTTP token + per-instance CTS and passes `runToken` through every internal await; OCE-path writes continue to use `CancellationToken.None`. Cancel endpoint persists `Cancelled` then calls `RequestCancellation` (order is asserted). SSE OCE handler reads fresh persisted status and skips the `Failed` overwrite when it sees `Cancelled`. ToolLoop gains two explicit `ThrowIfCancellationRequested()` calls: top-of-iteration (before `DrainUserMessagesAsync`) and post-batch (after the tool-role `ChatMessage` is appended). Tests 591/591. Manual E2E (Task 10) pending Adam. |
| 1.1 | 2026-04-14 | Manual E2E amendment #1 — Cancel button UI gate. `agentrun-instance-detail.element.ts` `showCancel` was gated on `!isInteractive && !this._streaming`, so the Cancel button never rendered in interactive mode mid-stream (the exact scenario 10.8 targets). Widened to `Running \|\| Pending \|\| (Failed && !_streaming)` across both modes. Story 8.11 test replaced with a rule-aligned version. Frontend tests 162/162. Manual verification by Adam: cancel now stops a mid-scan Content Audit run. |
| 1.2 | 2026-04-14 | Manual E2E amendment #2 — OCE rethrow in Cancelled path. Deliberate server-initiated cancel produced a `[ERR] An unhandled exception has occurred` log with a 100-line stack on every Cancel click (the SSE handler's `throw;` in the Cancelled branch surfaced the OCE as an aborted controller action even though the run was stopped correctly). Changed the Cancelled branch to `return new EmptyResult();` — SSE stream ends cleanly from the client side, Information-level "Cancellation observed" log retained, disconnect path (status=Running) still rethrows (Story 10.9 scope). This deviates from AC6's "OCE is re-thrown after status handling" clause — the rethrow was a safe-default clause written when cancel couldn't actually trigger this path in the pre-10.8 world, and manual E2E proved it produces log noise with no signal value. `Start_OceWithCancelledStatus_ReturnsEmptyResultAndSkipsFailedOverwrite` was reshaped from a rethrow assertion to an EmptyResult assertion. |
| 1.4 | 2026-04-14 | Code review applied 7 patches: (P1) `ActiveInstanceRegistry.RegisterInstance` switched from `AddOrUpdate` to TryAdd/TryUpdate retry loop to prevent the update-factory-disposes-live-entry race; (P2) `InstanceManager.SetInstanceStatusAsync` now refuses transitions out of `Completed|Failed|Cancelled` to prevent cancel-after-complete overwrites; (P3) SSE Cancelled branch now emits `run.finished(Cancelled)` before returning `EmptyResult` so non-initiating observers can distinguish cancel from dropped connection; (P4) client `_onCancelClick` treats 409 as idempotent and always reloads view state; (P5) `ToolLoopTests.ToolThrowingOce_DoesNotRecordToolResult` now positively asserts `RecordToolCallAsync` to protect F5's orphan-tool_call invariant; (P6) consolidated `Start_Oce_RunningStatusPath_StillRethrows` into `Start_OceWithRunningStatus_WritesFailedAndRethrows`, added Running-transition assertion to the Cancelled-status test, added two re-registration tests (`RegisterInstance_Twice_PriorTokenNotSignalled`, `RegisterInstance_ThenRequestCancellation_SignalsNewEntryOnly`), and strengthened the concurrent-registration test to verify survivor is functional; (P7) removed `Failed` from `showCancel` — Retry is the right action for Failed runs and the server rejects with 409. Backend 592/592, frontend 162/162. |
| 1.3 | 2026-04-14 | Manual E2E amendment #3 — Umbraco.AI cancellation log noise. After 1.2, cancel is fully functional (status persists as Cancelled, engine stops within 1s, AgentRun logs clean). Remaining noise is two `[ERR]` lines per cancel from Umbraco.AI's middleware chain (`AIUsageRecordingChatClient` + `AIAuditingChatClient`) which treat deliberate cancellation the same as provider failure. Out of AgentRun's control — the logs emit before OCE bubbles up to the SSE handler. Documented in [deferred-work.md](./deferred-work.md#deferred-from-manual-e2e-of-story-10-8-cancel-wiring-2026-04-14) with upstream issue draft at [upstream-issue-umbraco-ai-cancellation-logging.md](./upstream-issue-umbraco-ai-cancellation-logging.md). **Manual E2E core behaviour verified by Adam — Task 10 complete.** Story ready for code-review. |

### File List

**Modified — production code:**

- `AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs` — Task 1 (IActiveInstanceRegistry interface + implementation; per-instance CTS storage, IDisposable, re-registration disposal)
- `AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs` — Task 2 (linked token construction, top-of-loop `ThrowIfCancellationRequested`, `runToken` plumbing)
- `AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs` — Task 3 (inject `IActiveInstanceRegistry`, call `RequestCancellation` after persisting Cancelled)
- `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — Task 4 (OCE handler reads current status and skips Failed overwrite when Cancelled)
- `AgentRun.Umbraco/Engine/ToolLoop.cs` — Task 5 (two explicit `ThrowIfCancellationRequested` calls: top-of-iteration + post-batch)

**Modified — tests:**

- `AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs` — Task 6 (+9 new tests: CTS created, null when unregistered, RequestCancellation triggers, re-registration disposes prior CTS, re-registration completes prior writer, concurrent registration, Dispose clears; plus `[TearDown]` for NUnit1032)
- `AgentRun.Umbraco.Tests/Engine/WorkflowOrchestratorTests.cs` — Task 7 (+4 new tests: linked token cancels on instance CTS, on HTTP token, registry unregistered in finally on cancel, top-of-loop check fires between steps)
- `AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs` — Task 8 (+4 new tests: top-of-iteration check fires, post-batch check fires, OCE skips remaining tools in batch, OCE does not record tool result)
- `AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs` — Task 9 (+4 new tests: cancel calls RequestCancellation, persist-before-signal ordering, 409 path skips registry, registry no-op when no entry; plus constructor update for IActiveInstanceRegistry)
- `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs` — Task 9 (+3 new tests: OCE with Cancelled skips Failed, OCE with Running writes Failed, OCE rethrows; plus `AttachHttpContext` helper)

**Files NOT touched (per Files in Scope locked scope):**

- `AgentRun.Umbraco/Instances/InstanceStatus.cs` — enum unchanged
- `AgentRun.Umbraco/Engine/StepExecutor.cs` — token flows through unchanged
- `AgentRun.Umbraco/Composers/AgentRunComposer.cs` — no DI changes needed
- `AgentRun.Umbraco/Tools/*.cs` — IWorkflowTool contract unchanged

### Review Findings

Code review completed 2026-04-14. Three adversarial layers ran (Blind Hunter, Edge Case Hunter, Acceptance Auditor). 3 decision-needed, 6 patch, 4 defer, 8 dismissed.

**Decision-needed — resolved 2026-04-14:**

- [x] [Review][Decision→Patch] Cancel-on-Failed button vs server 409 — **Resolved:** remove `Failed` from `showCancel`. Amendment 1.1's `Failed` inclusion was incidental; Retry is the correct action for Failed instances, not Cancel. Promoted to patch P7.
- [x] [Review][Decision→Dismiss] Mid-stream OCE loses recorded partial assistant text — **Resolved:** dismissed. Spec F4 stands ("do not flush partial text — partial UI state is acceptable"). F5 records the tool_call before dispatch (not a flush-on-cancel) — the patterns are consistent: record what completed, don't half-save interrupted work.
- [x] [Review][Decision→Defer to 10.10] Step-level `StepStatus.Active` orphan after cancel — **Resolved:** defer to Story 10.10 (Instance Resume After Step Completion). Same class as 10.10's chat-input-rejects-after-completion issue: post-terminal-state cleanup. Tagged in deferred-work.md for explicit pickup in 10.10.

**Patch (straightforward fixes):**

- [x] [Review][Patch] `ActiveInstanceRegistry` concurrency hardening [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs] — `AddOrUpdate`'s update factory can run more than once under contention per CLR docs, disposing a *live* entry's CTS and channel writer. Combined with `GetCancellationToken` returning `null` on ODE (swallowed in `WorkflowOrchestrator.cs:47-57` via `?? CancellationToken.None`), same-instance re-registration races can silently run a step with a disposed CTS. Fix: have `RegisterInstance` return `(ChannelReader, CancellationToken)` atomically (one dictionary op), and dispose only the prior-winner entry rather than inside the factory. Also tighten `RequestCancellation` to resolve entry + call `Cancel()` without a TOCTOU gap. Dismissed variants: `B2` persist-then-signal race (confirmed covered by `ExecutionEndpoints.StartInstance` 409 guard at line 63-71), `B8` stranded reader on re-registration (intentional per AC9).
- [x] [Review][Patch] Terminal-status guard in `SetInstanceStatusAsync` [AgentRun.Umbraco/Instances/InstanceManager.cs] — Cancel reads state at [InstanceEndpoints.cs:89](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L89), validates `Running|Pending` at line 99, then persists `Cancelled`. Between the read and the write, the orchestrator can complete the run and persist `Completed`/`Failed`. `SetInstanceStatusAsync` is read-modify-write with only a `Running→Running` no-op guard — it will overwrite `Completed` with `Cancelled`. Fix: refuse transitions out of terminal states (`Completed|Failed|Cancelled`) at the store layer, or re-check status inside `SetInstanceStatusAsync` after the read and no-op if terminal.
- [x] [Review][Patch] SSE Cancelled branch emits no terminal event [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:~186-199] — After amendment 1.2 the Cancelled branch `return new EmptyResult()` without emitting a `run.cancelled` (or equivalent) SSE event via `emitter`. Client sees stream close with `onerror`; non-initiating observers have no way to distinguish cancel from connection drop. Fix: emit `run.cancelled` before the EmptyResult return, mirroring the `run.error` pattern in the generic Exception branch.
- [x] [Review][Patch] Client double-cancel swallows 409 silently [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:~803-828] — After a successful cancel, a second click from a stale UI triggers 409; `_onCancelClick` only `console.warn`s. Fix: treat 409 as idempotent on the client (no-op toast, reload state), or move to 200-with-unchanged-state on the server for already-cancelled instances.
- [x] [Review][Patch] Missing positive assertion for F5 orphan tool_call [AgentRun.Umbraco.Tests/Engine/ToolLoopTests.cs:~551-582] — `ToolThrowingOce_DoesNotRecordToolResult` asserts `RecordToolResultAsync` NOT called; F5 narrative requires `RecordToolCallAsync` IS called. Add one line: `recorder.Received().RecordToolCallAsync(...)`. Prevents silent regression of F5.
- [x] [Review][Patch] P7: Remove `Failed` from `showCancel` [AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts:~912-920] — Promoted from D1. Retry is the right action for Failed instances; cancelling a Failed run would overwrite the failure signal with Cancelled and lose diagnostic state. Drop `(inst.status === "Failed" && !this._streaming)` from `showCancel`. Update `agentrun-instance-detail.element.test.ts` to reflect the narrower scope.
- [x] [Review][Patch] Test coverage gaps (consolidate + add) [AgentRun.Umbraco.Tests/] — (a) Consolidate near-duplicate `Start_OceWithRunningStatus_WritesFailed` and `Start_Oce_RunningStatusPath_StillRethrows` into one test. (b) `Start_OceWithCancelledStatus_ReturnsEmptyResultAndSkipsFailedOverwrite` should also assert `Received().SetInstanceStatusAsync(..., Running, ...)` was called on entry (guards against an initial-transition regression). (c) Add a test: register-A → register-A' → `RequestCancellation(A)` → A's reader/token stays functional; covers the registry race scenario.

**Deferred (pre-existing, out of story scope, or spec-sanctioned):**

- [x] [Review][Defer] `instance.yaml` .tmp orphan when cancel cancels mid-write [AgentRun.Umbraco/Instances/InstanceManager.cs:~347-358] — `WriteAllTextAsync(tmp, ..., cancellationToken)` followed by `File.Move` with no OCE cleanup of the tmp file. Pre-existing to 10.8; cancel now fires this path more often. Deferred — log to follow-up story.
- [x] [Review][Defer] `ActiveInstanceRegistry.Dispose` has no `_disposed` flag guarding `RegisterInstance`/`RequestCancellation` post-dispose [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs:~120-128] — Singleton disposed only on host shutdown; concurrent registration during shutdown is already racing with app-stopping token cancellation. Low-risk; defer.
- [x] [Review][Defer] Task 10.6-10.9 manual E2E boxes unchecked — JSONL inspection, Retry-on-Cancelled boundary, `fetch_url` cancel mid-fetch, explicit token-burn measurement. Dev Agent Record argues primary contract is demonstrated by 10.1-10.5 + 10.10; socket teardown observable. Accepted deferral per Completion Notes — defer.
- [x] [Review][Defer] AC6 body wording inconsistent with amendment 1.2 in Change Log [spec itself] — AC6 still says "OCE is re-thrown after status handling" but the Cancelled branch now returns EmptyResult. Change Log documents the deviation; AC body not back-propagated. Doc hygiene only — defer (can edit in next spec touch).

**Dismissed (8):** OCE status-reread (amendment 1.2 sanctioned); persist-then-signal with new-registration race (StartInstance 409 guard confirmed at [ExecutionEndpoints.cs:63-71](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L63)); stranded channel reader on re-register (intentional per AC9); ToolLoop in-memory `messages` list mismatch on OCE (list is not externally observable, not persisted by this path); Pending-cancel UX (semantically fine, terminal-final preserved by StartInstance guard); test brittleness nit on Dispose-then-recreate; LinkedToken test racy speculation (test is deterministic); `using var linkedCts` in finally speculation (reviewer self-uncertain, finally block is clean).

