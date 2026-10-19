namespace LibreKO.Network.Requests;

public interface IFishingHallRequests
{
    void List();
}

public sealed class FishingHallRequests : IFishingHallRequests
{
    private readonly IPacketSink sink;

    public FishingHallRequests(IPacketSink sink) => this.sink = sink;

    public void List()
    {
        var p = new Packet(GameOpcodes.GS_FISHING_HALL);
        p.WriteByte((byte)FishingHallSubOpcode.List);
        sink.Send(p);
    }
}
