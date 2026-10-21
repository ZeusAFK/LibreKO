using System.Net;
using System.Net.Sockets;
using LibreKO.Login.Configuration;
using Microsoft.Extensions.Options;

namespace LibreKO.Login;

public sealed class AccountCreationLimiter(IOptions<LoginServerSettings> settings)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int Ipv4NetworkBytes = 3;
    private const int Ipv6NetworkBytes = 8;
    private readonly Dictionary<string, Queue<DateTime>> created = new();
    private readonly object gate = new();

    public bool TryTake(IPAddress? address, DateTime now)
    {
        int limit = settings.Value.Account.AutoCreatePerNetworkPerHour;
        if (limit <= 0 || address == null || IPAddress.IsLoopback(address)) return true;
        string network = NetworkOf(address);
        lock (gate)
        {
            if (!created.TryGetValue(network, out var times)) created[network] = times = new Queue<DateTime>();
            while (times.Count > 0 && now - times.Peek() >= Window) times.Dequeue();
            if (times.Count >= limit) return false;
            times.Enqueue(now);
            return true;
        }
    }

    public static string NetworkOf(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        int keep = address.AddressFamily == AddressFamily.InterNetwork ? Ipv4NetworkBytes : Ipv6NetworkBytes;
        return Convert.ToHexString(bytes, 0, Math.Min(keep, bytes.Length));
    }
}
