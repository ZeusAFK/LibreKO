using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public static class TempleEventHelpers
{
    public static async Task<int> FormNationPartyAsync(
        SessionManager sessionManager,
        ICombatNotificationService combatNotificationService,
        List<UserSession> sessions,
        string eventName,
        ushort roomId,
        ILogger logger)
    {
        if (sessions.Count < 2)
            return -1;

        foreach (var session in sessions)
        {
            await LeavePreviousPartyIfAnyAsync(sessionManager, combatNotificationService, session);
        }

        var leader = sessions[0];
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;

        for (var i = 1; i < sessions.Count && i < PartyGroup.MaxMembers; i++)
        {
            var member = sessions[i];
            party.MemberIds[i] = (short)member.CharacterId;
            member.PartyIndex = party.Index;
            member.IsPartyLeader = false;
        }

        for (var i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.MemberIds[i] < 0)
                continue;

            var memberSession = sessionManager.GetByCharacterId(party.MemberIds[i]);
            if (memberSession == null)
                continue;

            byte statusCode = memberSession == leader
                ? PartyPacketWriter.MemberPromotedToLeader
                : PartyPacketWriter.MemberJoined;

            var memberPacket = PartyPacketWriter.MemberInfo(MemberStateOf(memberSession), statusCode);
            await combatNotificationService.SendToPartyAsync(party, memberPacket);
        }

        var partyCount = party.MemberCount;
        var noticePacket = ChatPacketWriter.SystemNotice(
            (byte)leader.Nation,
            $"[{eventName}] Party formed with {partyCount} members! Leader: {leader.Name}");

        await combatNotificationService.SendToPartyAsync(party, noticePacket);

        logger.LogInformation("Formed {Event} auto-party {PartyIndex} for {Nation} in room {Room} with {Count} members (Leader: {Leader})",
            eventName, party.Index, leader.Nation, roomId, partyCount, leader.Name);

        return party.Index;
    }

    public static PartyPacketWriter.MemberState MemberStateOf(UserSession session) => new(
        session.CharacterId,
        session.Name,
        session.Level,
        session.Class,
        session.MaxHp,
        session.Hp,
        session.MaxMp,
        session.Mp);

    public static async Task LeavePreviousPartyIfAnyAsync(
        SessionManager sessionManager,
        ICombatNotificationService combatNotificationService,
        UserSession session)
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

    public static async Task DisbandPartyAsync(
        SessionManager sessionManager,
        ICombatNotificationService combatNotificationService,
        int partyIndex)
    {
        if (partyIndex <= 0)
            return;

        var party = sessionManager.Parties.GetParty(partyIndex);
        if (party == null)
            return;

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

    public static async Task<bool> TryGiveItemAsync(
        IGameDataService gameDataService,
        IUserNotificationService userNotificationService,
        UserSession session,
        int itemId,
        ushort count,
        string fullInventoryNotice = "Inventory is full! Could not receive event reward.")
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
                (byte)session.Nation, fullInventoryNotice));
            return false;
        }

        await userNotificationService.SendStackChangeAsync(
            session, (byte)slotIndex, itemId, session.Inventory[slotIndex].Count, durability, isNew);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
        return true;
    }

    public static async Task SendNoticeToRoomAsync(
        SessionManager sessionManager,
        IEnumerable<int> participantCharacterIds,
        ushort roomId,
        byte zoneId,
        string message)
    {
        var packet = NoticePacketWriter.Broadcast(message);
        var chatPacket = ChatPacketWriter.SystemNotice(0, message);
        foreach (var charId in participantCharacterIds)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member != null && member.Room == roomId && member.ZoneId == zoneId)
            {
                await member.Client.SendPacket(packet);
                await member.Client.SendPacket(chatPacket);
            }
        }
    }

    public static async Task BroadcastPacketToRoomAsync(
        SessionManager sessionManager,
        IEnumerable<int> participantCharacterIds,
        ushort roomId,
        byte zoneId,
        Packet packet)
    {
        foreach (var charId in participantCharacterIds)
        {
            var member = sessionManager.GetByCharacterId(charId);
            if (member != null && member.Room == roomId && member.ZoneId == zoneId)
            {
                await member.Client.SendPacket(packet);
            }
        }
    }

    public const byte ExpHighLevelBandStart = 58;
    public const byte ExpMinRewardLevel = 20;
    public const long ExpLowBandBaseConstant = 3000L;
    public const long ExpLowBandMultiplier = 200000L; // 100L * 2000L
    public const long ExpHighBandLevelOffset = 55L;
    public const long ExpHighBandBaseConstant = 20000L;
    public const long ExpHighBandMultiplier = 100000L; // 100L * 1000L
    public const long ExpFallbackMinimum = 50000L;

    public static long CalculateBaseExp(byte level)
    {
        long baseExp;
        if (level < ExpHighLevelBandStart)
        {
            byte effectiveLevel = Math.Max(ExpMinRewardLevel, level);
            baseExp = (effectiveLevel - ExpMinRewardLevel) * (ExpLowBandBaseConstant + ExpLowBandMultiplier);
        }
        else
        {
            baseExp = (level + ExpHighBandLevelOffset) * (ExpHighBandBaseConstant + ExpHighBandMultiplier);
        }

        return baseExp > 0 ? baseExp : ExpFallbackMinimum;
    }
}


