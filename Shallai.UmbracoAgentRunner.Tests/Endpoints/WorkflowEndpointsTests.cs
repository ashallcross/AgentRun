using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Endpoints;
using Shallai.UmbracoAgentRunner.Models.ApiModels;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Endpoints;

[TestFixture]
public class WorkflowEndpointsTests
{
    private IWorkflowRegistry _registry = null!;
    private WorkflowEndpoints _endpoints = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IWorkflowRegistry>();
        _endpoints = new WorkflowEndpoints(_registry);
    }

    [Test]
    public void GetWorkflows_ReturnsMappedSummaries()
    {
        var workflows = new List<RegisteredWorkflow>
        {
            new("content-audit", "/workflows/content-audit", new WorkflowDefinition
            {
                Name = "Content Audit",
                Description = "Audits content quality",
                Mode = "interactive",
                Steps =
                [
                    new StepDefinition { Id = "step-1", Name = "Analyse", Agent = "agent.md" },
                    new StepDefinition { Id = "step-2", Name = "Report", Agent = "report.md" },
                    new StepDefinition { Id = "step-3", Name = "Fix", Agent = "fix.md" }
                ]
            }),
            new("seo-check", "/workflows/seo-check", new WorkflowDefinition
            {
                Name = "SEO Check",
                Description = "Checks SEO compliance",
                Mode = "autonomous",
                Steps =
                [
                    new StepDefinition { Id = "step-1", Name = "Scan", Agent = "scan.md" }
                ]
            })
        };

        _registry.GetAllWorkflows().Returns(workflows.AsReadOnly());

        var result = _endpoints.GetWorkflows();

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var summaries = okResult.Value as WorkflowSummary[];
        Assert.That(summaries, Is.Not.Null);
        Assert.That(summaries!, Has.Length.EqualTo(2));

        Assert.That(summaries[0].Alias, Is.EqualTo("content-audit"));
        Assert.That(summaries[0].Name, Is.EqualTo("Content Audit"));
        Assert.That(summaries[0].Description, Is.EqualTo("Audits content quality"));
        Assert.That(summaries[0].StepCount, Is.EqualTo(3));
        Assert.That(summaries[0].Mode, Is.EqualTo("interactive"));

        Assert.That(summaries[1].Alias, Is.EqualTo("seo-check"));
        Assert.That(summaries[1].Name, Is.EqualTo("SEO Check"));
        Assert.That(summaries[1].StepCount, Is.EqualTo(1));
        Assert.That(summaries[1].Mode, Is.EqualTo("autonomous"));
    }

    [Test]
    public void GetWorkflows_ReturnsEmptyArray_WhenRegistryHasNoWorkflows()
    {
        _registry.GetAllWorkflows().Returns(new List<RegisteredWorkflow>().AsReadOnly());

        var result = _endpoints.GetWorkflows();

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var summaries = okResult!.Value as WorkflowSummary[];
        Assert.That(summaries, Is.Not.Null);
        Assert.That(summaries!, Is.Empty);
    }

    [Test]
    public void GetWorkflows_StepCountMapsToDefinitionStepsCount()
    {
        var steps = new List<StepDefinition>
        {
            new() { Id = "s1", Name = "Step 1", Agent = "a.md" },
            new() { Id = "s2", Name = "Step 2", Agent = "b.md" },
            new() { Id = "s3", Name = "Step 3", Agent = "c.md" },
            new() { Id = "s4", Name = "Step 4", Agent = "d.md" },
            new() { Id = "s5", Name = "Step 5", Agent = "e.md" }
        };

        var workflows = new List<RegisteredWorkflow>
        {
            new("multi-step", "/workflows/multi-step", new WorkflowDefinition
            {
                Name = "Multi Step",
                Description = "Has many steps",
                Mode = "interactive",
                Steps = steps
            })
        };

        _registry.GetAllWorkflows().Returns(workflows.AsReadOnly());

        var result = _endpoints.GetWorkflows();

        var okResult = result as OkObjectResult;
        var summaries = okResult!.Value as WorkflowSummary[];
        Assert.That(summaries![0].StepCount, Is.EqualTo(5));
    }
}
