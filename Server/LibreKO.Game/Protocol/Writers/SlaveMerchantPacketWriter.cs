using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum SlaveMerchantSub : byte
{
    Status = 1,
    Collect = 2,
    Redeem = 3,
    Time = 4,
    Deploy = 5,
    Sold = 6,
    Closed = 7,
}

public enum SlaveMerchantClosedReason : byte
{
    SoldOut = 1,
    OutOfTime = 2,
}

public enum SlaveMerchantDeployResult : byte
{
    Deployed = 1,
    NoTime = 2,
    AlreadyOpen = 3,
    ItemsMoved = 4,
    Failed = 5,
}

public enum SlaveMerchantCollectResult : byte
{
    Failed = 0,
    Collected = 1,
}

public enum SlaveMerchantRedeemResult : byte
{
    Failed = 0,
    Added = 1,
}

public readonly record struct SlaveMerchantLine(int ItemId, ushort Count, int Price);

public static class SlaveMerchantPacketWriter
{
    public static Packet Status(bool open, int hours, long coins, IReadOnlyList<SlaveMerchantLine> lines)
    {
        var packet = Sub(SlaveMerchantSub.Status);
        packet.WriteByte((byte)(open ? 1 : 0));
        packet.WriteUShort(ClampHours(hours));
        packet.WriteUInt(ClampCoins(coins));
        packet.WriteByte((byte)MerchantPacketConstants.StallSlots);
        for (var i = 0; i < MerchantPacketConstants.StallSlots; i++)
        {
            var line = i < lines.Count ? lines[i] : default;
            packet.WriteInt(line.ItemId);
            packet.WriteUShort(line.Count);
            packet.WriteInt(line.Price);
        }

        return packet;
    }

    public static Packet Collected(long coinsPaid, int itemsReturned, int itemsKept, long coinsKept) =>
        Collected(SlaveMerchantCollectResult.Collected, coinsPaid, itemsReturned, itemsKept, coinsKept);

    public static Packet CollectFailed() => Collected(SlaveMerchantCollectResult.Failed, 0, 0, 0, 0);

    private static Packet Collected(SlaveMerchantCollectResult result, long coinsPaid, int itemsReturned, int itemsKept, long coinsKept)
    {
        var packet = Sub(SlaveMerchantSub.Collect);
        packet.WriteByte((byte)result);
        packet.WriteUInt(ClampCoins(coinsPaid));
        packet.WriteByte((byte)Math.Clamp(itemsReturned, 0, byte.MaxValue));
        packet.WriteByte((byte)Math.Clamp(itemsKept, 0, byte.MaxValue));
        packet.WriteUInt(ClampCoins(coinsKept));
        return packet;
    }

    public static Packet Redeemed(SlaveMerchantRedeemResult result, int hours)
    {
        var packet = Sub(SlaveMerchantSub.Redeem);
        packet.WriteByte((byte)result);
        packet.WriteUShort(ClampHours(hours));
        return packet;
    }

    public static Packet Time(int hours)
    {
        var packet = Sub(SlaveMerchantSub.Time);
        packet.WriteUShort(ClampHours(hours));
        return packet;
    }

    public static Packet Deploy(SlaveMerchantDeployResult result)
    {
        var packet = Sub(SlaveMerchantSub.Deploy);
        packet.WriteByte((byte)result);
        return packet;
    }

    public static Packet Sold(int itemId, ushort count, long coins)
    {
        var packet = Sub(SlaveMerchantSub.Sold);
        packet.WriteInt(itemId);
        packet.WriteUShort(count);
        packet.WriteUInt(ClampCoins(coins));
        return packet;
    }

    public static Packet Closed(SlaveMerchantClosedReason reason)
    {
        var packet = Sub(SlaveMerchantSub.Closed);
        packet.WriteByte((byte)reason);
        return packet;
    }

    private static Packet Sub(SlaveMerchantSub sub)
    {
        var packet = new Packet(GameOpcodes.GS_SLAVE_MERCHANT);
        packet.WriteByte((byte)sub);
        return packet;
    }

    private static ushort ClampHours(int hours) => (ushort)Math.Clamp(hours, 0, ushort.MaxValue);

    private static uint ClampCoins(long coins) => (uint)Math.Clamp(coins, 0L, uint.MaxValue);
}
