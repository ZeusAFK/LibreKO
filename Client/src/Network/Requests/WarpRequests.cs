namespace LibreKO.Network.Requests;

public interface IWarpRequests
{
    void ListRequest(int npcId);
    void Select(int npcId, int warpId);
}

public sealed class WarpRequests : IWarpRequests
{
    private readonly IPacketSink sink;

    public WarpRequests(IPacketSink sink) => this.sink = sink;

    public void ListRequest(int npcId)
    {
        var p = new Packet(GameOpcodes.GS_WARP_LIST);
        p.WriteShort((short)npcId);
        sink.Send(p);
    }

    public void Select(int npcId, int warpId)
    {
        var p = new Packet(GameOpcodes.GS_WARP_LIST);
        p.WriteShort((short)npcId);
        p.WriteShort((short)warpId);
        sink.Send(p);
    }
}
