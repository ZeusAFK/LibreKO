namespace LibreKO.Network.Readers;

internal sealed class ZoneConcurrentReader : IPacketReader
{
    private readonly ZoneConcurrentSession zoneConcurrent;

    public ZoneConcurrentReader(ZoneConcurrentSession zoneConcurrent)
    {
        this.zoneConcurrent = zoneConcurrent;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ZONE_CONCURRENT, HandleZoneConcurrent);
    }

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
        zoneConcurrent.RaiseZoneConcurrent(list);
    }
}
