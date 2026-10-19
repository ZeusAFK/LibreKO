namespace LibreKO.Session;

public sealed class CapeSession
{
    public bool Pending { get; internal set; }

    public event Action<CapeChangeReply>? CapeResultEvent;

    private void RaiseCapeResult(CapeChangeReply reply) => CapeResultEvent?.Invoke(reply);

    public event Action? CapeResetEvent;

    private void RaiseCapeReset() => CapeResetEvent?.Invoke();

    internal void ApplyResult(CapeChangeReply reply)
    {
        Pending = false;
        RaiseCapeResult(reply);
    }

    internal void ApplyReset()
    {
        Pending = false;
        RaiseCapeReset();
    }
}
