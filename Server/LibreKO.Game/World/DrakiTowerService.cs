using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    public DateTime Deadline { get; set; }
    public bool IsBreak { get; set; }
    public int RemainingMonsters;
    public bool Completed { get; set; }
}

public interface IDrakiTowerService
{
    Task HandleListAsync(UserSession session);
    Task HandleEnterAsync(UserSession session, Packet packet);
    Task HandleTownAsync(UserSession session);
    Task OnNpcKilledAsync(NpcInstance npc);
    Task AdvanceFromGateNpcAsync(UserSession session, NpcInstance gateNpc);
    Task CheckTimeoutsAsync();
    void DropRoomState(ushort roomId);
    DrakiRoomState? GetRoomState(ushort roomId);
}

public sealed class DrakiTowerService : IDrakiTowerService
{
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameData;
    private readonly IMagicItemUsageService _itemUsage;
    private readonly IItemGrantService _itemGrant;
    private readonly IMonsterAggressionPolicy _aggression;
    private readonly IZoneTransitionService _zoneTransition;
    private readonly InstanceRoomRegistry _rooms;
    private readonly IDrakiStageProvider _stageProvider;
    private readonly ILogger<DrakiTowerService> _logger;
    private readonly ConcurrentDictionary<ushort, DrakiRoomState> _activeRooms = new();

    public DrakiTowerService(
        SessionManager sessionManager,
        IGameDataService gameData,
        IMagicItemUsageService itemUsage,
        IItemGrantService itemGrant,
        IMonsterAggressionPolicy aggression,
        IZoneTransitionService zoneTransition,
        InstanceRoomRegistry rooms,
        IDrakiStageProvider stageProvider,
        ILogger<DrakiTowerService> logger)
    {
        _sessionManager = sessionManager;
        _gameData = gameData;
        _itemUsage = itemUsage;
        _itemGrant = itemGrant;
        _aggression = aggression;
        _zoneTransition = zoneTransition;
        _rooms = rooms;
        _stageProvider = stageProvider;
        _logger = logger;

        _rooms.RoomClosed += room => DropRoomState(room.Id);
    }

    public void DropRoomState(ushort roomId) => _activeRooms.TryRemove(roomId, out _);

    public DrakiRoomState? GetRoomState(ushort roomId) => _activeRooms.GetValueOrDefault(roomId);

