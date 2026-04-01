using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Endpoints;

[ApiController]
[Route("umbraco/api/shallai")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class InstanceEndpoints : ControllerBase
{
    private readonly IInstanceManager _instanceManager;
    private readonly IWorkflowRegistry _workflowRegistry;

    public InstanceEndpoints(IInstanceManager instanceManager, IWorkflowRegistry workflowRegistry)
    {
        _instanceManager = instanceManager;
        _workflowRegistry = workflowRegistry;
    }

    [HttpPost("instances")]
    [ProducesResponseType<InstanceResponse>(201)]
    [ProducesResponseType<ErrorResponse>(404)]
    public async Task<IActionResult> CreateInstance(
        [FromBody] CreateInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var registered = _workflowRegistry.GetWorkflow(request.WorkflowAlias);
        if (registered is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "workflow_not_found",
                Message = $"Workflow '{request.WorkflowAlias}' was not found in the registry."
            });
        }

        var createdBy = User.Identity?.Name ?? "unknown";
        var state = await _instanceManager.CreateInstanceAsync(
            request.WorkflowAlias, registered.Definition, createdBy, cancellationToken);

        return StatusCode(201, MapToResponse(state));
    }

    [HttpGet("instances")]
    [ProducesResponseType<InstanceResponse[]>(200)]
    public async Task<IActionResult> ListInstances(
        [FromQuery] string? workflowAlias,
        CancellationToken cancellationToken)
    {
        var instances = await _instanceManager.ListInstancesAsync(workflowAlias, cancellationToken);
        var responses = instances.Select(MapToResponse).ToArray();
        return Ok(responses);
    }

    [HttpGet("instances/{id}")]
    [ProducesResponseType<InstanceDetailResponse>(200)]
    [ProducesResponseType<ErrorResponse>(404)]
    public async Task<IActionResult> GetInstance(string id, CancellationToken cancellationToken)
    {
        var state = await _instanceManager.FindInstanceAsync(id, cancellationToken);
        if (state is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found."
            });
        }

        return Ok(MapToDetailResponse(state));
    }

    [HttpPost("instances/{id}/cancel")]
    [ProducesResponseType<InstanceResponse>(200)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public async Task<IActionResult> CancelInstance(string id, CancellationToken cancellationToken)
    {
        var state = await _instanceManager.FindInstanceAsync(id, cancellationToken);
        if (state is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found."
            });
        }

        if (state.Status is not (InstanceStatus.Running or InstanceStatus.Pending))
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_status",
                Message = $"Cannot cancel instance with status '{state.Status}'. Only running or pending instances can be cancelled."
            });
        }

        var updated = await _instanceManager.SetInstanceStatusAsync(
            state.WorkflowAlias, state.InstanceId, InstanceStatus.Cancelled, cancellationToken);

        return Ok(MapToResponse(updated));
    }

    [HttpDelete("instances/{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public async Task<IActionResult> DeleteInstance(string id, CancellationToken cancellationToken)
    {
        var state = await _instanceManager.FindInstanceAsync(id, cancellationToken);
        if (state is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found."
            });
        }

        if (state.Status is not (InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled))
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_status",
                Message = $"Cannot delete instance with status '{state.Status}'. Only completed, failed, or cancelled instances can be deleted."
            });
        }

        var deleted = await _instanceManager.DeleteInstanceAsync(state.WorkflowAlias, state.InstanceId, cancellationToken);
        if (!deleted)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found on disk."
            });
        }

        return NoContent();
    }

    private static InstanceResponse MapToResponse(InstanceState state) => new()
    {
        Id = state.InstanceId,
        WorkflowAlias = state.WorkflowAlias,
        Status = state.Status,
        CurrentStepIndex = state.CurrentStepIndex,
        CreatedAt = state.CreatedAt,
        UpdatedAt = state.UpdatedAt
    };

    private InstanceDetailResponse MapToDetailResponse(InstanceState state)
    {
        var registered = _workflowRegistry.GetWorkflow(state.WorkflowAlias);
        var definition = registered?.Definition;

        return new InstanceDetailResponse
        {
            Id = state.InstanceId,
            WorkflowAlias = state.WorkflowAlias,
            WorkflowName = definition?.Name ?? string.Empty,
            WorkflowMode = definition?.Mode ?? "interactive",
            Status = state.Status,
            CurrentStepIndex = state.CurrentStepIndex,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt,
            CreatedBy = state.CreatedBy,
            Steps = state.Steps.Select(s =>
            {
                var stepDef = definition?.Steps.FirstOrDefault(d => d.Id == s.Id);
                return new StepResponse
                {
                    Id = s.Id,
                    Name = stepDef?.Name ?? s.Id,
                    Status = s.Status,
                    StartedAt = s.StartedAt,
                    CompletedAt = s.CompletedAt,
                    WritesTo = stepDef?.WritesTo?.ToArray()
                };
            }).ToArray()
        };
    }
}
