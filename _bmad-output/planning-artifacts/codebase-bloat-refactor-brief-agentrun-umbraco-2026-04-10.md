# Codebase Bloat Refactor Brief: AgentRun.Umbraco

Date: 2026-04-10
Audience: Follow-on coding agent / reviewer
Purpose: Turn the code-shape review into a concrete refactor brief, with explicit areas to confirm, challenge, or refine before implementation.

## How To Use This Brief

This document is intentionally more operational than the companion review. It is meant to help a coding agent:

- inspect the same hotspots
- confirm whether the critique is fair
- agree or disagree with the proposed splits
- turn the agreed items into implementation tasks

The expectation is not blind acceptance. The next agent should explicitly validate each recommendation against the current code and push back where the suggested refactor would add more complexity than it removes.

## Executive Framing

The current package does not look too large for its product scope. The goal is not to make it smaller for its own sake. The goal is to reduce:

- overly large responsibility surfaces
- branch-heavy execution code
- UI state sprawl
- workaround-shaped implementations
- production-code narrative clutter

The most important question for each hotspot is:

> Does this file express one clear responsibility, or has it become a holding area for multiple concerns that now need separate homes?

## Review Rules For The Next Agent

For each hotspot below:

1. Confirm whether the file really has multiple responsibilities.
2. Identify which responsibilities are stable enough to extract cleanly.
3. Reject any proposed split that would create thin wrappers without reducing complexity.
4. Preserve behavior first, then improve shape.
5. Prefer extractions that produce obvious test seams.
6. Call out anywhere the suggested refactor would fight existing project conventions.

## Priority Order

Recommended order of attention:

1. `AgentRun.Umbraco/Tools/FetchUrlTool.cs`
2. `AgentRun.Umbraco/Engine/ToolLoop.cs`
3. `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts`
4. `AgentRun.Umbraco/Engine/StepExecutor.cs`
5. `AgentRun.Umbraco/Client/src/components/agentrun-chat-message.element.ts`
6. `AgentRun.Umbraco/Engine/PromptAssembler.cs`
7. `AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs`

## Hotspot 1: `FetchUrlTool.cs`

Path: `AgentRun.Umbraco/Tools/FetchUrlTool.cs`  
Current concern: Too many responsibilities in one tool implementation  
Suggested disposition: `Split`

### What To Validate

Check whether the file currently owns all or most of the following:

- request validation
- SSRF checks
- redirect policy
- fetch execution
- response size limiting
- content-type branching
- structured HTML extraction
- result shaping
- cache persistence

If yes, the split recommendation is justified.

### What To Challenge

Push back on the recommendation if:

- the extracted pieces would be too trivial to justify their own files
- the current file is cohesive because the behaviors are tightly coupled in practice
- the existing tests become materially harder to follow after extraction

### Suggested Refactor Shape

Potential extractions:

- `UrlFetchRequestValidator`
- `RedirectingHttpFetcher`
- `FetchedResponseLimiter`
- `StructuredHtmlExtractor`
- `FetchResultSerializer`
- `FetchCacheWriter`

The tool class should ideally become a coordinator over these parts.

### Concrete Tasks

- Map the existing methods by responsibility.
- Group methods into extractable clusters.
- Identify which clusters have their own test surface already.
- Propose a minimum viable split, not a maximal one.
- Preserve public behavior and result shape.

### Definition Of Done

- `FetchUrlTool` reads as a tool coordinator, not a subsystem dump.
- HTML extraction and fetch transport logic no longer live in the same dense file.
- Tests remain readable and focused.

## Hotspot 2: `ToolLoop.cs`

Path: `AgentRun.Umbraco/Engine/ToolLoop.cs`  
Current concern: Branch-heavy control loop mixing orchestration, policy, and persistence  
Suggested disposition: `Split`

### What To Validate

Confirm whether the loop currently combines:

- response streaming and accumulation
- tool call detection
- tool execution dispatch
- tool error handling
- conversation persistence
- SSE emission
- interactive wait behavior
- stall recovery
- loop termination shaping

If most of these are present, the file is carrying too much.

### What To Challenge

Push back if:

- the loop would become harder to follow after extraction
- the new abstractions would only move branches around without reducing reasoning load
- the proposed helpers do not correspond to stable concepts in the engine

### Suggested Refactor Shape

Potential extractions:

- `StreamingResponseAccumulator`
- `ToolCallProcessor`
- `InteractiveWaitCoordinator`
- `StallRecoveryPolicy`
- `ConversationAppender`

