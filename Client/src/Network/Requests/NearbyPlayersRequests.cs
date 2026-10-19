namespace LibreKO.Network.Requests;

public interface INearbyPlayersRequests
{
    void Request(bool first = false);
}

public sealed class NearbyPlayersRequests : INearbyPlayersRequests
{
    private readonly IPacketSink sink;
    private readonly NearbyPlayersSession nearby;

    public NearbyPlayersRequests(IPacketSink sink, NearbyPlayersSession nearby)
    {
        this.sink = sink;
        this.nearby = nearby;
    }

    public void Request(bool first = false)
    {
        nearby.Requested = first ? PlayerInspectSubOpcode.Nearby : PlayerInspectSubOpcode.NearbyAll;
        var p = new Packet(GameOpcodes.GS_USER_INFO);
        p.WriteByte((byte)nearby.Requested);
        sink.Send(p);
    }
}
