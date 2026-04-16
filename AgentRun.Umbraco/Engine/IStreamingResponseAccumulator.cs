using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Engine.Events;

namespace AgentRun.Umbraco.Engine;

public interface IStreamingResponseAccumulator
{
    // Drains the LLM streaming response, appends text deltas to a builder,
    // fans them out to the SSE emitter, collects the full update list, and
    // records the final assistant text (or partial text on failure) to the
    // conversation recorder.
    //
    // Behaviour contract preserved from the original ToolLoop.cs streaming
    // block (Story 10.7a Track B extraction): OperationCanceledException
    // rethrows; any other exception records partial text before rethrowing
    // so the recorder contains what was actually streamed before the break.
    Task<AccumulatedResponse> AccumulateAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        ISseEventEmitter? emitter,
        IConversationRecorder? recorder,
        CancellationToken cancellationToken);
}

public sealed record AccumulatedResponse(
    string Text,
    IReadOnlyList<ChatResponseUpdate> Updates);
