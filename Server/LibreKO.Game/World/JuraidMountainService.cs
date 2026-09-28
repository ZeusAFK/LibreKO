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
    Task<bool> UnlockBridgeForUserAsync(UserSession session, int trapNumber = 0);
}

public sealed class JuraidMatch
{
    public ushort RoomId { get; set; }
    public short Set { get; set; }
    public InstanceRoom InstanceRoom { get; set; } = null!;
    public HashSet<int> Participants { get; } = [];
    public HashSet<int> KarusMembers { get; } = [];
    public HashSet<int> ElmoradMembers { get; } = [];

    // Stage remaining monsters (tracked by unique NPC ID)
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

    // Bridges indexed by TrapNumber (1..6)
    public Dictionary<int, NpcInstance> BridgesByTrap { get; } = [];

    public NpcInstance? DevabirdNpc { get; set; }
    public bool DevabirdKilled { get; set; }
    public AccountNation? WinnerNation { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed class JuraidMountainService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMonsterAggressionPolicy aggressionPolicy,
    IZoneTransitionService zoneTransitionService,
    InstanceRoomRegistry instanceRooms,
    IUserNotificationService userNotificationService,
    ILoyaltyService loyaltyService,
    ICombatNotificationService combatNotificationService,
    ILogger<JuraidMountainService> logger) : IJuraidMountainService
{
    private const byte JuraidZoneId = (byte)ZoneId.JuradMountain;

    // Start coordinates for Karus and El Morad (Safe platforms at room entrances)
    public const float KarusStartX = 224f;
    public const float KarusStartY = 1.58f;
    public const float KarusStartZ = 272f;

    public const float ElmoradStartX = 800f;
    public const float ElmoradStartY = 0f;
    public const float ElmoradStartZ = 748f;

    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;

    public const int SilveryGemItemId = 389196000;
    public const int BlackGemItemId = 389205000;
    public const int LoyaltyWinBonus = 500;
    public const int MaxPlayersPerNationPerRoom = 8;

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

        // Group into matches of up to 8 Karus and 8 El Morad
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
        logger.LogInformation("Launching instant test Juraid Mountain match for {Name} ({Nation})",
            session.Name, session.Nation);

        await CreateAndLaunchMatchAsync([session], durationSeconds);
    }

