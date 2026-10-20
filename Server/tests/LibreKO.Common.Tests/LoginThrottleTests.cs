using System.Net;
using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using Xunit;

namespace LibreKO.Common.Tests;

public class LoginThrottleTests
{
    private const string Address = "203.0.113.7";
    private const string OtherAddress = "198.51.100.9";
    private const int Burst = 64;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
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
    public void ManyMissesFromOneAddressLockEveryName()
    {
        var throttle = new LoginThrottle();

        for (var name = 0; name < LoginThrottle.MaxFailuresPerAddress; name++)
            throttle.TryAttempt(Address, $"guess{name}").Should().BeTrue();

        throttle.TryAttempt(Address, "fresh").Should().BeFalse("one address cannot try a password on account after account");
        throttle.TryAttempt(OtherAddress, "fresh").Should().BeTrue();
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
        throttle.SlowdownFor("victim").Should().Be(TimeSpan.Zero);

        throttle.TryAttempt("192.0.2.200", "victim");

        throttle.SlowdownFor("Victim").Should().Be(LoginThrottle.SlowdownStep);
        throttle.TryAttempt(OtherAddress, "victim").Should().BeTrue("the owner still gets in, only more slowly");
        for (var guess = 0; guess < LoginThrottle.FailuresBeforeSlowing; guess++)
            throttle.TryAttempt($"198.18.0.{guess}", "victim");
        throttle.SlowdownFor("victim").Should().Be(LoginThrottle.MaxSlowdown);
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
        for (var miss = 0; miss < LoginThrottle.MaxFailuresPerAddress; miss++)
            throttle.TryAttempt(string.Empty, "someone");

        throttle.TryAttempt(string.Empty, "anyone").Should().BeTrue();
    }

    [Theory]
    [InlineData(ServerType.Login, (byte)LoginOpcodes.LS_LOGIN, true)]
    [InlineData(ServerType.Login, (byte)LoginOpcodes.LS_MGAME_LOGIN, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_LOGIN, true)]
    [InlineData(ServerType.Game, (byte)GameOpcodes.GS_KICKOUT, true)]
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
