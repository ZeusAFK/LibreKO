using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IChaosDungeonService
{
    bool HasActiveMatches { get; }
    Task StartMatchesAsync(IReadOnlyList<int> participantCharacterIds, int durationSeconds);
    Task OnPlayerKilledAsync(UserSession victim, UserSession? killer);
    Task OnPlayerLogoutOrLeaveAsync(UserSession session);
    Task<bool> TryTouchCubeAsync(UserSession session, NpcInstance npc);
    Packet BuildRanking(UserSession session, byte rankType);
    Task FinishAllMatchesAsync();
    Task CancelAllMatchesAsync();
    Task TickAsync();
}

public sealed class ChaosMatch
{
    public ushort RoomId { get; init; }
    public InstanceRoom InstanceRoom { get; init; } = null!;
    public List<int> Participants { get; } = [];
    public Dictionary<int, ChaosRules.Score> Scores { get; } = [];
    public DateTime MatchEndUtc { get; set; }
    public bool IsFinishing { get; set; }
    public DateTime? FinishAtUtc { get; set; }
    public bool IsCompleted { get; set; }
    public List<(NpcInstance Cube, DateTime RespawnAtUtc)> CubeRespawns { get; } = [];
}

public sealed class ChaosDungeonService : IChaosDungeonService
{
    private const byte NoWinner = 0;
    private const int CleanupMarginSeconds = 5;

    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameDataService;
    private readonly IZoneTransitionService zoneTransitionService;
    private readonly InstanceRoomRegistry instanceRooms;
    private readonly IInstanceEntryService instanceEntryService;
    private readonly IUserNotificationService userNotificationService;
    private readonly IPlayerProgressionService playerProgressionService;
    private readonly ICombatNotificationService combatNotificationService;
    private readonly ILogger<ChaosDungeonService> logger;
    private readonly ConcurrentDictionary<ushort, ChaosMatch> activeMatches = new();

    public ChaosDungeonService(
        SessionManager sessionManager,
        IGameDataService gameDataService,
        IZoneTransitionService zoneTransitionService,
        InstanceRoomRegistry instanceRooms,
        IInstanceEntryService instanceEntryService,
        IUserNotificationService userNotificationService,
        IPlayerProgressionService playerProgressionService,
        ICombatNotificationService combatNotificationService,
        ILogger<ChaosDungeonService> logger)
    {
        this.sessionManager = sessionManager;
        this.gameDataService = gameDataService;
        this.zoneTransitionService = zoneTransitionService;
        this.instanceRooms = instanceRooms;
        this.instanceEntryService = instanceEntryService;
        this.userNotificationService = userNotificationService;
        this.playerProgressionService = playerProgressionService;
        this.combatNotificationService = combatNotificationService;
        this.logger = logger;

        zoneTransitionService.PlayerLeavingZone += OnPlayerLeavingZoneAsync;
    }

    public bool HasActiveMatches => !activeMatches.IsEmpty;

    internal ChaosMatch? GetMatch(ushort roomId) => activeMatches.GetValueOrDefault(roomId);

    public async Task StartMatchesAsync(IReadOnlyList<int> participantCharacterIds, int durationSeconds)
    {
        var sessions = participantCharacterIds
            .Select(sessionManager.GetByCharacterId)
            .Where(s => s != null)
            .Cast<UserSession>()
            .ToList();

        short set = 1;
        for (int i = 0; i < sessions.Count && set <= ChaosRules.MaxSpawnSet; i += ChaosRules.PlayersPerRoom, set++)
            await StartRoomAsync(sessions.Skip(i).Take(ChaosRules.PlayersPerRoom).ToList(), set, durationSeconds);
    }

    private async Task StartRoomAsync(List<UserSession> group, short set, int durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds > 0 ? durationSeconds : TempleEventRules.ChaosDurationSeconds);
        var room = instanceRooms.Open(ChaosRules.Zone, set, duration);
        var match = new ChaosMatch
        {
            RoomId = room.Id,
            InstanceRoom = room,
            MatchEndUtc = DateTime.UtcNow + duration
                - TimeSpan.FromSeconds(ChaosRules.FinishCountdownSeconds + CleanupMarginSeconds),
        };
        activeMatches[room.Id] = match;
        instanceEntryService.Populate(room);

