namespace LibreKO.Session;

public sealed class ClanBattleSession
{
    public event Action? ClanBattleNotifyEvent;

    internal void RaiseClanBattleNotify() => ClanBattleNotifyEvent?.Invoke();

    public event Action<byte>? IdentityCheckResultEvent;

    internal void RaiseIdentityCheckResult(byte value) => IdentityCheckResultEvent?.Invoke(value);
}
