namespace LibreKO.Session;

public sealed class MonsterStoneSession
{
    private const ulong MsPerSecond = 1000;

    public ulong NestEndsAtMs { get; private set; }

    public int NestSecondsLeft(ulong nowMs) =>
        NestEndsAtMs > nowMs ? (int)((NestEndsAtMs - nowMs + MsPerSecond - 1) / MsPerSecond) : 0;

    public void ClearNestTimer()
    {
        NestEndsAtMs = 0;
        NestCompleted = false;
    }

    public event Action<MonsterStoneResult, int>? MonsterStoneEvent;

    internal void RaiseMonsterStone(MonsterStoneResult monsterStoneResult, int arg2) => MonsterStoneEvent?.Invoke(monsterStoneResult, arg2);

    public int NestStoneItemId { get; private set; }

    public bool NestCompleted { get; private set; }

    internal void ApplyEntered(int stoneItemId)
    {
        NestStoneItemId = stoneItemId;
        ClearNestTimer();
    }

    internal void StartNestTimer(uint seconds, ulong nowMs, bool completed)
    {
        NestCompleted = completed;
        NestEndsAtMs = nowMs + seconds * MsPerSecond;
    }
}
