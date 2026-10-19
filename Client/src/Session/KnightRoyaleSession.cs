namespace LibreKO.Session;

public sealed class KnightRoyaleSession
{
    public event Action<RoyaleSignUp>? RoyaleSignUpEvent;

    internal void RaiseRoyaleSignUp(RoyaleSignUp royaleSignUp) => RoyaleSignUpEvent?.Invoke(royaleSignUp);

    public event Action<RoyaleEntry>? RoyaleEntryEvent;

    private void RaiseRoyaleEntry(RoyaleEntry royaleEntry) => RoyaleEntryEvent?.Invoke(royaleEntry);

    public event Action<RoyaleStats>? RoyaleStatsEvent;

    internal void RaiseRoyaleStats(RoyaleStats royaleStats) => RoyaleStatsEvent?.Invoke(royaleStats);

    public event Action<int>? RoyaleExpEvent;

    internal void RaiseRoyaleExp(int value) => RoyaleExpEvent?.Invoke(value);

    public event Action<string, int>? RoyaleAbandonedEvent;

    internal void RaiseRoyaleAbandoned(string arg1, int arg2) => RoyaleAbandonedEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? RoyaleGasEvent;

    private void RaiseRoyaleGas(int arg1, int arg2) => RoyaleGasEvent?.Invoke(arg1, arg2);

    public event Action<int, int, bool>? RoyalePingEvent;

    internal void RaiseRoyalePing(int arg1, int arg2, bool arg3) => RoyalePingEvent?.Invoke(arg1, arg2, arg3);

    public event Action<KnightRoyale.Reward>? RoyaleRewardEvent;

    internal void RaiseRoyaleReward(KnightRoyale.Reward reward) => RoyaleRewardEvent?.Invoke(reward);

    public event Action<string, int>? RoyaleWinnerEvent;

    internal void RaiseRoyaleWinner(string arg1, int arg2) => RoyaleWinnerEvent?.Invoke(arg1, arg2);

    public event Action<RoyaleLoot>? RoyaleLootEvent;

    internal void RaiseRoyaleLoot(RoyaleLoot royaleLoot) => RoyaleLootEvent?.Invoke(royaleLoot);

    public event Action<RoyaleItem>? RoyaleItemEvent;

    internal void RaiseRoyaleItem(RoyaleItem royaleItem) => RoyaleItemEvent?.Invoke(royaleItem);

    public event Action? RoyaleRestoreEvent;

    private void RaiseRoyaleRestore() => RoyaleRestoreEvent?.Invoke();

    public event Action<bool>? RoyaleDestroyEvent;

    internal void RaiseRoyaleDestroy(bool value) => RoyaleDestroyEvent?.Invoke(value);

    public int RoyaleSurvivors { get; private set; }

    internal void ApplySurvivors(int survivors) => RoyaleSurvivors = survivors;

    public int RoyaleReleasedArea { get; private set; }

    public int RoyaleNextArea { get; private set; }

    public bool RoyaleCountdown { get; private set; }

    private const double MsPerSecond = 1000.0;

    public double NextGasAt { get; private set; }

    public int NextGasSeconds(ulong nowMs) => RoyaleCountdown ? (int)Math.Max(0, Math.Ceiling(NextGasAt - nowMs / MsPerSecond)) : 0;

    private const int SecondsPerMinute = 60;

    internal void ApplyEntry(RoyaleEntry entry)
    {
        RoyaleReleasedArea = 0;
        RoyaleNextArea = 0;
        RoyaleCountdown = true;
        RaiseRoyaleEntry(entry);
    }

    internal void ApplyRestore()
    {
        RoyaleCountdown = false;
        RaiseRoyaleRestore();
    }

    internal void ApplyAbandoned(string name, int survivors)
    {
        RoyaleSurvivors = survivors;
        RaiseRoyaleAbandoned(name, survivors);
    }

    internal void ApplyGas(int released, int minutes, ulong nowMs)
    {
        RoyaleReleasedArea = released;
        RoyaleNextArea = released + 1;
        RoyaleCountdown = !KnightRoyale.GasEnds(RoyaleNextArea);
        NextGasAt = nowMs / MsPerSecond + minutes * SecondsPerMinute;
        RaiseRoyaleGas(released, minutes * SecondsPerMinute);
    }
}
