using System.Net;

namespace AgentRun.Umbraco.Security;

public interface INetworkAccessPolicy
{
    bool IsAddressAllowed(IPAddress address);
}
