using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ILoyaltyService
{
    Task ChangeAsync(UserSession session, int changeAmount);
    Task AwardKillAsync(UserSession session, int amount);
    Task SetAsync(UserSession session, int loyalty);
    Task<bool> TryDonateToKnightsAsync(UserSession session, int amount);
}

public class LoyaltyService(
    TimeWeatherBroadcastService timeWeather,
    IGameDataService gameData,
    SessionManager sessionManager,
    IClanStandingService clanStanding,
    IAchievementProgressService achievementProgressService) : ILoyaltyService
{
    public Task ChangeAsync(UserSession session, int changeAmount) =>
        ApplyAsync(session, changeAmount < 0 ? changeAmount : timeWeather.ApplyNpBonus(changeAmount));

    public Task AwardKillAsync(UserSession session, int amount)
    {
        if (amount <= 0)
            return ChangeAsync(session, amount);

        var premium = gameData.GetPremiumProperty(session.PremiumType, PremiumPropertyType.Loyalty);
        session.DailyPremiumLoyalty += premium;
        var clanNp = session.KnightsId > 0 ? ClanOnlineBonus.Np(session.ClanOnlineCount) : 0;
        return ApplyAsync(session, timeWeather.ApplyNpBonus(amount) + premium + clanNp);
    }

    private async Task ApplyAsync(UserSession session, int changeAmount)
    {
        var contribution = AutoContribution(session, changeAmount);
        session.WithLock(s =>
        {
            if (changeAmount < 0)
            {
                s.Loyalty = Math.Max(0, s.Loyalty + changeAmount);
                return;
            }

            s.KnightsPoints += contribution;
            s.Loyalty = Gained(s.Loyalty, changeAmount - contribution);
            s.MonthlyLoyalty = Gained(s.MonthlyLoyalty, changeAmount);
            s.DailyLoyalty = Gained(s.DailyLoyalty, changeAmount);
        });

        await session.Client.SendPacket(
            LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty));

        if (changeAmount > 0)
            await achievementProgressService.RefreshAsync(session);
    }

    // A total of national points stops at the largest value it can hold instead of wrapping negative.
    internal static int Gained(int total, long gain) => (int)Math.Clamp(total + gain, 0, int.MaxValue);

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
        ClanRules.AddToFund(clan, contribution);
        clanStanding.MarkDirty(clan.Id);
        return contribution;
    }

    public async Task SetAsync(UserSession session, int loyalty)
    {
        var gained = session.WithLock(s =>
        {
            var raised = loyalty > s.Loyalty;
            s.Loyalty = Math.Max(0, loyalty);
            return raised;
        });

        await session.Client.SendPacket(
            LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty));

        if (gained)
            await achievementProgressService.RefreshAsync(session);
    }

    public async Task<bool> TryDonateToKnightsAsync(UserSession session, int amount)
    {
        if (!session.WithLock(s => TryDonate(s, amount)))
            return false;

        await achievementProgressService.RefreshAsync(session);
        return true;
    }

    // A donor keeps a floor of national points; the check and the payment are one step.
    private static bool TryDonate(UserSession session, int amount)
    {
        if (amount <= 0 || (long)session.Loyalty - amount < ClanRules.DonorKeepsNationalPoints)
            return false;

        session.Loyalty -= amount;
        session.KnightsPoints += amount;
        return true;
    }
}
