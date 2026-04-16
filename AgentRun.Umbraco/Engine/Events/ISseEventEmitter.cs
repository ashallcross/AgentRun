namespace AgentRun.Umbraco.Engine.Events;

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

    /// <summary>
    /// Starts a long-running heartbeat that emits a <c>: keepalive\n\n</c> SSE comment
    /// line every <paramref name="interval"/> until <paramref name="cancellationToken"/>
    /// fires. Serialises against concurrent <c>Emit*</c> calls via an internal
    /// <see cref="SemaphoreSlim"/>. Heartbeat write failures
    /// (<see cref="IOException"/>, <see cref="ObjectDisposedException"/>,
    /// <see cref="OperationCanceledException"/>) are caught, logged at Debug, and exit
    /// the loop cleanly. Intended to be fire-and-forget at the call site — use a linked
    /// CTS for deterministic teardown. Story 10.11 (Track A).
    /// </summary>
    Task StartKeepaliveAsync(TimeSpan interval, CancellationToken cancellationToken);
}
