namespace LibreKO.Network.Requests;

public interface IStealthRequests
{
    void Send();
}

public sealed class StealthRequests : IStealthRequests
{
    private readonly IPacketSink sink;

    public StealthRequests(IPacketSink sink) => this.sink = sink;

    public void Send()
    {
        var p = new Packet(GameOpcodes.GS_STEALTH);
        p.WriteByte(0);
        sink.Send(p);
    }
}
