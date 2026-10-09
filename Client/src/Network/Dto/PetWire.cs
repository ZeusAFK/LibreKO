using LibreKO.Domain;

namespace LibreKO.Network;

public readonly record struct HatchedPet(int ItemId, int BagSlot, PetItemInfo Info);

public readonly record struct TransformedPet(HatchedPet Pet, int MaterialItemId, int MaterialSlot);

public readonly record struct PetFoodReply(bool Succeeded, int BagSlot, int ItemId, short CountLeft, short Increase)
{
    public bool TryLower(ItemSlot held, out ItemSlot left)
    {
        left = default;
        if (!Succeeded || held.ItemId != ItemId) return false;
        if (CountLeft > 0)
        {
            left = held;
            left.Count = CountLeft;
        }
        return true;
    }
}

public readonly record struct PetFeedRequest(int BagSlot, int ItemId)
{
    public bool Matches(PetFoodReply reply) => reply.BagSlot == BagSlot && reply.ItemId == ItemId;

    public PetFoodReply Refusal => new(false, BagSlot, ItemId, 0, 0);
}

public readonly record struct PetIncubationRequest(bool Transform, int ItemId, int BagSlot, int UniqueId, int MaterialItemId, int MaterialSlot)
{
    public const int AnyFamiliar = 0;
    private const int NoMaterialItem = 0;
    private const int NoMaterialSlot = 0;

    public static PetIncubationRequest Hatch(int eggItemId, int bagSlot) =>
        new(false, eggItemId, bagSlot, AnyFamiliar, NoMaterialItem, NoMaterialSlot);

    public static PetIncubationRequest Transformation(int petItemId, int petSlot, int uniqueId, int materialItemId, int materialSlot) =>
        new(true, petItemId, petSlot, uniqueId, materialItemId, materialSlot);

    public bool Matches(HatchedPet pet) => !Transform && pet.BagSlot == BagSlot;

    public bool Matches(TransformedPet result) =>
        Transform && result.Pet.BagSlot == BagSlot && result.Pet.ItemId != ItemId
        && (UniqueId == AnyFamiliar || result.Pet.Info.Index == UniqueId)
        && result.MaterialItemId == MaterialItemId && result.MaterialSlot == MaterialSlot;
}

public static class PetWire
{
    public const int ItemRecordBytes = 19;
    public const byte HatchRefused = 0;
    public const byte HatchSucceeded = 1;
    public const byte HatchNameTaken = 2;
    public const int NameTakenCode = -1;
    public const int MalformedReplyCode = int.MinValue;
    public const byte FoodRefused = 0;
    public const byte FoodSucceeded = 1;
    public const int FoodHeaderBytes = 6;
    private const int FoodSuccessTailBytes = 10;
    private const int HatchSuccessBytes = 17;
    private const int HatchNameTailBytes = 6;
    private const int TransformTailBytes = 6;

    public static bool TryReadFood(Packet p, out PetFoodReply reply)
    {
        reply = default;
        if (p.RemainingBytes < FoodHeaderBytes) return false;
        byte result = p.ReadByte();
        int slot = p.ReadByte();
        int item = p.ReadInt();
        if (result is not (FoodRefused or FoodSucceeded) || slot >= InventoryConstants.HaveMax || item <= 0) return false;
        if (result == FoodRefused)
        {
            reply = new PetFoodReply(false, slot, item, 0, 0);
            return true;
        }
        if (p.RemainingBytes < FoodSuccessTailBytes) return false;
        short count = p.ReadShort();
        p.ReadShort();
        p.ReadInt();
        short increase = p.ReadShort();
        if (count < 0 || count > Inventory.StackMax || increase < 0 || increase > PetSheet.MaxSatisfaction) return false;
        reply = new PetFoodReply(true, slot, item, count, increase);
        return true;
    }

    public static PetFoodReply ReadFoodFor(Packet p, PetFeedRequest request) =>
        TryReadFood(p, out var reply) && request.Matches(reply) ? reply : request.Refusal;

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
        failure = MalformedReplyCode;
        if (p.RemainingBytes < 1) return false;
        byte result = p.ReadByte();
        if (result == HatchNameTaken)
        {
            failure = NameTakenCode;
            return false;
        }
        if (result == HatchRefused)
        {
            if (p.RemainingBytes >= 1) failure = p.ReadByte();
            return false;
        }
        if (result != HatchSucceeded || p.RemainingBytes < HatchSuccessBytes) return false;
        int itemId = p.ReadInt();
        int bagSlot = p.ReadByte();
        int index = p.ReadInt();
        int nameLength = p.ReadShort();
        if (nameLength is < 1 or > PetSheet.NameMaxLength || p.RemainingBytes < nameLength + HatchNameTailBytes) return false;
        string name = System.Text.Encoding.ASCII.GetString(p.ReadBytes(nameLength));
        int attack = p.ReadByte();
        int level = p.ReadByte();
        int expPercent = p.ReadUShort();
        int satisfaction = p.ReadShort();
        if (itemId <= 0 || index <= 0 || bagSlot >= InventoryConstants.HaveMax || level == 0) return false;
        hatched = new HatchedPet(itemId, bagSlot, new PetItemInfo(index, name, attack, level, expPercent, satisfaction));
        failure = 0;
        return true;
    }

    public static bool TryReadHatchFor(Packet p, PetIncubationRequest request, out HatchedPet hatched, out int failure)
    {
        if (!TryReadHatch(p, out hatched, out failure)) return false;
        if (request.Matches(hatched)) return true;
        failure = MalformedReplyCode;
        return false;
    }

    public static bool TryReadTransformFor(Packet p, PetIncubationRequest request, out TransformedPet transformed, out int failure)
    {
        if (!TryReadTransform(p, out transformed, out failure)) return false;
        if (request.Matches(transformed)) return true;
        failure = MalformedReplyCode;
        return false;
    }

    public static bool TryReadTransform(Packet p, out TransformedPet transformed, out int failure)
    {
        transformed = default;
        if (!TryReadHatch(p, out var pet, out failure)) return false;
        if (p.RemainingBytes < TransformTailBytes)
        {
            failure = MalformedReplyCode;
            return false;
        }
        p.ReadByte();
        int materialItemId = p.ReadInt();
        int materialSlot = p.ReadByte();
        if (materialItemId <= 0 || materialSlot >= InventoryConstants.HaveMax || materialSlot == pet.BagSlot)
        {
            failure = MalformedReplyCode;
            return false;
        }
        transformed = new TransformedPet(pet, materialItemId, materialSlot);
        return true;
    }
}
