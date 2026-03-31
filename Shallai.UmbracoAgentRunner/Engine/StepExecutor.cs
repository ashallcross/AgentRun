using Microsoft.Extensions.AI;
using Shallai.UmbracoAgentRunner.Instances;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Engine;

public class StepExecutor : IStepExecutor
{
    private readonly IProfileResolver _profileResolver;
    private readonly IPromptAssembler _promptAssembler;
    private readonly IEnumerable<IWorkflowTool> _allTools;
    private readonly IInstanceManager _instanceManager;
    private readonly ILogger<StepExecutor> _logger;

    public StepExecutor(
        IProfileResolver profileResolver,
        IPromptAssembler promptAssembler,
        IEnumerable<IWorkflowTool> allTools,
        IInstanceManager instanceManager,
        ILogger<StepExecutor> logger)
    {
        _profileResolver = profileResolver;
        _promptAssembler = promptAssembler;
        _allTools = allTools;
        _instanceManager = instanceManager;
        _logger = logger;
    }

    public async Task ExecuteStepAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var step = context.Step;
        var workflow = context.Workflow;
        var instance = context.Instance;
        var stepIndex = instance.Steps.FindIndex(s => s.Id == step.Id);

        if (stepIndex == -1)
        {
            _logger.LogError(
                "Step {StepId} not found in instance steps for workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);
            throw new AgentRunnerException(
                $"Step '{step.Id}' not found in instance '{instance.InstanceId}' steps list");
        }

        _logger.LogInformation(
            "Step {StepId} starting for workflow {WorkflowAlias} instance {InstanceId}",
            step.Id, instance.WorkflowAlias, instance.InstanceId);

        try
        {
            // Update step status to Active
            await _instanceManager.UpdateStepStatusAsync(
                instance.WorkflowAlias, instance.InstanceId, stepIndex, StepStatus.Active, cancellationToken);

            // Resolve IChatClient via profile resolver
            var client = await _profileResolver.ResolveAndGetClientAsync(step, workflow, cancellationToken);
            _logger.LogInformation(
                "Profile resolved for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            // Filter tools to only those declared in the step
            var declaredToolNames = step.Tools ?? [];
            var toolDict = _allTools
                .Where(t => declaredToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

            // Build tool descriptions for prompt assembly
            var toolDescriptions = toolDict.Values
                .Select(t => new ToolDescription(t.Name, t.Description))
                .ToList();

            // Assemble prompt
            var promptContext = new PromptAssemblyContext(
                WorkflowFolderPath: context.WorkflowFolderPath,
                Step: step,
                AllSteps: instance.Steps,
                AllStepDefinitions: workflow.Steps,
                InstanceFolderPath: context.InstanceFolderPath,
                DeclaredTools: toolDescriptions);

            var prompt = await _promptAssembler.AssemblePromptAsync(promptContext, cancellationToken);
            _logger.LogInformation(
                "Prompt assembled for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            // Build AIFunction wrappers for declared tools
            var toolExecutionContext = new ToolExecutionContext(
                context.InstanceFolderPath, instance.InstanceId, step.Id, instance.WorkflowAlias);

            var aiTools = new List<AITool>();
            foreach (var tool in toolDict.Values)
            {
                var capturedTool = tool;
                var aiFunction = AIFunctionFactory.Create(
                    async (IDictionary<string, object?> arguments) =>
                        await capturedTool.ExecuteAsync(arguments, toolExecutionContext, cancellationToken),
                    capturedTool.Name,
                    capturedTool.Description);
                aiTools.Add(aiFunction);
            }

            // Build initial messages
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, prompt)
            };

            var chatOptions = new ChatOptions { Tools = aiTools };

            // Run the tool loop
            await ToolLoop.RunAsync(
                client, messages, chatOptions, toolDict, toolExecutionContext, _logger, cancellationToken);

            _logger.LogInformation(
                "Tool loop complete for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            // Update step status to Complete
            await _instanceManager.UpdateStepStatusAsync(
                instance.WorkflowAlias, instance.InstanceId, stepIndex, StepStatus.Complete, cancellationToken);

            _logger.LogInformation(
                "Step {StepId} completed for workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Step {StepId} failed for workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            try
            {
                await _instanceManager.UpdateStepStatusAsync(
                    instance.WorkflowAlias, instance.InstanceId, stepIndex, StepStatus.Error, CancellationToken.None);
            }
            catch (Exception statusEx)
            {
                _logger.LogCritical(statusEx,
                    "Failed to update step status to Error for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                    step.Id, instance.WorkflowAlias, instance.InstanceId);
            }
        }
    }
}
