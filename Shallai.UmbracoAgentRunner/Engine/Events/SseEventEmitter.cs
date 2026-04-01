using System.Text;

namespace Shallai.UmbracoAgentRunner.Engine.Events;

public sealed class SseEventEmitter : ISseEventEmitter
{
    private readonly Stream _stream;
    private readonly ILogger<SseEventEmitter> _logger;

    public SseEventEmitter(Stream stream, ILogger<SseEventEmitter> logger)
    {
        _stream = stream;
        _logger = logger;
    }

    public Task EmitRunStartedAsync(string instanceId, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.RunStarted, new RunStartedPayload(instanceId), cancellationToken);

    public Task EmitTextDeltaAsync(string content, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.TextDelta, new TextDeltaPayload(content), cancellationToken);

    public Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.ToolStart, new ToolStartPayload(toolCallId, toolName), cancellationToken);

    public Task EmitToolArgsAsync(string toolCallId, object arguments, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.ToolArgs, new ToolArgsPayload(toolCallId, arguments), cancellationToken);

    public Task EmitToolEndAsync(string toolCallId, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.ToolEnd, new ToolEndPayload(toolCallId), cancellationToken);

    public Task EmitToolResultAsync(string toolCallId, object result, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.ToolResult, new ToolResultPayload(toolCallId, result), cancellationToken);

    public Task EmitStepStartedAsync(string stepId, string stepName, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.StepStarted, new StepStartedPayload(stepId, stepName), cancellationToken);

    public Task EmitStepFinishedAsync(string stepId, string status, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.StepFinished, new StepFinishedPayload(stepId, status), cancellationToken);

    public Task EmitRunFinishedAsync(string instanceId, string status, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.RunFinished, new RunFinishedPayload(instanceId, status), cancellationToken);

    public Task EmitRunErrorAsync(string error, string message, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.RunError, new RunErrorPayload(error, message), cancellationToken);

    public Task EmitSystemMessageAsync(string message, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.SystemMessage, new SystemMessagePayload(message), cancellationToken);

    public Task EmitUserMessageAsync(string content, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.UserMessage, new UserMessagePayload(content), cancellationToken);

    public Task EmitInputWaitAsync(string stepId, CancellationToken cancellationToken)
        => EmitAsync(SseEventTypes.InputWait, new InputWaitPayload(stepId), cancellationToken);

    private async Task EmitAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {json}\n\n");

        _logger.LogDebug("SSE emit {EventType}: {Json}", eventType, json);

        await _stream.WriteAsync(bytes, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }
}
