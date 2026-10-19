namespace LibreKO.Network.Requests;

public interface IRouletteRequests
{
    void Status();
    void PrizeLog();
    void Spin();
}

public sealed class RouletteRequests : IRouletteRequests
{
    private readonly IPacketSink sink;

    public RouletteRequests(IPacketSink sink) => this.sink = sink;

    public void Status()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.RouletteOpen);
        sink.Send(p);
    }

    public void PrizeLog()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.RoulettePrizeList);
        p.WriteInt(0);
        sink.Send(p);
    }

    public void Spin()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.RouletteSpin);
        sink.Send(p);
    }
}
