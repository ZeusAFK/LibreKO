namespace LibreKO.Session;

public sealed class ZoneSession
{
    public event Action<int, int>? TimeEvent;

    private void RaiseTime(int arg1, int arg2) => TimeEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? WeatherEvent;

    private void RaiseWeather(int arg1, int arg2) => WeatherEvent?.Invoke(arg1, arg2);

    public event Action<string>? NoticeEvent;

    internal void RaiseNotice(string value) => NoticeEvent?.Invoke(value);

    public event Action<string>? TradeBlockedEvent;

    internal void RaiseTradeBlocked(string value) => TradeBlockedEvent?.Invoke(value);

    public event Action? EventTitlesChangedEvent;

    internal void RaiseEventTitlesChanged() => EventTitlesChangedEvent?.Invoke();

    public EventTitles EventTitles { get; } = new();

    public event Action<float, float>? WarpEvent;

    internal void RaiseWarp(float arg1, float arg2) => WarpEvent?.Invoke(arg1, arg2);

    public event Action<GroundEffect>? GroundEffectEvent;

    private void RaiseGroundEffect(GroundEffect groundEffect) => GroundEffectEvent?.Invoke(groundEffect);

    public GroundEffect GroundEffect { get; private set; }

    public float GroundSpeedFactor { get; private set; } = 1f;

    public string FlashBonusTitle { get; private set; } = "";

    internal void ShowFlashBonus(string title)
    {
        if (FlashBonusTitle.Length > 0) EventTitles.Remove(FlashBonusTitle);
        FlashBonusTitle = title;
        if (title.Length > 0)
        {
            EventTitles.Show(title, "");
            EventTitles.Expand();
        }
        RaiseEventTitlesChanged();
    }

    internal void ForgetEventTitles()
    {
        EventTitles.Clear();
        FlashBonusTitle = "";
    }

    public (int Hour, int Minute)? LastTime { get; private set; }

    public (int Type, int Amount)? LastWeather { get; private set; }

    internal void ForgetGroundEffect()
    {
        GroundEffect = GroundEffect.None;
        GroundSpeedFactor = 1f;
    }

    internal void ApplyGroundEffect(GroundEffect effect)
    {
        GroundEffect = effect;
        GroundSpeedFactor = GroundEffects.SpeedFactor(effect, GroundSpeedFactor);
        RaiseGroundEffect(effect);
    }

    internal void ApplyTime(int hour, int minute)
    {
        LastTime = (hour, minute);
        RaiseTime(hour, minute);
    }

    internal void ApplyWeather(int type, int amount)
    {
        LastWeather = (type, amount);
        RaiseWeather(type, amount);
    }
}
