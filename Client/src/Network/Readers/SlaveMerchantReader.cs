namespace LibreKO.Network.Readers;

internal sealed class SlaveMerchantReader : IPacketReader
{
    private readonly SlaveMerchantSession slaveMerchant;

    public SlaveMerchantReader(SlaveMerchantSession slaveMerchant)
    {
        this.slaveMerchant = slaveMerchant;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Status, HandleStatus);
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Collect, HandleCollect);
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Redeem, HandleRedeem);
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Time, HandleTime);
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Deploy, HandleDeploy);
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Sold, HandleSold);
        dispatcher.On(GameOpcodes.GS_SLAVE_MERCHANT, SlaveMerchantSub.Closed, HandleClosed);
    }

    private void HandleStatus(Packet p)
    {
        if (SlaveMerchantWire.ReadStatus(p) is { } status) slaveMerchant.ApplyStatus(status);
    }

    private void HandleCollect(Packet p)
    {
        if (SlaveMerchantWire.ReadCollected(p) is { } collected) slaveMerchant.RaiseCollect(collected);
    }

    private void HandleRedeem(Packet p)
    {
        if (SlaveMerchantWire.ReadRedeem(p) is { } redeem) slaveMerchant.ApplyRedeem(redeem);
    }

    private void HandleTime(Packet p)
    {
        if (SlaveMerchantWire.ReadTime(p) is { } hours) slaveMerchant.ApplyHours(hours);
    }

    private void HandleDeploy(Packet p)
    {
        if (SlaveMerchantWire.ReadDeploy(p) is { } result) slaveMerchant.RaiseDeploy(result);
    }

    private void HandleSold(Packet p)
    {
        if (SlaveMerchantWire.ReadSale(p) is { } sale) slaveMerchant.RaiseSale(sale);
    }

    private void HandleClosed(Packet p)
    {
        if (SlaveMerchantWire.ReadClosed(p) is { } reason) slaveMerchant.RaiseClosed(reason);
    }
}
