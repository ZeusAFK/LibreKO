namespace LibreKO.Session;

public sealed class ZoneAbilitySession
{
    private readonly HashSet<byte> ownStatus = new();

    public IReadOnlyCollection<byte> OwnStatus => ownStatus;

    internal void SetOwnStatus(byte type, bool inflicted)
    {
        if (inflicted) ownStatus.Add(type); else ownStatus.Remove(type);
    }

    internal void ClearOwnStatus() => ownStatus.Clear();

    public ZoneAbilityInfo CurrentZoneAbility { get; private set; } = new() { ZoneType = ZoneAbilityType.Neutral, Tariff = 10 };

    internal void ApplyZoneAbility(ZoneAbilityInfo info) => CurrentZoneAbility = info;

    public void SetTariff(int tariff)
    {
        var zone = CurrentZoneAbility;
        zone.Tariff = tariff;
        CurrentZoneAbility = zone;
    }

    public event Action<ZoneAbilityInfo>? ZoneAbilityEvent;

    internal void RaiseZoneAbility(ZoneAbilityInfo zoneAbilityInfo) => ZoneAbilityEvent?.Invoke(zoneAbilityInfo);

    public event Action<byte, bool>? OwnStatusEvent;

    internal void RaiseOwnStatus(byte arg1, bool arg2) => OwnStatusEvent?.Invoke(arg1, arg2);
}
