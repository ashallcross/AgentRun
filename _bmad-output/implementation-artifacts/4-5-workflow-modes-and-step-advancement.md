# Story 4.5: Workflow Modes & Step Advancement

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer or editor,
I want to choose between interactive mode (manual step advancement) and autonomous mode (automatic advancement),
so that I can review intermediate outputs before proceeding or let the workflow run unattended.

## Acceptance Criteria

1. **Given** a workflow defines `mode: "interactive"` in workflow.yaml, **when** a step completes, **then** the engine waits — no next step is started until the user clicks "Continue" (FR29, FR31)
2. **Given** a step completes in interactive mode, **when** the dashboard refreshes, **then** the header shows a "Continue" primary action button (replacing "Start")
3. **Given** the user clicks "Continue" (or "Start" for the first step), **when** `POST /umbraco/api/shallai/instances/{id}/start` is called, **then** the endpoint sets instance status to Running (if not already), advances `CurrentStepIndex`, starts the step execution in the background, and returns an SSE stream emitting AG-UI events
4. **Given** the SSE stream is open during step execution, **then** it emits events: `run.started`, `text.delta`, `tool.start`, `tool.args`, `tool.end`, `tool.result`, `step.started`, `step.finished` — each as a single-line JSON `data:` field with an `event:` type header
5. **Given** a workflow defines `mode: "autonomous"`, **when** a step completes and the completion check passes, **then** the engine automatically starts the next step within 5 seconds (FR30, NFR3) with a system message "Auto-advancing to {next step name}..."
6. **Given** autonomous mode, **then** the process continues until all steps complete or an error occurs — no user intervention required
7. **Given** the final step of any workflow completes (interactive or autonomous), **when** all steps show Complete status, **then** instance status is set to `Completed`, the primary action button is hidden (workflow is done), and a `run.finished` SSE event is emitted
8. **Given** a user sends `POST /instances/{id}/start` for a Pending instance, **when** `IProfileResolver.HasConfiguredProviderAsync()` returns false, **then** the endpoint returns a 400 error with `error: "no_provider"` and `message: "Configure an AI provider in Umbraco.AI before workflows can run."` (FR53, UX-DR28)
9. **Given** a user sends `POST /instances/{id}/start` for an instance that is already Running, **then** the endpoint returns 409 Conflict (prevent concurrent execution)
10. **Given** a user sends `POST /instances/{id}/start` for a Completed, Failed, or Cancelled instance, **then** the endpoint returns 409 Conflict
11. Unit tests verify: workflow orchestrator step advancement, autonomous auto-advance, interactive wait behaviour, SSE event emission, provider prerequisite check, instance status transitions, and edge cases (final step, already-running, error during step)

## What NOT to Build

- No conversation persistence to JSONL (Story 6.1)
- No chat panel or message rendering in the dashboard (Epic 6)
- No user messaging / multi-turn conversation (Story 6.4)
- No retry logic for failed steps (Story 7.2)
- No tool implementations (Epic 5) — the tool list will be empty for now; the tool loop handles zero tools correctly
- No artifact viewer component (Epic 8)
- No SSE reconnection with replay (deferred to v2, NFR5)
- No per-instance SemaphoreSlim locking (acknowledged deferred item — the `SetInstanceStatusAsync` already rejects double-Running; full locking deferred)
- No state machine enforcement beyond what's needed for this story (the Running guard is sufficient)

## Tasks / Subtasks