    private async Task<JuraidMatch> CreateAndLaunchMatchAsync(List<UserSession> participants, int durationSeconds)
    {
        // Sets 1 to 5 correspond to different monster layouts in NpcPositions.json
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

        // 1. Populate all 35 monsters and classify them into stages
        PopulateMatchMonsters(match);

        // 2. Warp participants into their respective starting positions
        foreach (var session in participants)
        {
            instanceRooms.Join(instanceRoom, session);
            session.InstanceReturn = (session.ZoneId, session.X, session.Z);

            float startX = session.Nation == AccountNation.Karus ? KarusStartX : ElmoradStartX;
            float startZ = session.Nation == AccountNation.Karus ? KarusStartZ : ElmoradStartZ;

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

        // 3. Auto-form nation parties (up to 8 players per party per nation)
        var karusSessions = participants.Where(p => p.Nation == AccountNation.Karus).ToList();
        var elmoSessions = participants.Where(p => p.Nation == AccountNation.ElMorad).ToList();

        match.KarusPartyIndex = await FormNationPartyAsync(match, karusSessions);
        match.ElmoradPartyIndex = await FormNationPartyAsync(match, elmoSessions);

        logger.LogInformation("Juraid Mountain room {Room} launched with set {Set}: {Karus} Karus (Party={KParty}), {Elmo} El Morad (Party={EParty}), {MonsterCount} NPCs spawned",
            match.RoomId, set, match.KarusMembers.Count, match.KarusPartyIndex, match.ElmoradMembers.Count, match.ElmoradPartyIndex, instanceRoom.Npcs.Count);

        return match;
    }

    private void PopulateMatchMonsters(JuraidMatch match)
    {
        var positions = gameDataService.NpcPositions
            .Where(pos => pos.ZoneId == JuraidZoneId && pos.Room == match.Set)
            .ToList();

        if (positions.Count == 0)
        {
            // Fallback to room 1 positions if set has no positions
            positions = gameDataService.NpcPositions
                .Where(pos => pos.ZoneId == JuraidZoneId && pos.Room == 1)
                .ToList();
        }

        // Ensure all 6 bridges (Trap 1..6) are present in the match room
        for (int trap = 1; trap <= 6; trap++)
        {
            if (!positions.Any(p => p.NpcId == 8110 && p.TrapNumber == trap))
            {
                var bridgePos = gameDataService.NpcPositions
                    .FirstOrDefault(p => p.ZoneId == JuraidZoneId && p.NpcId == 8110 && p.TrapNumber == trap);
                if (bridgePos != null)
                    positions.Add(bridgePos);
            }
        }

        foreach (var pos in positions)
        {
            var npcData = gameDataService.GetSpawnProto(pos);
            if (npcData == null)
            {
                logger.LogWarning("Juraid set {Set} names missing NPC {NpcId}", match.Set, pos.NpcId);
                continue;
            }

            var count = pos.NumNPC > 1 ? pos.NumNPC : 1;
            for (var i = 0; i < count; i++)
            {
                var npc = NpcInstance.FromData(npcData, pos, 0);
                npc.Room = match.RoomId;
                npc.RespawnType = NpcRespawnType.Never;

                if (npc.NpcId == 8110)
                {
                    npc.IsAggressive = false;
                    npc.Direction = pos.TrapNumber switch
                    {
                        1 => 0,     // Karus Room 1 -> 2: North
                        2 => 90,    // Karus Room 2 -> 3: East
                        3 => 180,   // Karus Room 3 -> Center: South
                        4 => 180,   // El Morad Room 1 -> 2: South
                        5 => 270,   // El Morad Room 2 -> 3: West
                        6 => 0,     // El Morad Room 3 -> Center: North
                        _ => 0
                    };
                }
                else
                {
                    aggressionPolicy.Apply(npc);
                }

                var height = sessionManager.Maps?.GetHeight(npc.ZoneId, npc.X, npc.Z) ?? 0f;
                npc.Y = height;
                npc.SpawnY = height;

                sessionManager.Regions.SpawnNpc(npc);
                match.InstanceRoom.Npcs.Add(npc);

                ClassifyMonster(match, npc, pos);
            }
        }
    }

    private static void ClassifyMonster(JuraidMatch match, NpcInstance npc, NpcPosData pos)
    {
        if (npc.NpcId == 8106)
        {
            match.DevabirdNpc = npc;
            return;
        }

        if (npc.NpcId == 8110)
        {
            // Bridge of Summoning
            match.BridgesByTrap[pos.TrapNumber] = npc;
            return;
        }

        // Merchants
        if (npc.NpcId is 8111 or 8112 or 8161 or 8162)
            return;

        int x = pos.LeftX;
        int z = pos.TopZ;

        // Karus side
        if (x < 400 && z >= 540 && z <= 650)
            match.KarusStage1NpcIds.Add(npc.UniqueId);
        else if (x < 400 && z >= 800)
            match.KarusStage2NpcIds.Add(npc.UniqueId);
        else if (x >= 400 && x <= 600 && z >= 800)
            match.KarusStage3NpcIds.Add(npc.UniqueId);

        // El Morad side
        else if (x > 600 && z >= 350 && z <= 500)
            match.ElmoradStage1NpcIds.Add(npc.UniqueId);
        else if (x > 600 && z <= 250)
            match.ElmoradStage2NpcIds.Add(npc.UniqueId);
        else if (x >= 400 && x <= 600 && z <= 250)
            match.ElmoradStage3NpcIds.Add(npc.UniqueId);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.ZoneId != JuraidZoneId || npc.Room == 0)
            return;

        if (!_activeMatches.TryGetValue(npc.Room, out var match))
            return;

        if (match.IsCompleted)
            return;

        // 1. Devabird defeat -> match victory
        if (npc.NpcId == 8106)
        {
            await HandleDevabirdKilledAsync(match, killer);
            return;
        }

        // 2. Direct kill of Bridge of Summoning
        if (npc.NpcId == 8110)
        {
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] A bridge barrier has collapsed! ###");
            return;
        }

