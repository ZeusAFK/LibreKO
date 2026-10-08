using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed class DrakiRoomState
{
    public ushort RoomId { get; init; }
    public int CharacterId { get; init; }
    public byte CurrentStage { get; set; }
    public byte CurrentSubStage { get; set; }
    public int StageIndex { get; set; }
    public DateTime StartTime { get; init; }
    public DateTime SubStageStartTime { get; set; }
    public int RemainingMonsters { get; set; }
    public bool Completed { get; set; }
}

public interface IDrakiTowerService
{
    Task HandleListAsync(UserSession session);
    Task HandleEnterAsync(UserSession session, Packet packet);
    Task HandleTownAsync(UserSession session);
    Task OnNpcKilledAsync(NpcInstance npc);
    Task AdvanceFromGateNpcAsync(UserSession session, NpcInstance gateNpc);
}

public sealed class DrakiTowerService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IMagicItemUsageService itemUsage,
    IItemGrantService itemGrant,
    IMonsterAggressionPolicy aggression,
    IZoneTransitionService zoneTransition,
    InstanceRoomRegistry rooms,
    ILogger<DrakiTowerService> logger) : IDrakiTowerService
{
    private readonly ConcurrentDictionary<ushort, DrakiRoomState> _activeRooms = new();

    public async Task HandleListAsync(UserSession session)
    {
        EnsureDailyLimit(session);
        var userStage = Math.Max((byte)1, session.DrakiStage);
        var userMaxStage = Math.Max((byte)1, session.DrakiStage);

        var packet = EventPacketWriter.DrakiList(
            topRanks: [],
            userRank: 255,
            userName: session.Name,
            userFinishTime: 3600,
            userStage: userStage,
            userMaxStage: userMaxStage,
            userEntranceLimit: session.DrakiEntranceLimit);

        await session.Client.SendPacket(packet);
    }

    public async Task HandleEnterAsync(UserSession session, Packet packet)
    {
        EnsureDailyLimit(session);

        if (session.Hp <= 0)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(9));
            return;
        }

        if (session.Room != 0 || session.ZoneId == DrakiTowerRules.ZoneIdValue || session.IsWarping)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(2));
            return;
        }

        if (!DrakiTowerRules.CanEnterFrom(session.ZoneId))
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(1));
            return;
        }

        var itemId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var requestedStage = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)1;

        if (requestedStage is < 1 or > 5)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(1));
            return;
        }

        if (itemId != 0 && itemId != DrakiTowerRules.CertificateOfDrakiItem)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(5));
            return;
        }

        if (session.DrakiEntranceLimit <= 0)
        {
            if (!itemUsage.CanUseItem(session, DrakiTowerRules.CertificateOfDrakiItem)
                || !await itemUsage.TryConsumeItemAsync(session, DrakiTowerRules.CertificateOfDrakiItem))
            {
                await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(7));
                return;
            }
        }
        else
        {
            session.DrakiEntranceLimit--;
        }

        var stages = DrakiTowerRules.Stages;
        var stageIndex = stages.ToList().FindIndex(s => s.Stage == requestedStage && s.SubStage == 1 && !s.IsNpcBreak);
        if (stageIndex < 0)
        {
            logger.LogWarning("Draki stage {Stage} substage 1 not found in stages table", requestedStage);
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(6));
            return;
        }

        var duration = TimeSpan.FromMinutes(120);
        var room = rooms.Open(DrakiTowerRules.ZoneIdValue, requestedStage, duration, endsOnBossKill: false);
        rooms.Join(room, session);
        session.InstanceReturn = (session.ZoneId, session.X, session.Z);

        session.DrakiStage = requestedStage;
        session.DrakiSubStage = 1;

        var state = new DrakiRoomState
        {
            RoomId = room.Id,
            CharacterId = session.CharacterId,
            CurrentStage = requestedStage,
            CurrentSubStage = 1,
            StageIndex = stageIndex,
            StartTime = DateTime.UtcNow,
            SubStageStartTime = DateTime.UtcNow,
        };
        _activeRooms[room.Id] = state;

        var coord = DrakiTowerRules.StageCoordinates.TryGetValue(requestedStage, out var c) ? c : (X: 40f, Z: 451f);
        await zoneTransition.ChangeZoneAsync(session, DrakiTowerRules.ZoneIdValue, coord.X, coord.Z);
        await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(0));
        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(requestedStage, 1, DrakiTowerRules.WaveSeconds, 0));

        SpawnStageEntities(room, state, stages[stageIndex]);
    }

    public async Task HandleTownAsync(UserSession session)
    {
        if (session.ZoneId != DrakiTowerRules.ZoneIdValue)
            return;

        _activeRooms.TryRemove(session.Room, out var state);

        var elapsed = state != null ? (uint)(DateTime.UtcNow - state.StartTime).TotalSeconds : 0u;
        var stage = state?.CurrentStage ?? session.DrakiStage;
        var subStage = state?.CurrentSubStage ?? session.DrakiSubStage;

        await session.Client.SendPacket(EventPacketWriter.DrakiLeaveFirst());
        await session.Client.SendPacket(EventPacketWriter.DrakiLeaveSecond(stage, subStage, elapsed));

        var returnPoint = session.InstanceReturn ?? (session.Nation == AccountNation.ElMorad
            ? ((byte)ZoneId.ElMoradCamp1, 1600f, 446f)
            : ((byte)ZoneId.KarusCamp1, 459f, 1607f));

        rooms.Leave(session);
        await zoneTransition.ChangeZoneAsync(session, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc)
    {
        if (npc.ZoneId != DrakiTowerRules.ZoneIdValue || npc.Room == 0)
            return;

        if (!_activeRooms.TryGetValue(npc.Room, out var state))
            return;

        if (sessionManager.GetByCharacterId(state.CharacterId) is not { } session)
            return;

        if (state.RemainingMonsters > 0)
            state.RemainingMonsters--;

        if (state.RemainingMonsters == 0 && !state.Completed)
        {
            await AdvanceToNextStageAsync(session, state);
        }
    }

    public async Task AdvanceFromGateNpcAsync(UserSession session, NpcInstance gateNpc)
    {
        if (session.ZoneId != DrakiTowerRules.ZoneIdValue || session.Room == 0)
            return;

        if (!_activeRooms.TryGetValue(session.Room, out var state))
            return;

        if (rooms.Get(state.RoomId) is not { } room)
            return;

        ClearRoomNpcs(room);

        var stages = DrakiTowerRules.Stages;
        var nextIndex = state.StageIndex + 1;

        if (nextIndex >= stages.Count)
        {
            await CompleteTowerAsync(session, state);
            return;
        }

        var nextStage = stages[nextIndex];
        state.StageIndex = nextIndex;
        state.CurrentStage = nextStage.Stage;
        state.CurrentSubStage = nextStage.SubStage;
        state.SubStageStartTime = DateTime.UtcNow;

        session.DrakiStage = nextStage.Stage;
        session.DrakiSubStage = nextStage.SubStage;

        if (DrakiTowerRules.StageCoordinates.TryGetValue(nextStage.Stage, out var coord))
        {
            await zoneTransition.ChangeZoneAsync(session, DrakiTowerRules.ZoneIdValue, coord.X, coord.Z);
        }

        var elapsed = (int)(DateTime.UtcNow - state.SubStageStartTime).TotalSeconds;
        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
            nextStage.Stage, nextStage.SubStage, DrakiTowerRules.WaveSeconds, elapsed));

        SpawnStageEntities(room, state, nextStage);
    }

    private async Task AdvanceToNextStageAsync(UserSession session, DrakiRoomState state)
    {
        var stages = DrakiTowerRules.Stages;
        var nextIndex = state.StageIndex + 1;

        if (nextIndex >= stages.Count)
        {
            await CompleteTowerAsync(session, state);
            return;
        }

        var nextStage = stages[nextIndex];
        state.StageIndex = nextIndex;
        state.CurrentStage = nextStage.Stage;
        state.CurrentSubStage = nextStage.SubStage;
        state.SubStageStartTime = DateTime.UtcNow;

        session.DrakiStage = nextStage.Stage;
        session.DrakiSubStage = nextStage.SubStage;

        var elapsed = (int)(DateTime.UtcNow - state.SubStageStartTime).TotalSeconds;

        if (rooms.Get(state.RoomId) is not { } room)
            return;

        ClearRoomNpcs(room);

        if (nextStage.IsNpcBreak)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
                nextStage.Stage, nextStage.SubStage, DrakiTowerRules.BreakSeconds, elapsed));
            SpawnStageEntities(room, state, nextStage);
        }
        else
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
                nextStage.Stage, nextStage.SubStage, DrakiTowerRules.WaveSeconds, elapsed));
            SpawnStageEntities(room, state, nextStage);
        }
    }

    private async Task CompleteTowerAsync(UserSession session, DrakiRoomState state)
    {
        state.Completed = true;
        var elapsed = (int)(DateTime.UtcNow - state.StartTime).TotalSeconds;

        if (rooms.Get(state.RoomId) is { } room)
        {
            ClearRoomNpcs(room);
            SpawnNpcAt(room, DrakiTowerRules.FinalExitNpcId, 77, 214, isMonster: false);
        }

        var boxItemId = elapsed <= 1200
            ? DrakiTowerRules.SuperiorDrakiSupplyBoxItem
            : DrakiTowerRules.DrakiSupplyBoxItem;

        if (gameData.GetItem(boxItemId) is { } boxItem)
        {
            await itemGrant.GrantAsync(session, boxItem, 1);
        }

        await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
            (byte)session.Nation, "Congratulations! You have conquered Draki's Tower!"));
    }

    private void SpawnStageEntities(InstanceRoom room, DrakiRoomState state, DrakiStageInfo stageInfo)
    {
        var monsterCount = 0;
        foreach (var spawn in stageInfo.Spawns)
        {
            var npc = SpawnNpcAt(room, spawn.MonsterId, spawn.PosX, spawn.PosZ, spawn.IsMonster, spawn.Direction);
            if (npc != null && spawn.IsMonster)
                monsterCount++;
        }
        state.RemainingMonsters = monsterCount;
    }

    private NpcInstance? SpawnNpcAt(InstanceRoom room, int npcId, float x, float z, bool isMonster, short direction = 0)
    {
        var npcData = gameData.GetNpc(npcId, isMonster: isMonster);
        if (npcData == null)
        {
            logger.LogWarning("Draki entity {NpcId} (monster={IsMonster}) not found in game data", npcId, isMonster);
            return null;
        }

        var pos = new NpcPosData
        {
            ZoneId = DrakiTowerRules.ZoneIdValue,
            NpcId = npcId,
            LeftX = (int)x,
            TopZ = (int)z,
            Direction = direction,
            ActType = isMonster ? (byte)1 : (byte)100,
            NumNPC = 1,
            RegTime = 0,
            Room = (short)room.Id,
        };

        var npc = NpcInstance.FromData(npcData, pos, 0);
        npc.Room = room.Id;
        npc.RespawnType = NpcRespawnType.Never;
        var height = sessionManager.Maps?.GetHeight(npc.ZoneId, npc.X, npc.Z) ?? 0f;
        npc.Y = height;
        npc.SpawnY = height;

        if (isMonster)
            aggression.Apply(npc);

        sessionManager.Regions.SpawnNpc(npc);
        room.Npcs.Add(npc);
        return npc;
    }

    private void ClearRoomNpcs(InstanceRoom room)
    {
        foreach (var npc in room.Npcs.ToList())
        {
            sessionManager.Regions.RemoveNpc(npc);
        }
        room.Npcs.Clear();
    }

    private static void EnsureDailyLimit(UserSession session) =>
        DrakiTowerRules.EnsureDailyLimit(session);
}
