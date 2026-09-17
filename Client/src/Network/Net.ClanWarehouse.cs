using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, ItemSlot[]>? ClanWhContentsEvent;

    public event Action<byte, bool>? ClanWhResultEvent;

    public const int ClanWhNetSlots = 192;

    private void HandleClanWarehouse(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 1:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                if (!ok) { ClanWhResultEvent?.Invoke(sub, false); break; }
                int money = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                var slots = new ItemSlot[ClanWhNetSlots];
                for (int i = 0; i < ClanWhNetSlots && p.RemainingBytes >= 17; i++)
                {
                    int itemId = p.ReadInt();
                    short dur = UShortToShort(p.ReadUShort());
                    short count = UShortToShort(p.ReadUShort());
                    p.ReadByte();
                    SkipBytes(p, 8);
                    slots[i] = itemId == 0 ? default : new ItemSlot { ItemId = itemId, Durability = dur, Count = count };
                }
                ClanWhContentsEvent?.Invoke(money, slots);
                break;
            }
            case 2: case 3: case 4: case 5:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                ClanWhResultEvent?.Invoke(sub, ok);
                break;
            }
        }
    }

    public void SendClanWhOpen()
    {
        var p = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendClanWhInput(int itemId, byte page, byte srcInvPos, byte dstWhPos, int count)
        => SendClanWhMove(2, itemId, page, srcInvPos, dstWhPos, count);

    public void SendClanWhOutput(int itemId, byte page, byte srcWhPos, byte dstInvPos, int count)
        => SendClanWhMove(3, itemId, page, srcWhPos, dstInvPos, count);

    private void SendClanWhMove(byte sub, int itemId, byte page, byte srcPos, byte dstPos, int count)
    {
        var p = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
        p.WriteByte(sub);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteInt(count);
        _conn.Send(p);
    }
}
