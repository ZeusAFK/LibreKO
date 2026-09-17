using LibreKO.Common.Enums;

namespace LibreKO.Common.Domain.Services;

public static class ClanDonationCalculator
{
    public const int NationalPointsPerClanPoint = 36;
    public const int LeaverRefundPercent = 30;
    public const int FullRefundPercent = 100;
    public const int LoyaltyMax = 2_100_000_000;

    public const int ContRecoveryItemId = 800_370_000;
    public const int ContributionRestorationItemId = 810_324_000;

    public static readonly int[] RecoveryItemIds =
        [ContRecoveryItemId, ContributionRestorationItemId];

    private static readonly Dictionary<ClanType, int> ClanPointPrice = new()
    {
        [ClanType.Accredited4] = 7_000,
        [ClanType.Accredited3] = 10_000,
        [ClanType.Accredited2] = 15_000,
        [ClanType.Accredited1] = 20_000,
        [ClanType.Royal5] = 25_000,
        [ClanType.Royal4] = 30_000,
        [ClanType.Royal3] = 35_000,
        [ClanType.Royal2] = 40_000,
        [ClanType.Royal1] = 45_000,
    };

    public static int ClanPointsFor(ClanType grade) =>
        ClanPointPrice.TryGetValue(grade, out var points) ? points : 0;

    public static int NationalPointsFor(ClanType grade) =>
        ClanPointsFor(grade) * NationalPointsPerClanPoint;

    public static ClanType PreviousGrade(ClanType grade) =>
        grade <= ClanType.Accredited5 ? grade : grade - 1;

    public static int RefundFor(int donated, bool keepsEverything) =>
        donated <= 0
            ? 0
            : (int)((long)donated
                * (keepsEverything ? FullRefundPercent : LeaverRefundPercent) / FullRefundPercent);

    public static (ClanType Grade, int Fund) WithdrawDonation(ClanType grade, int fund, int donated)
    {
        fund = Math.Max(0, fund);
        var owed = Math.Max(0, donated);

        if (grade < ClanType.Accredited5)
            return (grade, Math.Max(0, fund - owed));

        while (owed > 0)
        {
            if (fund >= owed)
                return (grade, fund - owed);

            if (grade == ClanType.Accredited5)
                return (grade, 0);

            fund += NationalPointsFor(grade);
            grade = PreviousGrade(grade);

            if (fund < owed)
            {
                owed -= fund;
                fund = 0;
            }
        }

        return (grade, fund);
    }
}
