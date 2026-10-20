using System.Collections.Concurrent;
using System.Net;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Common.Domain.Services;

public sealed class LoginThrottle(TimeProvider? clock = null)
{
    public const int MaxFailuresPerName = 5;
    public const int MaxFailuresPerAddress = 20;
    private const int WindowMinutes = 15;
    private const int TrackedBeforePruning = 10_000;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(WindowMinutes);

    private sealed class Failures
    {
        public int Count;
        public DateTimeOffset Since;
    }

    private readonly ConcurrentDictionary<string, Failures> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Failures> byAddress = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    // An address that failed too often, for one login or in all, waits out the window before a password is even checked.
    public bool IsLocked(string address, string login) =>
        Reached(byName, Key(address, login), MaxFailuresPerName) || Reached(byAddress, address, MaxFailuresPerAddress);

    public void Failed(string address, string login)
    {
        Count(byName, Key(address, login));
        Count(byAddress, address);
    }

    public void Succeeded(string address, string login) => byName.TryRemove(Key(address, login), out _);

    public static string AddressOf(IClient client) =>
        (client.Socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;

    private static string Key(string address, string login) => $"{address}\n{login}";

    private bool Reached(ConcurrentDictionary<string, Failures> table, string key, int limit)
    {
        if (!table.TryGetValue(key, out var failures))
            return false;

        lock (failures)
            return Now - failures.Since < Window && failures.Count >= limit;
    }

    private void Count(ConcurrentDictionary<string, Failures> table, string key)
    {
        if (table.Count > TrackedBeforePruning)
            Prune(table);

        var failures = table.GetOrAdd(key, _ => new Failures { Since = Now });
        lock (failures)
        {
            if (Now - failures.Since >= Window)
                (failures.Count, failures.Since) = (0, Now);
            failures.Count++;
        }
    }

    private void Prune(ConcurrentDictionary<string, Failures> table)
    {
        foreach (var (key, failures) in table)
        {
            bool expired;
            lock (failures)
                expired = Now - failures.Since >= Window;
            if (expired)
                table.TryRemove(key, out _);
        }
    }
}
