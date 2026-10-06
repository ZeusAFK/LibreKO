using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public readonly record struct AuctionLotView(byte Slot, int ItemId, long CurrentBid, string TopBidder);

public readonly record struct AuctionBidView(int Channel, byte Day, byte Slot, int ItemId, short Count, int Serial, long Total, byte Status);

public readonly record struct AuctionResultView(int ItemId, long Price, byte Status);

public static class SpecialAuctionPacketWriter
{
    public const byte TodaySub = 1;
    public const byte BidSub = 2;
    public const byte CollectSub = 3;
    public const byte MyInfoSub = 4;
    public const byte NoticeSub = 5;
    public const byte ClaimSub = 6;
    public const byte LogSub = 7;

    public const short NothingToBid = 0;
    public const short Bidding = 1;
    public const short CollectOnly = 2;
    public const short Settling = 3;

    public const short Success = 1;
    public const short DatabaseError = -1;
    public const short WrongChecks = -2;
    public const short PriceNotAccurate = -3;
    public const short NotEnoughBalance = -4;
    public const short Busy = -5;
    public const short NoSuchItem = -6;
    public const short AlreadyTopBidder = -7;
    public const short CollectFirst = -8;
    public const short NotBiddingTime = -9;

    public const short NoHistory = 0;
    public const short CoinsFull = -2;
    public const short NoRoomForChecks = -3;
    public const short WrongChannel = -9;

    public const short MyInfoSettling = 2;

    public const short NoWin = -2;
    public const short BagFull = -4;
    public const short ClaimWrongChannel = -5;

    public const byte EndedNotice = 1;
    public const int LotsPerDay = 8;

    public static Packet Today(short status, int group, int seconds, int day, IReadOnlyList<AuctionLotView> lots)
    {
        var packet = Sub(TodaySub);
        packet.WriteShort(status);
        if (status == NothingToBid)
            return packet;

        packet.WriteInt(group);
        packet.WriteInt(0);
        packet.WriteByte(0);
        packet.WriteInt(seconds);
        packet.WriteByte((byte)day);
        if (status != Bidding)
            return packet;

        packet.WriteShort((short)lots.Count);
        foreach (var lot in lots)
        {
            packet.WriteByte(lot.Slot);
            packet.WriteInt(lot.ItemId);
            packet.WriteLong(lot.CurrentBid);
            packet.WriteSByteString(lot.TopBidder);
        }
        return packet;
    }

    public static Packet BidResult(short result)
    {
        var packet = Sub(BidSub);
        packet.WriteShort(result);
        packet.WriteShort(0);
        return packet;
    }

    public static Packet CollectResult(short result)
    {
        var packet = Sub(CollectSub);
        packet.WriteShort(result);
        packet.WriteShort(0);
        if (result > 0)
        {
            packet.WriteInt(0);
            packet.WriteLong(0);
        }
        return packet;
    }

    public static Packet MyInfo(short result, IReadOnlyList<AuctionBidView> rows)
    {
        var packet = Sub(MyInfoSub);
        packet.WriteShort(result);
        if (result != Success)
            return packet;

        packet.WriteByte((byte)rows.Count);
        foreach (var row in rows)
        {
            packet.WriteInt(row.Channel);
            packet.WriteByte(row.Day);
            packet.WriteByte(row.Slot);
            packet.WriteInt(row.ItemId);
            packet.WriteShort(row.Count);
            packet.WriteInt(row.Serial);
            packet.WriteByte(0);
            packet.WriteLong(row.Total);
            packet.WriteByte(row.Status);
        }
        return packet;
    }

    public static Packet Ended(IReadOnlyList<AuctionResultView> results)
    {
        var packet = Sub(NoticeSub);
        packet.WriteByte(EndedNotice);
        packet.WriteShort(0);
        packet.WriteByte(0);
        packet.WriteByte((byte)Math.Min(results.Count, LotsPerDay));
        foreach (var result in results.Take(LotsPerDay))
            WriteResult(packet, result);
        return packet;
    }

    public static Packet ClaimResult(short result)
    {
        var packet = Sub(ClaimSub);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet Log(IReadOnlyList<IReadOnlyList<AuctionResultView>> days)
    {
        var packet = Sub(LogSub);
        packet.WriteShort((short)days.Count);
        foreach (var day in days)
        {
            for (var slot = 0; slot < LotsPerDay; slot++)
                WriteResult(packet, slot < day.Count ? day[slot] : default);
        }
        return packet;
    }

    private static void WriteResult(Packet packet, AuctionResultView result)
    {
        packet.WriteInt(result.ItemId);
        packet.WriteLong(result.Price);
        packet.WriteByte(result.Status);
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_AUCTION);
        packet.WriteByte(sub);
        return packet;
    }
}
