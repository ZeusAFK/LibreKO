namespace LibreKO.Session;

public sealed class PetSession
{
    private readonly Dictionary<int, PetItemInfo> items = new();

    public IReadOnlyDictionary<int, PetItemInfo> Items => items;

    public PetSheet? Pet { get; private set; }

    internal void Remember(PetItemInfo? info)
    {
        if (info is { } known) items[known.Index] = known;
    }

    public PetFeedRequest? PendingFeed { get; internal set; }

    public PetIncubationRequest? PendingIncubation { get; internal set; }

    public event Action<PetSheet>? PetSummonedEvent;

    internal void RaisePetSummoned(PetSheet petSheet) => PetSummonedEvent?.Invoke(petSheet);

    public event Action? PetGoneEvent;

    private void RaisePetGone() => PetGoneEvent?.Invoke();

    public event Action<PetMode>? PetModeEvent;

    internal void RaisePetMode(PetMode value) => PetModeEvent?.Invoke(value);

    public event Action? PetVitalsEvent;

    internal void RaisePetVitals() => PetVitalsEvent?.Invoke();

    public event Action<int>? PetHpDeltaEvent;

    internal void RaisePetHpDelta(int value) => PetHpDeltaEvent?.Invoke(value);

    public event Action<int>? PetMpDeltaEvent;

    internal void RaisePetMpDelta(int value) => PetMpDeltaEvent?.Invoke(value);

    public event Action<long>? PetExpEvent;

    internal void RaisePetExp(long value) => PetExpEvent?.Invoke(value);

    public event Action<int, int, int, int>? PetFedEvent;

    internal void RaisePetFed(int arg1, int arg2, int arg3, int arg4) => PetFedEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action<int>? PetFoodRefusedEvent;

    internal void RaisePetFoodRefused(int value) => PetFoodRefusedEvent?.Invoke(value);

    public event Action<int, int>? PetStrikeEvent;

    internal void RaisePetStrike(int arg1, int arg2) => PetStrikeEvent?.Invoke(arg1, arg2);

    public event Action<int>? PetLevelUpEvent;

    internal void RaisePetLevelUp(int value) => PetLevelUpEvent?.Invoke(value);

    public event Action<int, PetItemInfo>? PetHatchedEvent;

    internal void RaisePetHatched(int arg1, PetItemInfo petItemInfo) => PetHatchedEvent?.Invoke(arg1, petItemInfo);

    public event Action<int>? PetHatchFailedEvent;

    internal void RaisePetHatchFailed(int value) => PetHatchFailedEvent?.Invoke(value);

    public event Action<int, PetItemInfo>? PetTransformedEvent;

    internal void RaisePetTransformed(int arg1, PetItemInfo petItemInfo) => PetTransformedEvent?.Invoke(arg1, petItemInfo);

    public event Action<int>? PetTransformFailedEvent;

    internal void RaisePetTransformFailed(int value) => PetTransformFailedEvent?.Invoke(value);

    public event Action? PetResetEvent;

    private void RaisePetReset() => PetResetEvent?.Invoke();

    internal void Reset()
    {
        PendingFeed = null;
        PendingIncubation = null;
        Pet = null;
        items.Clear();
        RaisePetGone();
        RaisePetReset();
    }

    internal void ApplySummoned(PetSheet sheet)
    {
        sheet.Mode = Pet?.Mode ?? PetMode.Defence;
        Pet = sheet;
        RememberSheet(sheet);
        RaisePetSummoned(sheet);
    }

    internal void ApplyMode(PetMode mode)
    {
        if (Pet is { } sheet) sheet.Mode = mode;
        RaisePetMode(mode);
    }

    internal void ApplyHp(int maxHp, int hp)
    {
        if (Pet is not { } sheet) return;
        bool known = sheet.MaxHp > 0;
        int oldHp = sheet.Hp;
        sheet.MaxHp = maxHp;
        sheet.Hp = hp;
        RaisePetVitals();
        if (known && hp != oldHp) RaisePetHpDelta(hp - oldHp);
    }

    internal void ApplyMp(int maxMp, int mp)
    {
        if (Pet is not { } sheet) return;
        bool known = sheet.MaxMp > 0;
        int oldMp = sheet.Mp;
        sheet.MaxMp = maxMp;
        sheet.Mp = mp;
        RaisePetVitals();
        if (known && mp != oldMp) RaisePetMpDelta(mp - oldMp);
    }

    internal void ApplyExp(long gained, int percent, int level, int satisfaction)
    {
        if (Pet is { } sheet)
        {
            sheet.ExpPercent = percent;
            sheet.Level = level;
            sheet.Satisfaction = satisfaction;
            RememberSheet(sheet);
        }
        RaisePetExp(gained);
    }

    internal void ApplySatisfaction(int satisfaction)
    {
        if (Pet is { } sheet)
        {
            sheet.Satisfaction = satisfaction;
            RememberSheet(sheet);
        }
        RaisePetVitals();
    }

    private void RememberSheet(PetSheet sheet) =>
        items[sheet.Index] = new PetItemInfo(sheet.Index, sheet.Name, sheet.Attack, sheet.Level, sheet.ExpPercent, sheet.Satisfaction);

    internal void ApplyDied()
    {
        Pet = null;
        RaisePetGone();
    }
}
