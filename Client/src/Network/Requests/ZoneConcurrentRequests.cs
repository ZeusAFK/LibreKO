namespace LibreKO.Network.Requests;

public interface IZoneConcurrentRequests
{
    void ZoneConcurrentRequest();
}

public sealed class ZoneConcurrentRequests : IZoneConcurrentRequests
{
    private readonly IPacketSink sink;

    public ZoneConcurrentRequests(IPacketSink sink) => this.sink = sink;

    public void ZoneConcurrentRequest()
    {
        var p = new Packet(GameOpcodes.GS_ZONE_CONCURRENT);
        sink.Send(p);
    }
}