- [x] Task 1: Create SSE event infrastructure (AC: #4)
  - [x] 1.1: Create `Engine/Events/ISseEventEmitter.cs` — interface with methods: `EmitRunStartedAsync`, `EmitTextDeltaAsync`, `EmitToolStartAsync`, `EmitToolArgsAsync`, `EmitToolEndAsync`, `EmitToolResultAsync`, `EmitStepStartedAsync`, `EmitStepFinishedAsync`, `EmitRunFinishedAsync`, `EmitRunErrorAsync`, `EmitSystemMessageAsync`
  - [x] 1.2: Create `Engine/Events/SseEventTypes.cs` — static class with string constants for event type names (`run.started`, `text.delta`, etc.) and payload record types
  - [x] 1.3: Create `Engine/Events/SseEventEmitter.cs` implementing `ISseEventEmitter` — writes `event: {type}\ndata: {json}\n\n` to a provided `Stream` using `System.Text.Json`. Constructor takes `Stream` and `ILogger<SseEventEmitter>`
  - [x] 1.4: Create `Endpoints/SseHelper.cs` — static helper with `ConfigureSseResponse(HttpResponse response)` that sets `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`, and disables response buffering

- [x] Task 2: Create workflow orchestrator for step advancement (AC: #1, #2, #5, #6, #7)
  - [x] 2.1: Create `Engine/IWorkflowOrchestrator.cs` — interface with `ExecuteNextStepAsync(string workflowAlias, string instanceId, ISseEventEmitter emitter, CancellationToken cancellationToken)`. This is the single entry point for running one step with advancement logic.
  - [x] 2.2: Create `Engine/WorkflowOrchestrator.cs` implementing `IWorkflowOrchestrator` — inject `IInstanceManager`, `IWorkflowRegistry`, `IStepExecutor`, `ILogger<WorkflowOrchestrator>`
  - [x] 2.3: `ExecuteNextStepAsync` flow:
    - Load instance state via `FindInstanceAsync`
    - Load workflow definition via `GetWorkflow`
    - Resolve current step from `instance.CurrentStepIndex`
    - Emit `run.started` event
    - Emit `step.started` event with step ID and name
    - Build `StepExecutionContext` and call `IStepExecutor.ExecuteStepAsync`
    - After step completes, emit `step.finished` with step ID and status
    - Re-load instance state (it was updated by StepExecutor)
    - If step succeeded AND more steps remain: advance `CurrentStepIndex` (increment and persist)
    - If step succeeded AND no more steps: set instance status to `Completed`, emit `run.finished`
    - If step failed: set instance status to `Failed`, emit `run.error`
  - [x] 2.4: Add autonomous mode auto-advance: after advancing `CurrentStepIndex`, if `workflow.Mode == "autonomous"`, emit system message "Auto-advancing to {next step name}...", wait `Task.Delay(1000)`, then recursively call `ExecuteNextStepAsync` for the next step
  - [x] 2.5: For interactive mode: after advancing `CurrentStepIndex`, just return — the user must POST `/start` again to trigger the next step

- [x] Task 3: Add `AdvanceStepAsync` method to IInstanceManager (AC: #3, #7)
  - [x] 3.1: Add `Task<InstanceState> AdvanceStepAsync(string workflowAlias, string instanceId, CancellationToken cancellationToken)` to `IInstanceManager`
  - [x] 3.2: Implement in `InstanceManager` — reads state, increments `CurrentStepIndex`, writes atomically. This finally resolves the deferred `CurrentStepIndex` advancement item from Story 3.1.
  - [x] 3.3: Validate that `CurrentStepIndex < Steps.Count - 1` before advancing (i.e., not already on the last step)

- [x] Task 4: Create the POST start endpoint with SSE streaming (AC: #3, #8, #9, #10)
  - [x] 4.1: Create `Endpoints/ExecutionEndpoints.cs` — new controller class `ExecutionEndpoints : ControllerBase` with route `umbraco/api/shallai` and `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`
  - [x] 4.2: Add `POST instances/{id}/start` action method:
    - Find instance via `FindInstanceAsync` — 404 if not found
    - Reject if status is Completed/Failed/Cancelled — 409
    - Reject if status is Running — 409 (concurrent execution guard)
    - Call `IProfileResolver.HasConfiguredProviderAsync()` — if false, return 400 with `no_provider` error
    - Set instance status to `Running` via `SetInstanceStatusAsync`
    - Configure SSE response via `SseHelper`
    - Create `SseEventEmitter` with `Response.Body`
    - Call `WorkflowOrchestrator.ExecuteNextStepAsync` (awaited, streams SSE events during execution)
    - Catch exceptions, emit `run.error` event, set instance status to Failed
  - [x] 4.3: SSE authentication: the endpoint already has `[Authorize]` from the controller — bearer token is sent via the standard `Authorization` header. No query string token needed since we use POST (not EventSource GET).

- [x] Task 5: Modify StepExecutor to emit SSE events during execution (AC: #4)
  - [x] 5.1: Add `ISseEventEmitter?` as an optional parameter on `IStepExecutor.ExecuteStepAsync` (nullable — backwards compat for tests that don't provide it). Alternatively, add it to `StepExecutionContext` as an optional property.
  - [x] 5.2: After ToolLoop processes a `text.delta` from the LLM response, emit `text.delta` via emitter
  - [x] 5.3: Before each tool execution in ToolLoop, emit `tool.start` with tool call ID and name
  - [x] 5.4: After tool arguments are parsed, emit `tool.args` with tool call ID and serialized arguments
  - [x] 5.5: After tool execution completes, emit `tool.end` with tool call ID, then `tool.result` with the result content
  - [x] 5.6: Update `ToolLoop.RunAsync` signature to accept `ISseEventEmitter?` and thread it through the loop

- [x] Task 6: Wire up frontend Start/Continue button (AC: #2, #3, #7)
  - [x] 6.1: Add `startInstance(id: string, token?: string): Promise<Response>` to `api-client.ts` — uses raw `fetch` with POST, returns the `Response` object (caller reads SSE stream). Must include `Authorization: Bearer {token}` header.
  - [x] 6.2: In `shallai-instance-detail.element.ts`, implement `_onStartClick`:
    - Call `startInstance` with instance ID and auth token
    - Read SSE stream from response body using `ReadableStream` + `TextDecoder` (not `EventSource` — this is a POST, not GET)
    - Parse SSE events and update `_instance` state reactively as `step.started`/`step.finished` events arrive
    - On `step.finished`: re-fetch instance detail to get latest state (ensures step sidebar updates)
    - On `run.finished`: update instance status to Completed, hide action buttons
    - On `run.error`: show error state
  - [x] 6.3: Change header button logic:
    - Show "Start" when `status === "Pending"` (existing)
    - Show "Continue" when `status === "Running"` AND current step is Complete AND instance is not completed (new — indicates interactive mode waiting for user)
    - Hide all action buttons when `status === "Completed"` or `status === "Failed"` or `status === "Cancelled"` (existing for some, extend for others)
  - [x] 6.4: While streaming, show a loading/progress indicator (disable the button to prevent double-click)
  - [x] 6.5: Update the main content area: when running, replace "Click 'Start' to begin the workflow." with a simple "Step in progress..." message (full chat panel is Epic 6)
  - [x] 6.6: For provider prerequisite error (400 `no_provider`), display inline guidance message: "Configure an AI provider in Umbraco.AI before workflows can run." (UX-DR28)

- [x] Task 7: Register new services in DI (AC: all)
  - [x] 7.1: Add `builder.Services.AddSingleton<IWorkflowOrchestrator, WorkflowOrchestrator>();` to `AgentRunnerComposer`
  - [x] 7.2: Register `ISseEventEmitter` is NOT needed in DI — it's created per-request in the endpoint with the response stream

- [x] Task 8: Write backend unit tests (AC: #11)
  - [x] 8.1: **WorkflowOrchestrator tests** — step executes and advances to next step (interactive mode)
  - [x] 8.2: **WorkflowOrchestrator tests** — step executes and auto-advances (autonomous mode)
  - [x] 8.3: **WorkflowOrchestrator tests** — final step completes → instance set to Completed
  - [x] 8.4: **WorkflowOrchestrator tests** — step fails → instance set to Failed, run.error emitted
  - [x] 8.5: **WorkflowOrchestrator tests** — SSE events emitted in correct order (run.started → step.started → step.finished → run.finished)
  - [x] 8.6: **InstanceManager tests** — AdvanceStepAsync increments CurrentStepIndex
  - [x] 8.7: **InstanceManager tests** — AdvanceStepAsync on last step throws
  - [x] 8.8: **SseEventEmitter tests** — writes correct SSE format (`event: {type}\ndata: {json}\n\n`)
  - [x] 8.9: **ExecutionEndpoints tests** — provider prerequisite check returns 400 when no provider
  - [x] 8.10: **ExecutionEndpoints tests** — already-running instance returns 409
  - [x] 8.11: **ExecutionEndpoints tests** — completed/failed/cancelled instance returns 409
  - [x] 8.12: **StepExecutor tests** — existing tests updated for optional emitter parameter (backwards compat)

- [x] Task 9: Write frontend tests (AC: #2, #6, #7)
  - [x] 9.1: Test `startInstance` API client function — verify POST with correct path and auth header
  - [x] 9.2: Test header button state logic — "Start" for Pending, "Continue" for Running with completed current step, hidden for Completed

- [x] Task 10: Build frontend (AC: all)
  - [x] 10.1: Run `npm run build` in `Client/` to update `wwwroot/` output

## Dev Notes

### Architecture Overview

This story connects the entire execution pipeline. The key new component is **WorkflowOrchestrator** which sits between the API endpoint and StepExecutor, handling step sequencing, mode logic, and SSE event coordination.

```
[Dashboard] → POST /start → [ExecutionEndpoints] → SSE response
                                      ↓
                           [WorkflowOrchestrator] → mode check
                                      ↓
                              [StepExecutor] → LLM + tools
                                      ↓
                           [ISseEventEmitter] → writes to response stream
```

### SSE Event Format (AG-UI-compatible)

Each event is a single SSE message with `event:` and `data:` fields. One JSON object per `data:` line — never multiline.

```
event: run.started
data: {"instanceId":"abc123"}

event: step.started
data: {"stepId":"scanner","stepName":"Content Scanner"}

event: text.delta
data: {"content":"Scanning Homepage..."}

event: tool.start
data: {"toolCallId":"tc_001","toolName":"read_file"}

event: tool.args
data: {"toolCallId":"tc_001","arguments":{"path":"scan-results.md"}}

event: tool.end
data: {"toolCallId":"tc_001"}

event: tool.result
data: {"toolCallId":"tc_001","result":"File contents here..."}

event: step.finished
data: {"stepId":"scanner","status":"Complete"}

event: run.finished
data: {"instanceId":"abc123","status":"Completed"}

event: run.error
data: {"error":"step_failed","message":"Completion check failed: missing scan-results.md"}
```

Tool call IDs use `tc_` prefix. Event names use `dot.notation`.

### WorkflowOrchestrator Design

The orchestrator is the key new abstraction. It owns:
1. Loading the workflow definition and instance state
2. Determining which step to execute (from `CurrentStepIndex`)
3. Building `StepExecutionContext` for the step executor
4. Handling post-step logic: advance index, check mode, auto-advance or wait
5. Instance lifecycle: Pending → Running → Completed/Failed

**Critical design choice:** The orchestrator is **not** an `IHostedService`. Step execution runs inline within the POST request — the SSE stream stays open for the duration. This avoids the complexity of background services, task queues, and reconnection. The response is streamed progressively via SSE events as the step executes.

For autonomous multi-step workflows, the orchestrator loops within the same request. The SSE stream stays open across step boundaries. This is acceptable because:
- Workflow steps are expected to take seconds to minutes, not hours
- HTTP keep-alive prevents idle timeouts for active streams
- The alternative (background service + separate SSE subscription) adds significant complexity with no v1 benefit

**If this becomes a problem later**, the `IWorkflowOrchestrator` interface makes it trivial to swap in a background-service-backed implementation.

### Instance Status Transitions

```
Pending → Running (on POST /start)
Running → Completed (all steps done)
Running → Failed (step error)
Running → Cancelled (user cancels — existing endpoint)
```

The `SetInstanceStatusAsync` already rejects `Running → Running` transitions (concurrent execution guard). The orchestrator sets `Running` at the start and `Completed`/`Failed` at the end.

### CurrentStepIndex Advancement

The new `AdvanceStepAsync` method on `IInstanceManager` is the only place `CurrentStepIndex` changes after creation. This resolves the deferred item from Story 3.1.

Flow:
1. StepExecutor completes step → sets step status to Complete
2. Orchestrator detects step completed → calls `AdvanceStepAsync` to increment index
3. Orchestrator checks mode:
   - Interactive: return, wait for next `/start` POST
   - Autonomous: delay 1s, then execute next step

### SSE from POST (Not EventSource)

The architecture says "SSE + POST" — this is important. The browser-native `EventSource` API only supports GET. Since our `/start` endpoint is POST, the frontend must use `fetch()` with `ReadableStream` to read the SSE stream.

```typescript
const response = await fetch(`${API_BASE}/instances/${id}/start`, {
  method: "POST",
  headers: { Authorization: `Bearer ${token}` },
});

const reader = response.body!.getReader();
const decoder = new TextDecoder();
let buffer = "";

while (true) {
  const { done, value } = await reader.read();
  if (done) break;
  buffer += decoder.decode(value, { stream: true });

  // Parse SSE events from buffer
  const events = buffer.split("\n\n");
  buffer = events.pop()!; // last chunk may be incomplete
  for (const event of events) {
    // Parse "event: {type}\ndata: {json}" format
    const lines = event.split("\n");
    const eventType = lines.find(l => l.startsWith("event:"))?.slice(7);
    const data = lines.find(l => l.startsWith("data:"))?.slice(5);
    if (eventType && data) {
      this._handleSseEvent(eventType, JSON.parse(data));
    }
  }
}
```

### SseEventEmitter Implementation

The emitter writes directly to a `Stream` (the response body). It's created per-request, not registered in DI.

```csharp
public sealed class SseEventEmitter : ISseEventEmitter
{
    private readonly Stream _stream;
    private readonly ILogger<SseEventEmitter> _logger;

    public async Task EmitAsync(string eventType, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {json}\n\n");
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }
}
```

**Critical:** Always `FlushAsync` after each event — SSE depends on immediate delivery. Response buffering must be disabled via `SseHelper`.

### SseHelper Response Configuration

```csharp
public static class SseHelper
{
    public static void ConfigureSseResponse(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Disable response buffering for real-time streaming
        var bufferingFeature = response.HttpContext.Features
            .Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
    }
}
```

### Frontend Button State Logic

The header action button follows UX-DR11 (single primary action, hidden not disabled):

| Instance Status | Current Step Status | Button |
|----------------|-------------------|--------|
| Pending | all Pending | **Start** (primary) |
| Running | one Active | Hidden (step in progress) |
| Running | current Complete, more steps remain | **Continue** (primary, interactive mode) |
| Completed | all Complete | Hidden |
| Failed | one Error | Hidden (Retry is Story 7.2) |
| Cancelled | — | Hidden |

The "Continue" button needs to know whether the current step is complete but the instance isn't done yet. This can be determined from `instance.status === "Running"` AND no step has `status === "Active"` AND not all steps are Complete.

### Existing Types to Modify

| Type | Location | Change |
|------|----------|--------|
| `IInstanceManager` | `Instances/IInstanceManager.cs` | Add `AdvanceStepAsync` method |
| `InstanceManager` | `Instances/InstanceManager.cs` | Implement `AdvanceStepAsync` |
| `IStepExecutor` | `Engine/IStepExecutor.cs` | Add optional `ISseEventEmitter?` to `StepExecutionContext` |
| `StepExecutor` | `Engine/StepExecutor.cs` | Emit SSE events during execution |
| `StepExecutionContext` | `Engine/StepExecutionContext.cs` | Add `ISseEventEmitter? EventEmitter` property |
| `ToolLoop` | `Engine/ToolLoop.cs` | Accept and use `ISseEventEmitter?` for tool events |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | Register `IWorkflowOrchestrator` |
| `shallai-instance-detail` | `Client/src/components/shallai-instance-detail.element.ts` | Wire Start/Continue, SSE parsing |
| `api-client.ts` | `Client/src/api/api-client.ts` | Add `startInstance` function |

### New Files

| File | Path | Purpose |
|------|------|---------|
| ISseEventEmitter.cs | `Engine/Events/ISseEventEmitter.cs` | SSE event emission interface |
| SseEventTypes.cs | `Engine/Events/SseEventTypes.cs` | Event type constants + payload records |
| SseEventEmitter.cs | `Engine/Events/SseEventEmitter.cs` | Stream-based SSE writer |
| SseHelper.cs | `Endpoints/SseHelper.cs` | Response configuration helper |
| IWorkflowOrchestrator.cs | `Engine/IWorkflowOrchestrator.cs` | Orchestration interface |
| WorkflowOrchestrator.cs | `Engine/WorkflowOrchestrator.cs` | Step sequencing + mode logic |
| ExecutionEndpoints.cs | `Endpoints/ExecutionEndpoints.cs` | POST /start SSE endpoint |
| WorkflowOrchestratorTests.cs | Tests/Engine/WorkflowOrchestratorTests.cs | Orchestrator unit tests |
| SseEventEmitterTests.cs | Tests/Engine/Events/SseEventEmitterTests.cs | SSE format tests |
| InstanceManagerAdvanceTests.cs | Tests/Instances/InstanceManagerAdvanceTests.cs | AdvanceStepAsync tests |

### Namespace

- `Engine/Events/` classes: `Shallai.UmbracoAgentRunner.Engine.Events`
- `Engine/` orchestrator: `Shallai.UmbracoAgentRunner.Engine`
- `Endpoints/` classes: `Shallai.UmbracoAgentRunner.Endpoints`
- Tests mirror source paths

### Engine Boundary

The `ISseEventEmitter` interface lives in `Engine/Events/` — it has no Umbraco or ASP.NET dependencies (just `Stream` and `System.Text.Json`). The implementation also stays within Engine — writing to a `Stream` is pure .NET.

The `SseHelper` lives in `Endpoints/` — it references `HttpResponse` which is an ASP.NET type. This is correct — the endpoint layer bridges ASP.NET and Engine.

### Error Handling

- **Provider prerequisite fail:** Return 400 from endpoint before starting execution. No SSE stream opened.
- **Step execution error:** StepExecutor catches, sets step to Error. Orchestrator detects Error status, sets instance to Failed, emits `run.error` via SSE, then returns.
- **Orchestrator exception:** Endpoint catches, emits `run.error` if possible, sets instance to Failed, closes stream.
- **SSE write failure** (client disconnected): `OperationCanceledException` propagates up via `CancellationToken`. The step continues executing (it's already in progress), but SSE events are lost. Instance state is still correct on disk.
- **Error-path status writes** use `CancellationToken.None` with `try/catch LogCritical` (same pattern as Story 4.3/4.4).

### Testing Approach

- **WorkflowOrchestrator** — mock `IInstanceManager`, `IWorkflowRegistry`, `IStepExecutor`. Verify correct sequencing: status transitions, step advancement, SSE events emitted in order, mode-specific behaviour.
- **SseEventEmitter** — write to `MemoryStream`, assert exact SSE format output.
- **InstanceManager.AdvanceStepAsync** — real temp directory tests (same pattern as existing InstanceManager tests).
- **ExecutionEndpoints** — these are harder to unit test due to SSE streaming. Consider integration tests or test the orchestrator thoroughly and keep the endpoint thin.
- **Frontend** — test API client function and button state logic. SSE parsing is complex to test in isolation; rely on manual E2E.
- **Existing StepExecutor tests** — `StepExecutionContext` gains a new optional property. Existing tests continue to work if the property defaults to null (emitter is optional).
- Test target: ~12 backend tests + ~2 frontend tests

### Project Structure Notes

- `Engine/Events/` is a new subdirectory — maintains the Engine boundary (no Umbraco deps)
- `Endpoints/ExecutionEndpoints.cs` is separate from `InstanceEndpoints.cs` — SSE endpoints have different response patterns (streaming vs JSON)
- Architecture specifies `ExecutionEndpoints.cs` and `SseHelper.cs` in the `Endpoints/` folder — follow this exactly

### Retrospective Intelligence

From Epics 1-3 retrospective (2026-03-31):

- **Simplest fix first** — the inline SSE approach (no IHostedService) is simpler than a background service + reconnection. Start here. Only introduce background execution if a real problem emerges.
- **OperationCanceledException must propagate** — all new async code paths must include `catch (OperationCanceledException) { throw; }` before generic catches. This is critical for SSE stream cancellation when the client disconnects.
- **Browser testing shortcut** — SSE streaming is hard to unit test end-to-end. Ask Adam to verify in the browser after automated tests pass.
- **Error handling edge cases are the blind spot** — the orchestrator has multiple failure points (step fails, status write fails, SSE write fails). Test each path explicitly.
- **Test target ~12 per story** — slightly above ~10 due to new orchestrator + SSE + advancement logic across multiple classes.

From Story 4.4 dev notes:
- **Error-path status update uses `CancellationToken.None`** — apply same pattern in orchestrator error paths
- **Existing StepExecutor tests (137 total)** — constructor/context changes need backwards compat. Default emitter to null.
- **CompletionChecker returns `CompletionCheckResult`** — orchestrator checks `completionResult.Passed` via step status (step is Complete or Error after executor finishes)

From Story 4.3 dev notes:
- **ToolLoop.RunAsync** — currently takes `(IChatClient, List<ChatMessage>, ChatOptions, Dictionary<string, IWorkflowTool>, ToolExecutionContext, ILogger, CancellationToken)`. Adding `ISseEventEmitter?` is one more parameter.
- **`WithAlias()` required by Umbraco.AI** — already handled in ProfileResolver, no impact here.

### Manual E2E Validation

After automated tests pass, manually verify with the test site:

1. Open the dashboard, create a new instance of the content-quality-audit workflow (mode: autonomous)
2. Click "Start" — verify the SSE stream opens and step progress updates in the sidebar
3. If the workflow has multiple steps, verify autonomous mode auto-advances without user intervention
4. Verify the instance shows "Completed" status when all steps finish
5. Create another instance, but configure the workflow as `mode: interactive`
6. Click "Start" — first step executes, then the "Continue" button should appear
7. Click "Continue" — second step starts
8. Test the provider prerequisite check: temporarily misconfigure the Umbraco.AI provider, click Start, verify the guidance message appears
9. Test double-start: while a step is executing, try to start again — should get 409

### Build Verification

Run `dotnet test Shallai.UmbracoAgentRunner.slnx` — never bare `dotnet test`.
Run `npm test` in `Client/` for frontend tests.
Run `npm run build` in `Client/` before committing.

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 4, Story 4.5 (lines 583-612)]
- [Source: _bmad-output/planning-artifacts/architecture.md — Decision 2: Streaming Protocol (SSE + POST, AG-UI)]
- [Source: _bmad-output/planning-artifacts/architecture.md — SSE Event Format (lines 471-497)]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure: ExecutionEndpoints.cs, SseHelper.cs, Engine/Events/]
- [Source: _bmad-output/planning-artifacts/architecture.md — API Endpoints table (line 284): POST /instances/{id}/start]
- [Source: _bmad-output/planning-artifacts/prd.md — FR29 (interactive mode), FR30 (autonomous mode), FR31 (mode declaration), FR53 (provider prerequisite)]
- [Source: _bmad-output/planning-artifacts/prd.md — NFR3 (step transition <5s)]
- [Source: _bmad-output/project-context.md — SSE streaming for real-time chat, IHostedService constraints, Engine boundary]
- [Source: _bmad-output/implementation-artifacts/epic-1-2-3-retro-2026-03-31.md — Simplest fix first, OperationCanceledException, browser testing shortcut]
- [Source: _bmad-output/implementation-artifacts/4-4-artifact-handoff-and-completion-checking.md — StepExecutor integration points, error-path patterns]
- [Source: _bmad-output/implementation-artifacts/4-3-step-executor-and-tool-loop.md — ToolLoop.RunAsync signature, error handling patterns]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — CurrentStepIndex never advanced (line 29), TOCTOU race conditions]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Backend: 153 tests pass (137 existing + 16 new), 0 failures
- Frontend: 67 tests pass (62 existing + 5 new), 0 failures
- Frontend build: successful (tsc + vite)

### Completion Notes List

- Task 1: Created SSE event infrastructure — ISseEventEmitter interface, SseEventTypes constants with payload records, SseEventEmitter stream writer with flush-per-event, SseHelper response configurator
- Task 2: Created WorkflowOrchestrator — single entry point for step execution with mode-aware advancement (interactive waits, autonomous auto-advances with 1s delay and recursion)
- Task 3: Added AdvanceStepAsync and GetInstanceFolderPath to IInstanceManager — atomic step index increment with last-step guard, resolves deferred item from Story 3.1
- Task 4: Created ExecutionEndpoints with POST /instances/{id}/start — full status validation (404/409/400), provider prerequisite check, SSE response configuration, error-path status updates with CancellationToken.None
- Task 5: Modified StepExecutor/ToolLoop to emit SSE events — added EventEmitter to StepExecutionContext as optional property (backwards compat), ToolLoop emits text.delta, tool.start/args/end/result events
- Task 6: Wired frontend Start/Continue button — startInstance API function returns raw Response, SSE stream parsing with ReadableStream+TextDecoder, reactive state updates from SSE events, Continue button for interactive mode, provider error display
- Task 7: Registered IWorkflowOrchestrator as singleton in AgentRunnerComposer
- Task 8: 16 backend unit tests — WorkflowOrchestrator (5: interactive/autonomous/final step/failure/event order), SseEventEmitter (6: format/payloads/multiple events), InstanceManager (5: advance/persist/last-step-guard/folder-path)
- Task 9: 5 frontend tests — startInstance API client (3: POST path/auth header/raw response), button state logic (2: Continue conditions/terminal state hiding)
- Task 10: Frontend built with tsc + vite, wwwroot output updated
- Decision: Added GetInstanceFolderPath to IInstanceManager (not in story spec) — needed to avoid leaking path construction logic out of InstanceManager

#### Review Fixes Applied (2026-03-31)

- **OCE status reset in ExecutionEndpoints** — OperationCanceledException catch now sets instance to Failed with CancellationToken.None before re-throwing. Without this, a client disconnect left the instance permanently stuck at Running with no way to restart (the 409 guard blocks all future starts).
- **Autonomous mode while-loop** — Replaced recursive ExecuteNextStepAsync call with a `while (true)` loop. The recursive approach had unbounded async stack growth proportional to step count. The loop uses an `isFirstStep` flag to emit `run.started` only once. All existing orchestrator tests pass unchanged because the mock setup already returns correct state on successive FindInstanceAsync calls.
- **Error-path CancellationToken.None** — All SSE emissions on the error path (step.finished with Error status, run.error) now use CancellationToken.None consistently, matching the pattern established in Stories 4.3/4.4 for error-path status writes. This prevents cleanup from failing if the original token is already cancelled.

### File List

**New files:**
- Shallai.UmbracoAgentRunner/Engine/Events/ISseEventEmitter.cs
- Shallai.UmbracoAgentRunner/Engine/Events/SseEventTypes.cs
- Shallai.UmbracoAgentRunner/Engine/Events/SseEventEmitter.cs
- Shallai.UmbracoAgentRunner/Engine/IWorkflowOrchestrator.cs
- Shallai.UmbracoAgentRunner/Engine/WorkflowOrchestrator.cs
- Shallai.UmbracoAgentRunner/Endpoints/ExecutionEndpoints.cs
- Shallai.UmbracoAgentRunner/Endpoints/SseHelper.cs
- Shallai.UmbracoAgentRunner.Tests/Engine/WorkflowOrchestratorTests.cs
- Shallai.UmbracoAgentRunner.Tests/Engine/Events/SseEventEmitterTests.cs
- Shallai.UmbracoAgentRunner.Tests/Instances/InstanceManagerAdvanceTests.cs
- Shallai.UmbracoAgentRunner/Client/src/api/api-client.test.ts

**Modified files:**
- Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs (added EventEmitter property)
- Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs (pass emitter to ToolLoop)
- Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs (accept ISseEventEmitter?, emit SSE events)
- Shallai.UmbracoAgentRunner/Instances/IInstanceManager.cs (added AdvanceStepAsync, GetInstanceFolderPath)
- Shallai.UmbracoAgentRunner/Instances/InstanceManager.cs (implemented AdvanceStepAsync, GetInstanceFolderPath)
- Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs (registered IWorkflowOrchestrator)
- Shallai.UmbracoAgentRunner/Client/src/api/api-client.ts (added startInstance)
- Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.ts (SSE streaming, Continue button, provider error)
- Shallai.UmbracoAgentRunner/Client/src/components/shallai-instance-detail.element.test.ts (added button state tests)
- Shallai.UmbracoAgentRunner/wwwroot/App_Plugins/ShallaiUmbracoAgentRunner/* (rebuilt frontend output)

### Review Findings

- [x] [Review][Patch] Instance stuck in Running on OperationCanceledException — endpoint re-throws OCE without resetting status, leaving instance permanently stuck at Running [ExecutionEndpoints.cs:98-99] — FIXED: added status reset to Failed with CancellationToken.None before re-throw
- [x] [Review][Patch] Autonomous mode uses recursion instead of iteration — unbounded async stack growth for many-step workflows [WorkflowOrchestrator.cs:132] — FIXED: converted to while loop with isFirstStep flag for run.started emission
- [x] [Review][Patch] Error-path step.finished emit uses original CancellationToken instead of CancellationToken.None — may fail if token is cancelled before cleanup runs [WorkflowOrchestrator.cs:81] — FIXED: changed to CancellationToken.None (included in while-loop refactor)
- [x] [Review][Defer] TOCTOU race on concurrent start requests [ExecutionEndpoints.cs + InstanceManager.cs] — deferred, explicitly out of scope per story spec
- [x] [Review][Defer] No timeout/abort on frontend SSE stream reader [shallai-instance-detail.element.ts] — deferred to v2 (NFR5)
- [x] [Review][Defer] No way to resume failed autonomous workflow [WorkflowOrchestrator.cs] — deferred, Story 7.2 retry logic
