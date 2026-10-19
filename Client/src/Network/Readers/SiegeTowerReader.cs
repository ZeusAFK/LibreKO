namespace LibreKO.Network.Readers;

internal sealed class SiegeTowerReader : IPacketReader
{
    private readonly SiegeSession siege;

    public SiegeTowerReader(SiegeSession siege)
    {
        this.siege = siege;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SIEGE_TOWER, HandleSiegeTower);
    }

    private void HandleSiegeTower(Packet p)
    {
        siege.SiegeTowerPending = false;
        if (SiegeTowerReply.Read(p) is { } reply) siege.RaiseSiegeTower(reply);
    }
}
