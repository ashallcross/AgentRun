using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Services;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Engine;

public class WorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly IInstanceManager _instanceManager;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly IStepExecutor _stepExecutor;
    private readonly IConversationStore _conversationStore;
    private readonly IActiveInstanceRegistry _activeInstanceRegistry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WorkflowOrchestrator> _logger;

    public WorkflowOrchestrator(
        IInstanceManager instanceManager,
        IWorkflowRegistry workflowRegistry,
        IStepExecutor stepExecutor,
        IConversationStore conversationStore,
        IActiveInstanceRegistry activeInstanceRegistry,
        ILoggerFactory loggerFactory,
        ILogger<WorkflowOrchestrator> logger)
    {
        _instanceManager = instanceManager;
        _workflowRegistry = workflowRegistry;
        _stepExecutor = stepExecutor;
        _conversationStore = conversationStore;
        _activeInstanceRegistry = activeInstanceRegistry;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task ExecuteNextStepAsync(
        string workflowAlias,
        string instanceId,
        ISseEventEmitter emitter,
        CancellationToken cancellationToken)
    {
        var registered = _workflowRegistry.GetWorkflow(workflowAlias)
            ?? throw new AgentRunException($"Workflow '{workflowAlias}' not found in registry");

        var workflow = registered.Definition;
        var isFirstStep = true;
        var userMessageReader = _activeInstanceRegistry.RegisterInstance(instanceId);

        // Story 10.8: combine the HTTP request token (existing) with the
        // per-instance cancellation token (new). Either source firing cancels
        // the run. Must dispose the linked source in finally to avoid leaking
        // the subscription chain.
        var instanceToken = _activeInstanceRegistry.GetCancellationToken(instanceId)
            ?? CancellationToken.None;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, instanceToken);
        var runToken = linkedCts.Token;

        try
        {
        while (true)
        {
            runToken.ThrowIfCancellationRequested();

            var instance = await _instanceManager.FindInstanceAsync(instanceId, runToken)
                ?? throw new AgentRunException($"Instance '{instanceId}' not found");

            var stepIndex = instance.CurrentStepIndex;

            if (stepIndex < 0 || stepIndex >= workflow.Steps.Count)
            {
                throw new AgentRunException(
                    $"CurrentStepIndex {stepIndex} is out of range for workflow '{workflowAlias}' with {workflow.Steps.Count} steps");
            }

            var step = workflow.Steps[stepIndex];

            _logger.LogInformation(
                "Orchestrator starting step {StepId} (index {StepIndex}) for workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, stepIndex, workflowAlias, instanceId);

            // Emit run.started only for the first step in this execution
            if (isFirstStep)
            {
                await emitter.EmitRunStartedAsync(instanceId, runToken);
                isFirstStep = false;
            }

            // Emit step.started
            await emitter.EmitStepStartedAsync(step.Id, step.Name, runToken);

            // Create conversation recorder for this step execution
            var recorder = new ConversationRecorder(
                _conversationStore, workflowAlias, instanceId, step.Id,
                _loggerFactory.CreateLogger<ConversationRecorder>());

            // Record step.started system message
            await recorder.RecordSystemMessageAsync($"Starting {step.Name}...", runToken);

            // Build StepExecutionContext
            var instanceFolderPath = _instanceManager.GetInstanceFolderPath(workflowAlias, instanceId);

            var context = new StepExecutionContext(
                Workflow: workflow,
                Step: step,
                Instance: instance,
                InstanceFolderPath: instanceFolderPath,
                WorkflowFolderPath: registered.FolderPath,
                EventEmitter: emitter,
                ConversationRecorder: recorder,
                UserMessageReader: userMessageReader);

            // Execute the step
            await _stepExecutor.ExecuteStepAsync(context, runToken);

            // Re-load instance state after step execution (StepExecutor updates step status)
            instance = await _instanceManager.FindInstanceAsync(instanceId, runToken)
                ?? throw new AgentRunException($"Instance '{instanceId}' not found after step execution");

            var stepState = instance.Steps[stepIndex];

            if (stepState.Status == StepStatus.Error)
            {
                var errorCode = context.LlmError?.ErrorCode ?? "step_failed";
                var errorMessage = context.LlmError?.UserMessage ?? $"Step '{step.Name}' failed";

                // Record step.finished system message
                await recorder.RecordSystemMessageAsync(errorMessage, CancellationToken.None);

                // Step failed — emit step.finished and run.error
                await emitter.EmitStepFinishedAsync(step.Id, "Error", CancellationToken.None);

                try
                {
                    await _instanceManager.SetInstanceStatusAsync(
                        workflowAlias, instanceId, InstanceStatus.Failed, CancellationToken.None);
                }
                catch (Exception statusEx)
                {
                    _logger.LogCritical(statusEx,
                        "Failed to set instance {InstanceId} status to Failed for workflow {WorkflowAlias}",
                        instanceId, workflowAlias);
                }

                await emitter.EmitRunErrorAsync(errorCode, errorMessage, CancellationToken.None);
                return;
            }

            // Record step.finished system message
            await recorder.RecordSystemMessageAsync($"{step.Name} completed", runToken);

            // Step completed successfully
            await emitter.EmitStepFinishedAsync(step.Id, "Complete", runToken);

            var isLastStep = stepIndex >= workflow.Steps.Count - 1;

            if (isLastStep)
            {
                // All steps done — mark instance Completed
                await _instanceManager.SetInstanceStatusAsync(
                    workflowAlias, instanceId, InstanceStatus.Completed, runToken);

                await emitter.EmitRunFinishedAsync(instanceId, "Completed", runToken);

                _logger.LogInformation(
                    "Workflow {WorkflowAlias} instance {InstanceId} completed all steps",
                    workflowAlias, instanceId);
                return;
            }

            // More steps remain — advance CurrentStepIndex
            await _instanceManager.AdvanceStepAsync(workflowAlias, instanceId, runToken);

            if (!string.Equals(workflow.Mode, "autonomous", StringComparison.OrdinalIgnoreCase))
            {
                // Interactive mode: return — user must POST /start again
                return;
            }

            // Autonomous mode: emit system message, brief delay, then loop
            var nextStep = workflow.Steps[stepIndex + 1];
            await emitter.EmitSystemMessageAsync(
                $"Auto-advancing to {nextStep.Name}...", runToken);

            await Task.Delay(1000, runToken);
        }
        }
        finally
        {
            _activeInstanceRegistry.UnregisterInstance(instanceId);
        }
    }
}