        var start = gameDataService.GetStartPosition(ChaosRules.Zone);
        var blinkUntil = DateTime.UtcNow.AddSeconds(ChaosRules.BattleLockSeconds).Ticks;
        foreach (var session in group)
        {
            match.Participants.Add(session.CharacterId);
            match.Scores[session.CharacterId] = new ChaosRules.Score(session.CharacterId, session.Name, 0, 0);
            instanceRooms.Join(room, session);
            var (x, z) = start?.RandomSpawn(session.Nation) ?? (0f, 0f);
            session.InstanceReturn = (session.ZoneId, session.X, session.Z);
            await zoneTransitionService.ChangeZoneAsync(session, ChaosRules.Zone, x, z);
            session.BlinkUntilTicks = blinkUntil;
            session.RecalculateStatsWithBuffs(gameDataService);
            session.Hp = session.MaxHp;
            await combatNotificationService.SendHpChangeAsync(session);
        }

        await SendClockAsync(match);
        await BroadcastToRoomAsync(match, ParticipantStates(match, PlayerAppearanceState.Blinking));
        logger.LogInformation("Chaos room {Room} (set {Set}) started with {Count} players", room.Id, set, group.Count);
    }

    private IEnumerable<Packet> ParticipantStates(ChaosMatch match, PlayerAppearanceState state) =>
        match.Participants.Select(id =>
            MovementPacketWriter.StateChange(id, (byte)StateChangeType.Abnormal, (byte)state));

    private async Task SendClockAsync(ChaosMatch match)
    {
        uint left = (uint)Math.Max(0, (match.MatchEndUtc - DateTime.UtcNow).TotalSeconds);
        await BroadcastToRoomAsync(match, [
            InstanceDashboardPacketWriter.EventClock((byte)ChaosRules.IndunEvent, left),
            BifrostPacketWriter.InstanceTimer((ushort)Math.Min(left, ushort.MaxValue)),
        ]);
    }

    public async Task OnPlayerKilledAsync(UserSession victim, UserSession? killer)
    {
        await RemoveChaosItemsAsync(victim);
        if (!activeMatches.TryGetValue(victim.Room, out var match) || match.IsCompleted || match.IsFinishing)
            return;

        if (match.Scores.TryGetValue(victim.CharacterId, out var died))
            match.Scores[victim.CharacterId] = died with { Deaths = died.Deaths + 1 };

        if (killer == null || killer.IsGM || killer.Room != match.RoomId
            || !match.Scores.TryGetValue(killer.CharacterId, out var won))
            return;

        match.Scores[killer.CharacterId] = won with { Kills = won.Kills + 1 };
        await combatNotificationService.SendDeathNoticeAsync(killer, victim);
    }

    public async Task<bool> TryTouchCubeAsync(UserSession session, NpcInstance npc)
    {
        if (npc.ZoneId != ChaosRules.Zone || npc.NpcId != ChaosRules.CubeNpcId)
            return false;
        if (session.Hp <= 0 || !activeMatches.TryGetValue(session.Room, out var match) || match.IsCompleted
            || npc.Room != match.RoomId || !npc.IsAlive || !npc.TryBeginDeath())
            return true;

        var ordered = ChaosRules.Ordered(match.Scores.Values);
        int itemId = ChaosRules.CubeItem(ChaosRules.RankPercent(ordered, session.CharacterId));
        await TempleEventHelpers.TryGiveItemAsync(gameDataService, userNotificationService, session, itemId, 1);

        npc.Hp = 0;
        npc.State = NpcState.Dead;
        await sessionManager.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.Out));
        match.CubeRespawns.Add((npc, DateTime.UtcNow.AddSeconds(ChaosRules.CubeRespawnSeconds)));
        return true;
    }

    public Packet BuildRanking(UserSession session, byte rankType)
    {
        if (session.ZoneId != ChaosRules.Zone || !activeMatches.TryGetValue(session.Room, out var match))
            return RankPacketWriter.ChaosDungeon(rankType, [], 0, 0);

        var rows = ChaosRules.Ordered(match.Scores.Values)
            .Take(ChaosRules.PlayersPerRoom)
            .Select(s => new RankPacketWriter.ChaosRow(s.Name, (short)Math.Min(s.Kills, short.MaxValue), (short)Math.Min(s.Deaths, short.MaxValue)))
            .ToList();
        var own = match.Scores.GetValueOrDefault(session.CharacterId);
        long exp = ChaosRules.Exp(session.Level, own.Kills, own.Deaths);
        long premium = ChaosRules.PremiumExp(exp, session.PremiumType > 0);
        return RankPacketWriter.ChaosDungeon(rankType, rows, (uint)exp, (uint)premium);
    }

    public Task OnPlayerLeavingZoneAsync(UserSession session, byte oldZone) =>
        oldZone == ChaosRules.Zone ? LeaveAsync(session) : Task.CompletedTask;

    public Task OnPlayerLogoutOrLeaveAsync(UserSession session) =>
        session.ZoneId == ChaosRules.Zone ? LeaveAsync(session) : Task.CompletedTask;

    private async Task LeaveAsync(UserSession session)
    {
        await RemoveChaosItemsAsync(session);
        if (session.IsBlinking)
        {
            session.BlinkUntilTicks = 0;
            await session.Client.SendPacket(MovementPacketWriter.StateChange(
                session.CharacterId, (byte)StateChangeType.Abnormal, (byte)session.SizeState));
        }
        if (activeMatches.TryGetValue(session.Room, out var match) && !match.IsCompleted)
            match.Participants.Remove(session.CharacterId);
    }

    public async Task TickAsync()
    {
        if (activeMatches.IsEmpty)
            return;

        var now = DateTime.UtcNow;
        foreach (var match in activeMatches.Values.ToList())
        {
            if (match.IsCompleted)
                continue;

            await RespawnCubesAsync(match, now);

            if (match.IsFinishing && match.FinishAtUtc is { } finishAt && now >= finishAt)
                await CloseMatchAsync(match, rewarded: true);
            else if (!match.IsFinishing && now >= match.MatchEndUtc)
                await FinishAsync(match);
        }
    }

    private async Task RespawnCubesAsync(ChaosMatch match, DateTime now)
    {
        for (int i = match.CubeRespawns.Count - 1; i >= 0; i--)
        {
            var (cube, at) = match.CubeRespawns[i];
            if (now < at)
                continue;
            match.CubeRespawns.RemoveAt(i);
            cube.Respawn();
            await sessionManager.Regions.BroadcastFromNpc(cube, NpcPacketMapper.BuildInOutPacket(cube, InOutType.In));
        }
    }

    private async Task FinishAsync(ChaosMatch match)
    {
        if (match.IsFinishing || match.IsCompleted)
            return;
        match.IsFinishing = true;
        match.FinishAtUtc = DateTime.UtcNow.AddSeconds(ChaosRules.FinishCountdownSeconds);
        long holdUntil = DateTime.UtcNow.AddSeconds(ChaosRules.FinishCountdownSeconds + CleanupMarginSeconds).Ticks;
        foreach (var id in match.Participants)
            if (sessionManager.GetByCharacterId(id) is { } member)
                member.BlinkUntilTicks = holdUntil;
        await BroadcastToRoomAsync(match, [EventPacketWriter.TempleEventFinish(
            ChaosRules.IndunEvent, NoWinner, (uint)ChaosRules.FinishCountdownSeconds)]);
        await RewardAsync(match);
        logger.LogInformation("Chaos room {Room} finished", match.RoomId);
    }

    private async Task RewardAsync(ChaosMatch match)
    {
        var ordered = ChaosRules.Ordered(match.Scores.Values);
        foreach (var id in match.Participants.ToList())
        {
            var member = sessionManager.GetByCharacterId(id);
            if (member == null || member.ZoneId != ChaosRules.Zone || member.Room != match.RoomId || member.IsGM)
                continue;

            int percent = ChaosRules.RankPercent(ordered, id);
            foreach (int itemId in ChaosRules.Rewards(percent))
                await TempleEventHelpers.TryGiveItemAsync(gameDataService, userNotificationService, member, itemId, 1,
                    "Inventory is full! Could not receive the Chaos reward.");

            var score = match.Scores.GetValueOrDefault(id);
            long exp = ChaosRules.Exp(member.Level, score.Kills, score.Deaths);
            long total = exp + ChaosRules.PremiumExp(exp, member.PremiumType > 0);
            if (total > 0)
                await playerProgressionService.AwardExperienceAsync(member, total);
        }
    }

    public async Task FinishAllMatchesAsync()
    {
        foreach (var match in activeMatches.Values.ToList())
        {
            if (match.IsCompleted)
                continue;
            if (!match.IsFinishing)
                await FinishAsync(match);
            await CloseMatchAsync(match, rewarded: true);
        }
    }

    public async Task CancelAllMatchesAsync()
    {
        foreach (var match in activeMatches.Values.ToList())
            await CloseMatchAsync(match, rewarded: false);
    }

    private async Task CloseMatchAsync(ChaosMatch match, bool rewarded)
    {
        if (match.IsCompleted)
            return;
        match.IsCompleted = true;

        foreach (var id in match.Participants.ToList())
        {
            var member = sessionManager.GetByCharacterId(id);
            if (member == null || member.ZoneId != ChaosRules.Zone || member.Room != match.RoomId)
                continue;
            try
            {
                await RemoveChaosItemsAsync(member);
                member.BlinkUntilTicks = 0;
                instanceRooms.Leave(member);
                var home = member.InstanceReturn ?? ((byte)ZoneId.Moradon, 0f, 0f);
                member.InstanceReturn = null;
                await zoneTransitionService.ChangeZoneAsync(member, home.ZoneId, home.X, home.Z);
                await member.Client.SendPacket(MovementPacketWriter.StateChange(
                    member.CharacterId, (byte)StateChangeType.Abnormal, (byte)PlayerAppearanceState.Normal));
                member.RecalculateStatsWithBuffs(gameDataService);
                member.Hp = member.MaxHp;
                await userNotificationService.SendStatUpdateAsync(member);
                await combatNotificationService.SendHpChangeAsync(member);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send {Name} home from Chaos room {Room}", member.Name, match.RoomId);
            }
        }

        instanceRooms.Close(match.InstanceRoom);
        activeMatches.TryRemove(match.RoomId, out _);
        logger.LogInformation("Chaos room {Room} closed (rewarded {Rewarded})", match.RoomId, rewarded);
    }

    private async Task RemoveChaosItemsAsync(UserSession session)
    {
        var removed = session.WithLock(s =>
        {
            var slots = new List<byte>();
            for (int i = 0; i < s.Inventory.Length; i++)
            {
                var slot = s.Inventory[i];
                if (slot.IsEmpty || gameDataService.GetItem(slot.ItemId)?.Kind != ChaosRules.ChaosItemKind)
                    continue;
                slot.Clear();
                slots.Add((byte)i);
            }
            return slots;
        });
        if (removed.Count == 0)
            return;
        foreach (var slot in removed)
            await userNotificationService.SendStackChangeAsync(session, slot, 0, 0, 0);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task BroadcastToRoomAsync(ChaosMatch match, IEnumerable<Packet> packets)
    {
        var list = packets.ToList();
        foreach (var id in match.Participants.ToList())
        {
            var member = sessionManager.GetByCharacterId(id);
            if (member == null || member.Room != match.RoomId || member.ZoneId != ChaosRules.Zone)
                continue;
            foreach (var packet in list)
                await member.Client.SendPacket(packet);
        }
    }
}