        // 3. Stage monster kills
        if (match.KarusStage1NpcIds.Remove(npc.UniqueId) && match.KarusStage1NpcIds.Count == 0)
        {
            match.KarusStage = 2;
            await UnlockBridgeAsync(match, 1);
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] Karus has cleared Stage 1! Bridge 1 is now OPEN! ###");
        }
        else if (match.KarusStage2NpcIds.Remove(npc.UniqueId) && match.KarusStage2NpcIds.Count == 0)
        {
            match.KarusStage = 3;
            await UnlockBridgeAsync(match, 2);
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] Karus has cleared Stage 2! Bridge 2 is now OPEN! ###");
        }
        else if (match.KarusStage3NpcIds.Remove(npc.UniqueId) && match.KarusStage3NpcIds.Count == 0)
        {
            match.KarusStage = 4;
            await UnlockBridgeAsync(match, 3);
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] Karus has cleared Stage 3! Central bridge to Devabird is now OPEN! ###");
        }
        else if (match.ElmoradStage1NpcIds.Remove(npc.UniqueId) && match.ElmoradStage1NpcIds.Count == 0)
        {
            match.ElmoradStage = 2;
            await UnlockBridgeAsync(match, 4);
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] El Morad has cleared Stage 1! Bridge 1 is now OPEN! ###");
        }
        else if (match.ElmoradStage2NpcIds.Remove(npc.UniqueId) && match.ElmoradStage2NpcIds.Count == 0)
        {
            match.ElmoradStage = 3;
            await UnlockBridgeAsync(match, 5);
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] El Morad has cleared Stage 2! Bridge 2 is now OPEN! ###");
        }
        else if (match.ElmoradStage3NpcIds.Remove(npc.UniqueId) && match.ElmoradStage3NpcIds.Count == 0)
        {
            match.ElmoradStage = 4;
            await UnlockBridgeAsync(match, 6);
            await SendNoticeToRoomAsync(match, "### [Juraid Mountain] El Morad has cleared Stage 3! Central bridge to Devabird is now OPEN! ###");
        }
    }

    private async Task UnlockBridgeAsync(JuraidMatch match, int trapNumber)
    {
        if (match.BridgesByTrap.TryGetValue(trapNumber, out var bridgeNpc))
        {
            try
            {
                bridgeNpc.GateOpen = true;
                bridgeNpc.Hp = bridgeNpc.MaxHp > 0 ? bridgeNpc.MaxHp : 1000;

                var gatePacket = MiscPacketWriter.ObjectGateFlag(1, bridgeNpc.UniqueId, true);
                var deadPacket = DeathPacketWriter.NpcDeath(bridgeNpc.UniqueId);
                var spawnPacket = NpcPacketMapper.BuildInOutPacket(bridgeNpc, InOutType.In);

                // Broadcast directly to all participants in this match room (regardless of region distance)
                foreach (var charId in match.Participants)
                {
                    var session = sessionManager.GetByCharacterId(charId);
                    if (session != null && session.ZoneId == JuraidZoneId)
                    {
                        await session.Client.SendPacket(gatePacket);
                        await session.Client.SendPacket(deadPacket);
                        await session.Client.SendPacket(spawnPacket);
                    }
                }

                // Also broadcast into surrounding regions
                await sessionManager.Regions.BroadcastFromNpc(bridgeNpc, gatePacket);
                await sessionManager.Regions.BroadcastFromNpc(bridgeNpc, deadPacket);
                await sessionManager.Regions.BroadcastFromNpc(bridgeNpc, spawnPacket);

                logger.LogInformation("Bridge {Trap} unlocked and lowered in Juraid room {Room}", trapNumber, match.RoomId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to unlock bridge trap {Trap} in room {Room}", trapNumber, match.RoomId);
            }
        }
    }

    public async Task<bool> UnlockBridgeForUserAsync(UserSession session, int trapNumber = 0)
    {
        if (session.ZoneId != JuraidZoneId || session.Room == 0)
            return false;

        if (!_activeMatches.TryGetValue(session.Room, out var match))
            return false;

        if (trapNumber > 0)
        {
            await UnlockBridgeAsync(match, trapNumber);
            string side = trapNumber <= 3 ? "Karus" : "El Morad";
            int stage = trapNumber <= 3 ? trapNumber : trapNumber - 3;
            await SendNoticeToRoomAsync(match, $"### [Juraid Mountain] {side} has cleared Stage {stage}! Bridge {stage} is now OPEN! ###");
            return true;
        }

        for (int t = 1; t <= 6; t++)
        {
            await UnlockBridgeAsync(match, t);
        }
        await SendNoticeToRoomAsync(match, "### [Juraid Mountain] All bridges have been OPENED by GM! ###");
        return true;
    }

    private async Task HandleDevabirdKilledAsync(JuraidMatch match, UserSession killer)
    {
        if (match.DevabirdKilled)
            return;

        match.DevabirdKilled = true;
        match.IsCompleted = true;

        var winnerNation = killer.Nation != AccountNation.None ? killer.Nation : AccountNation.Karus;
        match.WinnerNation = winnerNation;

        var winnerName = winnerNation == AccountNation.Karus ? "Karus" : "El Morad";
        var noticePkt = NoticePacketWriter.Broadcast(
            $"### [Juraid Mountain] The {winnerName} nation has slain Devabird and claimed victory! ###");
        await sessionManager.BroadcastToAll(noticePkt);

        // Distribute rewards to participants in this room
        foreach (var charId in match.Participants)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member == null || member.Room != match.RoomId || member.ZoneId != JuraidZoneId)
                continue;

            if (member.Nation == winnerNation)
            {
                await TryGiveItemAsync(member, SilveryGemItemId, 2);
                await loyaltyService.ChangeAsync(member, LoyaltyWinBonus);
                await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)member.Nation,
                    $"[Juraid Mountain] Victory! You have received 2x Silvery Gem and {LoyaltyWinBonus} National Points!"));
            }
            else
            {
                await TryGiveItemAsync(member, BlackGemItemId, 1);
                await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)member.Nation,
                    "[Juraid Mountain] Defeat! You have received 1x Black Gem for your participation."));
            }
        }

        // Schedule delayed return to Moradon after 20 seconds
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(10000);
                await SendNoticeToRoomAsync(match, "### [Juraid Mountain] Returning to Moradon in 10 seconds... ###");
                await Task.Delay(10000);
                await CloseMatchAsync(match);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while finalizing Juraid room {Room}", match.RoomId);
            }
        });
    }

    public async Task CancelAllMatchesAsync()
    {
        if (_activeMatches.IsEmpty)
            return;

        logger.LogInformation("Cancelling / finalizing all active Juraid Mountain matches ({Count})", _activeMatches.Count);

        foreach (var match in _activeMatches.Values.ToList())
        {
            if (!match.IsCompleted)
            {
                match.IsCompleted = true;

                // If Devabird was not killed, check stage progression to determine outcome
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
                        await TryGiveItemAsync(member, SilveryGemItemId, 1);
                        await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                            (byte)member.Nation,
                            "[Juraid Mountain] Time expired! Your nation advanced further and received 1x Silvery Gem!"));
                    }
                    else
                    {
                        await TryGiveItemAsync(member, BlackGemItemId, 1);
                        await member.Client.SendPacket(ChatPacketWriter.SystemNotice(
                            (byte)member.Nation,
                            "[Juraid Mountain] Event ended! You received 1x Black Gem for participating."));
                    }
                }
            }

            await CloseMatchAsync(match);
        }

        // Sweep any remaining players who are still in Juraid Mountain back to Moradon
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


    private async Task CloseMatchAsync(JuraidMatch match)
    {
        // Disband auto-parties upon match conclusion
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

        // Ensure participants leave any prior party from before entering
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

        // Broadcast member info to everyone in the party so the party HUD displays all members
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
