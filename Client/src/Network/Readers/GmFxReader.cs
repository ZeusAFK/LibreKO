namespace LibreKO.Network.Readers;

internal sealed class GmFxReader : IPacketReader
{
    private readonly GmFxSession gmFx;

    public GmFxReader(GmFxSession gmFx)
    {
        this.gmFx = gmFx;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ADMIN_PANEL, AdminReplySubOpcode.GmFx, HandleGmFx);
    }

    private void HandleGmFx(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int id = p.ReadInt();
        bool enabled = p.ReadByte() == 1;
        gmFx.Set(id, enabled);
        gmFx.RaiseGmFx(id, enabled);
    }
}
