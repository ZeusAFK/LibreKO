using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<string>? KingElectionOpenEvent;
    public event Action<KingSchedule>? KingScheduleEvent;
    public event Action<KingReply, short>? KingResultEvent;
    public event Action<KingCandidates>? KingCandidatesEvent;
    public event Action<KingPlan>? KingPlanEvent;
    public event Action<KingShout>? KingShoutEvent;
    public event Action<KingSenators>? KingSenatorsEvent;
    public event Action? KingImpeachmentProposedEvent;
    public event Action<KingTreasury>? KingTreasuryEvent;
    public event Action<KingCoins>? KingFundEvent;
    public event Action<KingTariff>? KingTariffReadEvent;
    public event Action<KingTariff>? KingTariffSetEvent;
    public event Action<KingCoins>? KingReserveEvent;
    public event Action<string>? KingIntroEvent;
    public event Action<bool>? KingIntroSavedEvent;
    public event Action<KingTreasuryNotice>? KingTreasuryNoticeEvent;

    private void HandleKing(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch (p.ReadByte())
        {
            case KingElection.Election:
                HandleKingElection(p);
                break;
            case KingElection.Impeachment:
                HandleKingImpeachment(p);
                break;
            case KingElection.Tax:
                HandleKingTax(p);
                break;
            case KingElection.ElectionOfficer:
                KingElectionOpenEvent?.Invoke(KingWire.Str8(p));
                break;
            case KingElection.NationIntro:
                HandleKingIntro(p);
                break;
            case KingElection.TreasuryNotice:
                KingTreasuryNoticeEvent?.Invoke(KingWire.ReadTreasuryNotice(p));
                break;
        }
    }

    private void HandleKingElection(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case KingElection.Schedule:
                KingScheduleEvent?.Invoke(KingWire.ReadSchedule(p));
                break;
            case KingElection.Nominate:
                KingResultEvent?.Invoke(KingReply.Nominate, KingWire.I16(p));
                break;
            case KingElection.Plan:
                HandleKingPlan(p);
                break;
            case KingElection.Poll:
                HandleKingPoll(p);
                break;
            case KingElection.Withdraw:
                KingResultEvent?.Invoke(KingReply.Withdraw, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingPlan(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case KingElection.PlanWrite:
                KingResultEvent?.Invoke(KingReply.PlanPosted, KingWire.I16(p));
                break;
            case KingElection.PlanRead:
                switch (KingWire.U8(p))
                {
                    case KingElection.PlanText:
                        KingPlanEvent?.Invoke(KingWire.ReadPlan(p));
                        break;
                    case KingElection.PlanShout:
                        KingShoutEvent?.Invoke(KingWire.ReadShout(p));
                        break;
                }
                break;
            case KingElection.PlanBoard:
                KingResultEvent?.Invoke(KingReply.Board, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingPoll(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case KingElection.PollList:
                KingCandidatesEvent?.Invoke(KingWire.ReadCandidates(p));
                break;
            case KingElection.PollVote:
                KingResultEvent?.Invoke(KingReply.Vote, KingWire.I16(p));
                break;
            case KingElection.PollKingChange:
                KingWire.ReadKingChange(p);
                break;
        }
    }

    private void HandleKingImpeachment(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case KingElection.ImpeachPropose:
                KingResultEvent?.Invoke(KingReply.Propose, KingWire.I16(p));
                break;
            case KingElection.ImpeachSenatorVote:
                KingResultEvent?.Invoke(KingReply.SenatorVote, KingWire.I16(p));
                break;
            case KingElection.ImpeachSenators:
                KingSenatorsEvent?.Invoke(KingWire.ReadSenators(p));
                break;
            case KingElection.ImpeachPublicVote:
                KingResultEvent?.Invoke(KingReply.PublicVote, KingWire.I16(p));
                break;
            case KingElection.ImpeachProposed:
                KingImpeachmentProposedEvent?.Invoke();
                break;
            case KingElection.ImpeachSenatorBallot:
                KingResultEvent?.Invoke(KingReply.SenatorBallot, KingWire.I16(p));
                break;
            case KingElection.ImpeachPublicBallot:
                KingResultEvent?.Invoke(KingReply.PublicBallot, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingTax(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case KingElection.TaxOpen:
                KingTreasuryEvent?.Invoke(KingWire.ReadTreasury(p));
                break;
            case KingElection.TaxFund:
                KingFundEvent?.Invoke(KingWire.ReadCoins(p));
                break;
            case KingElection.TaxRateRead:
                KingTariffReadEvent?.Invoke(KingWire.ReadTariff(p));
                break;
            case KingElection.TaxRateSet:
                var tariff = KingWire.ReadTariff(p);
                if (tariff.Result == KingElection.Success) SetZoneTariff(tariff.Tariff);
                KingTariffSetEvent?.Invoke(tariff);
                break;
            case KingElection.TaxReserve:
                KingReserveEvent?.Invoke(KingWire.ReadCoins(p));
                break;
            case KingElection.TaxKingItem:
                KingResultEvent?.Invoke(KingReply.KingItem, KingWire.I16(p));
                break;
            case KingElection.TaxChannelOnly:
                KingResultEvent?.Invoke(KingReply.ChannelOnly, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingIntro(Packet p)
    {
        switch (KingWire.U8(p))
        {
            case KingElection.IntroRead:
                KingIntroEvent?.Invoke(KingWire.Str16(p));
                break;
            case KingElection.IntroWrite:
                KingIntroSavedEvent?.Invoke(KingWire.U8(p) == KingElection.IntroSaved);
                break;
        }
    }

    public void SetZoneTariff(int tariff)
    {
        var zone = CurrentZoneAbility;
        zone.Tariff = tariff;
        CurrentZoneAbility = zone;
    }

    public void SendKingSchedule() => _conn.Send(KingWire.Request(KingElection.Election, KingElection.Schedule));

    public void SendKingNominate(string name) =>
        _conn.Send(KingWire.Named(name, KingElection.Election, KingElection.Nominate));

    public void SendKingPlan(string plan) => _conn.Send(KingWire.Plan(plan));

    public void SendKingPlanList() =>
        _conn.Send(KingWire.Request(KingElection.Election, KingElection.Plan, KingElection.PlanRead, KingElection.PlanList));

    public void SendKingPlanShoutAgain() =>
        _conn.Send(KingWire.Request(KingElection.Election, KingElection.Plan, KingElection.PlanRead, KingElection.PlanShout));

    public void SendKingPlanRead(string candidate) =>
        _conn.Send(KingWire.Named(candidate, KingElection.Election, KingElection.Plan, KingElection.PlanRead, KingElection.PlanText));

    public void SendKingCandidates() =>
        _conn.Send(KingWire.Request(KingElection.Election, KingElection.Poll, KingElection.PollList));

    public void SendKingVote(string candidate) =>
        _conn.Send(KingWire.Named(candidate, KingElection.Election, KingElection.Poll, KingElection.PollVote));

    public void SendKingWithdraw() => _conn.Send(KingWire.Request(KingElection.Election, KingElection.Withdraw));

    public void SendKingImpeachmentPropose() =>
        _conn.Send(KingWire.Request(KingElection.Impeachment, KingElection.ImpeachPropose));

    public void SendKingSenatorVote(bool inFavour) =>
        _conn.Send(KingWire.Request(KingElection.Impeachment, KingElection.ImpeachSenatorVote, KingWire.Ballot(inFavour)));

    public void SendKingSenators() => _conn.Send(KingWire.Request(KingElection.Impeachment, KingElection.ImpeachSenators));

    public void SendKingPublicVote(bool inFavour) =>
        _conn.Send(KingWire.Request(KingElection.Impeachment, KingElection.ImpeachPublicVote, KingWire.Ballot(inFavour)));

    public void SendKingSenatorBallot() =>
        _conn.Send(KingWire.Request(KingElection.Impeachment, KingElection.ImpeachSenatorBallot));

    public void SendKingPublicBallot() =>
        _conn.Send(KingWire.Request(KingElection.Impeachment, KingElection.ImpeachPublicBallot));

    public void SendKingFund() => _conn.Send(KingWire.Request(KingElection.Tax, KingElection.TaxFund));

    public void SendKingTariffRead() => _conn.Send(KingWire.Request(KingElection.Tax, KingElection.TaxRateRead));

    public void SendKingTariff(int tariff) =>
        _conn.Send(KingWire.Request(KingElection.Tax, KingElection.TaxRateSet, (byte)tariff));

    public void SendKingItem() => _conn.Send(KingWire.Request(KingElection.Tax, KingElection.TaxKingItem));

    public void SendKingIntroRead() => _conn.Send(KingWire.Request(KingElection.NationIntro, KingElection.IntroRead));

    public void SendKingIntro(string text) => _conn.Send(KingWire.Intro(text));
}
