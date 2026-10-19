namespace LibreKO.Network.Requests;

public interface IPieceChangeRequests
{
    void PieceExchange(int npcId, int pieceItemId, int piecePosition);
}

public sealed class PieceChangeRequests : IPieceChangeRequests
{
    private readonly IPacketSink sink;

    public PieceChangeRequests(IPacketSink sink) => this.sink = sink;

    public void PieceExchange(int npcId, int pieceItemId, int piecePosition)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.BifrostExchange);
        p.WriteInt(npcId);
        p.WriteInt(pieceItemId);
        p.WriteByte(piecePosition >= 0 ? (byte)piecePosition : byte.MaxValue);
        sink.Send(p);
    }
}
