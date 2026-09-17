using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

internal static class DeathPenaltyCalculator
{
    private const byte MinimumPenaltyLevel = 6;
    private const int StandardLossDivisor = 20;
    private const int ReducedLossDivisor = 100;

    public static long CalculateNpcDeathExpLoss(UserSession target, NpcInstance npc, IGameDataService gameDataService)
    {
        if (!LosesExperienceOnDeath(target) || IsExemptZone(target.ZoneId))
            return 0;

        var reduced = npc.NpcType == NpcData.TypePatrolGuard || IsEnemyNationHomeZone(target);
        var expLoss = gameDataService.GetMaxExpForLevel(target.Level)
            / (reduced ? ReducedLossDivisor : StandardLossDivisor);

        return ApplyPremiumReduction(target, expLoss, gameDataService);
    }

    public static long CalculatePlayerDeathExpLoss(UserSession victim, IGameDataService gameDataService)
    {
        if (!LosesExperienceOnDeath(victim))
            return 0;

        var expLoss = gameDataService.GetMaxExpForLevel(victim.Level)
            / (IsEnemyNationHomeZone(victim) ? ReducedLossDivisor : StandardLossDivisor);

        return ApplyPremiumReduction(victim, expLoss, gameDataService);
    }

    private static bool LosesExperienceOnDeath(UserSession session) =>
        session.Level >= MinimumPenaltyLevel && session.RebirthLevel <= 0;

    private static bool IsExemptZone(byte zoneId) => (ZoneId)zoneId is
        ZoneId.MonsterStone1 or ZoneId.MonsterStone2 or ZoneId.MonsterStone3
        or ZoneId.ForgottenTemple or ZoneId.JuradMountain or ZoneId.KrowazDominion;

    private static bool IsEnemyNationHomeZone(UserSession session)
        => session.ZoneId <= (byte)ZoneId.ElMoradCamp1 && session.ZoneId != (byte)session.Nation;

    private static long ApplyPremiumReduction(UserSession session, long expLoss, IGameDataService gameDataService)
    {
        if (expLoss <= 0 || session.PremiumType == 0)
            return expLoss;

        var premiumTable = gameDataService.PremiumItemTable;
        if (premiumTable == null || !premiumTable.TryGetValue(session.PremiumType, out PremiumItemData? premium))
            return expLoss;

        return premium.ExpRestorePercent > 0
            ? (long)(expLoss * premium.ExpRestorePercent / 100.0)
            : expLoss;
    }
}
