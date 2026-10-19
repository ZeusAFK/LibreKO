namespace LibreKO.Session;

public sealed class ShoutSession
{
    public event Action<ShoutReply>? ShoutEvent;

    internal void RaiseShout(ShoutReply reply) => ShoutEvent?.Invoke(reply);

    public event Action<bool, string, int, int>? ShoutUpgradeEvent;

    internal void RaiseShoutUpgrade(bool arg1, string arg2, int arg3, int arg4) => ShoutUpgradeEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action<string, int, byte>? ShoutRareItemEvent;

    internal void RaiseShoutRareItem(string arg1, int arg2, byte arg3) => ShoutRareItemEvent?.Invoke(arg1, arg2, arg3);

    public event Action<LogosShoutRegisterResult, int>? ShoutResultEvent;

    internal void RaiseShoutResult(LogosShoutRegisterResult arg1, int arg2) => ShoutResultEvent?.Invoke(arg1, arg2);
}
