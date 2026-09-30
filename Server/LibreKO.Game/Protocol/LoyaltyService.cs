using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ILoyaltyService
{
    Task ChangeAsync(UserSession session, int changeAmount);
    Task SetAsync(UserSession session, int loyalty);
    Task DonateToKnightsAsync(UserSession session, int amount);
}

public class LoyaltyService(
    TimeWeatherBroadcastService timeWeather,
    SessionManager sessionManager,
    IClanStandingService clanStanding,
    IAchievementProgressService achievementProgressService) : ILoyaltyService
{
    public async Task ChangeAsync(UserSession session, int changeAmount)
    {
        if (changeAmount < 0)
        {
            session.Loyalty = Math.Max(0, session.Loyalty + changeAmount);
        }
        else
        {
            changeAmount = timeWeather.ApplyNpBonus(changeAmount);
            session.Loyalty += changeAmount - AutoContribution(session, changeAmount);
            session.MonthlyLoyalty += changeAmount;
            session.DailyLoyalty += changeAmount;
        }

        await session.Client.SendPacket(
            LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty));

        if (changeAmount > 0)
            await achievementProgressService.RefreshAsync(session);
    }

    private int AutoContribution(UserSession session, int gained)
    {
        if (session.KnightsId <= 0 || gained <= 0)
            return 0;

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null
            || !ClanRules.AcceptsDonations((ClanType)clan.Flag)
            || clan.ClanPointMethod != ClanRules.AutomaticAccumulation)
            return 0;

        var contribution = Math.Min(gained, ClanRules.AutoContribution(clan.Members));
        clan.ClanPointFund = (int)Math.Min((long)clan.ClanPointFund + contribution, int.MaxValue);
        session.KnightsPoints += contribution;
        clanStanding.MarkDirty(clan.Id);
        return contribution;
    }

    public async Task SetAsync(UserSession session, int loyalty)
    {
        var gained = loyalty > session.Loyalty;
        session.Loyalty = Math.Max(0, loyalty);

        await session.Client.SendPacket(
            LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty));

        if (gained)
            await achievementProgressService.RefreshAsync(session);
    }

    public async Task DonateToKnightsAsync(UserSession session, int amount)
    {
        session.Loyalty = Math.Max(0, session.Loyalty - amount);
        session.KnightsPoints += amount;
        await achievementProgressService.RefreshAsync(session);
    }
}
