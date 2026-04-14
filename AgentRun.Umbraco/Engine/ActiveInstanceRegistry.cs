using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentRun.Umbraco.Engine;

public interface IActiveInstanceRegistry
{
    ChannelReader<string>? GetMessageReader(string instanceId);
    ChannelWriter<string>? GetMessageWriter(string instanceId);
    ChannelReader<string> RegisterInstance(string instanceId);
    void UnregisterInstance(string instanceId);

    /// <summary>
    /// Returns the per-instance cancellation token for a registered instance,
    /// or <c>null</c> if no entry exists. Used by the orchestrator to build
    /// a linked token combining the HTTP request token with the per-instance
    /// cancellation signal (Story 10.8).
    /// </summary>
    CancellationToken? GetCancellationToken(string instanceId);

    /// <summary>
    /// Signals the per-instance cancellation source if present. No-op when the
    /// instance is not currently registered (e.g. cancel before orchestrator
    /// started, or after it unregistered). Safe to call concurrently.
    /// </summary>
    void RequestCancellation(string instanceId);
}

public sealed class ActiveInstanceRegistry : IActiveInstanceRegistry, IDisposable
{
    private sealed record InstanceEntry(Channel<string> Channel, CancellationTokenSource CancellationSource);

    private readonly ConcurrentDictionary<string, InstanceEntry> _entries = new();

    public ChannelReader<string>? GetMessageReader(string instanceId)
    {
        return _entries.TryGetValue(instanceId, out var entry) ? entry.Channel.Reader : null;
    }

    public ChannelWriter<string>? GetMessageWriter(string instanceId)
    {
        return _entries.TryGetValue(instanceId, out var entry) ? entry.Channel.Writer : null;
    }

    public ChannelReader<string> RegisterInstance(string instanceId)
    {
        // TryAdd / TryUpdate retry loop. Avoids ConcurrentDictionary.AddOrUpdate's
        // documented pitfall: its update factory may run multiple times under
        // contention. Performing DisposeEntry inside the factory can double-dispose
        // (harmless) OR — under a rare interleave — dispose an entry that has already
        // been stored as the winner by a prior factory call, leaving the dictionary
        // pointing at a live key whose CTS is disposed. This loop always disposes
        // only the old entry whose reference we know we replaced.
        while (true)
        {
            var newEntry = new InstanceEntry(
                Channel.CreateUnbounded<string>(),
                new CancellationTokenSource());

            if (_entries.TryGetValue(instanceId, out var existing))
            {
                if (_entries.TryUpdate(instanceId, newEntry, existing))
                {
                    DisposeEntry(existing);
                    return newEntry.Channel.Reader;
                }
            }
            else if (_entries.TryAdd(instanceId, newEntry))
            {
                return newEntry.Channel.Reader;
            }

            // Lost the race — discard this throwaway entry and retry.
            DisposeEntry(newEntry);
        }
    }

    public void UnregisterInstance(string instanceId)
    {
        if (_entries.TryRemove(instanceId, out var entry))
        {
            DisposeEntry(entry);
        }
    }

    public CancellationToken? GetCancellationToken(string instanceId)
    {
        if (!_entries.TryGetValue(instanceId, out var entry))
        {
            return null;
        }

        try
        {
            return entry.CancellationSource.Token;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public void RequestCancellation(string instanceId)
    {
        if (!_entries.TryGetValue(instanceId, out var entry))
        {
            return;
        }

        try
        {
            entry.CancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Entry was removed + disposed between TryGetValue and Cancel.
            // Cancellation is moot — the owning orchestrator has already exited.
        }
    }

    public void Dispose()
    {
        foreach (var key in _entries.Keys.ToList())
        {
            if (_entries.TryRemove(key, out var entry))
            {
                DisposeEntry(entry);
            }
        }
    }

    private static void DisposeEntry(InstanceEntry entry)
    {
        entry.Channel.Writer.TryComplete();
        try
        {
            entry.CancellationSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — acceptable in race conditions.
        }
    }
}
