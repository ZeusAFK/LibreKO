using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte BifrostEventSub = 2;

    private const byte BifrostJoinSub    = 8;
    private const byte BifrostDisbandSub = 9;

    public event Action<int>? BifrostTimeEvent;

    public event Action<bool, int>? BifrostJoinEvent;

    public event Action? BifrostDisbandEvent;

    private void HandleBifrost(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub != BifrostEventSub) return;

        int remaining = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (remaining < 0) remaining = 0;
        BifrostTimeEvent?.Invoke(remaining);
    }

    private void HandleBifrostEvent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case BifrostJoinSub:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int zone = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                BifrostJoinEvent?.Invoke(ok, zone);
                break;
            }
            case BifrostDisbandSub:
            {
                if (p.RemainingBytes >= 1) p.ReadByte();
                if (p.RemainingBytes >= 2) p.ReadShort();
                BifrostDisbandEvent?.Invoke();
                break;
            }
        }
    }

    public void SendBifrostTimeRequest()
    {
        var p = new Packet(GameOpcodes.GS_BIFROST);
        p.WriteByte(BifrostEventSub);
        _conn.Send(p);
    }

    public void SendBifrostJoin()
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(BifrostJoinSub);
        _conn.Send(p);
    }

    public void SendBifrostDisband()
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte(BifrostDisbandSub);
        _conn.Send(p);
    }
}
