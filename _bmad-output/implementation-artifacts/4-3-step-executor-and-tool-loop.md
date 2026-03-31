# Story 4.3: Step Executor & Tool Loop

Status: done

## Story

As a developer,
I want the engine to execute a step by calling IAIChatService with the assembled prompt and handling tool calls in a loop,
so that agents can use tools iteratively until their task is complete.

## Acceptance Criteria

1. **Given** a prompt has been assembled and a profile resolved for a step, **when** the StepExecutor executes the step, **then** it calls `GetChatClientAsync(profileId)` to obtain an `IChatClient` with the full Umbraco.AI middleware pipeline (NFR16)
2. It sends the assembled prompt as messages with declared tools passed via `ChatOptions.Tools`
3. When the response contains `FunctionCallContent`, each tool call name is validated against the step's declared tools list (NFR11)
4. Undeclared tool call attempts are rejected with an error logged and error result returned to the LLM (FR59)
5. Valid tool calls are dispatched to the corresponding `IWorkflowTool` implementation via the ToolLoop
6. Tool results are sent back as `FunctionResultContent` and the loop continues until no tool calls remain in the response
7. Step execution runs as a background task via `IHostedService`, never blocking the web request pipeline (FR15, NFR4)
8. All exceptions during step execution are caught, logged, and surfaced as step errors — never crashing the host application (NFR23)
9. Tool execution errors are returned to the LLM as error results, not thrown — allowing the LLM to decide recovery strategy
10. The `IStepExecutor` interface wraps the implementation for future swapability
11. Structured logging uses consistent field names: `WorkflowAlias`, `InstanceId`, `StepId`, `ToolName`
12. Unit tests verify the tool loop with mocked `IChatClient`, tool call validation, undeclared tool rejection, and error handling

## What NOT to Build

- No `IWorkflowTool` implementations (Epic 5 — `ReadFileTool`, `WriteFileTool`, etc.)
- No completion checking or artifact validation (Story 4.4)
- No workflow mode logic (interactive vs autonomous), no step advancement (Story 4.5)
- No SSE streaming or event emission to the dashboard (Story 4.5 / Epic 6)
- No conversation persistence to JSONL (Story 6.1)
- No retry logic for failed steps (Story 7.2)
- No `IHostedService` registration — the background service hosting is Story 4.5's scope. This story builds the `IStepExecutor` that the hosted service will call.
- No dashboard UI changes
- No per-instance `SemaphoreSlim` locking (deferred from 3.1 — address in Story 4.5 when the hosted service introduces concurrent callers)

## Tasks / Subtasks

