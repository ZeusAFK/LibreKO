using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<bool, int>? InnStatusEvent;
    public event Action<bool, int>? InnSetHomeEvent;
    public event Action<bool>? InnRestEvent;

    private void HandleInn(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            bool saved = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            int zone = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            InnStatusEvent?.Invoke(saved, zone);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            int zone = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            InnSetHomeEvent?.Invoke(ok, zone);
        }
        else if (sub == 3)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            InnRestEvent?.Invoke(ok);
        }
    }

    public void SendInnStatus()
    {
        var p = new Packet(GameOpcodes.GS_INN);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendInnSetHome()
    {
        var p = new Packet(GameOpcodes.GS_INN);
        p.WriteByte(2);
        _conn.Send(p);
    }

    public void SendInnRest()
    {
        var p = new Packet(GameOpcodes.GS_INN);
        p.WriteByte(3);
        _conn.Send(p);
    }
}
