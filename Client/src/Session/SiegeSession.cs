namespace LibreKO.Session;

public sealed class SiegeSession
{
    public event Action? SiegeGuardEvent;

    internal void RaiseSiegeGuard() => SiegeGuardEvent?.Invoke();

    public event Action<SiegeSchedule>? SiegeScheduleEvent;

    internal void RaiseSiegeSchedule(SiegeSchedule siegeSchedule) => SiegeScheduleEvent?.Invoke(siegeSchedule);

    public event Action<short>? SiegeApplyEvent;

    internal void RaiseSiegeApply(short value) => SiegeApplyEvent?.Invoke(value);

    public event Action<SiegeChallengers>? SiegeChallengersEvent;

    internal void RaiseSiegeChallengers(SiegeChallengers siegeChallengers) => SiegeChallengersEvent?.Invoke(siegeChallengers);

    public event Action<SiegeDefenders>? SiegeDefendersEvent;

    internal void RaiseSiegeDefenders(SiegeDefenders siegeDefenders) => SiegeDefendersEvent?.Invoke(siegeDefenders);

    public event Action<SiegeOffice>? SiegeOfficeEvent;

    internal void RaiseSiegeOffice(SiegeOffice siegeOffice) => SiegeOfficeEvent?.Invoke(siegeOffice);

    public event Action<SiegeCollected>? SiegeCollectedEvent;

    internal void RaiseSiegeCollected(SiegeCollected siegeCollected) => SiegeCollectedEvent?.Invoke(siegeCollected);

    public event Action<SiegeTaxRates>? SiegeTaxRatesEvent;

    internal void RaiseSiegeTaxRates(SiegeTaxRates siegeTaxRates) => SiegeTaxRatesEvent?.Invoke(siegeTaxRates);

    public event Action<SiegeRateChanged>? SiegeRateChangedEvent;

    internal void RaiseSiegeRateChanged(SiegeRateChanged siegeRateChanged) => SiegeRateChangedEvent?.Invoke(siegeRateChanged);

    public event Action? SiegeWarEvent;

    internal void RaiseSiegeWar() => SiegeWarEvent?.Invoke();

    public event Action<short>? SiegeBaseEvent;

    internal void RaiseSiegeBase(short value) => SiegeBaseEvent?.Invoke(value);

    public event Action<SiegePoints>? SiegePointsEvent;

    private void RaiseSiegePoints(SiegePoints siegePoints) => SiegePointsEvent?.Invoke(siegePoints);

    public SiegeWarStatus SiegeWar { get; private set; } = SiegeWarStatus.None;

    internal void ApplySiegeWar(SiegeWarStatus status) => SiegeWar = status;

    internal void ResetForWorld()
    {
        SiegeWar = SiegeWarStatus.None;
        SiegePointTotal = 0;
        SiegeTowerPending = false;
    }

    public int SiegePointTotal { get; private set; }

    public event Action<SiegeTowerReply>? SiegeTowerEvent;

    internal void RaiseSiegeTower(SiegeTowerReply siegeTowerReply) => SiegeTowerEvent?.Invoke(siegeTowerReply);

    public bool SiegeTowerPending { get; internal set; }

    internal void ApplyPoints(SiegePoints points)
    {
        SiegePointTotal = points.Kind == SiegePointsKind.Renewed ? 0 : points.Total;
        RaiseSiegePoints(points);
    }
}
