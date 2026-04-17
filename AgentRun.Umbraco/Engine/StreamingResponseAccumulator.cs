using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AgentRun.Umbraco.Engine.Events;

namespace AgentRun.Umbraco.Engine;

// Owns the text builder + updates list + SSE text-delta fan-out +
// partial-text recording-on-error for a streaming LLM response. Downstream
// FinishReason telemetry and empty-turn stall logic stay in ToolLoop /
// IStallRecoveryPolicy because they operate on the post-accumulation result.
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
        // Emit-boundary instrumentation (count + total-chars). Logged at Debug
        // so the default production level stays quiet; flip a single category
        // to Debug in appsettings when reproducing intermittent UI-dropouts to
        // correlate backend emit with frontend receipt. Zero cost when Debug
        // is off.
        var emitCount = 0;
        var emitChars = 0;
        // Story 11.5 — accumulate UsageDetails from any UsageContent fragments
        // in the stream. Providers surface usage via UsageContent in the Contents
        // list (per-turn cumulative or per-chunk delta depending on adapter);
        // UsageDetails.Add handles merge semantics so the caller sees a single
        // totalled UsageDetails per accumulated response.
        UsageDetails? accumulatedUsage = null;

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

                foreach (var usageContent in update.Contents.OfType<UsageContent>())
                {
                    // UsageContent.Details has a public setter and the parameterless
                    // ctor creates an empty UsageDetails — a malformed provider chunk
                    // with Details=null would otherwise throw ArgumentNullException
                    // out of UsageDetails.Add and tear down the whole stream. Skip.
                    if (usageContent.Details is null) continue;
                    accumulatedUsage ??= new UsageDetails();
                    accumulatedUsage.Add(usageContent.Details);
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

        return new AccumulatedResponse(accumulatedText, updates, accumulatedUsage);
    }
}
