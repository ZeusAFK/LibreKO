using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
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
}

public class KnightsRuntimeService(SessionManager sessionManager) : IKnightsRuntimeService
{
    private const byte ViceChiefFame = 2;
    private const int MaxViceChiefs = 3;

    public bool IsClanLeader(UserSession session)
    {
        return session.KnightsId > 0 && session.KnightsFame == 1;
    }

    public bool CanAdmitCandidates(UserSession session)
    {
        return session.KnightsId > 0 && session.KnightsFame is > 0 and <= 3;
    }

    public void ClearClanState(UserSession session)
    {
        // Clan rank is persisted through the shared fame field, so clear both when leaving the clan.
        session.KnightsId = 0;
        session.KnightsFame = 0;
        session.KnightsName = string.Empty;
        session.Fame = 0;
    }

    public async Task<bool> CanPromoteToViceChiefAsync(
        IKnightsRepository repo, short knightsId, string targetName)
    {
        var members = await repo.GetCharactersByClanAsync(knightsId);
        if (members.Any(m => m.Fame == ViceChiefFame
                             && m.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
            return true;

        return members.Count(m => m.Fame == ViceChiefFame) < MaxViceChiefs;
    }

    public async Task SyncCharacterAsync(IKnightsRepository repo, UserSession session, bool includeMoney = false, bool includeLoyalty = false)
    {
        var fame = session.KnightsId > 0 ? session.KnightsFame : session.Fame;
        await repo.SyncCharacterClanStateAsync(
            session.CharacterId,
            session.KnightsId,
            fame,
            includeMoney ? session.Money : null,
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
                // Ignore per-member delivery failures.
            }
        }
    }

}
