namespace LibreKO.Network.Readers;

internal sealed class VendorReader : IPacketReader
{
    private readonly ProgressSession progress;
    private readonly VendorSession vendor;

    public VendorReader(ProgressSession progress, VendorSession vendor)
    {
        this.progress = progress;
        this.vendor = vendor;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_TRADE_NPC, HandleTradeNpc);
        dispatcher.On(GameOpcodes.GS_ITEM_TRADE, HandleItemTrade);
    }

    private void HandleTradeNpc(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        vendor.RaiseTradeNpc(p.ReadInt());
    }

    private void HandleItemTrade(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((ItemTradeResult)p.ReadByte())
        {
            case ItemTradeResult.Traded:
                {
                    if (p.RemainingBytes < 8) return;
                    int balance = p.ReadInt();
                    int price = p.ReadInt();
                    if (p.RemainingBytes >= 1)
                    {
                        p.ReadByte();
                        progress.ApplyLoyalty(balance, null);
                    }
                    else
                    {
                        progress.ApplyGold(balance);
                    }
                    vendor.RaiseItemTradeResult(true, 0, balance, price);
                    break;
                }
            case ItemTradeResult.Refused:
                {
                    int code = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    vendor.RaiseItemTradeResult(false, code, 0, 0);
                    break;
                }
            case ItemTradeResult.Moved:
                vendor.RaiseItemTradeMoved();
                break;
            case ItemTradeResult.Repurchase:
                vendor.RaiseRepurchase(RepurchaseReply.Read(p));
                break;
        }
    }
}
