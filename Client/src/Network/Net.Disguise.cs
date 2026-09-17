using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<DisguiseEntry>>? DisguiseListEvent;
    public event Action<int, bool>? DisguiseApplyEvent;
    public event Action<int, bool>? DisguiseRemoveEvent;

    private void HandleDisguise(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<DisguiseEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            {
                int id = p.ReadInt();
                string name = p.ReadSByteString();
                list.Add(new DisguiseEntry { Id = id, Name = name });
            }
            DisguiseListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int disguiseId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            DisguiseApplyEvent?.Invoke(disguiseId, ok);
        }
        else if (sub == 3)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int disguiseId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            DisguiseRemoveEvent?.Invoke(disguiseId, ok);
        }
    }

    public void SendDisguiseList()
    {
        var p = new Packet(GameOpcodes.GS_DISGUISE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendDisguiseApply(int disguiseId)
    {
        var p = new Packet(GameOpcodes.GS_DISGUISE);
        p.WriteByte(2);
        p.WriteInt(disguiseId);
        _conn.Send(p);
    }

    public void SendDisguiseRemove()
    {
        var p = new Packet(GameOpcodes.GS_DISGUISE);
        p.WriteByte(3);
        _conn.Send(p);
    }
}

public struct DisguiseEntry
{
    public int Id;
    public string Name;
}
