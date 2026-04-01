using System.Text;
using Microsoft.Extensions.AI;
using Shallai.UmbracoAgentRunner.Engine.Events;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Engine;

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
                // No tool calls — return a ChatResponse from the accumulated updates
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
        }
    }
}
