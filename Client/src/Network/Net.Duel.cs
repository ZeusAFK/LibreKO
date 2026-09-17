using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<DuelEntry>>? DuelListEvent;
    public event Action<int, bool>? DuelCreateEvent;
    public event Action<int, bool>? DuelJoinEvent;
    public event Action<int, bool>? DuelLeaveEvent;

    private void HandleDuel(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<DuelEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 10; i++)
            {
                int id = p.ReadInt();
                string creator = p.ReadSByteString();
                int stake = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                bool full = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
                list.Add(new DuelEntry { Id = id, Creator = creator, Stake = stake, Full = full });
            }
            DuelListEvent?.Invoke(list);
        }
        else if (sub == 2 || sub == 3 || sub == 4)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int duelId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            if (sub == 2) DuelCreateEvent?.Invoke(duelId, ok);
            else if (sub == 3) DuelJoinEvent?.Invoke(duelId, ok);
            else DuelLeaveEvent?.Invoke(duelId, ok);
        }
    }

    public void SendDuelList()
    {
        var p = new Packet(GameOpcodes.GS_DUEL);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendDuelCreate(int stake)
    {
        var p = new Packet(GameOpcodes.GS_DUEL);
        p.WriteByte(2);
        p.WriteInt(stake);
        _conn.Send(p);
    }

    public void SendDuelJoin(int duelId)
    {
        var p = new Packet(GameOpcodes.GS_DUEL);
        p.WriteByte(3);
        p.WriteInt(duelId);
        _conn.Send(p);
    }

    public void SendDuelLeave(int duelId)
    {
        var p = new Packet(GameOpcodes.GS_DUEL);
        p.WriteByte(4);
        p.WriteInt(duelId);
        _conn.Send(p);
    }
}

public struct DuelEntry
{
    public int Id;
    public string Creator;
    public int Stake;
    public bool Full;
}
