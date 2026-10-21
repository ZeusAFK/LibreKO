using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Common.Domain.Services;

public sealed class LoginThrottle(TimeProvider? clock = null)
{
    public const int MaxFailuresPerName = 5;
    public const int AddressFailuresBeforeSlowing = 20;
    public const int FailuresBeforeSlowing = 25;
    private const int WindowMinutes = 15;
    private const int SlowdownStepMilliseconds = 500;
    private const int MaxSlowdownSeconds = 5;
    public const int MaxTracked = 100_000;
    private const int KeptAfterEviction = MaxTracked / 10 * 9;
    private const int PruneIntervalSeconds = 60;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(PruneIntervalSeconds);
    private const int Ipv6NetworkBytes = 8;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(WindowMinutes);
    public static readonly TimeSpan SlowdownStep = TimeSpan.FromMilliseconds(SlowdownStepMilliseconds);
    public static readonly TimeSpan MaxSlowdown = TimeSpan.FromSeconds(MaxSlowdownSeconds);

    private sealed class Failures
    {
        public int Count;
        public DateTimeOffset Since;
        public bool Retired;
    }

    private sealed class Table
    {
        public readonly ConcurrentDictionary<string, Failures> Entries = new(StringComparer.OrdinalIgnoreCase);
        public int Size;
        public long NextPruneTicks;
        public int Evicting;
    }

    private readonly Table byAddressAndName = new();
    private readonly Table byAddress = new();
    private readonly Table byName = new();
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    public int Tracked => Volatile.Read(ref byAddressAndName.Size) + Volatile.Read(ref byAddress.Size) + Volatile.Read(ref byName.Size);

    // An attempt counts before its password is checked; a name over its limit from one address waits out the window and adds nothing while it waits.
    public bool TryAttempt(string address, string login)
    {
        if (login.Length > Account.MaxLoginLength)
            return false;

        var name = Normal(login);
        if (address.Length > 0)
        {
            var key = Key(address, name);
            if (Over(byAddressAndName, key, MaxFailuresPerName))
                return false;

            var forName = Count(byAddressAndName, key);
            Count(byAddress, address);
            if (forName > MaxFailuresPerName)
                return false;
        }

        Count(byName, name);
        return true;
    }

    // A name guessed from many addresses, or an address that guesses many names, is answered more slowly but never locked out.
    public TimeSpan SlowdownFor(string address, string login)
    {
        var forName = Slowdown(byName, Normal(login), FailuresBeforeSlowing);
        var forAddress = address.Length == 0 ? TimeSpan.Zero : Slowdown(byAddress, address, AddressFailuresBeforeSlowing);
        return forName > forAddress ? forName : forAddress;
    }

    private TimeSpan Slowdown(Table table, string key, int free)
    {
        if (!table.Entries.TryGetValue(key, out var failures))
            return TimeSpan.Zero;

        lock (failures)
        {
            if (Now - failures.Since >= Window || failures.Count <= free)
                return TimeSpan.Zero;
            var steps = failures.Count - free;
            return TimeSpan.FromTicks(Math.Min(SlowdownStep.Ticks * steps, MaxSlowdown.Ticks));
        }
    }

    public void Succeeded(string address, string login)
    {
        var name = Normal(login);
        Uncount(byName, name);
        if (address.Length == 0)
            return;
        var key = Key(address, name);
        if (byAddressAndName.Entries.TryGetValue(key, out var pair))
        {
            lock (pair)
                Retire(byAddressAndName, key, pair);
        }

        Uncount(byAddress, address);
    }

    // The same name the database matches: case, trailing spaces and accents aside.
    public static string Normal(string login)
    {
        var decomposed = login.TrimEnd().Normalize(System.Text.NormalizationForm.FormD);
        var plain = new System.Text.StringBuilder(decomposed.Length);
        foreach (var letter in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(letter) != System.Globalization.UnicodeCategory.NonSpacingMark)
                plain.Append(char.ToUpperInvariant(letter));
        }

        return plain.ToString();
    }

    public static string AddressOf(IClient client) =>
        (client.Socket?.RemoteEndPoint as IPEndPoint)?.Address is { } address ? Bucket(address) : string.Empty;

    // One IPv6 network counts as one address.
    public static string Bucket(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().ToString();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6NetworkBytes, bytes.Length - Ipv6NetworkBytes);
        return new IPAddress(bytes).ToString();
    }

    private static string Key(string address, string login) => $"{address}\n{login}";

    private bool Over(Table table, string key, int limit)
    {
        if (!table.Entries.TryGetValue(key, out var failures))
            return false;

        lock (failures)
            return Now - failures.Since < Window && failures.Count >= limit;
    }

    private int Count(Table table, string key)
    {
        while (true)
        {
            var found = table.Entries.TryGetValue(key, out var failures);
            var now = Now;
            var due = Volatile.Read(ref table.NextPruneTicks);
            if (now.UtcTicks >= due && Interlocked.CompareExchange(ref table.NextPruneTicks, (now + PruneInterval).UtcTicks, due) == due)
                Prune(table, now);

            if (!found || failures == null)
            {
                if (Volatile.Read(ref table.Size) >= MaxTracked)
                    MakeRoom(table, now);
                var added = new Failures { Since = now };
                failures = table.Entries.GetOrAdd(key, added);
                if (ReferenceEquals(failures, added))
                    Interlocked.Increment(ref table.Size);
            }

            lock (failures)
            {
                if (failures.Retired)
                    continue;
                if (now - failures.Since >= Window)
                    (failures.Count, failures.Since) = (0, now);
                return ++failures.Count;
            }
        }
    }

    private static void Uncount(Table table, string key)
    {
        if (!table.Entries.TryGetValue(key, out var failures))
            return;

        lock (failures)
        {
            failures.Count = Math.Max(0, failures.Count - 1);
            if (failures.Count == 0)
                Retire(table, key, failures);
        }
    }

    // Only under the entry's own lock, so a count waiting on that lock sees the entry is gone.
    private static void Retire(Table table, string key, Failures failures)
    {
        failures.Retired = true;
        if (table.Entries.TryRemove(KeyValuePair.Create(key, failures)))
            Interlocked.Decrement(ref table.Size);
    }

    private static void MakeRoom(Table table, DateTimeOffset now)
    {
        if (Interlocked.CompareExchange(ref table.Evicting, 1, 0) != 0)
            return;

        try
        {
            Prune(table, now);
            var excess = Volatile.Read(ref table.Size) - KeptAfterEviction;
            if (excess <= 0)
                return;

            foreach (var (key, failures) in table.Entries.OrderBy(entry => entry.Value.Since).Take(excess).ToList())
            {
                lock (failures)
                    Retire(table, key, failures);
            }
        }
        finally
        {
            Volatile.Write(ref table.Evicting, 0);
        }
    }

    private static void Prune(Table table, DateTimeOffset now)
    {
        foreach (var (key, failures) in table.Entries)
        {
            lock (failures)
            {
                if (now - failures.Since >= Window)
                    Retire(table, key, failures);
            }
        }
    }
}
