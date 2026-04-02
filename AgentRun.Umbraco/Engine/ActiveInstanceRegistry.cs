using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentRun.Umbraco.Engine;

public interface IActiveInstanceRegistry
{
    ChannelReader<string>? GetMessageReader(string instanceId);
    ChannelWriter<string>? GetMessageWriter(string instanceId);
    ChannelReader<string> RegisterInstance(string instanceId);
    void UnregisterInstance(string instanceId);
}

public sealed class ActiveInstanceRegistry : IActiveInstanceRegistry
{
    private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();

    public ChannelReader<string>? GetMessageReader(string instanceId)
    {
        return _channels.TryGetValue(instanceId, out var channel) ? channel.Reader : null;
    }

    public ChannelWriter<string>? GetMessageWriter(string instanceId)
    {
        return _channels.TryGetValue(instanceId, out var channel) ? channel.Writer : null;
    }

    public ChannelReader<string> RegisterInstance(string instanceId)
    {
        var channel = Channel.CreateUnbounded<string>();
        _channels[instanceId] = channel;
        return channel.Reader;
    }

    public void UnregisterInstance(string instanceId)
    {
        _channels.TryRemove(instanceId, out _);
    }
}
