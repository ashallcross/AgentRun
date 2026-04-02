using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Security;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Security;

[TestFixture]
public class SsrfProtectionTests
{
    private INetworkAccessPolicy _policy = null!;
    private IDnsResolver _dnsResolver = null!;
    private SsrfProtection _ssrfProtection = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = Substitute.For<INetworkAccessPolicy>();
        _dnsResolver = Substitute.For<IDnsResolver>();
        _ssrfProtection = new SsrfProtection(_policy, _dnsResolver);
    }

    [Test]
    public async Task ValidPublicUrl_PassesValidation()
    {
        var uri = new Uri("https://example.com");
        _dnsResolver.ResolveAsync("example.com", Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Parse("93.184.216.34") });
        _policy.IsAddressAllowed(Arg.Any<IPAddress>()).Returns(true);

        await _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None);

        await _dnsResolver.Received(1).ResolveAsync("example.com", Arg.Any<CancellationToken>());
        _policy.Received(1).IsAddressAllowed(IPAddress.Parse("93.184.216.34"));
    }

    [Test]
    public void UrlResolvingToPrivateIp_Throws()
    {
        var uri = new Uri("https://evil.com");
        _dnsResolver.ResolveAsync("evil.com", Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Parse("10.0.0.1") });
        _policy.IsAddressAllowed(IPAddress.Parse("10.0.0.1")).Returns(false);

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("resolves to a blocked address"));
    }

    [Test]
    public void UrlResolvingToLoopback_Throws()
    {
        var uri = new Uri("https://evil.com");
        _dnsResolver.ResolveAsync("evil.com", Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Loopback });
        _policy.IsAddressAllowed(IPAddress.Loopback).Returns(false);

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("resolves to a blocked address"));
    }

    [Test]
    public void NonHttpScheme_Throws()
    {
        var uri = new Uri("ftp://example.com/file");

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("only HTTP and HTTPS schemes are allowed"));
    }

    [Test]
    public void FileScheme_Throws()
    {
        var uri = new Uri("file:///etc/passwd");

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("only HTTP and HTTPS schemes are allowed"));
    }

    [Test]
    public void DnsResolutionFailure_Throws()
    {
        var uri = new Uri("https://nonexistent.invalid");
        _dnsResolver.ResolveAsync("nonexistent.invalid", Arg.Any<CancellationToken>())
            .ThrowsAsync(new System.Net.Sockets.SocketException());

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("DNS resolution failed"));
    }

    [Test]
    public void MultipleIps_OneBlocked_Throws()
    {
        var uri = new Uri("https://mixed.com");
        var publicIp = IPAddress.Parse("93.184.216.34");
        var privateIp = IPAddress.Parse("10.0.0.1");
        _dnsResolver.ResolveAsync("mixed.com", Arg.Any<CancellationToken>())
            .Returns(new[] { publicIp, privateIp });
        _policy.IsAddressAllowed(publicIp).Returns(true);
        _policy.IsAddressAllowed(privateIp).Returns(false);

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("resolves to a blocked address"));
    }

    [Test]
    public void EmptyDnsResult_Throws()
    {
        var uri = new Uri("https://empty-dns.com");
        _dnsResolver.ResolveAsync("empty-dns.com", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<IPAddress>());

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _ssrfProtection.ValidateUrlAsync(uri, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("DNS resolution failed"));
    }
}
