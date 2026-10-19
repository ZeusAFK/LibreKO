using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class SiegeTowerTests
{
    private const byte Delos = (byte)ZoneId.Delos;
    private const short Clan = 40;
    private const short OtherClan = 50;
    private const float TowerX = 500f;
    private const float TowerZ = 780f;

    private readonly SessionManager sessions = new();
    private readonly IWorldPacketCoordinator world = Substitute.For<IWorldPacketCoordinator>();
    private readonly IMagicStatusEffectService magic = Substitute.For<IMagicStatusEffectService>();
    private readonly SiegeTowerService towers;

    public SiegeTowerTests()
    {
        magic.TransformAsync(Arg.Any<UserSession>(), Arg.Any<int>()).Returns(true);
        towers = new SiegeTowerService(sessions, world, magic, Substitute.For<IZoneTransitionService>());
    }

    private UserSession Player(int id, short clan, float x = TowerX, float z = TowerZ)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id + 1000);
        session.Name = $"Player{id}";
        session.Nation = AccountNation.Karus;
        session.ZoneId = Delos;
        session.KnightsId = clan;
        session.X = x;
        session.Z = z;
        session.MaxHp = 100;
        session.Hp = 100;
        sessions.Regions.AddToRegion(session);
        return session;
    }

    private UserSession MovingTower(int id, short clan)
    {
        var tower = Player(id, clan);
        tower.TransformId = SiegeTowerRules.MovingTowerLook;
        return tower;
    }

    private NpcInstance GuardTower() =>
        sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 32701, Name = "Karus Guard tower", NpcType = SiegeTowerRules.GuardTowerNpcType, Nation = EntityNation.Karus,
            ZoneId = Delos, X = TowerX, Z = TowerZ, Hp = 1000, MaxHp = 1000,
        });

    private static Packet Request(byte sub, params object[] parts)
    {
        var packet = new Packet(GameOpcodes.GS_SIEGE_TOWER);
        packet.WriteByte(sub);
        foreach (var part in parts)
        {
            if (part is int i) packet.WriteInt(i);
            else if (part is ushort u) packet.WriteUShort(u);
        }
        packet.ResetOffset();
        return packet;
    }

    private static Packet LastSent(UserSession session)
    {
        var packet = session.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Last();
        packet.ResetOffset();
        return packet;
    }

    private static (byte Sub, byte Result) Head(Packet packet) => (packet.ReadByte(), packet.ReadByte());

    [Fact]
    public async Task AClanMateBoardsTheMovingTowerAndIsWarpedOntoIt()
    {
        var tower = MovingTower(1, Clan);
        var rider = Player(2, Clan, TowerX + 3f);

        await towers.HandleAsync(rider.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));

        var reply = LastSent(rider);
        Head(reply).Should().Be((SiegeTowerPacketWriter.SubBoard, SiegeTowerPacketWriter.Succeeded));
        reply.ReadUShort().Should().Be((ushort)(TowerX * SiegeTowerRules.WireScale));
        reply.ReadUShort().Should().Be((ushort)(TowerZ * SiegeTowerRules.WireScale));
        rider.TowerHostId.Should().Be(tower.CharacterId);
        await world.Received(1).WarpAsync(rider, (ushort)(TowerX * SiegeTowerRules.WireScale), (ushort)(TowerZ * SiegeTowerRules.WireScale));
    }

    [Fact]
    public async Task BoardingIsRefusedWithThe2619Codes()
    {
        var tower = MovingTower(1, Clan);
        var plain = Player(2, Clan);
        var far = Player(3, Clan, TowerX + SiegeTowerRules.BoardReach + 1f);
        var stranger = Player(4, OtherClan);

        await towers.HandleAsync(far.Client, Request(SiegeTowerPacketWriter.SubBoard, plain.CharacterId));
        Head(LastSent(far)).Result.Should().Be(SiegeTowerPacketWriter.BoardNotTower);

        await towers.HandleAsync(far.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));
        Head(LastSent(far)).Result.Should().Be(SiegeTowerPacketWriter.BoardTooFar);

        await towers.HandleAsync(far.Client, Request(SiegeTowerPacketWriter.SubBoard, 999));
        Head(LastSent(far)).Result.Should().Be(SiegeTowerPacketWriter.BoardNoUser);

        await towers.HandleAsync(stranger.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));
        Head(LastSent(stranger)).Result.Should().Be(SiegeTowerPacketWriter.Failed);

        await towers.HandleAsync(plain.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));
        await towers.HandleAsync(plain.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));
        Head(LastSent(plain)).Result.Should().Be(SiegeTowerPacketWriter.BoardAlreadyOnboard);
    }

    [Fact]
    public async Task LeavingNeedsToBeOnboardAndADropPointBesideTheTower()
    {
        var tower = MovingTower(1, Clan);
        var rider = Player(2, Clan);
        ushort Wire(float v) => (ushort)(v * SiegeTowerRules.WireScale);

        await towers.HandleAsync(rider.Client, Request(SiegeTowerPacketWriter.SubLeave, Wire(TowerX), Wire(TowerZ)));
        Head(LastSent(rider)).Result.Should().Be(SiegeTowerPacketWriter.LeaveNotOnboard);

        await towers.HandleAsync(rider.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));
        await towers.HandleAsync(rider.Client, Request(SiegeTowerPacketWriter.SubLeave, Wire(TowerX + 20f), Wire(TowerZ)));
        Head(LastSent(rider)).Result.Should().Be(SiegeTowerPacketWriter.LeaveTooFar);

        await towers.HandleAsync(rider.Client, Request(SiegeTowerPacketWriter.SubLeave, Wire(TowerX + 2f), Wire(TowerZ)));
        Head(LastSent(rider)).Result.Should().Be(SiegeTowerPacketWriter.Succeeded);
        rider.TowerHostId.Should().Be(0);
        await world.Received(1).WarpAsync(rider, Wire(TowerX + 2f), Wire(TowerZ));
    }

    [Fact]
    public async Task UsingAGuardTowerHidesItTransformsThePlayerAndLocksItForOthers()
    {
        var npc = GuardTower();
        var gunner = Player(1, Clan);
        var second = Player(2, Clan);

        await towers.HandleAsync(gunner.Client, Request(SiegeTowerPacketWriter.SubUse, npc.UniqueId));

        var reply = LastSent(gunner);
        Head(reply).Should().Be((SiegeTowerPacketWriter.SubUse, SiegeTowerPacketWriter.Succeeded));
        reply.ReadInt().Should().Be(gunner.CharacterId);
        reply.ReadInt().Should().Be(npc.UniqueId);
        npc.TowerUserId.Should().Be(gunner.CharacterId);
        gunner.GuardTowerNpcId.Should().Be(npc.UniqueId);
        await magic.Received(1).TransformAsync(gunner, SiegeTowerRules.GuardTowerSkill);

        await towers.HandleAsync(second.Client, Request(SiegeTowerPacketWriter.SubUse, npc.UniqueId));
        Head(LastSent(second)).Result.Should().Be(SiegeTowerPacketWriter.UseInUse);
    }

    [Fact]
    public async Task AnEnemyGuardTowerIsNotAvailable()
    {
        var npc = GuardTower();
        var enemy = Player(1, Clan);
        enemy.Nation = AccountNation.ElMorad;

        await towers.HandleAsync(enemy.Client, Request(SiegeTowerPacketWriter.SubUse, npc.UniqueId));

        Head(LastSent(enemy)).Result.Should().Be(SiegeTowerPacketWriter.UseNotAvailable);
        npc.TowerUserId.Should().Be(0);
    }

    [Fact]
    public async Task ExitingCancelsTheTransformationAndFreesTheTower()
    {
        var npc = GuardTower();
        var gunner = Player(1, Clan);
        await towers.HandleAsync(gunner.Client, Request(SiegeTowerPacketWriter.SubUse, npc.UniqueId));

        await towers.HandleAsync(gunner.Client, Request(SiegeTowerPacketWriter.SubExit));

        await magic.Received(1).CancelAsync(gunner, SiegeTowerRules.GuardTowerSkill);
        var reply = LastSent(gunner);
        Head(reply).Should().Be((SiegeTowerPacketWriter.SubExit, SiegeTowerPacketWriter.Succeeded));
        reply.ReadInt().Should().Be(gunner.CharacterId);
        npc.TowerUserId.Should().Be(0);
        gunner.GuardTowerNpcId.Should().Be(0);
    }

    [Fact]
    public async Task TheMovingTowerEndingDismissesItsRiders()
    {
        var tower = MovingTower(1, Clan);
        var rider = Player(2, Clan);
        await towers.HandleAsync(rider.Client, Request(SiegeTowerPacketWriter.SubBoard, tower.CharacterId));

        magic.TransformationEnded += Raise.Event<Func<UserSession, int, Task>>(tower, SiegeTowerRules.MovingTowerSkill);

        Head(LastSent(rider)).Should().Be((SiegeTowerPacketWriter.SubEnded, SiegeTowerPacketWriter.EndedDismissed));
        rider.TowerHostId.Should().Be(0);
    }

    [Fact]
    public void SiegeLooksAreTheSevenRetailForms()
    {
        SiegeTowerRules.InSiegeLook(SiegeTowerRules.MovingTowerLook).Should().BeTrue();
        SiegeTowerRules.InSiegeLook(30061).Should().BeTrue();
        SiegeTowerRules.InSiegeLook(30020).Should().BeFalse();
    }
}
