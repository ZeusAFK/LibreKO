using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<ForcesStatus>? ForcesStatusEvent;
    public event Action<bool>? ForcesJoinEvent;
    public event Action<bool>? ForcesLeaveEvent;

    private void HandleForces(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            if (p.RemainingBytes < 7) return;
            bool joined = p.ReadByte() != 0;
            int points = p.ReadInt();
            byte rank = p.ReadByte();
            byte angerPct = p.ReadByte();
            ForcesStatusEvent?.Invoke(new ForcesStatus
            {
                Joined = joined,
                Points = points,
                Rank = rank,
                AngerPct = angerPct,
            });
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            ForcesJoinEvent?.Invoke(ok);
        }
        else if (sub == 3)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            ForcesLeaveEvent?.Invoke(ok);
        }
    }

    public void SendForcesStatus()
    {
        var p = new Packet(GameOpcodes.GS_FORCES);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendForcesJoin()
    {
        var p = new Packet(GameOpcodes.GS_FORCES);
        p.WriteByte(2);
        _conn.Send(p);
    }

    public void SendForcesLeave()
    {
        var p = new Packet(GameOpcodes.GS_FORCES);
        p.WriteByte(3);
        _conn.Send(p);
    }
}

public struct ForcesStatus
{
    public bool Joined;
    public int Points;
    public byte Rank;
    public byte AngerPct;
}
