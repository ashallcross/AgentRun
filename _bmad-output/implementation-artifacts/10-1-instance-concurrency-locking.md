# Story 10.1: Instance Concurrency Locking

Status: done

**Depends on:** None (built on existing `InstanceManager`, `ActiveInstanceRegistry`, `ExecutionEndpoints` plumbing)
**Branch:** `feature/epic-10`
**Priority:** 6th in Epic 10 (10.2 done → 10.12 done → 10.8 done → 10.9 done → 10.10 done → **10.1** → 10.6 → 10.11 → 10.7 → 10.4 → 10.5)

> **Beta known issue — listed in epics.md §"Known Issues for Beta v1" line 1995:**
> *"Single concurrent instance per workflow. No instance-level locking exists — running two instances of the same workflow simultaneously may corrupt state. Fix planned for Story 10.1."*
> The wording in epics.md is slightly imprecise: the actual race is **per-instance** (two concurrent callers on the *same* instance), not per-workflow. Different instances of the same workflow can already run concurrently — that capability is preserved by this story. The fix is per-instance state-mutation locking and atomic orchestrator-claim semantics so the same instance cannot be operated on twice in parallel.

## Story

As a developer running AgentRun in production,
I want every state mutation on a single instance to be serialised and a second concurrent `/start` or `/retry` on the same instance to fail fast,
So that simultaneous requests don't silently overwrite each other's writes, leak orchestrator state, or stall on detection bugs that surface as data corruption days later.

## Context

**UX Mode: N/A — engine + endpoint plumbing change. No new UI work.** This story closes long-standing TOCTOU race windows in `InstanceManager` and the `/start` and `/retry` endpoints. The user-visible effect is: a double-clicked Continue/Retry/Cancel cleanly returns 409 on the loser instead of producing inconsistent state, and concurrent state writes from different code paths (cancel vs. orchestrator vs. retry) cannot interleave their reads and writes.

### The two races being fixed

This story targets two *distinct* concurrency bugs that were deferred from earlier stories. Both must be fixed; they require slightly different mechanisms.

**Race A — Read-modify-write inside `InstanceManager`** (deferred from Story 3.1 code review, [deferred-work.md:28](deferred-work.md), and reaffirmed by Story 3.2 deferral [line 34](deferred-work.md), Story 3.4 [line 52](deferred-work.md), and Story 4.5 [line 74](deferred-work.md))

`SetInstanceStatusAsync`, `UpdateStepStatusAsync`, `AdvanceStepAsync`, and `DeleteInstanceAsync` each do `ReadStateAsync(yamlPath) → mutate state object → WriteStateAtomicAsync(...)`. The `File.Move(temp, target, overwrite: true)` at [InstanceManager.cs:376](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L376) gives **file-level** atomicity (a reader sees either the old or new YAML, never a torn one), but it does **not** give **operation-level** atomicity. Two callers can both load `state v1`, both mutate independently, and the second write silently overwrites the first.

Concrete example: orchestrator finishes step 0 and calls `UpdateStepStatusAsync(stepIndex=0, Complete)`. At the same instant, the cancel endpoint calls `SetInstanceStatusAsync(Cancelled)`. Both load `state` with `Status=Running, Steps[0].Status=Active`. Cancel writes `{Status=Cancelled, Steps[0].Status=Active}`. Orchestrator writes `{Status=Running, Steps[0].Status=Complete}`. Cancel is silently lost; the user sees the run continue past their cancel.

Story 10.8 partially papered over this for the cancel path by ordering "persist Cancelled → signal CTS → orchestrator catches OCE → existing OCE handler at [ExecutionEndpoints.cs:213-310](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L213) reads status and skips the Failed overwrite". That makes the *cancel-vs-OCE-handler* race safe. It does **not** make the *cancel-vs-orchestrator-mid-step-mutation* race safe.

**Race B — Two concurrent orchestrators on the same instance** (deferred from Story 4.5, [deferred-work.md:74](deferred-work.md))

Today, the only guard against two concurrent `/start` calls on the same instance is at [ExecutionEndpoints.cs:75-86](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L75): a non-atomic two-step check that reads `instance.Status` and then reads `_activeInstanceRegistry.GetMessageWriter(id)`. Two requests can both pass this guard. They then both call `_workflowOrchestrator.ExecuteNextStepAsync`, which calls `_activeInstanceRegistry.RegisterInstance(instanceId)`. Today's `RegisterInstance` ([ActiveInstanceRegistry.cs:45-76](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs#L45)) deliberately uses a TryUpdate retry loop that **replaces** the existing entry — disposing the first orchestrator's `CancellationTokenSource` and channel, then handing the second orchestrator a fresh entry. The first orchestrator now holds a stale token (already disposed) and a stale channel writer (closed). Its next `await` on a tool, an LLM call, or a recorder write will throw `OperationCanceledException` or `ObjectDisposedException` from underneath, and the second orchestrator is now driving the same instance.

This is silent corruption: nothing in the system surfaces "two orchestrators were running on instance X for 200ms before the first crashed". The JSONL log gets interleaved writes. The instance YAML status flips back and forth. Worst case: the first orchestrator's `Complete` write for step N lands *after* the second orchestrator's `Active` write for step N+1, producing a state where `Steps[N].Status=Complete` and `CurrentStepIndex=N+1` and `Steps[N+1].Status=Active` — except `Steps[N+1]` actually wasn't started yet from the user's perspective.

Fixing Race A makes Race B harder to trigger (the in-manager `Status==Running` guard at [InstanceManager.cs:207-211](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L207) becomes reliably atomic). But Race B also needs an explicit fix at the orchestrator-claim layer: a TryClaim-or-409 step that runs *before* the orchestrator's `RegisterInstance` call, so the loser request sees a clean 409 from the endpoint instead of being silently displaced inside the registry.

### What this story does NOT directly lock — but does close

