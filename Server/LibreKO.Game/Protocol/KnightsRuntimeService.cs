using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IKnightsRuntimeService
{
    bool IsClanLeader(UserSession session);
    bool CanAdmitCandidates(UserSession session);
    void ClearClanState(UserSession session);
    Task<bool> CanPromoteToViceChiefAsync(IKnightsRepository repo, short knightsId, string targetName);
    Task SyncCharacterAsync(IKnightsRepository repo, UserSession session, bool includeMoney = false, bool includeLoyalty = false);
    Task NotifyOnlineClanMembersAsync(short clanId, Packet packet);
    Task SendClanChatAsync(short clanId, string message);
    Task BroadcastFameChangeAsync(UserSession member, short clanId, byte fame);
    Task SendClanUpdateAsync(KnightsEntity clan);
    Task NotifyMemberOnlineAsync(UserSession session);
    Task NotifyMemberOfflineAsync(UserSession session);
    Task RefreshClanBonusAsync(short clanId, UserSession? leaving = null);
}

public class KnightsRuntimeService(SessionManager sessionManager) : IKnightsRuntimeService
{
    public bool IsClanLeader(UserSession session)
    {
        return session.KnightsId > 0 && session.KnightsFame == ClanRules.FameChief;
    }

    public bool CanAdmitCandidates(UserSession session)
    {
        return session.KnightsId > 0 && ClanRules.CanAdmit(session.KnightsFame);
    }

    public void ClearClanState(UserSession session)
    {
        short oldClan = session.KnightsId;
        if (oldClan > 0)
        {
            session.ClanOnlineCount = 0;
            _ = ClearClanBonusAsync(session, oldClan);
        }
        session.KnightsId = 0;
        session.KnightsFame = 0;
        session.KnightsName = string.Empty;
        session.KnightsPoints = 0;
        session.Fame = 0;
    }

    public async Task<bool> CanPromoteToViceChiefAsync(
        IKnightsRepository repo, short knightsId, string targetName)
    {
        var members = await repo.GetCharactersByClanAsync(knightsId);
        if (members.Any(m => m.Fame == ClanRules.FameViceChief
                             && m.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
            return true;

        return members.Count(m => m.Fame == ClanRules.FameViceChief) < ClanRules.MaxViceChiefs;
    }

    public async Task SyncCharacterAsync(IKnightsRepository repo, UserSession session, bool includeMoney = false, bool includeLoyalty = false)
    {
        var fame = session.KnightsId > 0 ? session.KnightsFame : session.Fame;
        await repo.SyncCharacterClanStateAsync(
            session.CharacterId,
            session.KnightsId,
            fame,
            includeMoney ? session.SavedMoney : null,
            includeLoyalty ? session.Loyalty : null);
    }

    public async Task NotifyOnlineClanMembersAsync(short clanId, Packet packet)
    {
        foreach (var member in sessionManager.GetAll())
        {
            if (member.KnightsId != clanId)
                continue;

            try
            {
                await member.Client.SendPacket(packet);
            }
            catch
            {
            }
        }
    }

    public Task SendClanChatAsync(short clanId, string message) =>
        NotifyOnlineClanMembersAsync(clanId, ChatPacketWriter.ClanNotice(message));

    public Task BroadcastFameChangeAsync(UserSession member, short clanId, byte fame) =>
        sessionManager.Regions.SendToRegion(
            member, KnightsPacketWriter.FameChanged(member.CharacterId, clanId, fame), excludeSender: false);

    public Task SendClanUpdateAsync(KnightsEntity clan) =>
        NotifyOnlineClanMembersAsync(clan.Id, KnightsPacketWriter.ClanUpdate(
            clan.Id, clan.Flag, clan.Cape, clan.CapeR, clan.CapeG, clan.CapeB, clan.ClanPointFund));

    public async Task NotifyMemberOnlineAsync(UserSession session)
    {
        if (session.KnightsId <= 0)
            return;
        await NotifyOthersAsync(session, KnightsPacketWriter.MemberOnline(session.Name));
        await RefreshClanBonusAsync(session.KnightsId);
    }

    public async Task NotifyMemberOfflineAsync(UserSession session)
    {
        if (session.KnightsId <= 0)
            return;
        await NotifyOthersAsync(session, KnightsPacketWriter.MemberOffline(session.Name));
        await RefreshClanBonusAsync(session.KnightsId, session);
    }

    public async Task RefreshClanBonusAsync(short clanId, UserSession? leaving = null)
    {
        if (clanId <= 0)
            return;

        var members = sessionManager.GetAll()
            .Where(m => m.KnightsId == clanId && m.CharacterId != leaving?.CharacterId)
            .ToList();
        var packet = KnightsPacketWriter.ClanBonus(members.Count);
        foreach (var member in members)
        {
            member.ClanOnlineCount = members.Count;
            try
            {
                await member.Client.SendPacket(packet);
            }
            catch
            {
            }
        }
    }

    private async Task ClearClanBonusAsync(UserSession session, short oldClan)
    {
        try
        {
            await session.Client.SendPacket(KnightsPacketWriter.ClanBonusCleared());
        }
        catch
        {
        }
        await RefreshClanBonusAsync(oldClan, session);
    }

    private async Task NotifyOthersAsync(UserSession session, Packet packet)
    {
        foreach (var member in sessionManager.GetAll())
        {
            if (member.KnightsId != session.KnightsId || member.CharacterId == session.CharacterId)
                continue;

            try
            {
                await member.Client.SendPacket(packet);
            }
            catch
            {
            }
        }
    }
}
