using System.Net;
using System.Net.Sockets;

namespace LibreKO.Common.Infrastructure.Network;

public static class AddressBucket
{
    private const int Ipv6NetworkBytes = 8;

    // One IPv6 network counts as one address, as a single host is handed a whole /64.
    public static IPAddress Of(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address;

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6NetworkBytes, bytes.Length - Ipv6NetworkBytes);
        return new IPAddress(bytes);
    }
}
