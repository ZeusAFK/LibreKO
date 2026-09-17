using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<InstanceEntry>>? InstanceListEvent;
    public event Action<int, bool>? InstanceEnterEvent;
    public event Action<int, bool>? InstanceLeaveEvent;

    private void HandleInstance(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<InstanceEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 7; i++)
            {
                int id = p.ReadInt();
                string name = p.ReadSByteString();
                byte minLevel = p.ReadByte();
                ushort partySize = p.ReadUShort();
                list.Add(new InstanceEntry { Id = id, Name = name, MinLevel = minLevel, PartySize = partySize });
            }
            InstanceListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            InstanceEnterEvent?.Invoke(id, ok);
        }
        else if (sub == 3)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            InstanceLeaveEvent?.Invoke(id, ok);
        }
    }

    public void SendInstanceList()
    {
        var p = new Packet(GameOpcodes.GS_INSTANCE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendInstanceEnter(int instanceId)
    {
        var p = new Packet(GameOpcodes.GS_INSTANCE);
        p.WriteByte(2);
        p.WriteInt(instanceId);
        _conn.Send(p);
    }

    public void SendInstanceLeave()
    {
        var p = new Packet(GameOpcodes.GS_INSTANCE);
        p.WriteByte(3);
        _conn.Send(p);
    }
}

public struct InstanceEntry
{
    public int Id;
    public string Name;
    public byte MinLevel;
    public ushort PartySize;
}
