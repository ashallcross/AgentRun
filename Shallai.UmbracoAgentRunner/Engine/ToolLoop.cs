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
        ISseEventEmitter? emitter = null)
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

            var response = await client.GetResponseAsync(messages, options, cancellationToken);

            // Emit text.delta for any text content in the response
            if (emitter is not null)
            {
                foreach (var message in response.Messages)
                {
                    foreach (var content in message.Contents.OfType<TextContent>())
                    {
                        if (!string.IsNullOrEmpty(content.Text))
                        {
                            await emitter.EmitTextDeltaAsync(content.Text, cancellationToken);
                        }
                    }
                }
            }

            var functionCalls = response.Messages
                .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
                .ToList();

            if (functionCalls.Count == 0)
            {
                return response;
            }

            // Add assistant message(s) containing the tool calls to conversation
            foreach (var message in response.Messages)
            {
                messages.Add(message);
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

                // Emit tool.args
                var arguments = functionCall.Arguments ?? new Dictionary<string, object?>();
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

                    resultContents.Add(new FunctionResultContent(
                        functionCall.CallId,
                        $"Tool '{tool.Name}' execution error: {tex.Message}"));
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

                    resultContents.Add(new FunctionResultContent(
                        functionCall.CallId,
                        $"Tool '{tool.Name}' failed: {ex.Message}"));
                }
            }

            // Add tool results as a single tool-role message
            messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
        }
    }
}
