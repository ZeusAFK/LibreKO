namespace LibreKO.Network.Requests;

public interface IRankRequests
{
    void RankRequest(RankType rankType);
}

public sealed class RankRequests : IRankRequests
{
    private readonly IPacketSink sink;

    public RankRequests(IPacketSink sink) => this.sink = sink;

    public void RankRequest(RankType rankType)
    {
        var p = new Packet(GameOpcodes.GS_RANK);
        p.WriteByte((byte)rankType);
        sink.Send(p);
    }
}
