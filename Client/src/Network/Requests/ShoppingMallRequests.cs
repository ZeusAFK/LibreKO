namespace LibreKO.Network.Requests;

public interface IShoppingMallRequests
{
    void Open();
    void Close();
    void PowerUpStorePurchase(string recipient, IReadOnlyList<PowerUpStoreCart.Line> lines);
    void PowerUpStoreCheckRecipient(string name);
}

public sealed class ShoppingMallRequests : IShoppingMallRequests
{
    private readonly IPacketSink sink;

    public ShoppingMallRequests(IPacketSink sink) => this.sink = sink;

    public void Open() => SendShoppingMallByte(ShoppingMallSubOpcode.Open);

    public void Close() => SendShoppingMallByte(ShoppingMallSubOpcode.Close);

    public void PowerUpStorePurchase(string recipient, IReadOnlyList<PowerUpStoreCart.Line> lines)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte((byte)ShoppingMallSubOpcode.Purchase);
        p.WriteByte((byte)ShoppingMallPurchaseSubOpcode.Cart);
        p.WriteSByteString(recipient);
        p.WriteByte((byte)lines.Count);
        foreach (var line in lines)
        {
            p.WriteInt(line.Entry.Id);
            p.WriteUShort((ushort)line.Count);
            p.WriteInt(line.Entry.Price);
        }
        sink.Send(p);
    }

    public void PowerUpStoreCheckRecipient(string name)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte((byte)ShoppingMallSubOpcode.Purchase);
        p.WriteByte((byte)ShoppingMallPurchaseSubOpcode.CheckRecipient);
        p.WriteSByteString(name);
        sink.Send(p);
    }

    private void SendShoppingMallByte(ShoppingMallSubOpcode channel)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte((byte)channel);
        sink.Send(p);
    }
}
