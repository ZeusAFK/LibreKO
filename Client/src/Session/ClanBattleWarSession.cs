namespace LibreKO.Session;

public sealed class ClanBattleWarSession
{
    public short EnemyClanId { get; private set; }

    internal void ApplyEnemyClan(short clanId) => EnemyClanId = clanId;

    public event Action<ClanBattleResult, int, string>? ClanBattleResultEvent;

    internal void RaiseClanBattleResult(ClanBattleResult arg1, int arg2, string arg3) => ClanBattleResultEvent?.Invoke(arg1, arg2, arg3);

    public event Action<short>? ClanBattleBegunEvent;

    internal void RaiseClanBattleBegun(short value) => ClanBattleBegunEvent?.Invoke(value);

    public event Action<ClanBattleEnd, string>? ClanBattleEndEvent;

    internal void RaiseClanBattleEnd(ClanBattleEnd arg1, string arg2) => ClanBattleEndEvent?.Invoke(arg1, arg2);
}
