using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Shallai.UmbracoAgentRunner.Engine.Events;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Engine;

public static class ToolLoop
{
    internal const int MaxIterations = 100;
    internal static TimeSpan UserMessageTimeout = TimeSpan.FromMinutes(5);

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
        IConversationRecorder? recorder = null)
    {
        var iteration = 0;
        while (true)
        {
            if (++iteration > MaxIterations)
            {
                logger.LogError(
                    "Tool loop exceeded {MaxIterations} iterations for step {StepId} in workflow {WorkflowAlias} instance {InstanceId}",
                    MaxIterations, context.StepId, context.WorkflowAlias, context.InstanceId);
                throw new AgentRunnerException(
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
                // No tool calls — check if we should wait for user input
                if (userMessageReader is null)
                {
                    // Non-interactive mode — exit immediately
                    return new ChatResponse(messages.Where(m => m.Role == ChatRole.Assistant).LastOrDefault()
                        ?? new ChatMessage(ChatRole.Assistant, accumulatedText));
                }

                // Try non-blocking drain first
                var drained = await DrainUserMessagesAsync(
                    userMessageReader, messages, recorder, emitter, cancellationToken);

                if (drained > 0)
                    continue;

                // Nothing waiting — block with timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(UserMessageTimeout);

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
