using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class NereidMonumentTests
{
    private const byte Nereid = BattleZoneManager.ZONE_BATTLE4;
    private const byte Karus = (byte)AccountNation.Karus;
    private const byte ElMorad = (byte)AccountNation.ElMorad;

    private readonly SessionManager sessions = new();
    private readonly IZoneTransitionService zones = Substitute.For<IZoneTransitionService>();
    private readonly NereidMonumentService monuments;

    public NereidMonumentTests() => monuments = new NereidMonumentService(sessions, zones);

    private UserSession Player(int id, AccountNation nation, byte zone = Nereid)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id + 1000);
        session.Nation = nation;
        session.ZoneId = zone;
        return session;
    }

    private static List<Packet> Sent(UserSession session) =>
        session.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call =>
            {
                var packet = (Packet)call.GetArguments()[0]!;
                packet.ResetOffset();
                return packet;
            })
            .ToList();

    private static NpcInstance Monument(short trap) =>
        new() { NpcId = 29466, NpcType = NpcData.TypeBattleMonument, ZoneId = Nereid, TrapNumber = trap };

    [Theory]
    [InlineData(5, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(7, 4)]
    [InlineData(6, 5)]
    [InlineData(1, 6)]
    public void TrapsMapToThe2619FlagOrder(short trap, int index) =>
        BattleZoneManager.MonumentIndexOfTrap(trap).Should().Be(index);

    [Fact]
    public void OnlyARealChangeScoresAndHoldingAllSevenAddsTheSweep()
    {
        var battle = new BattleZoneManager();
        battle.CaptureMonumentAt(0, Karus).Should().BeTrue();
        battle.CaptureMonumentAt(0, Karus).Should().BeFalse();
        battle.KarusMonumentPoint.Should().Be(BattleZoneManager.MonumentCapturePoints);

        for (int i = 1; i < BattleZoneManager.MonumentCount; i++)
            battle.CaptureMonumentAt(i, Karus);
        battle.KarusMonuments.Should().Be(BattleZoneManager.MonumentCount);
        battle.KarusMonumentPoint.Should().Be(BattleZoneManager.MonumentCount * BattleZoneManager.MonumentCapturePoints
            + BattleZoneManager.MonumentSweepPoints);

        battle.CaptureMonumentAt(3, ElMorad).Should().BeTrue();
        battle.KarusMonuments.Should().Be(BattleZoneManager.MonumentCount - 1);
        battle.ElmoMonuments.Should().Be(1);
    }

    [Fact]
    public void TheOwnerListIsACountThenOneByteEach()
    {
        var packet = MapEventPacketWriter.Owners([0, 1, 2, 0, 0, 0, 1]);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(MapEventPacketWriter.SubOwners);
        packet.ReadByte().Should().Be(7);
        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
    }

    [Fact]
    public async Task AMonumentKilledInTheWarIsCapturedAndTheZoneIsTold()
    {
        sessions.Battle.OpenBattleZone(BattleZoneManager.BATTLEZONE_OPEN, Nereid);
        var killer = Player(1, AccountNation.ElMorad);
        var watcher = Player(2, AccountNation.Karus);
        var elsewhere = Player(3, AccountNation.Karus, (byte)ZoneId.Moradon);

        await monuments.OnNpcKilledAsync(Monument(4), killer);

        sessions.Battle.MonumentOwners[3].Should().Be(ElMorad);
        var captured = Sent(watcher)[0];
        captured.ReadByte().Should().Be(MapEventPacketWriter.SubCaptured);
        captured.ReadByte().Should().Be(MapEventPacketWriter.CapturedOne);
        captured.ReadByte().Should().Be(3);
        captured.ReadByte().Should().Be(ElMorad);
        var points = Sent(watcher)[1];
        points.ReadByte().Should().Be(MapEventPacketWriter.SubPoints);
        points.ReadUShort().Should().Be(0);
        points.ReadUShort().Should().Be(BattleZoneManager.MonumentCapturePoints);
        Sent(elsewhere).Should().BeEmpty();
    }

    [Fact]
    public async Task OutsideTheWarAKillChangesNothing()
    {
        var killer = Player(1, AccountNation.ElMorad);

        await monuments.OnNpcKilledAsync(Monument(4), killer);

        sessions.Battle.MonumentOwners.Should().OnlyContain(owner => owner == 0);
        Sent(killer).Should().BeEmpty();
    }

    [Fact]
    public async Task EnteringNereidSendsTheOwnersThenThePoints()
    {
        var arriving = Player(1, AccountNation.Karus);

        zones.ZoneAbilitySent += Raise.Event<Func<UserSession, Task>>(arriving);

        var sent = Sent(arriving);
        sent[0].ReadByte().Should().Be(MapEventPacketWriter.SubOwners);
        sent[1].ReadByte().Should().Be(MapEventPacketWriter.SubPoints);
    }
}
