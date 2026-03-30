using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Authorization;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Endpoints;

[ApiController]
[Route("umbraco/api/shallai")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class WorkflowEndpoints : ControllerBase
{
    private readonly IWorkflowRegistry _workflowRegistry;

    public WorkflowEndpoints(IWorkflowRegistry workflowRegistry)
    {
        _workflowRegistry = workflowRegistry;
    }

    [HttpGet("workflows")]
    [ProducesResponseType<WorkflowSummary[]>(200)]
    public IActionResult GetWorkflows()
    {
        var workflows = _workflowRegistry.GetAllWorkflows();
        var summaries = workflows.Select(w => new WorkflowSummary
        {
            Alias = w.Alias,
            Name = w.Definition.Name,
            Description = w.Definition.Description,
            StepCount = w.Definition.Steps.Count,
            Mode = w.Definition.Mode
        }).ToArray();

        return Ok(summaries);
    }
}
