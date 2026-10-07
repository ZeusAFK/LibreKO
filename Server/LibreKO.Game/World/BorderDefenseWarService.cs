using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IBorderDefenseWarService
{
    bool HasActiveMatches { get; }
    Task StartMatchesAsync(IReadOnlyList<int> participantCharacterIds, int durationSeconds);
    Task StartMatchForCallerAsync(UserSession session, int durationSeconds = TempleEventRules.BorderDefenseWarDurationSeconds);
    Task OnNpcKilledAsync(NpcInstance npc, UserSession killer);
    Task OnPlayerKilledAsync(UserSession victim, UserSession? killer);
    Task CheckCarrierBaseDeliveryAsync(UserSession session);
    Task OnPlayerLogoutOrLeaveAsync(UserSession session);
    Task FinishAllMatchesAsync();
    Task CancelAllMatchesAsync();
    Task TickAsync();
}

public sealed class BdwMatch
{
    public ushort RoomId { get; set; }
    public InstanceRoom InstanceRoom { get; set; } = null!;
    public HashSet<int> Participants { get; } = [];
    public HashSet<int> KarusMembers { get; } = [];
    public HashSet<int> ElmoradMembers { get; } = [];

    public int KarusScore { get; set; }
    public int ElmoradScore { get; set; }
    public int KarusKillCount { get; set; }
    public int ElmoradKillCount { get; set; }

    public int TargetScore { get; set; } = 600;

    public int KarusPartyIndex { get; set; } = -1;
    public int ElmoradPartyIndex { get; set; } = -1;

    public NpcInstance? AltarNpc { get; set; }
    public int? AltarCarrierCharacterId { get; set; }
    public AccountNation? AltarCarrierNation { get; set; }
    public DateTime? AltarRespawnAtUtc { get; set; }

    public DateTime MatchStartUtc { get; set; }
    public DateTime MatchEndUtc { get; set; }

