using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<bool, int, int>? TournamentStatusEvent;
    public event Action<bool>? TournamentRegisterEvent;

    private void HandleTournament(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            bool registered = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            int participantCount = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            int round = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            TournamentStatusEvent?.Invoke(registered, participantCount, round);
        }
        else if (sub == 2 || sub == 3)
        {
            bool nowRegistered = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            TournamentRegisterEvent?.Invoke(nowRegistered);
        }
    }

    public void SendTournamentStatus()
    {
        var p = new Packet(GameOpcodes.GS_TOURNAMENT);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendTournamentRegister()
    {
        var p = new Packet(GameOpcodes.GS_TOURNAMENT);
        p.WriteByte(2);
        _conn.Send(p);
    }

    public void SendTournamentUnregister()
    {
        var p = new Packet(GameOpcodes.GS_TOURNAMENT);
        p.WriteByte(3);
        _conn.Send(p);
    }
}
