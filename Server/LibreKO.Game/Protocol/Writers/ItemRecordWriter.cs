using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public readonly record struct PetItemInfo(
    int Index, string Name, byte Attack, byte Level, ushort ExpPercent, short Satisfaction)
{
    public const int ExpPercentScale = 10_000;

    public static PetItemInfo From(Pet pet, PetLevelData? level) => new(
        pet.Id, pet.Name, (byte)Math.Clamp((int)(level?.Attack ?? 0), 0, byte.MaxValue), pet.Level,
        ExpPercentOf(pet.Exp, level), pet.Satisfaction);

    public static ushort ExpPercentOf(long exp, PetLevelData? level) =>
        level is not { Exp: > 0 }
            ? (ushort)0
            : (ushort)Math.Clamp(exp * ExpPercentScale / level.Exp, 0, ExpPercentScale);
}

public static class ItemRecordWriter
{
    public const short NoRentalMinutes = 0;
    public const int NoUniqueId = 0;
    public const int NoExpiry = 0;
    public const byte PetBlockTail = 0;

    public static void Write(Packet packet, int itemId, short durability, short count, byte flag, PetItemInfo? pet)
    {
        packet.WriteInt(itemId);
        packet.WriteShort(durability);
        packet.WriteShort(count);
        packet.WriteByte(flag);
        packet.WriteShort(NoRentalMinutes);
        if (pet is { } info)
        {
            packet.WriteInt(info.Index);
            WritePetBlock(packet, info);
        }
        else
        {
            packet.WriteInt(NoUniqueId);
        }

        packet.WriteInt(NoExpiry);
    }

    public static void Write(Packet packet, ItemSlot slot) =>
        Write(packet, slot.ItemId, slot.Durability, (short)slot.Count, slot.Flag, null);

    private static void WritePetBlock(Packet packet, PetItemInfo info)
    {
        packet.WriteString(info.Name);
        packet.WriteByte(info.Attack);
        packet.WriteByte(info.Level);
        packet.WriteUShort(info.ExpPercent);
        packet.WriteShort(info.Satisfaction);
        packet.WriteByte(PetBlockTail);
    }
}
