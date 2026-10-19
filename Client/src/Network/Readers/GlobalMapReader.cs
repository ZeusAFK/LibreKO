namespace LibreKO.Network.Readers;

internal sealed class GlobalMapReader : IPacketReader
{
    private readonly GlobalMapSession globalMap;

    public GlobalMapReader(GlobalMapSession globalMap)
    {
        this.globalMap = globalMap;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_GLOBAL_MAP, HandleGlobalMap);
    }

    private void HandleGlobalMap(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (GlobalMapSubOpcode)p.ReadByte();
        if (sub == GlobalMapSubOpcode.List)
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
            globalMap.RaiseGlobalMapList(list);
        }
    }
}
