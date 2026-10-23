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
    public DateTime Deadline { get; set; }
    public bool IsBreak { get; set; }
    public int RemainingMonsters;
    public bool Completed { get; set; }
    public SemaphoreSlim GateLock { get; } = new(1, 1);
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

public sealed class DrakiTowerService : IDrakiTowerService, IDisposable
{
    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameData;
    private readonly IMagicItemUsageService itemUsage;
    private readonly IItemGrantService itemGrant;
    private readonly IMailService mailService;
    private readonly IMonsterAggressionPolicy aggression;
    private readonly IZoneTransitionService zoneTransition;
    private readonly INpcLifecycleService npcLifecycle;
    private readonly InstanceRoomRegistry rooms;
    private readonly IDrakiStageProvider stageProvider;
    private readonly ILogger<DrakiTowerService> logger;
    private readonly ConcurrentDictionary<ushort, DrakiRoomState> activeRooms = new();
    private readonly Timer? timeoutTimer;
    private int checkingTimeouts;

    public DrakiTowerService(
        SessionManager sessionManager,
        IGameDataService gameData,
        IMagicItemUsageService itemUsage,
        IItemGrantService itemGrant,
        IMailService mailService,
        IMonsterAggressionPolicy aggression,
        IZoneTransitionService zoneTransition,
        INpcLifecycleService npcLifecycle,
        InstanceRoomRegistry rooms,
        IDrakiStageProvider stageProvider,
        ILogger<DrakiTowerService> logger,
        bool startTimer = true)
    {
        this.sessionManager = sessionManager;
        this.gameData = gameData;
        this.itemUsage = itemUsage;
        this.itemGrant = itemGrant;
        this.mailService = mailService;
        this.aggression = aggression;
        this.zoneTransition = zoneTransition;
        this.npcLifecycle = npcLifecycle;
        this.rooms = rooms;
        this.stageProvider = stageProvider;
        this.logger = logger;

        this.rooms.RoomClosed += room => _ = OnRoomClosedAsync(room);

        if (startTimer)
        {
            timeoutTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private void OnTimerTick(object? _)
    {
        if (Interlocked.CompareExchange(ref checkingTimeouts, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await CheckTimeoutsAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking Draki tower timeouts");
            }
            finally
            {
                Volatile.Write(ref checkingTimeouts, 0);
            }
        });
    }

    private async Task OnRoomClosedAsync(InstanceRoom room)
    {
        if (room.ZoneId != DrakiTowerRules.ZoneIdValue)
        {
            DropRoomState(room.Id);
            return;
        }

        if (activeRooms.TryRemove(room.Id, out var state))
        {
            if (sessionManager.GetByCharacterId(state.CharacterId) is { } session)
            {
                try
                {
                    var elapsed = (uint)(DateTime.UtcNow - state.StartTime).TotalSeconds;
                    await session.Client.SendPacket(EventPacketWriter.DrakiLeaveFirst());
                    await session.Client.SendPacket(EventPacketWriter.DrakiLeaveSecond(state.CurrentStage, state.CurrentSubStage, elapsed, state.Completed));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not send Draki leave packets for character {CharacterId} on room close", state.CharacterId);
                }
            }
        }
    }

    public void DropRoomState(ushort roomId) => activeRooms.TryRemove(roomId, out _);

    public DrakiRoomState? GetRoomState(ushort roomId) => activeRooms.GetValueOrDefault(roomId);

    public async Task HandleListAsync(UserSession session)
    {
        EnsureDailyLimit(session);
        var userStage = Math.Max(UserSession.DrakiStageMin, session.DrakiStage);
        var userMaxStage = Math.Max(UserSession.DrakiStageMin, session.DrakiStage);

        var packet = EventPacketWriter.DrakiList(
            topRanks: [],
            userRank: DrakiTowerRules.UnrankedUser,
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

        if (session.Room != 0 || session.ZoneId == DrakiTowerRules.ZoneIdValue)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.AlreadyInInstance));
            return;
        }

