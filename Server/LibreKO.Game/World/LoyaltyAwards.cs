using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct LoyaltyAward(int Killer, int Victim);

public static class LoyaltyAwards
{
    public const int RivalryBonus = 150;
    private const int PartyShareNumerator = 3;
    private const int PartyShareOffset = 2;
    private const int PartySmallBonus = 2;
    private const int PartyShareTrim = 1;
    private const int AwardCeiling = 1000;
    private const int CeilingFallback = 150;

    private static readonly LoyaltyAward Ardream = new(32, -25);
    private static readonly LoyaltyAward Standard = new(64, -50);

    public static LoyaltyAward For(byte zoneId) => (ZoneId)zoneId switch
    {
        ZoneId.Ardream => Ardream,
        _ => Standard,
    };

    public static LoyaltyAward PerPartyMember(LoyaltyAward award, int partySize)
    {
        if (partySize <= 1)
            return award;

        var share = ((award.Killer * PartyShareNumerator) - PartyShareOffset) / PartyGroup.MaxMembers;
        if (share > 0)
            share += PartySmallBonus * Math.Max(0, PartyGroup.MaxMembers - partySize);

        return award with { Killer = share - PartyShareTrim };
    }

    public static LoyaltyAward WithRivalryBonus(LoyaltyAward award, bool killerHadRival)
    {
        if (!killerHadRival)
            return Clamp(award);

        return Clamp(award with
        {
            Killer = award.Killer + RivalryBonus,
            Victim = award.Victim - RivalryBonus,
        });
    }

    private static LoyaltyAward Clamp(LoyaltyAward award) => new(
        award.Killer > AwardCeiling ? CeilingFallback : award.Killer,
        award.Victim > AwardCeiling ? CeilingFallback : award.Victim);
}
