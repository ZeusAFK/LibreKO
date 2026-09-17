using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<GlobalMapZone>>? GlobalMapListEvent;

    private void HandleGlobalMap(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<GlobalMapZone>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
            {
                int zoneId = p.ReadUShort();
                string name = p.ReadSByteString();
                int population = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                byte ownerNation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                list.Add(new GlobalMapZone
                {
                    ZoneId = zoneId,
                    Name = name,
                    Population = population,
                    OwnerNation = ownerNation,
                });
            }
            GlobalMapListEvent?.Invoke(list);
        }
    }

    public void SendGlobalMapList()
    {
        var p = new Packet(GameOpcodes.GS_GLOBAL_MAP);
        p.WriteByte(1);
        _conn.Send(p);
    }
}

public struct GlobalMapZone
{
    public int ZoneId;
    public string Name;
    public int Population;
    public byte OwnerNation;
}
