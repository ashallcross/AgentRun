using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

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

                // Story 11.5 — emit cache.usage even for pre-LLM validation
                // failures so the "every step attempt emits one log line"
                // contract documented in workflow-authoring-guide.md holds for
                // dashboard authors. No LLM call was made, so zeros across
                // every field are the correct signal.
                LogCacheUsage(usage: null, instance, step);

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

            // Build initial messages — start with fresh system prompt.
            // Story 11.5 — annotate the System message with a neutral
            // Cacheable hint. Standard M.E.AI AdditionalProperties is the
            // documented extension point — conforming adapters ignore unknown
            // keys, so this is harmless on every provider (OpenAI, Azure
            // OpenAI, Gemini, Copilot, Ollama, custom). A future provider
            // adapter in Services/ (e.g. an Anthropic cache_control wrapper)
            // can translate the hint into provider-native caching markers
            // without any change to Engine/ code. See D8 of 11.5 spec.
            var systemMessage = new ChatMessage(ChatRole.System, prompt);
            systemMessage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            systemMessage.AdditionalProperties[EngineDefaults.CacheableHintKey] = true;

            var messages = new List<ChatMessage> { systemMessage };

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

            // Run the tool loop. Story 11.5 — capture the returned
            // ChatResponse so we can read the per-step UsageDetails total
            // (aggregated inside ToolLoop across every LLM call) and emit the
            // cache.usage log line at each terminal exit path.
            var toolLoopResponse = await ToolLoop.RunAsync(
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

                LogCacheUsage(toolLoopResponse.Usage, instance, step);

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

            LogCacheUsage(toolLoopResponse.Usage, instance, step);

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

            // Story 11.5 — emit the cache.usage log even on the error path so
            // adopters see partial usage (stall / max-iterations / provider-
            // empty-response paths carry the UsageDetails accumulated across
            // prior turns via AgentRunException.PartialUsage) or zeros (for
            // exceptions originating outside the engine-domain throw sites —
            // e.g. raw provider exceptions mid-stream — where no partial total
            // is available). Zeros remain signal, not absence.
            var partialUsage = (ex as AgentRunException)?.PartialUsage;
            LogCacheUsage(partialUsage, instance, step);

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

    // Story 11.5 AC2 — emit the per-step cache.usage structured log line.
    // Fires at Information level (D4) with M.E.AI UsageDetails fields plus
    // provider-specific AdditionalCounts extras (Anthropic surfaces these as
    // CacheReadInputTokens / CacheCreationInputTokens; OpenAI / Azure don't
    // split the counts today). Zeros are signal, not absence — the log fires
    // unconditionally so adopters can validate cache behaviour via standard
    // log sinks (Serilog, App Insights, Seq).
    private void LogCacheUsage(UsageDetails? usage, InstanceState instance, StepDefinition step)
    {
        var inputTokens = usage?.InputTokenCount ?? 0;
        var outputTokens = usage?.OutputTokenCount ?? 0;
        var cachedInputTokens = usage?.CachedInputTokenCount ?? 0;
        var cacheReadExtra = TryGetLong(usage?.AdditionalCounts, "CacheReadInputTokens");
        var cacheWriteExtra = TryGetLong(usage?.AdditionalCounts, "CacheCreationInputTokens");

        _logger.LogInformation(
            "cache.usage: step={StepId}, workflow={WorkflowAlias}, instance={InstanceId}, input={InputTokens}, output={OutputTokens}, cached_input={CachedInputTokens}, cache_read={CacheReadExtra}, cache_write={CacheWriteExtra}",
            step.Id,
            instance.WorkflowAlias,
            instance.InstanceId,
            inputTokens,
            outputTokens,
            cachedInputTokens,
            cacheReadExtra,
            cacheWriteExtra);
    }

    private static long TryGetLong(AdditionalPropertiesDictionary<long>? dict, string key)
    {
        if (dict is null) return 0;
        return dict.TryGetValue(key, out var value) ? value : 0;
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
