namespace LibreKO.Network.Readers;

internal sealed class WarpReader : IPacketReader
{
    private readonly WarpSession warp;

    public WarpReader(WarpSession warp)
    {
        this.warp = warp;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_WARP_LIST, HandleWarpList);
    }

    private void HandleWarpList(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var kind = (WarpListSubOpcode)p.ReadByte();
        if (kind == WarpListSubOpcode.Result)
        {
            var result = p.RemainingBytes >= 1 ? (WarpResult)p.ReadByte() : WarpResult.Arrived;
            if (result == WarpResult.Arrived) return;
            int detail = result switch
            {
                WarpResult.LevelTooLow when p.RemainingBytes >= 1 => p.ReadByte(),
                WarpResult.QuestCompleted when p.RemainingBytes >= 2 => p.ReadUShort(),
                _ => 0,
            };
            warp.RaiseWarpFail(result, detail);
            return;
        }
        if (kind != WarpListSubOpcode.Menu)
        {
            warp.RaiseWarpFail(default, 0);
            return;
        }

        if (p.RemainingBytes < 2) { warp.RaiseWarpList(new List<WarpListEntry>()); return; }
        int count = p.ReadShort();
        var list = new List<WarpListEntry>(count > 0 ? count : 0);
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 2) break;
            int warpId = p.ReadShort();
            string name = p.ReadString();
            string announce = p.ReadString();
            if (p.RemainingBytes < 8) break;
            int zoneId = p.ReadShort();
            int maxUsers = p.ReadShort();
            long fee = p.ReadUInt();
            list.Add(new WarpListEntry(warpId, name, announce, zoneId, maxUsers, fee));
        }
        warp.RaiseWarpList(list);
    }
}
