namespace LibreKO.Network.Readers;

internal sealed class CosmeticsReader : IPacketReader
{
    private readonly CosmeticsSession cosmetics;
    private readonly SelfSession self;

    public CosmeticsReader(CosmeticsSession cosmetics, SelfSession self)
    {
        this.cosmetics = cosmetics;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_HELMET, HandleHelmet);
    }

    private void HandleHelmet(Packet p)
    {
        if (p.RemainingBytes < 6) return;
        bool hidden = p.ReadByte() != 0;
        bool costumeHidden = p.ReadByte() != 0;
        int charId = p.ReadInt();
        if (charId == self.LastEnter.CharId) cosmetics.ApplyOwnLook(hidden, costumeHidden);
        cosmetics.RaiseHelmet(charId, hidden);
        cosmetics.RaiseCostume(charId, costumeHidden);
    }
}
