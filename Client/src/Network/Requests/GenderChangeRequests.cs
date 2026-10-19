namespace LibreKO.Network.Requests;

public interface IGenderChangeRequests
{
    void Send(int race, int face, int hair);
}

public sealed class GenderChangeRequests : IGenderChangeRequests
{
    private readonly IPacketSink sink;

    public GenderChangeRequests(IPacketSink sink) => this.sink = sink;

    public void Send(int race, int face, int hair)
    {
        var p = new Packet(GameOpcodes.GS_GENDER_CHANGE);
        p.WriteByte((byte)GenderChangeSubOpcode.Change);
        p.WriteByte((byte)race);
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        sink.Send(p);
    }
}
