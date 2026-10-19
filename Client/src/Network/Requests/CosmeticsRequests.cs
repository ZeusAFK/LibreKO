namespace LibreKO.Network.Requests;

public interface ICosmeticsRequests
{
    void LookToggles(bool hideHelmet, bool hideCostume);
}

public sealed class CosmeticsRequests : ICosmeticsRequests
{
    private readonly IPacketSink sink;

    public CosmeticsRequests(IPacketSink sink) => this.sink = sink;

    public void LookToggles(bool hideHelmet, bool hideCostume)
    {
        var p = new Packet(GameOpcodes.GS_HELMET);
        p.WriteByte((byte)(hideHelmet ? 1 : 0));
        p.WriteByte((byte)(hideCostume ? 1 : 0));
        sink.Send(p);
    }
}