        if (session.IsWarping || session.IsBusy || !NpcPresence.IsAt(sessionManager, session, DrakiTowerRules.DrakiRiftNpcId))
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.CannotEnterNow));
            return;
        }

        if (session.Level < DrakiTowerRules.MinimumLevel)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.LevelTooLow));
            return;
        }

        if (!DrakiTowerRules.CanEnterFrom(session.ZoneId))
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.WrongZone));
            return;
        }

        var itemId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var requestedStage = packet.RemainingBytes >= 1 ? packet.ReadByte() : UserSession.DrakiStageMin;

        var maxAllowedStage = Math.Max(UserSession.DrakiStageMin, session.DrakiStage);
        if (requestedStage < UserSession.DrakiStageMin || requestedStage > UserSession.DrakiStageMax || requestedStage > maxAllowedStage)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.FloorNotReached));
            return;
        }

        if (itemId != 0 && itemId != DrakiTowerRules.CertificateOfDrakiItem)
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.InvalidItem));
            return;
        }

        if (session.DrakiEntranceLimit <= 0 && !itemUsage.CanUseItem(session, DrakiTowerRules.CertificateOfDrakiItem))
        {
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.NoLimit));
            return;
        }

        var stages = stageProvider.Stages;
        var stageIndex = stages.ToList().FindIndex(s => s.Stage == requestedStage && s.SubStage == 1 && !s.IsNpcBreak);
        if (stageIndex < 0)
        {
            logger.LogWarning("Draki stage {Stage} substage 1 not found in stages table", requestedStage);
            await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.RoomCreationFailed));
            return;
        }

        if (session.DrakiEntranceLimit <= 0)
        {
            if (!await itemUsage.TryConsumeItemAsync(session, DrakiTowerRules.CertificateOfDrakiItem))
            {
                await session.Client.SendPacket(EventPacketWriter.DrakiEnterResult(DrakiEnterResult.NoLimit));
                return;
            }
        }
        else
        {
            session.DrakiEntranceLimit--;
        }

        var room = rooms.Open(DrakiTowerRules.ZoneIdValue, requestedStage, DrakiTowerRules.RoomDuration, endsOnBossKill: false);
        rooms.Join(room, session);
        session.InstanceReturn = (session.ZoneId, session.X, session.Z);

        var now = DateTime.UtcNow;
        var state = new DrakiRoomState
        {
            RoomId = room.Id,
            CharacterId = session.CharacterId,
            CurrentStage = requestedStage,
            CurrentSubStage = 1,
            StageIndex = stageIndex,
            StartTime = now,
            Deadline = now.AddSeconds(DrakiTowerRules.WaveSeconds),
            IsBreak = false,
        };
        activeRooms[room.Id] = state;

        var coord = DrakiTowerRules.StageCoordinates.TryGetValue(requestedStage, out var c)
            ? c
            : DrakiTowerRules.StageCoordinates[UserSession.DrakiStageMin];
        await zoneTransition.ChangeZoneAsync(session, DrakiTowerRules.ZoneIdValue, coord.X, coord.Z);
        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(requestedStage, 1, DrakiTowerRules.WaveSeconds, 0));

        await SpawnStageEntitiesAsync(room, state, stages[stageIndex]);
    }

    public async Task HandleTownAsync(UserSession session)
    {
        if (session.ZoneId != DrakiTowerRules.ZoneIdValue)
            return;

        if (!activeRooms.TryGetValue(session.Room, out var held))
        {
            await LeaveTowerAsync(session);
            return;
        }

        await held.GateLock.WaitAsync();
        try
        {
            await LeaveTowerAsync(session);
        }
        finally
        {
            held.GateLock.Release();
        }
    }

    private async Task LeaveTowerAsync(UserSession session)
    {
        if (session.ZoneId != DrakiTowerRules.ZoneIdValue)
            return;

        activeRooms.TryRemove(session.Room, out var state);

        var elapsed = state != null ? (uint)(DateTime.UtcNow - state.StartTime).TotalSeconds : 0u;
        var stage = state?.CurrentStage ?? session.DrakiStage;
        var subStage = state?.CurrentSubStage ?? session.DrakiSubStage;
        var completed = state?.Completed ?? false;

        await session.Client.SendPacket(EventPacketWriter.DrakiLeaveFirst());
        await session.Client.SendPacket(EventPacketWriter.DrakiLeaveSecond(stage, subStage, elapsed, completed));

        var returnPoint = session.InstanceReturn ?? (session.Nation == AccountNation.ElMorad
            ? DrakiTowerRules.ElMoradReturn
            : DrakiTowerRules.KarusReturn);

        rooms.Leave(session);
        await zoneTransition.ChangeZoneAsync(session, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc)
    {
        if (npc.ZoneId != DrakiTowerRules.ZoneIdValue || npc.Room == 0)
            return;

        if (!activeRooms.TryGetValue(npc.Room, out var state))
            return;

        if (sessionManager.GetByCharacterId(state.CharacterId) is not { } session)
            return;

        if (Interlocked.Decrement(ref state.RemainingMonsters) == 0 && !state.Completed)
        {
            await state.GateLock.WaitAsync();
            try
            {
                if (!state.Completed)
                {
                    await AdvanceToNextStageAsync(session, state);
                }
            }
            finally
            {
                state.GateLock.Release();
            }
        }
    }

    public async Task AdvanceFromGateNpcAsync(UserSession session, NpcInstance gateNpc)
    {
        if (session.ZoneId != DrakiTowerRules.ZoneIdValue || session.Room == 0)
            return;

        if (!activeRooms.TryGetValue(session.Room, out var state))
            return;

        if (!state.IsBreak || state.Completed)
            return;

        if (!await state.GateLock.WaitAsync(0))
            return;

        try
        {
            if (!state.IsBreak || state.Completed)
                return;

            await AdvanceFromBreakAsync(session, state);
        }
        finally
        {
            state.GateLock.Release();
        }
    }

    public async Task CheckTimeoutsAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in activeRooms.ToArray())
        {
            var state = kvp.Value;

            if (now >= state.Deadline)
            {
                if (sessionManager.GetByCharacterId(state.CharacterId) is not { } session)
                    continue;

                if (state.Completed)
                {
                    await HandleTownAsync(session);
                    continue;
                }

                if (state.IsBreak)
                {
                    var stages = stageProvider.Stages;
                    if (state.StageIndex + 1 >= stages.Count || stages[state.StageIndex].Spawns.Any(s => s.MonsterId == DrakiTowerRules.FinalExitNpcId))
                    {
                        await HandleTownAsync(session);
                    }
                    else
                    {
                        if (!await state.GateLock.WaitAsync(0))
                            continue;

                        try
                        {
                            if (state.IsBreak && !state.Completed && now >= state.Deadline)
                            {
                                await AdvanceFromBreakAsync(session, state);
                            }
                        }
                        finally
                        {
                            state.GateLock.Release();
                        }
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
        if (!IsActive(state) || rooms.Get(state.RoomId) is not { } room)
            return;

        ClearRoomNpcs(room);

        var stages = stageProvider.Stages;
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
        state.IsBreak = nextStage.IsNpcBreak;
        state.Deadline = DateTime.UtcNow.AddSeconds(nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds);
        RaiseBest(session, nextStage.Stage, nextStage.SubStage);

        if (DrakiTowerRules.StageCoordinates.TryGetValue(nextStage.Stage, out var coord))
        {
            await zoneTransition.ChangeZoneAsync(session, DrakiTowerRules.ZoneIdValue, coord.X, coord.Z);
        }

        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
            nextStage.Stage, nextStage.SubStage, nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds, 0));

        await SpawnStageEntitiesAsync(room, state, nextStage);
    }

    private async Task AdvanceToNextStageAsync(UserSession session, DrakiRoomState state)
    {
        if (!IsActive(state))
            return;

        var stages = stageProvider.Stages;
        var nextIndex = state.StageIndex + 1;

        if (nextIndex >= stages.Count
            || stages[nextIndex].Spawns.Any(s => s.MonsterId == DrakiTowerRules.FinalExitNpcId))
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
        state.IsBreak = nextStage.IsNpcBreak;
        state.Deadline = DateTime.UtcNow.AddSeconds(nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds);
        RaiseBest(session, nextStage.Stage, nextStage.SubStage);

        if (rooms.Get(state.RoomId) is not { } room)
            return;

        ClearRoomNpcs(room);

        var duration = nextStage.IsNpcBreak ? DrakiTowerRules.BreakSeconds : DrakiTowerRules.WaveSeconds;
        await session.Client.SendPacket(EventPacketWriter.DrakiTimer(
            nextStage.Stage, nextStage.SubStage, duration, 0));
        await SpawnStageEntitiesAsync(room, state, nextStage);
    }

    private async Task CompleteTowerAsync(UserSession session, DrakiRoomState state)
    {
        state.Completed = true;
        state.IsBreak = true;
        state.Deadline = DateTime.UtcNow.AddSeconds(DrakiTowerRules.BreakSeconds);
        RaiseBest(session, UserSession.DrakiStageMax, state.CurrentSubStage);

        if (rooms.Get(state.RoomId) is { } room)
        {
            ClearRoomNpcs(room);
            await SpawnNpcAtAsync(room, DrakiTowerRules.FinalExitNpcId, DrakiTowerRules.FinalExitCoordinates.X, DrakiTowerRules.FinalExitCoordinates.Z, isMonster: false);
        }

        var boxItemId = DrakiTowerRules.SupplyBoxItem;

        if (gameData.GetItem(boxItemId) is { } boxItem)
        {
            var placed = await itemGrant.GrantAsync(session, boxItem, 1);
            if (placed < 1)
            {
                var attachments = new[]
                {
                    new MailAttachmentDraft(MailAttachmentKind.Item, boxItemId, 1, boxItem.Duration)
                };
                await mailService.SendSystemMailAsync(
                    session.CharacterId,
                    "Draki's Tower Reward",
                    "Your inventory was full when completing Draki's Tower. Your reward is attached to this mail.",
                    attachments);
            }
        }

        await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
            (byte)session.Nation, "Congratulations! You have conquered Draki's Tower!"));
    }

    private async Task SpawnStageEntitiesAsync(InstanceRoom room, DrakiRoomState state, DrakiStageInfo stageInfo)
    {
        var monsterCount = 0;
        foreach (var spawn in stageInfo.Spawns)
        {
            var npc = await SpawnNpcAtAsync(room, spawn.MonsterId, spawn.PosX, spawn.PosZ, spawn.IsMonster, spawn.Direction);
            if (npc != null && spawn.IsMonster)
                monsterCount++;
        }
        Volatile.Write(ref state.RemainingMonsters, monsterCount);
    }

    private async Task<NpcInstance?> SpawnNpcAtAsync(InstanceRoom room, int npcId, float x, float z, bool isMonster, short direction = 0)
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
            ActType = isMonster ? DrakiTowerRules.MonsterActType : DrakiTowerRules.NpcActType,
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

        await npcLifecycle.SpawnAsync(npc);
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

    private bool IsActive(DrakiRoomState state) =>
        activeRooms.TryGetValue(state.RoomId, out var current) && ReferenceEquals(current, state);

    private static void RaiseBest(UserSession session, byte stage, byte subStage)
    {
        if (stage < session.DrakiStage || (stage == session.DrakiStage && subStage <= session.DrakiSubStage))
            return;

        session.DrakiStage = stage;
        session.DrakiSubStage = subStage;
    }

    private static void EnsureDailyLimit(UserSession session) =>
        DrakiTowerRules.EnsureDailyLimit(session);

    public void Dispose()
    {
        timeoutTimer?.Dispose();
        activeRooms.Clear();
    }
}
