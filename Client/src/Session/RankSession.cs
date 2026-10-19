namespace LibreKO.Session;

public sealed class RankSession
{
    public event Action<PkLadderReply>? RankEvent;

    internal void RaiseRank(PkLadderReply pkLadderReply) => RankEvent?.Invoke(pkLadderReply);

    public event Action<BorderWarRanking>? BorderWarRankEvent;

    internal void RaiseBorderWarRank(BorderWarRanking borderWarRanking) => BorderWarRankEvent?.Invoke(borderWarRanking);
}
