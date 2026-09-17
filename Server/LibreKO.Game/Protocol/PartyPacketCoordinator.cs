using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IPartyPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task RemoveMemberAsync(UserSession session, short memberId);
}

public class PartyPacketCoordinator(
    SessionManager sessionManager,
    ICombatNotificationService combatNotificationService,
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
                    await InsertAsync(session);
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

            case PartyRequest.TargetNumber:
                await HandleTargetNumberAsync(session, packet);
                break;

            case PartyRequest.Alert:
                await HandleAlertAsync(session, packet);
                break;
        }
    }

    private async Task HandleCommandPromoteAsync(UserSession session, Packet packet)
    {
        if (!session.IsPartyLeader)
            return;

        var now = DateTime.UtcNow;
        if ((now - session.LastPartySignalTime).TotalMilliseconds < 850)
            return;
        session.LastPartySignalTime = now;

        if (packet.RemainingBytes < 2) return;
        var targetId = packet.ReadShort();

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null || party.FindMember(targetId) < 0)
            return;

        party.CommandLeaderId = targetId;

        var target = sessionManager.GetByCharacterId(targetId);
        var broadcast = PartyPacketWriter.Commander(targetId, target?.Name ?? string.Empty);
        await combatNotificationService.SendToPartyAsync(party, broadcast);
    }

    private async Task HandleTargetNumberAsync(UserSession session, Packet packet)
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

        if (packet.RemainingBytes < 7) return;
        var targetId = packet.ReadShort();
        _ = packet.ReadInt(); // effect id (visual hint, unused server-side)
        var success = (sbyte)packet.ReadByte();

        party.TargetNumberId = targetId;

        var broadcast = PartyPacketWriter.TargetNumber(targetId, success);
        await combatNotificationService.SendToPartyAsync(party, broadcast);
    }

    private async Task HandleAlertAsync(UserSession session, Packet packet)
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

        if (packet.RemainingBytes < 5) return;
        var subOpcode = packet.ReadByte();
        _ = packet.ReadInt(); // effect id

        var broadcast = PartyPacketWriter.Alert(subOpcode);
        await combatNotificationService.SendToPartyAsync(party, broadcast);
    }

    public async Task RemoveMemberAsync(UserSession session, short memberId)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
        {
            session.PartyIndex = -1;
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

        party.MemberIds[memberPos] = -1;
        var removedUser = sessionManager.GetByCharacterId(memberId);
        if (removedUser != null)
        {
            removedUser.PartyIndex = -1;
            removedUser.IsPartyLeader = false;
        }
    }

    private async Task HandleCreateOrInsertAsync(UserSession session, Packet packet, byte subOpcode)
    {
        var targetName = packet.ReadString();
        if (string.IsNullOrEmpty(targetName))
            return;

        var target = sessionManager.GetByName(targetName);
        if (target == null || target == session || target.IsInParty)
        {
            await SendErrorAsync(session, -1);
            return;
        }

        if (target.Level > session.Level + 8 || target.Level < session.Level - 8)
        {
            await SendErrorAsync(session, -2);
            return;
        }

        if (target.ZoneId != session.ZoneId)
        {
            await SendErrorAsync(session, -3);
            return;
        }

        if ((PartyRequest)subOpcode == PartyRequest.Create)
        {
            if (session.IsInParty)
            {
                await SendErrorAsync(session, -1);
                return;
            }

            var party = sessionManager.Parties.CreateParty((short)session.CharacterId);
            session.PartyIndex = party.Index;
            session.IsPartyLeader = true;
            logger.LogInformation("{Name} created party {PartyIndex}", session.Name, party.Index);
        }
        else
        {
            var party = sessionManager.Parties.GetParty(session.PartyIndex);
            if (party == null || party.FindEmptySlot() < 0)
            {
                await SendErrorAsync(session, -1);
                return;
            }
        }

        target.PartyIndex = session.PartyIndex;

        var invite = PartyPacketWriter.Invite(session.CharacterId, session.Name);
        await target.Client.SendPacket(invite);
    }

    private static Packet BuildPartyMemberPacket(
        UserSession session, byte statusCode = PartyPacketWriter.MemberJoined) =>
        PartyPacketWriter.MemberInfo(MemberStateOf(session), statusCode);

    private static PartyPacketWriter.MemberState MemberStateOf(UserSession session) => new(
        session.CharacterId,
        session.Name,
        session.Level,
        session.Class,
        session.MaxHp,
        session.Hp,
        session.MaxMp,
        session.Mp);

    private async Task InsertAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
        {
            session.PartyIndex = -1;
            return;
        }

        if (party.FindMember((short)session.CharacterId) >= 0)
        {
            session.PartyIndex = -1;
            return;
        }

        var slot = party.FindEmptySlot();
        if (slot < 0)
        {
            session.PartyIndex = -1;
            return;
        }

        party.MemberIds[slot] = (short)session.CharacterId;

        for (var i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.MemberIds[i] < 0)
                continue;

            var member = sessionManager.GetByCharacterId(party.MemberIds[i]);
            if (member != null)
                await session.Client.SendPacket(BuildPartyMemberPacket(member));
        }

        await combatNotificationService.SendToPartyAsync(party, BuildPartyMemberPacket(session));
    }

    private async Task CancelAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        session.PartyIndex = -1;

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

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
        {
            session.PartyIndex = -1;
            return;
        }

        logger.LogInformation("Party {PartyIndex} deleted by {Name}", party.Index, session.Name);

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

        sessionManager.Parties.DeleteParty(party.Index);
    }

    private async Task PromoteAsync(UserSession session, short newLeaderId)
    {
        if (!session.IsPartyLeader)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
            return;

        var memberPos = party.FindMember(newLeaderId);
        if (memberPos <= 0)
            return;

        (party.MemberIds[0], party.MemberIds[memberPos]) = (party.MemberIds[memberPos], party.MemberIds[0]);

        session.IsPartyLeader = false;
        var newLeader = sessionManager.GetByCharacterId(newLeaderId);
        if (newLeader == null)
            return;

        newLeader.IsPartyLeader = true;
        await combatNotificationService.SendToPartyAsync(
            party, BuildPartyMemberPacket(newLeader, PartyPacketWriter.MemberPromotedToLeader));
    }

    private static async Task SendErrorAsync(UserSession session, short errorCode)
    {
        var result = PartyPacketWriter.Rejected(errorCode);
        await session.Client.SendPacket(result);
    }

}
