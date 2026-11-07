namespace LibreKO.Network.Requests;

public interface IPremiumRequests
{
    void PremiumRequest();
    void Switch(int type);
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

    public void Switch(int type)
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.WriteByte((byte)PremiumSubOpcode.Switch);
        p.WriteByte((byte)type);
        sink.Send(p);
    }
}
