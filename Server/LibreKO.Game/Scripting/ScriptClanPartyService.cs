using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

#pragma warning disable IDE0060
namespace LibreKO.Game.Scripting;

public class ScriptClanPartyService(
    UserSession session,
    SessionManager sessionManager,
    ScriptItemService items,
    QuestScriptContext context)
{
    private const ClanType DefaultPromotedClanRank = ClanType.Promoted;

    public bool IsInClan(int _uid) => session.KnightsId > 0;
    public bool IsClanLeader(int _uid) => session.KnightsFame <= 1 && session.KnightsId > 0;
    public bool IsInParty(int _uid) => session.IsInParty;
    public bool IsPartyLeader(int _uid) => session.IsPartyLeader;

    public int CheckClanGrade(int _uid)
    {
        return GetClan()?.Grade ?? 0;
    }

    public int CheckClanPoint(int _uid)
    {
        return GetClan()?.ClanPointFund ?? 0;
    }

    public int CheckKnight(int _uid) => GetClan()?.Flag ?? 0;

    public int GetClanGrade(int _uid) => GetClan()?.Grade ?? 0;

    public int GetClanPoint(int _uid) => GetClan()?.ClanPointFund ?? 0;

    public int GetClanRank(int _uid)
    {
        var clan = GetClan();
        if (clan == null) return 0;

        var fund = clan.ClanPointFund;
        var ranked = sessionManager.Knights.GetAll().Count(c => c.ClanPointFund > fund);
        return ranked + 1;
    }

    public int CheckLoyalty(int _uid) => session.Loyalty;
    public bool CheckLoyalty(int _uid, int amount) => session.Loyalty >= amount;

    public void RobClanPoint(int _uid, int amount)
    {
        if (amount <= 0)
            return;

        var clan = GetClan();
        if (clan != null)
            clan.ClanPointFund = Math.Max(0, clan.ClanPointFund - amount);
    }

    public bool GiveClanPremium(int _uid, int days)
    {
        if (days <= 0 || !IsClanLeader(_uid))
            return false;

        var clan = GetClan();
        if (clan == null)
            return false;

        var now = DateTime.UtcNow;
        var standing = clan.PremiumExpiry > now ? clan.PremiumExpiry!.Value : now;
        clan.PremiumExpiry = standing.AddDays(days);
        context.PremiumClanId = clan.Id;
        return true;
    }

    public void PromoteKnight(int _uid)
    {
        PromoteKnight(_uid, (int)DefaultPromotedClanRank);
    }

    public void PromoteKnight(int _uid, int clanRank)
    {
        if (!Enum.IsDefined(typeof(ClanType), (byte)clanRank) || clanRank == (int)ClanType.None)
            return;

        var clan = GetClan();
        if (clan == null)
            return;

        var promotedTo = (ClanType)clanRank;
        clan.Flag = (byte)promotedTo;
        clan.Cape = ClanRules.CapeForType(promotedTo, clan.Cape);
        context.PromotedClanId = clan.Id;
    }

    public int PartyCountMembers(int _uid)
    {
        if (!session.IsInParty)
            return 0;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        return party?.MemberCount ?? 0;
    }

    public void RobAllItemParty(int uid, int itemId)
    {
        items.RobItem(uid, itemId, 1);
    }

    public bool ClanNts(int _uid)
    {
        if (session.KnightsId <= 0 || session.KnightsFame != 1)
            return false;
        context.RunClanNts = true;
        return true;
    }

    private KnightsEntity? GetClan()
    {
        if (session.KnightsId <= 0)
            return null;

        return sessionManager.Knights.GetClan(session.KnightsId);
    }
}
#pragma warning restore IDE0060
