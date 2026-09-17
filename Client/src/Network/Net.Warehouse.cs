using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public event Action? WarehouseNpcEvent;

    public event Action<int, ItemSlot[]>? WarehouseContentsEvent;

    public event Action<byte, bool>? WarehouseResultEvent;

    public const int WarehouseSlots = 192;

    private void HandleWarehouse(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 0x10:
                WarehouseNpcEvent?.Invoke();
                break;
            case 1:
            {
                if (p.RemainingBytes >= 1) p.ReadByte();
                int money = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                var slots = new ItemSlot[WarehouseSlots];
                for (int i = 0; i < WarehouseSlots && p.RemainingBytes >= 17; i++)
                {
                    int itemId = p.ReadInt();
                    short dur = UShortToShort(p.ReadUShort());
                    short count = UShortToShort(p.ReadUShort());
                    p.ReadByte();
                    SkipBytes(p, 8);
                    slots[i] = itemId == 0 ? default : new ItemSlot { ItemId = itemId, Durability = dur, Count = count };
                }
                WarehouseContentsEvent?.Invoke(money, slots);
                break;
            }
            case 2: case 3: case 4: case 5:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                WarehouseResultEvent?.Invoke(sub, ok);
                break;
            }
        }
    }

    public void SendWarehouseOpen()
    {
        var p = new Packet(GameOpcodes.GS_WAREHOUSE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendWarehouseInput(int npcId, int itemId, byte page, byte srcInvPos, byte dstWhPos, int count)
        => SendWarehouseMove(2, npcId, itemId, page, srcInvPos, dstWhPos, count);

    public void SendWarehouseOutput(int npcId, int itemId, byte page, byte srcWhPos, byte dstInvPos, int count)
        => SendWarehouseMove(3, npcId, itemId, page, srcWhPos, dstInvPos, count);

    private void SendWarehouseMove(byte sub, int npcId, int itemId, byte page, byte srcPos, byte dstPos, int count)
    {
        var p = new Packet(GameOpcodes.GS_WAREHOUSE);
        p.WriteByte(sub);
        p.WriteInt(npcId);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteInt(count);
        _conn.Send(p);
    }
}
