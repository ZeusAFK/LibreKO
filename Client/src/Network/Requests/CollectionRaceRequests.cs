namespace LibreKO.Network.Requests;

public interface ICollectionRaceRequests
{
    void CollectionRaceRequest();
}

public sealed class CollectionRaceRequests : ICollectionRaceRequests
{
    private readonly IPacketSink sink;

    public CollectionRaceRequests(IPacketSink sink) => this.sink = sink;

    public void CollectionRaceRequest()
    {
        var p = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        sink.Send(p);
    }
}