    public async Task HandleListAsync(UserSession session)
    {
        EnsureDailyLimit(session);
        var userStage = Math.Max(UserSession.DrakiStageMin, session.DrakiStage);
        var userMaxStage = Math.Max(UserSession.DrakiStageMin, session.DrakiStage);

        var packet = EventPacketWriter.DrakiList(
            topRanks: [],
            userRank: 255,
            userName: session.Name,
            userFinishTime: EventPacketWriter.DrakiDefaultFinishTime,
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
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.Dead));
            return;
        }

        if (session.Room != 0 || session.ZoneId == DrakiTowerRules.ZoneIdValue || session.IsWarping)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.AlreadyInEvent));
            return;
        }

        if (session.Level < DrakiTowerRules.MinimumLevel)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.CannotEnter));
            return;
        }

        if (!DrakiTowerRules.CanEnterFrom(session.ZoneId))
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.CannotEnter));
            return;
        }

        var itemId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var requestedStage = packet.RemainingBytes >= 1 ? packet.ReadByte() : UserSession.DrakiStageMin;

        var maxAllowedStage = Math.Max(UserSession.DrakiStageMin, session.DrakiStage);
        if (requestedStage < UserSession.DrakiStageMin || requestedStage > UserSession.DrakiStageMax || requestedStage > maxAllowedStage)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.CannotEnter));
            return;
        }

        if (itemId != 0 && itemId != DrakiTowerRules.CertificateOfDrakiItem)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.InvalidItem));
            return;
        }

        if (session.DrakiEntranceLimit <= 0 && !_itemUsage.CanUseItem(session, DrakiTowerRules.CertificateOfDrakiItem))
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.NoLimit));
            return;
        }

        var stages = _stageProvider.Stages;
        var stageIndex = stages.ToList().FindIndex(s => s.Stage == requestedStage && s.SubStage == 1 && !s.IsNpcBreak);
        if (stageIndex < 0)
        {
            _logger.LogWarning("Draki stage {Stage} substage 1 not found in stages table", requestedStage);
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.Failed));
            return;
        }

        if (session.DrakiEntranceLimit <= 0)
        {
            if (!await _itemUsage.TryConsumeItemAsync(session, DrakiTowerRules.CertificateOfDrakiItem))
            {
                await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.NoLimit));
                return;
            }
        }
        else
        {
            session.DrakiEntranceLimit--;
        }

        var room = _rooms.Open(DrakiTowerRules.ZoneIdValue, requestedStage, DrakiTowerRules.RoomDuration, endsOnBossKill: false);
        _rooms.Join(room, session);
        session.InstanceReturn = (session.ZoneId, session.X, session.Z);

        session.DrakiStage = requestedStage;
        session.DrakiSubStage = 1;

        var now = DateTime.UtcNow;
        var state = new DrakiRoomState
        {
            RoomId = room.Id,
            CharacterId = session.CharacterId,
            CurrentStage = requestedStage,
            CurrentSubStage = 1,
            StageIndex = stageIndex,
            StartTime = now,
            SubStageStartTime = now,
            Deadline = now.AddSeconds(DrakiTowerRules.WaveSeconds),
            IsBreak = false,
        };
        _activeRooms[room.Id] = state;

        var coord = DrakiTowerRules.StageCoordinates.TryGetValue(requestedStage, out var c) ? c : (X: 40f, Z: 451f);
        await _zoneTransition.ChangeZoneAsync(session, DrakiTowerRules.ZoneIdValue, coord.X, coord.Z);
        await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.Success));
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

        _rooms.Leave(session);
        await _zoneTransition.ChangeZoneAsync(session, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc)
    {
        if (npc.ZoneId != DrakiTowerRules.ZoneIdValue || npc.Room == 0)
            return;

        if (!_activeRooms.TryGetValue(npc.Room, out var state))
            return;

        if (_sessionManager.GetByCharacterId(state.CharacterId) is not { } session)
            return;

        if (Interlocked.Decrement(ref state.RemainingMonsters) == 0 && !state.Completed)
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

        await AdvanceFromBreakAsync(session, state);
    }

    public async Task CheckTimeoutsAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _activeRooms.ToArray())
        {
            var state = kvp.Value;
            if (state.Completed)
                continue;

            if (now >= state.Deadline)
            {
                if (_sessionManager.GetByCharacterId(state.CharacterId) is not { } session)
                    continue;

                if (state.IsBreak)
                {
                    var stages = _stageProvider.Stages;
                    if (state.StageIndex + 1 >= stages.Count || stages[state.StageIndex].Spawns.Any(s => s.MonsterId == DrakiTowerRules.FinalExitNpcId))
                    {
                        await HandleTownAsync(session);
                    }
                    else
                    {
                        await AdvanceFromBreakAsync(session, state);
                    }
                }
                else
                {
                    await HandleTownAsync(session);
                }
            }
        }
    }

    private async Task AdvanceFromBreakAsync(UserSession session, DrakiRoomState state)
    {
        if (_rooms.Get(state.RoomId) is not { } room)
            return;

        ClearRoomNpcs(room);

        var stages = _stageProvider.Stages;
        var nextIndex = state.StageIndex + 1;

        if (nextIndex >= stages.Count || stages[nextIndex].Spawns.Any(s => s.MonsterId == DrakiTowerRules.FinalExitNpcId))
        {
            await CompleteTowerAsync(session, state);
            return;
        }

        var nextStage = stages[nextIndex];
        state.StageIndex = nextIndex;
        state.CurrentStage = nextStage.Stage;
        state.CurrentSubStage = nextStage.SubStage;
        state.SubStageStartTime = DateTime.UtcNow;
        state.IsBreak = nextStage.IsNpcBreak;
        state.Deadline = DateTime.UtcNow.AddSeconds(nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds);

        session.DrakiStage = nextStage.Stage;
        session.DrakiSubStage = nextStage.SubStage;

        if (DrakiTowerRules.StageCoordinates.TryGetValue(nextStage.Stage, out var coord))
        {
            await _zoneTransition.ChangeZoneAsync(session, DrakiTowerRules.ZoneIdValue, coord.X, coord.Z);
        }

        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
            nextStage.Stage, nextStage.SubStage, nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds, 0));

        SpawnStageEntities(room, state, nextStage);
    }

    private async Task AdvanceToNextStageAsync(UserSession session, DrakiRoomState state)
    {
        var stages = _stageProvider.Stages;
        var nextIndex = state.StageIndex + 1;

        if (nextIndex >= stages.Count
            || stages[nextIndex].Spawns.Any(s => s.MonsterId == DrakiTowerRules.FinalExitNpcId)
            || (stages[nextIndex].Stage == 5 && stages[nextIndex].SubStage == 8 && stages[nextIndex].IsNpcBreak))
        {
            if (nextIndex < stages.Count)
                state.StageIndex = nextIndex;
            await CompleteTowerAsync(session, state);
            return;
        }

        var nextStage = stages[nextIndex];
        state.StageIndex = nextIndex;
        state.CurrentStage = nextStage.Stage;
        state.CurrentSubStage = nextStage.SubStage;
        state.SubStageStartTime = DateTime.UtcNow;
        state.IsBreak = nextStage.IsNpcBreak;
        state.Deadline = DateTime.UtcNow.AddSeconds(nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds);

        session.DrakiStage = nextStage.Stage;
        session.DrakiSubStage = nextStage.SubStage;

        if (_rooms.Get(state.RoomId) is not { } room)
            return;

        ClearRoomNpcs(room);

        var duration = nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds;
        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
            nextStage.Stage, nextStage.SubStage, duration, 0));
        SpawnStageEntities(room, state, nextStage);
    }

    private async Task CompleteTowerAsync(UserSession session, DrakiRoomState state)
    {
        state.Completed = true;
        state.IsBreak = true;
        state.Deadline = DateTime.UtcNow.AddSeconds(DrakiTowerRules.BreakSeconds);
        var elapsed = (int)(DateTime.UtcNow - state.StartTime).TotalSeconds;

        if (_rooms.Get(state.RoomId) is { } room)
        {
            ClearRoomNpcs(room);
            SpawnNpcAt(room, DrakiTowerRules.FinalExitNpcId, DrakiTowerRules.FinalExitCoordinates.X, DrakiTowerRules.FinalExitCoordinates.Z, isMonster: false);
        }

        var boxItemId = elapsed <= DrakiTowerRules.SuperiorBoxThresholdSeconds
            ? DrakiTowerRules.SuperiorDrakiSupplyBoxItem
            : DrakiTowerRules.DrakiSupplyBoxItem;

        if (_gameData.GetItem(boxItemId) is { } boxItem)
        {
            await _itemGrant.GrantAsync(session, boxItem, 1);
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
        Volatile.Write(ref state.RemainingMonsters, monsterCount);
    }

    private NpcInstance? SpawnNpcAt(InstanceRoom room, int npcId, float x, float z, bool isMonster, short direction = 0)
    {
        var npcData = _gameData.GetNpc(npcId, isMonster: isMonster);
        if (npcData == null)
        {
            _logger.LogWarning("Draki entity {NpcId} (monster={IsMonster}) not found in game data", npcId, isMonster);
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
        var height = _sessionManager.Maps?.GetHeight(npc.ZoneId, npc.X, npc.Z) ?? 0f;
        npc.Y = height;
        npc.SpawnY = height;

        if (isMonster)
            _aggression.Apply(npc);

        _sessionManager.Regions.SpawnNpc(npc);
        room.Npcs.Add(npc);
        return npc;
    }

    private void ClearRoomNpcs(InstanceRoom room)
    {
        foreach (var npc in room.Npcs.ToList())
        {
            _sessionManager.Regions.RemoveNpc(npc);
        }
        room.Npcs.Clear();
    }

    private static void EnsureDailyLimit(UserSession session) =>
        DrakiTowerRules.EnsureDailyLimit(session);
}
