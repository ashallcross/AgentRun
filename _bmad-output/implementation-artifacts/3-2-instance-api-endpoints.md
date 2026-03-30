# Story 3.2: Instance API Endpoints

Status: done

## Story

As a developer or editor,
I want API endpoints to create, list, view, cancel, and delete workflow instances,
so that the dashboard can manage workflow runs through a standard REST interface.

## Acceptance Criteria

1. **Given** the InstanceManager from Story 3.1 is available via DI
   **When** the API endpoints are registered in AgentRunnerComposer
   **Then** POST `/umbraco/api/shallai/instances` creates a new instance for a given workflowAlias and returns JSON with id, workflowAlias, status, currentStepIndex, createdAt

2. **Given** instances exist on disk
   **When** GET `/umbraco/api/shallai/instances` is called
   **Then** it returns a JSON array of all instances with status, current step, and timestamps

3. **Given** instances exist for multiple workflows
   **When** GET `/umbraco/api/shallai/instances?workflowAlias={alias}` is called
   **Then** it returns only instances matching the given workflow alias

4. **Given** a specific instance exists
   **When** GET `/umbraco/api/shallai/instances/{id}` is called
   **Then** it returns full instance detail including per-step statuses

5. **Given** an instance is running
   **When** POST `/umbraco/api/shallai/instances/{id}/cancel` is called
   **Then** the instance status is set to cancelled

6. **Given** an instance is completed or cancelled
   **When** DELETE `/umbraco/api/shallai/instances/{id}` is called
   **Then** the instance folder is removed from disk

7. **Given** an instance is pending or running
   **When** DELETE `/umbraco/api/shallai/instances/{id}` is called
   **Then** the request is rejected with 409 Conflict

8. All endpoints require backoffice authentication via `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`

9. Error responses use `{ error, message }` format with appropriate HTTP status codes

10. JSON responses use camelCase field naming via System.Text.Json

## What NOT to Build

- No SSE streaming endpoints (Epic 4: start, retry, message)
- No conversation history endpoints (Epic 6)
- No artifact download endpoints (Epic 8)
- No step execution or advancement logic (Epic 4)
- No frontend components (Story 3.3)

## Tasks / Subtasks

