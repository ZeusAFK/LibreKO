namespace LibreKO;

public static class ItemSounds
{
    public static void Moved(int itemId, int from, int to)
    {
        if (from >= Inventory.GridStart && to >= Inventory.GridStart) { Audio.PlayUi(Sfx.UiButton); return; }
        var def = ItemData.Get(itemId);
        Audio.PlayUi(def != null && Inventory.IsTwoHandedSlotType(def.Slot) ? Sfx.ItemWeapon : Sfx.ItemArmor);
    }
}
