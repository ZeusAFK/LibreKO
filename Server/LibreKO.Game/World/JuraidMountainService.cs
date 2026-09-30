using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IJuraidMountainService
{
    bool HasActiveMatches { get; }
    Task StartMatchesAsync(IReadOnlyList<int> participantCharacterIds, int durationSeconds);
    Task StartMatchForCallerAsync(UserSession session, int durationSeconds = TempleEventRules.JuraidMountainDurationSeconds);
    Task OnNpcKilledAsync(NpcInstance npc, UserSession killer);
    Task CancelAllMatchesAsync();
    Task TickAsync();
}

public sealed class JuraidMatch
{
    public ushort RoomId { get; set; }
    public short Set { get; set; }
    public InstanceRoom InstanceRoom { get; set; } = null!;
    public HashSet<int> Participants { get; } = [];
    public HashSet<int> KarusMembers { get; } = [];
    public HashSet<int> ElmoradMembers { get; } = [];

    public HashSet<int> KarusStage1NpcIds { get; } = [];
    public HashSet<int> KarusStage2NpcIds { get; } = [];
    public HashSet<int> KarusStage3NpcIds { get; } = [];

    public HashSet<int> ElmoradStage1NpcIds { get; } = [];
    public HashSet<int> ElmoradStage2NpcIds { get; } = [];
    public HashSet<int> ElmoradStage3NpcIds { get; } = [];

    public int KarusStage { get; set; } = 1;
    public int ElmoradStage { get; set; } = 1;

    public int KarusPartyIndex { get; set; } = -1;
    public int ElmoradPartyIndex { get; set; } = -1;

    public Dictionary<int, NpcInstance> BridgesByTrap { get; } = [];

