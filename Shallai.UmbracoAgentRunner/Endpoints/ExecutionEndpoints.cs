using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Engine.Events;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Endpoints;

[ApiController]
[Route("umbraco/api/shallai")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class ExecutionEndpoints : ControllerBase
{
    private readonly IInstanceManager _instanceManager;
    private readonly IProfileResolver _profileResolver;
    private readonly IWorkflowOrchestrator _workflowOrchestrator;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly ILogger<ExecutionEndpoints> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ExecutionEndpoints(
        IInstanceManager instanceManager,
        IProfileResolver profileResolver,
        IWorkflowOrchestrator workflowOrchestrator,
        IWorkflowRegistry workflowRegistry,
        ILogger<ExecutionEndpoints> logger,
        ILoggerFactory loggerFactory)
    {
        _instanceManager = instanceManager;
        _profileResolver = profileResolver;
        _workflowOrchestrator = workflowOrchestrator;
        _workflowRegistry = workflowRegistry;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    [HttpPost("instances/{id}/start")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(400)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public async Task<IActionResult> StartInstance(string id, CancellationToken cancellationToken)
    {
        var instance = await _instanceManager.FindInstanceAsync(id, cancellationToken);
        if (instance is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found."
            });
        }

        // Reject completed/failed/cancelled
        if (instance.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled)
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_status",
                Message = $"Cannot start instance with status '{instance.Status}'."
            });
        }

        // Reject already running (concurrent execution guard)
        if (instance.Status == InstanceStatus.Running)
        {
            return Conflict(new ErrorResponse
            {
                Error = "already_running",
                Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
            });
        }

        // Provider prerequisite check — resolve workflow definition for profile fallback
        var registered = _workflowRegistry.GetWorkflow(instance.WorkflowAlias);
        if (!await _profileResolver.HasConfiguredProviderAsync(registered?.Definition, cancellationToken))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "no_provider",
                Message = "Configure an AI provider in Umbraco.AI before workflows can run."
            });
        }

        // Set instance to Running
        await _instanceManager.SetInstanceStatusAsync(
            instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Running, cancellationToken);

        // Configure SSE response
        SseHelper.ConfigureSseResponse(Response);

        var emitter = new SseEventEmitter(
            Response.Body, _loggerFactory.CreateLogger<SseEventEmitter>());

        try
        {
            await _workflowOrchestrator.ExecuteNextStepAsync(
                instance.WorkflowAlias, instance.InstanceId, emitter, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _instanceManager.SetInstanceStatusAsync(
                    instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Failed, CancellationToken.None);
            }
            catch (Exception statusEx)
            {
                _logger.LogCritical(statusEx,
                    "Failed to set instance {InstanceId} status to Failed after cancellation",
                    instance.InstanceId);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Execution failed for instance {InstanceId} of workflow {WorkflowAlias}",
                instance.InstanceId, instance.WorkflowAlias);

            try
            {
                await _instanceManager.SetInstanceStatusAsync(
                    instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Failed, CancellationToken.None);
            }
            catch (Exception statusEx)
            {
                _logger.LogCritical(statusEx,
                    "Failed to set instance {InstanceId} status to Failed",
                    instance.InstanceId);
            }

            try
            {
                await emitter.EmitRunErrorAsync("execution_error", ex.Message, CancellationToken.None);
            }
            catch (Exception sseEx)
            {
                _logger.LogWarning(sseEx,
                    "Failed to emit run.error SSE event for instance {InstanceId}",
                    instance.InstanceId);
            }
        }

        // SSE stream ends naturally when the method returns
        return new EmptyResult();
    }
}
