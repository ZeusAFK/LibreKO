using LibreKO.Network;

namespace LibreKO.Domain;

public enum RepurchaseListing
{
    Ignored,
    Refreshed,
    Opened,
}

public sealed class RepurchaseSession
{
    private bool _awaitingOpen;

    public bool IsOpen { get; private set; }

    public void Request() => _awaitingOpen = true;

    public RepurchaseListing Accept(byte op, bool ok)
    {
        bool opening = op == RepurchaseReply.Open && _awaitingOpen;
        if (!ok || (!opening && !IsOpen)) return RepurchaseListing.Ignored;
        _awaitingOpen = false;
        if (!opening) return RepurchaseListing.Refreshed;
        IsOpen = true;
        return RepurchaseListing.Opened;
    }

    public bool Close()
    {
        _awaitingOpen = false;
        if (!IsOpen) return false;
        IsOpen = false;
        return true;
    }
}
