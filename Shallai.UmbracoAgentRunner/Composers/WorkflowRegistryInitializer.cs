using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shallai.UmbracoAgentRunner.Configuration;
using Shallai.UmbracoAgentRunner.Workflows;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Shallai.UmbracoAgentRunner.Composers;

public sealed class WorkflowRegistryInitializer : INotificationHandler<UmbracoApplicationStartedNotification>
{
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly IOptions<AgentRunnerOptions> _options;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<WorkflowRegistryInitializer> _logger;

    public WorkflowRegistryInitializer(
        IWorkflowRegistry workflowRegistry,
        IOptions<AgentRunnerOptions> options,
        IWebHostEnvironment webHostEnvironment,
        ILogger<WorkflowRegistryInitializer> logger)
    {
        _workflowRegistry = workflowRegistry;
        _options = options;
        _webHostEnvironment = webHostEnvironment;
        _logger = logger;
    }

    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        var workflowPath = _options.Value.WorkflowPath;

        if (string.IsNullOrWhiteSpace(workflowPath))
        {
            _logger.LogWarning("WorkflowPath is not configured — workflow registry will be empty");
            return;
        }

        var absolutePath = Path.IsPathRooted(workflowPath)
            ? workflowPath
            : Path.Combine(_webHostEnvironment.ContentRootPath, workflowPath);

        try
        {
            // UmbracoApplicationStartedNotification handler is synchronous — use GetAwaiter().GetResult()
            // since this runs once at startup before any requests are served.
            _workflowRegistry.LoadWorkflowsAsync(absolutePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workflow registry from '{WorkflowsPath}' — registry will be empty", absolutePath);
        }
    }
}
