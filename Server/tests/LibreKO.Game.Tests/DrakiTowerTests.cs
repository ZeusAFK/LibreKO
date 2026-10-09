using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibreKO.Common.Domain.Entities;
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
    private readonly IDrakiStageProvider _stageProvider = new DrakiStageProvider();
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
            _stageProvider,
            NullLogger<DrakiTowerService>.Instance);

        _zoneTransition.ChangeZoneAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(callInfo =>
            {
                var s = callInfo.ArgAt<UserSession>(0);
                s.ZoneId = callInfo.ArgAt<byte>(1);
                s.X = callInfo.ArgAt<float>(2);
                s.Z = callInfo.ArgAt<float>(3);
                return Task.CompletedTask;
            });

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

        _gameData.GetItem(Arg.Any<int>()).Returns(callInfo =>
        {
            var id = callInfo.Arg<int>();
            return new ItemData
            {
                Num = id,
                Name = $"Item_{id}",
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
        session.DrakiStage = 1;
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
    public void DrakiStageProvider_LoadsStagesAndMonsters()
    {
        var stages = _stageProvider.Stages;
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
            userFinishTime: EventPacketWriter.DrakiDefaultFinishTime,
            userStage: 1,
            userMaxStage: 1,
            userEntranceLimit: 3);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiList);
    }

    [Fact]
    public void EventPacketWriter_DrakiEnterResult_WritesCode()
    {
        var packet = EventPacketWriter.DrakiEnterResult(DrakiEnterResult.Success);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        packet.ReadUInt().Should().Be((uint)DrakiEnterResult.Success);
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
        sent.ReadUInt().Should().Be((uint)DrakiEnterResult.Dead);
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
        sent.ReadUInt().Should().Be((uint)DrakiEnterResult.CannotEnter);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenStageNotYetUnlocked_FailsWithCannotEnter()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiStage = 1;

        // Player requests floor 5 without reaching it
        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(5);

        await _service.HandleEnterAsync(session, packet);

        var sent = _sent[session].Single();
        sent.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        sent.ReadUInt().Should().Be((uint)DrakiEnterResult.CannotEnter);
        session.DrakiEntranceLimit.Should().Be(3);
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
        sent.ReadUInt().Should().Be((uint)DrakiEnterResult.NoLimit);
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
            && BitConverter.ToUInt32(p.GetData(), 1) == (uint)DrakiEnterResult.Success);

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

    [Fact]
    public async Task FullRun_ClearsEveryStageInOrder_GrantsSupplyBox()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        var room = _rooms.Get((ushort)session.Room)!;
        var stages = _stageProvider.Stages;

        // Loop through all stages in order
        var iterations = 0;
        while (iterations++ < 100)
        {
            var state = _service.GetRoomState(room.Id)!;
            if (state.Completed)
                break;

            var currentStageInfo = stages[state.StageIndex];
            if (currentStageInfo.IsNpcBreak)
            {
                var gateNpc = room.Npcs.FirstOrDefault(n => DrakiTowerRules.IsGateNpc(n.NpcId))
                    ?? room.Npcs.First();
                await _service.AdvanceFromGateNpcAsync(session, gateNpc);
            }
            else
            {
                var monsters = room.Npcs.Where(n => n.IsMonster).ToList();
                foreach (var monster in monsters)
                {
                    await _service.OnNpcKilledAsync(monster);
                }
            }
        }

        var finalState = _service.GetRoomState(room.Id)!;
        finalState.Completed.Should().BeTrue();

        // Verify supply box is granted
        await _itemGrant.Received(1).GrantAsync(session, Arg.Is<ItemData>(i =>
            i.Num == DrakiTowerRules.SuperiorDrakiSupplyBoxItem || i.Num == DrakiTowerRules.DrakiSupplyBoxItem), 1);

        // Verify exit rift is present
        room.Npcs.Should().ContainSingle(n => n.NpcId == DrakiTowerRules.FinalExitNpcId);
    }

    [Fact]
    public async Task CheckTimeoutsAsync_WhenWaveTimesOut_ExpelsPlayer()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.X = 1600f;
        session.Z = 446f;
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        var state = _service.GetRoomState((ushort)session.Room)!;
        state.Deadline = DateTime.UtcNow.AddSeconds(-1);
        session.ZoneId = (byte)ZoneId.DrakiTower;

        await _service.CheckTimeoutsAsync();

        session.Room.Should().Be(0);
        _service.GetRoomState(state.RoomId).Should().BeNull();
    }

    [Fact]
    public async Task CheckTimeoutsAsync_WhenBreakTimesOut_AdvancesToNextWave()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        var room = _rooms.Get((ushort)session.Room)!;
        var stages = _stageProvider.Stages;

        // Clear until first break (after stage 1-3)
        while (true)
        {
            var state = _service.GetRoomState(room.Id)!;
            if (stages[state.StageIndex].IsNpcBreak)
                break;

            var monsters = room.Npcs.Where(n => n.IsMonster).ToList();
            foreach (var monster in monsters)
                await _service.OnNpcKilledAsync(monster);
        }

        var breakState = _service.GetRoomState(room.Id)!;
        breakState.IsBreak.Should().BeTrue();
        var breakStageIndex = breakState.StageIndex;

        // Set deadline to past
        breakState.Deadline = DateTime.UtcNow.AddSeconds(-1);
        await _service.CheckTimeoutsAsync();

        // Break ended and advanced to next wave!
        var afterState = _service.GetRoomState(room.Id)!;
        afterState.StageIndex.Should().Be(breakStageIndex + 1);
        afterState.IsBreak.Should().BeFalse();
    }

    [Fact]
    public async Task InstanceRoomClosed_DropsRoomState()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        var roomId = (ushort)session.Room;
        var room = _rooms.Get(roomId)!;
        _service.GetRoomState(roomId).Should().NotBeNull();

        _rooms.Close(room);

        _service.GetRoomState(roomId).Should().BeNull();
    }

    [Fact]
    public async Task OnNpcKilledAsync_ConcurrentKills_AdvancesWaveOnlyOnce()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await _service.HandleEnterAsync(session, enterPacket);

        var room = _rooms.Get((ushort)session.Room)!;
        var initialMonsters = room.Npcs.ToList();

        // Simulate concurrent kills
        var tasks = initialMonsters.Select(npc => Task.Run(() => _service.OnNpcKilledAsync(npc)));
        await Task.WhenAll(tasks);

        session.DrakiSubStage.Should().Be(2);
    }

    [Fact]
    public void DailyEntranceLimit_PersistsBetweenCharacterAndUserSession()
    {
        var character = new Character
        {
            Id = 1,
            Name = "Hero",
            DrakiStage = 2,
            DrakiSubStage = 1,
            DrakiEntranceLimit = 1,
            DrakiEntranceLimitResetDate = DateTime.UtcNow.Date,
        };

        var mapper = new UserSessionCharacterMapper();
        var session = CreatePlayer();
        var account = new Account { Id = 1, Nation = AccountNation.ElMorad };
        var warehouse = new Warehouse { AccountId = 1 };

        mapper.HydrateSession(session, character, account, warehouse, 1000, 1000, _gameData);

        session.DrakiEntranceLimit.Should().Be(1);
        session.DrakiEntranceLimitResetDate.Date.Should().Be(DateTime.UtcNow.Date);

        // Player uses an entry
        session.DrakiEntranceLimit--;

        var updatedCharacter = new Character();
        mapper.ApplyToCharacter(session, updatedCharacter);

        updatedCharacter.DrakiEntranceLimit.Should().Be(0);
        updatedCharacter.DrakiEntranceLimitResetDate.Should().Be(DateTime.UtcNow.Date);
    }
}
