using System.Net;
using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using Xunit;

namespace LibreKO.Common.Tests;

public class LoginThrottleTests
{
    private const string Address = "203.0.113.7";
    private const string OtherAddress = "198.51.100.9";
    private const int Burst = 64;
    private const int Flood = 5_000;
    private const int TablesTracked = 3;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public Action? OnNextRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            var meanwhile = OnNextRead;
            OnNextRead = null;
            meanwhile?.Invoke();
            return Now;
        }
    }

    [Fact]
    public void FiveMissesOnOneNameLockThatNameFromThatAddress()
    {
        var throttle = new LoginThrottle();

        Attempts(throttle, Address, "victim", LoginThrottle.MaxFailuresPerName).Should().OnlyContain(allowed => allowed);

        throttle.TryAttempt(Address, "victim").Should().BeFalse();
        throttle.TryAttempt(Address, "VICTIM ").Should().BeFalse("the database matches the name without case or trailing spaces");
        throttle.TryAttempt(Address, "Víctim").Should().BeFalse("nor accents");
        throttle.TryAttempt(OtherAddress, "victim").Should().BeTrue("an attacker cannot lock someone else out of their account");
    }

    [Fact]
    public void AttemptsSentAtOnceCannotAllSlipUnderTheLimit()
    {
        var throttle = new LoginThrottle();
        var allowed = 0;

        Parallel.For(0, Burst, _ =>
        {
            if (throttle.TryAttempt(Address, "victim"))
                Interlocked.Increment(ref allowed);
        });

        allowed.Should().Be(LoginThrottle.MaxFailuresPerName);
    }

    [Fact]
    public void ManyMissesFromOneAddressSlowItDownButLockNoOneOut()
    {
        var throttle = new LoginThrottle();
        for (var name = 0; name < LoginThrottle.AddressFailuresBeforeSlowing; name++)
            throttle.TryAttempt(Address, $"guess{name}").Should().BeTrue();
        throttle.SlowdownFor(Address, "fresh").Should().Be(TimeSpan.Zero);

        throttle.TryAttempt(Address, "fresh").Should().BeTrue("everyone behind a shared address still gets their password checked");

        throttle.SlowdownFor(Address, "anyone").Should().Be(LoginThrottle.SlowdownStep, "one address that keeps missing is answered more slowly");
        throttle.SlowdownFor(OtherAddress, "anyone").Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AnAddressSlowdownStopsAtItsCeiling()
    {
        var throttle = new LoginThrottle();

        for (var name = 0; name < Flood; name++)
            throttle.TryAttempt(Address, $"guess{name}").Should().BeTrue();

        throttle.SlowdownFor(Address, "anyone").Should().Be(LoginThrottle.MaxSlowdown);
    }

    [Fact]
    public void ASuccessClearsTheNameAndTheWindowLetsGo()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        Attempts(throttle, Address, "owner", LoginThrottle.MaxFailuresPerName - 1);
        throttle.TryAttempt(Address, "owner").Should().BeTrue();
        throttle.Succeeded(Address, "owner");

        Attempts(throttle, Address, "owner", LoginThrottle.MaxFailuresPerName).Should().OnlyContain(allowed => allowed,
            "the success started the count again");
        throttle.TryAttempt(Address, "owner").Should().BeFalse();
        clock.Now += LoginThrottle.Window;
        throttle.TryAttempt(Address, "owner").Should().BeTrue("the lock lasts one window");
    }

    [Fact]
    public void ANameGuessedFromManyAddressesIsSlowedButNotLocked()
    {
        var throttle = new LoginThrottle();
        for (var guess = 0; guess < LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt($"192.0.2.{guess}", "victim");
        throttle.SlowdownFor(string.Empty, "victim").Should().Be(TimeSpan.Zero);

        throttle.TryAttempt("192.0.2.200", "victim");

        throttle.SlowdownFor(string.Empty, "Victim").Should().Be(LoginThrottle.SlowdownStep);
        throttle.TryAttempt(OtherAddress, "victim").Should().BeTrue("the owner still gets in, only more slowly");
        for (var guess = 0; guess < LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt($"198.18.0.{guess}", "victim");
        throttle.SlowdownFor(string.Empty, "victim").Should().Be(LoginThrottle.MaxSlowdown);
    }

    [Fact]
    public void AttemptsRefusedAtTheAddressDoNotSlowTheName()
    {
        var throttle = new LoginThrottle();

        for (var guess = 0; guess < 2 * LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt(Address, "victim");

        throttle.SlowdownFor(string.Empty, "victim").Should().Be(TimeSpan.Zero, "only attempts that reach the password count against the name");
    }

    [Fact]
    public void AnIpv6NetworkCountsAsOneAddress()
    {
        LoginThrottle.Bucket(IPAddress.Parse("2001:db8:1:2:aaaa::1"))
            .Should().Be(LoginThrottle.Bucket(IPAddress.Parse("2001:db8:1:2:bbbb::9")));
        LoginThrottle.Bucket(IPAddress.Parse("2001:db8:1:3::1"))
            .Should().NotBe(LoginThrottle.Bucket(IPAddress.Parse("2001:db8:1:2::1")));
        LoginThrottle.Bucket(IPAddress.Parse("::ffff:203.0.113.7")).Should().Be(Address);
    }

    [Fact]
    public void AnUnknownAddressSharesNoBucket()
    {
        var throttle = new LoginThrottle();
        for (var miss = 0; miss < LoginThrottle.AddressFailuresBeforeSlowing; miss++)
            throttle.TryAttempt(string.Empty, "someone");

        throttle.TryAttempt(string.Empty, "anyone").Should().BeTrue();
    }

    [Fact]
    public void ANameOverItsLimitFromOneAddressStoresNothingMore()
    {
        var throttle = new LoginThrottle();
        Attempts(throttle, Address, "victim", LoginThrottle.MaxFailuresPerName + 1);
        var tracked = throttle.Tracked;

        Attempts(throttle, Address, "victim", Flood).Should().OnlyContain(allowed => !allowed);

        throttle.Tracked.Should().Be(tracked, "a refused attempt must not cost the server memory");
    }

    [Fact]
    public void ALoginLongerThanAnyAccountIsRefusedAndNotKept()
    {
        var throttle = new LoginThrottle();

        throttle.TryAttempt(Address, new string('x', Account.MaxLoginLength + 1)).Should().BeFalse();

        throttle.Tracked.Should().Be(0);
    }

    [Fact]
    public void AFullTableStillCountsTheNewestAttemptsAndStaysBounded()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        for (var address = 0; address < LoginThrottle.MaxTracked + Burst; address++)
            throttle.TryAttempt(Filler(address), $"name{address}");
        clock.Now += TimeSpan.FromSeconds(1);

        Attempts(throttle, Address, "victim", LoginThrottle.MaxFailuresPerName).Should().OnlyContain(allowed => allowed);

        throttle.TryAttempt(Address, "victim").Should().BeFalse("a full table makes room rather than letting new attempts through uncounted");
        throttle.Tracked.Should().BeLessThanOrEqualTo(TablesTracked * LoginThrottle.MaxTracked);
    }

    private static string Filler(int address) => $"10.{address >> 16 & 0xFF}.{address >> 8 & 0xFF}.{address & 0xFF}";

    [Fact]
    public void LoginsThatSucceedTakeNoRoomFromAGuessedName()
    {
        var throttle = new LoginThrottle();
        for (var guess = 0; guess <= LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt($"192.0.2.{guess}", "victim");

        for (var name = 0; name < LoginThrottle.MaxTracked; name++)
        {
            throttle.TryAttempt(Address, $"new{name}");
            throttle.Succeeded(Address, $"new{name}");
        }

        throttle.SlowdownFor(string.Empty, "victim").Should().Be(LoginThrottle.SlowdownStep, "a login that let its player in leaves nothing behind to crowd out a guessed name");
    }

    [Fact]
    public void AnAttemptCountedAsItsEntryIsDroppedIsNotLost()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        throttle.TryAttempt(string.Empty, "victim");
        clock.OnNextRead = () => throttle.Succeeded(string.Empty, "victim");

        throttle.TryAttempt(string.Empty, "victim");
        for (var guess = 0; guess < LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt(string.Empty, "victim");

        throttle.SlowdownFor(string.Empty, "victim").Should().Be(LoginThrottle.SlowdownStep,
            "an attempt that looked its entry up just before a success dropped it still counts");
    }

    [Fact]
    public void AFullTableDropsItsOldestEntriesFirst()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        for (var name = 0; name < LoginThrottle.MaxTracked - 1; name++)
            throttle.TryAttempt(string.Empty, $"name{name}");
        clock.Now += TimeSpan.FromSeconds(1);
        for (var guess = 0; guess <= LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt($"192.0.2.{guess}", "victim");
        clock.Now += TimeSpan.FromSeconds(1);

        throttle.TryAttempt(string.Empty, "newcomer");

        throttle.SlowdownFor(string.Empty, "victim").Should().Be(LoginThrottle.SlowdownStep, "a full table drops its oldest entries, not the newest");
    }

    [Fact]
    public void ExpiredAttemptsAreSweptFromEveryTable()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        Attempts(throttle, Address, "owner", LoginThrottle.MaxFailuresPerName);
        Attempts(throttle, OtherAddress, "other", LoginThrottle.MaxFailuresPerName);

        clock.Now += LoginThrottle.Window + LoginThrottle.Window;
        throttle.TryAttempt(Address, "later");

        throttle.Tracked.Should().Be(TablesTracked, "only the newest attempt's address, name and pair are still kept");
    }

    [Theory]
    [InlineData(ServerType.Login, (byte)LoginOpcodes.LS_LOGIN, true)]
    [InlineData(ServerType.Login, (byte)LoginOpcodes.LS_MGAME_LOGIN, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_LOGIN, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_KICKOUT, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_DELETE_CHARACTER, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_ITEM_UPGRADE, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_VIP_WAREHOUSE, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_NATION_CHAT, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_MOVE, false)]
    public void APasswordNeverReachesTheLog(ServerType serverType, byte opcode, bool withheld)
    {
        var packet = new Packet(opcode);
        packet.WriteString("login");
        packet.WriteString("secret");

        (Client.LoggedBytes(serverType, packet) == Client.WithheldCredentials).Should().Be(withheld);
    }

    private static List<bool> Attempts(LoginThrottle throttle, string address, string login, int times) =>
        Enumerable.Range(0, times).Select(_ => throttle.TryAttempt(address, login)).ToList();
}
