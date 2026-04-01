namespace Shallai.UmbracoAgentRunner.Engine.Events;

public interface ISseEventEmitter
{
    Task EmitRunStartedAsync(string instanceId, CancellationToken cancellationToken);

    Task EmitTextDeltaAsync(string content, CancellationToken cancellationToken);

    Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken cancellationToken);

    Task EmitToolArgsAsync(string toolCallId, object arguments, CancellationToken cancellationToken);

    Task EmitToolEndAsync(string toolCallId, CancellationToken cancellationToken);

    Task EmitToolResultAsync(string toolCallId, object result, CancellationToken cancellationToken);

    Task EmitStepStartedAsync(string stepId, string stepName, CancellationToken cancellationToken);

    Task EmitStepFinishedAsync(string stepId, string status, CancellationToken cancellationToken);

    Task EmitRunFinishedAsync(string instanceId, string status, CancellationToken cancellationToken);

    Task EmitRunErrorAsync(string error, string message, CancellationToken cancellationToken);

    Task EmitSystemMessageAsync(string message, CancellationToken cancellationToken);

    Task EmitUserMessageAsync(string content, CancellationToken cancellationToken);

    Task EmitInputWaitAsync(string stepId, CancellationToken cancellationToken);
}
