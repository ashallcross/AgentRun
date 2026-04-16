using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;
using AgentRun.Umbraco.Security;

namespace AgentRun.Umbraco.Endpoints;

[ApiController]
[Route("umbraco/api/agentrun")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class ArtifactEndpoints : ControllerBase
{
    private readonly IInstanceManager _instanceManager;

    public ArtifactEndpoints(IInstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }

    [HttpGet("instances/{instanceId}/artifacts/{*filePath}")]
    [ProducesResponseType(typeof(string), 200, "text/plain")]
    [ProducesResponseType<ErrorResponse>(400)]
    [ProducesResponseType<ErrorResponse>(404)]
    public async Task<IActionResult> GetArtifact(
        string instanceId,
        string filePath,
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

        var instanceFolderPath = _instanceManager.GetInstanceFolderPath(instance.WorkflowAlias, instanceId);

        string canonicalPath;
        try
        {
            canonicalPath = PathSandbox.ValidatePath(filePath, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "invalid_path",
                Message = "The requested file path is not permitted."
            });
        }

        if (!System.IO.File.Exists(canonicalPath))
        {
            return NotFound(new ErrorResponse
            {
                Error = "artifact_not_found",
                Message = $"Artifact '{filePath}' was not found."
            });
        }

        var content = await System.IO.File.ReadAllTextAsync(canonicalPath, cancellationToken);
        return Content(content, "text/plain; charset=utf-8");
    }
}
