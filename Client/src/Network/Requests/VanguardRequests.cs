namespace LibreKO.Network.Requests;

public interface IVanguardRequests
{
    void Accept();
}

public sealed class VanguardRequests : IVanguardRequests
{
    private readonly IPacketSink sink;

    public VanguardRequests(IPacketSink sink) => this.sink = sink;

    public void Accept()
    {
        var p = new Packet(GameOpcodes.GS_VANGUARD);
        p.WriteByte((byte)VanguardSubOpcode.Appoint);
        p.WriteByte((byte)VanguardAppointKind.Accept);
        sink.Send(p);
    }
}
