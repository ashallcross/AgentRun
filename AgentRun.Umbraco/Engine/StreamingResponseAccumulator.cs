using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AgentRun.Umbraco.Engine.Events;

namespace AgentRun.Umbraco.Engine;

// Story 10.7a Track B: extracted from ToolLoop.cs. Owns the text builder +
// updates list + SSE text-delta fan-out + partial-text recording-on-error.
// The downstream FinishReason telemetry and empty-turn stall logic stay in
// ToolLoop because they operate on the post-accumulation result (locked
// decision 5).
public class StreamingResponseAccumulator : IStreamingResponseAccumulator
{
    private readonly ILogger<StreamingResponseAccumulator> _logger;

    public StreamingResponseAccumulator(ILogger<StreamingResponseAccumulator>? logger = null)
    {
        _logger = logger ?? NullLogger<StreamingResponseAccumulator>.Instance;
    }

    public async Task<AccumulatedResponse> AccumulateAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        ISseEventEmitter? emitter,
        IConversationRecorder? recorder,
        CancellationToken cancellationToken)
    {
        var textBuilder = new StringBuilder();
        var updates = new List<ChatResponseUpdate>();
        // Story 10.7a review monitoring (SSE-flake triage): count + total-chars
        // instrumentation at the emit boundary. Logged at Debug so the default
        // production level stays quiet; flip a single category to Debug in
        // appsettings when reproducing the intermittent UI-dropout to correlate
        // backend emit with frontend receipt. Zero cost when Debug is off.
        var emitCount = 0;
        var emitChars = 0;

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
                        emitCount++;
                        emitChars += update.Text.Length;
                        _logger.LogDebug(
                            "engine.streaming.text_delta_emitted: seq={Seq}, chars={Chars}, cumulative={Cumulative}",
                            emitCount, update.Text.Length, emitChars);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Flush any partial text accumulated before the error so the
            // recorder contains what was actually streamed before the break.
            var partialText = textBuilder.ToString();
            _logger.LogWarning(ex,
                "engine.streaming.aborted: emittedDeltas={EmitCount}, partialChars={PartialChars}",
                emitCount, partialText.Length);
            if (!string.IsNullOrEmpty(partialText))
            {
                await (recorder?.RecordAssistantTextAsync(partialText, CancellationToken.None) ?? Task.CompletedTask);
            }

            throw;
        }

        var accumulatedText = textBuilder.ToString();
        _logger.LogDebug(
            "engine.streaming.completed: emittedDeltas={EmitCount}, totalChars={TotalChars}, updates={UpdateCount}",
            emitCount, accumulatedText.Length, updates.Count);

        if (!string.IsNullOrEmpty(accumulatedText))
        {
            await (recorder?.RecordAssistantTextAsync(accumulatedText, cancellationToken) ?? Task.CompletedTask);
        }

        return new AccumulatedResponse(accumulatedText, updates);
    }
}
