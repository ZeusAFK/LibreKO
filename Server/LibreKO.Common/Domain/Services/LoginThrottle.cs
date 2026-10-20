using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
    private const int TrackedBeforePruning = 10_000;
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

    private readonly ConcurrentDictionary<string, Failures> byAddressAndName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Failures> byAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Failures> byName = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    private long nextPruneTicks;

    // An attempt is counted before its password is checked, so attempts sent at once cannot all slip under the limit;
    // an address over the limit, for one login or in all, waits out the window before a password is checked.
    public bool TryAttempt(string address, string login)
    {
        var name = Normal(login);
        if (address.Length > 0)
        {
            var forName = Count(byAddressAndName, Key(address, name));
            var forAddress = Count(byAddress, address);
            if (forName > MaxFailuresPerName || forAddress > MaxFailuresPerAddress)
                return false;
        }

        Count(byName, name);
        return true;
    }

    // A name guessed from many addresses is answered more slowly, so its owner is slowed but never locked out.
    public TimeSpan SlowdownFor(string login)
    {
        if (!byName.TryGetValue(Normal(login), out var failures))
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
        byAddressAndName.TryRemove(Key(address, name), out _);
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

    // One IPv6 network counts as one address, since its owner holds every address in it.
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

    private int Count(ConcurrentDictionary<string, Failures> table, string key)
    {
        // Expired entries are swept at most once a minute, so a flood of new names cannot make every attempt sweep.
        var due = Volatile.Read(ref nextPruneTicks);
        if (table.Count > TrackedBeforePruning && Now.UtcTicks >= due
            && Interlocked.CompareExchange(ref nextPruneTicks, (Now + PruneInterval).UtcTicks, due) == due)
            Prune(table);

        var failures = table.GetOrAdd(key, _ => new Failures { Since = Now });
        lock (failures)
        {
            if (Now - failures.Since >= Window)
                (failures.Count, failures.Since) = (0, Now);
            return ++failures.Count;
        }
    }

    private static void Uncount(ConcurrentDictionary<string, Failures> table, string key)
    {
        if (!table.TryGetValue(key, out var failures))
            return;

        lock (failures)
            failures.Count = Math.Max(0, failures.Count - 1);
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
