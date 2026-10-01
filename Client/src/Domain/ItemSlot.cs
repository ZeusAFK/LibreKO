namespace LibreKO.Domain;

public struct ItemSlot
{
    public int ItemId;
    public short Durability;
    public short Count;
    public byte Flag;
    public int UniqueId;

    public ItemFlag State => (ItemFlag)Flag;

    public bool IsEmpty => ItemId == 0;
    public bool IsSealed => State is ItemFlag.Sealed or ItemFlag.Bound;

    public bool IsLinked => UniqueId != 0;

    public void Clear() { ItemId = 0; Durability = 0; Count = 0; Flag = 0; UniqueId = 0; }
}
