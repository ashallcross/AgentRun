using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Engine;

public static class ToolLoop
{
    internal const int MaxIterations = 100;

    /// <summary>
    /// Placeholder prefix used by conversation compaction (Story 10.2).
    /// Tool results older than the compaction threshold are replaced with
    /// a placeholder containing this prefix. The JSONL log is NOT modified.
    /// </summary>
    internal const string CompactionPlaceholderPrefix = "[Content offloaded";

    // Story 10.7a Track B: default collaborators used when callers do not
    // inject their own. Stateless, so a single shared instance is fine.
    // Tests that need to verify .Received(n) on either collaborator can pass
    // in NSubstitute mocks via the optional parameters on RunAsync.
    private static readonly IStreamingResponseAccumulator DefaultAccumulator = new StreamingResponseAccumulator();
    private static readonly IStallRecoveryPolicy DefaultStallPolicy = new StallRecoveryPolicy();

    public static async Task<ChatResponse> RunAsync(
        IChatClient client,
        IList<ChatMessage> messages,
        ChatOptions options,
        IReadOnlyDictionary<string, IWorkflowTool> declaredTools,
        ToolExecutionContext context,
        ILogger logger,
        CancellationToken cancellationToken,
        ChannelReader<string>? userMessageReader = null,
        ISseEventEmitter? emitter = null,
        IConversationRecorder? recorder = null,
        Func<CancellationToken, Task<bool>>? completionCheck = null,
        IToolLimitResolver? toolLimitResolver = null,
        TimeSpan? userMessageTimeoutOverride = null,
        int? compactionTurnThreshold = null,
        IStreamingResponseAccumulator? accumulator = null,
        IStallRecoveryPolicy? stallPolicy = null)
    {
        accumulator ??= DefaultAccumulator;
        stallPolicy ??= DefaultStallPolicy;

        // The user-message wait window (Story 9.6) is only needed in interactive
        // mode (when userMessageReader is non-null). Resolution is deferred until
        // first use; in interactive mode without an override, missing resolver or
        // Step/Workflow context is a wiring bug — fail loud.
        TimeSpan? userMessageTimeout = userMessageTimeoutOverride;

        // Story 9.1b Phase 1 carve-out (manual E2E gate, 2026-04-09):
        // single-shot stall recovery via synthetic user nudge. When the model
        // loses track mid-batch and emits an empty turn after a tool round-trip
        // before the workflow is structurally complete (completionCheck fails),
        // give it ONE more turn with a directive synthetic user message before
        // throwing StallDetectedException. Diagnosed via the CQA 5-URL batch:
        // Sonnet 4.6 reliably stalls after the final fetch_url at N=5 but not
        // at N=1. The 1-URL canary proved the model can do the fetch→write
        // transition; it's the cumulative turn count that loses it. This is the
        // missing sibling to Story 9.0's completion-check-on-stall recovery
        // (which handled "model finished but went silent"; this handles "model
        // lost track and needs a nudge to finish"). Single attempt — if the
        // nudge also stalls, the exception throws as before. Generic wording
        // so it does not bake any workflow-specific tool name into the engine.
        var nudgeAttempted = false;

        // Story 10.2: conversation compaction state. Track which tool results
        // have been compacted (by CallId) and when they were added (by assistant
        // turn count). The threshold is resolved once here to avoid per-iteration
        // resolver calls; -1 disables compaction entirely.
        var compactThreshold = compactionTurnThreshold ?? -1;
        var compactedCallIds = new HashSet<string>(StringComparer.Ordinal);
        var assistantTurnCount = 0;
        // Map: FunctionResultContent.CallId → assistant turn count at which the
        // tool result was added. Populated when tool results are appended.
        var toolResultTurnMap = new Dictionary<string, int>(StringComparer.Ordinal);

        var iteration = 0;
        while (true)
        {
            // Story 10.8: observe cancellation between iterations. The streaming
            // response and per-tool calls already respect the token, but the
            // gap between tool-batch completion and the next LLM call is the
            // window this check closes — cancellation is observed within the
            // same tool turn instead of waiting for the next streaming chunk.
            cancellationToken.ThrowIfCancellationRequested();

            if (++iteration > MaxIterations)
            {
                logger.LogError(
                    "Tool loop exceeded {MaxIterations} iterations for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                    MaxIterations, context.StepId, context.WorkflowAlias, context.InstanceId);
                throw new AgentRunException(
                    $"Tool loop exceeded maximum of {MaxIterations} iterations for step '{context.StepId}'");
            }

            // Drain any user messages queued from the message endpoint
            await DrainUserMessagesAsync(userMessageReader, messages, recorder, emitter, cancellationToken);

            // Story 10.2: compact old tool results before the LLM call to keep
            // the in-memory context bounded. Only the Result string is replaced;
            // the message structure stays intact (no orphaned tool_calls).
            if (compactThreshold > 0)
            {
                CompactOldToolResults(messages, assistantTurnCount, compactThreshold, compactedCallIds, toolResultTurnMap, logger, context);
            }

            // Story 10.7a Track B: streaming accumulation + partial-text-on-error
            // recording delegated to IStreamingResponseAccumulator.
            var accumulated = await accumulator.AccumulateAsync(
                client.GetStreamingResponseAsync(messages, options, cancellationToken),
                emitter, recorder, cancellationToken);
            var accumulatedText = accumulated.Text;
            var updates = accumulated.Updates;

            // Assemble streaming updates into messages for conversation context
            messages.AddMessages(updates);

            // Story 10.2: track assistant turns for compaction age calculation
            assistantTurnCount++;

            // Extract function calls from the last assistant message only (not previously processed ones)
            var lastAssistantMessage = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
            var functionCalls = lastAssistantMessage?.Contents.OfType<FunctionCallContent>().ToList()
                ?? [];

            if (functionCalls.Count == 0)
            {
                // Story 10.7a Track B: empty-turn decision tree delegated to
                // IStallRecoveryPolicy (ProviderEmpty detection, FinishReason
                // telemetry, stall classification, one-shot nudge recovery).
                // ToolLoop owns the nudgeAttempted flag because it persists
                // across loop iterations; the policy is stateless.
                var stallDecision = await stallPolicy.EvaluateAsync(
                    messages, accumulatedText, functionCalls, updates,
                    assistantTurnCount, nudgeAttempted,
                    isInteractive: userMessageReader is not null,
                    completionCheck, context, cancellationToken);

                if (stallDecision.Action == StallRecoveryAction.Terminate)
                {
                    return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                        ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
                }

                if (stallDecision.Action == StallRecoveryAction.Nudge)
                {
                    nudgeAttempted = true;
                    messages.Add(new ChatMessage(ChatRole.User, stallDecision.NudgeMessage!));
                    await (recorder?.RecordUserMessageAsync(stallDecision.NudgeMessage!, cancellationToken) ?? Task.CompletedTask);
                    continue;
                }

                // NoStall — interactive mode falls through to user-input-wait
                // sequencing below. The policy returns NoStall ONLY when
                // isInteractive: true (userMessageReader is non-null). If this
                // invariant ever breaks, fail loud instead of silently
                // terminating the run.
                if (userMessageReader is null)
                {
                    throw new InvalidOperationException(
                        "IStallRecoveryPolicy returned NoStall in non-interactive mode; " +
                        "policy must return Terminate when userMessageReader is null.");
                }

                // Try non-blocking drain first
                var drained = await DrainUserMessagesAsync(
                    userMessageReader, messages, recorder, emitter, cancellationToken);

                if (drained > 0)
                    continue;

                // Check if the step's completion criteria are met — exit early if so
                if (completionCheck is not null && await completionCheck(cancellationToken))
                {
                    logger.LogInformation(
                        "Completion check passed during input wait for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                        context.StepId, context.WorkflowAlias, context.InstanceId);
                    return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                        ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
                }

                // Signal frontend that we're waiting for user input
                if (emitter is not null)
                {
                    await emitter.EmitInputWaitAsync(context.StepId, cancellationToken);
                }

                // Nothing waiting — block with timeout
                if (!userMessageTimeout.HasValue)
                {
                    if (toolLimitResolver is null || context.Step is null || context.Workflow is null)
                    {
                        throw new InvalidOperationException(
                            "ToolLoop.RunAsync interactive mode requires either userMessageTimeoutOverride OR a non-null " +
                            "IToolLimitResolver with ToolExecutionContext.Step and ToolExecutionContext.Workflow populated.");
                    }

                    userMessageTimeout = TimeSpan.FromSeconds(
                        toolLimitResolver.ResolveToolLoopUserMessageTimeoutSeconds(context.Step, context.Workflow));
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(userMessageTimeout.Value);

                try
                {
                    if (await userMessageReader.WaitToReadAsync(timeoutCts.Token))
                    {
                        await DrainUserMessagesAsync(
                            userMessageReader, messages, recorder, emitter, cancellationToken);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout — not real cancellation. Exit normally.
                    logger.LogInformation(
                        "User message wait timed out for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                        context.StepId, context.WorkflowAlias, context.InstanceId);
                }

                return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                    ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
            }

            // Process each tool call and collect results
            var resultContents = new List<AIContent>();

            foreach (var functionCall in functionCalls)
            {
                if (!declaredTools.TryGetValue(functionCall.Name, out var tool))
                {
                    logger.LogWarning(
                        "Tool {ToolName} is not declared for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                        functionCall.Name, context.StepId, context.WorkflowAlias, context.InstanceId);

                    resultContents.Add(new FunctionResultContent(
                        functionCall.CallId,
                        $"Tool '{functionCall.Name}' is not declared for this step"));
                    continue;
                }

                // Emit tool.start
                if (emitter is not null)
                {
                    await emitter.EmitToolStartAsync(functionCall.CallId, tool.Name, cancellationToken);
                }

                // Record tool call
                var arguments = functionCall.Arguments ?? new Dictionary<string, object?>();
                var argsJson = JsonSerializer.Serialize(arguments);
                await (recorder?.RecordToolCallAsync(functionCall.CallId, tool.Name, argsJson, cancellationToken) ?? Task.CompletedTask);

                // Emit tool.args
                if (emitter is not null)
                {
                    await emitter.EmitToolArgsAsync(functionCall.CallId, arguments, cancellationToken);
                }

                logger.LogInformation(
                    "Tool {ToolName} executing for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                    tool.Name, context.StepId, context.WorkflowAlias, context.InstanceId);

                try
                {
                    var result = await tool.ExecuteAsync(arguments, context, cancellationToken);

                    // Emit tool.end and tool.result
                    if (emitter is not null)
                    {
                        await emitter.EmitToolEndAsync(functionCall.CallId, cancellationToken);
                        await emitter.EmitToolResultAsync(functionCall.CallId, result, cancellationToken);
                    }

                    // Record tool result
                    var resultStr = result?.ToString() ?? string.Empty;
                    await (recorder?.RecordToolResultAsync(functionCall.CallId, resultStr, cancellationToken) ?? Task.CompletedTask);

                    resultContents.Add(new FunctionResultContent(functionCall.CallId, result));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ToolExecutionException tex)
                {
                    logger.LogWarning(tex,
                        "Tool {ToolName} execution error for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                        tool.Name, context.StepId, context.WorkflowAlias, context.InstanceId);

                    if (emitter is not null)
                    {
                        await emitter.EmitToolEndAsync(functionCall.CallId, cancellationToken);
                    }

                    var errorResult = $"Tool '{tool.Name}' execution error: {tex.Message}";
                    await (recorder?.RecordToolResultAsync(functionCall.CallId, errorResult, cancellationToken) ?? Task.CompletedTask);

                    resultContents.Add(new FunctionResultContent(
                        functionCall.CallId, errorResult));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Tool {ToolName} failed for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                        tool.Name, context.StepId, context.WorkflowAlias, context.InstanceId);

                    // Emit tool.end even on failure
                    if (emitter is not null)
                    {
                        await emitter.EmitToolEndAsync(functionCall.CallId, cancellationToken);
                    }

                    var errorResult = $"Tool '{tool.Name}' failed: {ex.Message}";
                    await (recorder?.RecordToolResultAsync(functionCall.CallId, errorResult, cancellationToken) ?? Task.CompletedTask);

                    resultContents.Add(new FunctionResultContent(
                        functionCall.CallId, errorResult));
                }
            }

            // Add tool results as a single tool-role message
            messages.Add(new ChatMessage(ChatRole.Tool, resultContents));

            // Story 10.8: observe cancellation after the tool batch completes.
            // Prevents a cancelled run from racing into the next LLM call when
            // cancel fires mid-batch.
            cancellationToken.ThrowIfCancellationRequested();

            // Story 10.2: register tool result CallIds at the current assistant turn
            // so compaction knows when they were added.
            if (compactThreshold > 0)
            {
                foreach (var rc in resultContents.OfType<FunctionResultContent>())
                {
                    if (rc.CallId is not null)
                        toolResultTurnMap.TryAdd(rc.CallId, assistantTurnCount);
                }
            }

            // Drain any user messages sent during tool execution
            await DrainUserMessagesAsync(userMessageReader, messages, recorder, emitter, cancellationToken);
        }
    }

    private static async Task<int> DrainUserMessagesAsync(
        ChannelReader<string>? userMessageReader,
        IList<ChatMessage> messages,
        IConversationRecorder? recorder,
        ISseEventEmitter? emitter,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (userMessageReader is not null && userMessageReader.TryRead(out var userMsg))
        {
            count++;
            messages.Add(new ChatMessage(ChatRole.User, userMsg));
            await (recorder?.RecordUserMessageAsync(userMsg, cancellationToken) ?? Task.CompletedTask);
            if (emitter is not null)
            {
                await emitter.EmitUserMessageAsync(userMsg, cancellationToken);
            }
        }
        return count;
    }

    /// <summary>
    /// Story 10.2: replace old tool result content with compact placeholders
    /// in the in-memory message list. The JSONL log is NOT modified — this
    /// operates on the live <paramref name="messages"/> list only.
    /// </summary>
    internal static void CompactOldToolResults(
        IList<ChatMessage> messages,
        int currentAssistantTurn,
        int threshold,
        HashSet<string> compactedCallIds,
        Dictionary<string, int> toolResultTurnMap,
        ILogger logger,
        ToolExecutionContext context)
    {
        // Find the most recent tool-role message index — those results are the
        // "current batch" the model hasn't responded to yet and must NOT be compacted.
        var lastToolMessageIndex = -1;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.Tool)
            {
                lastToolMessageIndex = i;
                break;
            }
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Tool) continue;

            // Skip the most recent tool result batch — model hasn't seen it yet
            if (i == lastToolMessageIndex) continue;

            foreach (var content in msg.Contents)
            {
                if (content is not FunctionResultContent frc) continue;
                if (frc.CallId is null) continue;
                if (compactedCallIds.Contains(frc.CallId)) continue;

                // Determine the age of this result in assistant turns
                if (!toolResultTurnMap.TryGetValue(frc.CallId, out var addedAtTurn))
                {
                    // Result was in the message list before ToolLoop started
                    // (e.g. from a retry reload). Treat as maximally old.
                    addedAtTurn = 0;
                }

                var age = currentAssistantTurn - addedAtTurn;
                if (age < threshold) continue;

                // Compute original size — skip compaction for small results (handles
                // from offloaded tools are already compact; replacing them destroys
                // metadata for negligible context savings).
                var originalResult = frc.Result?.ToString() ?? string.Empty;
                var originalSize = System.Text.Encoding.UTF8.GetByteCount(originalResult);
                if (originalSize <= EngineDefaults.CompactionMinSizeBytes)
                    continue;

                var toolName = frc.CallId; // CallId is typically the tool call ID, not the name

                // Try to find the matching FunctionCallContent for a better name
                var resolvedName = ResolveToolNameForCallId(messages, frc.CallId) ?? frc.CallId;

                var placeholder = $"[Content offloaded — {originalSize} bytes from {resolvedName}. Original result available in conversation log.]";

                // Replace the Result property. FunctionResultContent.Result has a public setter.
                frc.Result = placeholder;
                compactedCallIds.Add(frc.CallId);

                logger.LogDebug(
                    "Compacted tool result {CallId} ({ToolName}, {OriginalSize} bytes, age {Age} turns) for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                    frc.CallId, resolvedName, originalSize, age, context.StepId, context.WorkflowAlias, context.InstanceId);
            }
        }
    }

    private static string? ResolveToolNameForCallId(IList<ChatMessage> messages, string callId)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.Assistant) continue;
            foreach (var content in messages[i].Contents)
            {
                if (content is FunctionCallContent fcc && fcc.CallId == callId)
                    return fcc.Name;
            }
        }
        return null;
    }
}
