using Microsoft.Extensions.Logging;

namespace Shallai.UmbracoAgentRunner.Workflows;

public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private const string WorkflowFileName = "workflow.yaml";

    private readonly IWorkflowParser _parser;
    private readonly IWorkflowValidator _validator;
    private readonly ILogger<WorkflowRegistry> _logger;
    private readonly Dictionary<string, RegisteredWorkflow> _workflows = new(StringComparer.Ordinal);

    public WorkflowRegistry(
        IWorkflowParser parser,
        IWorkflowValidator validator,
        ILogger<WorkflowRegistry> logger)
    {
        _parser = parser;
        _validator = validator;
        _logger = logger;
    }

    public IReadOnlyList<RegisteredWorkflow> GetAllWorkflows()
    {
        return _workflows.Values.ToList().AsReadOnly();
    }

    public RegisteredWorkflow? GetWorkflow(string alias)
    {
        _workflows.TryGetValue(alias, out var workflow);
        return workflow;
    }

    public async Task LoadWorkflowsAsync(string workflowsRootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(workflowsRootPath))
        {
            _logger.LogInformation("Workflows directory '{WorkflowsPath}' does not exist — no workflows loaded", workflowsRootPath);
            return;
        }

        var subdirectories = Directory.GetDirectories(workflowsRootPath);

        foreach (var subDir in subdirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workflowFile = Path.Combine(subDir, WorkflowFileName);

            if (!File.Exists(workflowFile))
            {
                continue;
            }

            var alias = new DirectoryInfo(subDir).Name;

            try
            {
                await LoadSingleWorkflowAsync(alias, subDir, workflowFile, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Workflow '{WorkflowAlias}': I/O error reading workflow file — skipped", alias);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Workflow '{WorkflowAlias}': access denied reading workflow file — skipped", alias);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Workflow '{WorkflowAlias}': unexpected error loading workflow — skipped", alias);
            }
        }

        _logger.LogInformation("Workflow registry loaded {Count} workflow(s)", _workflows.Count);
    }

    private async Task LoadSingleWorkflowAsync(
        string alias,
        string folderPath,
        string workflowFile,
        CancellationToken cancellationToken)
    {
        var yamlContent = await File.ReadAllTextAsync(workflowFile, cancellationToken).ConfigureAwait(false);

        var validationResult = _validator.Validate(yamlContent);

        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                _logger.LogError(
                    "Workflow '{WorkflowAlias}': validation error at '{FieldPath}' — {Message}",
                    alias,
                    error.FieldPath,
                    error.Message);
            }

            return;
        }

        WorkflowDefinition definition;

        try
        {
            definition = _parser.Parse(yamlContent);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Workflow '{WorkflowAlias}': failed to parse workflow YAML — skipped", alias);
            return;
        }

        VerifyAgentFiles(alias, folderPath, definition);

        _workflows[alias] = new RegisteredWorkflow(alias, folderPath, definition);
    }

    private void VerifyAgentFiles(string alias, string folderPath, WorkflowDefinition definition)
    {
        foreach (var step in definition.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Agent))
            {
                continue;
            }

            var agentPath = Path.Combine(folderPath, step.Agent);

            if (!File.Exists(agentPath))
            {
                _logger.LogWarning(
                    "Workflow '{WorkflowAlias}': agent file '{AgentPath}' referenced by step '{StepId}' not found",
                    alias,
                    step.Agent,
                    step.Id);
            }
        }
    }
}
