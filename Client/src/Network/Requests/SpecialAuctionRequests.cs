namespace LibreKO.Network.Requests;

public interface ISpecialAuctionRequests
{
    void AuctionRequest(SpecialAuctionSubOpcode sub);
    void AuctionBid(AuctionLot lot, IReadOnlyList<int> checkSlots, int millions);
    void AuctionRetract(AuctionBidRow row);
    void AuctionReceive(AuctionBidRow row);
}

public sealed class SpecialAuctionRequests : ISpecialAuctionRequests
{
    private readonly IPacketSink sink;

    public SpecialAuctionRequests(IPacketSink sink) => this.sink = sink;

    public void AuctionRequest(SpecialAuctionSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte((byte)sub);
        sink.Send(p);
    }

    public void AuctionBid(AuctionLot lot, IReadOnlyList<int> checkSlots, int millions)
    {
        long coins = millions * SpecialAuction.CoinUnit;
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte((byte)SpecialAuctionSubOpcode.Bid);
        p.WriteByte((byte)lot.Slot);
        p.WriteInt(lot.ItemId);
        p.WriteShort((short)lot.Count);
        p.WriteByte((byte)checkSlots.Count);
        foreach (int abs in checkSlots)
        {
            p.WriteInt(SpecialAuction.MythrilCheck);
            p.WriteByte((byte)(abs - Inventory.GridStart));
        }
        p.WriteUInt((uint)coins);
        p.WriteLong(coins + checkSlots.Count * SpecialAuction.CheckValue);
        sink.Send(p);
    }

    public void AuctionRetract(AuctionBidRow row)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte((byte)SpecialAuctionSubOpcode.Collect);
        WriteAuctionRowKey(p, row);
        p.WriteInt(row.Serial);
        p.WriteByte(row.Sequence);
        sink.Send(p);
    }

    public void AuctionReceive(AuctionBidRow row)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte((byte)SpecialAuctionSubOpcode.Claim);
        WriteAuctionRowKey(p, row);
        sink.Send(p);
    }

    private static void WriteAuctionRowKey(Packet p, AuctionBidRow row)
    {
        p.WriteInt(row.Channel);
        p.WriteByte(row.Day);
        p.WriteByte(row.Slot);
        p.WriteInt(row.ItemId);
        p.WriteShort(row.Count);
    }
}
