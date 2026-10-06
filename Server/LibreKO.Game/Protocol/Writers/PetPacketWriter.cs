using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public enum HatchResult : byte
{
    Refused = 0,
    Succeeded = 1,
    NameTaken = 2,
}

public enum HatchRefusal : byte
{
    Failed = 1,
    InvalidName = 2,
    CannotHatch = 3,
    LimitExceeded = 4,
}

public static class PetPacketWriter
{
    public const short Succeeded = 1;
    public const byte FoodFailed = 0;
    public const byte FoodSucceeded = 1;
    public const short NoFoodDurability = 0;
    public const int NoFoodUniqueId = 0;
    public const byte PetClassDefault = 101;
    public const int ResistanceCount = 6;
    public const byte TargetHpTail = 0;
    public const int NameTakenFillerInt = 0;
    public const byte NameTakenFillerByte = 0;
    public const byte TransformedPad = 0;

    public readonly record struct SummonInfo(
        int Index, string Name, byte Class, byte Level, ushort ExpPercent,
        short MaxHp, short Hp, short MaxMp, short Mp, short Satisfaction,
        short Attack, short Defence, byte Resist, ItemSlot[] Items);

    public static SummonInfo InfoOf(PetState pet, PetLevelData? level) => new(
        pet.Record.Id, pet.Record.Name, pet.Record.Class, pet.Record.Level,
        PetItemInfo.ExpPercentOf(pet.Record.Exp, level),
        level?.MaxHp ?? pet.Record.Hp, pet.Record.Hp, level?.MaxMp ?? pet.Record.Mp, pet.Record.Mp,
        pet.Record.Satisfaction, pet.Npc?.Attack1 ?? level?.Attack ?? 0, pet.Npc?.Ac ?? level?.Defence ?? 0,
        level?.Resist ?? 0, pet.Items);

    public static Packet Summoned(SummonInfo info)
    {
        var packet = ModeHeader(PetMode.Summoned, Succeeded);
        packet.WriteInt(info.Index);
        packet.WriteString(info.Name);
        packet.WriteByte(info.Class);
        packet.WriteByte(info.Level);
        packet.WriteUShort(info.ExpPercent);
        packet.WriteShort(info.MaxHp);
        packet.WriteShort(info.Hp);
        packet.WriteShort(info.MaxMp);
        packet.WriteShort(info.Mp);
        packet.WriteShort(info.Satisfaction);
        packet.WriteShort(info.Attack);
        packet.WriteShort(info.Defence);
        for (var i = 0; i < ResistanceCount; i++)
            packet.WriteByte(info.Resist);
        foreach (var item in info.Items)
            ItemRecordWriter.Write(packet, item);
        return packet;
    }

    public static Packet ModeChanged(PetMode mode, short result = Succeeded) => ModeHeader(mode, result);

    public static Packet Died(int index)
    {
        var packet = ModeHeader(PetMode.Died, Succeeded);
        packet.WriteInt(index);
        return packet;
    }

    public static Packet HpChanged(short maxHp, short hp, int npcId)
    {
        var packet = Function(PetFunction.Hp);
        packet.WriteShort(maxHp);
        packet.WriteShort(hp);
        packet.WriteInt(npcId);
        return packet;
    }

    public static Packet MpChanged(short maxMp, short mp, int npcId)
    {
        var packet = Function(PetFunction.Mp);
        packet.WriteShort(maxMp);
        packet.WriteShort(mp);
        packet.WriteInt(npcId);
        return packet;
    }

    public static Packet TargetHp(int targetId, int maxHp, int hp, short damage)
    {
        var packet = Function(PetFunction.TargetHp);
        packet.WriteInt(targetId);
        packet.WriteByte(TargetHpTail);
        packet.WriteInt(maxHp);
        packet.WriteInt(hp);
        packet.WriteShort(damage);
        return packet;
    }

    public static Packet ExpChanged(long gained, ushort expPercent, byte level, short satisfaction)
    {
        var packet = Function(PetFunction.Exp);
        packet.WriteLong(gained);
        packet.WriteUShort(expPercent);
        packet.WriteByte(level);
        packet.WriteShort(satisfaction);
        return packet;
    }

