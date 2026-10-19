namespace LibreKO.Network.Requests;

public interface ICorpseRequests
{
    void Query(int charId);
}

public sealed class CorpseRequests : ICorpseRequests
{
    private readonly IPacketSink sink;

    public CorpseRequests(IPacketSink sink) => this.sink = sink;

    public void Query(int charId)
    {
        var p = new Packet(GameOpcodes.GS_CORPSE);
        p.WriteShort((short)charId);
        sink.Send(p);
    }
}
