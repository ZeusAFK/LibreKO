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

public interface IFullMoonRiftService
{
    Task<bool> HandlePartyEntryAsync(UserSession session);
    Task OnNpcKilledAsync(NpcInstance npc, UserSession? killer);
    Task CheckTimeoutsAsync();
    void DropRoomState(ushort roomId);
    FullMoonRiftRoomState? GetRoomState(ushort roomId);
}

public sealed class FullMoonRiftService : IFullMoonRiftService, IDisposable
{
    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameData;
    private readonly IMagicItemUsageService itemUsage;
    private readonly IItemGrantService itemGrant;
    private readonly IMonsterAggressionPolicy aggression;
    private readonly IZoneTransitionService zoneTransition;
    private readonly InstanceRoomRegistry rooms;
    private readonly IFullMoonRiftStageProvider stageProvider;
    private readonly IUserNotificationService userNotification;
    private readonly ILogger<FullMoonRiftService> logger;
    private readonly ConcurrentDictionary<ushort, FullMoonRiftRoomState> activeRooms = new();
    private readonly Timer? timer;
    private int checkingTimeouts;

    public FullMoonRiftService(
        SessionManager sessionManager,
        IGameDataService gameData,
        IMagicItemUsageService itemUsage,
        IItemGrantService itemGrant,
        IMonsterAggressionPolicy aggression,
        IZoneTransitionService zoneTransition,
        InstanceRoomRegistry rooms,
        IFullMoonRiftStageProvider stageProvider,
        IUserNotificationService userNotification,
        ILogger<FullMoonRiftService> logger,
        bool startTimer = true)
    {
        this.sessionManager = sessionManager;
        this.gameData = gameData;
        this.itemUsage = itemUsage;
        this.itemGrant = itemGrant;
        this.aggression = aggression;
        this.zoneTransition = zoneTransition;
        this.rooms = rooms;
        this.stageProvider = stageProvider;
        this.userNotification = userNotification;
        this.logger = logger;

        this.rooms.RoomClosed += room => _ = OnRoomClosedAsync(room);

        if (startTimer)
        {
            timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
                logger.LogError(ex, "Error checking Full Moon Rift timeouts");
            }
            finally
            {
                Interlocked.Exchange(ref checkingTimeouts, 0);
            }
        });
    }

    public async Task<bool> HandlePartyEntryAsync(UserSession session)
    {
        var party = session.IsInParty ? sessionManager.Parties.GetParty(session.PartyIndex) : null;
        if (party == null)
        {
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.NoParty));
            return false;
        }

        if (party.LeaderId != (short)session.CharacterId)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "Only the party leader can initiate entry into the Full Moon Rift."));
            return false;
        }

        if (party.BoardType != FullMoonRules.FullMoonBoard)
        {
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.NotFullMoonParty));
            return false;
        }

        var members = party.MemberIds.Where(id => id >= 0)
            .Select(id => sessionManager.GetByCharacterId(id))
            .OfType<UserSession>().ToList();

        var underLeveled = members.Where(m => m.Level < FullMoonRules.MinEntryLevel).ToList();
        if (underLeveled.Count > 0)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.LevelTooLowTalkId]));
            return false;
        }

        var enteredToday = members.Where(m => FullMoonRules.HasEnteredToday(m)).ToList();
        if (enteredToday.Count > 0)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.AlreadyEnteredTodayTalkId]));
            return false;
        }

        var notInMoradon = members.Where(m => !FullMoonRules.InMoradon(m.ZoneId)).Select(m => m.Name).ToList();
        if (notInMoradon.Count > 0)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"Cannot enter: {string.Join(", ", notInMoradon)} must be in Moradon."));
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.NotAllInMoradon));
            return false;
        }

        var deadMembers = members.Where(m => m.Hp <= 0).Select(m => m.Name).ToList();
        if (deadMembers.Count > 0)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"Cannot enter: {string.Join(", ", deadMembers)} is dead."));
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.MemberDead));
            return false;
        }

        var tiedUp = members.Where(m => !m.MayBeMovedByOthers).Select(m => m.Name).ToList();
        if (tiedUp.Count > 0)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"Cannot enter: {string.Join(", ", tiedUp)} cannot leave right now."));
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.InstanceFailed));
            return false;
        }

        var missingVouchers = members.Where(m => !FullMoonRules.HasVoucher(m)).Select(m => m.Name).ToList();
        if (missingVouchers.Count > 0)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"Cannot enter: {string.Join(", ", missingVouchers)} does not have a Full Moon Rift Voucher."));
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.MemberWithoutVoucher));
            return false;
        }

        // Only members whose voucher was spent go in.
        var entryTimestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entering = new List<UserSession>();
        foreach (var member in members)
        {
            if (!await itemUsage.TryConsumeItemAsync(member, FullMoonRules.VoucherItem))
                continue;
            member.DailyOps[(int)DailyOperation.FullMoonRift] = entryTimestamp;
            entering.Add(member);
        }

        if (entering.Count == 0)
        {
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.MemberWithoutVoucher));
            return false;
        }

        members = entering;
        var difficulty = FullMoonRules.DifficultyForPartySize(members.Count);
        var startStageId = FullMoonRules.StartingStageForDifficulty(difficulty);

        var duration = TimeSpan.FromMinutes(FullMoonRules.RoomDurationMinutes);
        var room = rooms.Open((byte)ZoneId.DungeonDefence, (short)difficulty, duration, endsOnBossKill: false);

        var now = DateTime.UtcNow;
        var state = new FullMoonRiftRoomState
        {
            RoomId = room.Id,
            Difficulty = difficulty,
            CurrentStageId = startStageId,
            StartTime = now,
            NextSpawnTime = now.AddSeconds(FullMoonRules.InitialSpawnDelaySeconds),
            Deadline = now + duration,
            WaveActive = false,
            RemainingTowers = FullMoonRules.InitialTowers.Length
        };

        foreach (var member in members)
        {
            rooms.Join(room, member);
            state.Participants[member.CharacterId] = 0;
            member.InstanceReturn = (member.ZoneId, member.X, member.Z);
            await zoneTransition.ChangeZoneAsync(member, (byte)ZoneId.DungeonDefence, FullMoonRules.SpawnX, FullMoonRules.SpawnZ);
            await member.Client.SendPacket(BifrostPacketWriter.InstanceTimer((ushort)duration.TotalSeconds));
        }

        SpawnInitialTowers(room);

        activeRooms[room.Id] = state;
        logger.LogInformation("Full Moon Rift room {Room} opened by {Leader} (difficulty={Diff}, stage={Stage}, members={Count})",
            room.Id, session.Name, difficulty, startStageId, members.Count);

        return true;
    }

    public async Task CheckTimeoutsAsync()
    {
        var now = DateTime.UtcNow;

        foreach (var (roomId, state) in activeRooms.ToArray())
        {
            var room = rooms.Get(roomId);
            if (room == null)
            {
                DropRoomState(roomId);
                continue;
            }

            bool shouldEject = false;
            bool shouldFail = false;
            bool shouldSpawn = false;

            lock (state.Lock)
            {
                if (state.ClosesAt != null && now >= state.ClosesAt)
                {
                    shouldEject = true;
                }
                else if (now >= state.Deadline && !state.Completed && !state.Failed)
                {
                    state.Failed = true;
                    state.ClosesAt = now.AddSeconds(10);
                    shouldFail = true;
                }
                else if (!state.WaveActive && !state.Completed && !state.Failed && now >= state.NextSpawnTime)
                {
                    state.WaveActive = true;
                    shouldSpawn = true;
                }
            }

            if (shouldEject)
            {
                await EjectAllAndCloseRoomAsync(room, state);
                continue;
            }

            if (shouldFail)
            {
                await BroadcastNoticeToRoomAsync(room, "Time is up! The Full Moon Rift has closed.");
                continue;
            }

            if (shouldSpawn)
            {
                await SpawnWaveAsync(room, state);
            }
        }
    }

    private async Task SpawnWaveAsync(InstanceRoom room, FullMoonRiftRoomState state)
    {
        var stageInfo = stageProvider.GetStage(state.CurrentStageId);
        if (stageInfo == null || stageInfo.Spawns.Count == 0)
        {
            logger.LogWarning("Stage {Stage} not found or empty for Full Moon Rift room {Room}, completing run", state.CurrentStageId, room.Id);
            lock (state.Lock)
            {
                state.Completed = true;
                state.WaveActive = false;
                state.ClosesAt = DateTime.UtcNow.AddSeconds(FullMoonRules.FinishGraceSeconds);
            }
            await BroadcastNoticeToRoomAsync(room, "Congratulations! You have conquered the Full Moon Rift!");
            return;
        }

        var (maxDisplay, currentDisplay) = FullMoonRules.DisplayStage(state.Difficulty, state.CurrentStageId);
        var counterPacket = EventPacketWriter.FullMoonStageCounter(maxDisplay, currentDisplay);

        foreach (var memberId in room.Members.Keys)
        {
            if (sessionManager.GetByCharacterId(memberId) is { } member)
                await member.Client.SendPacket(counterPacket);
        }

        var monsterCount = 0;
        foreach (var spawn in stageInfo.Spawns)
        {
            for (int i = 0; i < spawn.Count; i++)
            {
                float x = spawn.PosX;
                float z = spawn.PosZ;
                if (spawn.Radius > 0)
                {
                    var angle = Random.Shared.NextDouble() * Math.PI * 2;
                    var dist = Random.Shared.NextDouble() * spawn.Radius;
                    x += (float)(Math.Cos(angle) * dist);
                    z += (float)(Math.Sin(angle) * dist);
                }

                var npc = SpawnNpcAt(room, spawn.MonsterId, x, z, spawn.IsMonster, spawn.Direction);
                if (npc != null && spawn.IsMonster)
                    monsterCount++;
            }
        }

        if (monsterCount == 0)
        {
            lock (state.Lock)
            {
                state.Completed = true;
                state.WaveActive = false;
                state.ClosesAt = DateTime.UtcNow.AddSeconds(FullMoonRules.FinishGraceSeconds);
            }
            await BroadcastNoticeToRoomAsync(room, "Congratulations! You have conquered the Full Moon Rift!");
            return;
        }

        lock (state.Lock)
        {
            state.RemainingMonsters = monsterCount;
            state.WaveActive = true;
        }
        logger.LogInformation("Full Moon Rift room {Room} spawned stage {Stage} with {Count} monsters",
            room.Id, state.CurrentStageId, monsterCount);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession? killer)
    {
        if (npc.ZoneId != (byte)ZoneId.DungeonDefence || npc.Room == 0)
            return;

        if (!activeRooms.TryGetValue((ushort)npc.Room, out var state))
            return;

        var room = rooms.Get(state.RoomId);
        if (room == null)
            return;

        if (npc.NpcId == FullMoonRules.NexusTowerNpcId)
        {
            lock (state.Lock)
            {
                state.Failed = true;
                state.ClosesAt = DateTime.UtcNow.AddSeconds(10);
            }
            await BroadcastNoticeToRoomAsync(room, "The Nexus Tower has fallen! Defense failed!");
            return;
        }

        if (!npc.IsMonster)
        {
            int towersLeft;
            lock (state.Lock)
            {
                state.RemainingTowers = Math.Max(0, state.RemainingTowers - 1);
                towersLeft = state.RemainingTowers;
            }

            var towersPacket = EventPacketWriter.FullMoonTowersLeft((byte)towersLeft);
            foreach (var memberId in room.Members.Keys)
            {
                if (sessionManager.GetByCharacterId(memberId) is { } member)
                    await member.Client.SendPacket(towersPacket);
            }
            return;
        }

        if (killer != null)
        {
            await GrantKillRewardsAsync(killer, room, state, npc.NpcId);
        }

        bool waveFinished = false;
        bool runCompleted = false;
        int nextStage = 0;
        int interval = 10;

        lock (state.Lock)
        {
            state.RemainingMonsters--;
            if (state.RemainingMonsters <= 0)
            {
                state.WaveActive = false;
                waveFinished = true;
                var maxStage = FullMoonRules.MaxStageForDifficulty(state.Difficulty);

                if (state.CurrentStageId >= maxStage)
                {
                    state.Completed = true;
                    state.ClosesAt = DateTime.UtcNow.AddSeconds(FullMoonRules.FinishGraceSeconds);
                    runCompleted = true;
                }
                else
                {
                    state.CurrentStageId++;
                    nextStage = state.CurrentStageId;
                    var nextStageInfo = stageProvider.GetStage(nextStage);
                    interval = nextStageInfo?.SpawnIntervalSeconds ?? 10;
                    state.NextSpawnTime = DateTime.UtcNow.AddSeconds(interval);
                }
            }
        }

        if (waveFinished)
        {
            if (runCompleted)
            {
                await BroadcastNoticeToRoomAsync(room, "Congratulations! You have conquered the Full Moon Rift!");
                var timerPacket = BifrostPacketWriter.InstanceTimer(FullMoonRules.FinishGraceSeconds);
                foreach (var memberId in room.Members.Keys)
                {
                    if (sessionManager.GetByCharacterId(memberId) is { } member)
                        await member.Client.SendPacket(timerPacket);
                }
            }
            else
            {
                logger.LogInformation("Full Moon Rift room {Room} advanced to stage {Stage} in {Sec}s",
                    room.Id, nextStage, interval);
            }
        }
    }

    private async Task GrantKillRewardsAsync(UserSession killer, InstanceRoom room, FullMoonRiftRoomState state, int npcId)
    {
        if (gameData.GetItem(FullMoonRules.MonsterCoinItem) is { } coinItem)
        {
            await itemGrant.GrantAsync(killer, coinItem, 2);
        }

        var jewelCount = state.CurrentStageId >= FullMoonRules.HardMinStage ? 2 : 1;
        if (gameData.GetItem(FullMoonRules.RiftJewelItem) is { } jewelItem)
        {
            await itemGrant.GrantAsync(killer, jewelItem, jewelCount);
        }

        foreach (var memberId in room.Members.Keys)
        {
            if (memberId == killer.CharacterId)
                continue;

            if (sessionManager.GetByCharacterId(memberId) is { } member && gameData.GetItem(FullMoonRules.MonsterCoinItem) is { } cItem)
            {
                await itemGrant.GrantAsync(member, cItem, 1);
            }
        }
    }

    private async Task EjectAllAndCloseRoomAsync(InstanceRoom room, FullMoonRiftRoomState state)
    {
        foreach (var memberId in room.Members.Keys)
        {
            if (sessionManager.GetByCharacterId(memberId) is { } member)
            {
                await StripMonsterCoinsAsync(member);
                var returnPoint = member.InstanceReturn ?? ((byte)ZoneId.Moradon, 0f, 0f);
                await zoneTransition.ChangeZoneAsync(member, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
            }
        }

        DropRoomState(room.Id);
        rooms.Close(room);
    }

    private async Task StripMonsterCoinsAsync(UserSession session)
    {
        var removed = session.WithLock(s =>
        {
            var slots = new List<byte>();
            for (int i = 0; i < s.Inventory.Length; i++)
            {
                var slot = s.Inventory[i];
                if (slot.IsEmpty || slot.ItemId != FullMoonRules.MonsterCoinItem)
                    continue;
                slot.Clear();
                slots.Add((byte)i);
            }
            return slots;
        });

        if (removed.Count == 0)
            return;

        foreach (var slot in removed)
            await userNotification.SendStackChangeAsync(session, slot, 0, 0, 0);

        session.RecalculateStatsWithBuffs(gameData);
        await userNotification.SendWeightChangeAsync(session);
    }

    private async Task OnRoomClosedAsync(InstanceRoom room)
    {
        if (room.ZoneId != (byte)ZoneId.DungeonDefence)
            return;

        if (activeRooms.TryGetValue(room.Id, out var state))
        {
            foreach (var charId in state.Participants.Keys)
            {
                if (sessionManager.GetByCharacterId(charId) is { } member)
                    await StripMonsterCoinsAsync(member);
            }
            DropRoomState(room.Id);
        }

        await Task.CompletedTask;
    }

    private void SpawnInitialTowers(InstanceRoom room)
    {
        foreach (var (npcId, x, z) in FullMoonRules.InitialTowers)
        {
            SpawnNpcAt(room, npcId, x, z, isMonster: false);
        }

        foreach (var (npcId, x, z) in FullMoonRules.InitialPriests)
        {
            SpawnNpcAt(room, npcId, x, z, isMonster: false);
        }
    }

    private NpcInstance? SpawnNpcAt(InstanceRoom room, int npcId, float x, float z, bool isMonster, short direction = 0)
    {
        var npcData = gameData.GetNpc(npcId, isMonster: isMonster);
        if (npcData == null)
        {
            logger.LogWarning("Entity {NpcId} (monster={IsMonster}) not found in game data", npcId, isMonster);
            return null;
        }

        var pos = new NpcPosData
        {
            ZoneId = (byte)ZoneId.DungeonDefence,
            NpcId = npcId,
            LeftX = (int)x,
            TopZ = (int)z,
            Direction = direction,
            ActType = (byte)(isMonster ? 1 : NpcPosData.NpcSpawnActTypeBase),
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

        sessionManager.Regions.SpawnNpc(npc);

        if (isMonster)
        {
            aggression.Apply(npc);
            sessionManager.Regions.MarkNpcEngaged(npc);
        }

        room.Npcs.Add(npc);
        return npc;
    }

    private async Task BroadcastNoticeToRoomAsync(InstanceRoom room, string message)
    {
        foreach (var memberId in room.Members.Keys)
        {
            if (sessionManager.GetByCharacterId(memberId) is { } member)
                await member.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)member.Nation, message));
        }
    }

    public void DropRoomState(ushort roomId) => activeRooms.TryRemove(roomId, out _);

    public FullMoonRiftRoomState? GetRoomState(ushort roomId) => activeRooms.GetValueOrDefault(roomId);

    public void Dispose()
    {
        timer?.Dispose();
    }
}