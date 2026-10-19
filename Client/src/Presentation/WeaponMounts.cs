namespace LibreKO.Presentation;

internal static class WeaponMounts
{
    internal const string WeaponNodePrefix = "weapon_";
    private const int ExtRowSpan = 1000;
    private const float SizePercent = 100f;

    internal static float SizeScale(int size) => size > 0 ? size / SizePercent : 1f;

    internal static HandWearer WeaponWearer(bool npc, bool statue, int race, int npcId, int size, int modelId) =>
        npc && !statue
            ? new HandWearer(race, Npc: true, NpcId: npcId, NpcScale: SizeScale(size), PlugSlots: NpcPlugSlots.For(modelId))
            : new HandWearer(race, NpcId: npcId);

    internal static int TrailElement(int itemId)
    {
        if (itemId <= 0 || ItemData.Get(itemId) is not { } def) return WeaponTrailRule.Normal;
        int extId = ItemData.ExtIdFor(itemId);
        return extId % ExtRowSpan != 0 && ItemData.ExtRow(def.Cat, extId) is { } ext
            ? WeaponTrailRule.Element(ext.FireDamage, ext.IceDamage, ext.LightningDamage, ext.PoisonDamage)
            : WeaponTrailRule.Normal;
    }

    internal static (int Right, int Left, int Weight) HandLoadout(int[]? gear)
    {
        int rightId = gear != null && gear.Length > InventoryConstants.VisRightHand ? gear[InventoryConstants.VisRightHand] : 0;
        int leftId = gear != null && gear.Length > InventoryConstants.VisLeftHand ? gear[InventoryConstants.VisLeftHand] : 0;
        var right = rightId > 0 ? ItemData.Get(rightId) : null;
        var left = leftId > 0 ? ItemData.Get(leftId) : null;
        return (right?.Kind ?? WeaponAnimation.NoItem,
                left?.Kind ?? WeaponAnimation.NoItem,
                right?.Weight ?? 0);
    }
}
