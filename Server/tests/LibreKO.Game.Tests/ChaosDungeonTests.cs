using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ChaosDungeonTests : GameTestBase
{
    [Fact]
    public void RankPercentRoundsUpAndSharesTies()
    {
        var ordered = ChaosRules.Ordered([
            new ChaosRules.Score(1, "A", 5, 0),
            new ChaosRules.Score(2, "B", 5, 0),
            new ChaosRules.Score(3, "C", 1, 4),
            new ChaosRules.Score(4, "D", 0, 9),
        ]);

        ChaosRules.RankPercent(ordered, 1).Should().Be(25);
        ChaosRules.RankPercent(ordered, 2).Should().Be(25);
        ChaosRules.RankPercent(ordered, 3).Should().Be(75);
        ChaosRules.RankPercent(ordered, 4).Should().Be(100);
    }

    [Theory]
    [InlineData(10, ChaosRules.LightPitItem)]
    [InlineData(40, ChaosRules.DrainRestoreItem)]
    [InlineData(90, ChaosRules.KillingBladeItem)]
    public void TheCubeGivesTheItemOfYourRankTier(int percent, int item) =>
        ChaosRules.CubeItem(percent).Should().Be(item);

    [Fact]
    public void RewardsFollowTheGuideTiers()
    {
        ChaosRules.Rewards(20).Should().Equal(ChaosRules.VoucherOfChaos, ChaosRules.BlueTreasureChest, ChaosRules.VoucherOfOracle);
        ChaosRules.Rewards(40).Should().Equal(ChaosRules.VoucherOfChaos, ChaosRules.GreenTreasureChest);
        ChaosRules.Rewards(80).Should().Equal(ChaosRules.VoucherOfChaos, ChaosRules.RedTreasureChest);
        ChaosRules.Rewards(100).Should().Equal(ChaosRules.VoucherOfChaos);
    }

    [Fact]
    public void ExpGrowsWithKillsAndIsCapped()
    {
        ChaosRules.Exp(60, 0, 3).Should().Be(0);
        ChaosRules.Exp(60, 2, 1).Should().Be((long)(60.0 * 60 * 60 * 0.15 * 9));
        ChaosRules.Exp(83, 500, 0).Should().Be(8_000_000);
        ChaosRules.PremiumExp(8_000_000, true).Should().Be(10_000_000);
        ChaosRules.PremiumExp(1000, false).Should().Be(0);
    }

    [Fact]
    public void ChaosSkillsWorkOnlyInZoneEightyFive()
    {
        ChaosRules.SkillAllowedIn(ChaosRules.Zone, ChaosRules.ChaosSkillTree).Should().BeTrue();
        ChaosRules.SkillAllowedIn(ChaosRules.Zone, 105).Should().BeFalse();
        ChaosRules.SkillAllowedIn((byte)ZoneId.DungeonDefence, ChaosRules.ChaosSkillTree).Should().BeFalse();
        ChaosRules.SkillAllowedIn((byte)ZoneId.DungeonDefence, ChaosRules.DungeonDefenseSkillTree).Should().BeTrue();
        ChaosRules.SkillAllowedIn((byte)ZoneId.DungeonDefence, 105).Should().BeFalse();
        ChaosRules.SkillAllowedIn((byte)ZoneId.Moradon, ChaosRules.ChaosSkillTree).Should().BeFalse();
        ChaosRules.SkillAllowedIn((byte)ZoneId.Moradon, 105).Should().BeTrue();
    }

    [Fact]
    public void ThePlateClockUsesStyleSevenSubNine()
    {
        var packet = InstanceDashboardPacketWriter.EventClock((byte)ChaosRules.IndunEvent, 1200);

        packet.ReadInt().Should().Be(0);
        packet.ReadByte().Should().Be(7);
        packet.ReadLong().Should().Be(0);
        packet.ReadUInt().Should().Be(9u);
        packet.ReadByte().Should().Be(24);
        packet.ReadUInt().Should().Be(1200u);
    }

    [Fact]
    public void ChaosSignUpsAndFinishCarryEventTwentyFour()
    {
        var count = EventPacketWriter.TempleSignUps(TempleEvent.Chaos, 3, 4);
        count.ReadByte();
        count.ReadUShort().Should().Be(ChaosRules.IndunEvent);

        var finish = EventPacketWriter.TempleEventFinish(ChaosRules.IndunEvent, 0, 20);
        finish.ReadByte();
        finish.ReadUShort().Should().Be(ChaosRules.IndunEvent);
        finish.ReadByte().Should().Be(0);
        finish.ReadUInt().Should().Be(20u);
    }

    [Fact]
    public async Task KillsInARoomCountAndTheRankingListsThem()
    {
        using var provider = CreateProvider(_ => { },
            gameData => gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData()));
        var sessions = provider.GetRequiredService<SessionManager>();
        var chaos = (ChaosDungeonService)provider.GetRequiredService<IChaosDungeonService>();
        var a = Player(sessions, 500, AccountNation.Karus);
        var b = Player(sessions, 600, AccountNation.Karus);

        await chaos.StartMatchesAsync([500, 600], TempleEventRules.ChaosDurationSeconds);

        a.ZoneId.Should().Be(ChaosRules.Zone);
        a.Room.Should().Be(b.Room);
        a.IsBlinking.Should().BeTrue();
        a.MaxHp.Should().Be(ChaosRules.MaxHp);

        await chaos.OnPlayerKilledAsync(b, a);
        var match = chaos.GetMatch(a.Room)!;
        match.Scores[500].Kills.Should().Be(1);
        match.Scores[600].Deaths.Should().Be(1);

        var ranking = chaos.BuildRanking(a, 3);
        ranking.ReadByte().Should().Be(3);
        ranking.ReadByte().Should().Be(2);
        ranking.ReadString().Should().Be(a.Name);
        ranking.ReadShort().Should().Be(1);
    }

    [Fact]
    public async Task TheEntryBlinkEndsInChaosNormal()
    {
        using var provider = CreateProvider(_ => { },
            gameData => gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData()));
        var sessions = provider.GetRequiredService<SessionManager>();
        var chaos = provider.GetRequiredService<IChaosDungeonService>();
        var sent = new List<Packet>();
        var a = Player(sessions, 700, AccountNation.Karus, sent);

        await chaos.StartMatchesAsync([700], TempleEventRules.ChaosDurationSeconds);
        LastAbnormal(a, sent).Should().Be((int)PlayerAppearanceState.Blinking);

        await provider.GetRequiredService<IRespawnBlinkService>()
            .ExpireAsync(a, DateTime.UtcNow.AddSeconds(ChaosRules.BattleLockSeconds + 1).Ticks);

        a.IsBlinking.Should().BeFalse();
        LastAbnormal(a, sent).Should().Be((int)PlayerAppearanceState.ChaosNormal);
    }

    [Fact]
    public async Task LeavingDuringTheBattleLockEndsTheBlinkForTheClient()
    {
        using var provider = CreateProvider(_ => { },
            gameData => gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData()));
        var sessions = provider.GetRequiredService<SessionManager>();
        var chaos = (ChaosDungeonService)provider.GetRequiredService<IChaosDungeonService>();
        var sent = new List<Packet>();
        var a = Player(sessions, 701, AccountNation.Karus, sent);

        await chaos.StartMatchesAsync([701], TempleEventRules.ChaosDurationSeconds);
        await chaos.OnPlayerLeavingZoneAsync(a, ChaosRules.Zone);

        a.IsBlinking.Should().BeFalse();
        LastAbnormal(a, sent).Should().Be((int)PlayerAppearanceState.Normal);
    }

    private static int? LastAbnormal(UserSession session, List<Packet> sent)
    {
        int? last = null;
        foreach (var packet in sent)
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_STATE_CHANGE) continue;
            packet.ResetOffset();
            if (packet.ReadInt() != session.CharacterId) continue;
            if (packet.ReadByte() != (byte)StateChangeType.Abnormal) continue;
            last = packet.ReadInt();
        }
        return last;
    }

    private static UserSession Player(SessionManager sessions, int id, AccountNation nation, List<Packet>? sent = null)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(sent == null ? Arg.Any<Packet>() : Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId: id, accountId: id + 1);
        session.Name = $"P{id}";
        session.Nation = nation;
        session.Level = 60;
        session.ZoneId = (byte)ZoneId.Moradon;
        session.X = 800;
        session.Z = 400;
        sessions.Regions.AddToRegion(session);
        return session;
    }
}
