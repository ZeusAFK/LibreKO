namespace LibreKO.Network.Requests;

public interface ISoccerRequests
{
    void Emote(int action);
}

public sealed class SoccerRequests : ISoccerRequests
{
    private readonly IPacketSink sink;

    public SoccerRequests(IPacketSink sink) => this.sink = sink;

    public void Emote(int action)
    {
        var p = new Packet(GameOpcodes.GS_STATE_CHANGE);
        p.WriteByte(StateChange.Action);
        p.WriteInt(action);
        sink.Send(p);
    }
}
