namespace LibreKO.Network.Readers;

internal sealed class RankReader : IPacketReader
{
    private readonly ChaosSession chaos;
    private readonly RankSession rank;

    public RankReader(ChaosSession chaos, RankSession rank)
    {
        this.chaos = chaos;
        this.rank = rank;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_RANK, HandleRank);
    }

    private void HandleRank(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var rankType = (RankType)p.ReadByte();
        if (rankType == RankType.BorderDefenseWar)
        {
            rank.RaiseBorderWarRank(BorderWarRanking.Read(p));
            return;
        }
        if (rankType == RankType.ChaosDungeon)
        {
            chaos.RaiseChaosRank(ChaosRanking.Read(p));
            return;
        }
        if (rankType == RankType.PkZone) rank.RaiseRank(PkLadderReply.Read(p));
    }
}
