namespace LibreKO.Session;

public sealed class ChaosSession
{
    public event Action<ChaosRanking>? ChaosRankEvent;

    internal void RaiseChaosRank(ChaosRanking chaosRanking) => ChaosRankEvent?.Invoke(chaosRanking);

    public event Action? ChaosStartedEvent;

    private void RaiseChaosStarted() => ChaosStartedEvent?.Invoke();

    public event Action<uint>? ChaosFinishEvent;

    private void RaiseChaosFinish(uint value) => ChaosFinishEvent?.Invoke(value);

    public bool ChaosStarted { get; private set; }

    public bool ChaosOver { get; private set; }

    private const long MsPerSecond = 1000;

    public ulong FinishEndsAtMs { get; private set; }

    public bool Finished => FinishEndsAtMs != 0;

    public int FinishSecondsLeft(ulong nowMs) =>
        FinishEndsAtMs == 0 ? 0 : (int)Math.Max(0, ((long)FinishEndsAtMs - (long)nowMs) / MsPerSecond);

    public void ConsumeOver() => ChaosOver = false;

    internal void ResetBattle()
    {
        ChaosStarted = false;
        ChaosOver = false;
        DashboardEventId = 0;
        ChaosBannerPrinted = false;
        FinishEndsAtMs = 0;
    }

    public int DashboardEventId { get; private set; }

    internal void ApplyDashboardEvent(int eventId) => DashboardEventId = eventId;

    public bool ChaosBannerPrinted { get; private set; }

    internal void NoteBannerPrinted() => ChaosBannerPrinted = true;

    public int[]? ChaosHotbar { get; private set; }

    internal void KeepHotbar(int[] slots) => ChaosHotbar = slots;

    internal void ForgetHotbar() => ChaosHotbar = null;

    internal void Start()
    {
        ChaosStarted = true;
        RaiseChaosStarted();
    }

    internal void Finish(ulong endsAtMs, uint seconds)
    {
        FinishEndsAtMs = endsAtMs;
        if (ChaosStarted) ChaosOver = true;
        RaiseChaosFinish(seconds);
    }
}
