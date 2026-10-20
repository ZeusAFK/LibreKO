using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Common.Domain.Services;

public sealed class LoginThrottle(TimeProvider? clock = null)
{
    public const int MaxFailuresPerName = 5;
    public const int MaxFailuresPerAddress = 20;
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

    // An attempt counts before its password is checked; an address over a limit waits out the window and adds nothing while it waits.
    public bool TryAttempt(string address, string login)
    {
        if (login.Length > Account.MaxLoginLength)
            return false;

        var name = Normal(login);
        if (address.Length > 0)
        {
            var key = Key(address, name);
            if (Over(byAddress, address, MaxFailuresPerAddress) || Over(byAddressAndName, key, MaxFailuresPerName))
                return false;

            var forName = Count(byAddressAndName, key);
            var forAddress = Count(byAddress, address);
            if (forName > MaxFailuresPerName || forAddress > MaxFailuresPerAddress)
                return false;
        }

        Count(byName, name);
        return true;
    }

    // A name guessed from many addresses is answered more slowly but never locked.
    public TimeSpan SlowdownFor(string login)
    {
        if (!byName.Entries.TryGetValue(Normal(login), out var failures))
            return TimeSpan.Zero;

        lock (failures)
        {
            if (Now - failures.Since >= Window || failures.Count <= FailuresBeforeSlowing)
                return TimeSpan.Zero;
            var steps = failures.Count - FailuresBeforeSlowing;
            return TimeSpan.FromTicks(Math.Min(SlowdownStep.Ticks * steps, MaxSlowdown.Ticks));
        }
    }

    // A login that let the player in was no failure: it clears the name for this address and takes its attempt back.
    public void Succeeded(string address, string login)
    {
        var name = Normal(login);
        Uncount(byName, name);
        if (address.Length == 0)
            return;
        if (byAddressAndName.Entries.TryRemove(Key(address, name), out _))
            Interlocked.Decrement(ref byAddressAndName.Size);
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

    // Each table sweeps its expired entries at most once a minute, and a full one makes room by dropping its oldest.
    private int Count(Table table, string key)
    {
        var due = Volatile.Read(ref table.NextPruneTicks);
        if (Now.UtcTicks >= due && Interlocked.CompareExchange(ref table.NextPruneTicks, (Now + PruneInterval).UtcTicks, due) == due)
            Prune(table);

        if (!table.Entries.TryGetValue(key, out var failures))
        {
            if (Volatile.Read(ref table.Size) >= MaxTracked)
                MakeRoom(table);
            var added = new Failures { Since = Now };
            failures = table.Entries.GetOrAdd(key, added);
            if (ReferenceEquals(failures, added))
                Interlocked.Increment(ref table.Size);
        }

        lock (failures)
        {
            if (Now - failures.Since >= Window)
                (failures.Count, failures.Since) = (0, Now);
            return ++failures.Count;
        }
    }

    private static void Uncount(Table table, string key)
    {
        if (!table.Entries.TryGetValue(key, out var failures))
            return;

        lock (failures)
            failures.Count = Math.Max(0, failures.Count - 1);
    }

    private void MakeRoom(Table table)
    {
        if (Interlocked.CompareExchange(ref table.Evicting, 1, 0) != 0)
            return;

        try
        {
            Prune(table);
            var excess = Volatile.Read(ref table.Size) - KeptAfterEviction;
            if (excess <= 0)
                return;

            foreach (var (key, _) in table.Entries.OrderBy(entry => entry.Value.Since).Take(excess).ToList())
            {
                if (table.Entries.TryRemove(key, out _))
                    Interlocked.Decrement(ref table.Size);
            }
        }
        finally
        {
            Volatile.Write(ref table.Evicting, 0);
        }
    }

    private void Prune(Table table)
    {
        foreach (var (key, failures) in table.Entries)
        {
            bool expired;
            lock (failures)
                expired = Now - failures.Since >= Window;
            if (expired && table.Entries.TryRemove(key, out _))
                Interlocked.Decrement(ref table.Size);
        }
    }
}
