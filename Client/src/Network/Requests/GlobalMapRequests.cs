namespace LibreKO.Network.Requests;

public interface IGlobalMapRequests
{
    void List();
}

public sealed class GlobalMapRequests : IGlobalMapRequests
{
    private readonly IPacketSink sink;

    public GlobalMapRequests(IPacketSink sink) => this.sink = sink;

    public void List()
    {
        var p = new Packet(GameOpcodes.GS_GLOBAL_MAP);
        p.WriteByte((byte)GlobalMapSubOpcode.List);
        sink.Send(p);
    }
}
