namespace LibreKO.Domain;

public enum IconWash : byte { None, Exhausted, Rented, Duplicate, Sealed, Expired }

public enum IconBadge : byte { None, NotBound, Bound }

public static class IconVeil
{
    public static ItemFlag ShownFlag(ItemFlag flag, bool bindsOnEquip) =>
        flag == ItemFlag.Unsealed && bindsOnEquip ? ItemFlag.NotBound : flag;

    // The server stops counting an expired item where it lies, so it stays in its slot, painted red.
    public static IconWash Wash(ItemSlot slot, long nowUnixSeconds)
    {
        if (slot.IsEmpty) return IconWash.None;
        if (slot.HasExpired(nowUnixSeconds)) return IconWash.Expired;
        if (slot.Durability == 0) return IconWash.Exhausted;
        return slot.State switch
        {
            ItemFlag.Rented or ItemFlag.CharacterSeal => IconWash.Rented,
            ItemFlag.Duplicate => IconWash.Duplicate,
            ItemFlag.Sealed or ItemFlag.SealedPremium => IconWash.Sealed,
            _ => IconWash.None,
        };
    }

    public static IconBadge Badge(ItemFlag shownFlag) => shownFlag switch
    {
        ItemFlag.NotBound => IconBadge.NotBound,
        ItemFlag.Bound => IconBadge.Bound,
        _ => IconBadge.None,
    };
}
