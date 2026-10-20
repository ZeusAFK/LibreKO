using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using Xunit;

namespace LibreKO.Common.Tests;

public class LoginThrottleTests
{
    private const string Address = "203.0.113.7";
    private const string OtherAddress = "198.51.100.9";

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void FiveMissesOnOneNameLockThatNameFromThatAddress()
    {
        var throttle = new LoginThrottle();

        for (var miss = 0; miss < LoginThrottle.MaxFailuresPerName; miss++)
            throttle.IsLocked(Address, "victim").Should().BeFalse();
        Fail(throttle, Address, "victim", LoginThrottle.MaxFailuresPerName);

        throttle.IsLocked(Address, "victim").Should().BeTrue();
        throttle.IsLocked(Address, "VICTIM").Should().BeTrue("a login name is matched without case");
        throttle.IsLocked(OtherAddress, "victim").Should().BeFalse("an attacker cannot lock someone else out of their account");
    }

    [Fact]
    public void ManyMissesFromOneAddressLockEveryName()
    {
        var throttle = new LoginThrottle();

        for (var name = 0; name < LoginThrottle.MaxFailuresPerAddress; name++)
            throttle.Failed(Address, $"guess{name}");

        throttle.IsLocked(Address, "fresh").Should().BeTrue("one address cannot try a password on account after account");
        throttle.IsLocked(OtherAddress, "fresh").Should().BeFalse();
    }

    [Fact]
    public void ASuccessClearsTheNameAndTheWindowLetsGo()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        Fail(throttle, Address, "owner", LoginThrottle.MaxFailuresPerName - 1);
        throttle.Succeeded(Address, "owner");
        Fail(throttle, Address, "owner", LoginThrottle.MaxFailuresPerName - 1);
        throttle.IsLocked(Address, "owner").Should().BeFalse("the success started the count again");

        Fail(throttle, Address, "owner", 1);
        throttle.IsLocked(Address, "owner").Should().BeTrue();
        clock.Now += LoginThrottle.Window;
        throttle.IsLocked(Address, "owner").Should().BeFalse("the lock lasts one window");
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

    private static void Fail(LoginThrottle throttle, string address, string login, int times)
    {
        for (var miss = 0; miss < times; miss++)
            throttle.Failed(address, login);
    }
}
