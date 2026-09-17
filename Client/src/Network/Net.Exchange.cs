using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const int ExchangeGoldItem = 900000000;

    private const byte ExchangeModeTrade = 1;

    public event Action<int>? ExchangeRequestEvent;
    public event Action<bool>? ExchangeAgreeEvent;
    public event Action<bool>? ExchangeAddResultEvent;
    public event Action<int, int, short>? ExchangeOtherAddEvent;
    public event Action? ExchangeOtherDecideEvent;
    public event Action<bool, int, List<(byte DstPos, ItemSlot Slot)>>? ExchangeDoneEvent;
    public event Action? ExchangeCancelEvent;

    private void HandleExchange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 1:
                ExchangeRequestEvent?.Invoke(p.ReadInt());
                break;
            case 2:
                ExchangeAgreeEvent?.Invoke(p.ReadByte() == 1);
                break;
            case 3:
                ExchangeAddResultEvent?.Invoke(p.ReadByte() == 1);
                break;
            case 4:
            {
                int itemId = p.ReadInt();
                int count = p.ReadInt();
                short dura = p.ReadShort();
                ExchangeOtherAddEvent?.Invoke(itemId, count, dura);
                break;
            }
            case 6:
                ExchangeOtherDecideEvent?.Invoke();
                break;
            case 7:
            {
                byte ok = p.ReadByte();
                var received = new List<(byte, ItemSlot)>();
                int money = 0;
                if (ok == 1)
                {
                    money = p.ReadInt();
                    int n = p.ReadUShort();
                    for (int i = 0; i < n && p.RemainingBytes >= 14; i++)
                    {
                        byte dstPos = p.ReadByte();
                        int itemId = p.ReadInt();
                        int cnt = p.ReadUShort();
                        short dura = p.ReadShort();
                        p.ReadByte();
                        p.ReadInt();
                        received.Add((dstPos, new ItemSlot { ItemId = itemId, Count = (short)cnt, Durability = dura }));
                    }
                    GoldChangeEvent?.Invoke(money);
                }
                ExchangeDoneEvent?.Invoke(ok == 1, money, received);
                break;
            }
            case 8:
                ExchangeCancelEvent?.Invoke();
                break;
        }
    }

    public void SendExchangeRequest(int targetCharId)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(1);
        p.WriteInt(targetCharId);
        p.WriteByte(ExchangeModeTrade);
        _conn.Send(p);
    }

    public void SendExchangeAgree(bool accept)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(2);
        p.WriteByte((byte)(accept ? 1 : 0));
        _conn.Send(p);
    }

    public void SendExchangeAddItem(byte gridPos, int itemId, int count)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(3);
        p.WriteByte(gridPos);
        p.WriteInt(itemId);
        p.WriteInt(count);
        _conn.Send(p);
    }

    public void SendExchangeAddGold(int goldAmount)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(3);
        p.WriteByte(0xFF);
        p.WriteInt(ExchangeGoldItem);
        p.WriteInt(goldAmount);
        _conn.Send(p);
    }

    public void SendExchangeDecide()
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(5);
        _conn.Send(p);
    }

    public void SendExchangeCancel()
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(8);
        _conn.Send(p);
    }
}
