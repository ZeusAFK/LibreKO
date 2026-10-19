namespace LibreKO.Session;

public sealed class RouletteSession
{
    public event Action<int>? RouletteStatusEvent;

    internal void RaiseRouletteStatus(int value) => RouletteStatusEvent?.Invoke(value);

    public event Action<bool, int, int>? RouletteSpinEvent;

    internal void RaiseRouletteSpin(bool arg1, int arg2, int arg3) => RouletteSpinEvent?.Invoke(arg1, arg2, arg3);

    public event Action<RoulettePrize[]>? RoulettePrizeLogEvent;

    internal void RaiseRoulettePrizeLog(RoulettePrize[] roulettePrize) => RoulettePrizeLogEvent?.Invoke(roulettePrize);
}