- [x] Task 1: Create `IWorkflowTool` interface and `ToolExecutionContext` (AC: #5, #9)
  - [x]1.1: Create `Tools/IWorkflowTool.cs` with `Name`, `Description` properties and `ExecuteAsync` method
  - [x]1.2: Create `Tools/ToolExecutionContext.cs` as a record with `InstanceFolderPath`, `InstanceId`, `StepId`, `CancellationToken`

- [x] Task 2: Create `ToolLoop` in Engine/ (AC: #2-#6, #9, #11)
  - [x]2.1: Create `Engine/ToolLoop.cs` with method `RunAsync(IChatClient client, IList<ChatMessage> messages, ChatOptions options, IReadOnlyDictionary<string, IWorkflowTool> declaredTools, ToolExecutionContext context, CancellationToken cancellationToken)`
  - [x]2.2: Send messages via `client.GetResponseAsync(messages, options, cancellationToken)`
  - [x]2.3: Inspect response for `FunctionCallContent` items in the response message content
  - [x]2.4: For each `FunctionCallContent`: validate `Name` against `declaredTools` keys — if not found, log warning and return `FunctionResultContent` with error message `"Tool '{name}' is not declared for this step"` (FR59)
  - [x]2.5: For valid tool calls: call `tool.ExecuteAsync(arguments, context, cancellationToken)`, wrap result as `FunctionResultContent`
  - [x]2.6: If tool throws, catch exception and return `FunctionResultContent` with error message — do NOT rethrow (AC #9)
  - [x]2.7: Add assistant message (with tool calls) and tool result messages to conversation, then loop back to `GetResponseAsync`
  - [x]2.8: When response contains no `FunctionCallContent`, return the final `ChatResponse`
  - [x]2.9: Structured logging for each tool call: `"Tool {ToolName} executing for step {StepId} in instance {InstanceId}"`

- [x] Task 3: Create `IStepExecutor` interface (AC: #10)
  - [x]3.1: Create `Engine/IStepExecutor.cs` with method `ExecuteStepAsync(StepExecutionContext context, CancellationToken cancellationToken)`
  - [x]3.2: Create `Engine/StepExecutionContext.cs` as a record with all fields needed: `WorkflowDefinition`, `StepDefinition`, `InstanceState`, `InstanceFolderPath`, `WorkflowFolderPath`

- [x] Task 4: Implement `StepExecutor` (AC: #1-#11)
  - [x]4.1: Create `Engine/StepExecutor.cs` implementing `IStepExecutor`
  - [x]4.2: Constructor injects: `IProfileResolver`, `IPromptAssembler`, `IEnumerable<IWorkflowTool>`, `IInstanceManager`, `ILogger<StepExecutor>`
  - [x]4.3: In `ExecuteStepAsync`: update step status to `Active` via `IInstanceManager.UpdateStepStatusAsync`
  - [x]4.4: Resolve `IChatClient` via `IProfileResolver.ResolveAndGetClientAsync(step, workflow, ct)`
  - [x]4.5: Assemble prompt via `IPromptAssembler.AssemblePromptAsync`
  - [x]4.6: Build tool dictionary: from injected `IEnumerable<IWorkflowTool>`, filter to only those whose `Name` appears in `step.Tools` list
  - [x]4.7: Wrap each matching `IWorkflowTool` as `AIFunction` via `AIFunctionFactory.Create()` — pass as `ChatOptions.Tools`
  - [x]4.8: Build initial `ChatMessage` list: system message with assembled prompt
  - [x]4.9: Call `ToolLoop.RunAsync` with the client, messages, options, and declared tool dictionary
  - [x]4.10: On success: update step status to `Complete`
  - [x]4.11: Wrap entire execution in try/catch — on failure: log error, update step status to `Error` (AC #8)
  - [x]4.12: Structured logging at each lifecycle point: step started, profile resolved, prompt assembled, tool loop complete, step completed/failed

- [x] Task 5: Register in DI (AC: #10)
  - [x]5.1: Add `builder.Services.AddSingleton<IStepExecutor, StepExecutor>();` to `AgentRunnerComposer`
  - [x]5.2: Uncomment and update the `IWorkflowTool` registration comment — leave placeholder for Epic 5

- [x] Task 6: Write unit tests (AC: #12)
  - [x]6.1: **ToolLoop tests** — tool call detected and dispatched to correct `IWorkflowTool`
  - [x]6.2: **ToolLoop tests** — undeclared tool call returns error result to LLM, not thrown
  - [x]6.3: **ToolLoop tests** — tool execution error caught and returned as error result
  - [x]6.4: **ToolLoop tests** — loop continues until response has no tool calls
  - [x]6.5: **ToolLoop tests** — multiple tool calls in single response all dispatched
  - [x]6.6: **StepExecutor tests** — successful execution updates step status Active → Complete
  - [x]6.7: **StepExecutor tests** — exception during execution sets step status to Error
  - [x]6.8: **StepExecutor tests** — only declared tools (from step.Tools) passed to ToolLoop
  - [x]6.9: **StepExecutor tests** — profile resolved before prompt assembled before tool loop
  - [x]6.10: **StepExecutor tests** — structured logging includes WorkflowAlias, InstanceId, StepId
  - [x]6.11: **StepExecutor tests** — cancellation token propagated through all async calls

## Dev Notes

### Microsoft.Extensions.AI Types for the Tool Loop

The tool loop uses types from `Microsoft.Extensions.AI` (transitive via Umbraco.AI). Key types:

```csharp
// Sending messages and getting responses
IChatClient.GetResponseAsync(IList<ChatMessage> messages, ChatOptions? options, CancellationToken ct)

// Building messages
new ChatMessage(ChatRole.System, "prompt content")
new ChatMessage(ChatRole.Assistant, content)  // assistant message with tool calls
new ChatMessage(ChatRole.Tool, content)       // tool result message

// Tool calls in responses — inspect ChatResponse.Messages content items
FunctionCallContent  // found in message.Contents — has Name, CallId, Arguments
FunctionResultContent  // what you send back — has CallId, Result

// Declaring tools
AIFunctionFactory.Create(Delegate method, string name, string description)
ChatOptions { Tools = new List<AITool> { aiFunction1, aiFunction2 } }
```

**Critical:** `FunctionCallContent` and `FunctionResultContent` are content items within `ChatMessage.Contents` (an `IList<AIContent>`). They are NOT separate message types. Inspect `response.Messages` → each message's `.Contents` list → filter for `FunctionCallContent`.

**Tool wrapping pattern:**
```csharp
// For each IWorkflowTool, create an AIFunction
var aiFunction = AIFunctionFactory.Create(
    async (IDictionary<string, object?> arguments) =>
        await tool.ExecuteAsync(arguments, context, cancellationToken),
    tool.Name,
    tool.Description);
```

**Important:** `AIFunctionFactory.Create` accepts a `Delegate`. The exact overload depends on the M.E.AI version. If the delegate-based approach doesn't compile, use `AIFunctionFactory.Create(MethodInfo, object?)` with a wrapper method, or construct `AIFunction` directly. The dev agent should verify the available overloads by checking the actual API surface of the installed package.

### IWorkflowTool Interface (Architecture Decision 3)

The architecture specifies this interface:

```csharp
public interface IWorkflowTool
{
    string Name { get; }
    string Description { get; }
    Task<object> ExecuteAsync(IDictionary<string, object?> arguments,
        ToolExecutionContext context, CancellationToken cancellationToken);
}
```

`ToolExecutionContext` provides: instance folder path, instance ID, step ID. Tools receive only what they need — no access to engine internals.

This story creates the interface and context type only. No tool implementations (Epic 5).

### ToolLoop Is a Separate Class from StepExecutor

The architecture defines `Engine/ToolLoop.cs` as its own class — not a method on StepExecutor. This keeps the tool dispatch/validation logic isolated and testable. ToolLoop can be a static class or a service — keep it simple. It has no injected dependencies; it receives everything it needs via parameters.

### StepExecutor Does Not Own the Background Service

The architecture places step execution inside `IHostedService` (FR15), but the hosted service is Story 4.5's scope. This story builds `IStepExecutor` as a synchronous-from-the-caller's-perspective service. Story 4.5 will create the `StepExecutionHostedService` that calls `IStepExecutor.ExecuteStepAsync` on a background thread.

The `StepExecutor` itself is purely async — it awaits the tool loop. The "background" aspect comes from who calls it, not from the executor itself.

### Step Status Transitions in This Story

The StepExecutor manages these transitions via `IInstanceManager.UpdateStepStatusAsync`:

1. **On start:** Step status → `Active` (step index from `StepExecutionContext`)
2. **On success:** Step status → `Complete`
3. **On failure:** Step status → `Error`

Do NOT advance `CurrentStepIndex` — that's Story 4.5 workflow mode logic.

### Existing Types to Use

| Type | Location | Usage |
|------|----------|-------|
| `IProfileResolver` | `Engine/IProfileResolver.cs` | `ResolveAndGetClientAsync(step, workflow, ct)` returns `IChatClient` |
| `IPromptAssembler` | `Engine/IPromptAssembler.cs` | `AssemblePromptAsync(context, ct)` returns assembled prompt string |
| `PromptAssemblyContext` | `Engine/PromptAssemblyContext.cs` | Record with: `WorkflowFolderPath`, `Step`, `AllSteps`, `AllStepDefinitions`, `InstanceFolderPath`, `DeclaredTools` |
| `ToolDescription` | `Engine/ToolDescription.cs` | Record: `(string Name, string Description)` — used by PromptAssemblyContext |
| `IInstanceManager` | `Instances/IInstanceManager.cs` | `UpdateStepStatusAsync(workflowAlias, instanceId, stepIndex, status, ct)` |
| `InstanceState` | `Instances/InstanceState.cs` | Has `Steps[]`, `WorkflowAlias`, `InstanceId`, `CurrentStepIndex` |
| `StepState` | `Instances/InstanceState.cs` | Has `Id`, `Status`, `StartedAt`, `CompletedAt` |
| `StepStatus` | `Instances/StepStatus.cs` | Enum: `Pending`, `Active`, `Complete`, `Error` |
| `StepDefinition` | `Workflows/StepDefinition.cs` | Has `Id`, `Name`, `Agent`, `Profile`, `Tools` (List<string>?), `ReadsFrom`, `WritesTo` |
| `WorkflowDefinition` | `Workflows/WorkflowDefinition.cs` | Has `Name`, `DefaultProfile`, `Steps`, `Mode` |
| `AgentRunnerException` | `Engine/AgentRunnerException.cs` | Base exception |
| `ProfileNotFoundException` | `Engine/ProfileNotFoundException.cs` | Thrown by ProfileResolver |
| `AgentRunnerComposer` | `Composers/AgentRunnerComposer.cs` | DI registration — has placeholder comments for StepExecutor and IWorkflowTool |

### StepExecutionContext Design

Create a new record to pass all context the executor needs:

```csharp
public sealed record StepExecutionContext(
    WorkflowDefinition Workflow,
    StepDefinition Step,
    InstanceState Instance,
    string InstanceFolderPath,
    string WorkflowFolderPath);
```

The executor derives what it needs from this — no need to pass individual fields.

### Tool Filtering Logic

`StepDefinition.Tools` is `List<string>?` — it can be null (no tools declared). When null or empty, pass an empty tools list to `ChatOptions.Tools` and an empty dictionary to ToolLoop. The step runs without tools.

Filtering from `IEnumerable<IWorkflowTool>`:

```csharp
var declaredToolNames = step.Tools ?? [];
var toolDict = allTools
    .Where(t => declaredToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
    .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
```

### Error Handling Strategy

Three levels of error handling:

1. **Tool execution errors** (inside ToolLoop): Catch exception from `tool.ExecuteAsync`, return `FunctionResultContent` with error message. Do NOT rethrow. The LLM decides how to recover. Log at Warning level.

2. **Undeclared tool calls** (inside ToolLoop): Return `FunctionResultContent` with error message `"Tool '{name}' is not declared for this step"`. Log at Warning level with `ToolName` field.

3. **Step-level errors** (inside StepExecutor): Catch ALL exceptions (except `OperationCanceledException` — rethrow). Log at Error level. Update step status to `Error`. This is the safety net that prevents host crashes (NFR23).

### Testing Approach

- **Mock `IChatClient`** using NSubstitute — return `ChatResponse` with/without `FunctionCallContent` in messages
- **Mock `IWorkflowTool`** — verify dispatch with correct arguments, simulate errors
- **Mock `IProfileResolver`** — return substitute `IChatClient`
- **Mock `IPromptAssembler`** — return a test prompt string
- **Mock `IInstanceManager`** — verify `UpdateStepStatusAsync` calls with correct status transitions
- Use `NullLogger<T>.Instance` for loggers
- **Test ToolLoop separately from StepExecutor** — ToolLoop tests focus on the dispatch/validation loop, StepExecutor tests focus on orchestration

**Mocking `ChatResponse`:** `ChatResponse` may not be easily constructable. Check if it has a public constructor or if you need to use `ChatCompletion` from M.E.AI. The dev agent should inspect the actual type to determine the best approach. If construction is difficult, create a helper method in test fixtures.

### Namespace

- `IStepExecutor`, `StepExecutor`, `StepExecutionContext`, `ToolLoop`: `Shallai.UmbracoAgentRunner.Engine`
- `IWorkflowTool`, `ToolExecutionContext`: `Shallai.UmbracoAgentRunner.Tools`
- Tests: `Shallai.UmbracoAgentRunner.Tests.Engine`

### File Placement

| File | Path | Action |
|------|------|--------|
| IWorkflowTool.cs | `Shallai.UmbracoAgentRunner/Tools/IWorkflowTool.cs` | New |
| ToolExecutionContext.cs | `Shallai.UmbracoAgentRunner/Tools/ToolExecutionContext.cs` | New |
| ToolLoop.cs | `Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs` | New |
| IStepExecutor.cs | `Shallai.UmbracoAgentRunner/Engine/IStepExecutor.cs` | New |
| StepExecutionContext.cs | `Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs` | New |
| StepExecutor.cs | `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs` | New |
| AgentRunnerComposer.cs | `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` | Modified (add DI registrations) |
| ToolLoopTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/ToolLoopTests.cs` | New |
| StepExecutorTests.cs | `Shallai.UmbracoAgentRunner.Tests/Engine/StepExecutorTests.cs` | New |

### Project Structure Notes

- `Tools/` folder exists in the architecture but has no files yet. This story creates the first two files there.
- `Engine/ToolLoop.cs` is specified in the architecture's project structure.
- `Engine/Events/` folder (for SSE) is NOT created in this story — that's Story 4.5.
- All Engine/ files remain zero-Umbraco-dependency (ToolLoop, StepExecutor, StepExecutionContext use only M.E.AI types). `ProfileResolver` remains the only documented exception.

### Retrospective Intelligence

From Epics 1-3 retrospective (2026-03-31):

- **Simplest fix first** — don't overcomplicate the tool loop with abstract patterns. A while loop checking for `FunctionCallContent` is sufficient.
- **OperationCanceledException must propagate** — learned in Story 4.2 review. Always add explicit `catch (OperationCanceledException) { throw; }` before generic catch blocks.
- **Test target ~11 per story** — the 11 test cases above align with the ~10 guideline.
- **Live provider testing** — after automated tests, manually verify with a real Anthropic provider in the test site. This is the first story where the LLM is actually called.
- **Deferred items from earlier stories** relevant here:
  - `CurrentStepIndex` never advanced (3.1) — do NOT advance it here, Story 4.5 owns this
  - No state machine enforcement (3.1) — accepted for now, StepExecutor just calls UpdateStepStatusAsync
  - Per-instance locking (3.1) — not needed until Story 4.5 introduces the hosted service with concurrent callers

### Manual E2E Validation

After automated tests pass, manually verify with the test site:

1. Ensure test site has an Anthropic profile configured (e.g., "anthropic-claude")
2. Write a minimal test harness (temporary controller or console command) that:
   - Creates an instance via `IInstanceManager`
   - Calls `IStepExecutor.ExecuteStepAsync` with a real workflow step
   - Verifies the LLM responds and tool calls are dispatched
3. This is the first story where the full LLM round-trip is exercised — pay close attention to:
   - Does `IChatClient.GetResponseAsync` return `FunctionCallContent` correctly?
   - Are tool arguments properly deserialized into `IDictionary<string, object?>`?
   - Does the conversation flow (messages list) accumulate correctly across tool call rounds?
4. If no tool implementations exist yet, use a simple stub tool registered in the test site for validation

### Build Verification

Run `dotnet test Shallai.UmbracoAgentRunner.slnx` — never bare `dotnet test`.

### References

- [Source: _bmad-output/planning-artifacts/architecture.md — Decision 1: Step Executor Integration Layer]
- [Source: _bmad-output/planning-artifacts/architecture.md — Decision 3: Tool System Interface]
- [Source: _bmad-output/planning-artifacts/architecture.md — Implementation Patterns: Error Handling]
- [Source: _bmad-output/planning-artifacts/architecture.md — Project Structure: Engine/ToolLoop.cs, Engine/StepExecutor.cs]
- [Source: _bmad-output/planning-artifacts/epics.md — Epic 4, Story 4.3]
- [Source: _bmad-output/planning-artifacts/prd.md — FR11-16, FR17-24, FR59, NFR4, NFR11, NFR16, NFR23]
- [Source: _bmad-output/project-context.md — IAIChatService rules, Engine boundary, tool error handling]
- [Source: _bmad-output/implementation-artifacts/epic-1-2-3-retro-2026-03-31.md — Process rules, OperationCanceledException lesson]
- [Source: _bmad-output/implementation-artifacts/4-2-profile-resolution.md — IAIChatService API: CreateChatClientAsync, not GetChatClientAsync]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md — CurrentStepIndex, state machine, per-instance locking]

### Review Findings

- [x] [Review][Patch] `FindIndex` returns -1 with no guard — stepIndex passed to `UpdateStepStatusAsync` unchecked [StepExecutor.cs:34] — fixed: throws `AgentRunnerException` on -1
- [x] [Review][Patch] No iteration cap on `while (true)` in ToolLoop — LLM misbehaviour causes infinite loop [ToolLoop.cs:17] — fixed: `MaxIterations = 100` constant, throws `AgentRunnerException`
- [x] [Review][Patch] `WorkflowAlias` missing from `ToolExecutionContext` — ToolLoop cannot log it per AC #11 [ToolExecutionContext.cs, ToolLoop.cs] — fixed: added `WorkflowAlias` to record, all ToolLoop log messages updated
- [x] [Review][Patch] Error catch block uses original `cancellationToken` (may be cancelled) and no try/catch on status update [StepExecutor.cs:127-128] — fixed: uses `CancellationToken.None`, wrapped in try/catch with `LogCritical`
- [x] [Review][Patch] Missing ToolLoop `OperationCanceledException` propagation test per AC #12 [ToolLoopTests.cs] — fixed: added `OperationCanceledException_PropagatesFromTool` + `ExceedingMaxIterations_ThrowsAgentRunnerException`
- [x] [Review][Defer] AIFunction lambdas contain execution logic — dual-dispatch risk if pipeline adds auto-invocation — deferred, latent risk only under current manual-loop architecture

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

### Completion Notes List

- Task 1: Created `IWorkflowTool` interface with `Name`, `Description`, `ExecuteAsync` and `ToolExecutionContext` record with `InstanceFolderPath`, `InstanceId`, `StepId`
- Task 2: Created `ToolLoop` as a static class with `RunAsync` — while loop inspecting `FunctionCallContent`, validates against declared tools dictionary, dispatches valid calls, catches tool errors and returns as `FunctionResultContent`, logs undeclared tool attempts at Warning level
- Task 3: Created `IStepExecutor` interface and `StepExecutionContext` record with `Workflow`, `Step`, `Instance`, `InstanceFolderPath`, `WorkflowFolderPath`
- Task 4: Implemented `StepExecutor` — updates step Active/Complete/Error, resolves profile, assembles prompt, filters tools to step's declared list, wraps as `AIFunction` via `AIFunctionFactory.Create`, runs tool loop. `OperationCanceledException` propagates; all other exceptions caught and step set to Error
- Task 5: Registered `IStepExecutor` as singleton in `AgentRunnerComposer`, updated `IWorkflowTool` comment to reference Epic 5
- Task 6: 11 unit tests — 5 ToolLoop (dispatch, undeclared rejection, error handling, loop continuation, multiple calls) + 6 StepExecutor (Active→Complete, Error on exception, tool filtering, execution order, structured logging, cancellation propagation)
- All 121 tests pass (110 existing + 11 new), zero regressions
- Code review fixes: 5 patches applied, 2 new tests added (123 total)

### Dev Notes

- **ToolLoop.MaxIterations (100):** This cap is a safety net against runaway LLMs. The constant is `internal` so it can be referenced in tests. If real workflows need more iterations, consider making it configurable via `StepDefinition` or `AgentRunnerOptions`. For now 100 is generous — most steps should complete in <20 iterations.
- **AIFunction dual-dispatch latent risk:** `StepExecutor` wraps tools in `AIFunctionFactory.Create` with real execution lambdas because the factory requires a delegate to derive JSON schemas. These lambdas are NOT invoked under the current manual-loop architecture (no `FunctionInvokingChatClient` in the pipeline). If the Umbraco.AI provider pipeline ever adds auto-invocation middleware, tools would execute twice. See deferred-work.md entry. The lambdas also capture the outer `cancellationToken` — safe today since they're never called, but worth noting if the dispatch path changes.
- **Error-path status update:** The catch block now uses `CancellationToken.None` to ensure the Error status write succeeds even when the original token is cancelled. If the status write itself fails, it logs at `Critical` level and swallows — the step remains in `Active` state, which is a detectable inconsistency for monitoring.

### Change Log

- 2026-03-31: Story 4.3 implementation complete — step executor and tool loop with 11 unit tests
- 2026-03-31: Code review fixes — stepIndex guard, ToolLoop max iterations, WorkflowAlias in ToolExecutionContext, error-path hardening, 2 new tests (123 total)
- 2026-03-31: Manual E2E validation — discovered `WithAlias()` is required by Umbraco.AI `AIChatBuilder` (was missing from ProfileResolver), connection vs profile distinction (both needed), profile alias is the string set in Umbraco AI profile config. Fixed ProfileResolver, updated example workflow default_profile to `anthropic-sonnet-4-6`

### File List

- `Shallai.UmbracoAgentRunner/Tools/IWorkflowTool.cs` (new)
- `Shallai.UmbracoAgentRunner/Tools/ToolExecutionContext.cs` (new)
- `Shallai.UmbracoAgentRunner/Engine/ToolLoop.cs` (new)
- `Shallai.UmbracoAgentRunner/Engine/IStepExecutor.cs` (new)
- `Shallai.UmbracoAgentRunner/Engine/StepExecutionContext.cs` (new)
- `Shallai.UmbracoAgentRunner/Engine/StepExecutor.cs` (new)
- `Shallai.UmbracoAgentRunner/Composers/AgentRunnerComposer.cs` (modified)
- `Shallai.UmbracoAgentRunner.Tests/Engine/ToolLoopTests.cs` (new)
- `Shallai.UmbracoAgentRunner.Tests/Engine/StepExecutorTests.cs` (new)
