using System.Net;
using System.Net.Sockets;

namespace AgentRun.Umbraco.Security;

public class DefaultNetworkAccessPolicy : INetworkAccessPolicy
{
    public bool IsAddressAllowed(IPAddress address)
    {
        // Handle IPv4-mapped IPv6 addresses (e.g., ::ffff:127.0.0.1)
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10) return false;

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return false;

            // 127.0.0.0/8 (loopback)
            if (bytes[0] == 127) return false;

            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254) return false;

            // 0.0.0.0/8 (aliases localhost on many systems)
            if (bytes[0] == 0) return false;

            // 100.64.0.0/10 (CGNAT / shared address space, RFC 6598)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return false;

            // 240.0.0.0/4 (reserved/broadcast)
            if (bytes[0] >= 240) return false;

            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 (IPv6 loopback)
            if (IPAddress.IsLoopback(address)) return false;

            // fe80::/10 (IPv6 link-local)
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return false;

            // fc00::/7 (unique local addresses, IPv6 equivalent of RFC 1918)
            if ((bytes[0] & 0xFE) == 0xFC) return false;

            return true;
        }

        // Deny unknown address families by default
        return false;
    }
}
