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
    private bool awaitingOpen;

    public bool IsOpen { get; private set; }

    public void Request() => awaitingOpen = true;

    public RepurchaseListing Accept(RepurchaseSubOpcode op, bool ok)
    {
        bool opening = op == RepurchaseSubOpcode.Open && awaitingOpen;
        if (!ok || (!opening && !IsOpen)) return RepurchaseListing.Ignored;
        awaitingOpen = false;
        if (!opening) return RepurchaseListing.Refreshed;
        IsOpen = true;
        return RepurchaseListing.Opened;
    }

    public bool Close()
    {
        awaitingOpen = false;
        if (!IsOpen) return false;
        IsOpen = false;
        return true;
    }
}
