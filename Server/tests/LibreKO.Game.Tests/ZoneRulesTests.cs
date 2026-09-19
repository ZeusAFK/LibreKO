using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ZoneRulesTests : GameTestBase
{
    [Theory]
    [InlineData(ZoneId.Moradon, ZoneAbilityType.Neutral)]
    [InlineData(ZoneId.Delos, ZoneAbilityType.SiegeDisabled)]
    [InlineData(ZoneId.Bifrost, ZoneAbilityType.PvpNeutralNpcs)]
    [InlineData(ZoneId.DesperationAbyss, ZoneAbilityType.PvpNeutralNpcs)]
    [InlineData(ZoneId.HellAbyss, ZoneAbilityType.PvpNeutralNpcs)]
    [InlineData(ZoneId.DragonCave, ZoneAbilityType.PvpNeutralNpcs)]
    [InlineData(ZoneId.DrakiTower, ZoneAbilityType.SiegeType2)]
    [InlineData(ZoneId.CaitharosArena, ZoneAbilityType.CaitharosArena)]
    [InlineData(ZoneId.ClanWar, ZoneAbilityType.SiegeDisabled)]
    [InlineData(ZoneId.RonarkLand, ZoneAbilityType.Pvp)]
    [InlineData(ZoneId.NapiesGorge, ZoneAbilityType.Pvp)]
    [InlineData(ZoneId.KarusCamp1, ZoneAbilityType.Pvp)]
    public void EachZoneReportsItsOwnState(ZoneId zoneId, ZoneAbilityType expected)
        => ZoneRules.For((byte)zoneId).Ability.Should().Be(expected);

    [Theory]
    [InlineData(ZoneId.Moradon, true, true)]
    [InlineData(ZoneId.Delos, true, true)]
    [InlineData(ZoneId.ForgottenTemple, true, true)]
    [InlineData(ZoneId.DesperationAbyss, false, true)]
    [InlineData(ZoneId.Arena, false, true)]
    [InlineData(ZoneId.RonarkLand, false, false)]
    [InlineData(ZoneId.KarusCamp1, false, false)]
    public void TownsAreWhereTheTwoNationsMayDeal(ZoneId zoneId, bool trade, bool talk)
    {
        ZoneRules.Allows((byte)zoneId, ZoneFlags.TradeOtherNation).Should().Be(trade);
        ZoneRules.Allows((byte)zoneId, ZoneFlags.TalkOtherNation).Should().Be(talk);
    }

    [Theory]
    [InlineData(ZoneId.Moradon, true)]
    [InlineData(ZoneId.KarusCamp1, true)]
    [InlineData(ZoneId.ElMoradCamp2, true)]
    [InlineData(ZoneId.RonarkLand, false)]
    [InlineData(ZoneId.Bifrost, false)]
    public void ClansAreOnlyRunFromHomeAndTheCommonTown(ZoneId zoneId, bool expected)
        => ZoneRules.Allows((byte)zoneId, ZoneFlags.ClanUpdate).Should().Be(expected);

    [Theory]
    [InlineData(ZoneId.NapiesGorge, true)]
    [InlineData(ZoneId.SnowBattle, true)]
    [InlineData(ZoneId.RonarkLand, false)]
    public void OnlyTheDeclaredBattlegroundsAreWarZones(ZoneId zoneId, bool expected)
        => ZoneRules.Allows((byte)zoneId, ZoneFlags.WarZone).Should().Be(expected);

    [Fact]
    public void AZoneNobodyDescribedPermitsNothing()
    {
        var rule = ZoneRules.For((byte)ZoneId.DungeonDefence);
        rule.Ability.Should().Be(ZoneAbilityType.Pvp);
        rule.Flags.Should().Be(ZoneFlags.None);
    }

    [Fact]
    public async Task TheZoneStatePacketCarriesTheTradeAndTalkRules()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        Packet? sent = null;
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(920);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(call => { sent = call.Arg<Packet>(); return Task.CompletedTask; });

        var session = sessionManager.CreateSession(client, 920, accountId: 1020);
        session.Name = "Trader";
        session.Nation = AccountNation.Karus;
        session.ZoneId = (byte)ZoneId.Moradon;
        session.X = 816f;
        session.Z = 531f;

        await provider.GetRequiredService<IZoneTransitionService>().SendZoneAbilityAsync(session);

        sent.Should().NotBeNull();
        sent!.GetOpcode().Should().Be((byte)GameOpcodes.GS_ZONEABILITY);
        sent.ReadByte().Should().Be(1);
        sent.ReadByte().Should().Be(1);
        sent.ReadByte().Should().Be((byte)ZoneAbilityType.Neutral);
        sent.ReadByte().Should().Be(1);
    }
}
