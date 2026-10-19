namespace LibreKO.Session;

public sealed class NameChangeSession
{
    public event Action<NameChangeResult>? NameChangeResultEvent;

    internal void RaiseNameChangeResult(NameChangeResult value) => NameChangeResultEvent?.Invoke(value);

    public event Action<string>? NameChangeSuccessEvent;

    internal void RaiseNameChangeSuccess(string value) => NameChangeSuccessEvent?.Invoke(value);

    public event Action<ClanNameChangeResult>? ClanNameChangeResultEvent;

    internal void RaiseClanNameChangeResult(ClanNameChangeResult value) => ClanNameChangeResultEvent?.Invoke(value);

    public event Action<string>? ClanRenamedEvent;

    internal void RaiseClanRenamed(string value) => ClanRenamedEvent?.Invoke(value);
}
