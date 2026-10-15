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
    Task GrantStarterPackageAsync(UserSession session);
    Task<bool> BlessNexusTowerAsync(UserSession session, int healAmount);
}

public sealed class FullMoonRiftService : IFullMoonRiftService, IDisposable
{
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameData;
    private readonly IMagicItemUsageService _itemUsage;
    private readonly IItemGrantService _itemGrant;
    private readonly IMonsterAggressionPolicy _aggression;
    private readonly IZoneTransitionService _zoneTransition;
    private readonly InstanceRoomRegistry _rooms;
    private readonly IFullMoonRiftStageProvider _stageProvider;
    private readonly ILogger<FullMoonRiftService> _logger;
    private readonly ConcurrentDictionary<ushort, FullMoonRiftRoomState> _activeRooms = new();
    private readonly Timer? _timer;
    private int _checkingTimeouts;

    public FullMoonRiftService(
        SessionManager sessionManager,
        IGameDataService gameData,
        IMagicItemUsageService itemUsage,
        IItemGrantService itemGrant,
        IMonsterAggressionPolicy aggression,
        IZoneTransitionService zoneTransition,
        InstanceRoomRegistry rooms,
        IFullMoonRiftStageProvider stageProvider,
        ILogger<FullMoonRiftService> logger,
        bool startTimer = true)
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

        _rooms.RoomClosed += room => _ = OnRoomClosedAsync(room);

        if (startTimer)
        {
            _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private void OnTimerTick(object? _)
    {
        if (Interlocked.CompareExchange(ref _checkingTimeouts, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await CheckTimeoutsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Full Moon Rift timeouts");
            }
            finally
            {
                Interlocked.Exchange(ref _checkingTimeouts, 0);
            }
        });
    }

