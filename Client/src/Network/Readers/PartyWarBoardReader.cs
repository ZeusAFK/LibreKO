namespace LibreKO.Network.Readers;

internal sealed class PartyWarBoardReader : IPacketReader
{
    private readonly PartyWarBoardSession partyWarBoard;

    public PartyWarBoardReader(PartyWarBoardSession partyWarBoard)
    {
        this.partyWarBoard = partyWarBoard;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_PARTY, PartySubOpcode.WarBoard, HandlePartyWarBoard);
    }

    private void HandlePartyWarBoard(Packet p)
    {
        if (PartyWarBoardWire.ReadWarBoard(p) is { } reply) partyWarBoard.RaisePartyWarBoard(reply);
    }
}
