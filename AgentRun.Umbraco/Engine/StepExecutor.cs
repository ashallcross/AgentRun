using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Engine;

public class StepExecutor : IStepExecutor
{
    private readonly IProfileResolver _profileResolver;
    private readonly IPromptAssembler _promptAssembler;
    private readonly IEnumerable<IWorkflowTool> _allTools;
    private readonly IInstanceManager _instanceManager;
    private readonly IConversationStore _conversationStore;
    private readonly IArtifactValidator _artifactValidator;
    private readonly ICompletionChecker _completionChecker;
    private readonly IToolLimitResolver _toolLimitResolver;
    private readonly IStepExecutionFailureHandler _failureHandler;
    private readonly ILogger<StepExecutor> _logger;

    public StepExecutor(
        IProfileResolver profileResolver,
        IPromptAssembler promptAssembler,
        IEnumerable<IWorkflowTool> allTools,
        IInstanceManager instanceManager,
        IConversationStore conversationStore,
        IArtifactValidator artifactValidator,
        ICompletionChecker completionChecker,
        IToolLimitResolver toolLimitResolver,
        IStepExecutionFailureHandler failureHandler,
        ILogger<StepExecutor> logger)
    {
        _profileResolver = profileResolver;
        _promptAssembler = promptAssembler;
        _allTools = allTools;
        _instanceManager = instanceManager;
        _conversationStore = conversationStore;
        _artifactValidator = artifactValidator;
        _completionChecker = completionChecker;
        _toolLimitResolver = toolLimitResolver;
        _failureHandler = failureHandler;
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
            throw new AgentRunException(
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

            // Validate reads_from artifacts before calling the LLM
            var validationResult = await _artifactValidator.ValidateInputArtifactsAsync(
                step, context.InstanceFolderPath, cancellationToken);

            if (!validationResult.Passed)
            {
                _logger.LogError(
                    "Input artifact validation failed for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}: missing files {MissingFiles}",
                    step.Id, instance.WorkflowAlias, instance.InstanceId,
                    string.Join(", ", validationResult.MissingFiles));

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

                return;
            }

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

            // Assemble prompt.
            // Story 11.7 — InstanceId flows through so `{instance_id}` resolves;
            // WorkflowConfig is taken from the loaded WorkflowDefinition so
            // `{config_key}` tokens resolve from the workflow's root `config:`
            // block. Both fields default to no-op values when absent.
            var promptContext = new PromptAssemblyContext(
                WorkflowFolderPath: context.WorkflowFolderPath,
                Step: step,
                AllSteps: instance.Steps,
                AllStepDefinitions: workflow.Steps,
                InstanceFolderPath: context.InstanceFolderPath,
                DeclaredTools: toolDescriptions,
                InstanceId: instance.InstanceId,
                WorkflowConfig: workflow.Config);

            var prompt = await _promptAssembler.AssemblePromptAsync(promptContext, cancellationToken);
            _logger.LogInformation(
                "Prompt assembled for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            // Build declaration-only tool descriptors for the LLM.
            // Using AsDeclarationOnly() strips the executable delegate so that
            // FunctionInvokingChatClient in the Umbraco.AI middleware pipeline
            // won't auto-execute tools — our ToolLoop handles execution via declaredTools.
            var toolExecutionContext = new ToolExecutionContext(
                context.InstanceFolderPath, instance.InstanceId, step.Id, instance.WorkflowAlias)
            {
                Step = step,
                Workflow = workflow
            };

            var aiTools = new List<AITool>();
            foreach (var tool in toolDict.Values)
            {
                aiTools.Add(new ToolDeclaration(tool.Name, tool.Description, tool.ParameterSchema));
            }

            // Build initial messages — start with fresh system prompt
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, prompt)
            };

            // Load existing conversation history (retry scenario — prior tool calls and messages)
            var history = await _conversationStore.GetHistoryAsync(
                instance.WorkflowAlias, instance.InstanceId, step.Id, cancellationToken);

            if (history.Count > 0)
            {
                _logger.LogInformation(
                    "Loading {EntryCount} conversation history entries for retry of step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                    history.Count, step.Id, instance.WorkflowAlias, instance.InstanceId);

                messages.AddRange(ConvertHistoryToMessages(history));
            }

            // Hardcoded MaxOutputTokens = 32768 to avoid a silent-truncation
            // stall mode. The Microsoft.Extensions.AI Anthropic provider
            // defaults to ~4096 output tokens — Sonnet-class models with
            // thinking enabled consume a meaningful chunk of that on internal
            // thinking tokens before any visible output, and the ceiling is
            // hit mid-write_file presenting as FinishReason=length with
            // accumulatedTextLength=0 (detected via empty-turn FinishReason
            // telemetry in StallRecoveryPolicy). Sonnet 4.6 documented max
            // output is 64k; 32k leaves comfortable headroom for thinking-
            // token bursts plus a multi-page markdown write while still bounded
            // enough to prevent runaway generation. Per-step / per-workflow
            // override via IToolLimitResolver is a planned follow-up; until
            // then this is the global default for every workflow.
            var chatOptions = new ChatOptions
            {
                Tools = aiTools,
                MaxOutputTokens = 32768
            };

            // Build completion check delegate for interactive mode early exit
            Func<CancellationToken, Task<bool>>? completionCheck = step.CompletionCheck is not null
                ? async ct => (await _completionChecker.CheckAsync(step.CompletionCheck, context.InstanceFolderPath, ct)).Passed
                : null;

            var compactionThreshold = _toolLimitResolver.ResolveCompactionTurnThreshold(step, workflow);

            // Run the tool loop
            await ToolLoop.RunAsync(
                client, messages, chatOptions, toolDict, toolExecutionContext, _logger, cancellationToken,
                context.UserMessageReader, context.EventEmitter, context.ConversationRecorder,
                completionCheck, _toolLimitResolver, compactionTurnThreshold: compactionThreshold);

            _logger.LogInformation(
                "Tool loop complete for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            // Run completion check
            var completionResult = await _completionChecker.CheckAsync(
                step.CompletionCheck, context.InstanceFolderPath, cancellationToken);

            if (!completionResult.Passed)
            {
                _logger.LogWarning(
                    "Completion check failed for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}: missing files {MissingFiles}",
                    step.Id, instance.WorkflowAlias, instance.InstanceId,
                    string.Join(", ", completionResult.MissingFiles));

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

                return;
            }

            // Update step status to Complete
            await _instanceManager.UpdateStepStatusAsync(
                instance.WorkflowAlias, instance.InstanceId, stepIndex, StepStatus.Complete, cancellationToken);

            _logger.LogInformation(
                "Step {StepId} completed for workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Step {StepId} failed for workflow {WorkflowAlias} instance {InstanceId}",
                step.Id, instance.WorkflowAlias, instance.InstanceId);

            // Classification delegated to IStepExecutionFailureHandler. The
            // handler preserves the AgentRunException → bypass-classifier
            // invariant (engine-domain exceptions must not be routed through
            // LlmErrorClassifier, or their messages get masked as generic
            // provider failures); see StepExecutionFailureHandlerTests.
            context.LlmError = _failureHandler.Classify(ex);

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

    internal static IEnumerable<ChatMessage> ConvertHistoryToMessages(IReadOnlyList<ConversationEntry> history)
    {
        var messages = new List<ChatMessage>();

        foreach (var entry in history)
        {
            switch (entry.Role)
            {
                case "system":
                    // Skip — fresh system prompt is already prepended
                    break;

                case "user":
                    messages.Add(new ChatMessage(ChatRole.User, entry.Content));
                    break;

                case "assistant" when entry.ToolCallId is not null:
                {
                    // Tool call entry — group consecutive tool calls into one ChatMessage
                    var last = messages.Count > 0 ? messages[^1] : null;
                    if (last is not null && last.Role == ChatRole.Assistant
                        && last.Contents.OfType<FunctionCallContent>().Any())
                    {
                        // Append to existing tool-call assistant message
                        last.Contents.Add(new FunctionCallContent(
                            entry.ToolCallId,
                            entry.ToolName ?? string.Empty,
                            ParseArguments(entry.ToolArguments)));
                    }
                    else
                    {
                        var msg = new ChatMessage(ChatRole.Assistant,
                        [
                            new FunctionCallContent(
                                entry.ToolCallId,
                                entry.ToolName ?? string.Empty,
                                ParseArguments(entry.ToolArguments))
                        ]);
                        messages.Add(msg);
                    }

                    break;
                }

                case "assistant":
                    messages.Add(new ChatMessage(ChatRole.Assistant, entry.Content));
                    break;

                case "tool" when entry.ToolCallId is not null:
                    messages.Add(new ChatMessage(ChatRole.Tool,
                    [
                        new FunctionResultContent(entry.ToolCallId, entry.ToolResult)
                    ]));
                    break;
            }
        }

        return messages;
    }

    private static IDictionary<string, object?>? ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

}