    public async Task<bool> HandlePartyEntryAsync(UserSession session)
    {
        var party = session.IsInParty ? _sessionManager.Parties.GetParty(session.PartyIndex) : null;
        if (party != null && party.LeaderId != (short)session.CharacterId)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "Only the party leader can initiate entry into the Full Moon Rift."));
            return false;
        }

        var members = party == null
            ? [session]
            : party.MemberIds.Where(id => id >= 0)
                .Select(id => _sessionManager.GetByCharacterId(id))
                .OfType<UserSession>().ToList();

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

        var missingVouchers = members.Where(m => !FullMoonRules.HasVoucher(m)).Select(m => m.Name).ToList();
        if (missingVouchers.Count > 0)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"Cannot enter: {string.Join(", ", missingVouchers)} does not have a Full Moon Rift Voucher."));
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(FullMoonRules.MemberWithoutVoucher));
            return false;
        }

        var code = FullMoonRules.EntryRefusal(session, members, party?.BoardType ?? FullMoonRules.FullMoonBoard, requireFullMoonBoard: false);
        if (code != FullMoonRules.Ready)
        {
            await session.Client.SendPacket(EventPacketWriter.FullMoonEntry(code));
            return false;
        }

        var difficulty = FullMoonRules.DifficultyForPartySize(members.Count);
        var startStageId = FullMoonRules.StartingStageForDifficulty(difficulty);

        foreach (var member in members)
        {
            await _itemUsage.TryConsumeItemAsync(member, FullMoonRules.VoucherItem);
        }

        var duration = TimeSpan.FromMinutes(FullMoonRules.RoomDurationMinutes);
        var room = _rooms.Open((byte)ZoneId.DungeonDefence, (short)difficulty, duration, endsOnBossKill: false);

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
        };

        foreach (var member in members)
        {
            _rooms.Join(room, member);
            state.Participants[member.CharacterId] = 0;
            member.InstanceReturn = (member.ZoneId, member.X, member.Z);
            await _zoneTransition.ChangeZoneAsync(member, (byte)ZoneId.DungeonDefence, FullMoonRules.SpawnX, FullMoonRules.SpawnZ);
            await member.Client.SendPacket(BifrostPacketWriter.InstanceTimer((ushort)duration.TotalSeconds));
            await GrantStarterPackageAsync(member);
        }

        SpawnInitialTowers(room);

        _activeRooms[room.Id] = state;
        _logger.LogInformation("Full Moon Rift room {Room} opened by {Leader} (difficulty={Diff}, stage={Stage}, members={Count})",
            room.Id, session.Name, difficulty, startStageId, members.Count);

        return true;
    }

    public async Task CheckTimeoutsAsync()
    {
        var now = DateTime.UtcNow;

        foreach (var (roomId, state) in _activeRooms.ToArray())
        {
            var room = _rooms.Get(roomId);
            if (room == null)
            {
                DropRoomState(roomId);
                continue;
            }

            if (state.ClosesAt != null && now >= state.ClosesAt)
            {
                await EjectAllAndCloseRoomAsync(room, state);
                continue;
            }

            if (now >= state.Deadline && !state.Completed && !state.Failed)
            {
                state.Failed = true;
                state.ClosesAt = now.AddSeconds(10);
                await BroadcastNoticeToRoomAsync(room, "Time is up! The Full Moon Rift has closed.");
                continue;
            }

            if (!state.WaveActive && !state.Completed && !state.Failed && now >= state.NextSpawnTime)
            {
                await SpawnWaveAsync(room, state);
            }
        }
    }

    private async Task SpawnWaveAsync(InstanceRoom room, FullMoonRiftRoomState state)
    {
        var stageInfo = _stageProvider.GetStage(state.CurrentStageId);
        if (stageInfo == null)
        {
            _logger.LogWarning("Stage {Stage} not found for Full Moon Rift room {Room}", state.CurrentStageId, room.Id);
            return;
        }

        var (maxDisplay, currentDisplay) = FullMoonRules.DisplayStage(state.Difficulty, state.CurrentStageId);
        var counterPacket = EventPacketWriter.FullMoonStageCounter(maxDisplay, currentDisplay);

        foreach (var memberId in room.Members.Keys)
        {
            if (_sessionManager.GetByCharacterId(memberId) is { } member)
                await member.Client.SendPacket(counterPacket);
        }

        var monsterCount = 0;
        foreach (var spawn in stageInfo.Spawns)
        {
            for (int i = 0; i < spawn.Count; i++)
            {
                float x = spawn.IsMonster ? FullMoonRules.MonsterSpawnX : spawn.PosX;
                float z = spawn.IsMonster ? FullMoonRules.MonsterSpawnZ : spawn.PosZ;
                if (spawn.Radius > 0 && !spawn.IsMonster)
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

        state.RemainingMonsters = monsterCount;
        state.WaveActive = true;
        _logger.LogInformation("Full Moon Rift room {Room} spawned stage {Stage} with {Count} monsters",
            room.Id, state.CurrentStageId, monsterCount);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession? killer)
    {
        if (npc.ZoneId != (byte)ZoneId.DungeonDefence || npc.Room == 0)
            return;

        if (!_activeRooms.TryGetValue((ushort)npc.Room, out var state))
            return;

        var room = _rooms.Get(state.RoomId);
        if (room == null)
            return;

        if (npc.NpcId == FullMoonRules.NexusTowerNpcId)
        {
            state.Failed = true;
            state.ClosesAt = DateTime.UtcNow.AddSeconds(10);
            await BroadcastNoticeToRoomAsync(room, "The Nexus Tower has fallen! Defense failed!");
            return;
        }

        if (!npc.IsMonster)
            return;

        if (killer != null)
        {
            await GrantKillRewardsAsync(killer, room, state, npc.NpcId);
        }

        if (Interlocked.Decrement(ref state.RemainingMonsters) <= 0)
        {
            state.WaveActive = false;
            var maxStage = FullMoonRules.MaxStageForDifficulty(state.Difficulty);

            if (state.CurrentStageId >= maxStage)
            {
                state.Completed = true;
                state.ClosesAt = DateTime.UtcNow.AddSeconds(FullMoonRules.FinishGraceSeconds);
                await BroadcastNoticeToRoomAsync(room, "Congratulations! You have conquered the Full Moon Rift!");
                var timerPacket = BifrostPacketWriter.InstanceTimer(FullMoonRules.FinishGraceSeconds);
                foreach (var memberId in room.Members.Keys)
                {
                    if (_sessionManager.GetByCharacterId(memberId) is { } member)
                        await member.Client.SendPacket(timerPacket);
                }
            }
            else
            {
                state.CurrentStageId++;
                var nextStageInfo = _stageProvider.GetStage(state.CurrentStageId);
                var interval = nextStageInfo?.SpawnIntervalSeconds ?? 10;
                state.NextSpawnTime = DateTime.UtcNow.AddSeconds(interval);
                _logger.LogInformation("Full Moon Rift room {Room} advanced to stage {Stage} in {Sec}s",
                    room.Id, state.CurrentStageId, interval);
            }
        }
    }

    private async Task GrantKillRewardsAsync(UserSession killer, InstanceRoom room, FullMoonRiftRoomState state, int npcId)
    {
        if (_gameData.GetItem(FullMoonRules.MonsterCoinItem) is { } coinItem)
        {
            await _itemGrant.GrantAsync(killer, coinItem, 2);
        }

        var jewelCount = state.CurrentStageId >= FullMoonRules.HardMinStage ? 2 : 1;
        if (_gameData.GetItem(FullMoonRules.RiftJewelItem) is { } jewelItem)
        {
            await _itemGrant.GrantAsync(killer, jewelItem, jewelCount);
        }

        foreach (var memberId in room.Members.Keys)
        {
            if (memberId == killer.CharacterId)
                continue;

            if (_sessionManager.GetByCharacterId(memberId) is { } member && _gameData.GetItem(FullMoonRules.MonsterCoinItem) is { } cItem)
            {
                await _itemGrant.GrantAsync(member, cItem, 1);
            }
        }

        if (FullMoonRules.BossMonsterIds.Contains(npcId) && _gameData.GetItem(FullMoonRules.LunarOrderToken) is { } tokenItem)
        {
            foreach (var memberId in room.Members.Keys)
            {
                if (_sessionManager.GetByCharacterId(memberId) is { } member)
                {
                    await _itemGrant.GrantAsync(member, tokenItem, 1);
                }
            }
        }
    }

    private async Task EjectAllAndCloseRoomAsync(InstanceRoom room, FullMoonRiftRoomState state)
    {
        foreach (var memberId in room.Members.Keys)
        {
            if (_sessionManager.GetByCharacterId(memberId) is { } member)
            {
                StripMonsterCoins(member);
                var returnPoint = member.InstanceReturn ?? ((byte)ZoneId.Moradon, 0f, 0f);
                await _zoneTransition.ChangeZoneAsync(member, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
            }
        }

        DropRoomState(room.Id);
        _rooms.Close(room);
    }

    public async Task GrantStarterPackageAsync(UserSession session)
    {
        if (_gameData.GetItem(FullMoonRules.KillingBladeItem) is { } bladeItem)
            await _itemGrant.GrantAsync(session, bladeItem, FullMoonRules.StarterKillingBladeCount);

        if (_gameData.GetItem(FullMoonRules.DrainRestoreItem) is { } healItem)
            await _itemGrant.GrantAsync(session, healItem, FullMoonRules.StarterDrainRestoreCount);

        if (_gameData.GetItem(FullMoonRules.LightPitItem) is { } pitItem)
            await _itemGrant.GrantAsync(session, pitItem, FullMoonRules.StarterLightPitCount);

        await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
            (byte)session.Nation,
            $"[Priest of Full Moon] Received starter battle skills: Killing Blade x{FullMoonRules.StarterKillingBladeCount}, Drain Restore x{FullMoonRules.StarterDrainRestoreCount}, Light Pit x{FullMoonRules.StarterLightPitCount}!"));
    }

    public async Task<bool> BlessNexusTowerAsync(UserSession session, int healAmount)
    {
        var state = _activeRooms.Values.FirstOrDefault(r => r.Participants.ContainsKey(session.CharacterId));
        if (state == null)
            return false;

        var room = _rooms.Get(state.RoomId);
        if (room == null)
            return false;

        var nexusTower = room.Npcs.FirstOrDefault(n => n.NpcId == FullMoonRules.NexusTowerNpcId && n.IsAlive);
        if (nexusTower == null)
            return false;

        nexusTower.Hp = Math.Min(nexusTower.MaxHp, nexusTower.Hp + healAmount);
        var hpPkt = TargetHpPacketWriter.For(nexusTower.UniqueId, nexusTower.MaxHp, nexusTower.Hp, 0);
        await _sessionManager.Regions.BroadcastFromNpc(nexusTower, hpPkt);
        await BroadcastNoticeToRoomAsync(room, $"[Priest of Full Moon] {session.Name} has blessed the Nexus Tower! (+{healAmount:N0} HP)");
        return true;
    }

    private static void StripMonsterCoins(UserSession session)
    {
        for (int i = 0; i < session.Inventory.Length; i++)
        {
            var itemId = session.Inventory[i].ItemId;
            if (itemId is FullMoonRules.MonsterCoinItem
                       or FullMoonRules.KillingBladeItem
                       or FullMoonRules.DrainRestoreItem
                       or FullMoonRules.LightPitItem)
            {
                session.Inventory[i].ItemId = 0;
                session.Inventory[i].Count = 0;
            }
        }
    }

    private async Task OnRoomClosedAsync(InstanceRoom room)
    {
        if (room.ZoneId != (byte)ZoneId.DungeonDefence)
            return;

        if (_activeRooms.TryGetValue(room.Id, out var state))
        {
            foreach (var charId in state.Participants.Keys)
            {
                if (_sessionManager.GetByCharacterId(charId) is { } member)
                    StripMonsterCoins(member);
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
    }

    private NpcInstance? SpawnNpcAt(InstanceRoom room, int npcId, float x, float z, bool isMonster, short direction = 0)
    {
        var npcData = _gameData.GetNpc(npcId, isMonster: isMonster);
        if (npcData == null)
        {
            _logger.LogWarning("Entity {NpcId} (monster={IsMonster}) not found in game data", npcId, isMonster);
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
        var height = _sessionManager.Maps?.GetHeight(npc.ZoneId, npc.X, npc.Z) ?? 0f;
        npc.Y = height;
        npc.SpawnY = height;

        _sessionManager.Regions.SpawnNpc(npc);

        if (isMonster)
        {
            _aggression.Apply(npc);
            npc.MoveType = NpcMoveType.PatrolOnce;
            var stageId = _activeRooms.TryGetValue(room.Id, out var state) ? state.CurrentStageId : 1;
            var useNorth = stageId <= 5 ? (stageId % 2 == 1) : (room.Npcs.Count % 2 == 0);
            npc.Waypoints = useNorth ? FullMoonRules.NorthLaneWaypoints : FullMoonRules.EastLaneWaypoints;
            npc.CurrentWaypoint = 0;
            _sessionManager.Regions.MarkNpcEngaged(npc);
        }

        room.Npcs.Add(npc);
        return npc;
    }

    private async Task BroadcastNoticeToRoomAsync(InstanceRoom room, string message)
    {
        foreach (var memberId in room.Members.Keys)
        {
            if (_sessionManager.GetByCharacterId(memberId) is { } member)
                await member.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)member.Nation, message));
        }
    }

    public void DropRoomState(ushort roomId) => _activeRooms.TryRemove(roomId, out _);

    public FullMoonRiftRoomState? GetRoomState(ushort roomId) => _activeRooms.GetValueOrDefault(roomId);

    public void Dispose()
    {
        _timer?.Dispose();
    }
}