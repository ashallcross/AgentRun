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

// Story 11.5 — Usage is aggregated from any UsageContent fragments that
// appear in the stream via UsageDetails.Add. Null when no UsageContent
// was observed (text-only streams or providers that don't report usage
// on the streaming path).
public sealed record AccumulatedResponse(
    string Text,
    IReadOnlyList<ChatResponseUpdate> Updates,
    UsageDetails? Usage = null);
