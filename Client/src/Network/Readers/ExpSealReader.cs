namespace LibreKO.Network.Readers;

internal sealed class ExpSealReader : IPacketReader
{
    private readonly ExpSealSession expSeal;

    public ExpSealReader(ExpSealSession expSeal)
    {
        this.expSeal = expSeal;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_EXP_SEAL, HandleExpSeal);
    }

    private void HandleExpSeal(Packet p)
    {
        if (ExpSealReply.Read(p) is not { } reply) return;
        expSeal.ApplyReply(reply);
    }
}
