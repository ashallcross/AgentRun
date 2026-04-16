using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentRun.Umbraco.Engine;

public interface IActiveInstanceRegistry
{
    ChannelReader<string>? GetMessageReader(string instanceId);
    ChannelWriter<string>? GetMessageWriter(string instanceId);

    /// <summary>
    /// Legacy replace-on-collision registration. Preserved for the rare defensive
    /// case where a caller needs to force-replace an existing entry (Story 10.8
    /// disposal semantics). New code should prefer <see cref="TryClaim"/> (atomic
    /// endpoint-side) and <see cref="AttachOrClaim"/> (orchestrator-side).
    /// </summary>
    [Obsolete("Use TryClaim (endpoint) + AttachOrClaim (orchestrator) instead. Replace-on-collision semantics retained for compatibility — do not introduce new callers.")]
    ChannelReader<string> RegisterInstance(string instanceId);

    void UnregisterInstance(string instanceId);

    /// <summary>
    /// Atomically claims an orchestrator slot for the given instance (Story 10.1).
    /// Returns <c>true</c> if the slot was free and is now claimed; <c>false</c>
    /// if another orchestrator already holds the slot. The claim is released by
    /// <see cref="UnregisterInstance"/>, which is called by the orchestrator's
    /// <c>finally</c> block. On a successful claim, the registry creates a fresh
    /// Channel and CancellationTokenSource so the channel reader and cancellation
    /// token are immediately available to subsequent <see cref="GetMessageReader"/>
    /// / <see cref="GetCancellationToken"/> calls — the orchestrator does not
    /// need to re-create them and should use <see cref="AttachOrClaim"/> as its
    /// entry point.
    /// </summary>
    bool TryClaim(string instanceId);

    /// <summary>
    /// Orchestrator entry point (Story 10.1). Returns the channel reader for the
    /// given instance: attaches to an existing entry created by a prior
    /// <see cref="TryClaim"/> call, or creates a fresh entry if none exists
    /// (supports direct-invocation test paths that do not go through the endpoint).
    /// Never replaces an existing entry — if an entry exists, its channel and
    /// cancellation source are reused as-is.
    /// </summary>
    ChannelReader<string> AttachOrClaim(string instanceId);

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

    public bool TryClaim(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentException("Instance id must be non-empty.", nameof(instanceId));
        }

        var newEntry = new InstanceEntry(
            Channel.CreateUnbounded<string>(),
            new CancellationTokenSource());

        if (_entries.TryAdd(instanceId, newEntry))
        {
            return true;
        }

        // Lost the race — discard the throwaway entry to avoid leaking the CTS.
        DisposeEntry(newEntry);
        return false;
    }

    public ChannelReader<string> AttachOrClaim(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentException("Instance id must be non-empty.", nameof(instanceId));
        }

        if (_entries.TryGetValue(instanceId, out var existing))
        {
            return existing.Channel.Reader;
        }

        // No prior claim — create-and-add. Under contention with another concurrent
        // AttachOrClaim / TryClaim on the same id, GetOrAdd's factory may run and
        // still lose; the loser's throwaway entry is returned but never inserted.
        // Build a single entry and reuse it via GetOrAdd's atomic add; if we lose,
        // dispose the loser.
        var newEntry = new InstanceEntry(
            Channel.CreateUnbounded<string>(),
            new CancellationTokenSource());

        var winner = _entries.GetOrAdd(instanceId, newEntry);
        if (!ReferenceEquals(winner, newEntry))
        {
            DisposeEntry(newEntry);
        }

        return winner.Channel.Reader;
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
