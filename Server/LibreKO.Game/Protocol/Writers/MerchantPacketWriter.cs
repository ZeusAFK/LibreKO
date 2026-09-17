using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MerchantPacketWriter
{
    public const ushort Failed = 0;
    public const ushort Succeeded = 1;

    public readonly record struct StallItem(int ItemId, ushort Count, short Durability, int Price);

    public readonly record struct StallOwner(int CharacterId, bool IsBuyingStall, byte Flags);

    public static Packet Result(MerchantSubOpcode sub, ushort result)
    {
        var packet = Sub(sub);
        packet.WriteUShort(result);
        return packet;
    }

    public static Packet Refused(MerchantSubOpcode sub, MerchantResult result)
    {
        var packet = Sub(sub);
        packet.WriteShort((short)result);
        return packet;
    }

    public static Packet ItemAdded(
        MerchantSubOpcode sub, int itemId, ushort count, short durability, int price, byte sourceSlot, byte stallSlot)
    {
        var packet = Sub(sub);
        packet.WriteUShort(Succeeded);
        packet.WriteInt(itemId);
        packet.WriteUShort(count);
        packet.WriteShort(durability);
        packet.WriteInt(price);
        packet.WriteByte(sourceSlot);
        packet.WriteByte(stallSlot);
        return packet;
    }

    public static Packet ItemCancelled(MerchantSubOpcode sub, byte stallSlot)
    {
        var packet = Sub(sub);
        packet.WriteUShort(Succeeded);
        packet.WriteByte(stallSlot);
        return packet;
    }

    public static Packet StallContents(MerchantSubOpcode sub, int merchantId, IReadOnlyList<StallItem?> items)
    {
        var packet = Sub(sub);
        packet.WriteUShort(Succeeded);
        packet.WriteInt(merchantId);

        foreach (var entry in items)
        {
            var item = entry ?? default;
            packet.WriteInt(item.ItemId);
            packet.WriteUShort(item.Count);
            packet.WriteShort(item.Durability);
            packet.WriteInt(item.Price);
            packet.WriteInt(0);
        }

        return packet;
    }

    public static Packet ItemBought(
        MerchantSubOpcode sub, int itemId, ushort remainingCount, byte merchantSlot, byte buyerSlot)
    {
        var packet = Sub(sub);
        packet.WriteUShort(Succeeded);
        packet.WriteInt(itemId);
        packet.WriteUShort(remainingCount);
        packet.WriteByte(merchantSlot);
        packet.WriteByte(buyerSlot);
        return packet;
    }

    public static Packet ItemSold(MerchantSubOpcode sub, int itemId, string buyerName)
    {
        var packet = Sub(sub);
        packet.WriteInt(itemId);
        packet.WriteString(buyerName);
        return packet;
    }

    public static Packet OpenResult(MerchantSubOpcode sub, short result)
    {
        var packet = Sub(sub);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet StallClosed(int characterId)
    {
        var packet = Sub(MerchantSubOpcode.Close);
        packet.WriteInt(characterId);
        return packet;
    }

    public static Packet StallsInView(MerchantInOut inOutType, IReadOnlyCollection<StallOwner> owners)
    {
        var packet = InOut(inOutType);
        packet.WriteShort((short)owners.Count);

        foreach (var owner in owners)
        {
            packet.WriteInt(owner.CharacterId);
            packet.WriteByte(owner.IsBuyingStall ? (byte)1 : (byte)0);
            packet.WriteByte(owner.Flags);
        }

        return packet;
    }

    public static Packet StallInserted(
        ushort result, string advert, int characterId, byte flags, IReadOnlyList<int> itemIds)
    {
        var packet = Sub(MerchantSubOpcode.Insert);
        packet.WriteUShort(result);
        packet.WriteString(advert);
        packet.WriteInt(characterId);
        packet.WriteByte(flags);

        for (var slot = 0; slot < MerchantPacketConstants.StallSlots; slot++)
            packet.WriteInt(slot < itemIds.Count ? itemIds[slot] : 0);

        return packet;
    }

    public static Packet InsertRefused() =>
        StallInserted(Failed, string.Empty, 0, 0, []);

    public const ushort StallOpenRefused = 7;

    public static Packet StallWindowOpen(
        MerchantSubOpcode sub, ushort result, int merchantId, string shopName)
    {
        var packet = Sub(sub);
        if (sub == MerchantSubOpcode.SellingStallRequest) packet.WriteUShort(result);
        else packet.WriteByte((byte)result);

        if (result != Succeeded) return packet;

        packet.WriteInt(merchantId);
        packet.WriteSByteString(shopName);
        return packet;
    }

    public static Packet StallContentsAllowed(MerchantSubOpcode sub, bool allowed)
    {
        var packet = Sub(sub);
        packet.WriteByte(allowed ? (byte)1 : (byte)0);
        return packet;
    }

    public static Packet BuyOpenResult(BuyingMerchantResult result) =>
        BuyResult(MerchantSubOpcode.BuyOpen, result);

    public static Packet BuyInsertResult(BuyingMerchantResult result) =>
        BuyResult(MerchantSubOpcode.BuyInsert, result);

    public static Packet BuyPurchaseResult(BuyingMerchantResult result) =>
        BuyResult(MerchantSubOpcode.BuyBuy, result);

    public static Packet WantedList(int merchantId, IReadOnlyList<StallItem?> items)
    {
        var packet = Sub(MerchantSubOpcode.BuyList);
        packet.WriteByte((byte)BuyingMerchantResult.Accepted);
        packet.WriteInt(merchantId);

        foreach (var entry in items)
        {
            var item = entry ?? default;
            packet.WriteInt(item.ItemId);
            packet.WriteUShort(item.Count);
            packet.WriteShort(item.Durability);
            packet.WriteInt(item.Price);
        }

        return packet;
    }

    public static Packet WantedItemSold(
        byte wantedSlot, ushort wantedRemaining, byte sellerSlot, ushort sellerRemaining)
    {
        var packet = Sub(MerchantSubOpcode.BuySold);
        packet.WriteByte((byte)BuyingMerchantResult.Accepted);
        packet.WriteByte(wantedSlot);
        packet.WriteUShort(wantedRemaining);
        packet.WriteByte(sellerSlot);
        packet.WriteUShort(sellerRemaining);
        return packet;
    }

    public static Packet WantedItemBought(byte wantedSlot, ushort wantedRemaining, string sellerName)
    {
        var packet = Sub(MerchantSubOpcode.BuyBought);
        packet.WriteByte(wantedSlot);
        packet.WriteUShort(wantedRemaining);
        packet.WriteString(sellerName);
        return packet;
    }

    public static Packet BuyingStallClosed(int characterId)
    {
        var packet = Sub(MerchantSubOpcode.BuyClose);
        packet.WriteInt(characterId);
        return packet;
    }

    public static Packet BuyingStallInserted(int characterId, IReadOnlyList<int> itemIds)
    {
        var packet = Sub(MerchantSubOpcode.BuyRegionInsert);
        packet.WriteInt(characterId);

        for (var slot = 0; slot < MerchantPacketConstants.StallDisplaySlots; slot++)
            packet.WriteInt(slot < itemIds.Count ? itemIds[slot] : 0);

        return packet;
    }

    public static Packet StallList(StallOwner owner, IReadOnlyList<int> itemIds)
    {
        var packet = Sub(MerchantSubOpcode.StallList);
        packet.WriteByte((byte)BuyingMerchantResult.Accepted);
        packet.WriteInt(owner.CharacterId);
        packet.WriteByte(owner.IsBuyingStall ? (byte)1 : (byte)0);
        packet.WriteByte(owner.Flags);

        var shown = (owner.Flags & 7) != 0
            ? MerchantPacketConstants.StallDisplaySlotsPremium
            : MerchantPacketConstants.StallDisplaySlots;

        for (var slot = 0; slot < shown; slot++)
            packet.WriteInt(slot < itemIds.Count ? itemIds[slot] : 0);

        return packet;
    }

    private static Packet BuyResult(MerchantSubOpcode sub, BuyingMerchantResult result)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)result);
        return packet;
    }

    private static Packet InOut(MerchantInOut inOutType)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT_INOUT);
        packet.WriteByte((byte)inOutType);
        return packet;
    }

    private static Packet Sub(MerchantSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
