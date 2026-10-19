namespace LibreKO.Network.Requests;

public interface IObjectEventRequests
{
    void Send(short objectIndex, int npcId);
}

public sealed class ObjectEventRequests : IObjectEventRequests
{
    private readonly IPacketSink sink;

    public ObjectEventRequests(IPacketSink sink) => this.sink = sink;

    public void Send(short objectIndex, int npcId)
    {
        var p = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        p.WriteShort(objectIndex);
        p.WriteInt(npcId);
        sink.Send(p);
    }
}
