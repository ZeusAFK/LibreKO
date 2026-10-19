namespace LibreKO.Session;

public sealed class KingSession
{
    public event Action<string>? KingElectionOpenEvent;

    internal void RaiseKingElectionOpen(string value) => KingElectionOpenEvent?.Invoke(value);

    public event Action<KingSchedule>? KingScheduleEvent;

    internal void RaiseKingSchedule(KingSchedule kingSchedule) => KingScheduleEvent?.Invoke(kingSchedule);

    public event Action<KingReply, short>? KingResultEvent;

    internal void RaiseKingResult(KingReply kingReply, short arg2) => KingResultEvent?.Invoke(kingReply, arg2);

    public event Action<KingCandidates>? KingCandidatesEvent;

    internal void RaiseKingCandidates(KingCandidates kingCandidates) => KingCandidatesEvent?.Invoke(kingCandidates);

    public event Action<KingPlan>? KingPlanEvent;

    internal void RaiseKingPlan(KingPlan kingPlan) => KingPlanEvent?.Invoke(kingPlan);

    public event Action<KingShout>? KingShoutEvent;

    internal void RaiseKingShout(KingShout kingShout) => KingShoutEvent?.Invoke(kingShout);

    public event Action<KingSenators>? KingSenatorsEvent;

    internal void RaiseKingSenators(KingSenators kingSenators) => KingSenatorsEvent?.Invoke(kingSenators);

    public event Action? KingImpeachmentProposedEvent;

    internal void RaiseKingImpeachmentProposed() => KingImpeachmentProposedEvent?.Invoke();

    public event Action<KingTreasury>? KingTreasuryEvent;

    internal void RaiseKingTreasury(KingTreasury kingTreasury) => KingTreasuryEvent?.Invoke(kingTreasury);

    public event Action<KingCoins>? KingFundEvent;

    internal void RaiseKingFund(KingCoins kingCoins) => KingFundEvent?.Invoke(kingCoins);

    public event Action<KingTariff>? KingTariffReadEvent;

    internal void RaiseKingTariffRead(KingTariff kingTariff) => KingTariffReadEvent?.Invoke(kingTariff);

    public event Action<KingTariff>? KingTariffSetEvent;

    internal void RaiseKingTariffSet(KingTariff kingTariff) => KingTariffSetEvent?.Invoke(kingTariff);

    public event Action<KingCoins>? KingReserveEvent;

    internal void RaiseKingReserve(KingCoins kingCoins) => KingReserveEvent?.Invoke(kingCoins);

    public event Action<string>? KingIntroEvent;

    internal void RaiseKingIntro(string value) => KingIntroEvent?.Invoke(value);

    public event Action<bool>? KingIntroSavedEvent;

    internal void RaiseKingIntroSaved(bool value) => KingIntroSavedEvent?.Invoke(value);

    public event Action<KingTreasuryNotice>? KingTreasuryNoticeEvent;

    internal void RaiseKingTreasuryNotice(KingTreasuryNotice kingTreasuryNotice) => KingTreasuryNoticeEvent?.Invoke(kingTreasuryNotice);

    public event Action<KingEventSubOpcode, short>? KingEventResultEvent;

    internal void RaiseKingEventResult(KingEventSubOpcode arg1, short arg2) => KingEventResultEvent?.Invoke(arg1, arg2);

    public event Action<byte, string>? RoyalOrderEvent;

    internal void RaiseRoyalOrder(byte arg1, string arg2) => RoyalOrderEvent?.Invoke(arg1, arg2);
}
