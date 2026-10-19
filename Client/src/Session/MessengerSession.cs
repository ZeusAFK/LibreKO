namespace LibreKO.Session;

public sealed class MessengerSession
{
    public event Action<List<MessengerBuddy>>? MessengerListEvent;

    internal void RaiseMessengerList(List<MessengerBuddy> list) => MessengerListEvent?.Invoke(list);

    public event Action<string, byte>? MessengerSendResultEvent;

    internal void RaiseMessengerSendResult(string arg1, byte arg2) => MessengerSendResultEvent?.Invoke(arg1, arg2);
}
