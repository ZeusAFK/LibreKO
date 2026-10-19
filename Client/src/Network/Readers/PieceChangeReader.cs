namespace LibreKO.Network.Readers;

internal sealed class PieceChangeReader : IPacketReader
{
    private readonly PieceChangeSession pieceChange;

    public PieceChangeReader(PieceChangeSession pieceChange)
    {
        this.pieceChange = pieceChange;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.BifrostRequest, HandlePieceChangeOpen);
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.BifrostExchange, HandlePieceExchangeResult);
    }

    private void HandlePieceChangeOpen(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        pieceChange.RaisePieceChangeOpen(p.ReadInt());
    }

    private void HandlePieceExchangeResult(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var result = (BifrostResult)p.ReadByte();

        int rewardItemId = 0, rewardPosition = -1, pieceItemId = 0, piecePosition = -1;
        byte effect = 0;

        if (result == BifrostResult.Succeeded && p.RemainingBytes >= 5)
        {
            rewardItemId = p.ReadInt();
            rewardPosition = unchecked((sbyte)p.ReadByte());
            if (p.RemainingBytes >= 5)
            {
                pieceItemId = p.ReadInt();
                piecePosition = unchecked((sbyte)p.ReadByte());
            }
            if (p.RemainingBytes >= 1)
                effect = p.ReadByte();
        }

        pieceChange.RaisePieceExchangeResult(new PieceExchangeResult(
            result, rewardItemId, rewardPosition, pieceItemId, piecePosition, effect));
    }
}
