namespace LibreKO.Session;

public sealed class UpgradeNoticeSession
{
    public event Action<bool, string, int>? UpgradeNoticeEvent;

    internal void RaiseUpgradeNotice(bool arg1, string arg2, int arg3) => UpgradeNoticeEvent?.Invoke(arg1, arg2, arg3);
}
