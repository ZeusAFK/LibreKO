namespace LibreKO.Network.Requests;

public interface IDailyRankRequests
{
    void DailyRankRequest(int board);
}

public sealed class DailyRankRequests : IDailyRankRequests
{
    private readonly IPacketSink sink;

    public DailyRankRequests(IPacketSink sink) => this.sink = sink;

    public void DailyRankRequest(int board)
    {
        var p = new Packet(GameOpcodes.GS_DAILY_RANK);
        p.WriteByte((byte)DailyRankSubOpcode.Board);
        p.WriteByte((byte)board);
        sink.Send(p);
    }
}
