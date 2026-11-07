namespace LibreKO.Session;

public sealed class PremiumSession
{
    private const int StatusNone = 0;
    private const int StatusActive = 1;

    public bool HasPremium => PremiumAccountStatus != 0 && PremiumHours > 0;

    public event Action<int, int, int>? PremiumEvent;

    public event Action<PremiumSwitchReply>? SwitchReplied;

    internal void RaisePremium(int arg1, int arg2, int arg3) => PremiumEvent?.Invoke(arg1, arg2, arg3);

    internal void RaiseSwitch(PremiumSwitchReply reply) => SwitchReplied?.Invoke(reply);

    public int PremiumAccountStatus { get; private set; }

    public int PremiumType { get; private set; }

    public int PremiumHours { get; private set; }

    public IReadOnlyList<HeldPremium> Held { get; private set; } = Array.Empty<HeldPremium>();

    public IReadOnlyList<PremiumBonus> PremiumBonuses { get; private set; } = Array.Empty<PremiumBonus>();

    internal void Apply(PremiumList list, IReadOnlyList<PremiumBonus> bonuses)
    {
        int hours = list.InUseHours;
        PremiumAccountStatus = hours > 0 ? StatusActive : StatusNone;
        PremiumType = hours > 0 ? list.InUse : 0;
        PremiumHours = hours;
        Held = list.Held;
        PremiumBonuses = bonuses;
    }
}