- [x] Task 1: Create InstanceEndpoints controller (AC: #1–#10)
  - [x] 1.1: Create `Endpoints/InstanceEndpoints.cs` as `ApiController` with route `umbraco/api/shallai` and `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`
  - [x] 1.2: Inject `IInstanceManager` and `IWorkflowRegistry`
  - [x] 1.3: POST `instances` — accept `CreateInstanceRequest { workflowAlias }`, validate workflow exists in registry, call `CreateInstanceAsync`, return mapped response
  - [x] 1.4: GET `instances` — accept optional `[FromQuery] string? workflowAlias`, call `ListInstancesAsync`, return mapped array
  - [x] 1.5: GET `instances/{id}` — call `FindInstanceAsync` via Option B implementation, return 404 if not found
  - [x] 1.6: POST `instances/{id}/cancel` — find instance, validate status is `Running` or `Pending`, call `SetInstanceStatusAsync(Cancelled)`, return updated state; return 404 if not found, 409 if terminal
  - [x] 1.7: DELETE `instances/{id}` — find instance, validate status is terminal (Completed, Failed, Cancelled), call `DeleteInstanceAsync`, return 204; return 404 if not found, 409 if not terminal
- [x] Task 2: Create API request/response models (AC: #1, #9, #10)
  - [x] 2.1: Create `Models/ApiModels/CreateInstanceRequest.cs` with `WorkflowAlias` property
  - [x] 2.2: Create `Models/ApiModels/InstanceResponse.cs` — flat summary with id, workflowAlias, status, currentStepIndex, createdAt, updatedAt
  - [x] 2.3: Create `Models/ApiModels/InstanceDetailResponse.cs` — full detail including steps array (id, status, startedAt, completedAt)
  - [x] 2.4: Create `Models/ApiModels/ErrorResponse.cs` with `Error` and `Message` properties
- [x] Task 3: Handle instance lookup by ID across workflows (AC: #4, #5, #6, #7)
  - [x] 3.1: `IInstanceManager.ListInstancesAsync(null)` returns all instances — scan results by `InstanceId` for single-instance lookups
  - [x] 3.2: Implemented Option B: added `FindInstanceAsync(string instanceId)` to `IInstanceManager` — scans workflow directories for matching instanceId subfolder. Keeps I/O logic in the manager and out of the controller. Path traversal guard included.
- [x] Task 4: Write unit tests (AC: #1–#10)
  - [x] 4.1: Test POST creates instance and returns correct shape
  - [x] 4.2: Test POST returns 404 when workflow alias not found
  - [x] 4.3: Test GET list returns all instances
  - [x] 4.4: Test GET list with workflowAlias filter
  - [x] 4.5: Test GET detail returns full instance with steps
  - [x] 4.6: Test GET detail returns 404 for unknown id
  - [x] 4.7: Test POST cancel sets status to Cancelled
  - [x] 4.8: Test POST cancel returns 409 when not running
  - [x] 4.9: Test DELETE removes completed instance
  - [x] 4.10: Test DELETE returns 409 when instance is pending/running
  - [x] 4.11: Test DELETE returns 404 for unknown id
  - [x] 4.12: Test all responses use correct HTTP status codes

### Review Findings

- [x] [Review][Decision] Enum serialization: InstanceStatus/StepStatus serialize as integers not strings — Fixed: added `[JsonConverter(typeof(JsonStringEnumConverter))]` to both enums.
- [x] [Review][Patch] FindInstanceAsync: sandbox check after File.Exists + yamlPath not from resolved path — Fixed: added 32-char hex format validation via `GeneratedRegex`, moved sandbox check before File.Exists, derived yamlPath from resolved path.
- [x] [Review][Patch] Delete endpoint ignores bool return from DeleteInstanceAsync — Fixed: checks return value, returns 404 if false.
- [x] [Review][Patch] FindInstanceAsync: unhandled exception if directory deleted mid-read — Fixed: added try/catch matching `CollectInstancesFromDirectoryAsync` pattern.
- [x] [Review][Defer] Cancel endpoint TOCTOU race between FindInstanceAsync and SetInstanceStatusAsync — status could change between the two calls. Requires InstanceManager-level locking. [InstanceEndpoints.cs:82-106] — deferred, requires architectural change
- [x] [Review][Defer] Delete endpoint TOCTOU race between FindInstanceAsync and DeleteInstanceAsync — same TOCTOU pattern as cancel. [InstanceEndpoints.cs:112-135] — deferred, requires architectural change

## Dev Notes

### Endpoint Pattern — Follow WorkflowEndpoints Exactly

The existing pattern in `Endpoints/WorkflowEndpoints.cs` uses:
- `[ApiController]` attribute on class
- `[Route("umbraco/api/shallai")]` base route
- `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]` at class level
- Inherits `ControllerBase` (not `Controller`)
- `[ProducesResponseType]` attributes on each action
- Returns `IActionResult` (`Ok()`, `NotFound()`, `NoContent()`, etc.)

**Do NOT use Minimal API `MapGet`/`MapPost`** — the project uses controller-based routing.

### IInstanceManager Interface (Story 3-1 Output)

```csharp
public interface IInstanceManager
{
    Task<InstanceState> CreateInstanceAsync(string workflowAlias, WorkflowDefinition definition, string createdBy, CancellationToken cancellationToken);
    Task<InstanceState?> GetInstanceAsync(string workflowAlias, string instanceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstanceState>> ListInstancesAsync(string? workflowAlias, CancellationToken cancellationToken);
    Task<InstanceState> UpdateStepStatusAsync(string workflowAlias, string instanceId, int stepIndex, StepStatus status, CancellationToken cancellationToken);
    Task<InstanceState> SetInstanceStatusAsync(string workflowAlias, string instanceId, InstanceStatus status, CancellationToken cancellationToken);
    Task<bool> DeleteInstanceAsync(string workflowAlias, string instanceId, CancellationToken cancellationToken);
}
```

**Critical:** `GetInstanceAsync` and all mutation methods require `workflowAlias` + `instanceId`. The API routes only expose `{id}`. This means GET/cancel/delete by `{id}` alone need a lookup strategy:
- Option A: Call `ListInstancesAsync(null)` to get all instances, find matching `InstanceId`, extract `WorkflowAlias` from the result — works but inefficient for large instance counts
- Option B: Add `FindInstanceAsync(string instanceId)` to `IInstanceManager` that scans workflow directories — cleaner API surface, keeps search logic in the manager

**Recommended: Option B** — add `FindInstanceAsync` to `IInstanceManager` and implement in `InstanceManager`. The disk structure is `{root}/{workflowAlias}/{instanceId}/`, so scanning top-level folders for a matching instanceId subfolder is straightforward and keeps I/O logic out of the controller.

### InstanceState Model (Reference)

```csharp
public sealed class InstanceState
{
    public string WorkflowAlias { get; set; }     // e.g. "content-audit"
    public int CurrentStepIndex { get; set; }       // 0-based
    public InstanceStatus Status { get; set; }      // Pending | Running | Completed | Failed | Cancelled
    public List<StepState> Steps { get; set; }      // per-step detail
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; }
    public string InstanceId { get; set; }          // 32-char hex (Guid.NewGuid().ToString("N"))
}
```

### Create Instance — Getting `createdBy`

`CreateInstanceAsync` requires a `createdBy` string. In the controller, extract the authenticated user's identity:
```csharp
var createdBy = User.Identity?.Name ?? "unknown";
```

### Create Instance — WorkflowDefinition Required

`CreateInstanceAsync` requires the `WorkflowDefinition` object (not just the alias). Resolve via `IWorkflowRegistry`:
```csharp
var registered = _workflowRegistry.GetWorkflow(request.WorkflowAlias);
if (registered is null) return NotFound(new ErrorResponse { Error = "workflow_not_found", Message = $"..." });
await _instanceManager.CreateInstanceAsync(request.WorkflowAlias, registered.Definition, createdBy, ct);
```

### Cancel — Failed Instances Are Also Terminal

Story 3-1 treats `Failed` as a terminal status alongside `Completed` and `Cancelled`. Cancel should only be allowed when status is `Running` (or `Pending` — consider whether pending instances should be cancellable). Return 409 for already-terminal statuses.

### Delete — Terminal Statuses

Story 3-1 allows deletion for `Completed`, `Failed`, and `Cancelled` (all three terminal statuses). Reject delete for `Pending` and `Running` with 409.

### Error Response Format

Use a consistent error response model:
```csharp
public sealed class ErrorResponse
{
    public required string Error { get; init; }    // machine-readable: "workflow_not_found", "invalid_status"
    public required string Message { get; init; }  // human-readable explanation
}
```

**Error codes to implement:**
| Scenario | HTTP Status | Error Code |
|----------|-------------|------------|
| Workflow alias not found | 404 | `workflow_not_found` |
| Instance not found | 404 | `instance_not_found` |
| Cancel non-running instance | 409 | `invalid_status` |
| Delete non-terminal instance | 409 | `invalid_status` |

### Response Mapping

Map `InstanceState` to response DTOs — do NOT return domain models directly from endpoints. This follows the existing `WorkflowSummary` pattern.

### JSON Serialization

System.Text.Json is configured project-wide. Ensure response models use `PascalCase` properties — the default `JsonSerializerOptions` in ASP.NET Core serialises to camelCase automatically. Do not add `[JsonPropertyName]` attributes unless overriding.

### File Placement

| File | Path |
|------|------|
| InstanceEndpoints.cs | `Shallai.UmbracoAgentRunner/Endpoints/` |
| CreateInstanceRequest.cs | `Shallai.UmbracoAgentRunner/Models/ApiModels/` |
| InstanceResponse.cs | `Shallai.UmbracoAgentRunner/Models/ApiModels/` |
| InstanceDetailResponse.cs | `Shallai.UmbracoAgentRunner/Models/ApiModels/` |
| ErrorResponse.cs | `Shallai.UmbracoAgentRunner/Models/ApiModels/` |
| InstanceEndpointsTests.cs | `Shallai.UmbracoAgentRunner.Tests/Endpoints/` |

### Project Structure Notes

- File placement follows the existing pattern: controllers in `Endpoints/`, DTOs in `Models/ApiModels/`
- Namespace convention: `Shallai.UmbracoAgentRunner.Endpoints`, `Shallai.UmbracoAgentRunner.Models.ApiModels`
- No new NuGet packages required — all dependencies already available
- If adding `FindInstanceAsync` to `IInstanceManager`, update the existing `InstanceManagerTests.cs` to cover the new method

### Testing Conventions

- Follow `WorkflowEndpointsTests.cs` pattern: NSubstitute mocks for `IInstanceManager` and `IWorkflowRegistry`
- NUnit 4 attributes: `[TestFixture]`, `[SetUp]`, `[Test]`
- Assert pattern: `Assert.That(actual, Is.EqualTo(expected))`
- Cast `IActionResult` to specific result types (`OkObjectResult`, `NotFoundObjectResult`, `NoContentResult`, `ConflictObjectResult`)
- Test file mirrors source: `Tests/Endpoints/InstanceEndpointsTests.cs`
- Async test methods must return `Task`

### Previous Story Intelligence

From Story 3-1 implementation:
- `IInstanceManager` is registered as singleton in `AgentRunnerComposer`
- Instance ID format is `Guid.NewGuid().ToString("N")` (32-char hex)
- `ListInstancesAsync` has resilience — catches per-item exceptions
- Path traversal guards exist in `InstanceManager` — no need to duplicate in endpoints
- Delete allows all terminal statuses: Completed, Failed, Cancelled
- No in-memory caching — always reads from disk
- `InstanceManager` requires `IWebHostEnvironment.ContentRootPath` for path resolution

### References

- [Source: _bmad-output/planning-artifacts/epics.md — Epic 3, Story 3.2]
- [Source: _bmad-output/planning-artifacts/architecture.md — API Endpoints table, Error Handling, Data Persistence]
- [Source: _bmad-output/implementation-artifacts/3-1-instance-state-management.md — IInstanceManager interface, dev notes]
- [Source: Shallai.UmbracoAgentRunner/Endpoints/WorkflowEndpoints.cs — controller pattern reference]
- [Source: Shallai.UmbracoAgentRunner/Instances/IInstanceManager.cs — actual interface signature]
- [Source: _bmad-output/project-context.md — all framework rules, naming conventions, testing standards]

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context)

### Debug Log References

- Initial build failure: `User.Identity` NullReferenceException in test context — resolved by setting up `ControllerContext` with `DefaultHttpContext` and `ClaimsPrincipal` in test `[SetUp]`.

### Completion Notes List

- Created 5 REST endpoints (POST create, GET list, GET detail, POST cancel, DELETE) following existing `WorkflowEndpoints` controller pattern
- Created 4 API model DTOs: `CreateInstanceRequest`, `InstanceResponse`, `InstanceDetailResponse`, `ErrorResponse` — all using `required` + `init` pattern matching `WorkflowSummary`
- Added `StepResponse` DTO for per-step detail in `InstanceDetailResponse`
- Implemented Option B for cross-workflow instance lookup: added `FindInstanceAsync(string instanceId)` to `IInstanceManager` interface and `InstanceManager` implementation — scans workflow directories for matching instanceId subfolder with path traversal guard
- Cancel allows both `Running` and `Pending` statuses (story dev notes suggested considering this); returns 409 for terminal statuses (Completed, Failed, Cancelled)
- Delete allows all three terminal statuses (Completed, Failed, Cancelled); returns 409 for Pending/Running
- POST create returns 201 (not 200) with mapped `InstanceResponse`
- All error responses use `{ error, message }` format with machine-readable error codes: `workflow_not_found`, `instance_not_found`, `invalid_status`
- 19 endpoint tests + 3 `FindInstanceAsync` integration tests added, all passing (77 total tests, 0 failures)
- `createdBy` extracted from `User.Identity?.Name ?? "unknown"` per story dev notes

#### Review Pass Fixes (Amelia — Code Review)

- Added `[JsonConverter(typeof(JsonStringEnumConverter))]` to `InstanceStatus` and `StepStatus` enums — API responses now serialize as `"Pending"` not `0`
- Hardened `FindInstanceAsync`: added 32-char hex format validation via `GeneratedRegex` (rejects path traversal before any I/O), moved sandbox check before `File.Exists`, derived `yamlPath` from resolved path, added try/catch for mid-read directory deletion
- Delete endpoint now checks `DeleteInstanceAsync` return value — returns 404 if instance was concurrently deleted
- Added 5 new tests: 4 `FindInstance_ReturnsNull_WhenInstanceIdFormatInvalid` test cases (../traversal, short, uppercase, hyphenated GUID) + 1 `DeleteInstance_Returns404_WhenDeleteReturnsFalse`
- Total test count: 82 (was 77), 0 failures
- 2 findings deferred to Epic 4 (TOCTOU races on cancel/delete — require InstanceManager-level locking)

### Change Log

- 2026-03-30: Story 3.2 implementation complete — instance API endpoints with full CRUD, cancel, cross-workflow lookup, and comprehensive test coverage
- 2026-03-30: Review pass — enum string serialization, FindInstanceAsync security hardening, delete return value check, 5 new tests (82 total)

### File List

- `Shallai.UmbracoAgentRunner/Endpoints/InstanceEndpoints.cs` (new, review-modified — delete return check)
- `Shallai.UmbracoAgentRunner/Models/ApiModels/CreateInstanceRequest.cs` (new)
- `Shallai.UmbracoAgentRunner/Models/ApiModels/InstanceResponse.cs` (new)
- `Shallai.UmbracoAgentRunner/Models/ApiModels/InstanceDetailResponse.cs` (new)
- `Shallai.UmbracoAgentRunner/Models/ApiModels/ErrorResponse.cs` (new)
- `Shallai.UmbracoAgentRunner/Instances/IInstanceManager.cs` (modified — added `FindInstanceAsync`)
- `Shallai.UmbracoAgentRunner/Instances/InstanceManager.cs` (modified — `FindInstanceAsync` with format validation, sandbox-first, try/catch; class now `partial` for `GeneratedRegex`)
- `Shallai.UmbracoAgentRunner/Instances/InstanceStatus.cs` (modified — added `JsonStringEnumConverter`)
- `Shallai.UmbracoAgentRunner/Instances/StepStatus.cs` (modified — added `JsonStringEnumConverter`)
- `Shallai.UmbracoAgentRunner.Tests/Endpoints/InstanceEndpointsTests.cs` (new, review-modified — added delete-returns-false test)
- `Shallai.UmbracoAgentRunner.Tests/Instances/InstanceManagerTests.cs` (modified — FindInstance tests + format validation tests)
