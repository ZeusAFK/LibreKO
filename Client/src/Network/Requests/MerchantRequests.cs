namespace LibreKO.Network.Requests;

public interface IMerchantRequests
{
    void Open();
    void Close();
    void TradeCancel();
    void BuyMerchantOpen();
    void BuyMerchantClose();
    void AddItem(int itemId, int count, int price, byte srcPos, byte dstPos);
    void RemoveItem(byte stallSlot);
    void List(int merchantCharId);
    void Buy(int itemId, int count, byte merchantSlot, byte buyerSlot);
    void BeginSell(string advert);
    void BuyMerchantInsert(IReadOnlyList<MerchantWishItem> wanted);
    void BuyMerchantList(int merchantCharId);
    void BuyMerchantSell(byte sellerSlot, byte wantedSlot, int count);
    void StallListRequest(int merchantCharId);
}

public sealed class MerchantRequests : IMerchantRequests
{
    private readonly IPacketSink sink;

    public MerchantRequests(IPacketSink sink) => this.sink = sink;

    public void Open() => SendMerchantByte(MerchantSubOpcode.Open);

    public void Close() => SendMerchantByte(MerchantSubOpcode.Close);

    public void TradeCancel() => SendMerchantByte(MerchantSubOpcode.TradeCancel);

    public void BuyMerchantOpen() => SendMerchantByte(MerchantSubOpcode.BuyOpen);

    public void BuyMerchantClose() => SendMerchantByte(MerchantSubOpcode.BuyClose);

    public void AddItem(int itemId, int count, int price, byte srcPos, byte dstPos)
    {
        var p = Merchant(MerchantSubOpcode.ItemAdd);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)count);
        p.WriteInt(price);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        sink.Send(p);
    }

    public void RemoveItem(byte stallSlot)
    {
        var p = Merchant(MerchantSubOpcode.ItemCancel);
        p.WriteByte(stallSlot);
        sink.Send(p);
    }

    public void List(int merchantCharId)
    {
        var p = Merchant(MerchantSubOpcode.ItemList);
        p.WriteInt(merchantCharId);
        sink.Send(p);
    }

    public void Buy(int itemId, int count, byte merchantSlot, byte buyerSlot)
    {
        var p = Merchant(MerchantSubOpcode.ItemBuy);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)count);
        p.WriteByte(merchantSlot);
        p.WriteByte(buyerSlot);
        sink.Send(p);
    }

    public void BeginSell(string advert)
    {
        var p = Merchant(MerchantSubOpcode.Insert);
        p.WriteString(advert);
        sink.Send(p);
    }

    public void BuyMerchantInsert(IReadOnlyList<MerchantWishItem> wanted)
    {
        var p = Merchant(MerchantSubOpcode.BuyInsert);
        p.WriteByte((byte)wanted.Count);
        foreach (var wish in wanted)
        {
            p.WriteInt(wish.ItemId);
            p.WriteUShort((ushort)wish.Count);
            p.WriteInt(wish.Price);
        }
        sink.Send(p);
    }

    public void BuyMerchantList(int merchantCharId)
    {
        var p = Merchant(MerchantSubOpcode.BuyList);
        p.WriteInt(merchantCharId);
        sink.Send(p);
    }

    public void BuyMerchantSell(byte sellerSlot, byte wantedSlot, int count)
    {
        var p = Merchant(MerchantSubOpcode.BuyBuy);
        p.WriteByte(sellerSlot);
        p.WriteByte(wantedSlot);
        p.WriteUShort((ushort)count);
        sink.Send(p);
    }

    public void StallListRequest(int merchantCharId)
    {
        var p = Merchant(MerchantSubOpcode.StallList);
        p.WriteInt(merchantCharId);
        sink.Send(p);
    }

    private void SendMerchantByte(MerchantSubOpcode sub) => sink.Send(Merchant(sub));

    private static Packet Merchant(MerchantSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte((byte)sub);
        return p;
    }
}
