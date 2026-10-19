namespace LibreKO.Network.Readers;

internal sealed class KingReader : IPacketReader
{
    private readonly KingSession king;
    private readonly ProgressSession progress;
    private readonly ZoneAbilitySession zoneAbility;

    public KingReader(KingSession king, ProgressSession progress, ZoneAbilitySession zoneAbility)
    {
        this.king = king;
        this.progress = progress;
        this.zoneAbility = zoneAbility;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_KING, HandleKing);
    }

    private void HandleKing(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((KingSubOpcode)p.ReadByte())
        {
            case KingSubOpcode.Election:
                HandleKingElection(p);
                break;
            case KingSubOpcode.Impeachment:
                HandleKingImpeachment(p);
                break;
            case KingSubOpcode.Tax:
                HandleKingTax(p);
                break;
            case KingSubOpcode.ElectionOfficer:
                king.RaiseKingElectionOpen(KingWire.Str8(p));
                break;
            case KingSubOpcode.NationIntro:
                HandleKingIntro(p);
                break;
            case KingSubOpcode.TreasuryNotice:
                king.RaiseKingTreasuryNotice(KingWire.ReadTreasuryNotice(p));
                break;
            case KingSubOpcode.KingEvent:
                HandleKingEvent(p);
                break;
        }
    }

    private void HandleKingEvent(Packet p)
    {
        var kingEvent = (KingEventSubOpcode)KingWire.U8(p);
        if (kingEvent != KingEventSubOpcode.RoyalOrder)
        {
            king.RaiseKingEventResult(kingEvent, KingWire.I16(p));
            return;
        }
        byte kind = KingWire.U8(p);
        short result = KingWire.I16(p);
        if (result < 0) king.RaiseKingEventResult(kingEvent, result);
        else king.RaiseRoyalOrder(kind, KingWire.Str8(p));
    }

    private void HandleKingElection(Packet p)
    {
        switch ((KingElectionSubOpcode)KingWire.U8(p))
        {
            case KingElectionSubOpcode.Schedule:
                king.RaiseKingSchedule(KingWire.ReadSchedule(p));
                break;
            case KingElectionSubOpcode.Nominate:
                king.RaiseKingResult(KingReply.Nominate, KingWire.I16(p));
                break;
            case KingElectionSubOpcode.Plan:
                HandleKingPlan(p);
                break;
            case KingElectionSubOpcode.Poll:
                HandleKingPoll(p);
                break;
            case KingElectionSubOpcode.Withdraw:
                king.RaiseKingResult(KingReply.Withdraw, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingPlan(Packet p)
    {
        switch ((KingPlanSubOpcode)KingWire.U8(p))
        {
            case KingPlanSubOpcode.Write:
                king.RaiseKingResult(KingReply.PlanPosted, KingWire.I16(p));
                break;
            case KingPlanSubOpcode.Read:
                switch ((KingPlanBoardSubOpcode)KingWire.U8(p))
                {
                    case KingPlanBoardSubOpcode.Text:
                        king.RaiseKingPlan(KingWire.ReadPlan(p));
                        break;
                    case KingPlanBoardSubOpcode.Shout:
                        king.RaiseKingShout(KingWire.ReadShout(p));
                        break;
                }
                break;
            case KingPlanSubOpcode.Board:
                king.RaiseKingResult(KingReply.Board, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingPoll(Packet p)
    {
        switch ((KingPollSubOpcode)KingWire.U8(p))
        {
            case KingPollSubOpcode.List:
                king.RaiseKingCandidates(KingWire.ReadCandidates(p));
                break;
            case KingPollSubOpcode.Vote:
                king.RaiseKingResult(KingReply.Vote, KingWire.I16(p));
                break;
            case KingPollSubOpcode.KingChange:
                KingWire.ReadKingChange(p);
                break;
        }
    }

    private void HandleKingImpeachment(Packet p)
    {
        switch ((KingImpeachmentSubOpcode)KingWire.U8(p))
        {
            case KingImpeachmentSubOpcode.Propose:
                king.RaiseKingResult(KingReply.Propose, KingWire.I16(p));
                break;
            case KingImpeachmentSubOpcode.SenatorVote:
                king.RaiseKingResult(KingReply.SenatorVote, KingWire.I16(p));
                break;
            case KingImpeachmentSubOpcode.Senators:
                king.RaiseKingSenators(KingWire.ReadSenators(p));
                break;
            case KingImpeachmentSubOpcode.PublicVote:
                king.RaiseKingResult(KingReply.PublicVote, KingWire.I16(p));
                break;
            case KingImpeachmentSubOpcode.Proposed:
                king.RaiseKingImpeachmentProposed();
                break;
            case KingImpeachmentSubOpcode.SenatorBallot:
                king.RaiseKingResult(KingReply.SenatorBallot, KingWire.I16(p));
                break;
            case KingImpeachmentSubOpcode.PublicBallot:
                king.RaiseKingResult(KingReply.PublicBallot, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingTax(Packet p)
    {
        switch ((KingTaxSubOpcode)KingWire.U8(p))
        {
            case KingTaxSubOpcode.Open:
                king.RaiseKingTreasury(KingWire.ReadTreasury(p));
                break;
            case KingTaxSubOpcode.Fund:
                var fund = KingWire.ReadCoins(p);
                if (fund.Result == KingWire.Success) progress.ApplyGold((int)fund.Coins);
                king.RaiseKingFund(fund);
                break;
            case KingTaxSubOpcode.RateRead:
                king.RaiseKingTariffRead(KingWire.ReadTariff(p));
                break;
            case KingTaxSubOpcode.RateSet:
                var tariff = KingWire.ReadTariff(p);
                if (tariff.Result == KingWire.Success) zoneAbility.SetTariff(tariff.Tariff);
                king.RaiseKingTariffSet(tariff);
                break;
            case KingTaxSubOpcode.Reserve:
                var reserve = KingWire.ReadCoins(p);
                if (reserve.Result == KingWire.Success) progress.ApplyGold((int)reserve.Coins);
                king.RaiseKingReserve(reserve);
                break;
            case KingTaxSubOpcode.KingItem:
                king.RaiseKingResult(KingReply.KingItem, KingWire.I16(p));
                break;
            case KingTaxSubOpcode.ChannelOnly:
                king.RaiseKingResult(KingReply.ChannelOnly, KingWire.I16(p));
                break;
        }
    }

    private void HandleKingIntro(Packet p)
    {
        switch ((KingIntroSubOpcode)KingWire.U8(p))
        {
            case KingIntroSubOpcode.Read:
                king.RaiseKingIntro(KingWire.Str16(p));
                break;
            case KingIntroSubOpcode.Write:
                king.RaiseKingIntroSaved(KingWire.U8(p) == KingWire.IntroSaved);
                break;
        }
    }
}
