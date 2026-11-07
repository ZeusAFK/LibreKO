using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class PremiumSwitchTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static UserSession Holder()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = new SessionManager().CreateSession(client, 1, 1);
        PremiumHoldings.Grant(session, PremiumHoldings.SwitchingPremium, 30, Now);
        return session;
    }

    [Fact]
    public void APlayerSwitchesToAnotherPremiumTheyHold()
    {
        var session = Holder();

        PremiumHoldings.Switch(session, PremiumHoldings.WarPremium, Now).Should().Be(PremiumSwitchResult.Switched);

        session.PremiumService.Should().Be(PremiumHoldings.WarPremium);
    }

    [Fact]
    public void ASecondSwitchWaitsThirtySeconds()
    {
        var session = Holder();
        PremiumHoldings.Switch(session, PremiumHoldings.WarPremium, Now);

        PremiumHoldings.Switch(session, PremiumHoldings.ExpPremium, Now.AddSeconds(10)).Should().Be(PremiumSwitchResult.TooSoon);
        PremiumHoldings.SwitchWait(session, Now.AddSeconds(10)).Should().Be(TimeSpan.FromSeconds(20));
        PremiumHoldings.Switch(session, PremiumHoldings.ExpPremium, Now.AddSeconds(30)).Should().Be(PremiumSwitchResult.Switched);
    }

    [Fact]
    public void APremiumNotHeldCannotBeChosen()
    {
        PremiumHoldings.Switch(Holder(), 5, Now).Should().Be(PremiumSwitchResult.NotHeld);
    }

    [Fact]
    public void TheListNamesEveryHeldPremiumWithItsHours()
    {
        var listed = PremiumHoldings.Listed(Holder(), Now);

        listed.Should().Equal(
            new HeldPremium(PremiumHoldings.DcPremium, 720),
            new HeldPremium(PremiumHoldings.ExpPremium, 720),
            new HeldPremium(PremiumHoldings.WarPremium, 720));
    }

    [Fact]
    public void ASwitchTooSoonCarriesTheSecondsLeft()
    {
        var packet = MiscPacketWriter.PremiumSwitchWait(PremiumHoldings.ExpPremium, 20);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(MiscPacketWriter.PremiumSwitch);
        packet.ReadByte().Should().Be(PremiumHoldings.ExpPremium);
        packet.ReadShort().Should().Be(MiscPacketWriter.PremiumSwitchTooSoon);
        packet.ReadUShort().Should().Be(20);
        packet.RemainingBytes.Should().Be(0);
    }
}
