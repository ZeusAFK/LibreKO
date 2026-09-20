using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ShoppingMallPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const byte LetterTypeWithItem = 2;

    public readonly record struct Letter(
        int LetterId,
        byte Status,
        string Subject,
        string SenderId,
        byte Type,
        int ItemId,
        ushort Count,
        int Coins,
        int DateStamp,
        ushort DaysRemaining);

    public readonly record struct CatalogEntry(
        int Id,
        int ItemId,
        string Name,
        string Description,
        byte Category,
        int Price,
        byte PriceType);

    public readonly record struct Category(
        byte Id,
        string Name,
        string Description);

    public static Packet Result(byte storeOpcode, byte sub, byte result)
    {
        var packet = Sub(storeOpcode, sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet UnreadCount(byte storeOpcode, byte sub, byte count) =>
        Result(storeOpcode, sub, count);

    public static Packet LetterList(byte storeOpcode, byte sub, IReadOnlyCollection<Letter> letters)
    {
        var packet = Result(storeOpcode, sub, Succeeded);
        packet.WriteByte(unchecked((byte)(sbyte)letters.Count));

        foreach (var letter in letters)
        {
            packet.WriteInt(letter.LetterId);
            packet.WriteByte(letter.Status);
            packet.WriteSByteString(letter.Subject);
            packet.WriteSByteString(letter.SenderId);
            packet.WriteByte(letter.Type);

            if (letter.Type == LetterTypeWithItem)
            {
                packet.WriteInt(letter.ItemId);
                packet.WriteUShort(letter.Count);
                packet.WriteInt(letter.Coins);
            }

            packet.WriteInt(letter.DateStamp);
            packet.WriteUShort(letter.DaysRemaining);
        }

        return packet;
    }

    public static Packet LetterBody(byte storeOpcode, byte sub, int letterId, string message)
    {
        var packet = Result(storeOpcode, sub, Succeeded);
        packet.WriteInt(letterId);
        packet.WriteSByteString(message);
        return packet;
    }

    public static Packet DeletedLetters(byte storeOpcode, byte sub, IReadOnlyCollection<int> letterIds)
    {
        var packet = Sub(storeOpcode, sub);
        packet.WriteByte((byte)letterIds.Count);
        foreach (var letterId in letterIds)
            packet.WriteInt(letterId);
        return packet;
    }

    public static Packet StoreOpened(byte storeOpcode, short errorCode, short freeSlot)
    {
        var packet = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        packet.WriteByte(storeOpcode);
        packet.WriteShort(errorCode);
        packet.WriteShort(freeSlot);
        return packet;
    }

    public static Packet Catalog(byte storeOpcode, byte sub, IReadOnlyCollection<CatalogEntry> entries)
    {
        var packet = Sub(storeOpcode, sub);
        packet.WriteUShort((ushort)entries.Count);
        foreach (var entry in entries)
        {
            packet.WriteInt(entry.Id);
            packet.WriteInt(entry.ItemId);
            packet.WriteSByteString(entry.Name);
            packet.WriteSByteString(entry.Description);
            packet.WriteByte(entry.Category);
            packet.WriteInt(entry.Price);
            packet.WriteByte(entry.PriceType);
        }

        return packet;
    }

    public static Packet Categories(byte storeOpcode, byte sub, IReadOnlyCollection<Category> categories)
    {
        var packet = Sub(storeOpcode, sub);
        packet.WriteByte((byte)categories.Count);
        foreach (var category in categories)
        {
            packet.WriteByte(category.Id);
            packet.WriteSByteString(category.Name);
            packet.WriteSByteString(category.Description);
        }

        return packet;
    }

    public static Packet Balance(byte storeOpcode, byte sub, int knightCash, int usdBalance)
    {
        var packet = Sub(storeOpcode, sub);
        packet.WriteInt(knightCash);
        packet.WriteInt(usdBalance);
        return packet;
    }

    public static Packet PurchaseResult(byte storeOpcode, byte sub, byte result, int knightCash, int usdBalance)
    {
        var packet = Result(storeOpcode, sub, result);
        packet.WriteInt(knightCash);
        packet.WriteInt(usdBalance);
        return packet;
    }

    public static Packet Sub(byte storeOpcode, byte sub) => SubHeader(storeOpcode, sub);

    private static Packet SubHeader(byte storeOpcode, byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        packet.WriteByte(storeOpcode);
        packet.WriteByte(sub);
        return packet;
    }
}
