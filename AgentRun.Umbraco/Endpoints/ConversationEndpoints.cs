using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;

namespace AgentRun.Umbraco.Endpoints;

[ApiController]
[Route("umbraco/api/agentrun")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class ConversationEndpoints : ControllerBase
{
    private readonly IConversationStore _conversationStore;
    private readonly IInstanceManager _instanceManager;

    public ConversationEndpoints(IConversationStore conversationStore, IInstanceManager instanceManager)
    {
        _conversationStore = conversationStore;
        _instanceManager = instanceManager;
    }

    [HttpGet("instances/{instanceId}/conversation/{stepId}")]
    [ProducesResponseType<IReadOnlyList<ConversationEntry>>(200)]
    [ProducesResponseType<ErrorResponse>(404)]
    public async Task<IActionResult> GetConversation(
        string instanceId,
        string stepId,
        CancellationToken cancellationToken)
    {
        var instance = await _instanceManager.FindInstanceAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{instanceId}' was not found."
            });
        }

        if (!instance.Steps.Any(s => s.Id == stepId))
        {
            return NotFound(new ErrorResponse
            {
                Error = "step_not_found",
                Message = $"Step '{stepId}' was not found in instance '{instanceId}'."
            });
        }

        var entries = await _conversationStore.GetHistoryAsync(
            instance.WorkflowAlias, instanceId, stepId, cancellationToken);
        return Ok(entries);
    }
}
