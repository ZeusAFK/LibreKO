namespace LibreKO.Session;

public sealed class ClanPremiumSession
{
    public event Action<bool>? ClanPremiumEvent;

    internal void RaiseClanPremium(bool value) => ClanPremiumEvent?.Invoke(value);

    public bool ClanPremiumActive { get; private set; }

    public bool ClanPremiumKnown { get; private set; }

    public long ClanPremiumEndsAt { get; private set; }

    internal void ApplyStatus(bool active, long endsAt)
    {
        ClanPremiumActive = active;
        ClanPremiumKnown = true;
        ClanPremiumEndsAt = endsAt;
        RaiseClanPremium(active);
    }
}
