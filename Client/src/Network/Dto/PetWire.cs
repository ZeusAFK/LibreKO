using LibreKO.Domain;

namespace LibreKO.Network;

public readonly record struct HatchedPet(int ItemId, int BagSlot, PetItemInfo Info);

public readonly record struct TransformedPet(HatchedPet Pet, int MaterialItemId, int MaterialSlot);

public static class PetWire
{
    public const int ItemRecordBytes = 19;
    public const byte HatchSucceeded = 1;
    public const byte HatchNameTaken = 2;
    public const int NameTakenCode = -1;
    private const int HatchSuccessBytes = 9;
    private const int TransformTailBytes = 6;

    public static ItemSlot ReadItemRecord(Packet p, out PetItemInfo? pet)
    {
        int itemId = p.ReadInt();
        short durability = p.ReadShort();
        short count = p.ReadShort();
        byte flag = p.ReadByte();
        p.ReadShort();
        int uniqueId = p.ReadInt();
        pet = null;
        if (uniqueId != 0)
        {
            string name = p.ReadString();
            int attack = p.ReadByte();
            int level = p.ReadByte();
            int expPercent = p.ReadUShort();
            int satisfaction = p.ReadShort();
            p.ReadByte();
            pet = new PetItemInfo(uniqueId, name, attack, level, expPercent, satisfaction);
        }
        p.ReadInt();
        return new ItemSlot { ItemId = itemId, Durability = durability, Count = count, Flag = flag, UniqueId = uniqueId };
    }

    public static PetSheet ReadSheet(Packet p)
    {
        var sheet = new PetSheet
        {
            Index = p.ReadInt(),
            Name = p.ReadString(),
            Class = p.ReadByte(),
            Level = p.ReadByte(),
            ExpPercent = p.ReadUShort(),
            MaxHp = p.ReadShort(),
            Hp = p.ReadShort(),
            MaxMp = p.ReadShort(),
            Mp = p.ReadShort(),
            Satisfaction = p.ReadShort(),
            Attack = p.ReadShort(),
            Defence = p.ReadShort(),
        };
        for (int i = 0; i < PetSheet.ResistanceCount; i++)
            sheet.Resists[i] = p.ReadByte();
        for (int i = 0; i < PetSheet.InventorySize && p.RemainingBytes >= ItemRecordBytes; i++)
            sheet.Items[i] = ReadItemRecord(p, out _);
        return sheet;
    }

    public static bool TryReadHatch(Packet p, out HatchedPet hatched, out int failure)
    {
        hatched = default;
        failure = 0;
        if (p.RemainingBytes < 1) return false;
        byte result = p.ReadByte();
        if (result == HatchNameTaken)
        {
            failure = NameTakenCode;
            return false;
        }
        if (result != HatchSucceeded)
        {
            failure = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            return false;
        }
        if (p.RemainingBytes < HatchSuccessBytes) return false;
        int itemId = p.ReadInt();
        int bagSlot = p.ReadByte();
        int index = p.ReadInt();
        string name = p.ReadString();
        int attack = p.ReadByte();
        int level = p.ReadByte();
        int expPercent = p.ReadUShort();
        int satisfaction = p.ReadShort();
        hatched = new HatchedPet(itemId, bagSlot, new PetItemInfo(index, name, attack, level, expPercent, satisfaction));
        return true;
    }

    public static bool TryReadTransform(Packet p, out TransformedPet transformed, out int failure)
    {
        transformed = default;
        if (!TryReadHatch(p, out var pet, out failure)) return false;
        if (p.RemainingBytes < TransformTailBytes)
        {
            transformed = new TransformedPet(pet, 0, 0);
            return true;
        }
        p.ReadByte();
        int materialItemId = p.ReadInt();
        int materialSlot = p.ReadByte();
        transformed = new TransformedPet(pet, materialItemId, materialSlot);
        return true;
    }
}
