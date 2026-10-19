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
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class DrakiTowerTests : GameTestBase
{
    private readonly IGameDataService gameData = Substitute.For<IGameDataService>();
    private readonly IMagicItemUsageService itemUsage = Substitute.For<IMagicItemUsageService>();
    private readonly IItemGrantService itemGrant = Substitute.For<IItemGrantService>();
    private readonly IMailService mailService = Substitute.For<IMailService>();
    private readonly IMonsterAggressionPolicy aggression = Substitute.For<IMonsterAggressionPolicy>();
    private readonly IZoneTransitionService zoneTransition = Substitute.For<IZoneTransitionService>();
    private readonly SessionManager sessionManager = new();
    private readonly InstanceRoomRegistry rooms;
    private readonly IDrakiStageProvider stageProvider = new DrakiStageProvider();
    private readonly DrakiTowerService service;
    private readonly Dictionary<UserSession, List<Packet>> sent = new();

    public DrakiTowerTests()
    {
        rooms = new InstanceRoomRegistry(sessionManager, NullLogger<InstanceRoomRegistry>.Instance);
        service = new DrakiTowerService(
            sessionManager,
            gameData,
            itemUsage,
            itemGrant,
            mailService,
            aggression,
            zoneTransition,
            rooms,
            stageProvider,
            NullLogger<DrakiTowerService>.Instance,
            startTimer: false);

        zoneTransition.ChangeZoneAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(callInfo =>
            {
                var s = callInfo.ArgAt<UserSession>(0);
                s.ZoneId = callInfo.ArgAt<byte>(1);
                s.X = callInfo.ArgAt<float>(2);
                s.Z = callInfo.ArgAt<float>(3);
                return Task.CompletedTask;
            });

        gameData.GetNpc(Arg.Any<int>(), Arg.Any<bool>()).Returns(callInfo =>
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

        gameData.GetItem(Arg.Any<int>()).Returns(callInfo =>
        {
            var id = callInfo.Arg<int>();
            return new ItemData
            {
                Num = id,
                Name = $"Item_{id}",
            };
        });

        itemUsage.CanUseItem(Arg.Any<UserSession>(), Arg.Any<int>()).Returns(true);
        itemUsage.TryConsumeItemAsync(Arg.Any<UserSession>(), Arg.Any<int>()).Returns(true);
        itemGrant.GrantAsync(Arg.Any<UserSession>(), Arg.Any<ItemData>(), Arg.Any<int>()).Returns(1);
    }

    private UserSession CreatePlayer(byte level = 80, byte zone = (byte)ZoneId.ElMoradCamp1, short hp = 1000, ushort room = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 9000 + sent.Count, accountId: 9500 + sent.Count);
        var packets = new List<Packet>();
        sent[session] = packets;
        client.SendPacket(Arg.Do<Packet>(packets.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        session.Level = level;
        session.ZoneId = zone;
        session.Hp = hp;
        session.MaxHp = 1000;
        session.Room = room;
        session.Nation = AccountNation.ElMorad;
        session.DrakiStage = 1;
        session.DrakiEntranceLimit = 3;
        session.DrakiEntranceLimitResetDate = DateTime.UtcNow.Date;
        return session;
    }

    [Fact]
    public void DrakiTowerRules_Constants_MatchSpecification()
    {
        DrakiTowerRules.ZoneIdValue.Should().Be(95);
        DrakiTowerRules.MinimumLevel.Should().Be(1);
        DrakiTowerRules.MaxDailyEntrances.Should().Be(3);
        DrakiTowerRules.CertificateOfDrakiItem.Should().Be(810595000);
        DrakiTowerRules.SupplyBoxItem.Should().Be(810596000);
        DrakiTowerRules.RoomDurationMinutes.Should().Be(120);
        DrakiTowerRules.WaveSeconds.Should().Be(ScriptCharacterService.DrakiRiftSeconds);
        DrakiTowerRules.BreakSeconds.Should().Be(180);
        DrakiTowerRules.DrakiRiftNpcId.Should().Be(25267);
        DrakiTowerRules.FinalExitNpcId.Should().Be(25266);
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
    public void DrakiTowerRules_EnsureDailyLimit_ResetsOnNewDay()
    {
        var session = CreatePlayer();
        session.DrakiEntranceLimit = 0;
        session.DrakiEntranceLimitResetDate = DateTime.UtcNow.Date.AddDays(-1);

        DrakiTowerRules.EnsureDailyLimit(session);

        session.DrakiEntranceLimit.Should().Be(3);
        session.DrakiEntranceLimitResetDate.Date.Should().Be(DateTime.UtcNow.Date);
    }

    [Fact]
    public void DrakiTowerRules_EnsureDailyLimit_PreservesSameDay()
    {
        var session = CreatePlayer();
        session.DrakiEntranceLimit = 1;
        session.DrakiEntranceLimitResetDate = DateTime.UtcNow.Date;

        DrakiTowerRules.EnsureDailyLimit(session);

        session.DrakiEntranceLimit.Should().Be(1);
    }

    [Fact]
    public void DrakiStageProvider_LoadsStagesAndMonsters()
    {
        var stages = stageProvider.Stages;
        stages.Should().NotBeEmpty();
        stages.Count.Should().Be(41);

        var first = stages[0];
        first.Stage.Should().Be(1);
        first.SubStage.Should().Be(1);
        first.IsNpcBreak.Should().BeFalse();
        first.Spawns.Should().NotBeEmpty();

        var firstBreak = stages.First(s => s.IsNpcBreak);
        firstBreak.Stage.Should().Be(1);
        firstBreak.SubStage.Should().Be(3);
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
        var packet = EventPacketWriter.DrakiEnterResult(DrakiEnterResult.InvalidItem);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        packet.ReadByte().Should().Be((byte)DrakiEnterResult.InvalidItem);
    }

    [Fact]
    public void EventPacketWriter_DrakiLeaveFirst_SendsOnlySubOpcode()
    {
        var packet = EventPacketWriter.DrakiLeaveFirst();
        packet.GetData().Length.Should().Be(1);
        packet.GetData()[0].Should().Be((byte)TempleSubOpcode.DrakiLeaveFirst);
    }

    [Fact]
    public void EventPacketWriter_DrakiLeaveSecond_ButtonCode()
    {
        var clearPacket = EventPacketWriter.DrakiLeaveSecond(5, 8, 300, completed: true);
        var giveUpPacket = EventPacketWriter.DrakiLeaveSecond(1, 2, 50, completed: false);

        clearPacket.GetData().Last().Should().Be(0);
        giveUpPacket.GetData().Last().Should().Be(1);
    }

    [Fact]
    public async Task HandleListAsync_SendsDrakiListPacket()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        await service.HandleListAsync(session);

        sent[session].Should().ContainSingle(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiList);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenInvalidZone_FailsWithCode1()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.Moradon);

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await service.HandleEnterAsync(session, packet);

        var reply = sent[session].Single();
        reply.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        reply.ReadByte().Should().Be((byte)DrakiEnterResult.WrongZone);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenStageNotYetUnlocked_FailsWithFloorNotReached()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiStage = 1;

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(5);

        await service.HandleEnterAsync(session, packet);

        var reply = sent[session].Single();
        reply.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        reply.ReadByte().Should().Be((byte)DrakiEnterResult.FloorNotReached);
        session.DrakiEntranceLimit.Should().Be(3);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenLimitZeroAndNoCertificate_FailsWithCode5()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 0;
        session.DrakiEntranceLimitResetDate = DateTime.UtcNow.Date;

        itemUsage.CanUseItem(session, DrakiTowerRules.CertificateOfDrakiItem).Returns(false);

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await service.HandleEnterAsync(session, packet);

        var reply = sent[session].Single();
        reply.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiEnter);
        reply.ReadByte().Should().Be((byte)DrakiEnterResult.NoLimit);
    }

    [Fact]
    public async Task HandleEnterAsync_WhenLimitZeroAndHasCertificate_ConsumesCertificate810595000()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 0;
        session.DrakiEntranceLimitResetDate = DateTime.UtcNow.Date;

        itemUsage.CanUseItem(session, DrakiTowerRules.CertificateOfDrakiItem).Returns(true);
        itemUsage.TryConsumeItemAsync(session, DrakiTowerRules.CertificateOfDrakiItem).Returns(true);

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(DrakiTowerRules.CertificateOfDrakiItem);
        packet.WriteByte(1);

        await service.HandleEnterAsync(session, packet);

        await itemUsage.Received(1).TryConsumeItemAsync(session, 810595000);
        session.Room.Should().NotBe(0);
    }

    [Fact]
    public async Task HandleEnterAsync_DoesNotOverwriteFurthestStage()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiStage = 3;
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        session.DrakiStage.Should().Be(3);
    }

    [Fact]
    public async Task AReplayOfAnEarlierFloorKeepsTheBestFloorAndWave()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiStage = 3;
        session.DrakiSubStage = 5;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        var room = rooms.Get((ushort)session.Room)!;
        foreach (var monster in room.Npcs.Where(n => n.IsMonster).ToList())
            await service.OnNpcKilledAsync(monster);

        service.GetRoomState(room.Id)!.CurrentSubStage.Should().Be(2);
        session.DrakiStage.Should().Be(3);
        session.DrakiSubStage.Should().Be(5);
    }

    [Fact]
    public async Task HandleEnterAsync_SuccessfulEntry_OpensRoom_WarpsPlayer_AndStartsWave()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var packet = new Packet((byte)GameOpcodes.GS_EVENT);
        packet.WriteInt(0);
        packet.WriteByte(1);

        await service.HandleEnterAsync(session, packet);

        session.DrakiEntranceLimit.Should().Be(2);
        session.Room.Should().NotBe(0);
        rooms.Holds((ushort)session.Room, (byte)ZoneId.DrakiTower).Should().BeTrue();

        await zoneTransition.Received(1).ChangeZoneAsync(
            session, (byte)ZoneId.DrakiTower, 40f, 451f);

        sent[session].Should().NotContain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData().Length > 0
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiEnter);

        sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiTimer);

        var room = rooms.Get((ushort)session.Room);
        room.Should().NotBeNull();
        room!.Npcs.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AdvanceFromGateNpcAsync_IgnoresClickIfNotInBreak()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        var state = service.GetRoomState((ushort)session.Room)!;
        state.IsBreak.Should().BeFalse();

        var gateNpc = new NpcInstance { NpcId = 25257, Room = (ushort)session.Room };
        await service.AdvanceFromGateNpcAsync(session, gateNpc);

        state.StageIndex.Should().Be(0);
        state.IsBreak.Should().BeFalse();
    }

    [Fact]
    public async Task HandleTownAsync_LeavesRoom_SendsPackets_AndWarpsToReturnPoint()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.X = 1600f;
        session.Z = 446f;
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        session.ZoneId = (byte)ZoneId.DrakiTower;
        sent[session].Clear();

        await service.HandleTownAsync(session);

        session.Room.Should().Be(0);

        sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiLeaveFirst);

        sent[session].Should().Contain(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_EVENT
            && p.GetData()[0] == (byte)TempleSubOpcode.DrakiLeaveSecond);

        await zoneTransition.Received(1).ChangeZoneAsync(
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
        await service.HandleEnterAsync(session, enterPacket);

        var room = rooms.Get((ushort)session.Room)!;
        var stages = stageProvider.Stages;

        var iterations = 0;
        while (iterations++ < 100)
        {
            var state = service.GetRoomState(room.Id)!;
            if (state.Completed)
                break;

            var currentStageInfo = stages[state.StageIndex];
            if (currentStageInfo.IsNpcBreak)
            {
                var gateNpc = room.Npcs.FirstOrDefault(n => DrakiTowerRules.IsGateNpc(n.NpcId))
                    ?? room.Npcs.First();
                await service.AdvanceFromGateNpcAsync(session, gateNpc);
            }
            else
            {
                var monsters = room.Npcs.Where(n => n.IsMonster).ToList();
                foreach (var monster in monsters)
                {
                    await service.OnNpcKilledAsync(monster);
                }
            }
        }

        var finalState = service.GetRoomState(room.Id)!;
        finalState.Completed.Should().BeTrue();

        await itemGrant.Received(1).GrantAsync(session, Arg.Is<ItemData>(i => i.Num == DrakiTowerRules.SupplyBoxItem), 1);

        room.Npcs.Should().ContainSingle(n => n.NpcId == DrakiTowerRules.FinalExitNpcId);
    }

    [Fact]
    public async Task FullRun_WhenInventoryFull_SendsSupplyBoxViaSystemMail()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var boxItem = new ItemData { Num = DrakiTowerRules.SupplyBoxItem, Name = "Superior Draki Supply Box" };
        gameData.GetItem(DrakiTowerRules.SupplyBoxItem).Returns(boxItem);
        itemGrant.GrantAsync(session, boxItem, 1).Returns(0);

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        var room = rooms.Get((ushort)session.Room)!;
        var stages = stageProvider.Stages;

        var iterations = 0;
        while (iterations++ < 100)
        {
            var state = service.GetRoomState(room.Id)!;
            if (state.Completed)
                break;

            var currentStageInfo = stages[state.StageIndex];
            if (currentStageInfo.IsNpcBreak)
            {
                var gateNpc = room.Npcs.FirstOrDefault(n => DrakiTowerRules.IsGateNpc(n.NpcId)) ?? room.Npcs.First();
                await service.AdvanceFromGateNpcAsync(session, gateNpc);
            }
            else
            {
                var monsters = room.Npcs.Where(n => n.IsMonster).ToList();
                foreach (var monster in monsters)
                    await service.OnNpcKilledAsync(monster);
            }
        }

        var finalState = service.GetRoomState(room.Id)!;
        finalState.Completed.Should().BeTrue();

        await mailService.Received(1).SendSystemMailAsync(
            session.CharacterId,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<MailAttachmentDraft>>(att => att.Any(a => a.ItemId == DrakiTowerRules.SupplyBoxItem)),
            Arg.Any<MailKind>());
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
        await service.HandleEnterAsync(session, enterPacket);

        var state = service.GetRoomState((ushort)session.Room)!;
        state.Deadline = DateTime.UtcNow.AddSeconds(-1);
        session.ZoneId = (byte)ZoneId.DrakiTower;

        await service.CheckTimeoutsAsync();

        session.Room.Should().Be(0);
        service.GetRoomState(state.RoomId).Should().BeNull();
    }

    [Fact]
    public async Task CheckTimeoutsAsync_WhenCompletedRunTimesOut_ExpelsPlayer()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.X = 1600f;
        session.Z = 446f;
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        var state = service.GetRoomState((ushort)session.Room)!;
        state.Completed = true;
        state.IsBreak = true;
        state.Deadline = DateTime.UtcNow.AddSeconds(-1);
        session.ZoneId = (byte)ZoneId.DrakiTower;

        await service.CheckTimeoutsAsync();

        session.Room.Should().Be(0);
        service.GetRoomState(state.RoomId).Should().BeNull();
    }

    [Fact]
    public async Task CheckTimeoutsAsync_WhenBreakTimesOut_AdvancesToNextWave()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        var room = rooms.Get((ushort)session.Room)!;
        var stages = stageProvider.Stages;

        while (true)
        {
            var state = service.GetRoomState(room.Id)!;
            if (stages[state.StageIndex].IsNpcBreak)
                break;

            var monsters = room.Npcs.Where(n => n.IsMonster).ToList();
            foreach (var monster in monsters)
                await service.OnNpcKilledAsync(monster);
        }

        var breakState = service.GetRoomState(room.Id)!;
        breakState.IsBreak.Should().BeTrue();
        var breakStageIndex = breakState.StageIndex;

        breakState.Deadline = DateTime.UtcNow.AddSeconds(-1);
        await service.CheckTimeoutsAsync();

        var afterState = service.GetRoomState(room.Id)!;
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
        await service.HandleEnterAsync(session, enterPacket);

        var roomId = (ushort)session.Room;
        var room = rooms.Get(roomId)!;
        service.GetRoomState(roomId).Should().NotBeNull();

        rooms.Close(room);

        service.GetRoomState(roomId).Should().BeNull();
    }

    [Fact]
    public async Task OnNpcKilledAsync_ConcurrentKills_AdvancesWaveOnlyOnce()
    {
        var session = CreatePlayer(zone: (byte)ZoneId.ElMoradCamp1);
        session.DrakiEntranceLimit = 3;

        var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
        enterPacket.WriteInt(0);
        enterPacket.WriteByte(1);
        await service.HandleEnterAsync(session, enterPacket);

        var room = rooms.Get((ushort)session.Room)!;
        var initialMonsters = room.Npcs.ToList();

        var tasks = initialMonsters.Select(npc => Task.Run(() => service.OnNpcKilledAsync(npc)));
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

        mapper.HydrateSession(session, character, account, warehouse, 1000, 1000, gameData);

        session.DrakiEntranceLimit.Should().Be(1);
        session.DrakiEntranceLimitResetDate.Date.Should().Be(DateTime.UtcNow.Date);

        session.DrakiEntranceLimit--;

        var updatedCharacter = new Character();
        mapper.ApplyToCharacter(session, updatedCharacter);

        updatedCharacter.DrakiEntranceLimit.Should().Be(0);
        updatedCharacter.DrakiEntranceLimitResetDate.Should().Be(DateTime.UtcNow.Date);
    }
}
