using System;

namespace LibreKO.Network;

public enum ItemTradeResult : byte
{
    Refused = 0,
    Traded = 1,
    Moved = 3,
}

public partial class Net
{
    public event Action<int>? TradeNpcEvent;

    public event Action<bool, int, int, int>? ItemTradeResultEvent;

    public event Action? ItemTradeMovedEvent;

    private void HandleTradeNpc(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        TradeNpcEvent?.Invoke(p.ReadInt());
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
                    LoyaltyChangeEvent?.Invoke(balance, 0);
                }
                else
                {
                    GoldChangeEvent?.Invoke(balance);
                }
                ItemTradeResultEvent?.Invoke(true, 0, balance, price);
                break;
            }
            case ItemTradeResult.Refused:
            {
                int code = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                ItemTradeResultEvent?.Invoke(false, code, 0, 0);
                break;
            }
            case ItemTradeResult.Moved:
                ItemTradeMovedEvent?.Invoke();
                break;
        }
    }

    public void SendVendorBuy(int sellingGroup, int npcId, int itemId, byte destPos, ushort count,
        byte line, byte listIndex)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte(1);
        p.WriteInt(sellingGroup);
        p.WriteInt(npcId);
        p.WriteByte(1);
        p.WriteInt(itemId);
        p.WriteByte(destPos);
        p.WriteUShort(count);
        p.WriteByte(line);
        p.WriteByte(listIndex);
        _conn.Send(p);
    }

    public void SendVendorSell(int sellingGroup, int npcId, int itemId, byte srcPos, ushort count)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte(2);
        p.WriteInt(sellingGroup);
        p.WriteInt(npcId);
        p.WriteByte(1);
        p.WriteInt(itemId);
        p.WriteByte(srcPos);
        p.WriteUShort(count);
        _conn.Send(p);
    }
}
