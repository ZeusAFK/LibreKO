using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

internal static class StatusEffectChance
{
    public const int ExtendedRowChance = 10;
    public const int StatusChance = 15;

    private const int Percent = 100;
    private const int MageBaseChance = 25;
    private const int MageMinimumChance = 5;
    private const int StatBonusThreshold = 100;
    private const float MageChancePerIntelligence = 0.1f;
    private const float MageChanceLostPerResistance = 0.5f;
    private const int ResistanceOddsBase = 101;
    private static readonly long MageImmunityTicks = TimeSpan.FromSeconds(5).Ticks;

    public static bool LocksMovement(BuffType buffType) =>
        buffType is BuffType.Speed or BuffType.Speed2 or BuffType.Stun;

    public static int Resistance(UserSession target, BuffType buffType) =>
        Math.Max(0, (buffType == BuffType.Speed2 ? target.Stats.ColdR : target.Stats.LightningR)
            + target.Stats.ResistanceBonus);

    public static int MageChance(int intelligence, int resistance) =>
        Math.Clamp(
            (int)(MageBaseChance + (intelligence - StatBonusThreshold) * MageChancePerIntelligence
                - resistance * MageChanceLostPerResistance),
            MageMinimumChance,
            Percent);

    public static bool Lands(UserSession caster, UserSession target, BuffType buffType, int otherChance)
    {
        var resistance = Resistance(target, buffType);
        if (!ClassIdHelper.IsMage(caster.Class))
            return Random.Shared.Next(ResistanceOddsBase + resistance) < otherChance;

        var now = DateTime.UtcNow.Ticks;
        if (now < target.MageStatusImmuneUntilTicks
            || Random.Shared.Next(Percent) >= MageChance(caster.Intelligence, resistance))
            return false;

        target.MageStatusImmuneUntilTicks = now + MageImmunityTicks;
        return true;
    }
}
