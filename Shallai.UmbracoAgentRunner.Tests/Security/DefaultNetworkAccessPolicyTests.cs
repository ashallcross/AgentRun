using System.Net;
using Shallai.UmbracoAgentRunner.Security;

namespace Shallai.UmbracoAgentRunner.Tests.Security;

[TestFixture]
public class DefaultNetworkAccessPolicyTests
{
    private DefaultNetworkAccessPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = new DefaultNetworkAccessPolicy();
    }

    [Test]
    public void PublicIPv4_IsAllowed()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("8.8.8.8")), Is.True);
    }

    [Test]
    public void PrivateRange_10_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("10.0.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("10.255.255.255")), Is.False);
    }

    [Test]
    public void PrivateRange_172_16_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("172.16.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("172.31.255.255")), Is.False);
    }

    [Test]
    public void PrivateRange_172_Boundary_IsAllowed()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("172.15.255.255")), Is.True);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("172.32.0.1")), Is.True);
    }

    [Test]
    public void PrivateRange_192_168_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("192.168.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("192.168.255.255")), Is.False);
    }

    [Test]
    public void Loopback_127_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("127.0.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("127.255.255.255")), Is.False);
    }

    [Test]
    public void LinkLocal_169_254_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("169.254.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("169.254.255.255")), Is.False);
    }

    [Test]
    public void IPv6Loopback_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.IPv6Loopback), Is.False);
    }

    [Test]
    public void IPv6LinkLocal_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("fe80::1")), Is.False);
    }

    [Test]
    public void IPv4MappedIPv6_Private_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("::ffff:127.0.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("::ffff:10.0.0.1")), Is.False);
    }

    [Test]
    public void IPv4MappedIPv6_Public_IsAllowed()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("::ffff:8.8.8.8")), Is.True);
    }

    [Test]
    public void PublicIPv6_IsAllowed()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("2001:4860:4860::8888")), Is.True);
    }

    [Test]
    public void ZeroNetwork_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("0.0.0.0")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("0.255.255.255")), Is.False);
    }

    [Test]
    public void CgnatRange_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("100.64.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("100.127.255.255")), Is.False);
    }

    [Test]
    public void CgnatBoundary_IsAllowed()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("100.63.255.255")), Is.True);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("100.128.0.1")), Is.True);
    }

    [Test]
    public void ReservedRange_240_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("240.0.0.1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("255.255.255.255")), Is.False);
    }

    [Test]
    public void IPv6UniqueLocal_IsBlocked()
    {
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("fc00::1")), Is.False);
        Assert.That(_policy.IsAddressAllowed(IPAddress.Parse("fd00::1")), Is.False);
    }
}