    public bool IsFinishing { get; set; }
    public DateTime? FinishAtUtc { get; set; }
    public AccountNation? WinnerNation { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed class BorderDefenseWarService : IBorderDefenseWarService
{
    public const byte BdwZoneId = (byte)ZoneId.BorderDefenseWar;
    public const int FragmentOfManesSkillId = 492063;
    public const short AltarOfManesProtoId = 9840;
    public const float AltarDefaultX = 127f;
    public const float AltarDefaultZ = 131f;

    public const int RedTreasureChestId = 379154000;
    public const int AltarRespawnCooldownSeconds = 60;
    public const int FinishCountdownSeconds = 20;

    public const int MaxPlayersPerNationPerRoom = 8;
    public const int PointsPerAltarDelivery = 10;
    public const int PointsPerKill = 1;

    public const int HeadcountTier1 = 5;
    public const int HeadcountTier2 = 10;
    public const int HeadcountTier3 = 16;

    public const int TargetScoreBase = 130;
    public const int TargetScoreTier1 = 300;
    public const int TargetScoreTier2 = 400;
    public const int TargetScoreTier3 = 600;

    public const float KarusStartX = 51f;
    public const float KarusStartZ = 58f;
    public const float ElmoradStartX = 201f;
    public const float ElmoradStartZ = 207f;

    public const float KarusDeliveryMinX = 25f;
    public const float KarusDeliveryMaxX = 55f;
    public const float KarusDeliveryMinZ = 45f;
    public const float KarusDeliveryMaxZ = 140f;

    public const float ElmoradDeliveryMinX = 195f;
    public const float ElmoradDeliveryMaxX = 230f;
    public const float ElmoradDeliveryMinZ = 120f;
    public const float ElmoradDeliveryMaxZ = 215f;

    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameDataService;
    private readonly IMonsterAggressionPolicy aggressionPolicy;
    private readonly IZoneTransitionService zoneTransitionService;
    private readonly InstanceRoomRegistry instanceRooms;
    private readonly IInstanceEntryService instanceEntryService;
    private readonly IUserNotificationService userNotificationService;
    private readonly ILoyaltyService loyaltyService;
    private readonly IPlayerProgressionService playerProgressionService;
    private readonly ICombatNotificationService combatNotificationService;
    private readonly IMagicStatusEffectService magicStatusEffectService;
    private readonly ILogger<BorderDefenseWarService> logger;

    private readonly ConcurrentDictionary<ushort, BdwMatch> _activeMatches = new();

    public bool HasActiveMatches => !_activeMatches.IsEmpty;

    public BorderDefenseWarService(
        SessionManager sessionManager,
        IGameDataService gameDataService,
        IMonsterAggressionPolicy aggressionPolicy,
        IZoneTransitionService zoneTransitionService,
        InstanceRoomRegistry instanceRooms,
        IInstanceEntryService instanceEntryService,
        IUserNotificationService userNotificationService,
        ILoyaltyService loyaltyService,
        IPlayerProgressionService playerProgressionService,
        ICombatNotificationService combatNotificationService,
        IMagicStatusEffectService magicStatusEffectService,
        ILogger<BorderDefenseWarService> logger)
    {
        this.sessionManager = sessionManager;
        this.gameDataService = gameDataService;
        this.aggressionPolicy = aggressionPolicy;
        this.zoneTransitionService = zoneTransitionService;
        this.instanceRooms = instanceRooms;
        this.instanceEntryService = instanceEntryService;
        this.userNotificationService = userNotificationService;
        this.loyaltyService = loyaltyService;
        this.playerProgressionService = playerProgressionService;
        this.combatNotificationService = combatNotificationService;
        this.magicStatusEffectService = magicStatusEffectService;
        this.logger = logger;

        zoneTransitionService.PlayerLeavingZone += OnPlayerLeavingZoneAsync;
    }

    internal BdwMatch? GetMatch(ushort roomId) => _activeMatches.GetValueOrDefault(roomId);

    public async Task StartMatchesAsync(IReadOnlyList<int> participantCharacterIds, int durationSeconds)
    {
        var karusSessions = new List<UserSession>();
        var elmoSessions = new List<UserSession>();

        foreach (var charId in participantCharacterIds)
        {
            var session = sessionManager.GetByCharacterId(charId);
            if (session == null)
                continue;

            if (session.Nation == AccountNation.Karus)
                karusSessions.Add(session);
            else if (session.Nation == AccountNation.ElMorad)
                elmoSessions.Add(session);
        }

        while (karusSessions.Count > 0 && elmoSessions.Count > 0)
        {
            var karusGroup = karusSessions.Take(MaxPlayersPerNationPerRoom).ToList();
            var elmoGroup = elmoSessions.Take(MaxPlayersPerNationPerRoom).ToList();
            karusSessions.RemoveRange(0, karusGroup.Count);
            elmoSessions.RemoveRange(0, elmoGroup.Count);

            await CreateAndStartMatchAsync(karusGroup, elmoGroup, durationSeconds);
        }

        var leftovers = karusSessions.Concat(elmoSessions);
        foreach (var leftover in leftovers)
        {
            await leftover.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)leftover.Nation,
                "[Border Defense War] Not enough opponent players registered. Match could not start."));
        }
    }

    public async Task StartMatchForCallerAsync(UserSession session, int durationSeconds = TempleEventRules.BorderDefenseWarDurationSeconds)
    {
        var bdwSchedules = gameDataService.TempleEventSchedules.Where(s => s.Event == TempleEvent.BorderDefenseWar).ToList();
        var minLevel = bdwSchedules.Count > 0
            ? bdwSchedules.Min(s => s.MinLevel)
            : TempleEventRules.BorderDefenseWarDefaultMinLevel;

        if (session.Level < minLevel)
        {
            var noticePkt = ChatPacketWriter.SystemNotice((byte)session.Nation, $"You must be at least level {minLevel} to enter Border Defense War.");
            await session.Client.SendPacket(noticePkt);
            return;
        }

        var karus = session.Nation == AccountNation.Karus ? new List<UserSession> { session } : [];
        var elmo = session.Nation == AccountNation.ElMorad ? new List<UserSession> { session } : [];

        await CreateAndStartMatchAsync(karus, elmo, durationSeconds);
    }

    private async Task CreateAndStartMatchAsync(List<UserSession> karusGroup, List<UserSession> elmoGroup, int durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds > 0 ? durationSeconds : TempleEventRules.BorderDefenseWarDurationSeconds);
        var instanceRoom = instanceRooms.Open(BdwZoneId, 1, duration);
        ushort roomId = instanceRoom.Id;

        int totalCount = karusGroup.Count + elmoGroup.Count;
        int targetScore = totalCount >= HeadcountTier3
            ? TargetScoreTier3
            : (totalCount >= HeadcountTier2
                ? TargetScoreTier2
                : (totalCount >= HeadcountTier1 ? TargetScoreTier1 : TargetScoreBase));

        var match = new BdwMatch
        {
            RoomId = roomId,
            InstanceRoom = instanceRoom,
            TargetScore = targetScore,
            MatchStartUtc = DateTime.UtcNow,
            MatchEndUtc = DateTime.UtcNow.AddSeconds(durationSeconds > 0 ? durationSeconds : TempleEventRules.BorderDefenseWarDurationSeconds)
        };

        foreach (var s in karusGroup)
        {
            match.Participants.Add(s.CharacterId);
            match.KarusMembers.Add(s.CharacterId);
        }

        foreach (var s in elmoGroup)
        {
            match.Participants.Add(s.CharacterId);
            match.ElmoradMembers.Add(s.CharacterId);
        }

        _activeMatches[roomId] = match;

        instanceEntryService.Populate(match.InstanceRoom);
        match.AltarNpc = match.InstanceRoom.Npcs.FirstOrDefault(n => n.NpcId == AltarOfManesProtoId);

        var startPos = gameDataService.GetStartPosition(BdwZoneId);

        foreach (var s in karusGroup)
        {
            instanceRooms.Join(match.InstanceRoom, s);
            var (startX, startZ) = startPos?.RandomSpawn(s.Nation) ?? (KarusStartX, KarusStartZ);
            s.InstanceReturn = (s.ZoneId, s.X, s.Z);
            await zoneTransitionService.ChangeZoneAsync(s, BdwZoneId, startX, startZ);
            await s.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)s.Nation,
                $"[Border Defense War] Entered Room {roomId}! First nation to {targetScore} points wins!"));
        }

        foreach (var s in elmoGroup)
        {
            instanceRooms.Join(match.InstanceRoom, s);
            var (startX, startZ) = startPos?.RandomSpawn(s.Nation) ?? (ElmoradStartX, ElmoradStartZ);
            s.InstanceReturn = (s.ZoneId, s.X, s.Z);
            await zoneTransitionService.ChangeZoneAsync(s, BdwZoneId, startX, startZ);
            await s.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)s.Nation,
                $"[Border Defense War] Entered Room {roomId}! First nation to {targetScore} points wins!"));
        }

        match.KarusPartyIndex = await TempleEventHelpers.FormNationPartyAsync(
            sessionManager, combatNotificationService, karusGroup, "Border Defense War", roomId, logger);
        match.ElmoradPartyIndex = await TempleEventHelpers.FormNationPartyAsync(
            sessionManager, combatNotificationService, elmoGroup, "Border Defense War", roomId, logger);

        await BroadcastScoresToRoomAsync(match);
        await SendNoticeToRoomAsync(match, $"### [Border Defense War] Match started! Target score to win: {targetScore} points! ###");
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.ZoneId != BdwZoneId || !_activeMatches.TryGetValue(killer.Room, out var match) || match.IsCompleted)
            return;

        if (npc.NpcId == AltarOfManesProtoId)
        {
            await HandleAltarCapturedAsync(match, killer);
        }
    }

    private async Task HandleAltarCapturedAsync(BdwMatch match, UserSession killer)
    {
        if (match.AltarCarrierCharacterId.HasValue)
            return;

        match.AltarCarrierCharacterId = killer.CharacterId;
        match.AltarCarrierNation = killer.Nation;
        match.AltarRespawnAtUtc = null;

        var magic = gameDataService.GetMagic(FragmentOfManesSkillId);
        if (magic != null)
        {
            await magicStatusEffectService.ExecuteAsync(
                killer, magic, MagicSkillType.Buff, FragmentOfManesSkillId, killer.CharacterId, new int[7]);
        }

        var flagPkt = EventPacketWriter.AltarFlag(killer.Name, (byte)killer.Nation);
        await BroadcastPacketToRoomAsync(match, flagPkt);

        var nationName = killer.Nation == AccountNation.Karus ? "Karus" : "El Morad";
        await SendNoticeToRoomAsync(match, $"### [Border Defense War] {killer.Name} ({nationName}) has obtained the Fragment of Manes! Bring it back to your base! ###");

        logger.LogInformation("Player {Name} ({Nation}) picked up Altar in BDW room {Room}",
            killer.Name, killer.Nation, match.RoomId);
    }

    public async Task CheckCarrierBaseDeliveryAsync(UserSession session)
    {
        if (session.ZoneId != BdwZoneId || !_activeMatches.TryGetValue(session.Room, out var match) || match.IsCompleted)
            return;

        if (match.AltarCarrierCharacterId != session.CharacterId)
            return;

        bool inBase = false;
        if (session.Nation == AccountNation.Karus)
        {
            if (session.X >= KarusDeliveryMinX && session.X <= KarusDeliveryMaxX && session.Z >= KarusDeliveryMinZ && session.Z <= KarusDeliveryMaxZ)
                inBase = true;
        }
        else if (session.Nation == AccountNation.ElMorad)
        {
            if (session.X >= ElmoradDeliveryMinX && session.X <= ElmoradDeliveryMaxX && session.Z >= ElmoradDeliveryMinZ && session.Z <= ElmoradDeliveryMaxZ)
                inBase = true;
        }

        if (!inBase)
            return;

        match.AltarCarrierCharacterId = null;
        match.AltarCarrierNation = null;

        await magicStatusEffectService.CancelAsync(session, FragmentOfManesSkillId);

        int teamCount = session.Nation == AccountNation.Karus ? match.KarusMembers.Count : match.ElmoradMembers.Count;
        if (teamCount <= 0) teamCount = 1;

        int pointsAwarded = PointsPerAltarDelivery * teamCount;

        if (session.Nation == AccountNation.Karus)
            match.KarusScore += pointsAwarded;
        else
            match.ElmoradScore += pointsAwarded;

        var nationName = session.Nation == AccountNation.Karus ? "Karus" : "El Morad";
        await SendNoticeToRoomAsync(match, $"### [Border Defense War] {session.Name} ({nationName}) successfully delivered the Fragment! +{pointsAwarded} points! ###");

        match.AltarRespawnAtUtc = DateTime.UtcNow.AddSeconds(AltarRespawnCooldownSeconds);
        var timerPkt = EventPacketWriter.AltarTimer((ushort)AltarRespawnCooldownSeconds);
        await BroadcastPacketToRoomAsync(match, timerPkt);

        await BroadcastScoresToRoomAsync(match);
        await CheckWinConditionAsync(match);
    }

    public async Task OnPlayerKilledAsync(UserSession victim, UserSession? killer)
    {
        if (victim.ZoneId != BdwZoneId || !_activeMatches.TryGetValue(victim.Room, out var match) || match.IsCompleted)
            return;

        if (match.AltarCarrierCharacterId == victim.CharacterId)
        {
            match.AltarCarrierCharacterId = null;
            match.AltarCarrierNation = null;
            match.AltarRespawnAtUtc = DateTime.UtcNow.AddSeconds(AltarRespawnCooldownSeconds);

            await magicStatusEffectService.CancelAsync(victim, FragmentOfManesSkillId);

            var timerPkt = EventPacketWriter.AltarTimer((ushort)AltarRespawnCooldownSeconds);
            await BroadcastPacketToRoomAsync(match, timerPkt);

            await SendNoticeToRoomAsync(match, $"### [Border Defense War] {victim.Name} dropped the Fragment of Manes! Altar will respawn in 60s. ###");
        }

        if (killer != null && killer.Nation != victim.Nation && killer.Room == match.RoomId)
        {
            int killerTeamCount = killer.Nation == AccountNation.Karus ? match.KarusMembers.Count : match.ElmoradMembers.Count;
            if (killerTeamCount <= 0) killerTeamCount = 1;

            if (killer.Nation == AccountNation.Karus)
            {
                match.KarusKillCount++;
                match.KarusScore += PointsPerKill * killerTeamCount;
            }
            else
            {
                match.ElmoradKillCount++;
                match.ElmoradScore += PointsPerKill * killerTeamCount;
            }

            await BroadcastScoresToRoomAsync(match);
            await CheckWinConditionAsync(match);
        }
    }

    private async Task CheckWinConditionAsync(BdwMatch match)
    {
        if (match.IsFinishing || match.IsCompleted)
            return;

        AccountNation? winner = null;

        if (match.KarusScore >= match.TargetScore)
            winner = AccountNation.Karus;
        else if (match.ElmoradScore >= match.TargetScore)
            winner = AccountNation.ElMorad;

        if (winner.HasValue)
        {
            await TriggerMatchFinishAsync(match, winner.Value);
        }
    }

    private async Task TriggerMatchFinishAsync(BdwMatch match, AccountNation winner)
    {
        if (match.IsFinishing || match.IsCompleted)
            return;

        match.IsFinishing = true;
        match.WinnerNation = winner;
        match.FinishAtUtc = DateTime.UtcNow.AddSeconds(FinishCountdownSeconds);

        var finishPkt = EventPacketWriter.TempleEventFinish((byte)winner, (uint)FinishCountdownSeconds);
        await BroadcastPacketToRoomAsync(match, finishPkt);

        var winnerName = winner == AccountNation.Karus ? "Karus" : "El Morad";
        await SendNoticeToRoomAsync(match, $"### [Border Defense War] The {winnerName} nation has won the match! Teleporting in {FinishCountdownSeconds}s... ###");

        logger.LogInformation("BDW Room {Room} finished! Winner: {Winner}", match.RoomId, winner);
    }

    public Task OnPlayerLeavingZoneAsync(UserSession session, byte oldZone)
    {
        if (oldZone != BdwZoneId)
            return Task.CompletedTask;

        return OnPlayerLogoutOrLeaveAsync(session);
    }

    public async Task OnPlayerLogoutOrLeaveAsync(UserSession session)
    {
        if (session.ZoneId != BdwZoneId || !_activeMatches.TryGetValue(session.Room, out var match) || match.IsCompleted)
            return;

        if (match.AltarCarrierCharacterId == session.CharacterId)
        {
            match.AltarCarrierCharacterId = null;
            match.AltarCarrierNation = null;
            match.AltarRespawnAtUtc = DateTime.UtcNow.AddSeconds(AltarRespawnCooldownSeconds);
            await magicStatusEffectService.CancelAsync(session, FragmentOfManesSkillId);
            var timerPkt = EventPacketWriter.AltarTimer((ushort)AltarRespawnCooldownSeconds);
            await BroadcastPacketToRoomAsync(match, timerPkt);
        }

        match.Participants.Remove(session.CharacterId);
        match.KarusMembers.Remove(session.CharacterId);
        match.ElmoradMembers.Remove(session.CharacterId);

        if (!match.IsFinishing && !match.IsCompleted)
        {
            if (match.KarusMembers.Count == 0 && match.ElmoradMembers.Count > 0)
                await TriggerMatchFinishAsync(match, AccountNation.ElMorad);
            else if (match.ElmoradMembers.Count == 0 && match.KarusMembers.Count > 0)
                await TriggerMatchFinishAsync(match, AccountNation.Karus);
        }
    }

    public async Task TickAsync()
    {
        if (_activeMatches.IsEmpty)
            return;

        var now = DateTime.UtcNow;

        foreach (var match in _activeMatches.Values.ToList())
        {
            if (match.IsCompleted)
                continue;

            if (!match.AltarCarrierCharacterId.HasValue && match.AltarRespawnAtUtc.HasValue && now >= match.AltarRespawnAtUtc.Value)
            {
                match.AltarRespawnAtUtc = null;
                RespawnAltar(match);
                await SendNoticeToRoomAsync(match, "### [Border Defense War] The Altar of Manes has respawned in the center! ###");
            }

            if (match.IsFinishing && match.FinishAtUtc.HasValue && now >= match.FinishAtUtc.Value)
            {
                await FinalizeMatchAndDistributeRewardsAsync(match);
                continue;
            }

            if (!match.IsFinishing && now >= match.MatchEndUtc)
            {
                AccountNation? winner = DetermineWinner(match);

                if (winner.HasValue)
                {
                    await TriggerMatchFinishAsync(match, winner.Value);
                }
                else
                {
                    match.WinnerNation = null;
                    await FinalizeMatchAndDistributeRewardsAsync(match);
                }
            }
        }
    }

    private void RespawnAltar(BdwMatch match)
    {
        if (match.AltarNpc != null)
        {
            match.AltarNpc.Respawn();
            match.AltarNpc.X = AltarDefaultX;
            match.AltarNpc.Z = AltarDefaultZ;

            var spawnPkt = NpcPacketMapper.BuildInOutPacket(match.AltarNpc, InOutType.In);
            _ = sessionManager.Regions.BroadcastFromNpc(match.AltarNpc, spawnPkt);
        }
    }

    private async Task FinalizeMatchAndDistributeRewardsAsync(BdwMatch match)
    {
        match.IsCompleted = true;

        foreach (var charId in match.Participants)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member == null || member.Room != match.RoomId || member.ZoneId != BdwZoneId)
                continue;

            bool isWinner = match.WinnerNation.HasValue && member.Nation == match.WinnerNation.Value;
            var outcome = isWinner ? TempleEventRewardOutcome.Win : TempleEventRewardOutcome.Loss;

            var rewardRows = gameDataService.TempleEventRewards
                .Where(r => r.Event == TempleEvent.BorderDefenseWar
                    && r.Outcome == outcome
                    && ((r.MinLevel == 0 && r.MaxLevel == 0) || (member.Level >= r.MinLevel && (r.MaxLevel == 0 || member.Level <= r.MaxLevel))))
                .ToList();

            foreach (var r in rewardRows)
            {
                if (r.ItemId > 0 && r.ItemCount > 0)
                {
                    await TempleEventHelpers.TryGiveItemAsync(
                        gameDataService, userNotificationService, member, r.ItemId, (ushort)r.ItemCount,
                        "Inventory is full! Could not receive BDW reward item.");
                }

                if (r.LoyaltyPoints > 0)
                {
                    await loyaltyService.ChangeAsync(member, r.LoyaltyPoints);
                }

                if (r.ExpPercent > 0)
                {
                    long baseExp = TempleEventHelpers.CalculateBaseExp(member.Level);
                    long expAward = baseExp * r.ExpPercent / 100;
                    if (expAward > 0)
                        await playerProgressionService.AwardExperienceAsync(member, expAward);
                }
            }

            string resultText = match.WinnerNation.HasValue
                ? (isWinner ? "VICTORY" : "DEFEAT")
                : "DRAW";

            await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)member.Nation,
                $"[Border Defense War] {resultText}! Match finished."));
        }

        await CloseMatchAsync(match);
    }

    private static AccountNation? DetermineWinner(BdwMatch match)
    {
        if (match.KarusScore > match.ElmoradScore)
            return AccountNation.Karus;
        if (match.ElmoradScore > match.KarusScore)
            return AccountNation.ElMorad;
        if (match.KarusKillCount > match.ElmoradKillCount)
            return AccountNation.Karus;
        if (match.ElmoradKillCount > match.KarusKillCount)
            return AccountNation.ElMorad;
        return null;
    }

    public async Task FinishAllMatchesAsync()
    {
        if (_activeMatches.IsEmpty)
            return;

        logger.LogInformation("Finishing all BDW active matches by score ({Count})", _activeMatches.Count);

        foreach (var match in _activeMatches.Values.ToList())
        {
            if (match.IsCompleted)
                continue;

            if (match.WinnerNation.HasValue)
            {
                await FinalizeMatchAndDistributeRewardsAsync(match);
                continue;
            }

            AccountNation? winner = DetermineWinner(match);

            match.WinnerNation = winner;

            if (winner.HasValue)
            {
                var winnerName = winner.Value == AccountNation.Karus ? "Karus" : "El Morad";
                await SendNoticeToRoomAsync(match, $"### [Border Defense War] The {winnerName} nation has won the match! ###");
            }
            else
            {
                await SendNoticeToRoomAsync(match, "### [Border Defense War] The match ended in a DRAW! ###");
            }

            await FinalizeMatchAndDistributeRewardsAsync(match);
        }
    }

    public async Task CancelAllMatchesAsync()
    {
        if (_activeMatches.IsEmpty)
            return;

        logger.LogInformation("Cancelling all BDW active matches ({Count}) without rewards", _activeMatches.Count);

        foreach (var match in _activeMatches.Values.ToList())
        {
            match.IsCompleted = true;
            await CloseMatchAsync(match);
        }

        foreach (var session in sessionManager.GetAll())
        {
            if (session.ZoneId == BdwZoneId)
            {
                try
                {
                    instanceRooms.Leave(session);
                    await zoneTransitionService.ChangeZoneAsync(session, (byte)ZoneId.Moradon, 0f, 0f);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to warp {Name} from BDW zone", session.Name);
                }
            }
        }
    }

    private async Task CloseMatchAsync(BdwMatch match)
    {
        if (match.AltarCarrierCharacterId.HasValue)
        {
            var carrier = sessionManager.GetByCharacterId(match.AltarCarrierCharacterId.Value);
            if (carrier != null)
                await magicStatusEffectService.CancelAsync(carrier, FragmentOfManesSkillId);
            match.AltarCarrierCharacterId = null;
        }

        await TempleEventHelpers.DisbandPartyAsync(sessionManager, combatNotificationService, match.KarusPartyIndex);
        await TempleEventHelpers.DisbandPartyAsync(sessionManager, combatNotificationService, match.ElmoradPartyIndex);

        foreach (var charId in match.Participants)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member != null && member.ZoneId == BdwZoneId && member.Room == match.RoomId)
            {
                try
                {
                    instanceRooms.Leave(member);
                    await zoneTransitionService.ChangeZoneAsync(member, (byte)ZoneId.Moradon, 0f, 0f);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to warp {Name} from BDW room {Room}", member.Name, match.RoomId);
                }
            }
        }

        instanceRooms.Close(match.InstanceRoom);
        _activeMatches.TryRemove(match.RoomId, out _);
    }

    private async Task BroadcastScoresToRoomAsync(BdwMatch match)
    {
        var pkt = EventPacketWriter.TempleScreenScores(match.KarusScore, match.ElmoradScore);
        await BroadcastPacketToRoomAsync(match, pkt);
    }

    private async Task BroadcastPacketToRoomAsync(BdwMatch match, Packet packet)
    {
        await TempleEventHelpers.BroadcastPacketToRoomAsync(
            sessionManager, match.Participants, match.RoomId, BdwZoneId, packet);
    }

    private async Task SendNoticeToRoomAsync(BdwMatch match, string message)
    {
        await TempleEventHelpers.SendNoticeToRoomAsync(
            sessionManager, match.Participants, match.RoomId, BdwZoneId, message);
    }
}
