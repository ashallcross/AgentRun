using AgentRun.Umbraco.Engine;

namespace AgentRun.Umbraco.Services;

/// <summary>
/// Default <see cref="IWebSearchProviderFactory"/> implementation. Indexes
/// registered <see cref="IWebSearchProvider"/> instances by
/// <see cref="IWebSearchProvider.Name"/> (case-insensitive). Composer
/// registers providers in preferred-order so the "first registered with
/// configured key" fallback in <c>WebSearchTool</c> honours operator
/// intent. Story 11.8.
/// </summary>
public sealed class WebSearchProviderFactory : IWebSearchProviderFactory
{
    private readonly Dictionary<string, IWebSearchProvider> _byName;
    private readonly string[] _registrationOrder;

    public WebSearchProviderFactory(IEnumerable<IWebSearchProvider> providers)
    {
        _byName = new Dictionary<string, IWebSearchProvider>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var provider in providers)
        {
            if (!_byName.TryAdd(provider.Name, provider))
            {
                throw new InvalidOperationException(
                    $"Multiple web search providers registered with name '{provider.Name}'. Provider names must be unique.");
            }
            order.Add(provider.Name);
        }
        _registrationOrder = order.ToArray();
    }

    public IWebSearchProvider GetAsync(string providerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("provider name must be a non-empty string", nameof(providerName));

        if (_byName.TryGetValue(providerName, out var provider))
            return provider;

        var registered = string.Join(", ", _registrationOrder);
        throw new WebSearchException(
            $"No web search provider registered with name '{providerName}'. Registered providers: {registered}.");
    }

    public IReadOnlyList<string> GetRegisteredProviderNames() => _registrationOrder;
}
