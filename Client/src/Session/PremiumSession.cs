namespace LibreKO.Session;

public sealed class PremiumSession
{
    public bool HasPremium => PremiumAccountStatus != 0 && PremiumHours > 0;

    public event Action<int, int, int>? PremiumEvent;

    internal void RaisePremium(int arg1, int arg2, int arg3) => PremiumEvent?.Invoke(arg1, arg2, arg3);

    public int PremiumAccountStatus { get; private set; }

    public int PremiumType { get; private set; }

    public int PremiumHours { get; private set; }

    public IReadOnlyList<PremiumBonus> PremiumBonuses { get; private set; } = Array.Empty<PremiumBonus>();

    internal void Apply(int accountStatus, int premiumType, int premiumHours, IReadOnlyList<PremiumBonus> bonuses)
    {
        PremiumAccountStatus = accountStatus;
        PremiumType = premiumType;
        PremiumHours = premiumHours;
        PremiumBonuses = bonuses;
    }
}
