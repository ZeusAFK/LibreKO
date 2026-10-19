namespace LibreKO.Network.Requests;

public interface IForcesRequests
{
    void Status();
    void Join();
    void Leave();
}

public sealed class ForcesRequests : IForcesRequests
{
    private readonly IPacketSink sink;

    public ForcesRequests(IPacketSink sink) => this.sink = sink;

    public void Status()
    {
        var p = new Packet(GameOpcodes.GS_FORCES);
        p.WriteByte((byte)ForcesSubOpcode.Status);
        sink.Send(p);
    }

    public void Join()
    {
        var p = new Packet(GameOpcodes.GS_FORCES);
        p.WriteByte((byte)ForcesSubOpcode.Join);
        sink.Send(p);
    }

    public void Leave()
    {
        var p = new Packet(GameOpcodes.GS_FORCES);
        p.WriteByte((byte)ForcesSubOpcode.Leave);
        sink.Send(p);
    }
}
