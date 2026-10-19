namespace LibreKO.Network.Readers;

internal sealed class DailyRankReader : IPacketReader
{
    private readonly DailyRankSession dailyRank;

    public DailyRankReader(DailyRankSession dailyRank)
    {
        this.dailyRank = dailyRank;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_DAILY_RANK, HandleDailyRank);
    }

    private void HandleDailyRank(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((DailyRankSubOpcode)p.ReadByte())
        {
            case DailyRankSubOpcode.Board when DailyRankWire.ReadBoard(p) is { } board:
                dailyRank.RaiseDailyRankBoard(board);
                break;
            case DailyRankSubOpcode.Reset:
                dailyRank.RaiseDailyRankReset();
                break;
        }
    }
}
