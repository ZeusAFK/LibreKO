using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct PhysicalDefender(
    int Armour,
    float EvasionRate,
    bool IsPlayer,
    bool BlocksPhysical,
    WeaponResistances Resistances,
    short ClassId = 0,
    short AcBonusClassType = 0,
    short AcBonusClassPercent = 0)
{
    public static PhysicalDefender Of(UserSession target) => new(
        target.Stats.TotalAc,
        target.Stats.TotalEvasionrate,
        IsPlayer: true,
        target.BlockPhysical,
        WeaponResistances.Of(target.Stats),
        target.Class,
        target.Stats.AcBonusClassType,
        target.Stats.AcBonusClassPercent);

    public static PhysicalDefender Of(NpcInstance target) => new(
        target.Ac,
        target.EvadeRate,
        IsPlayer: false,
        BlocksPhysical: false,
        default);
}

public static class PhysicalDamageCalculator
{
    private const float BaseDamageShare = 0.85f;
    private const float RandomDamageShare = 0.3f;
    private const int ArmourSoftening = 240;
    private const int ArmourPenetration = 2;
    private const int PercentScale = 100;
    private const int PlayerDamageDivisor = 2;
    private const int MinimumLandedDamage = 1;

    private const byte ZoneSnowBattle = (byte)ZoneId.SnowBattle;
    private const byte ZoneChaosDungeon = (byte)ZoneId.ChaosDungeon;
    private const byte ZoneDungeonDefence = (byte)ZoneId.DungeonDefence;
    private const int FixedDungeonDamage = 50;

    private static int ApplyClassTypedBonuses(
        UserSession attacker, PhysicalDefender defender, int attackPower)
    {
        var stats = attacker.Stats;
        if (defender.IsPlayer
            && stats.ApBonusClassPercent > 0
            && ClassIdHelper.IsSameJobGroup(defender.ClassId, (byte)stats.ApBonusClassType))
            attackPower = attackPower * (PercentScale + stats.ApBonusClassPercent) / PercentScale;

        if (defender.AcBonusClassPercent > 0
            && ClassIdHelper.IsSameJobGroup(attacker.Class, (byte)defender.AcBonusClassType))
            attackPower = attackPower * Math.Max(0, PercentScale - defender.AcBonusClassPercent)
                / PercentScale;

        return attackPower;
    }

    public static int Calculate(
        UserSession attacker, PhysicalDefender defender, IGameDataService gameData)
    {
        if (defender.BlocksPhysical)
            return 0;

        var armour = Math.Max(0, defender.Armour);
        var attackPower = attacker.Stats.TotalHit * attacker.AttackAmount;
        if (defender.IsPlayer)
            attackPower = attackPower * attacker.PlayerAttackAmount / PercentScale;

        attackPower = ApplyClassTypedBonuses(attacker, defender, attackPower);

        var hitBase = attackPower * ArmourPenetration / (armour + ArmourSoftening);
        if (hitBase <= 0)
            return 0;

        var rate = attacker.Stats.TotalHitrate / Math.Max(1f, defender.EvasionRate) + 1f;
        if (CombatUtils.GetHitRate(rate) == AttackHitResult.Fail)
            return 0;

        var damage = (int)(BaseDamageShare * hitBase
            + RandomDamageShare * Random.Shared.Next(0, hitBase + 1));

        if (defender.IsPlayer)
        {
            damage = CombatUtils.ApplyWeaponTypeResistance(
                damage, attacker, defender.Resistances, gameData);
            damage /= PlayerDamageDivisor;
        }

        damage = Math.Max(MinimumLandedDamage, damage);
        return Math.Clamp(ZoneDamage(attacker.ZoneId, damage), 0, CombatUtils.MaxDamage);
    }

    private static int ZoneDamage(byte zoneId, int damage) => zoneId switch
    {
        ZoneSnowBattle => 0,
        ZoneChaosDungeon or ZoneDungeonDefence => FixedDungeonDamage,
        _ => damage,
    };
}
