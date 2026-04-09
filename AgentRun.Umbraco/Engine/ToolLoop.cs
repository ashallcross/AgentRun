using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Engine;

public static class ToolLoop
{
    internal const int MaxIterations = 100;

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
        TimeSpan? userMessageTimeoutOverride = null)
    {
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

        var iteration = 0;
        while (true)
        {
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

            // Stream the LLM response, emitting text.delta events for each chunk
            var textBuilder = new StringBuilder();
            var updates = new List<ChatResponseUpdate>();

            try
            {
                await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    updates.Add(update);

                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        textBuilder.Append(update.Text);

                        if (emitter is not null)
                        {
                            await emitter.EmitTextDeltaAsync(update.Text, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Flush any partial text accumulated before the error
                var partialText = textBuilder.ToString();
                if (!string.IsNullOrEmpty(partialText))
                {
                    await (recorder?.RecordAssistantTextAsync(partialText, CancellationToken.None) ?? Task.CompletedTask);
                }

                throw;
            }

            // Record accumulated assistant text (if any)
            var accumulatedText = textBuilder.ToString();
            if (!string.IsNullOrEmpty(accumulatedText))
            {
                await (recorder?.RecordAssistantTextAsync(accumulatedText, cancellationToken) ?? Task.CompletedTask);
            }

            // Assemble streaming updates into messages for conversation context
            messages.AddMessages(updates);

            // Extract function calls from the last assistant message only (not previously processed ones)
            var lastAssistantMessage = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
            var functionCalls = lastAssistantMessage?.Contents.OfType<FunctionCallContent>().ToList()
                ?? [];

            if (functionCalls.Count == 0)
            {
                // Story 9.0: classify the empty-tool-call turn before deciding
                // whether to wait for user input. Detection is pure; recovery
                // (the throw below) is the only line a future strategy would
                // swap. Autonomous mode ignores the classification entirely.
                var stallClassification = StallDetector.Classify(messages, accumulatedText, functionCalls);

                // No tool calls — check if we should wait for user input
                if (userMessageReader is null)
                {
                    // Non-interactive mode — exit immediately. Stall detection
                    // is interactive-only (Story 9.0 AC #6).
                    return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                        ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
                }

                // Story 9.1b Phase 1 carve-out: post-nudge confirmed-stall
                // throw. If we've already injected the synthetic recovery
                // nudge once and we're back in the no-tool-call branch, the
                // model has burned its second chance. The classifier will NOT
                // fire as a stall on this turn (the most recent non-assistant
                // message is now the synthetic user nudge, not a tool result),
                // so we cannot rely on the classification path below — we have
                // to throw directly. The success-path check still wins if the
                // model called the recovery tool on the post-nudge turn AND
                // the completion check now passes (e.g. write_file in turn 2,
                // narration in turn 3) — that path is reached because the
                // tool-call turn does not enter this branch at all.
                if (nudgeAttempted)
                {
                    if (completionCheck is not null && await completionCheck(cancellationToken))
                    {
                        logger.LogInformation(
                            "Completion check passed on post-nudge turn for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} — treating as successful recovery",
                            context.StepId, context.WorkflowAlias, context.InstanceId);
                        return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                            ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
                    }

                    var lastToolCallNamePostNudge = ExtractLastToolCallName(messages);
                    logger.LogWarning(
                        "Post-nudge turn produced no tool call for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} after tool call {LastToolCall} — synthetic nudge already attempted, failing the run",
                        context.StepId, context.WorkflowAlias, context.InstanceId, lastToolCallNamePostNudge);

                    throw new StallDetectedException(
                        lastToolCallNamePostNudge, context.StepId, context.InstanceId, context.WorkflowAlias);
                }

                if (stallClassification == StallClassification.StallEmptyContent
                    || stallClassification == StallClassification.StallNarration)
                {
                    // Story 9.0 refinement (live test 2026-04-07): if the step's
                    // completion criteria are already satisfied, the run actually
                    // succeeded — the model just narrated "I'm done" instead of
                    // going silent. Failing it would mask a successful run as a
                    // bug and force a pointless retry. Run the completion check
                    // first; if it passes, exit cleanly with success. Only throw
                    // a stall when there is no verified evidence of completion.
                    // This preserves "deny by default" — unverified narration
                    // still stalls — while letting verified-complete runs finish.
                    if (completionCheck is not null && await completionCheck(cancellationToken))
                    {
                        logger.LogInformation(
                            "Completion check passed despite empty/narrative final turn for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} — treating as successful completion, not a stall",
                            context.StepId, context.WorkflowAlias, context.InstanceId);
                        return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                            ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
                    }

                    // Story 9.1b Phase 1 carve-out: single-shot stall recovery
                    // via synthetic user nudge. See the comment at the top of
                    // RunAsync for the diagnosis and rationale. Gated on
                    // `completionCheck is not null` so the engine only attempts
                    // recovery when the workflow has supplied a verifiable
                    // success signal — otherwise we have no way to know if the
                    // post-nudge turn produced real progress and the safer
                    // behaviour is to throw immediately as before. The nudge
                    // fires before the throw so the model gets exactly one
                    // chance to recover from a "lost track mid-batch" stall
                    // before we hard-fail. If the nudge also stalls, the throw
                    // below executes on the next loop iteration's classify
                    // path with nudgeAttempted == true.
                    if (completionCheck is not null && !nudgeAttempted)
                    {
                        nudgeAttempted = true;
                        var nudge = "Continue with the next required action — your previous turn was empty and the task is not yet complete.";
                        messages.Add(new ChatMessage(ChatRole.User, nudge));
                        await (recorder?.RecordUserMessageAsync(nudge, cancellationToken) ?? Task.CompletedTask);
                        logger.LogInformation(
                            "Empty/narrative stall detected with completion check failing for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} — injecting one-shot synthetic user nudge before failing",
                            context.StepId, context.WorkflowAlias, context.InstanceId);
                        continue;
                    }

                    var lastToolCallName = ExtractLastToolCallName(messages);
                    var stallType = stallClassification == StallClassification.StallEmptyContent
                        ? "empty"
                        : "narration";

                    logger.LogWarning(
                        "ToolLoop stall detected ({StallType}) for step {StepId} in workflow {WorkflowAlias} instance {InstanceId} after tool call {LastToolCall} — synthetic nudge already attempted, failing the run",
                        stallType, context.StepId, context.WorkflowAlias, context.InstanceId, lastToolCallName);

                    throw new StallDetectedException(
                        lastToolCallName, context.StepId, context.InstanceId, context.WorkflowAlias);
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

            // Drain any user messages sent during tool execution
            await DrainUserMessagesAsync(userMessageReader, messages, recorder, emitter, cancellationToken);
        }
    }

    private static string ExtractLastToolCallName(IList<ChatMessage> messages)
    {
        // Walk backwards through assistant messages and find the most recent
        // FunctionCallContent — that is the tool call the LLM saw a result for
        // before stalling. "none" if there is no tool call in the conversation
        // (defensive — stall detection should not fire in that case).
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.Assistant) continue;
            var fnCall = messages[i].Contents.OfType<FunctionCallContent>().LastOrDefault();
            if (fnCall is not null) return fnCall.Name;
        }
        return "none";
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
}
