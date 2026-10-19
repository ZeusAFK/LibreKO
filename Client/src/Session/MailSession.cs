namespace LibreKO.Session;

public sealed class MailSession
{
    public event Action<List<MailEntry>>? MailListEvent;

    internal void RaiseMailList(List<MailEntry> list) => MailListEvent?.Invoke(list);

    public event Action<int, bool, string>? MailReadEvent;

    internal void RaiseMailRead(int arg1, bool arg2, string arg3) => MailReadEvent?.Invoke(arg1, arg2, arg3);

    public event Action<bool, string>? MailSendEvent;

    internal void RaiseMailSend(bool arg1, string arg2) => MailSendEvent?.Invoke(arg1, arg2);

    public event Action<int, bool, string>? MailDeleteEvent;

    internal void RaiseMailDelete(int arg1, bool arg2, string arg3) => MailDeleteEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int, bool, string>? MailClaimEvent;

    internal void RaiseMailClaim(int arg1, bool arg2, string arg3) => MailClaimEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int>? MailUnreadEvent;

    private void RaiseMailUnread(int value) => MailUnreadEvent?.Invoke(value);

    public int MailUnread { get; private set; }

    internal void ApplyUnread(int count)
    {
        MailUnread = count;
        RaiseMailUnread(count);
    }
}
