namespace LibreKO.Network.Requests;

public interface IProgressRequests
{
    void PointChange(int type);
}

public sealed class ProgressRequests : IProgressRequests
{
    private readonly IPacketSink sink;

    public ProgressRequests(IPacketSink sink) => this.sink = sink;

    public void PointChange(int type)
    {
        var p = new Packet(GameOpcodes.GS_POINT_CHANGE);
        p.WriteByte((byte)type);
        p.WriteShort(1);
        sink.Send(p);
    }
}
