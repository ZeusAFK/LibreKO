using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct ElementResistances(int Fire, int Cold, int Lightning, int Bonus)
{
    public static ElementResistances Of(DerivedStats stats) =>
        new(stats.FireR, stats.ColdR, stats.LightningR, stats.ResistanceBonus);

    public static ElementResistances Of(NpcInstance npc) => new(npc.FireR, npc.ColdR, npc.LightningR, 0);
}

public readonly record struct ItemHit(int Element, int HpDrain, int MpDamage, int MpDrain);

public static class ItemCombatBonus
{
    public const int PlayerSkillElementDivisor = 2;
    private const int ResistanceCap = 200;
    private const int DrainScale = 100;
    private const int SharedDamageScale = 300;

    public static ItemHit Of(UserSession attacker, ElementResistances resistances, IGameDataService gameData)
    {
        int element = 0, hpDrain = 0, mpDamage = 0, mpDrain = 0;
        foreach (var item in EquippedItems(attacker, gameData))
        {
            element += Resisted(item.FireDamage, resistances.Fire + resistances.Bonus)
                + Resisted(item.IceDamage, resistances.Cold + resistances.Bonus)
                + Resisted(item.LightningDamage, resistances.Lightning + resistances.Bonus)
                + Resisted(item.PoisonDamage, resistances.Bonus);
            hpDrain += item.HpDrain;
            mpDamage += item.MpDamage;
            mpDrain += item.MpDrain;
        }

        return new ItemHit(element, hpDrain, mpDamage, mpDrain);
    }

    public static int MirrorOf(UserSession target, IGameDataService gameData) =>
        EquippedItems(target, gameData).Sum(item => (int)item.MirrorDamage);

    public static int HpDrained(ItemHit hit, int damage) => Math.Max(0, hit.HpDrain * damage / DrainScale);

    public static int ManaDrained(ItemHit hit, int damage) => Math.Max(0, hit.MpDrain * damage / DrainScale);

    public static int ManaBurned(ItemHit hit, int damage) => Math.Max(0, damage * hit.MpDamage / SharedDamageScale);

    public static int Reflected(int mirror, int damage) => Math.Max(0, damage * mirror / SharedDamageScale);

    private static int Resisted(int amount, int resistance) =>
        amount <= 0 ? 0 : amount - amount * Math.Min(resistance, ResistanceCap) / ResistanceCap;

    private static IEnumerable<ItemData> EquippedItems(UserSession user, IGameDataService gameData)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (var slot = 0; slot < InventoryConstants.MagicBagStart; slot++)
        {
            if (slot >= InventoryConstants.SlotMax && slot < InventoryConstants.CospreStart)
                continue;

            var stored = user.Inventory[slot];
            if (stored.IsEmpty || stored.HasExpired(now) || gameData.GetItem(stored.ItemId) is not { } item)
                continue;

            if (slot >= InventoryConstants.CospreStart && item.Category != ItemKind.Cospre)
                continue;

            if (user.WeaponsDisabled && slot is InventoryConstants.RightHand or InventoryConstants.LeftHand
                && item.Category != ItemKind.Shield)
                continue;

            yield return item;
        }
    }
}
