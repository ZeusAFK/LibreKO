namespace LibreKO.Network.Requests;

public interface IKingRequests
{
    void Command(KingCommand command);
    void Schedule();
    void Nominate(string name);
    void Plan(string plan);
    void PlanList();
    void PlanShoutAgain();
    void PlanRead(string candidate);
    void Candidates();
    void Vote(string candidate);
    void Withdraw();
    void ImpeachmentPropose();
    void SenatorVote(bool inFavour);
    void Senators();
    void PublicVote(bool inFavour);
    void SenatorBallot();
    void PublicBallot();
    void Fund();
    void TariffRead();
    void Tariff(int tariff);
    void Item();
    void IntroRead();
    void Intro(string text);
}

public sealed class KingRequests : IKingRequests
{
    private readonly IPacketSink sink;

    public KingRequests(IPacketSink sink) => this.sink = sink;

    public void Command(KingCommand command)
    {
        var p = KingWire.Request(KingSubOpcode.KingEvent);
        switch (command.Kind)
        {
            case KingCommandKind.RoyalOrder:
                p.WriteByte((byte)KingEventSubOpcode.RoyalOrder);
                p.WriteString(command.Text);
                break;
            case KingCommandKind.Prize:
                p.WriteByte((byte)KingEventSubOpcode.Prize);
                p.WriteSByteString(command.Target);
                p.WriteInt(command.Amount);
                break;
            case KingCommandKind.ExperiencePoint:
                p.WriteByte((byte)KingEventSubOpcode.Experience);
                p.WriteByte((byte)Math.Clamp(command.Amount, 0, byte.MaxValue));
                break;
            case KingCommandKind.DropRate:
                p.WriteByte((byte)KingEventSubOpcode.DropRate);
                p.WriteByte((byte)Math.Clamp(command.Amount, 0, byte.MaxValue));
                break;
            case KingCommandKind.Weather:
                p.WriteByte((byte)KingEventSubOpcode.Weather);
                p.WriteByte(command.Weather);
                p.WriteByte((byte)Math.Clamp(command.Amount, 0, byte.MaxValue));
                break;
        }
        sink.Send(p);
    }

    public void Schedule() => sink.Send(KingWire.Election(KingElectionSubOpcode.Schedule));

    public void Nominate(string name) =>
            sink.Send(KingWire.Named(KingWire.Election(KingElectionSubOpcode.Nominate), name));

    public void Plan(string plan) => sink.Send(KingWire.Plan(plan));

    public void PlanList() =>
            sink.Send(KingWire.PlanBoard(KingPlanBoardSubOpcode.List));

    public void PlanShoutAgain() =>
            sink.Send(KingWire.PlanBoard(KingPlanBoardSubOpcode.Shout));

    public void PlanRead(string candidate) =>
            sink.Send(KingWire.Named(KingWire.PlanBoard(KingPlanBoardSubOpcode.Text), candidate));

    public void Candidates() =>
            sink.Send(KingWire.Poll(KingPollSubOpcode.List));

    public void Vote(string candidate) =>
            sink.Send(KingWire.Named(KingWire.Poll(KingPollSubOpcode.Vote), candidate));

    public void Withdraw() => sink.Send(KingWire.Election(KingElectionSubOpcode.Withdraw));

    public void ImpeachmentPropose() =>
            sink.Send(KingWire.Impeachment(KingImpeachmentSubOpcode.Propose));

    public void SenatorVote(bool inFavour) =>
            sink.Send(KingWire.WithBallot(KingWire.Impeachment(KingImpeachmentSubOpcode.SenatorVote), inFavour));

    public void Senators() => sink.Send(KingWire.Impeachment(KingImpeachmentSubOpcode.Senators));

    public void PublicVote(bool inFavour) =>
            sink.Send(KingWire.WithBallot(KingWire.Impeachment(KingImpeachmentSubOpcode.PublicVote), inFavour));

    public void SenatorBallot() =>
            sink.Send(KingWire.Impeachment(KingImpeachmentSubOpcode.SenatorBallot));

    public void PublicBallot() =>
            sink.Send(KingWire.Impeachment(KingImpeachmentSubOpcode.PublicBallot));

    public void Fund() => sink.Send(KingWire.Tax(KingTaxSubOpcode.Fund));

    public void TariffRead() => sink.Send(KingWire.Tax(KingTaxSubOpcode.RateRead));

    public void Tariff(int tariff)
    {
        var p = KingWire.Tax(KingTaxSubOpcode.RateSet);
        p.WriteByte((byte)tariff);
        sink.Send(p);
    }

    public void Item() => sink.Send(KingWire.Tax(KingTaxSubOpcode.KingItem));

    public void IntroRead() => sink.Send(KingWire.NationIntro(KingIntroSubOpcode.Read));

    public void Intro(string text) => sink.Send(KingWire.Intro(text));
}
