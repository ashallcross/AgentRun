using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Endpoints;

[ApiController]
[Route("umbraco/api/agentrun")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class ExecutionEndpoints : ControllerBase
{
    private readonly IInstanceManager _instanceManager;
    private readonly IProfileResolver _profileResolver;
    private readonly IWorkflowOrchestrator _workflowOrchestrator;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly IConversationStore _conversationStore;
    private readonly IActiveInstanceRegistry _activeInstanceRegistry;
    private readonly ILogger<ExecutionEndpoints> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ExecutionEndpoints(
        IInstanceManager instanceManager,
        IProfileResolver profileResolver,
        IWorkflowOrchestrator workflowOrchestrator,
        IWorkflowRegistry workflowRegistry,
        IConversationStore conversationStore,
        IActiveInstanceRegistry activeInstanceRegistry,
        ILogger<ExecutionEndpoints> logger,
        ILoggerFactory loggerFactory)
    {
        _instanceManager = instanceManager;
        _profileResolver = profileResolver;
        _workflowOrchestrator = workflowOrchestrator;
        _workflowRegistry = workflowRegistry;
        _conversationStore = conversationStore;
        _activeInstanceRegistry = activeInstanceRegistry;
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

        // Reject already running (concurrent execution guard) — but allow restart
        // for interactive mode between steps when not actively executing
        if (instance.Status == InstanceStatus.Running)
        {
            if (_activeInstanceRegistry.GetMessageWriter(id) is not null)
            {
                return Conflict(new ErrorResponse
                {
                    Error = "already_running",
                    Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
                });
            }
            // Not actively executing — allow restart (interactive mode between steps)
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

        // Set instance to Running (skip if already Running — interactive mode between steps)
        if (instance.Status != InstanceStatus.Running)
        {
            await _instanceManager.SetInstanceStatusAsync(
                instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Running, cancellationToken);
        }

        return await ExecuteSseAsync(instance, cancellationToken);
    }

    [HttpPost("instances/{id}/retry")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public async Task<IActionResult> RetryInstance(string id, CancellationToken cancellationToken)
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

        if (instance.Status != InstanceStatus.Failed)
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_state",
                Message = "Instance is not in a failed state"
            });
        }

        // Find the step in error
        var errorStepIndex = instance.Steps.FindIndex(s => s.Status == StepStatus.Error);
        if (errorStepIndex == -1)
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_state",
                Message = "No step in error state found"
            });
        }

        var errorStep = instance.Steps[errorStepIndex];

        // Truncate the failed assistant message from conversation
        await _conversationStore.TruncateLastAssistantEntryAsync(
            instance.WorkflowAlias, instance.InstanceId, errorStep.Id, cancellationToken);

        // Reset step to Pending so the executor handles the Active transition
        await _instanceManager.UpdateStepStatusAsync(
            instance.WorkflowAlias, instance.InstanceId, errorStepIndex, StepStatus.Pending, cancellationToken);

        // Set instance back to Running
        instance = await _instanceManager.SetInstanceStatusAsync(
            instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Running, cancellationToken);

        return await ExecuteSseAsync(instance, cancellationToken);
    }

    private async Task<IActionResult> ExecuteSseAsync(InstanceState instance, CancellationToken cancellationToken)
    {
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
            // Story 10.8: if the cancel endpoint already persisted Cancelled,
            // preserve that status. Writing Failed here would overwrite the
            // user's explicit cancel with a disconnect-style failure. A fresh
            // FindInstanceAsync is required because the in-memory `instance`
            // variable was loaded before the cancel endpoint mutated the YAML.
            // Story 10.9 will refine the disconnect path (status=Running) —
            // 10.8 only touches the cancel path here.
            var current = await _instanceManager.FindInstanceAsync(
                instance.InstanceId, CancellationToken.None);

            if (current is not null && current.Status == InstanceStatus.Cancelled)
            {
                // Deliberate server-initiated cancellation (cancel endpoint
                // already persisted Cancelled). The SSE stream ends cleanly —
                // do NOT rethrow. Rethrowing produces a 100-line "unhandled
                // exception" log every time Cancel is clicked because the OCE
                // surfaces as an aborted controller action, even though the
                // run was stopped correctly. Emit a terminal run.finished event
                // so non-initiating observers can distinguish cancel from a
                // dropped connection.
                _logger.LogInformation(
                    "Cancellation observed for instance {InstanceId}; preserving Cancelled status",
                    instance.InstanceId);

                try
                {
                    await emitter.EmitRunFinishedAsync(
                        instance.InstanceId, "Cancelled", CancellationToken.None);
                }
                catch (Exception emitEx)
                {
                    _logger.LogDebug(emitEx,
                        "Failed to emit run.finished(Cancelled) for instance {InstanceId}; client stream likely already closed",
                        instance.InstanceId);
                }

                return new EmptyResult();
            }

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

        return new EmptyResult();
    }

    [HttpPost("instances/{id}/message")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(400)]
    [ProducesResponseType<ErrorResponse>(409)]
    public IActionResult SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "empty_message",
                Message = "Message cannot be empty."
            });
        }

        var writer = _activeInstanceRegistry.GetMessageWriter(id);
        if (writer is null)
        {
            return Conflict(new ErrorResponse
            {
                Error = "not_running",
                Message = $"Instance '{id}' is not currently executing a step."
            });
        }

        writer.TryWrite(request.Message);
        return Ok();
    }
}