    public static Packet LevelUp(int npcId)
    {
        var packet = Function(PetFunction.LevelUp);
        packet.WriteInt(npcId);
        return packet;
    }

    public static Packet Satisfaction(short satisfaction, int npcId)
    {
        var packet = Function(PetFunction.Satisfaction);
        packet.WriteShort(satisfaction);
        packet.WriteInt(npcId);
        return packet;
    }

    public static Packet Fed(byte slot, int itemId, short countLeft, short increase)
    {
        var packet = Function(PetFunction.Food);
        packet.WriteByte(FoodSucceeded);
        packet.WriteByte(slot);
        packet.WriteInt(itemId);
        packet.WriteShort(countLeft);
        packet.WriteShort(NoFoodDurability);
        packet.WriteInt(NoFoodUniqueId);
        packet.WriteShort(increase);
        return packet;
    }

    public static Packet FoodRefused(byte slot, int itemId)
    {
        var packet = Function(PetFunction.Food);
        packet.WriteByte(FoodFailed);
        packet.WriteByte(slot);
        packet.WriteInt(itemId);
        return packet;
    }

    public static Packet Hatched(int itemId, byte slot, int index, string name, byte petClass, byte level,
        ushort expPercent, short satisfaction)
    {
        var packet = UpgradeHeader(ItemUpgradeSubOpcode.PetHatching, HatchResult.Succeeded);
        WriteFamiliarItem(packet, itemId, slot, index, name, petClass, level, expPercent, satisfaction);
        return packet;
    }

    public static Packet HatchRefused(HatchRefusal refusal)
    {
        var packet = UpgradeHeader(ItemUpgradeSubOpcode.PetHatching, HatchResult.Refused);
        packet.WriteByte((byte)refusal);
        return packet;
    }

    public static Packet HatchNameTaken()
    {
        var packet = UpgradeHeader(ItemUpgradeSubOpcode.PetHatching, HatchResult.NameTaken);
        packet.WriteInt(NameTakenFillerInt);
        packet.WriteByte(NameTakenFillerByte);
        return packet;
    }

    public static Packet Transformed(int itemId, byte slot, int index, string name, byte petClass, byte level,
        ushort expPercent, short satisfaction, int materialItemId, byte materialSlot)
    {
        var packet = UpgradeHeader(ItemUpgradeSubOpcode.PetTransform, HatchResult.Succeeded);
        WriteFamiliarItem(packet, itemId, slot, index, name, petClass, level, expPercent, satisfaction);
        packet.WriteByte(TransformedPad);
        packet.WriteInt(materialItemId);
        packet.WriteByte(materialSlot);
        return packet;
    }

    public static Packet TransformRefused(HatchRefusal refusal)
    {
        var packet = UpgradeHeader(ItemUpgradeSubOpcode.PetTransform, HatchResult.Refused);
        packet.WriteByte((byte)refusal);
        return packet;
    }

    private static void WriteFamiliarItem(Packet packet, int itemId, byte slot, int index, string name, byte petClass,
        byte level, ushort expPercent, short satisfaction)
    {
        packet.WriteInt(itemId);
        packet.WriteByte(slot);
        packet.WriteInt(index);
        packet.WriteString(name);
        packet.WriteByte(petClass);
        packet.WriteByte(level);
        packet.WriteUShort(expPercent);
        packet.WriteShort(satisfaction);
    }

    private static Packet UpgradeHeader(ItemUpgradeSubOpcode subOpcode, HatchResult result)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)subOpcode);
        packet.WriteByte((byte)result);
        return packet;
    }

    private static Packet ModeHeader(PetMode mode, short result)
    {
        var packet = Function(PetFunction.Mode);
        packet.WriteByte((byte)mode);
        packet.WriteShort(result);
        return packet;
    }

    private static Packet Function(PetFunction function)
    {
        var packet = new Packet(GameOpcodes.GS_PET);
        packet.WriteByte((byte)PetSubOpcode.ModeFunction);
        packet.WriteByte((byte)function);
        return packet;
    }
}
