namespace LibreKO.Session;

public sealed class BattleEventSession
{
    public event Action<BattleEventNotice, int>? BattleZoneToggleEvent;

    internal void RaiseBattleZoneToggle(BattleEventNotice arg1, int arg2) => BattleZoneToggleEvent?.Invoke(arg1, arg2);

    public event Action<BattleEventState, int, int>? BattleStatusEvent;

    internal void RaiseBattleStatus(BattleEventState arg1, int arg2, int arg3) => BattleStatusEvent?.Invoke(arg1, arg2, arg3);

    public event Action<BattleEventNotice, int>? BattleResultEvent;

    internal void RaiseBattleResult(BattleEventNotice arg1, int arg2) => BattleResultEvent?.Invoke(arg1, arg2);

    public event Action<MapEventSubOpcode, int, int>? BattleScoreEvent;

    internal void RaiseBattleScore(MapEventSubOpcode arg1, int arg2, int arg3) => BattleScoreEvent?.Invoke(arg1, arg2, arg3);

    public event Action<MapEventReply>? MapEventEvent;

    internal void RaiseMapEvent(MapEventReply mapEventReply) => MapEventEvent?.Invoke(mapEventReply);

    public event Action<List<ClanMatchFighter>>? ClanMatchRosterEvent;

    internal void RaiseClanMatchRoster(List<ClanMatchFighter> list) => ClanMatchRosterEvent?.Invoke(list);

    public event Action<ClanMatchVitals>? ClanMatchVitalsEvent;

    internal void RaiseClanMatchVitals(ClanMatchVitals clanMatchVitals) => ClanMatchVitalsEvent?.Invoke(clanMatchVitals);

    public event Action<int>? ClanMatchResultEvent;

    internal void RaiseClanMatchResult(int value) => ClanMatchResultEvent?.Invoke(value);

    private byte[] monumentOwners = Array.Empty<byte>();

    public IReadOnlyList<byte> MonumentOwners => monumentOwners;

    internal void ApplyMonumentOwners(byte[] owners) => monumentOwners = owners;

    internal void ApplyMonumentCaptured(int index, byte nation) => monumentOwners[index] = nation;

    public long NereidCont { get; private set; }

    public long NereidContGain { get; private set; }

    internal void ApplyNereidCont(long cont)
    {
        NereidContGain = cont - NereidCont;
        NereidCont = cont;
    }

    public event Action<List<RankBoardRow>>? RankBoardEvent;

    internal void RaiseRankBoard(List<RankBoardRow> list) => RankBoardEvent?.Invoke(list);
}
