namespace LibreKO.Session;

public sealed class BifrostSession
{
    private const ulong MsPerSecond = 1000;

    public ulong DrakiEndsAtMs { get; private set; }

    public int DrakiSecondsLeft(ulong nowMs) =>
        DrakiEndsAtMs > nowMs ? (int)((DrakiEndsAtMs - nowMs + MsPerSecond - 1) / MsPerSecond) : 0;

    public int DrakiStage { get; private set; }

    public int DrakiSubStage { get; private set; }

    public DrakiStageEnd? DrakiFinish { get; private set; }

    internal void ApplyDrakiTimer(int stage, int subStage, int secondsLeft, ulong nowMs)
    {
        DrakiStage = stage;
        DrakiSubStage = subStage;
        DrakiEndsAtMs = secondsLeft > 0 ? nowMs + (ulong)secondsLeft * MsPerSecond : 0;
        DrakiFinish = null;
    }

    internal void ApplyDrakiLeave()
    {
        DrakiEndsAtMs = 0;
        DrakiFinish = null;
    }

    public event Action<DrakiEnterResult>? DrakiEnterResultEvent;

    internal void RaiseDrakiEnterResult(DrakiEnterResult result) => DrakiEnterResultEvent?.Invoke(result);

    public event Action<DrakiRankList>? DrakiRankListEvent;

    internal void RaiseDrakiRankList(DrakiRankList list) => DrakiRankListEvent?.Invoke(list);

    internal void ApplyDrakiStageEnd(DrakiStageEnd finish)
    {
        DrakiStage = finish.Stage;
        DrakiSubStage = finish.SubStage;
        DrakiEndsAtMs = 0;
        DrakiFinish = finish;
    }

    public event Action<int, TempleEventType>? BifrostTimeEvent;

    internal void RaiseBifrostTime(int arg1, TempleEventType templeEventType) => BifrostTimeEvent?.Invoke(arg1, templeEventType);

    public event Action<TempleJoinResult, int>? BifrostJoinEvent;

    internal void RaiseBifrostJoin(TempleJoinResult arg1, int arg2) => BifrostJoinEvent?.Invoke(arg1, arg2);

    public event Action<int, int, int>? TempleSignUpsEvent;

    internal void RaiseTempleSignUps(int arg1, int arg2, int arg3) => TempleSignUpsEvent?.Invoke(arg1, arg2, arg3);

    public event Action? BifrostDisbandEvent;

    internal void RaiseBifrostDisband() => BifrostDisbandEvent?.Invoke();

    public BorderWarState BorderWar { get; } = new();

    public TempleSignUpBoard SignUps { get; } = new();

    public event Action<int, int>? TempleScreenScoreEvent;

    internal void RaiseTempleScreenScore(int arg1, int arg2) => TempleScreenScoreEvent?.Invoke(arg1, arg2);

    public event Action<int>? AltarTimerEvent;

    internal void RaiseAltarTimer(int value) => AltarTimerEvent?.Invoke(value);

    public event Action<int, int, uint>? TempleEventFinishEvent;

    internal void RaiseTempleEventFinish(int arg1, int arg2, uint arg3) => TempleEventFinishEvent?.Invoke(arg1, arg2, arg3);

    public event Action<string, byte>? AltarFlagEvent;

    internal void RaiseAltarFlag(string arg1, byte arg2) => AltarFlagEvent?.Invoke(arg1, arg2);

    public event Action<byte>? FullMoonEntryEvent;

    internal void RaiseFullMoonEntry(byte value) => FullMoonEntryEvent?.Invoke(value);

    public event Action<byte>? FullMoonTowerEvent;

    internal void RaiseFullMoonTower(byte value) => FullMoonTowerEvent?.Invoke(value);

    public event Action<byte, byte>? FullMoonStageEvent;

    internal void RaiseFullMoonStage(byte arg1, byte arg2) => FullMoonStageEvent?.Invoke(arg1, arg2);

    public int[]? FullMoonHotbar { get; private set; }

    internal void KeepFullMoonHotbar(int[] slots) => FullMoonHotbar = slots;

    internal void ForgetFullMoonHotbar() => FullMoonHotbar = null;
}
