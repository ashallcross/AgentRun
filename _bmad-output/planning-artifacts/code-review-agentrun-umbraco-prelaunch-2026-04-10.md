# Code Review Report: AgentRun.Umbraco Pre-Launch

## Executive Summary

`AgentRun.Umbraco` is **not ready for launch** in its current form.

Top 3 risks:

1. Unsandboxed workflow-defined paths can read host files and leak them to the LLM.
2. Execution is tied to the SSE request lifecycle instead of running safely in the background.
3. The cancel path marks an instance cancelled without actually stopping in-flight execution.

## Critical Findings

### 1. Workflow-defined paths and identifiers are not safely constrained

**Files**

- [PromptAssembler.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/PromptAssembler.cs#L27)
- [ArtifactValidator.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/ArtifactValidator.cs#L26)
- [CompletionChecker.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/CompletionChecker.cs#L26)
- [ConversationStore.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Instances/ConversationStore.cs#L172)
- [WorkflowValidator.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L248)

**What is wrong**

The validator only checks that `agent` is non-empty and does not validate `agent`, `reads_from`, `writes_to`, `completion_check.files_exist`, or step IDs as safe relative paths or filenames.

**Why it matters**

A malicious or malformed workflow can use `../../...` values to:

- read arbitrary files into the system prompt
- probe file existence outside the instance folder
- generate conversation-history paths from unsafe step IDs

In a package that can send host content to third-party LLM providers, this is a launch-blocking file-exfiltration risk.

**Fix**

Reject absolute paths, traversal segments, and path separators in step IDs at workflow-load time. Canonicalize every workflow-defined path against its allowed root and fail workflow registration if any path escapes that root.

## High Priority Findings

### 1. Execution is tied to the SSE request lifetime instead of a background runner

**Files**

- [ExecutionEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L162)
- [ExecutionEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L174)
- [WorkflowOrchestrator.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L36)
- [AgentRunComposer.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Composers/AgentRunComposer.cs#L15)

**What is wrong**

`Start` and `Retry` execute the orchestrator inline on the controller response stream, and an aborted request is treated as failure and persisted as `Failed`.

**Why it matters**

A browser refresh, tab close, proxy reset, or brief network interruption can kill a run.

**Fix**

Move execution into a hosted/background service with a per-instance queue/state machine. SSE should subscribe to run events instead of owning run lifetime.

### 2. `Cancel` does not actually stop in-flight work

**Files**

- [InstanceEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L102)
- [WorkflowOrchestrator.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs#L102)
- [ToolLoop.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/ToolLoop.cs#L51)

**What is wrong**

The cancel endpoint only updates persisted status. It does not signal a cancellation token, unregister the active instance, or stop the running tool loop/orchestrator.

**Why it matters**

A cancelled run can continue calling the provider and tools and later overwrite state back to `Completed` or `Failed`.

**Fix**

Keep a per-instance `CancellationTokenSource` in the active-run registry and have `Cancel` trigger it. The orchestrator and tool loop should check cancelled state between step boundaries and after tool calls.

### 3. `list_files` has a root-listing symlink gap

**Files**

- [ListFilesTool.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Tools/ListFilesTool.cs#L31)
- [PathSandbox.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Security/PathSandbox.cs#L44)

**What is wrong**

The empty-path branch bypasses `ValidatePath()` and calls `IsPathOrAncestorSymlink()`, but that helper only checks the target path itself, not its ancestors.

**Why it matters**

If an ancestor of the instance folder is a symlink/reparse point, root listing can escape the intended sandbox.

**Fix**

Reuse the full ancestor-walk logic for the root case, or replace the helper with one implementation that always checks the target and every ancestor between root and target.

### 4. Broken workflows can still register and then fail at runtime

**Files**

- [WorkflowRegistry.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowRegistry.cs#L165)

**What is wrong**

Missing agent files only produce a warning, so the workflow still appears in the UI even though execution is guaranteed to fail later in `PromptAssembler`.

**Why it matters**

This produces avoidable runtime failures and makes the registry look healthier than it is.

**Fix**

Treat missing agent files as registration failures, not warnings.

## Medium Priority Findings

### Umbraco Package Conventions

- [WorkflowEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/WorkflowEndpoints.cs#L9), [InstanceEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/InstanceEndpoints.cs#L10), and [ExecutionEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L12) use attribute MVC controllers with `[Route]` and `[Authorize]`, not Minimal API in `IComposer`.
- Several endpoints duplicate `ProducesResponseType` declarations for `400/404/409`, which is the Swagger footgun called out in the target architecture.

### Security

- There is no server-side markdown sanitiser counterpart to the frontend one. See [markdown-sanitiser.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/utils/markdown-sanitiser.ts#L1), [agentrun-markdown-renderer.element.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/components/agentrun-markdown-renderer.element.ts#L17), and the absence of the planned `MarkdownSanitiser.cs` in `Security/`.
- SSE streaming has no heartbeat/keepalive events beyond the initial headers. See [SseHelper.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/SseHelper.cs#L8) and [SseEventEmitter.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs#L55).

### Engine

- [PromptAssembler.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/PromptAssembler.cs#L151) does not include `reads_from` file contents; it only lists existence flags.
- `ConfigureAwait(false)` usage is inconsistent across the library. Examples: [StepExecutor.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/StepExecutor.cs#L41), [PromptAssembler.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/PromptAssembler.cs#L17), and [InstanceManager.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Instances/InstanceManager.cs#L71).

### Workflow Schema / Runtime Drift

- [workflow-schema.json](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Schemas/workflow-schema.json#L26), [workflow-schema.json](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Schemas/workflow-schema.json#L83), [WorkflowValidator.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowValidator.cs#L22), [StepDefinition.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/StepDefinition.cs#L3), [WorkflowDefinition.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowDefinition.cs#L3), and [WorkflowParser.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowParser.cs#L9) have drifted.
- `icon`, `variants`, and `data_files` are allowed by schema/validator but have no corresponding runtime properties, and unmatched properties are ignored during parse.

### Frontend

- [agentrun-dashboard.element.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/components/agentrun-dashboard.element.ts#L8) has no `**` catch-all route.

### Packaging

- [AgentRun.Umbraco.csproj](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/AgentRun.Umbraco.csproj#L16), [AgentRun.Umbraco.csproj](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/AgentRun.Umbraco.csproj#L17), and [umbraco-package.json](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/public/umbraco-package.json#L1) still have incomplete package metadata.

## Low Priority Findings

- [SseHelper.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/SseHelper.cs#L8) and [SseEventEmitter.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/Events/SseEventEmitter.cs#L55) could use .NET 10 native SSE APIs.
- [WorkflowRegistry.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowRegistry.cs#L13) and [WorkflowRegistry.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Workflows/WorkflowRegistry.cs#L25) are candidates for `FrozenDictionary` / `FrozenSet`.
- Several client components still use hardcoded pixel values rather than design tokens. Examples: [agentrun-instance-detail.element.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L148), [agentrun-chat-panel.element.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/components/agentrun-chat-panel.element.ts#L46), and [agentrun-tool-call.element.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/components/agentrun-tool-call.element.ts#L78).

## Architecture Compliance

| Decision | Status | Notes |
| --- | --- | --- |
| `IAIChatService` with manual tool loop | Partial | The package uses `IAIChatService` plus a manual tool loop, but the client creation/profile resolution shape differs from the target design. See [ProfileResolver.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/ProfileResolver.cs#L26) and [StepExecutor.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/StepExecutor.cs#L135). |
| SSE + POST, not SignalR | Match | `Start`/`Retry` stream SSE over POST. See [ExecutionEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L46) and [agentrun-instance-detail.element.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts#L433). |
| Custom `IWorkflowTool` with per-step filtering | Partial | Per-step filtering exists, but execution uses `AITool`/tool declarations rather than the planned `AIFunctionFactory.Create()` path. See [StepExecutor.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/StepExecutor.cs#L99). |
| Engine has no Umbraco dependencies | Deviates | [ProfileResolver.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/ProfileResolver.cs#L5) depends on `Umbraco.AI` namespaces from inside `Engine/`. |
| All endpoints use Minimal API with backoffice auth | Deviates | The package uses MVC controllers such as [WorkflowEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/WorkflowEndpoints.cs#L9). |
| Background execution via hosted service | Deviates | There is no hosted/background runner. Execution is request-bound in [ExecutionEndpoints.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Endpoints/ExecutionEndpoints.cs#L162). |

## Test Coverage Assessment

Backend unit coverage is broad and generally strong. Engine, tools, workflow loading, SSRF/path sandboxing, instances, conversation storage, and endpoint behavior all have NUnit 4 + NSubstitute coverage in [AgentRun.Umbraco.Tests.csproj](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco.Tests/AgentRun.Umbraco.Tests.csproj#L15).

The .NET suite passed locally via:

```text
dotnet test "Umbraco AI.sln" --no-restore
```

The client suite also passed locally:

```text
npm test
```

10 test files and 162 tests passed.

Highest-risk gaps:

- no end-to-end coverage for disconnect/resume/background execution semantics
- no proof that `Cancel` halts an active run
- no tests asserting that workflow-supplied paths and IDs are rejected before escaping their intended roots

## Positive Observations

- [ToolLoop.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Engine/ToolLoop.cs#L310) revalidates tool names against the step’s declared tool set, returns tool failures back to the model instead of crashing, and emits structured SSE events the UI can consume.
- [AgentRunComposer.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Composers/AgentRunComposer.cs#L67), [SsrfProtection.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Security/SsrfProtection.cs#L22), and [FetchUrlTool.cs](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Tools/FetchUrlTool.cs#L123) show a strong SSRF posture with manual redirect handling and per-hop revalidation.
- [vite.config.ts](/Users/adamshallcross/Documents/Umbraco AI/AgentRun.Umbraco/Client/vite.config.ts#L13) externalises `@umbraco` correctly, and the client components consistently use `UmbLitElement` plus `@umbraco-cms/backoffice/external/lit`.
