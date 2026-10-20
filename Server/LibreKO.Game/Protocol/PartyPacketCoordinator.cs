using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IPartyPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task RemoveMemberAsync(UserSession session, short memberId, bool deliberate = true);
    Task LeaveGameAsync(UserSession session);
    Task BoardInviteAsync(UserSession inviter, string targetName, byte boardType);
    Task BoardAcceptAsync(UserSession target, string inviterName, byte boardType);
}

public class PartyPacketCoordinator(
    SessionManager sessionManager,
    ICombatNotificationService combatNotificationService,
    IPartyWarBoardService partyWarBoard,
    IPartyUserVoteService userVote,
    IPartySignalService partySignal,
    IPartyLevelProgressService partyLevelProgress,
    ILogger<PartyPacketCoordinator> logger) : IPartyPacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var subOpcode = packet.ReadByte();
        switch ((PartyRequest)subOpcode)
        {
            case PartyRequest.Create:
            case PartyRequest.Insert:
                await HandleCreateOrInsertAsync(session, packet, subOpcode);
                break;

            case PartyRequest.Permit:
            {
                if (packet.RemainingBytes < 1)
                    break;

                if (packet.ReadByte() != 0)
                    await AcceptInviteAsync(session);
                else
                    await CancelAsync(session);
                break;
            }

            case PartyRequest.Remove:
                if (packet.RemainingBytes >= 4)
                    await RemoveMemberAsync(session, (short)packet.ReadInt());
                break;

            case PartyRequest.Delete:
                await DeleteAsync(session);
                break;

            case PartyRequest.Promote:
                if (packet.RemainingBytes >= 4)
                    await PromoteAsync(session, (short)packet.ReadInt());
                break;

            case PartyRequest.CommandPromote:
                await HandleCommandPromoteAsync(session, packet);
                break;

            case PartyRequest.CommanderMark:
                await HandleCommanderMarkAsync(session, packet);
                break;

            case PartyRequest.UserVote:
                if (packet.RemainingBytes >= 5)
                {
                    int target = packet.ReadInt();
                    byte reason = packet.ReadByte();
                    string text = reason == PartyUserVoteRules.ReasonOther && packet.RemainingBytes >= 2 ? packet.ReadString() : "";
                    await userVote.StartAsync(session, target, reason, text);
                }
                break;

            case PartyRequest.UserVoteAnswer:
                if (packet.RemainingBytes >= 1)
                    await userVote.AnswerAsync(session, packet.ReadByte());
                break;

            case PartyRequest.CommanderOrder:
                await HandleCommanderOrderAsync(session, packet);
                break;

            case PartyRequest.WarBoard:
                if (packet.RemainingBytes >= 2)
                    await partyWarBoard.SetModeAsync(session, packet.ReadByte(), packet.ReadByte());
                break;

            case PartyRequest.SignalEcho:
                if (packet.RemainingBytes >= sizeof(uint))
                    partySignal.Echo(session, packet.ReadUInt());
                break;
        }
    }

    private async Task HandleCommandPromoteAsync(UserSession session, Packet packet)
    {
        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null || party.LeaderId != (short)session.CharacterId)
            return;

        var now = DateTime.UtcNow;
        if ((now - session.LastPartySignalTime).TotalMilliseconds < 850)
            return;
        session.LastPartySignalTime = now;

        if (packet.RemainingBytes < sizeof(int)) return;
        var targetId = (short)packet.ReadInt();
        if (party.FindMember(targetId) < 0)
            return;

        party.CommandLeaderId = targetId;
        party.TargetNumberId = -1;
        await combatNotificationService.SendToPartyAsync(party, CommanderPacket(party));
    }

    private Packet CommanderPacket(PartyGroup party)
    {
        var commander = sessionManager.GetByCharacterId(party.CommanderId);
        return PartyPacketWriter.Commander(party.CommanderId, commander?.Name ?? string.Empty);
    }

    private const int CommanderMarkBytes = 9;
    private const int CommanderOrderBytes = 5;

    private async Task HandleCommanderMarkAsync(UserSession session, Packet packet)
    {
        if (session.Hp <= 0)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null || !party.IsCommandLeader((short)session.CharacterId))
            return;

        var now = DateTime.UtcNow;
        if ((now - session.LastPartySignalTime).TotalMilliseconds < 850)
            return;
        session.LastPartySignalTime = now;

        if (packet.RemainingBytes < CommanderMarkBytes) return;
        var targetId = packet.ReadInt();
        _ = packet.ReadInt();
        var clear = packet.ReadByte() != 0;

        party.TargetNumberId = clear ? (short)-1 : (short)targetId;

        var broadcast = PartyPacketWriter.CommanderMark(targetId, clear);
        await combatNotificationService.SendToPartyAsync(party, broadcast);
    }

    private async Task HandleCommanderOrderAsync(UserSession session, Packet packet)
    {
        if (session.Hp <= 0)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null || !party.IsCommandLeader((short)session.CharacterId))
            return;

        var now = DateTime.UtcNow;
        if ((now - session.LastPartySignalTime).TotalMilliseconds < 850)
            return;
        session.LastPartySignalTime = now;

        if (packet.RemainingBytes < CommanderOrderBytes) return;
        var order = packet.ReadByte();
        _ = packet.ReadInt();

        var broadcast = PartyPacketWriter.CommanderOrder(order);
        await combatNotificationService.SendToPartyAsync(party, broadcast);
    }

    public async Task LeaveGameAsync(UserSession session)
    {
        var party = session.IsInParty ? sessionManager.Parties.GetParty(session.PartyIndex) : null;
        if (party != null && party.LeaderId == (short)session.CharacterId && party.MemberCount > 2
            && NextLeader(party, (short)session.CharacterId) is { } heir)
            await PromoteAsync(session, (short)heir.CharacterId);

        await RemoveMemberAsync(session, (short)session.CharacterId, deliberate: false);
    }

    private UserSession? NextLeader(PartyGroup party, short leavingId)
    {
        for (var i = 0; i < PartyGroup.MaxMembers; i++)
        {
            var memberId = party.MemberIds[i];
            if (memberId < 0 || memberId == leavingId)
                continue;
            if (sessionManager.GetByCharacterId(memberId) is { } member)
                return member;
        }

        return null;
    }

    public async Task RemoveMemberAsync(UserSession session, short memberId, bool deliberate = true)
    {
        if (!session.IsInParty)
            return;

        var index = session.PartyIndex;
        var party = sessionManager.Parties.GetParty(index);
        if (party == null)
        {
            session.TryLeaveParty(index);
            return;
        }

        if (memberId != session.CharacterId && party.LeaderId != (short)session.CharacterId)
            return;

        if (memberId == party.LeaderId)
        {
            await DeleteAsync(session);
            return;
        }

        var memberPos = party.FindMember(memberId);
        if (memberPos < 0)
            return;

        if (party.MemberCount <= 2)
        {
            var leader = sessionManager.GetByCharacterId(party.LeaderId);
            if (leader != null)
                await DeleteAsync(leader);
            return;
        }

        logger.LogInformation("{Name} kicked member {MemberId} from party {PartyIndex}", session.Name, memberId, party.Index);

        var removePkt = PartyPacketWriter.MemberLeft(memberId);
        await combatNotificationService.SendToPartyAsync(party, removePkt);
        if (deliberate && sessionManager.GetByCharacterId(party.LeaderId) is { } partyLeader)
            await partyLeader.Client.SendPacket(PartyPacketWriter.RejoinForget(memberId));

        party.TryLeave(memberId);
        partyWarBoard.MemberLeft(party.Index, memberId);
        if (party.CommandLeaderId == memberId)
        {
            party.CommandLeaderId = -1;
            party.TargetNumberId = -1;
            await combatNotificationService.SendToPartyAsync(party, CommanderPacket(party));
        }
        var removedUser = sessionManager.GetByCharacterId(memberId);
        if (removedUser != null && removedUser.TryLeaveParty(party.Index))
            removedUser.IsPartyLeader = false;
    }

    private async Task HandleCreateOrInsertAsync(UserSession session, Packet packet, byte subOpcode)
    {
        var targetName = packet.ReadString();
        if (string.IsNullOrEmpty(targetName))
            return;
        var boardType = packet.RemainingBytes >= 1 ? packet.ReadByte() : FullMoonRules.NormalBoard;
        var rowMeterMode = packet.RemainingBytes >= 1 ? packet.ReadByte() : PartyWarBoardService.ModeDamage;
        var boardMeterMode = packet.RemainingBytes >= 1 ? packet.ReadByte() : PartyWarBoardService.ModeOff;

        var target = sessionManager.GetByName(targetName);
        if (!await InviteAllowedAsync(session, target, boardType))
            return;

        var created = false;
        if ((PartyRequest)subOpcode == PartyRequest.Create)
        {
            if (session.IsInParty || PartyLedBy(session, boardType) == null)
            {
                await SendErrorAsync(session, PartyInviteError.CannotParty);
                return;
            }

            created = true;
            partyWarBoard.SeedModes(session.PartyIndex, session.CharacterId, rowMeterMode, boardMeterMode);
        }
        else
        {
            var party = sessionManager.Parties.GetParty(session.PartyIndex);
            if (party == null || party.LeaderId != (short)session.CharacterId)
            {
                await SendErrorAsync(session, PartyInviteError.CannotParty);
                return;
            }

            if (party.FindEmptySlot() < 0)
            {
                await SendErrorAsync(session, PartyInviteError.PartyFull);
                return;
            }
        }

        if (!target!.TryOfferPartyInvite(session.PartyIndex, PartyStands))
        {
            if (created)
                DropUnformedParty(session, session.PartyIndex);
            await SendErrorAsync(session, PartyInviteError.CannotParty);
            return;
        }

        var invite = PartyPacketWriter.Invite(session.CharacterId, session.Name);
        await target.Client.SendPacket(invite);
    }

    private bool PartyStands(int index) => sessionManager.Parties.GetParty(index) != null;

    private void DropUnformedParty(UserSession leader, int index)
    {
        sessionManager.Parties.DeleteParty(index);
        partyWarBoard.PartyDeleted(index);
        if (leader.TryLeaveParty(index))
            leader.IsPartyLeader = false;
    }

    public async Task BoardInviteAsync(UserSession inviter, string targetName, byte boardType)
    {
        var target = sessionManager.GetByName(targetName);
        if (!await InviteAllowedAsync(inviter, target, boardType) || !await HasRoomToInviteAsync(inviter, inviter.PartyIndex))
            return;

        target!.OfferBoardInvite(new PartyBoardInvite(inviter.CharacterId, boardType));
        await target.Client.SendPacket(PartyBbsPacketWriter.BoardInvite(
            boardType, inviter.Name, (byte)inviter.Level, (short)inviter.Class));
    }

    // A board accept answers an invite this player was sent, and only once.
    public async Task BoardAcceptAsync(UserSession target, string inviterName, byte boardType)
    {
        var inviter = sessionManager.GetByName(inviterName);
        if (inviter == null || !target.TryTakeBoardInvite(new PartyBoardInvite(inviter.CharacterId, boardType)))
            return;

        if (!await InviteAllowedAsync(inviter, target, boardType))
            return;

        // The inviter's party is read once, and the checks and the join are about that party.
        var index = inviter.PartyIndex;
        var created = index < 0;
        if (created)
        {
            if (PartyLedBy(inviter, boardType) is not { } party)
            {
                await SendErrorAsync(inviter, PartyInviteError.CannotParty);
                return;
            }

            index = party.Index;
        }
        else if (!await HasRoomToInviteAsync(inviter, index))
            return;

        if (!target.TryEnterParty(index))
        {
            if (created)
                DropUnformedParty(inviter, index);
            await SendErrorAsync(inviter, PartyInviteError.CannotParty);
            return;
        }

        await InsertAsync(target, index);
    }

    private async Task<bool> InviteAllowedAsync(UserSession inviter, UserSession? target, byte boardType)
    {
        if (target == null || target == inviter || target.IsInParty)
        {
            await SendErrorAsync(inviter, PartyInviteError.CannotParty);
            return false;
        }

        if (inviter.ZoneId == ChaosRules.Zone)
        {
            await SendErrorAsync(inviter, PartyInviteError.RegionBarred);
            return false;
        }

        if (target.Nation != inviter.Nation && !ZoneRules.AllowsMixedNationParty(inviter.ZoneId))
        {
            await SendErrorAsync(inviter, PartyInviteError.OtherNation);
            return false;
        }

        if (target.ZoneId != inviter.ZoneId)
        {
            await SendErrorAsync(inviter, PartyInviteError.OtherZone);
            return false;
        }

        if (target.Level > inviter.Level + PartyLevelGap || target.Level < inviter.Level - PartyLevelGap)
        {
            await SendErrorAsync(inviter, PartyInviteError.LevelGap);
            return false;
        }

        if (boardType == FullMoonRules.FullMoonBoard && !FullMoonRules.HasVoucher(target))
        {
            await SendErrorAsync(inviter, PartyInviteError.NoVoucher);
            return false;
        }

        return true;
    }

    private async Task<bool> HasRoomToInviteAsync(UserSession inviter, int index)
    {
        if (index < 0)
            return true;
        var party = sessionManager.Parties.GetParty(index);
        if (party == null || party.LeaderId != (short)inviter.CharacterId)
        {
            await SendErrorAsync(inviter, PartyInviteError.CannotParty);
            return false;
        }
        if (party.FindEmptySlot() < 0)
        {
            await SendErrorAsync(inviter, PartyInviteError.PartyFull);
            return false;
        }
        return true;
    }

    private PartyGroup? PartyLedBy(UserSession leader, byte boardType)
    {
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.BoardType = boardType;
        if (!leader.TryEnterParty(party.Index))
        {
            sessionManager.Parties.DeleteParty(party.Index);
            return null;
        }

        partyWarBoard.MemberJoined(party.Index, leader.CharacterId);
        leader.IsPartyLeader = true;
        logger.LogInformation("{Name} created party {PartyIndex}", leader.Name, party.Index);
        return party;
    }

    private const int PartyLevelGap = 8;

    private static Packet BuildPartyMemberPacket(
        UserSession session, PartyGroup party, byte statusCode = PartyPacketWriter.MemberJoined) =>
        PartyPacketWriter.MemberInfo(MemberStateOf(session), statusCode, party.TargetNumberId, party.BoardType);

    private static PartyPacketWriter.MemberState MemberStateOf(UserSession session) => new(
        session.CharacterId,
        session.Name,
        session.Level,
        session.Class,
        session.MaxHp,
        session.Hp,
        session.MaxMp,
        session.Mp);

    private async Task InsertAsync(UserSession session, int index)
    {
        var party = sessionManager.Parties.GetParty(index);
        if (party == null || !party.TryJoin((short)session.CharacterId))
        {
            session.TryLeaveParty(index);
            return;
        }

        session.ForgetBoardInvites();
        if (session.InvitedToParty >= 0)
            await CancelAsync(session);
        partyWarBoard.MemberJoined(party.Index, session.CharacterId);
        await StopSeekingAsync(session);
        if (sessionManager.GetByCharacterId(party.LeaderId) is { } leader)
            await StopSeekingAsync(leader);

        for (var i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.MemberIds[i] < 0)
                continue;

            var member = sessionManager.GetByCharacterId(party.MemberIds[i]);
            if (member != null)
                await session.Client.SendPacket(BuildPartyMemberPacket(member, party));
        }

        await combatNotificationService.SendToPartyAsync(party, BuildPartyMemberPacket(session, party));
        await SendMemberBuffsToJoinerAsync(session, party);
        await combatNotificationService.SendPartyBuffsAsync(session);
        await partyLevelProgress.SendPartyToJoinerAsync(session, party);
        await partyLevelProgress.AnnounceAsync(session);
        await partySignal.SendTableAsync(session, party);
        if (party.MemberCount == FormedPartySize)
            await combatNotificationService.SendToPartyAsync(party, CommanderPacket(party));
        else
            await session.Client.SendPacket(CommanderPacket(party));
    }

    private const int FormedPartySize = 2;

    private async Task SendMemberBuffsToJoinerAsync(UserSession joiner, PartyGroup party)
    {
        foreach (var memberId in party.MemberIds)
        {
            if (memberId < 0 || memberId == joiner.CharacterId)
                continue;

            if (sessionManager.GetByCharacterId(memberId) is { } member)
                await joiner.Client.SendPacket(PartyPacketWriter.MemberBuffs(member.CharacterId, member.LiveBuffIds()));
        }
    }

    private async Task StopSeekingAsync(UserSession session)
    {
        if (!session.SeekingParty)
            return;
        session.SeekingParty = false;
        await sessionManager.Regions.SendToRegion(
            session,
            MovementPacketWriter.StateChange(
                session.CharacterId, (byte)StateChangeType.NeedParty, UserInfoPacketWriter.NotSeekingParty),
            excludeSender: false);
    }

    // Joining answers the invite this player was sent; a member is not a pending invitee.
    private async Task AcceptInviteAsync(UserSession session)
    {
        if (session.IsInParty)
        {
            await CancelAsync(session);
            return;
        }

        var invitedTo = session.TakePartyInvite();
        if (invitedTo < 0)
            return;

        if (!session.TryEnterParty(invitedTo))
        {
            await DeclineAsync(invitedTo);
            return;
        }

        await InsertAsync(session, invitedTo);
    }

    private async Task CancelAsync(UserSession session)
    {
        var invitedTo = session.TakePartyInvite();
        if (invitedTo >= 0)
            await DeclineAsync(invitedTo);
    }

    private async Task DeclineAsync(int invitedTo)
    {
        var party = sessionManager.Parties.GetParty(invitedTo);
        if (party == null)
            return;

        if (party.MemberCount == 1)
        {
            var leader = sessionManager.GetByCharacterId(party.LeaderId);
            if (leader != null)
                await DeleteAsync(leader);
            return;
        }

        var leaderSession = sessionManager.GetByCharacterId(party.LeaderId);
        if (leaderSession != null)
            await SendErrorAsync(leaderSession, -1);
    }

    private async Task DeleteAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var index = session.PartyIndex;
        var party = sessionManager.Parties.GetParty(index);
        if (party == null)
        {
            session.TryLeaveParty(index);
            return;
        }

        if (party.LeaderId != (short)session.CharacterId)
        {
            await RemoveMemberAsync(session, (short)session.CharacterId);
            return;
        }

        var members = sessionManager.Parties.DeleteParty(party.Index);
        if (members == null)
            return;

        logger.LogInformation("Party {PartyIndex} deleted by {Name}", party.Index, session.Name);
        await combatNotificationService.SendToPartyAsync(party, PartyPacketWriter.Disband());
        foreach (var memberId in members)
        {
            if (sessionManager.GetByCharacterId(memberId) is { } member && member.TryLeaveParty(party.Index))
                member.IsPartyLeader = false;
        }

        partyWarBoard.PartyDeleted(party.Index);
    }

    private async Task PromoteAsync(UserSession session, short newLeaderId)
    {
        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null || !party.TryPromote((short)session.CharacterId, newLeaderId))
            return;

        session.IsPartyLeader = false;
        var newLeader = sessionManager.GetByCharacterId(newLeaderId);
        if (newLeader == null)
            return;

        newLeader.IsPartyLeader = true;
        await combatNotificationService.SendToPartyAsync(
            party, BuildPartyMemberPacket(newLeader, party, PartyPacketWriter.MemberPromotedToLeader));
        if (party.CommandLeaderId <= 0)
            await combatNotificationService.SendToPartyAsync(party, CommanderPacket(party));
    }

    private static async Task SendErrorAsync(UserSession session, short errorCode)
    {
        var result = PartyPacketWriter.Rejected(errorCode);
        await session.Client.SendPacket(result);
    }

}
