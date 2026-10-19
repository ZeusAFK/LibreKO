namespace LibreKO.Network.Requests;

public interface IBountyRequests
{
    void List();
    void Post(string target, int reward);
    void Claim(int bountyId);
}

public sealed class BountyRequests : IBountyRequests
{
    private readonly IPacketSink sink;

    public BountyRequests(IPacketSink sink) => this.sink = sink;

    public void List()
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte((byte)BountySubOpcode.List);
        sink.Send(p);
    }

    public void Post(string target, int reward)
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte((byte)BountySubOpcode.Post);
        p.WriteSByteString(target ?? "");
        p.WriteInt(reward);
        sink.Send(p);
    }

    public void Claim(int bountyId)
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte((byte)BountySubOpcode.Claim);
        p.WriteInt(bountyId);
        sink.Send(p);
    }
}