An acceptable alternative is fewer helpers if they remove real branch density.

### Concrete Tasks

- Identify all exit paths and group them by purpose.
- Separate pure transformation logic from side-effecting behavior.
- Move any clearly policy-shaped code into named collaborators or internal services.
- Reduce repeated response-construction patterns.

### Definition Of Done

- The main loop tells a clear story.
- Recovery policy is named rather than embedded.
- Side effects are easier to locate and test.

## Hotspot 3: `agentrun-instance-detail.element.ts`

Path: `AgentRun.Umbraco/Client/src/components/agentrun-instance-detail.element.ts`  
Current concern: Page component acting as state store, reducer, transport client, and renderer  
Suggested disposition: `Split`

### What To Validate

Confirm whether the component handles:

- loading and fetch orchestration
- start / retry / cancel actions
- SSE parsing
- chat history state
- tool event state
- selected step state
- artifact popover state
- status banner logic
- page rendering

If yes, the split recommendation is strongly justified.

### What To Challenge

Push back if:

- a store layer would be more awkward than the current component pattern
- the component is already aligned with an established Bellissima pattern in this repo
- splitting would introduce unnecessary cross-file state hopping

### Suggested Refactor Shape

Potential extractions:

- `instance-detail.store.ts`
- `sse-event-reducer.ts`
- `instance-actions.ts`
- `chat-message-mapper.ts`

Potential UI splits:

- header/actions
- step list
- status banner
- main conversation panel

### Concrete Tasks

- List the current local state fields and classify each by concern.
- Identify reducer-shaped logic inside event handlers.
- Move transport parsing and state reduction out of the render component first.
- Keep the initial split small enough to preserve confidence.

### Definition Of Done

- The main element is primarily a view/composition file.
- SSE event handling is testable outside the component.
- UI state transitions are easier to reason about.

## Hotspot 4: `StepExecutor.cs`

Path: `AgentRun.Umbraco/Engine/StepExecutor.cs`  
Current concern: Sequencing service overloaded with mapping and policy details  
Suggested disposition: `Split`

### What To Validate

Confirm whether the executor is currently performing:

- profile resolution
- artifact validation
- prompt assembly preparation
- conversation-history remapping
- tool declaration creation
- chat options shaping
- completion behavior setup
- execution failure classification
- state update handling

If yes, this file is too broad.

### What To Challenge

Push back if:

- the extracted helpers would be one-method wrappers
- the current shape is still the clearest execution narrative
- there is no test or maintenance benefit from extraction

### Suggested Refactor Shape

Potential extractions:

- `ConversationHistoryMapper`
- `StepToolDeclarationFactory`
- `StepChatOptionsFactory`
- `StepExecutionFailureHandler`

### Concrete Tasks

- Map each method to either sequencing, transformation, or policy.
- Keep sequencing in `StepExecutor`.
- Move transformation-heavy code out first.
- Re-evaluate whether the remaining file becomes comfortably readable.

### Definition Of Done

- `StepExecutor` reads like an orchestrator.
- Helper-bucket behavior is reduced.
- Policy and mapping logic have clearer homes.

## Hotspot 5: `agentrun-chat-message.element.ts`

Path: `AgentRun.Umbraco/Client/src/components/agentrun-chat-message.element.ts`  
Current concern: Rendering flow is more workaround-shaped than domain-shaped  
Suggested disposition: `Simplify`

### What To Validate

Confirm whether the component:

- sanitises markdown
- constructs DOM nodes manually
- copies `innerHTML`
- re-injects the result into Lit templates

If yes, the concern is valid even if the file is not especially large.

### What To Challenge

Push back if:

- the current approach is required by a hard limitation in the markdown/rendering stack
- a simpler rendering path would weaken sanitisation or break streaming behavior

### Suggested Refactor Shape

- Reuse `agentrun-markdown-renderer.element.ts` where practical.
- Make the rendering path declarative.
- Eliminate DOM-to-HTML round trips unless there is a demonstrated need.

### Concrete Tasks

- Confirm why the current round trip exists.
- Test whether the markdown renderer can cover the same cases.
- Simplify only if security and rendering parity are preserved.

### Definition Of Done

- The message renderer is easier to trust at a glance.
- Sanitisation remains intact.
- The code no longer feels workaround-driven.

## Hotspot 6: `PromptAssembler.cs`

Path: `AgentRun.Umbraco/Engine/PromptAssembler.cs`  
Current concern: Prompt contract and policy are starting to blur  
Suggested disposition: `Simplify`

### What To Validate

