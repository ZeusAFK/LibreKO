namespace LibreKO.Session;

public sealed class AccountLockSession
{
    public event Action<AccountInUse>? AccountInUseEvent;

    internal void RaiseAccountInUse(AccountInUse accountInUse) => AccountInUseEvent?.Invoke(accountInUse);

    public event Action<AccountKickCode>? KickResultEvent;

    internal void RaiseKickResult(AccountKickCode accountKickCode) => KickResultEvent?.Invoke(accountKickCode);

    public bool Evicted { get; private set; }

    internal void ApplyEvicted(bool evicted) => Evicted = evicted;
}
