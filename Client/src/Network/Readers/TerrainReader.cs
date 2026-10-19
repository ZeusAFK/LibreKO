namespace LibreKO.Network.Readers;

internal sealed class TerrainReader : IPacketReader
{
    private readonly ZoneSession zone;

    public TerrainReader(ZoneSession zone)
    {
        this.zone = zone;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ZONE_TERRAIN, HandleZoneTerrain);
    }

    private void HandleZoneTerrain(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ZoneTerrainSubOpcode)p.ReadByte();
        byte value = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)GroundEffect.None;
        if (GroundEffects.Read(sub, value) is not { } effect) return;
        zone.ApplyGroundEffect(effect);
    }
}
