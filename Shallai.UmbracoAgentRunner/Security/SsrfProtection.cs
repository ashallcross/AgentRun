using System.Net;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Security;

public class SsrfProtection
{
    private readonly INetworkAccessPolicy _policy;
    private readonly IDnsResolver _dnsResolver;

    public SsrfProtection(INetworkAccessPolicy policy)
        : this(policy, new SystemDnsResolver())
    {
    }

    internal SsrfProtection(INetworkAccessPolicy policy, IDnsResolver dnsResolver)
    {
        _policy = policy;
        _dnsResolver = dnsResolver;
    }

    public async Task ValidateUrlAsync(Uri url, CancellationToken cancellationToken)
    {
        // Validate scheme
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ToolExecutionException(
                $"Access denied: only HTTP and HTTPS schemes are allowed, got '{url.Scheme}'");
        }

        // Resolve DNS
        IPAddress[] addresses;
        try
        {
            addresses = await _dnsResolver.ResolveAsync(url.Host, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ToolExecutionException($"DNS resolution failed for '{url.Host}'");
        }

        if (addresses.Length == 0)
        {
            throw new ToolExecutionException($"DNS resolution failed for '{url.Host}'");
        }

        // Check ALL resolved addresses against policy
        foreach (var address in addresses)
        {
            if (!_policy.IsAddressAllowed(address))
            {
                throw new ToolExecutionException(
                    $"Access denied: URL '{url}' resolves to a blocked address");
            }
        }
    }
}

internal interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        => Dns.GetHostAddressesAsync(host, cancellationToken);
}