    public NpcInstance? DevabirdNpc { get; set; }
    public bool DevabirdKilled { get; set; }
    public AccountNation? WinnerNation { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public bool ReturnNoticeSent { get; set; }
}

public sealed class JuraidMountainService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IZoneTransitionService zoneTransitionService,
    InstanceRoomRegistry instanceRooms,
    IInstanceEntryService instanceEntryService,
    IUserNotificationService userNotificationService,
    ILoyaltyService loyaltyService,
    ICombatNotificationService combatNotificationService,
    ILogger<JuraidMountainService> logger) : IJuraidMountainService
{
    private const byte JuraidZoneId = (byte)ZoneId.JuradMountain;

    public const int DevabirdNpcId = 8106;
    public const int BridgeNpcId = 8110;
    public const int KarusMerchantNpcId1 = 8111;
    public const int KarusMerchantNpcId2 = 8112;
    public const int ElmoradMerchantNpcId1 = 8161;
    public const int ElmoradMerchantNpcId2 = 8162;

    public const byte BridgeStatusLowered = 2;

    private const int KarusWestMaxX = 400;
    private const int KarusStage1MinZ = 540;
    private const int KarusStage1MaxZ = 650;
    private const int KarusStage2MinZ = 800;

    private const int ElmoradEastMinX = 600;
    private const int ElmoradStage1MinZ = 350;
    private const int ElmoradStage1MaxZ = 500;
    private const int ElmoradStage2MaxZ = 250;

    private const int CentralMinX = 400;
    private const int CentralMaxX = 600;

    private const float DefaultKarusStartX = 224f;
    private const float DefaultKarusStartZ = 272f;
    private const float DefaultElmoradStartX = 800f;
    private const float DefaultElmoradStartZ = 748f;

    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;

    public const int MaxPlayersPerNationPerRoom = 8;

    public const int FinishedMatchClosureDelaySeconds = 20;
    public const int FinishedMatchWarningDelaySeconds = 10;

    private readonly ConcurrentDictionary<ushort, JuraidMatch> _activeMatches = new();
    private int _nextSetCounter;

    public bool HasActiveMatches => !_activeMatches.IsEmpty;

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

        logger.LogInformation("Starting Juraid Mountain matches: {KarusCount} Karus, {ElmoCount} El Morad",
            karusSessions.Count, elmoSessions.Count);

        int karusIdx = 0;
        int elmoIdx = 0;

        while (karusIdx < karusSessions.Count || elmoIdx < elmoSessions.Count)
        {
            var matchKarus = karusSessions.Skip(karusIdx).Take(MaxPlayersPerNationPerRoom).ToList();
            var matchElmo = elmoSessions.Skip(elmoIdx).Take(MaxPlayersPerNationPerRoom).ToList();

            karusIdx += matchKarus.Count;
            elmoIdx += matchElmo.Count;

            var matchParticipants = matchKarus.Concat(matchElmo).ToList();
            if (matchParticipants.Count == 0)
                break;

            await CreateAndLaunchMatchAsync(matchParticipants, durationSeconds);
        }
    }

    public async Task StartMatchForCallerAsync(UserSession session, int durationSeconds = TempleEventRules.JuraidMountainDurationSeconds)
    {
        var juraidSchedules = gameDataService.TempleEventSchedules?.Where(s => s.Event == TempleEvent.JuraidMountain).ToList();
        var minLevel = juraidSchedules != null && juraidSchedules.Count > 0
            ? juraidSchedules.Min(s => s.MinLevel)
            : TempleEventRules.JuraidMountainDefaultMinLevel;

        if (session.Level < minLevel)
        {
            var noticePkt = ChatPacketWriter.SystemNotice((byte)session.Nation, $"You must be at least level {minLevel} to enter Juraid Mountain.");
            await session.Client.SendPacket(noticePkt);
            return;
        }

        logger.LogInformation("Launching instant test Juraid Mountain match for {Name} ({Nation})",
            session.Name, session.Nation);

        await CreateAndLaunchMatchAsync([session], durationSeconds);
    }

    private async Task<JuraidMatch> CreateAndLaunchMatchAsync(List<UserSession> participants, int durationSeconds)
    {
        short set = (short)((Interlocked.Increment(ref _nextSetCounter) - 1) % 5 + 1);

        var duration = TimeSpan.FromSeconds(durationSeconds > 0 ? durationSeconds : TempleEventRules.JuraidMountainDurationSeconds);
        var instanceRoom = instanceRooms.Open(JuraidZoneId, set, duration);

        var match = new JuraidMatch
        {
            RoomId = instanceRoom.Id,
            Set = set,
            InstanceRoom = instanceRoom,
        };

        foreach (var p in participants)
        {
            match.Participants.Add(p.CharacterId);
            if (p.Nation == AccountNation.Karus)
                match.KarusMembers.Add(p.CharacterId);
            else if (p.Nation == AccountNation.ElMorad)
                match.ElmoradMembers.Add(p.CharacterId);
        }

        _activeMatches[match.RoomId] = match;

        instanceEntryService.Populate(match.InstanceRoom);
        foreach (var npc in match.InstanceRoom.Npcs)
        {
            ClassifyMonster(match, npc);
        }

        var startPos = gameDataService.GetStartPosition(JuraidZoneId);

        foreach (var session in participants)
        {
            instanceRooms.Join(instanceRoom, session);
            session.InstanceReturn = (session.ZoneId, session.X, session.Z);

            var (startX, startZ) = startPos != null
                ? startPos.RandomSpawn(session.Nation)
                : (session.Nation == AccountNation.Karus
                    ? (DefaultKarusStartX, DefaultKarusStartZ)
                    : (DefaultElmoradStartX, DefaultElmoradStartZ));

            try
            {
                await zoneTransitionService.ChangeZoneAsync(session, JuraidZoneId, startX, startZ);
                await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)session.Nation,
                    "### [Juraid Mountain] Match started! Clear all monsters in each room to open the bridges to the center! ###"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp {Name} to Juraid room {Room}", session.Name, match.RoomId);
            }
        }

        var karusSessions = participants.Where(p => p.Nation == AccountNation.Karus).ToList();
        var elmoSessions = participants.Where(p => p.Nation == AccountNation.ElMorad).ToList();

        match.KarusPartyIndex = await FormNationPartyAsync(match, karusSessions);
        match.ElmoradPartyIndex = await FormNationPartyAsync(match, elmoSessions);

        logger.LogInformation("Juraid Mountain room {Room} launched with set {Set}: {Karus} Karus (Party={KParty}), {Elmo} El Morad (Party={EParty}), {MonsterCount} NPCs spawned",
            match.RoomId, set, match.KarusMembers.Count, match.KarusPartyIndex, match.ElmoradMembers.Count, match.ElmoradPartyIndex, instanceRoom.Npcs.Count);

        return match;
    }

    private static void ClassifyMonster(JuraidMatch match, NpcInstance npc)
    {
        if (npc.NpcId == DevabirdNpcId)
        {
            match.DevabirdNpc = npc;
            return;
        }

        if (npc.NpcId == BridgeNpcId)
        {
            match.BridgesByTrap[npc.TrapNumber] = npc;
            return;
        }

        if (npc.NpcId is KarusMerchantNpcId1 or KarusMerchantNpcId2 or ElmoradMerchantNpcId1 or ElmoradMerchantNpcId2)
            return;

        float x = npc.X;
        float z = npc.Z;

        if (x < KarusWestMaxX && z >= KarusStage1MinZ && z <= KarusStage1MaxZ)
            match.KarusStage1NpcIds.Add(npc.UniqueId);
        else if (x < KarusWestMaxX && z >= KarusStage2MinZ)
            match.KarusStage2NpcIds.Add(npc.UniqueId);
        else if (x >= CentralMinX && x <= CentralMaxX && z >= KarusStage2MinZ)
            match.KarusStage3NpcIds.Add(npc.UniqueId);
        else if (x > ElmoradEastMinX && z >= ElmoradStage1MinZ && z <= ElmoradStage1MaxZ)
            match.ElmoradStage1NpcIds.Add(npc.UniqueId);
        else if (x > ElmoradEastMinX && z <= ElmoradStage2MaxZ)
            match.ElmoradStage2NpcIds.Add(npc.UniqueId);
        else if (x >= CentralMinX && x <= CentralMaxX && z <= ElmoradStage2MaxZ)
            match.ElmoradStage3NpcIds.Add(npc.UniqueId);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.ZoneId != JuraidZoneId || npc.Room == 0)
            return;

        if (!_activeMatches.TryGetValue(npc.Room, out var match))
            return;

        int bridgeToUnlock = 0;
        string? noticeMessage = null;
        bool devabirdTriggered = false;

        lock (match)
        {
            if (match.IsCompleted)
                return;

            if (npc.NpcId == DevabirdNpcId)
            {
                if (!match.DevabirdKilled)
                {
                    match.DevabirdKilled = true;
                    match.IsCompleted = true;
                    match.FinishedAtUtc = DateTime.UtcNow;
                    devabirdTriggered = true;
                }
            }
            else if (npc.NpcId == BridgeNpcId)
            {
                noticeMessage = "### [Juraid Mountain] A bridge barrier has collapsed! ###";
            }
            else
            {
                if (match.KarusStage1NpcIds.Remove(npc.UniqueId) && match.KarusStage1NpcIds.Count == 0)
                {
                    match.KarusStage = 2;
                    bridgeToUnlock = 1;
                    noticeMessage = "### [Juraid Mountain] Karus has cleared Stage 1! Bridge 1 is now OPEN! ###";
                }
                else if (match.KarusStage2NpcIds.Remove(npc.UniqueId) && match.KarusStage2NpcIds.Count == 0)
                {
                    match.KarusStage = 3;
                    bridgeToUnlock = 2;
                    noticeMessage = "### [Juraid Mountain] Karus has cleared Stage 2! Bridge 2 is now OPEN! ###";
                }
                else if (match.KarusStage3NpcIds.Remove(npc.UniqueId) && match.KarusStage3NpcIds.Count == 0)
                {
                    match.KarusStage = 4;
                    bridgeToUnlock = 3;
                    noticeMessage = "### [Juraid Mountain] Karus has cleared Stage 3! Central bridge to Devabird is now OPEN! ###";
                }
                else if (match.ElmoradStage1NpcIds.Remove(npc.UniqueId) && match.ElmoradStage1NpcIds.Count == 0)
                {
                    match.ElmoradStage = 2;
                    bridgeToUnlock = 4;
                    noticeMessage = "### [Juraid Mountain] El Morad has cleared Stage 1! Bridge 1 is now OPEN! ###";
                }
                else if (match.ElmoradStage2NpcIds.Remove(npc.UniqueId) && match.ElmoradStage2NpcIds.Count == 0)
                {
                    match.ElmoradStage = 3;
                    bridgeToUnlock = 5;
                    noticeMessage = "### [Juraid Mountain] El Morad has cleared Stage 2! Bridge 2 is now OPEN! ###";
                }
                else if (match.ElmoradStage3NpcIds.Remove(npc.UniqueId) && match.ElmoradStage3NpcIds.Count == 0)
                {
                    match.ElmoradStage = 4;
                    bridgeToUnlock = 6;
                    noticeMessage = "### [Juraid Mountain] El Morad has cleared Stage 3! Central bridge to Devabird is now OPEN! ###";
                }
            }
        }

        if (devabirdTriggered)
        {
            await HandleDevabirdKilledAsync(match, killer);
            return;
        }

        if (bridgeToUnlock > 0)
        {
            await UnlockBridgeAsync(match, bridgeToUnlock);
        }

        if (noticeMessage != null)
        {
            await SendNoticeToRoomAsync(match, noticeMessage);
        }
    }

    private async Task UnlockBridgeAsync(JuraidMatch match, int trapNumber)
    {
        if (match.BridgesByTrap.TryGetValue(trapNumber, out var bridgeNpc))
        {
            try
            {
                bridgeNpc.GateOpen = BridgeStatusLowered;

                var outPacket = NpcPacketMapper.BuildInOutPacket(bridgeNpc, InOutType.Out);
                var inPacket = NpcPacketMapper.BuildInOutPacket(bridgeNpc, InOutType.In);

                foreach (var charId in match.Participants)
                {
                    var session = sessionManager.GetByCharacterId(charId);
                    if (session != null && session.ZoneId == JuraidZoneId)
                    {
                        await session.Client.SendPacket(outPacket);
                        await session.Client.SendPacket(inPacket);
                    }
                }

                await sessionManager.Regions.BroadcastFromNpc(bridgeNpc, outPacket);
                await sessionManager.Regions.BroadcastFromNpc(bridgeNpc, inPacket);

                logger.LogInformation("Bridge {Trap} unlocked and lowered in Juraid room {Room}", trapNumber, match.RoomId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to unlock bridge trap {Trap} in room {Room}", trapNumber, match.RoomId);
            }
        }
    }

    private TempleEventRewardData? GetReward(TempleEventRewardOutcome outcome)
    {
        return gameDataService.TempleEventRewards?.FirstOrDefault(r => r.Event == TempleEvent.JuraidMountain && r.Outcome == outcome);
    }

    private async Task HandleDevabirdKilledAsync(JuraidMatch match, UserSession killer)
    {
        var winnerNation = killer.Nation != AccountNation.None ? killer.Nation : AccountNation.Karus;
        match.WinnerNation = winnerNation;

        var winnerName = winnerNation == AccountNation.Karus ? "Karus" : "El Morad";
        var noticePkt = NoticePacketWriter.Broadcast(
            $"### [Juraid Mountain] The {winnerName} nation has slain Devabird and claimed victory! ###");
        await sessionManager.BroadcastToAll(noticePkt);

        var winReward = GetReward(TempleEventRewardOutcome.Win);
        var lossReward = GetReward(TempleEventRewardOutcome.Loss);

        foreach (var charId in match.Participants)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member == null || member.Room != match.RoomId || member.ZoneId != JuraidZoneId)
                continue;

            if (member.Nation == winnerNation)
            {
                if (winReward != null)
                {
                    if (winReward.ItemId > 0 && winReward.ItemCount > 0)
                        await TryGiveItemAsync(member, winReward.ItemId, (ushort)winReward.ItemCount);
                    if (winReward.LoyaltyPoints > 0)
                        await loyaltyService.ChangeAsync(member, winReward.LoyaltyPoints);

                    await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)member.Nation,
                        $"[Juraid Mountain] Victory! You have received {winReward.ItemCount}x {gameDataService.GetItem(winReward.ItemId)?.Name ?? "Gem"} and {winReward.LoyaltyPoints} National Points!"));
                }
            }
            else
            {
                if (lossReward != null)
                {
                    if (lossReward.ItemId > 0 && lossReward.ItemCount > 0)
                        await TryGiveItemAsync(member, lossReward.ItemId, (ushort)lossReward.ItemCount);
                    if (lossReward.LoyaltyPoints > 0)
                        await loyaltyService.ChangeAsync(member, lossReward.LoyaltyPoints);

                    await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)member.Nation,
                        $"[Juraid Mountain] Defeat! You have received {lossReward.ItemCount}x {gameDataService.GetItem(lossReward.ItemId)?.Name ?? "Gem"} for your participation."));
                }
            }
        }
    }

    public async Task CancelAllMatchesAsync()
    {
        if (_activeMatches.IsEmpty)
            return;

        logger.LogInformation("Cancelling / finalizing all active Juraid Mountain matches ({Count})", _activeMatches.Count);

        var timeoutWinReward = GetReward(TempleEventRewardOutcome.TimeoutWin);
        var timeoutReward = GetReward(TempleEventRewardOutcome.Timeout);

        foreach (var match in _activeMatches.Values.ToList())
        {
            lock (match)
            {
                if (match.IsCompleted)
                    continue;

                match.IsCompleted = true;
            }

            AccountNation? outcomeNation = null;
            if (match.KarusStage > match.ElmoradStage)
                outcomeNation = AccountNation.Karus;
            else if (match.ElmoradStage > match.KarusStage)
                outcomeNation = AccountNation.ElMorad;

            foreach (var charId in match.Participants)
            {
                var member = sessionManager.GetByCharacterId(charId);
                if (member == null || member.Room != match.RoomId || member.ZoneId != JuraidZoneId)
                    continue;

                if (outcomeNation != null && member.Nation == outcomeNation)
                {
                    if (timeoutWinReward != null)
                    {
                        if (timeoutWinReward.ItemId > 0 && timeoutWinReward.ItemCount > 0)
                            await TryGiveItemAsync(member, timeoutWinReward.ItemId, (ushort)timeoutWinReward.ItemCount);
                        if (timeoutWinReward.LoyaltyPoints > 0)
                            await loyaltyService.ChangeAsync(member, timeoutWinReward.LoyaltyPoints);

                        await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                            (byte)member.Nation,
                            $"[Juraid Mountain] Time expired! Your nation advanced further and received {timeoutWinReward.ItemCount}x {gameDataService.GetItem(timeoutWinReward.ItemId)?.Name ?? "Gem"}!"));
                    }
                }
                else
                {
                    if (timeoutReward != null)
                    {
                        if (timeoutReward.ItemId > 0 && timeoutReward.ItemCount > 0)
                            await TryGiveItemAsync(member, timeoutReward.ItemId, (ushort)timeoutReward.ItemCount);
                        if (timeoutReward.LoyaltyPoints > 0)
                            await loyaltyService.ChangeAsync(member, timeoutReward.LoyaltyPoints);

                        await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                            (byte)member.Nation,
                            $"[Juraid Mountain] Event ended! You received {timeoutReward.ItemCount}x {gameDataService.GetItem(timeoutReward.ItemId)?.Name ?? "Gem"} for participating."));
                    }
                }
            }

            await CloseMatchAsync(match);
        }

        foreach (var session in sessionManager.GetAll())
        {
            if (session.ZoneId == JuraidZoneId)
            {
                try
                {
                    instanceRooms.Leave(session);
                    await zoneTransitionService.ChangeZoneAsync(session, (byte)ZoneId.Moradon, MoradonTownX, MoradonTownZ);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to warp {Name} from Juraid zone", session.Name);
                }
            }
        }
    }

    public async Task TickAsync()
    {
        if (_activeMatches.IsEmpty)
            return;

        var now = DateTime.UtcNow;
        foreach (var match in _activeMatches.Values.ToList())
        {
            if (!match.IsCompleted || match.FinishedAtUtc == null)
                continue;

            var elapsed = now - match.FinishedAtUtc.Value;
            if (!match.ReturnNoticeSent && elapsed >= TimeSpan.FromSeconds(FinishedMatchWarningDelaySeconds))
            {
                match.ReturnNoticeSent = true;
                await SendNoticeToRoomAsync(match, "### [Juraid Mountain] Returning to Moradon in 10 seconds... ###");
            }

            if (elapsed >= TimeSpan.FromSeconds(FinishedMatchClosureDelaySeconds))
            {
                await CloseMatchAsync(match);
            }
        }
    }

    private async Task CloseMatchAsync(JuraidMatch match)
    {
        await DisbandPartyAsync(match.KarusPartyIndex);
        await DisbandPartyAsync(match.ElmoradPartyIndex);

        foreach (var charId in match.Participants)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member != null && member.ZoneId == JuraidZoneId && member.Room == match.RoomId)
            {
                try
                {
                    instanceRooms.Leave(member);
                    await zoneTransitionService.ChangeZoneAsync(member, (byte)ZoneId.Moradon, MoradonTownX, MoradonTownZ);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to warp {Name} from Juraid room {Room}", member.Name, match.RoomId);
                }
            }
        }

        instanceRooms.Close(match.InstanceRoom);
        _activeMatches.TryRemove(match.RoomId, out _);
    }

    private async Task SendNoticeToRoomAsync(JuraidMatch match, string message)
    {
        var packet = NoticePacketWriter.Broadcast(message);
        var chatPacket = ChatPacketWriter.SystemNotice(0, message);
        foreach (var charId in match.Participants)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member != null && member.Room == match.RoomId && member.ZoneId == JuraidZoneId)
            {
                await member.Client.SendPacket(packet);
                await member.Client.SendPacket(chatPacket);
            }
        }
    }

    private async Task<bool> TryGiveItemAsync(UserSession session, int itemId, ushort count)
    {
        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
            return false;

        var (success, slotIndex, durability, isNew) = session.WithLock(s =>
        {
            var idx = s.FindSlotForItem(itemId, gameDataService, count);
            if (idx < 0)
                return (false, 0, (short)0, false);

            var slot = s.Inventory[idx];
            bool isNewSlot = slot.IsEmpty;
            slot.ItemId = itemId;
            slot.Count += count;
            if (isNewSlot)
                slot.Durability = itemData.Duration;

            return (true, idx, slot.Durability, isNewSlot);
        });

        if (!success)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "Inventory is full! Could not receive event reward gem."));
            return false;
        }

        await userNotificationService.SendStackChangeAsync(
            session, (byte)slotIndex, itemId, session.Inventory[slotIndex].Count, durability, isNew);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
        return true;
    }

    private async Task<int> FormNationPartyAsync(JuraidMatch match, List<UserSession> sessions)
    {
        if (sessions.Count < 2)
            return -1;

        foreach (var session in sessions)
        {
            await LeavePreviousPartyIfAnyAsync(session);
        }

        var leader = sessions[0];
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;

        for (int i = 1; i < sessions.Count && i < PartyGroup.MaxMembers; i++)
        {
            var member = sessions[i];
            party.MemberIds[i] = (short)member.CharacterId;
            member.PartyIndex = party.Index;
            member.IsPartyLeader = false;
        }

        for (int i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.MemberIds[i] < 0)
                continue;

            var memberSession = sessionManager.GetByCharacterId(party.MemberIds[i]);
            if (memberSession == null)
                continue;

            byte statusCode = (memberSession == leader)
                ? PartyPacketWriter.MemberPromotedToLeader
                : PartyPacketWriter.MemberJoined;

            var memberPacket = PartyPacketWriter.MemberInfo(MemberStateOf(memberSession), statusCode);
            await combatNotificationService.SendToPartyAsync(party, memberPacket);
        }

        var partyCount = party.MemberCount;
        var noticePacket = ChatPacketWriter.SystemNotice(
            (byte)leader.Nation,
            $"[Juraid Mountain] Party formed with {partyCount} members! Leader: {leader.Name}");

        await combatNotificationService.SendToPartyAsync(party, noticePacket);

        logger.LogInformation("Formed Juraid Mountain auto-party {PartyIndex} for {Nation} in room {Room} with {Count} members (Leader: {Leader})",
            party.Index, leader.Nation, match.RoomId, partyCount, leader.Name);

        return party.Index;
    }

    private static PartyPacketWriter.MemberState MemberStateOf(UserSession session) => new(
        session.CharacterId,
        session.Name,
        session.Level,
        session.Class,
        session.MaxHp,
        session.Hp,
        session.MaxMp,
        session.Mp);

    private async Task LeavePreviousPartyIfAnyAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        session.PartyIndex = -1;
        session.IsPartyLeader = false;

        if (party == null)
            return;

        if (party.MemberCount <= 2 || party.LeaderId == (short)session.CharacterId)
        {
            var deletePacket = PartyPacketWriter.Disband();
            await combatNotificationService.SendToPartyAsync(party, deletePacket);
            for (var i = 0; i < PartyGroup.MaxMembers; i++)
            {
                if (party.MemberIds[i] < 0)
                    continue;

                var m = sessionManager.GetByCharacterId(party.MemberIds[i]);
                if (m != null)
                {
                    m.PartyIndex = -1;
                    m.IsPartyLeader = false;
                }
            }
            sessionManager.Parties.DeleteParty(party.Index);
        }
        else
        {
            var pos = party.FindMember((short)session.CharacterId);
            if (pos >= 0)
            {
                party.MemberIds[pos] = -1;
                var removePkt = PartyPacketWriter.MemberLeft(session.CharacterId);
                await combatNotificationService.SendToPartyAsync(party, removePkt);
            }
        }
    }

    private async Task DisbandPartyAsync(int partyIndex)
    {
        if (partyIndex <= 0)
            return;

        var party = sessionManager.Parties.GetParty(partyIndex);
        if (party == null)
            return;

        logger.LogInformation("Disbanding Juraid Mountain party {PartyIndex}", partyIndex);

        var deletePacket = PartyPacketWriter.Disband();
        await combatNotificationService.SendToPartyAsync(party, deletePacket);

        for (var i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.MemberIds[i] < 0)
                continue;

            var member = sessionManager.GetByCharacterId(party.MemberIds[i]);
            if (member != null)
            {
                member.PartyIndex = -1;
                member.IsPartyLeader = false;
            }
        }

        sessionManager.Parties.DeleteParty(partyIndex);
    }
}