`ConversationStore.AppendAsync` and `ConversationStore.TruncateLastAssistantEntryAsync` also do read-modify-write without their own locks (see [deferred-work.md:138](deferred-work.md#L138), which explicitly notes "Per-instance SemaphoreSlim from 3-1 deferred item covers this"). This story closes that deferral **without** wrapping `ConversationStore` in the new lock, because the only concurrent-caller scenarios are now structurally unreachable:

1. Two orchestrators on the same instance writing to the same JSONL — prevented by `TryClaim` (locked decision 6 / Task 2).
2. `RetryInstance` calling `TruncateLastAssistantEntryAsync` while an orchestrator is mid-append — `RetryInstance` now acquires the claim *before* truncating ([Task 4](#task-4)), so the in-flight orchestrator has either released the claim already (terminated normally) or has been forced out by cancellation propagation. Truncate cannot race a live orchestrator.
3. The cancel endpoint never touches `ConversationStore`. Only `InstanceManager` state. No race.

Marking `ConversationStore` items as resolved-by-claim-not-by-direct-locking in Task 10. If a future story adds a code path that mutates conversation history outside the orchestrator/retry boundary, that story owns the per-instance-lock extension.

### The fix shape

1. **Per-instance `SemaphoreSlim(1, 1)` wraps every read-modify-write in `InstanceManager`.** A private `ConcurrentDictionary<string, SemaphoreSlim> _instanceLocks` lazily allocates the lock the first time an instance is mutated. `SetInstanceStatusAsync`, `UpdateStepStatusAsync`, `AdvanceStepAsync`, and `DeleteInstanceAsync` each `await sem.WaitAsync(cancellationToken)` before their `ReadStateAsync(yamlPath)` line and release in `finally`. Pure reads (`GetInstanceAsync`, `ListInstancesAsync`, `FindInstanceAsync`) stay unlocked — they tolerate seeing either the pre- or post-write YAML, and locking them would serialise the read-heavy dashboard pointlessly.
2. **Atomic orchestrator-claim primitive on `IActiveInstanceRegistry`.** A new `bool TryClaim(string instanceId)` method uses `_entries.TryAdd` (no replacement, no retry loop) and returns `true` only if no entry existed. The orchestrator's `RegisterInstance` is split: the *create-or-replace* semantics stay (under a different name, e.g., `RegisterInstanceForceReplace`) for callers that legitimately need them (none today, but the disposal-on-replace logic is non-trivial and worth preserving as a private fallback); the *atomic-claim-or-fail* semantics become the default path used by orchestrator-bound entries.
3. **Endpoint claim-and-release flow.** `StartInstance` and `RetryInstance` call `_activeInstanceRegistry.TryClaim(id)` immediately after their pre-execution status guards. On `false` they return `409 already_running`. On `true` they proceed into `ExecuteSseAsync`, which delegates to the orchestrator. The orchestrator's existing `RegisterInstance` call is updated to *attach* the channel + CTS to the already-claimed entry rather than create a new one. `UnregisterInstance` releases the claim (existing behaviour).
4. **Lock dictionary is never pruned.** For beta scope, the lock dict grows monotonically with the set of instances ever touched in the process lifetime (bounded by total instance count, small for beta — single-digit thousands maximum on a busy beta site). Pruning under contention is a documented v2 concern; over-engineering the eviction path now adds complexity the beta does not need. Locks are tiny (≈ 96 bytes each); 10k instances is < 1 MB.
5. **Lock release survives cancellation and exceptions.** Every `await sem.WaitAsync(ct)` is paired with `try { ... } finally { sem.Release(); }`. `WaitAsync(ct)` itself throws `OperationCanceledException` if the token fires *before* the wait succeeds — in that case the finally must NOT release (the lock was never acquired). Standard `try/finally` placement after `await sem.WaitAsync(ct)` (not before) handles this correctly.

### Architect's locked decisions (do not relitigate)

1. **Per-instance lock, not per-workflow or global.** Concurrent runs of *different* instances must continue to work — the dashboard runs N instances in parallel today and that capability is the entire reason interactive mode is usable. The lock key is `instanceId` (globally unique 32-hex GUID per [InstanceManager.cs:351](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L351)), not `(workflowAlias, instanceId)` — keying by instanceId alone is sufficient and avoids a tuple allocation per lookup.
2. **Lock lives inside `InstanceManager`, not in `ActiveInstanceRegistry`.** The registry holds *only* entries for orchestrator-active instances. The mutation lock must be available even for instances that are NOT orchestrator-active (e.g., cancel-on-pending, delete-on-completed). Co-locating the lock dict with the mutating code keeps the lock acquire/release adjacent to the mutation site and avoids a cross-service dependency. `ActiveInstanceRegistry` gets only the new `TryClaim` primitive; it does not become a generic per-instance lock holder.
3. **Block-and-wait at the inner mutation layer; fail-fast (409) at the outer endpoint layer.** Inner `SetInstanceStatusAsync` etc. block until the lock is free — these are millisecond critical sections, blocking is fine, and blocking lets cancel briefly wait for an in-flight mutation to finish rather than racing it. Outer `/start` and `/retry` use `TryClaim` and 409 immediately — these are user actions where queueing two concurrent step-executions makes no semantic sense; fail-fast gives the user a clean error to react to. **This split was intentional and is not equivalent to a single uniform mechanism — do not collapse them.**
4. **`SetInstanceStatusAsync`'s in-manager `Status==Running` guard at [InstanceManager.cs:207-211](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L207) stays.** Once the lock wraps the read-modify-write, the guard is reliably atomic and serves as the inner backstop if `TryClaim` is somehow bypassed (e.g., a future code path that calls `SetInstanceStatusAsync(Running)` outside the endpoint layer). Defence-in-depth.
5. **`StartInstance` must catch the in-manager `InvalidOperationException("...already running...")` and map to 409 (mirror `RetryInstance`).** Today only `RetryInstance` does this ([ExecutionEndpoints.cs:184-196](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L184)); `StartInstance` does not. Once locking serialises the writes, the race is closed at the outer layer (`TryClaim`), but the inner guard can still fire if a code path bypasses the claim or if the existing line 75-86 status check passes and then gets overtaken in the brief window before `TryClaim` runs. Adding the catch in `StartInstance` mirrors `RetryInstance` and removes the residual 500 risk.
6. **`TryClaim` does NOT create the channel or CTS.** The channel and CTS are still created lazily by the orchestrator via the existing `RegisterInstance` (renamed/refactored — see Task 2). `TryClaim` only inserts a sentinel `InstanceEntry` (channel and CTS may be null, or constructed eagerly — Task 2 picks). The reason for splitting: the endpoint must claim *before* the orchestrator runs (so the loser fails fast), but the orchestrator owns the lifecycle of the channel and CTS (so test isolation and disposal work as today). The endpoint is allowed to claim and then for the orchestrator method to populate the entry shortly after.
7. **Reads stay unlocked.** `GetInstanceAsync`, `ListInstancesAsync`, `FindInstanceAsync` do not acquire the lock. The dashboard polls these heavily — locking would serialise read traffic against write traffic for no correctness gain (the file-level `File.Move` atomicity is sufficient for read consistency). If a read returns slightly stale state, the next poll picks up the update.
8. **No new persisted enum value, no instance YAML schema change.** The lock is purely in-memory. On process restart, the lock dict is fresh-empty and locks are re-created lazily. There is no on-disk concurrency state.
9. **No distributed locking, no file-system locks, no SQL locks.** This is in-process only, matching the existing singleton-service architecture. Multi-process / multi-node coordination is explicitly deferred (out of scope, no v1 deployment uses multiple AgentRun nodes). If a future Pro tier needs it, that's Epic 12 (Pro Foundation) territory.
10. **`Channel<string>` and `CancellationTokenSource` keep their existing disposal contract.** Story 10.8's careful re-registration disposal logic at [ActiveInstanceRegistry.cs:54-76](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs#L54) is preserved as the fallback path used by `RegisterInstanceForceReplace` (the renamed existing method). The new `TryClaim` path never triggers replacement because it `TryAdd`-only. Re-registration safety still matters for the legacy/defensive path.

## Acceptance Criteria (BDD)

### AC1: `SetInstanceStatusAsync` serialises concurrent writers on the same instance

**Given** an instance with persisted state `{Status: Running, UpdatedAt: T0}` and two callers about to mutate it
**When** caller A invokes `SetInstanceStatusAsync(..., Cancelled, ct)` and caller B invokes `SetInstanceStatusAsync(..., Failed, ct)` on the same instance, both starting before either completes
**Then** the two operations execute serially (one fully completes its read-mutate-write before the other begins its read)
**And** the final persisted state reflects exactly one of the two writes (no torn or interleaved state)
**And** the caller whose write went second observes the *first* caller's state as `state.Status` when its `ReadStateAsync` runs (so the existing terminal-status guard at [InstanceManager.cs:222-230](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L222) correctly refuses sideways terminal transitions)
**And** both callers' `await` return cleanly (no exception from contention itself)

### AC2: `UpdateStepStatusAsync` serialises concurrent writers on the same instance

**Given** an instance with two callers about to mutate different steps (e.g., orchestrator marking step 0 Complete and a hypothetical concurrent caller marking step 1 Active)
**When** both `UpdateStepStatusAsync` calls run concurrently
**Then** the two operations execute serially against the same per-instance lock
**And** the final persisted state contains both mutations (no lost write — the second caller reads the first caller's already-written `Steps[0].Status=Complete` and writes `{Steps[0].Status=Complete, Steps[1].Status=Active}`)
**And** if both callers attempt to mutate the *same* step, the final state reflects exactly one of the two writes (the second caller's write wins because it ran second; this is correct semantics — the caller is responsible for its own intent)

### AC3: `AdvanceStepAsync` and `DeleteInstanceAsync` are also serialised

**Given** the same per-instance lock used by `SetInstanceStatusAsync` and `UpdateStepStatusAsync`
**When** `AdvanceStepAsync` or `DeleteInstanceAsync` is called
**Then** the call acquires the same lock before its read-mutate-write
**And** the lock is released in `finally` even if the mutation throws (e.g., `DeleteInstanceAsync` throwing on a non-terminal status, or `AdvanceStepAsync` throwing on the last-step guard at [InstanceManager.cs:330-334](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L330))

### AC4: Pure reads do not acquire the lock

**Given** a long-running write is in-flight on instance X (lock held)
**When** `GetInstanceAsync(X)`, `ListInstancesAsync()`, or `FindInstanceAsync(X)` is called
**Then** the read returns immediately without waiting for the write
**And** the read returns either the pre-write or post-write YAML (depending on whether `File.Move` has fired) — never a torn read
**And** the dashboard's poll loop does not block on instances that happen to be mid-write

### AC5: Lock acquisition respects the cancellation token

**Given** `await sem.WaitAsync(cancellationToken)` is in progress
**When** the cancellation token is signalled before the lock is acquired
**Then** `WaitAsync` throws `OperationCanceledException`
**And** the `finally` block does NOT call `Release` (the lock was never held)
**And** the OCE propagates to the caller

### AC6: Lock is released even when the mutation throws

**Given** any of the locked write methods is in-flight and the lock has been acquired
**When** the mutation throws (read failure, validation failure, write failure, or any other exception)
**Then** the `finally` block calls `sem.Release()` exactly once
**And** subsequent waiters on the same instance acquire the lock without deadlock
**And** a unit test verifies this by injecting a failing write and asserting the next call on the same instance proceeds

### AC7: Locks are per-instance, not global

**Given** instance A and instance B
**When** a write on A is holding A's lock and a write on B begins
**Then** B's write proceeds without blocking on A
**And** a unit test asserts this by holding A's lock with a delayed task and verifying B's write completes within a tight bound (e.g., < 100ms while A is held for 5s)

### AC8: `IActiveInstanceRegistry.TryClaim` atomically acquires an orchestrator slot

**Given** no entry exists for instance X
**When** two concurrent callers each invoke `TryClaim(X)`
**Then** exactly one returns `true` and the other returns `false`
**And** the winner's subsequent `RegisterInstance` (or equivalent attach call — see Task 2 for shape) succeeds and creates the channel and CTS for that entry
**And** the loser does not create or replace any entry

### AC9: `StartInstance` returns 409 when `TryClaim` fails

**Given** instance X has been claimed by an in-flight orchestrator
**When** a second `POST /start` request arrives for X
**Then** the second request's `TryClaim(X)` returns `false`
**And** the endpoint returns `409 Conflict` with `{Error: "already_running", Message: "Instance '...' is already running. Concurrent execution is not permitted."}`
**And** the existing pre-claim guards at [ExecutionEndpoints.cs:53-86](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L53) (status check, provider check) still run first; the claim is the final atomic gate

### AC10: `RetryInstance` returns 409 when `TryClaim` fails

**Given** instance X has been claimed by an in-flight orchestrator
**When** a `POST /retry` request arrives for X
**Then** the request's `TryClaim(X)` returns `false`
**And** the endpoint returns `409 Conflict` with the same `already_running` error shape as `StartInstance`
**And** the existing pre-claim guards at [ExecutionEndpoints.cs:115-145](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L115) (status check, message-writer check) still run first; `TryClaim` replaces the message-writer check (which becomes redundant once the claim is the source of truth, but stays as a defensive backstop — see Task 4)

### AC11: `StartInstance` maps in-manager `InvalidOperationException("...already running...")` to 409

**Given** the in-manager `Status==Running` guard at [InstanceManager.cs:207-211](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L207) fires inside `StartInstance` (e.g., because the pre-claim status check passed but a concurrent retry won the race in the brief window before claim)
**When** `SetInstanceStatusAsync(..., Running, ...)` throws `InvalidOperationException` containing `"already running"`
**Then** the catch block (mirror of [ExecutionEndpoints.cs:184-196](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L184) in `RetryInstance`) returns `409 Conflict` with the `already_running` error shape
**And** if the claim was acquired before the exception fires, the catch block releases it via `UnregisterInstance` to avoid leaking the slot

### AC12: Cancel endpoint briefly waits for in-flight mutation but does not wait for the orchestrator

**Given** the orchestrator is mid-step and is currently inside a `SetInstanceStatusAsync` call (lock held) — a millisecond window
**When** the cancel endpoint calls `SetInstanceStatusAsync(..., Cancelled, ...)`
**Then** the cancel call blocks on the lock until the orchestrator's mutation completes (sub-second)
**And** the cancel call then runs its own mutation, observes the orchestrator-just-written state, applies the existing terminal-status guard correctly, and writes `Cancelled`
**And** the cancel does NOT wait for the orchestrator's full step duration (the orchestrator releases the lock between mutations, not at the end of the step)

### AC13: Lock dictionary is never pruned in v1

**Given** the per-instance lock dictionary
**When** an instance reaches a terminal status (Completed/Failed/Cancelled)
**Then** the lock entry is NOT removed from the dictionary
**And** subsequent `DeleteInstanceAsync` or status-rewrite (e.g., a re-create with the same instance ID — currently impossible, GUIDs don't recur — but defensively) finds the existing lock and reuses it
**And** a comment in the code explicitly documents this v2 deferral with a pointer back to this AC

### AC14: Existing Story 10.8/10.9/10.10 behaviour is preserved

**Given** the cancel endpoint flow from Story 10.8, the SSE OCE handler from Story 10.9 (Interrupted-status branch and terminal-guard narrowing), and the Active-step-cleanup loop from Story 10.10
**When** any of those flows runs after this story is merged
**Then** all three flows still behave as their respective story specs assert (no regression)
**And** the test suites for those stories (`InstanceManagerTests`, `ExecutionEndpointsTests`, `InstanceEndpointsTests`) all still pass without modification — except where Task 5 explicitly adds new concurrency tests

## Files in Scope

| File | Change |
|---|---|
| [AgentRun.Umbraco/Instances/InstanceManager.cs](../../AgentRun.Umbraco/Instances/InstanceManager.cs) | Add `private readonly ConcurrentDictionary<string, SemaphoreSlim> _instanceLocks = new();` field. Add a private `GetOrCreateInstanceLock(string instanceId)` helper that calls `_instanceLocks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1))`. Wrap the body of `SetInstanceStatusAsync`, `UpdateStepStatusAsync`, `AdvanceStepAsync`, and `DeleteInstanceAsync` in `await sem.WaitAsync(ct); try { ... } finally { sem.Release(); }`. The wrap must start *after* `GetInstanceDirectory` and `Path.Combine` (those are pure-functional and don't need the lock) and *before* `ReadStateAsync`. The existing in-method invariants (terminal-status guard, step-index range check, etc.) stay inside the lock. |
| [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs) | Add `bool TryClaim(string instanceId)` to `IActiveInstanceRegistry` and implement on `ActiveInstanceRegistry`. Implementation: `return _entries.TryAdd(instanceId, sentinelEntry)` where `sentinelEntry` is constructed with a fresh `Channel<string>` and `CancellationTokenSource` (eagerly — see Task 2 for why eager is simpler than lazy here). The existing `RegisterInstance` keeps its replace-on-collision semantics for backward compatibility (rename to `RegisterInstanceForceReplace` if a search reveals no callers other than the orchestrator OR keep the name and add a new `AttachOrClaim` method; Task 2 picks the cleanest shape). The orchestrator's call site changes to attach to the already-claimed entry rather than create one. |
| [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs) | In `StartInstance`, after the existing status check at lines 75-86, call `if (!_activeInstanceRegistry.TryClaim(id)) return Conflict(...);` before the `SetInstanceStatusAsync(..., Running, ...)` call at line 102. Wrap the subsequent code (SetInstanceStatus + ExecuteSseAsync) in `try { ... } catch (Exception) when (...) { _activeInstanceRegistry.UnregisterInstance(id); throw; }` — actually use a more targeted finally: if `ExecuteSseAsync` returns or throws *before* the orchestrator's `RegisterInstance` runs, the claim must be released; if it returns *after*, the orchestrator's `finally` already calls `UnregisterInstance`. See Task 3 for the exact shape — there is a subtle hand-off ownership boundary. Also: add a `catch (InvalidOperationException ex) when (ex.Message.Contains("already running")) { ... return Conflict(...); }` block around the `SetInstanceStatusAsync(..., Running, ...)` call (mirror of [RetryInstance lines 184-196](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L184)). In `RetryInstance`, replace the `_activeInstanceRegistry.GetMessageWriter(id) is not null` check at lines 138-145 with a `TryClaim` call (mapping `false` to the same 409 already produced today). Keep the existing in-manager-exception catch at lines 184-196 unchanged. |
| [AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) | Change `_activeInstanceRegistry.RegisterInstance(instanceId)` at line 47 to `AttachOrClaim` (or whatever Task 2 names it) — a method that succeeds if the entry already exists from a `TryClaim` call (returns the existing channel reader) AND succeeds if no entry exists (creates one and returns the reader). The orchestrator should still work in test scenarios where it's invoked directly without a prior `TryClaim` (so it must support both paths). The `finally`'s `UnregisterInstance` call stays unchanged — it releases whichever path created the entry. |
| [AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs](../../AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs) | Add tests for: (a) two concurrent `SetInstanceStatusAsync` calls serialise (no torn write); (b) two concurrent `UpdateStepStatusAsync` calls on different steps both land in final state; (c) per-instance scope — concurrent writes on instance A and instance B do not block each other; (d) lock released on exception (subsequent call proceeds); (e) `WaitAsync` cancellation does not release the lock spuriously. Use `Task.WhenAll` with `Task.Delay` injection or a `TaskCompletionSource` barrier to deterministically interleave the operations. Note: existing modified file in `git status` — Adam may already have partial work here; reconcile cleanly. |
| [AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs](../../AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs) | Add tests for: (a) `TryClaim` returns `true` when no entry exists, `false` when one does; (b) two concurrent `TryClaim` calls — exactly one returns `true`; (c) `UnregisterInstance` releases the claim so a subsequent `TryClaim` succeeds; (d) `AttachOrClaim` (orchestrator path) attaches to an existing claimed entry without re-creating the channel/CTS; (e) `AttachOrClaim` creates a fresh entry when called without a prior `TryClaim` (test isolation path); (f) re-registration via the legacy `RegisterInstanceForceReplace` path still disposes the prior CTS (regression test for Story 10.8 behaviour). |
| [AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs) | Add tests for: (a) `StartInstance` returns 409 when `TryClaim` returns `false`; (b) `StartInstance` returns 409 when `SetInstanceStatusAsync` throws `InvalidOperationException("...already running...")` and releases the claim before returning (verify `UnregisterInstance` is called via mock); (c) `RetryInstance` returns 409 when `TryClaim` returns `false`; (d) successful `StartInstance` and `RetryInstance` paths still call into the orchestrator (regression). Note: existing modified file in `git status` — reconcile cleanly. |
| [AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs](../../AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs) | Add a test verifying the cancel endpoint's `SetInstanceStatusAsync(Cancelled)` call serialises against a concurrent orchestrator `SetInstanceStatusAsync(Running)` — i.e., the cancel observes the orchestrator's just-written state and the existing terminal-status guard at [InstanceManager.cs:222-230](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L222) does the right thing (no overwrite of a freshly-written terminal). Note: existing modified file in `git status` — reconcile cleanly. |

**Files explicitly NOT touched:**

- [AgentRun.Umbraco/Instances/InstanceStatus.cs](../../AgentRun.Umbraco/Instances/InstanceStatus.cs) — enum is correct as-is, no new value (locked decision 8)
- [AgentRun.Umbraco/Instances/StepStatus.cs](../../AgentRun.Umbraco/Instances/StepStatus.cs) — enum is correct as-is
- [AgentRun.Umbraco/Engine/StepExecutor.cs](../../AgentRun.Umbraco/Engine/StepExecutor.cs) — accepts `CancellationToken` already; no concurrency surface change
- [AgentRun.Umbraco/Engine/ToolLoop.cs](../../AgentRun.Umbraco/Engine/ToolLoop.cs) — same; the linked token from Story 10.8 already covers cancel propagation
- [AgentRun.Umbraco/Instances/ConversationStore.cs](../../AgentRun.Umbraco/Instances/ConversationStore.cs) — JSONL `AppendAsync` and `TruncateLastAssistantEntryAsync` deliberately stay unlocked. Concurrent-caller scenarios are structurally unreachable post-`TryClaim` (see "What this story does NOT directly lock" above). Wrapping these in the new lock would also serialise the orchestrator's ToolLoop appends against state mutations for no correctness gain.
- Frontend `Client/src/components/agentrun-instance-detail.element.ts` — UX gates from Story 10.10 (`shouldShowContinueButton`, `computeChatInputGate`) already prevent accidental double-clicks on the happy path; this story's backend serialisation handles the malicious/buggy-client path
- [AgentRun.Umbraco/Composers/AgentRunComposer.cs](../../AgentRun.Umbraco/Composers/AgentRunComposer.cs) — no DI changes (no new services; the lock dict is private to `InstanceManager`)
- The architecture doc (`_bmad-output/planning-artifacts/architecture.md`) — the doc predates Epic 10's concurrency hardening; this story's locked decisions live in this spec file rather than amending the v1 architecture doc retroactively

## Tasks

### Task 1: Add per-instance `SemaphoreSlim` to `InstanceManager` (AC1, AC2, AC3, AC4, AC5, AC6, AC7, AC13)

- [x] Wrap the four read-modify-write methods in a per-instance lock.

1. Add `using System.Collections.Concurrent;` at the top of `InstanceManager.cs` (already present? verify).
2. Add private field: `private readonly ConcurrentDictionary<string, SemaphoreSlim> _instanceLocks = new();`
3. Add private helper: `private SemaphoreSlim GetOrCreateInstanceLock(string instanceId) => _instanceLocks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1));`
4. Add a single explanatory comment on the field documenting the v2 deferral (locks are never pruned; size is bounded by total instance count; pruning under contention is a v2 concern). Reference AC13 by AC number.
5. Wrap `SetInstanceStatusAsync` body: acquire lock immediately after `var instanceDir = GetInstanceDirectory(...)` and `var yamlPath = Path.Combine(...)` (these are pure-functional). Place the `await sem.WaitAsync(cancellationToken)` *before* the `try`, with the `try { ... } finally { sem.Release(); }` covering everything from `ReadStateAsync` through the `return state;`.
6. Repeat for `UpdateStepStatusAsync`, `AdvanceStepAsync`, `DeleteInstanceAsync` — same shape.
7. Verify `CreateInstanceAsync` does NOT need the lock (instance ID is freshly generated; no concurrency on a brand-new GUID). Add a one-line comment confirming this so a future reader doesn't add the lock defensively.
8. Verify `GetInstanceAsync`, `ListInstancesAsync`, `FindInstanceAsync` do NOT acquire the lock (per AC4 and locked decision 7). Add a one-line comment on each explaining why (file-level `File.Move` atomicity is sufficient for read consistency; locking would serialise dashboard polling pointlessly).
9. The lock acquisition pattern is the standard:
   ```csharp
   var sem = GetOrCreateInstanceLock(instanceId);
   await sem.WaitAsync(cancellationToken);
   try
   {
       // existing read-modify-write body
   }
   finally
   {
       sem.Release();
   }
   ```
   Crucially, place `await sem.WaitAsync(ct)` *before* `try`, NOT inside it. If `WaitAsync` throws OCE, the lock was never acquired and `Release()` would throw `SemaphoreFullException`.

### Task 2: Add `TryClaim` to `IActiveInstanceRegistry` (AC8, AC14)

- [x] Extend the registry interface and implementation with an atomic claim primitive that the endpoints can call before invoking the orchestrator.

1. Add to `IActiveInstanceRegistry`:
   ```csharp
   /// <summary>
   /// Atomically claims an orchestrator slot for the given instance.
   /// Returns true if the slot was free and is now claimed; false if another
   /// orchestrator already holds the slot. The claim is released by
   /// UnregisterInstance, which is called by the orchestrator's finally block.
   /// On a successful claim, the registry creates a fresh Channel and
   /// CancellationTokenSource so the channel reader and cancellation token
   /// are immediately available to subsequent GetMessageReader / GetCancellationToken
   /// calls — the orchestrator does not need to call RegisterInstance again
   /// (see AttachOrClaim for the orchestrator-path entry point).
   /// </summary>
   bool TryClaim(string instanceId);
   ```
2. Implement on `ActiveInstanceRegistry`:
   ```csharp
   public bool TryClaim(string instanceId)
   {
       var newEntry = new InstanceEntry(
           Channel.CreateUnbounded<string>(),
           new CancellationTokenSource());
       if (_entries.TryAdd(instanceId, newEntry))
       {
           return true;
       }
       // Lost the race — discard the throwaway entry to avoid leaking the CTS.
       DisposeEntry(newEntry);
       return false;
   }
   ```
3. Decide on the orchestrator entry-point shape. Two clean options:
   - **Option A (recommended):** Add a new `ChannelReader<string> AttachOrClaim(string instanceId)` method that uses `_entries.GetOrAdd` to either return the channel reader from an existing claim or create-and-add a fresh entry (orchestrator-direct invocation path, e.g., in unit tests that don't go through the endpoint). Update `WorkflowOrchestrator.ExecuteNextStepAsync` line 47 to call `AttachOrClaim` instead of `RegisterInstance`. Keep the existing `RegisterInstance` method exactly as it is today (with its replace-on-collision semantics) for any callers that need it — search for callers; if there are none other than the orchestrator, mark the existing method as `[Obsolete("Use AttachOrClaim or TryClaim. Replace-on-collision semantics retained for compatibility — do not introduce new callers.")]` and re-route the orchestrator off it. **Do not delete the method** — its disposal logic (Story 10.8 architect lock) is the correct behaviour for the legacy "force replace" path and is non-trivial to recover if we need it back.
   - **Option B:** Make `RegisterInstance` itself idempotent — if an entry exists (from a `TryClaim`), return its channel reader; otherwise create one. Risk: this collapses two distinct intents (claim vs. force-replace) into one method, and the existing tests that exercise the force-replace path will have ambiguous semantics.
   Pick Option A. Document the choice in the spec change-log if reviewer asks.
4. The `DisposeEntry` helper at [ActiveInstanceRegistry.cs:132-143](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs#L132) is reused as-is. Verify it remains private (no callers outside the file).

### Task 3: Update `StartInstance` to claim atomically and return 409 on contention (AC9, AC11)

- [x] Modify `StartInstance` in `ExecutionEndpoints.cs` to call `TryClaim` after the pre-existing status check and to map the in-manager `InvalidOperationException` to 409.

1. After the existing `if (instance.Status == InstanceStatus.Running) { ... }` block (lines 75-86), add:
   ```csharp
   // Atomic orchestrator-slot claim. The non-atomic status + writer-presence
   // check above is a fast-path UX hint; this is the source-of-truth race gate.
   if (!_activeInstanceRegistry.TryClaim(id))
   {
       return Conflict(new ErrorResponse
       {
           Error = "already_running",
           Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
       });
   }
   ```
2. Wrap the subsequent `SetInstanceStatusAsync(..., Running, ...)` and `ExecuteSseAsync` calls so the claim is released if anything throws *before* the orchestrator's normal lifecycle takes ownership. The orchestrator's `finally` at [WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs) (the `UnregisterInstance` call) handles the release on the happy path. The endpoint must release the claim only if `ExecuteSseAsync` does not reach the orchestrator. Concretely:
   ```csharp
   try
   {
       // existing SetInstanceStatusAsync(..., Running, ...) call, with new exception mapping:
       try
       {
           await _instanceManager.SetInstanceStatusAsync(...);
       }
       catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))
       {
           // Unwind: the in-manager guard fired despite our TryClaim winning,
           // which is only possible if a concurrent retry mutated the persisted
           // status to Running between our pre-claim status read and now. Release
           // the claim and 409.
           _activeInstanceRegistry.UnregisterInstance(id);
           return Conflict(new ErrorResponse
           {
               Error = "already_running",
               Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
           });
       }

       return await ExecuteSseAsync(instance, cancellationToken);
   }
   catch
   {
       // Anything else thrown before the orchestrator's lifecycle takes over
       // must release the claim. The orchestrator's finally handles the
       // happy + OCE paths once it has started.
       _activeInstanceRegistry.UnregisterInstance(id);
       throw;
   }
   ```
   **The hand-off boundary is subtle:** once `ExecuteSseAsync` calls `_workflowOrchestrator.ExecuteNextStepAsync`, the orchestrator's `try { ... } finally { UnregisterInstance }` owns the claim release. If `ExecuteSseAsync` throws *after* that point, the orchestrator's finally has already run. So the endpoint's `catch` in the snippet above will trigger a *second* `UnregisterInstance` call on an already-unregistered ID — which is a safe no-op per `TryRemove` semantics (verify in unit test). Document this with an inline comment so a future reader does not "fix" the apparent double-release.
3. The pre-existing status check at lines 75-86 stays — it's a fast-path UX hint that produces a 409 without paying for a `TryClaim` allocation when the status is obviously `Running`. The `TryClaim` is the source-of-truth race gate. Do not remove the pre-existing check.

### Task 4: Update `RetryInstance` to use `TryClaim` (AC10)

- [x] Replace the `GetMessageWriter` check in `RetryInstance` with a `TryClaim` call.

1. At [ExecutionEndpoints.cs:138-145](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L138), replace:
   ```csharp
   if (_activeInstanceRegistry.GetMessageWriter(id) is not null)
   {
       return Conflict(new ErrorResponse
       {
           Error = "already_running",
           Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
       });
   }
   ```
   with:
   ```csharp
   if (!_activeInstanceRegistry.TryClaim(id))
   {
       return Conflict(new ErrorResponse
       {
           Error = "already_running",
           Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
       });
   }
   ```
2. Apply the same try/catch claim-release wrapping shape as Task 3 around the subsequent `SetInstanceStatusAsync` + `ExecuteSseAsync` calls. The existing in-manager exception catch at lines 184-196 stays — it now also releases the claim (add the `UnregisterInstance` call inside that catch block, mirroring Task 3).
3. Consider whether the in-manager guard at [InstanceManager.cs:207-211](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L207) can still fire here once `TryClaim` is the gate. It can if the persisted status was already `Running` from a stale prior write *and* the retry guard at lines 125-132 admitted the request because the persisted status was `Failed` or `Interrupted`. Once the lock is in place this is unreachable, but the catch stays as defence-in-depth.

### Task 5: Wire `WorkflowOrchestrator` to use `AttachOrClaim` (AC8, AC14)

- [x] Update the orchestrator to attach to an already-claimed entry rather than create a new one.

1. At [WorkflowOrchestrator.cs:47](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L47), change `_activeInstanceRegistry.RegisterInstance(instanceId)` to `_activeInstanceRegistry.AttachOrClaim(instanceId)` (if Option A from Task 2 was picked).
2. Verify the orchestrator's `finally`-block `UnregisterInstance(instanceId)` call still fires the same disposal path. No code change expected — `UnregisterInstance` is unchanged.
3. The orchestrator's existing `GetCancellationToken` call (Story 10.8 wiring) at line 53 continues to work — `TryClaim` now creates the CTS eagerly, so the token is non-null whether the entry came from `TryClaim` or `AttachOrClaim`.
4. Update any unit tests that constructed a `WorkflowOrchestrator` directly and previously relied on `RegisterInstance` being called from inside `ExecuteNextStepAsync` — the tests now exercise the `AttachOrClaim` path, which behaves identically when no prior claim exists.

### Task 6: Add concurrency unit tests (AC1, AC2, AC3, AC5, AC6, AC7, AC8, AC9, AC10, AC11, AC12)

- [x] Add tests across `InstanceManagerTests`, `ActiveInstanceRegistryTests`, `ExecutionEndpointsTests`, and `InstanceEndpointsTests`.

1. **`InstanceManagerTests` — concurrent writers serialise (AC1, AC2):** Use a `TaskCompletionSource<bool>` barrier. Start two `SetInstanceStatusAsync` tasks; the first holds inside a fault-injection point (e.g., a custom test-only hook, or accept that `Task.WhenAll` + microsecond-scale sleeps is sufficient for a serialisation test). Assert the final state reflects exactly one of the two writes, and assert the lock was acquired sequentially via a counter or call-order assertion. Reference: NUnit 4 `[Test]`/`Assert.That()` per project-context.md.
2. **`InstanceManagerTests` — different-instance lock isolation (AC7):** Hold instance A's lock with a 5-second delayed write task; assert instance B's write completes within 100ms.
3. **`InstanceManagerTests` — lock released on exception (AC6):** Inject a write failure (e.g., point `_dataRootPath` at a path that becomes unwritable mid-test) and assert the next write on the same instance succeeds without deadlock. Use a `Task.WhenAny` with a `Task.Delay(timeout)` to detect deadlock.
4. **`InstanceManagerTests` — `WaitAsync` cancellation (AC5):** Acquire the lock from a holder task. From a second task, call `SetInstanceStatusAsync` with a token that cancels after 50ms. Assert OCE propagates and assert the holder's release still works (the lock state was never modified by the cancelled wait). Then call `SetInstanceStatusAsync` from a third task and assert it succeeds — proving no spurious release.
5. **`ActiveInstanceRegistryTests` — `TryClaim` atomicity (AC8):** Two concurrent `TryClaim` calls for the same instance ID; assert exactly one returns `true`. Repeat 100 times to flush out single-shot luck. Assert the loser's CTS was disposed (no leak).
6. **`ActiveInstanceRegistryTests` — `UnregisterInstance` releases the claim (AC8):** `TryClaim` → `UnregisterInstance` → `TryClaim` again; assert the second `TryClaim` returns `true`.
7. **`ActiveInstanceRegistryTests` — `AttachOrClaim` paths (AC8):** (a) `TryClaim` then `AttachOrClaim` — assert the channel reader is the *same* reader (not a new one); (b) `AttachOrClaim` standalone — assert it creates a fresh entry.
8. **`ExecutionEndpointsTests` — `StartInstance` returns 409 on contention (AC9):** Mock `IActiveInstanceRegistry.TryClaim` to return `false`; assert 409 + `already_running` payload.
9. **`ExecutionEndpointsTests` — `StartInstance` 409 on in-manager exception (AC11):** Mock `IInstanceManager.SetInstanceStatusAsync` to throw `InvalidOperationException("...already running...")`; assert 409 + `UnregisterInstance` was called via mock.
10. **`ExecutionEndpointsTests` — `RetryInstance` returns 409 on contention (AC10):** Same as (8) for the retry endpoint.
11. **`InstanceEndpointsTests` — cancel-vs-orchestrator interleave (AC12):** Start a task that holds the per-instance lock for 200ms while writing `Status=Running`; in parallel call cancel; assert cancel completes within ~250ms and the final state is `Cancelled` with the orchestrator's `Running` correctly observed and overwritten.

### Task 7: Verify no callers of legacy `RegisterInstance` outside the orchestrator (Task 2 cleanup)

- [x] Confirm the old `RegisterInstance` method has only the orchestrator as a caller. If so, either rename or `[Obsolete]` it.

1. `Grep` for `RegisterInstance(` across the repo. Expected callers: `WorkflowOrchestrator.cs` (will be removed by Task 5), `ActiveInstanceRegistryTests.cs` (test file — update tests to use new method names).
2. If any non-test caller exists outside the orchestrator, surface it as a Failure & Edge Case rather than silently changing behaviour. Confirm with the orchestrator-bound assumption is safe to act on.

### Task 8: Cross-test regression sweep (AC14)

- [x] Run the full test suite and confirm no Story 10.8/10.9/10.10 tests regress.

1. `dotnet test AgentRun.Umbraco.slnx` — must end green.
2. `npm test` from `Client/` — frontend tests should be unaffected (no UI surface change in this story), but verify.
3. Confirm test counts increase by approximately the number of new tests added (no silent test deletion).

### Task 9: Lock-leak smoke (AC13 awareness)

- [x] Add a documentation-only assertion that exercises the "many instances → many locks" growth path so a future reader can grep for it.

1. Add a single `InstanceManagerTests` test: create 100 instances, mutate each once, assert `_instanceLocks.Count == 100`. The intent is not to enforce a count limit (we explicitly defer pruning per AC13) but to make the growth behaviour visible and grep-able when the v2 pruning story is written.
2. Expose the count via an `internal` property `internal int InstanceLockCount => _instanceLocks.Count;` or similar (NOT public — this is a test seam, not an API).

### Task 10: Update deferred-work.md to mark the resolved items (cleanup)

- [x] After Task 8 passes, mark the three deferred-work items as resolved.

1. Strike-through (`~~...~~`) the three items at deferred-work.md lines 28, 34, and 74 with "**RESOLVED in Story 10.1** — per-instance `SemaphoreSlim` in `InstanceManager` + atomic `TryClaim` in `ActiveInstanceRegistry`."
2. Also strike-through deferred-work.md line 35 (Delete endpoint TOCTOU) and line 52 (cancel endpoint TOCTOU) — both were predicated on "requires InstanceManager-level locking" and are resolved by the same change.
3. Strike-through deferred-work.md line 138 (`ConversationStore.TruncateLastAssistantEntryAsync` / `AppendAsync` not concurrency-safe) with "**RESOLVED in Story 10.1** — `TryClaim` makes concurrent callers on the same instance structurally unreachable; see Story 10.1 §'What this story does NOT directly lock'."
4. Do NOT strike-through deferred-work.md line 57 (PromptAssembler `File.Exists` then `ReadAllText` TOCTOU on agent/sidecar files). Agent/sidecar files are read-only workflow assets that no AgentRun code path mutates. The original deferral note speculatively cited "instance locking will prevent concurrent modifications" but the actual concern (concurrent file-system mutation by an external editor while an instance is running) is not addressed by `InstanceManager` locking. Leave the item as-is; it is genuinely out of scope for 10.1.
5. Do NOT strike-through deferred-work.md line 265 (`instance.yaml` `.tmp` orphan when cancellation fires mid-write) — this is a separate `WriteStateAtomicAsync` cleanup concern. Story 10.1 holds the lock during the read-modify-write but does not change the inner write semantics; the .tmp orphan window remains as documented (low-risk; next write overwrites). Surfaced in `Failure & Edge Cases` for awareness.
6. Do NOT strike-through deferred-work.md line 266 (`ActiveInstanceRegistry.Dispose` no `_disposed` flag) — same defensive-only concern; the new `TryClaim` inherits the same pattern (no disposed-state guard) for consistency. Documented in `Failure & Edge Cases`.
7. Do NOT strike-through deferred-work.md line 274 (`InstanceManager.SetInstanceStatusAsync` engine-level no Interrupted-protection guard) — architectural concern about adding `Interrupted` to the terminal-status guard from a *direct* `InstanceManager` caller. The lock makes the existing guard atomic but does not add new guards. Out of scope for 10.1; revisit if a second code path attempts Interrupted transitions outside the endpoint layer.
8. This is a docs-only commit on top of the implementation commit; commit-per-story rule says ship as part of the story commit.

### Task 11: Manual E2E validation

- [ ] Adam to walk through the following scenarios in the browser before code review.

| # | Scenario | Expected Result |
|---|---|---|
| 11.1 | **Double-click Continue on a between-steps interactive workflow.** Open an interactive instance paused between step N and step N+1. Click Continue twice in rapid succession. | First click drives step N+1 normally. Second click returns 409 `already_running` (visible in browser DevTools network panel). The orchestrator does not start a second time; only one set of SSE events for step N+1 arrives. |
| 11.2 | **Double-click Retry on a Failed instance.** Trigger a step failure (e.g., kill the AI provider mid-step). Click Retry twice rapidly. | First click drives the retry. Second click returns 409 `already_running`. No state corruption (verify `instance.yaml` on disk). |
| 11.3 | **Cancel during a long step.** Start a long-running step (e.g., a multi-fetch scanner). Click Cancel. | Status flips to `Cancelled` within 1 second of click. The orchestrator's mutations (e.g., a `Steps[N].Status=Active` write that was about to fire) do not overwrite the `Cancelled` write — verify by inspecting `instance.yaml` final state. |
| 11.4 | **Cancel + Retry race.** Start a step. Click Cancel; immediately click Retry. | Cancel succeeds, status = `Cancelled`. Retry returns a clean 409 because `Cancelled` is not in `{Failed, Interrupted}`. No 500. |
| 11.5 | **Two browser tabs on the same instance.** Open the same instance in two tabs. In tab A, click Continue. In tab B, immediately click Continue. | Tab A's request drives the step; tab A sees normal SSE event stream. Tab B's request returns 409 `already_running`. Tab B's UI shows the error toast (existing 10.8 error-handling). |
| 11.6 | **Two browser tabs on DIFFERENT instances of the same workflow.** Open instance X in tab A, instance Y in tab B. Click Continue in both within a second. | Both succeed. Each tab sees its own SSE stream. No 409. (Confirms locking is per-instance, not per-workflow.) |
| 11.7 | **Process restart resilience.** Start an instance, let it run a few seconds, restart the process. Re-open the instance. | Status reflects the last persisted YAML (likely `Interrupted` per Story 10.9). Lock dictionary is fresh; clicking Retry works without any stale-lock issue. |
| 11.8 | **Lock-dictionary growth visibility.** Run 20 instances in sequence (start → finish → next). | Internal `InstanceLockCount` grows to 20 (verify via test seam, not via UI). Confirms locks are not pruned, matching AC13. |

Capture screenshots only if a failure mode appears. On full pass, log a one-line confirmation in the `## Manual E2E Verification` section below.

## Failure & Edge Cases

- **`WaitAsync(ct)` with a token that fires before lock acquisition (AC5).** Throws OCE. Lock state untouched. The `try/finally` placement (finally OUTSIDE the wait) ensures `Release()` is not called for an unacquired lock. Verified by Task 6 unit test.
- **Mutation throws after lock acquired (AC6).** `finally` releases. Subsequent waiters proceed. Verified by Task 6 unit test.
- **`TryClaim` succeeds but orchestrator never runs (e.g., `ExecuteSseAsync` throws synchronously before reaching the orchestrator).** Endpoint's catch block calls `UnregisterInstance` to release the claim. Verified by Task 6 unit test.
- **`TryClaim` succeeds, orchestrator runs and exits via OCE — and then the endpoint's catch block ALSO fires (OCE propagated up).** The orchestrator's finally has already called `UnregisterInstance`; the endpoint's catch will call it a second time. `TryRemove` returns false (not found) on the second call — safe no-op. Inline comment in the endpoint catch documents this.
- **`TryClaim` succeeds, orchestrator runs to completion, finally releases the claim. Then a second `/start` arrives and `TryClaim` returns true again.** Expected and correct — claim was released, slot is free, second request can proceed. Not an error path.
- **Process crashes while the lock is held.** The lock dict is in-memory; on restart the dict is fresh-empty. Persisted YAML is the source of truth. The crashed instance's status will be whatever was last written; if a write was in-flight when the crash happened, the file-level `File.Move` atomicity means the file is either pre-write or post-write, never torn. The user retries; the retry's lock acquisition succeeds against a fresh dict.
- **`SetInstanceStatusAsync` called with the same `Status` value already persisted.** Today this is a no-op (UpdatedAt changes but status doesn't). With the lock, the no-op still happens — but inside the critical section. Acceptable; the lock is held briefly. No correctness issue.
- **`UpdateStepStatusAsync` called with `stepIndex` out of range.** Throws `ArgumentOutOfRangeException` AFTER lock acquisition. `finally` releases the lock. Subsequent calls on the same instance proceed.
- **`AdvanceStepAsync` called when already on the last step.** Throws `InvalidOperationException` AFTER lock acquisition. `finally` releases. No deadlock.
- **`DeleteInstanceAsync` called on a non-terminal instance.** Today throws `InvalidOperationException` ([InstanceManager.cs:265-267](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L265)). With lock, throws AFTER acquisition. Finally releases. Caller (likely `InstanceEndpoints.DeleteInstance` at [InstanceEndpoints.cs:151-158](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L151)) catches and returns 409.
- **Cancel called when no orchestrator is running and no entry exists in registry.** `RequestCancellation` is a documented no-op ([ActiveInstanceRegistry.cs:103-119](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs#L103)). The cancel endpoint's `SetInstanceStatusAsync(Cancelled)` still runs and acquires the per-instance lock. No interaction with the claim primitive — claim only matters for orchestrator lifecycle, and there's no orchestrator here.
- **`AttachOrClaim` called on an entry that was created by `TryClaim` but the CTS has been disposed (Story 10.8 re-registration disposal logic — should not fire on the new path, but defence-in-depth).** Returns the entry's existing channel reader. The orchestrator's downstream `GetCancellationToken` call will detect the disposed CTS and return `null` (existing behaviour at [ActiveInstanceRegistry.cs:96-100](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs#L96)), and the linked-token construction at [WorkflowOrchestrator.cs:53-56](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L53) falls back to `CancellationToken.None`. This degrades cancel behaviour for that one orchestrator run but does not crash. **Note:** this scenario should not occur on the new code paths because `TryClaim`'s entry is not subject to the replace-on-collision disposal that triggered the original concern. Document and move on.
- **Unrecognised or unspecified inputs to `TryClaim` / `AttachOrClaim`** (e.g., null instanceId, empty string). Reject at the registry boundary with `ArgumentException` — do not silently treat as "free slot". This is a deny-by-default rule for security-relevant code per the project-context.md "Security code: deny by default" lesson. The endpoint already validates the instance exists before reaching the claim, so this is defence-in-depth.
- **Concurrent `TryClaim` calls with extreme contention (theoretical: 100 simultaneous calls).** Exactly one wins per `ConcurrentDictionary.TryAdd` semantics. The 99 losers each construct a throwaway `InstanceEntry` (Channel + CTS) and `DisposeEntry` it. Wasted allocations are bounded by contention rate; not a real concern for beta load profiles. Documented for completeness.
- **`Task.Run` or `Task.Factory.StartNew` on `WaitAsync`.** Don't. `WaitAsync` is already async-friendly. Do not wrap it.
- **Lock acquired, then a re-entrant call on the same thread to another locked method.** Would deadlock — `SemaphoreSlim` is NOT re-entrant. None of the four locked methods call each other from inside their critical sections (verify this in code review). If a future change introduces re-entrance, the deadlock will surface immediately in tests; do not switch to `lock {}` (which is re-entrant) because async/await is incompatible with `lock {}`.
- **`WriteStateAtomicAsync` cancellation mid-write leaves a `.tmp` file orphan.** Pre-existing concern logged at [deferred-work.md:265](deferred-work.md#L265). With the lock held during the read-modify-write, the cancellation token can fire between `File.WriteAllTextAsync(tmp, ...)` and `File.Move(tmp, target)`. The `.tmp` file is orphaned on disk. Next `SetInstanceStatusAsync` for the same instance overwrites it. This story does NOT fix the .tmp window — the lock changes WHEN the orphan can occur but not WHETHER. Documented for awareness; explicit fix is a future small story.
- **`ActiveInstanceRegistry.TryClaim` post-dispose.** Pre-existing concern logged at [deferred-work.md:266](deferred-work.md#L266) — `ActiveInstanceRegistry.Dispose` has no `_disposed` flag. `TryClaim` inherits the same pattern (no disposed-state guard). The singleton is only disposed on host shutdown, which already races with app-stopping cancellation; the absence of a guard is documented as defensive-only and not a blocker. Do NOT add a `_disposed` flag in this story — it would diverge from the existing pattern; if a guard is needed, do it across the whole class in a separate story.
- **In-flight ConversationStore append from the orchestrator while cancel fires.** `ConversationStore.AppendAsync` is unlocked (see "What this story does NOT directly lock" in Context). Cancel propagates through the linked CTS to the orchestrator's `await client.GetStreamingResponseAsync(...)` and `tool.ExecuteAsync(...)` calls. The append that records the cancelled assistant message either completes (orchestrator sees the OCE on the next iteration) or never runs (OCE surfaces before the recorder is called). No torn JSONL line — `AppendAsync` writes line-by-line with an immediate flush. Verified by Story 10.8's manual E2E.

## What NOT to Build

- **Do NOT introduce a distributed lock.** No Redis, no SQL Server `sp_getapplock`, no file-system flock. In-process only.
- **Do NOT prune the lock dictionary.** AC13 explicitly defers pruning to a v2 story. Pruning under contention requires either reference-counted locks or a TTL-based eviction loop; both add complexity that the beta does not need.
- **Do NOT lock pure reads.** `GetInstanceAsync`, `ListInstancesAsync`, `FindInstanceAsync` stay unlocked. The dashboard polls these heavily; locking would serialise read traffic against write traffic for no correctness gain.
- **Do NOT add a `Cancelling` interim status.** Story 10.8 architect decision 8 already locked this; restating to prevent regression.
- **Do NOT change the on-disk YAML schema.** No new fields. The lock is purely in-memory.
- **Do NOT switch from `SemaphoreSlim` to `lock {}`.** `lock {}` is incompatible with `await`.
- **Do NOT introduce a global instance lock.** The beta's whole UX assumes N instances run in parallel.
- **Do NOT remove the existing in-manager `Status==Running` guard at [InstanceManager.cs:207-211](../../AgentRun.Umbraco/Instances/InstanceManager.cs#L207).** It becomes reliably atomic with the lock and serves as the inner backstop. Defence-in-depth.
- **Do NOT remove the pre-claim status check at [ExecutionEndpoints.cs:75-86](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L75).** Fast-path UX hint; avoids paying for `TryClaim` allocation when status is obviously `Running`.
- **Do NOT delete the legacy `RegisterInstance` method.** Its replace-on-collision disposal logic (Story 10.8 architect lock) is non-trivial and worth preserving as `[Obsolete]` for the rare future need.
- **Do NOT add a "force-claim" override.** No code path should be able to claim a slot that's already claimed. If a stale claim exists from a crashed orchestrator (impossible in-process; the dict is cleared on restart), the user retries and the retry's `TryClaim` succeeds.
- **Do NOT introduce an `IInstanceLockProvider` abstraction.** YAGNI. The lock dict is a private implementation detail of `InstanceManager`. If a future story needs to share the locks with another service, refactor then.

## Dev Notes

**Per-instance lock placement rationale.** The lock lives in `InstanceManager` (not `ActiveInstanceRegistry`) because the registry's lifetime is "active orchestrator only" and many state mutations happen *outside* an orchestrator (cancel-on-pending, delete-on-completed, status flips during retry preparation). Co-locating the lock with the mutation site minimises the surface area where a future code change could forget to acquire it.

**`TryClaim` placement rationale.** The claim lives in `ActiveInstanceRegistry` (not `InstanceManager`) because the claim semantically tracks "is an orchestrator currently driving this instance" — which is exactly what the registry tracks today via `GetMessageWriter`. Adding `TryClaim` next to the existing `RegisterInstance`/`UnregisterInstance`/`GetMessageWriter` API surface keeps the orchestrator-lifecycle concerns together.

**Why not collapse the inner lock and the outer claim into one mechanism?** They have different blocking semantics (locked decision 3). The inner lock blocks for milliseconds during read-modify-write; the outer claim must fail-fast (409) because two orchestrator runs on the same instance are not a queueable operation.

**Lock allocation cost.** `SemaphoreSlim(1, 1)` is ≈ 96 bytes. 10,000 instances → < 1 MB of locks. The beta will see at most low-thousands of instances per process lifetime. Pruning is genuinely unnecessary at this scale.

**Async lock pattern.** `SemaphoreSlim.WaitAsync(ct)` is the standard async lock primitive in .NET. Do not use `Monitor`, `lock {}`, or `Mutex` — none are async-friendly. Do not introduce `AsyncLock` from third-party libraries — `SemaphoreSlim(1, 1)` is the idiomatic stdlib answer.

**Test strategy.** Concurrency tests are notoriously flaky if written with `Task.Delay` for synchronisation. Prefer `TaskCompletionSource<bool>` barriers or `ManualResetEventSlim` to deterministically interleave operations. If `Task.Delay` must be used, keep delays generous (50ms+) and the assertion windows tight (a holder-task delay of 5s + an assertion window of 100ms is a 50× safety margin).

**Project context references.** From `_bmad-output/project-context.md`:
- All async methods accept `CancellationToken` as last parameter, suffix `Async`. ✓
- `_camelCase` private fields. ✓ (`_instanceLocks`)
- Atomic writes mandatory for instance state — already in place via `WriteStateAtomicAsync`; the lock adds operation-level atomicity on top of file-level atomicity.
- "**Instance concurrency prevention** — only one step may execute per instance at a time. Guard against duplicate start requests." (project-context.md:165) — this story is the implementation of that rule.
- `dotnet test AgentRun.Umbraco.slnx` (always specify the slnx file).

**Cross-story integration.** Story 10.8 (cancel wiring), 10.9 (SSE disconnect / Interrupted), 10.10 (resume after step completion) all touched the same files (`InstanceManager.cs`, `ExecutionEndpoints.cs`, `InstanceEndpoints.cs`, `ActiveInstanceRegistry.cs`). This story is the final piece that makes those flows race-free. Do not relitigate any of their architect decisions; surface conflicts only if a literal code conflict exists.

### Project Structure Notes

- `InstanceManager.cs` is a `partial` class at line 10. The lock dict and helper can live in the existing file (no second partial file needed unless the file is approaching the size cap — currently 417 lines, well under any threshold).
- `ActiveInstanceRegistry.cs` already houses both the interface and the implementation in one file (project convention per locked decision in Story 10.8). Continue that pattern for `TryClaim` / `AttachOrClaim`.
- Test files mirror source paths: `AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs`, `AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs`, `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs`, `AgentRun.Umbraco.Tests/Endpoints/InstanceEndpointsTests.cs`. Several of these are marked modified in `git status` — Adam may already have started the work; reconcile cleanly.

### References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) §"Story 10.1: Instance Concurrency Locking" lines 2159-2165, §"Known Issues for Beta v1" line 1995
- [_bmad-output/implementation-artifacts/deferred-work.md](deferred-work.md) lines 28, 34, 35, 52, 74 (the deferred items this story resolves)
- [_bmad-output/implementation-artifacts/10-8-cancel-wiring-cancellation-token-per-instance.md](10-8-cancel-wiring-cancellation-token-per-instance.md) — registry pattern, locked decisions on disposal
- [_bmad-output/implementation-artifacts/10-9-sse-disconnect-resilience.md](10-9-sse-disconnect-resilience.md) — terminal-status guard narrowing, `Interrupted` status
- [_bmad-output/implementation-artifacts/10-10-instance-resume-after-step-completion.md](10-10-instance-resume-after-step-completion.md) — Active-step cleanup on cancel, Continue button gating
- [_bmad-output/project-context.md](../project-context.md) §"State & Persistence" line 165, §"Code Quality" naming conventions, §"Development Workflow" testing
- [AgentRun.Umbraco/Instances/InstanceManager.cs](../../AgentRun.Umbraco/Instances/InstanceManager.cs)
- [AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs](../../AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs)
- [AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs](../../AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs)
- [AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs](../../AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs)
- [AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs](../../AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs)

## Definition of Done

- [ ] All 14 ACs verified — unit tests for AC1–AC11, AC12, AC14; documentation/code-comment for AC13; manual E2E for the user-visible AC9, AC10, AC12.
- [ ] All four locked-write methods in `InstanceManager` wrap their read-modify-write in a per-instance `SemaphoreSlim`.
- [ ] `IActiveInstanceRegistry.TryClaim` exists, is atomic, and is used by both `StartInstance` and `RetryInstance` as the source-of-truth race gate.
- [ ] `StartInstance` maps in-manager `InvalidOperationException("...already running...")` to 409, mirroring `RetryInstance`.
- [ ] `WorkflowOrchestrator` uses `AttachOrClaim` (or equivalent) and the orchestrator's `finally` correctly releases the claim on every exit path.
- [ ] Endpoint's catch blocks release the claim if an exception fires before the orchestrator's lifecycle takes ownership (no claim leak).
- [ ] `dotnet test AgentRun.Umbraco.slnx` ends green; test count grows by approximately the number of new tests in Task 6 (no silent test deletion).
- [ ] `npm test` from `Client/` ends green (no frontend regression).
- [ ] Manual E2E Task 11 walked end-to-end by Adam; one-line confirmations logged below.
- [ ] `deferred-work.md` items at lines 28, 34, 35, 52, 74, 138 marked resolved with strike-through and a back-pointer to this story; lines 57, 265, 266, 274 explicitly NOT struck and rationale captured in Task 10.
- [ ] No new on-disk YAML schema changes.
- [ ] No new DI registrations (the lock dict is private to `InstanceManager`; `IActiveInstanceRegistry` is already singleton-registered).
- [ ] One commit for the implementation; deferred-work.md updates included in the same commit per the commit-per-story rule.

## Manual E2E Verification

Walked 2026-04-15 by Adam. Skipped scenarios are covered by automated tests; rationale below.

- [x] 11.1 — Double-click Continue: first 200 + SSE, second 409 `already_running`. TryClaim race gate verified at network layer.
- [x] 11.3 — Cancel during long step (instance `7ee403e3389e475ca58097927d941d69`): `status: Cancelled`, mid-flight `scanner` step `Cancelled`, downstream steps untouched. Lock serialised orchestrator-vs-cancel writes cleanly.
- [x] 11.6 — Two tabs, different instances of same workflow: both started within 1s, both succeeded with own SSE streams, no 409. Per-instance lock scope verified.
- [x] 11.2 — Double-click Retry: skipped, identical TryClaim mechanism to 11.1; covered by `Retry_TryClaimReturnsFalse_Returns409` and `Retry_OnInterrupted_WithActiveClaim_Returns409` unit tests.
- [x] 11.4 — Cancel + Retry race: skipped, covered by `Retry_TryClaimReturnsFalse_Returns409` (rejects retry on Cancelled status via `invalid_state` 409, not a 500).
- [x] 11.5 — Two tabs, same instance: skipped, identical race gate as 11.1.
- [x] 11.7 — Process restart resilience: skipped, lock dict is in-memory `ConcurrentDictionary`; trivially re-empty on process restart by construction (locked decision 8).
- [x] 11.8 — Lock-dictionary growth: covered by `Lock_DictionaryGrowsWithInstanceCount_NeverPruned` unit test (asserts `InstanceLockCount >= 100` after 100 mutated instances).

## Pre-Implementation Architect Review (optional)

This story does not require a pre-implementation Winston gate (cf. Story 10.6's gate). The locked decisions are all standard async-locking patterns, the deferred-work items have been on Winston's radar since Story 3.1 as planned `SemaphoreSlim` work, and the design consciously preserves Story 10.8's hard-won disposal logic via the `[Obsolete]` legacy path. If during implementation Amelia hits a structural surprise — e.g., a non-test caller of legacy `RegisterInstance` outside the orchestrator (Task 7) — pause and ping Winston rather than silently changing semantics.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context) via Claude Code, 2026-04-15.

### Debug Log References

- `dotnet build AgentRun.Umbraco.slnx` — clean (2 pre-existing warnings in `GetContentTool.cs` unchanged; no new warnings).
- `dotnet test AgentRun.Umbraco.slnx` — 635/635 (+19 new vs. 616 baseline).
- `npm test` (from `AgentRun.Umbraco/Client/`) — 183/183 unchanged.
- One in-flight fix during Task 6: `Assert.ThrowsAsync<OperationCanceledException>` rejected the derived `TaskCanceledException` that `SemaphoreSlim.WaitAsync(ct)` raises. Switched to `Assert.CatchAsync<OperationCanceledException>` which accepts the derived type — matches the actual contract the caller sees.

### Completion Notes List

- **Task 1 — per-instance lock in `InstanceManager`:** Added `ConcurrentDictionary<string, SemaphoreSlim> _instanceLocks` + `GetOrCreateInstanceLock(instanceId)`. Wrapped `SetInstanceStatusAsync`, `UpdateStepStatusAsync`, `AdvanceStepAsync`, `DeleteInstanceAsync` in `await sem.WaitAsync(ct); try { ... } finally { sem.Release(); }`. `WaitAsync` placed *before* the `try` so an OCE from the wait itself does not attempt a spurious `Release`. Pure reads (`GetInstanceAsync`, `ListInstancesAsync`, `FindInstanceAsync`) stay unlocked per AC4 + locked decision 7. `CreateInstanceAsync` not locked (fresh GUID has no concurrency surface). `DeleteInstanceAsync` fast-paths out on `!File.Exists(yamlPath)` *before* acquiring the lock — cheap, non-authoritative; the post-lock `ReadStateAsync` is the source of truth.
- **Task 2 — `TryClaim` + `AttachOrClaim` in `ActiveInstanceRegistry`:** Added both methods to the interface and implementation. `TryClaim` uses `_entries.TryAdd` (pure atomic, no retry loop, no replace); on collision the throwaway entry is `DisposeEntry`-d. `AttachOrClaim` uses `_entries.GetOrAdd` for the fresh-entry path and `TryGetValue` for the fast-path reattach. Legacy `RegisterInstance` preserved with `[Obsolete]` on the interface declaration (Option A from the story) — Story 10.8's replace-on-collision disposal logic intact as a defensive fallback. Deny-by-default: both `TryClaim` and `AttachOrClaim` throw `ArgumentException` on null/empty instance id.
- **Task 3 — `StartInstance`:** Calls `TryClaim(id)` after the provider check; on `false` returns 409 `already_running`. Wrapped the SetStatus + ExecuteSseAsync in `try { ... } catch { UnregisterInstance; throw; }` so any exception before the orchestrator's lifecycle takes over releases the claim. Inner `catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))` mirrors `RetryInstance` and maps to 409 + claim release.
- **Task 4 — `RetryInstance`:** Replaced `GetMessageWriter` check with `TryClaim`. Every exit path (invalid-state 409, in-manager-exception 409, any other exception) releases the claim via `UnregisterInstance` before returning; happy path hands off to `ExecuteSseAsync` whose orchestrator owns release.
- **Task 5 — `WorkflowOrchestrator`:** One line change — `RegisterInstance(instanceId)` → `AttachOrClaim(instanceId)`. Orchestrator still works end-to-end when invoked directly (tests) because `AttachOrClaim` creates a fresh entry when none exists. Linked-token construction at line 53 unchanged — CTS is eager in both `TryClaim` and `AttachOrClaim` paths.
- **Task 6 — unit tests (19 new):** `InstanceManagerTests` gained 5 (concurrent-serialise-writes, concurrent-different-steps, per-instance-scope, lock-released-on-exception, WaitAsync-cancellation-no-spurious-release) + 1 for AC13 (lock-dictionary growth). `ActiveInstanceRegistryTests` gained 9 (TryClaim free/claimed, TryClaim concurrent 100× iterations, UnregisterInstance-releases-claim, AttachOrClaim-after-TryClaim-same-reader, AttachOrClaim-standalone, AttachOrClaim-idempotent, round-trip, null-id guards × 2). `ExecutionEndpointsTests` gained 3 (Start-TryClaim-false, Start-SetStatus-already-running-releases-claim, Retry-TryClaim-false) + retrofit of the existing `Retry_OnInterrupted_ConcurrentRetry_Returns409NotUnhandled` to also assert claim release.
- **Task 7 — legacy caller audit:** Only caller of `RegisterInstance` outside test code was `WorkflowOrchestrator.cs:47` (removed in Task 5). Method retained with `[Obsolete]` on the interface; class implementation stays unannotated so the legacy-behaviour tests in `ActiveInstanceRegistryTests` (which exercise replace-on-collision disposal against the concrete class) continue to run warning-free. Only new warning was `WorkflowOrchestratorTests.cs:362`, fixed by switching that mock to `AttachOrClaim` in the same commit.
- **Task 8 — regression sweep:** Green. Story 10.8/10.9/10.10 tests all pass unmodified. One existing `ExecutionEndpointsTests` setup updated to default `TryClaim(Arg.Any<string>()).Returns(true)` so pre-10.1 tests reach their original code paths; tests that exercise contention override with `false`.
- **Task 9 — lock-leak smoke:** `InstanceManagerTests.Lock_DictionaryGrowsWithInstanceCount_NeverPruned` mutates 100 instances and asserts `_instanceLocks.Count == 100` via the `internal InstanceLockCount` seam. Grep target for the future v2 pruning story.
- **Task 10 — deferred-work.md:** Struck lines 28, 34, 35, 52, 74, 138 with "**RESOLVED in Story 10.1**" back-pointers. Lines 57, 265, 266, 274 deliberately NOT struck per the story spec.

### File List

**Source (production):**
- `AgentRun.Umbraco/Instances/InstanceManager.cs` — added `_instanceLocks` dict + `GetOrCreateInstanceLock`; wrapped 4 locked methods in `SemaphoreSlim` acquire/release.
- `AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs` — added `TryClaim` + `AttachOrClaim` methods (interface + implementation); `[Obsolete]` on interface's `RegisterInstance`.
- `AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs` — `RegisterInstance` → `AttachOrClaim`.
- `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs` — `StartInstance` + `RetryInstance` now call `TryClaim` as the race gate; both map in-manager "already running" `InvalidOperationException` to 409 and release the claim on every non-happy exit.

**Tests:**
- `AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs` — 6 new tests (concurrency + lock-leak smoke).
- `AgentRun.Umbraco.Tests/Engine/ActiveInstanceRegistryTests.cs` — 9 new tests (TryClaim + AttachOrClaim).
- `AgentRun.Umbraco.Tests/Endpoints/ExecutionEndpointsTests.cs` — 3 new tests + 2 existing tests retrofit (TryClaim SetUp default; pre-existing concurrency tests assert claim release; `WithActiveRegistryWriter` → `WithActiveClaim`).
- `AgentRun.Umbraco.Tests/Engine/WorkflowOrchestratorTests.cs` — mock at line 362 switched from `RegisterInstance` to `AttachOrClaim`.

**Docs:**
- `_bmad-output/implementation-artifacts/deferred-work.md` — 6 items marked **RESOLVED** with strike-through and back-pointer; lines 57 / 265 / 266 / 274 deliberately retained per Task 10.

### Review Findings

_Code review run on 2026-04-15 (Blind Hunter + Edge Case Hunter + Acceptance Auditor). All patches applied; build + 636/636 tests green._

- [x] [Review][Patch] **[HIGH]** Claim leaks permanently when anything throws between endpoint's `TryClaim` and orchestrator's `try` block — `AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs:42-62` + `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:373-401`. `GetWorkflow`, `AttachOrClaim`, `GetCancellationToken`, and `CreateLinkedTokenSource` run *before* the orchestrator's `try { ... } finally { UnregisterInstance }` at line 64/195. If any throws (realistic: workflow re-registered or removed during a run → `GetWorkflow` returns null → `AgentRunException` at line 43), the exception propagates into `ExecuteSseAsync`'s general `catch (Exception ex)` at line 373, which logs + sets Failed + emits run.error and **returns EmptyResult without rethrowing**. The endpoint's outer `try/catch` at line 140/251 never fires because `ExecuteSseAsync` returned normally. The TryClaim'd registry slot leaks permanently; all future Start/Retry on that instance return 409 `already_running` until process restart. Fix: move line 42-62 of the orchestrator inside the `try` block (convert `using var linkedCts` to explicit try/finally dispose) OR rethrow from `ExecuteSseAsync`'s general Exception catch so the endpoint's outer catch fires.
- [x] [Review][Patch] **[MEDIUM]** AC12 test missing + `InstanceEndpointsTests.cs` listed in Files-in-Scope but not modified — no test exercises the cancel-vs-orchestrator interleave the spec requires (Task 6.11). Add the cancel endpoint test that serialises against a concurrent orchestrator `SetInstanceStatusAsync(Running)` and asserts cancel completes within ~250ms with final state `Cancelled`.
- [x] [Review][Patch] **[MEDIUM]** `Lock_ConcurrentSetInstanceStatus_SerialisesWrites` passes even without the `SemaphoreSlim` — the assertion `final.Status is Cancelled or Failed` is satisfied by the pre-existing terminal-status guard alone, because both concurrent writes are valid transitions from `Running` and File.Move is atomic at the filesystem layer. Removing the lock would not fail this test. [`AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs:524`]. Fix: strengthen by asserting no torn YAML (e.g., round-trip parseability during contention) OR instrument the semaphore via the `InstanceLockCount` seam + reflection to verify acquire-count sequence.
- [x] [Review][Patch] **[MEDIUM]** `Lock_ConcurrentUpdateStepStatus_DifferentSteps_BothWritesLand` runs once with no iteration — lost-update detection on a single trial is unreliable (contrast `TryClaim_ConcurrentCalls_ExactlyOneWins` which iterates 100×). A lock regression could pass this test often. [`AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs:563`]. Fix: wrap in 50×–100× iteration loop, assert both writes land every iteration.
- [x] [Review][Patch] **[MEDIUM]** Endpoint couples to `InstanceManager`'s exception prose: `catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))` at `AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:123` and `:239`. Any future message rewording (localisation, rephrase) silently reverts the 409 to a 500. Fix: introduce `InstanceAlreadyRunningException : InvalidOperationException` in `InstanceManager`, throw typed; endpoints catch the type.
- [x] [Review][Patch] **[MEDIUM]** `ExecutionEndpointsTests` SetUp at line 49 defaults `TryClaim(Arg.Any<string>()).Returns(true)` so every pre-10.1 success-path test silently acquires a claim without asserting its release. A future regression that leaks the slot on the happy path goes unobserved. Fix: narrow the default to only tests that exercise the claim, or add `_activeInstanceRegistry.Received(1).UnregisterInstance(...)` assertions on existing happy-path tests.
- [x] [Review][Patch] **[LOW]** `Lock_DifferentInstances_DoNotBlockEachOther` uses a 500ms wall-clock bound (`sw.ElapsedMilliseconds < 500`) — under CI disk contention a single YAML read+write round-trip can exceed that. [`AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs:632`]. Fix: replace wall-clock with a `ManualResetEventSlim` that gates A's write until B completes, then observe completion order.
- [x] [Review][Patch] **[LOW]** `Lock_DictionaryGrowsWithInstanceCount_NeverPruned` asserts exact equality `InstanceLockCount == 100`; brittle to future SetUp changes (shared-manager fixture, cached instances). [`AgentRun.Umbraco.Tests/Instances/InstanceManagerTests.cs:696`]. Fix: assert `InstanceLockCount == 0` pre-condition + `InstanceLockCount >= instanceCount` post-condition.
- [x] [Review][Defer] SendMessage channel stale-writer race — pre-existing; 10.1 does not widen structurally [`AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs:421-432`] — deferred, pre-existing.
- [x] [Review][Defer] StartInstance fast-path 409 at lines 75-86 can fire on a stale `GetMessageWriter` reading (orchestrator released between FindInstance and MessageWriter reads); user sees transient `already_running` that self-resolves on retry — deferred, pre-existing UX hint; TryClaim is authoritative.
- [x] [Review][Defer] `UnregisterInstance` validates nothing while `TryClaim`/`AttachOrClaim` deny-by-default on null/empty — asymmetric boundary hides future misuse [`AgentRun.Umbraco/Engine/ActiveInstanceRegistry.cs`] — deferred, pre-existing pattern consistency.
- [x] [Review][Defer] No test covers Retry-path claim release on a pre-`UpdateStepStatusAsync` throw (symmetric to existing Start-path `Start_SetStatusThrowsAlreadyRunning_Returns409AndReleasesClaim`) — deferred, the outer catch at line 251 already handles this correctly; test-only gap.

## Change Log

| Date | Change |
|---|---|
| 2026-04-15 | Story 10.1 implementation (Amelia / Opus 4.6): Tasks 1–10 complete; 19 new tests; 635/635 backend + 183/183 frontend; 6 deferred-work items resolved; status → review; Task 11 manual E2E pending Adam. |
| 2026-04-15 | Code review (Amelia / Opus 4.6): 1 HIGH (claim-leak pre-try), 5 MED, 2 LOW patched. Typed `InstanceAlreadyRunningException` replaces message-prose coupling; orchestrator restructured so `AttachOrClaim` runs first under `try/finally`; AC12 integration test added with real `InstanceManager`; concurrency tests strengthened with 50× iteration + lost-update assertion; lock-isolation test swapped to deterministic semaphore reflection; 4 items deferred to future cleanup. 636/636 tests green; status → done (manual E2E still pending Adam). |
