namespace LibreKO.Network.Requests;

public interface IPremiumRequests
{
    void PremiumRequest();
}

public sealed class PremiumRequests : IPremiumRequests
{
    private readonly IPacketSink sink;

    public PremiumRequests(IPacketSink sink) => this.sink = sink;

    public void PremiumRequest()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        sink.Send(p);
    }
}
