using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using AgentRun.Umbraco.Engine.Events;

namespace AgentRun.Umbraco.Engine;

// Story 10.7a Track B: extracted from ToolLoop.cs. Owns the text builder +
// updates list + SSE text-delta fan-out + partial-text recording-on-error.
// The downstream FinishReason telemetry and empty-turn stall logic stay in
// ToolLoop because they operate on the post-accumulation result (locked
// decision 5).
public class StreamingResponseAccumulator : IStreamingResponseAccumulator
{
    public async Task<AccumulatedResponse> AccumulateAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        ISseEventEmitter? emitter,
        IConversationRecorder? recorder,
        CancellationToken cancellationToken)
    {
        var textBuilder = new StringBuilder();
        var updates = new List<ChatResponseUpdate>();

        try
        {
            await foreach (var update in stream.WithCancellation(cancellationToken))
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
            // Flush any partial text accumulated before the error so the
            // recorder contains what was actually streamed before the break.
            var partialText = textBuilder.ToString();
            if (!string.IsNullOrEmpty(partialText))
            {
                await (recorder?.RecordAssistantTextAsync(partialText, CancellationToken.None) ?? Task.CompletedTask);
            }

            throw;
        }

        var accumulatedText = textBuilder.ToString();
        if (!string.IsNullOrEmpty(accumulatedText))
        {
            await (recorder?.RecordAssistantTextAsync(accumulatedText, cancellationToken) ?? Task.CompletedTask);
        }

        return new AccumulatedResponse(accumulatedText, updates);
    }
}
