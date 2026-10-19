namespace LibreKO.Network.Requests;

public interface ITournamentRequests
{
    void Status();
    void Register();
    void Unregister();
}

public sealed class TournamentRequests : ITournamentRequests
{
    private readonly IPacketSink sink;

    public TournamentRequests(IPacketSink sink) => this.sink = sink;

    public void Status()
    {
        var p = new Packet(GameOpcodes.GS_TOURNAMENT);
        p.WriteByte((byte)TournamentSubOpcode.Status);
        sink.Send(p);
    }

    public void Register()
    {
        var p = new Packet(GameOpcodes.GS_TOURNAMENT);
        p.WriteByte((byte)TournamentSubOpcode.Register);
        sink.Send(p);
    }

    public void Unregister()
    {
        var p = new Packet(GameOpcodes.GS_TOURNAMENT);
        p.WriteByte((byte)TournamentSubOpcode.Unregister);
        sink.Send(p);
    }
}
