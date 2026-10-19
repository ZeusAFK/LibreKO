namespace LibreKO.Session;

public sealed class MiningSession
{
    public event Action<MiningSubOpcode, MiningResult, int>? GatherStartEvent;

    internal void RaiseGatherStart(MiningSubOpcode arg1, MiningResult arg2, int arg3) => GatherStartEvent?.Invoke(arg1, arg2, arg3);

    public event Action<MiningSubOpcode, MiningResult, int, int>? GatherResultEvent;

    internal void RaiseGatherResult(MiningSubOpcode arg1, MiningResult arg2, int arg3, int arg4) => GatherResultEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action<MiningSubOpcode, int, bool>? GatherStopEvent;

    internal void RaiseGatherStop(MiningSubOpcode arg1, int arg2, bool arg3) => GatherStopEvent?.Invoke(arg1, arg2, arg3);

    public event Action<YutRoll>? YutRollEvent;

    internal void RaiseYutRoll(YutRoll roll) => YutRollEvent?.Invoke(roll);

    public event Action<int, short>? ItemDurationEvent;

    internal void RaiseItemDuration(int arg1, short arg2) => ItemDurationEvent?.Invoke(arg1, arg2);
}
