using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class CombatUtils
{
    public const int MaxDamage = 32000;

    private const int NpcHitScale = 200;
    private const int NpcAcOffset = 240;
    private const float NpcDamageBase = 0.85f;
    private const float NpcDamageSpread = 0.3f;
    private const float NpcDamageCeiling = 2.6f;

    public static int NpcStrikeDamage(int totalHit, int defenderAc, float hitRate, float evadeRate)
    {
        if (totalHit <= 0)
            return 0;

        var hitBase = totalHit * NpcHitScale / (Math.Max(0, defenderAc) + NpcAcOffset);
        if (hitBase <= 0)
            return 0;

        var hit = GetHitRate(Math.Max(1f, hitRate) / Math.Max(1f, evadeRate));
        if (hit == AttackHitResult.Fail)
            return 0;

        var damage = (int)(NpcDamageBase * hitBase + NpcDamageSpread * Random.Shared.Next(0, Math.Max(1, hitBase)));
        if (hit == AttackHitResult.GreatSuccess)
            damage = damage * 3 / 2;

        return Math.Clamp(Math.Min(damage, (int)(NpcDamageCeiling * totalHit)), 0, MaxDamage);
    }

    private static readonly byte[] WeaponSlots =
        [InventoryConstants.LeftHand, InventoryConstants.RightHand];

    public static AttackHitResult GetHitRate(float rate)
    {
        var random = Random.Shared.Next(1, 10001);

        if (rate >= 5.0f)
        {
            if (random <= 3500) return AttackHitResult.GreatSuccess;
            if (random <= 7500) return AttackHitResult.Success;
            if (random <= 9800) return AttackHitResult.Normal;
        }
        else if (rate >= 3.0f)
        {
            if (random <= 2500) return AttackHitResult.GreatSuccess;
            if (random <= 6000) return AttackHitResult.Success;
            if (random <= 9600) return AttackHitResult.Normal;
        }
        else if (rate >= 2.0f)
        {
            if (random <= 2000) return AttackHitResult.GreatSuccess;
            if (random <= 5000) return AttackHitResult.Success;
            if (random <= 9400) return AttackHitResult.Normal;
        }
        else if (rate >= 1.25f)
        {
            if (random <= 1500) return AttackHitResult.GreatSuccess;
            if (random <= 4000) return AttackHitResult.Success;
            if (random <= 9200) return AttackHitResult.Normal;
        }
        else if (rate >= 0.8f)
        {
            if (random <= 1000) return AttackHitResult.GreatSuccess;
            if (random <= 3000) return AttackHitResult.Success;
            if (random <= 9000) return AttackHitResult.Normal;
        }
        else if (rate >= 0.5f)
        {
            if (random <= 800) return AttackHitResult.GreatSuccess;
            if (random <= 2500) return AttackHitResult.Success;
            if (random <= 8000) return AttackHitResult.Normal;
        }
        else if (rate >= 0.33f)
        {
            if (random <= 600) return AttackHitResult.GreatSuccess;
            if (random <= 2000) return AttackHitResult.Success;
            if (random <= 7000) return AttackHitResult.Normal;
        }
        else if (rate >= 0.2f)
        {
            if (random <= 400) return AttackHitResult.GreatSuccess;
            if (random <= 1500) return AttackHitResult.Success;
            if (random <= 6000) return AttackHitResult.Normal;
        }
        else
        {
            if (random <= 200) return AttackHitResult.GreatSuccess;
            if (random <= 1000) return AttackHitResult.Success;
            if (random <= 5000) return AttackHitResult.Normal;
        }

        return AttackHitResult.Fail;
    }

    public static int ApplyWeaponTypeResistance(
        int damage, UserSession attacker, UserSession target, IGameDataService gameData)
    {
        return ApplyWeaponTypeResistance(
            damage, attacker, WeaponResistances.Of(target.Stats), gameData);
    }

    public static int ApplyWeaponTypeResistance(
        int damage, UserSession attacker, WeaponResistances resistances, IGameDataService gameData)
    {
        if (damage <= 0)
            return damage;

        foreach (var slot in WeaponSlots)
        {
            var item = attacker.Inventory[slot];
            if (item.IsEmpty)
                continue;

            var proto = gameData.GetItem(item.ItemId);
            if (proto == null)
                continue;

            damage -= resistances.Reduction(damage, proto.Category);
        }

        return damage;
    }
}
