namespace LibreKO.Network.Readers;

internal sealed class CorpseReader : IPacketReader
{
    private readonly CorpseSession corpse;

    public CorpseReader(CorpseSession corpse)
    {
        this.corpse = corpse;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CORPSE, HandleCorpse);
    }

    private void HandleCorpse(Packet p)
    {
        if (p.RemainingBytes < 8) return;
        int charId = p.ReadShort();
        float x = p.ReadShort() / Coord.TenthsPerUnit;
        float z = p.ReadShort() / Coord.TenthsPerUnit;
        float y = p.ReadShort() / Coord.TenthsPerUnit;
        corpse.RaiseCorpse(charId, x, z, y);
    }
}
