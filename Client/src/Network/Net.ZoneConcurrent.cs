using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public readonly record struct ZoneConcurrentCount(int ZoneId, int Players);

    public event Action<IReadOnlyList<ZoneConcurrentCount>>? ZoneConcurrentEvent;

    private void HandleZoneConcurrent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int count = p.ReadByte();
        var list = new List<ZoneConcurrentCount>(count);
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 4) break;
            int zoneId = p.ReadUShort();
            int players = p.ReadUShort();
            list.Add(new ZoneConcurrentCount(zoneId, players));
        }
        ZoneConcurrentEvent?.Invoke(list);
    }

    public void SendZoneConcurrentRequest()
    {
        var p = new Packet(GameOpcodes.GS_ZONE_CONCURRENT);
        _conn.Send(p);
    }
}
