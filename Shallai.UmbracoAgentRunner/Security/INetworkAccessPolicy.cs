using System.Net;

namespace Shallai.UmbracoAgentRunner.Security;

public interface INetworkAccessPolicy
{
    bool IsAddressAllowed(IPAddress address);
}