Check whether the file currently mixes:

- prompt structure
- runtime context formatting
- tool description formatting
- artifact context behavior
- safety messaging

If yes, it is accumulating too many prompt concerns.

### What To Challenge

Push back if:

- extraction would only make prompt construction more fragmented
- the current file is still small and coherent enough to leave alone for now

### Suggested Refactor Shape

Potential sections or internal builders:

- `AgentInstructionsSection`
- `RuntimeContextSection`
- `DeclaredToolsSection`
- `ArtifactInputsSection`
- `PromptSafetySection`

### Concrete Tasks

- Define the intended prompt contract first.
- Compare current output to that contract.
- Refactor only after the contract is explicit.

### Definition Of Done

- Prompt assembly is easier to audit.
- Architecture drift is reduced.
- Prompt behavior is explicit rather than implicit.

## Hotspot 7: `WorkflowOrchestrator.cs`

Path: `AgentRun.Umbraco/Engine/WorkflowOrchestrator.cs`  
Current concern: Small file with disproportionate conceptual load  
Suggested disposition: `Simplify`

### What To Validate

Confirm whether the file currently bundles:

- active-run registration
- lifecycle state changes
- step progression
- terminal-state handling
- execution ownership assumptions

### What To Challenge

Push back if:

- the file is actually a clean coordinator already
- the real issue is background execution architecture, not file shape

### Suggested Refactor Shape

This should probably not be the first refactor unless execution architecture changes at the same time. If background execution is introduced later, consider splitting:

- `RunCoordinator`
- `InstanceProgressionService`
- `StepLifecyclePublisher`

### Concrete Tasks

- Decide whether this file needs cleanup now or only after execution architecture changes.
- Avoid cosmetic refactors that will be invalidated by a later background-run redesign.

### Definition Of Done

- The orchestrator has a clear boundary.
- Refactoring here is aligned with broader execution architecture.

## Areas That Probably Should Not Be Aggressively Reworked

These areas may benefit from light cleanup, but they do not currently read like primary bloat problems:

- `AgentRun.Umbraco/Security/PathSandbox.cs`
- `AgentRun.Umbraco/Security/SsrfProtection.cs`
- `AgentRun.Umbraco/Instances/ConversationStore.cs`
- `AgentRun.Umbraco/Instances/InstanceManager.cs`
- `AgentRun.Umbraco/Workflows/WorkflowRegistry.cs`

The next agent should resist the urge to "clean up everything." Focus on the true accumulation points first.

## Cross-Cutting Cleanup Recommendations

## 1. Comment Hygiene

Review for comments that are really work-item history, such as:

- story references
- review references
- previous gate or milestone notes
- implementation archaeology

Recommended action:

- move historical context into planning docs
- keep only concise technical rationale in code

## 2. Policy Extraction

Look for inline decisions that should become named policies:

- stall recovery rules
- chat option shaping
- extraction/truncation heuristics
- UI event reduction rules

Recommended action:

- turn implicit behavior into explicit concepts where it improves readability

## 3. Test-Supported Refactoring

Before touching any hotspot:

- map existing tests to the responsibilities inside the file
- avoid splits that make tests more brittle or indirect
- add characterization tests where behavior is hard to infer from current coverage

## Questions The Next Agent Should Explicitly Answer

The next agent's review should explicitly answer these:

1. Which of the proposed hotspot splits are clearly justified?
2. Which recommendations are directionally right but too aggressive?
3. Which files look ugly but are actually acceptable once read carefully?
4. Where would a refactor reduce complexity versus only redistributing it?
5. Which hotspot should be tackled first for the best maintainability return?

## Suggested Response Format For The Next Agent

To make agreement and disagreement easy to compare, ask the next agent to respond in this format:

### Overall Agreement

- brief view on whether the bloat critique is fair

### Agree

- recommendations it agrees with
- why they are worth doing

### Partially Agree

- recommendations it agrees with directionally but would reshape

### Disagree

- recommendations it thinks are overstated or low value

### Proposed Refactor Order

- the order it would actually execute

### Concrete First Refactor

- which file it would change first
- what exact split or simplification it would perform
- what tests it would rely on or add

## Final Instruction To A Follow-On Coding Agent

Do not optimize for maximum decomposition. Optimize for clearer responsibility boundaries with the fewest necessary moves.

The best outcome is not "more files." The best outcome is:

- less branch density
- clearer ownership of policy
- easier testability
- easier onboarding for the next maintainer
- less code that feels like it was discovered incrementally and never reshaped
