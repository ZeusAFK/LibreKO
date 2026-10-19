namespace LibreKO.Network.Requests;

public interface IClanPremiumRequests
{
    void Query();
}

public sealed class ClanPremiumRequests : IClanPremiumRequests
{
    private readonly IPacketSink sink;

    public ClanPremiumRequests(IPacketSink sink) => this.sink = sink;

    public void Query()
    {
        var p = new Packet(GameOpcodes.GS_CLAN_PREMIUM);
        p.WriteByte((byte)ClanPremiumSubOpcode.Query);
        sink.Send(p);
    }
}
