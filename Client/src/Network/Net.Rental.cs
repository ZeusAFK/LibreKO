using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<RentalItem>>? RentalListEvent;
    public event Action<int, bool>? RentalRentEvent;
    public event Action? RentalOpenEvent;

    private void HandleRental(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 1:
            {
                var list = new List<RentalItem>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 12; i++)
                    list.Add(new RentalItem { ItemId = p.ReadInt(), Days = p.ReadInt(), Cost = p.ReadInt() });
                RentalListEvent?.Invoke(list);
                break;
            }
            case 2:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int itemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                RentalRentEvent?.Invoke(itemId, ok);
                break;
            }
            case 3:
                RentalOpenEvent?.Invoke();
                break;
        }
    }

    public void SendRentalList() { var p = new Packet(GameOpcodes.GS_RENTAL); p.WriteByte(1); _conn.Send(p); }
    public void SendRentalRent(int itemId) { var p = new Packet(GameOpcodes.GS_RENTAL); p.WriteByte(2); p.WriteInt(itemId); _conn.Send(p); }
    public void SendRentalNpc() { var p = new Packet(GameOpcodes.GS_RENTAL); p.WriteByte(3); _conn.Send(p); }
}

public struct RentalItem { public int ItemId; public int Days; public int Cost; }
