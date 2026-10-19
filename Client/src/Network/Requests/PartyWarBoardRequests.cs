namespace LibreKO.Network.Requests;

public interface IPartyWarBoardRequests
{
    void Send(PartyWarBoardTarget target, PartyBoardMeterMode mode);
}

public sealed class PartyWarBoardRequests : IPartyWarBoardRequests
{
    private readonly IPacketSink sink;
    private readonly PartyWarBoardSession warBoard;

    public PartyWarBoardRequests(IPacketSink sink, PartyWarBoardSession warBoard)
    {
        this.sink = sink;
        this.warBoard = warBoard;
    }

    public void Send(PartyWarBoardTarget target, PartyBoardMeterMode mode)
    {
        if (target == PartyWarBoardTarget.Rows) Config.SetPartyRowMeter(mode);
        else warBoard.ApplyMeterMode(mode);
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte((byte)PartySubOpcode.WarBoard);
        p.WriteByte((byte)target);
        p.WriteByte((byte)mode);
        sink.Send(p);
    }
}
