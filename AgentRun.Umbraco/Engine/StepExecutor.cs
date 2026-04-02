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
    private readonly ILogger<StepExecutor> _logger;

    public StepExecutor(
        IProfileResolver profileResolver,
        IPromptAssembler promptAssembler,
        IEnumerable<IWorkflowTool> allTools,
        IInstanceManager instanceManager,
        IConversationStore conversationStore,
        IArtifactValidator artifactValidator,
        ICompletionChecker completionChecker,
        ILogger<StepExecutor> logger)
    {
        _profileResolver = profileResolver;
        _promptAssembler = promptAssembler;
        _allTools = allTools;
        _instanceManager = instanceManager;
        _conversationStore = conversationStore;
        _artifactValidator = artifactValidator;
        _completionChecker = completionChecker;
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

            // Build declaration-only tool descriptors for the LLM.
            // Using AsDeclarationOnly() strips the executable delegate so that
            // FunctionInvokingChatClient in the Umbraco.AI middleware pipeline
            // won't auto-execute tools — our ToolLoop handles execution via declaredTools.
            var toolExecutionContext = new ToolExecutionContext(
                context.InstanceFolderPath, instance.InstanceId, step.Id, instance.WorkflowAlias);

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

            var chatOptions = new ChatOptions { Tools = aiTools };

            // Build completion check delegate for interactive mode early exit
            Func<CancellationToken, Task<bool>>? completionCheck = step.CompletionCheck is not null
                ? async ct => (await _completionChecker.CheckAsync(step.CompletionCheck, context.InstanceFolderPath, ct)).Passed
                : null;

            // Run the tool loop
            await ToolLoop.RunAsync(
                client, messages, chatOptions, toolDict, toolExecutionContext, _logger, cancellationToken,
                context.UserMessageReader, context.EventEmitter, context.ConversationRecorder,
                completionCheck);

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

            context.LlmError = LlmErrorClassifier.Classify(ex);

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

    /// <summary>
    /// Non-executable tool declaration that carries only metadata for the LLM.
    /// FunctionInvokingChatClient in the Umbraco.AI middleware pipeline cannot auto-execute
    /// this — our ToolLoop handles execution via the declaredTools dictionary.
    /// </summary>
    private sealed class ToolDeclaration : AIFunctionDeclaration
    {
        private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema { get; }

        public ToolDeclaration(string name, string description, JsonElement? parameterSchema)
        {
            Name = name;
            Description = description;
            JsonSchema = parameterSchema ?? EmptySchema;
        }
    }
}
