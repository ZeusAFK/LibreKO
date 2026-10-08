using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class DrakiTowerTests : GameTestBase
{
    private readonly IGameDataService _gameData = Substitute.For<IGameDataService>();
    private readonly IMagicItemUsageService _itemUsage = Substitute.For<IMagicItemUsageService>();
    private readonly IItemGrantService _itemGrant = Substitute.For<IItemGrantService>();
    private readonly IMonsterAggressionPolicy _aggression = Substitute.For<IMonsterAggressionPolicy>();
    private readonly IZoneTransitionService _zoneTransition = Substitute.For<IZoneTransitionService>();
    private readonly SessionManager _sessionManager = new();
    private readonly InstanceRoomRegistry _rooms;
    private readonly DrakiTowerService _service;
    private readonly Dictionary<UserSession, List<Packet>> _sent = new();

    public DrakiTowerTests()
    {
        _rooms = new InstanceRoomRegistry(_sessionManager, NullLogger<InstanceRoomRegistry>.Instance);
        _service = new DrakiTowerService(
            _sessionManager,
            _gameData,
            _itemUsage,
            _itemGrant,
            _aggression,
            _zoneTransition,
            _rooms,
            NullLogger<DrakiTowerService>.Instance);

        _gameData.GetNpc(Arg.Any<int>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var id = callInfo.Arg<int>();
            var isMonster = callInfo.Arg<bool>();
            return new NpcData
            {
                Id = id,
                Name = $"NPC_{id}",
                IsMonster = isMonster,
                Hp = 1000,
                Ac = 100,
            };
        });
    }

    private UserSession CreatePlayer(byte level = 80, byte zone = (byte)ZoneId.ElMoradCamp1, short hp = 1000, ushort room = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = _sessionManager.CreateSession(client, characterId: 9000 + _sent.Count, accountId: 9500 + _sent.Count);
        var packets = new List<Packet>();
        _sent[session] = packets;
        client.SendPacket(Arg.Do<Packet>(packets.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        session.Level = level;
        session.ZoneId = zone;
        session.Hp = hp;
        session.MaxHp = 1000;
        session.Room = room;
        session.Nation = AccountNation.ElMorad;
        return session;
    }

    [Fact]
    public void DrakiTowerRules_CanEnterFrom_OnlyAllowsCastles()
    {
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.KarusCamp1).Should().BeTrue();
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.ElMoradCamp1).Should().BeTrue();
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.KarusCamp2).Should().BeTrue();
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.ElMoradCamp2).Should().BeTrue();

        DrakiTowerRules.CanEnterFrom((byte)ZoneId.Moradon).Should().BeFalse();
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.RonarkLand).Should().BeFalse();
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.Prison).Should().BeFalse();
        DrakiTowerRules.CanEnterFrom((byte)ZoneId.DrakiTower).Should().BeFalse();
    }

    [Fact]
    public void DrakiTowerRules_IsGateNpc_IdentifiesBreakGateNpcs()
    {
        DrakiTowerRules.IsGateNpc(25257).Should().BeTrue();
        DrakiTowerRules.IsGateNpc(25258).Should().BeTrue();
        DrakiTowerRules.IsGateNpc(25263).Should().BeTrue();
        DrakiTowerRules.IsGateNpc(25265).Should().BeTrue();

        DrakiTowerRules.IsGateNpc(25266).Should().BeFalse();
        DrakiTowerRules.IsGateNpc(25267).Should().BeFalse();
        DrakiTowerRules.IsGateNpc(13007).Should().BeFalse();
    }

    [Fact]
    public void DrakiTowerRules_Stages_LoadsStagesAndMonsters()
    {
        var stages = DrakiTowerRules.Stages;
        stages.Should().NotBeEmpty();
        stages.Count.Should().Be(41);

        var first = stages[0];
        first.Stage.Should().Be(1);
        first.SubStage.Should().Be(1);
        first.IsNpcBreak.Should().BeFalse();
        first.Spawns.Should().NotBeEmpty();
    }

    [Fact]
    public void EventPacketWriter_DrakiList_WritesExpectedBytes()
    {
        var packet = EventPacketWriter.DrakiList(
            topRanks: [],
            userRank: 255,
            userName: "Tester",
            userFinishTime: 3600,
            userStage: 1,
            userMaxStage: 1,
            userEntranceLimit: 3);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiList);
    }

    [Fact]
    public void EventPacketWriter_DrakiEnterResult_WritesCode()
    {
        var packet = EventPacketWriter.DrakiEnterResult(0);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        packet.ReadUInt().Should().Be(0u);
    }

    [Fact]
    public async Task HandleListAsync_SendsDrakiListPacket()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        await _service.HandleListAsync(session);

        _sent[session].Should().ContainSingle(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiList);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenDead_FailsWithCode9()
    {
        var session = CreatePlayer(hp: 0);

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await _service.HandleEnterAsync(session, packet);

        var sent = _sent[session].Single();
        sent.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        sent.ReadUInt().Should().Be(9u);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenInvalidZone_FailsWithCode1()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.Moradon);

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await _service.HandleEnterAsync(session, packet);

        var sent = _sent[session].Single();
        sent.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        sent.ReadUInt().Should().Be(1u);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenLimitZeroAndNoCertificate_FailsWithCode7()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 0;
        session.DrakiEntranceLimitResetDate = DateTime.UtcNow.Date;

        _itemUsage.CanUseItem(session, DrakiTowerRules.CertificateOfDrakiItem).Returns(false);

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await _service.HandleEnterAsync(session, packet);

        var sent = _sent[session].Single();
        sent.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        sent.ReadUInt().Should().Be(7u);
    }

    [Fact]
    public async Task HandleEnterAsync_SuccessfulEntry_OpensRoom_WarpsPlayer_AndStartsWave()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await _service.HandleEnterAsync(session, packet);

        session.DrakiEntranceLimit.Should().Be(2);
        session.Room.Should().NotBe(0);
        _rooms.Holds((ushort)session.Room, (byte)ZoneId.DrakiTower).Should().BeTrue();

        await _zoneTransition.Received(1).ChangeZoneAsync(
            session, (byte)ZoneId.DrakiTower, 40f, 451f);

        _sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData().Length >= 5
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiEnter
            && BitConverter.ToUInt32(p.GetData(), 1) == 0u);

        _sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiTimer);

        var room = _rooms.Get((ushort)session.Room);
        room.Should().NotBeNull();
        room!.Npcs.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OnNpcKilledAsync_WhenAllWaveMonstersKilled_AdvancesToNextSubStage()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        var room = _rooms.Get((ushort)session.Room)!;
        var initialMonsters = room.Npcs.ToList();

        foreach (var npc in initialMonsters)
        {
            await _service.OnNpcKilledAsync(npc);
        }

        session.DrakiSubStage.Should().Be(2);
    }

    [Fact]
    public async Task HandleTownAsync_LeavesRoom_SendsLeavePackets_AndReturnsPlayer()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.X = 1600f;
        session.Z = 446f;
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        session.ZoneId = (byte)ZoneId.DrakiTower;
        _sent[session].Clear();

        await _service.HandleTownAsync(session);

        session.Room.Should().Be(0);

        _sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiLeaveFirst);

        _sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiLeaveSecond);

        await _zoneTransition.Received(1).ChangeZoneAsync(
            session, (byte)ZoneId.ElMoradCamp1, 1600f, 446f);
    }
}
