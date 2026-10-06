using System.Collections.Generic;

namespace LibreKO.Domain;

public enum ElectionPage
{
    Main = 0,
    Schedule = 1,
    Election = 2,
    Board = 3,
    Impeachment = 4,
    Proposal = 5,
    Senators = 6,
}

public enum ElectionAction
{
    AskSchedule,
    ToElection,
    ToImpeachment,
    Back,
    ToMain,
    Nominate,
    TurnDown,
    OpenPlanEditor,
    AskCandidates,
    AskPlans,
    ToProposal,
    AskSenators,
    AskPublicBallot,
    Propose,
    AskSenatorBallot,
}

public enum KingReply
{
    Nominate,
    Withdraw,
    PlanPosted,
    PlanRead,
    Board,
    Candidates,
    Vote,
    Senators,
    Propose,
    SenatorVote,
    PublicVote,
    SenatorBallot,
    PublicBallot,
    Treasury,
    Fund,
    TariffRead,
    TariffSet,
    Reserve,
    KingItem,
    ChannelOnly,
}

public enum KingBox
{
    Nominate = 0,
    TurnDown = 2,
    Fund = 3,
    Propose = 5,
    SenatorBallot = 6,
    PublicBallot = 7,
}

public readonly record struct ElectionRow(int TextId, ElectionAction Action);

public sealed class ElectionPager
{
    public ElectionPage Current { get; private set; }
    public ElectionPage Previous { get; private set; }

    public void Go(ElectionPage page)
    {
        Previous = KingElection.PreviousOf(page, Current);
        Current = page;
    }

    public void Back() => Go(Previous);

    public void Reset()
    {
        Current = ElectionPage.Main;
        Previous = ElectionPage.Main;
    }
}

public static class KingElection
{
    public const byte Election = 1;
    public const byte Impeachment = 2;
    public const byte Tax = 3;
    public const byte KingEvent = 4;
    public const byte ElectionOfficer = 5;
    public const byte NationIntro = 6;
    public const byte TreasuryNotice = 7;

    public const byte Schedule = 1;
    public const byte Nominate = 2;
    public const byte Plan = 3;
    public const byte Poll = 4;
    public const byte Withdraw = 5;

    public const byte PlanWrite = 1;
    public const byte PlanRead = 2;
    public const byte PlanBoard = 5;
    public const byte PlanList = 1;
    public const byte PlanText = 2;
    public const byte PlanShout = 3;

    public const byte PollList = 1;
    public const byte PollVote = 2;
    public const byte PollKingChange = 4;

    public const byte ImpeachPropose = 1;
    public const byte ImpeachSenatorVote = 2;
    public const byte ImpeachSenators = 3;
    public const byte ImpeachPublicVote = 4;
    public const byte ImpeachProposed = 5;
    public const byte ImpeachSenatorBallot = 8;
    public const byte ImpeachPublicBallot = 9;
    public const byte InFavour = 1;
    public const byte Against = 2;

    public const byte TaxOpen = 1;
    public const byte TaxFund = 2;
    public const byte TaxRateRead = 3;
    public const byte TaxRateSet = 4;
    public const byte TaxReserve = 5;
    public const byte TaxKingItem = 7;
    public const byte TaxChannelOnly = 8;

    public const byte IntroRead = 1;
    public const byte IntroWrite = 2;
    public const byte IntroSaved = 1;
    public const byte TreasuryNoticeShown = 1;

    public const short Success = 1;
    public const short Refused = -1;
    public const short ShoutAskAgain = 2;

    public const byte ScheduleNone = 0;
    public const byte ScheduleElection = 1;
    public const byte ScheduleImpeachment = 2;
    public const byte ScheduleImpeachmentVote = 3;
    public const byte SenatorsVoting = 2;
    public const byte SenatorsVotingLate = 4;
    public const int NoElectionHour = 255;
    private const int MidnightHour = 24;
    private const int LastHour = 23;

    public const int MaxNameLength = 20;
    public const int PlanLimit = 500;
    public const int CandidateSlots = 10;
    public const int UnknownResult = -1;

    public const int NpcIntroText = 11308;
    public const int CurrentKingText = 11360;
    public const int NoKingText = 11361;
    public const int ElectionScheduleText = 11309;
    public const int ImpeachmentScheduleText = 11310;
    public const int ImpeachmentVoteScheduleText = 11398;
    public const int ElectionPageText = 11311;
    public const int BoardPageText = 11315;
    public const int ImpeachmentPageText = 11316;
    public const int ProposalPageText = 11317;
    public const int SenatorsPageText = 11318;
    public const int SenatorsVotingText = 11331;
    public const int NotVotingTimeText = 11322;

    public const int NoScheduleText = 11402;
    public const int NominateHoursText = 11314;
    public const int EnterIdText = 11362;
    public const int InvalidIdText = 11363;
    public const int ConfirmNominateText = 11325;
    public const int TurnDownText = 11377;
    public const int ProposeText = 11389;
    public const int SenatorBallotText = 11391;
    public const int PublicBallotText = 11395;
    public const int InFavourText = 11392;
    public const int AgainstText = 11393;
    public const int OkText = 11394;
    public const int PlanTooLongText = 7651;
    public const int ConfirmVoteText = 19301;

    private const int CheckElectionDayText = 11336;
    private const int ElectionForKingText = 11337;
    private const int ImpeachmentText = 11338;
    private const int ScheduleBackText = 11339;
    private const int NominateKingText = 11340;
    private const int TurnDownNominationText = 11341;
    private const int NomineeBoardText = 11342;
    private const int ElectionText = 11343;
    private const int ElectionBackText = 11344;
    private const int PostText = 11345;
    private const int ViewText = 11346;
    private const int BoardBackText = 11347;
    private const int ProposeImpeachmentText = 11348;
    private const int SenatorsListText = 11349;
    private const int ImpeachmentVoteText = 11350;
    private const int ImpeachmentBackText = 11351;
    private const int ProposeAgainText = 11352;
    private const int ImpeachmentResultText = 11353;
    private const int ProposalBackText = 11354;
    private const int SenatorsBackText = 11355;

    public const int NominatedText = 11367;
    public const int IdMissingText = 11364;
    public const int NotAcceptingText = 11384;
    public const int NoAuthorityText = 11321;
    public const int SameNationText = 11365;
    public const int AlreadyNominatedText = 11366;
    public const int RegisterFailedText = 19303;
    public const int CandidateRangeText = 19304;
    public const int NominateUnknownCode = -100;
    public const int PostedText = 11374;
    public const int TooLongText = 11375;
    public const int NotNomineeText = 11369;
    public const int NoPlanText = 11376;
    public const int VotedText = 11370;
    public const int AlreadyVotedText = 11372;
    public const int VoteLevelText = 19302;
    public const int VotePremiumText = 30200;
    public const int WithdrawnText = 11368;
    public const int ProposedText = 11387;
    public const int SenatorsOnlyText = 11329;
    public const int NotEnoughCoinsText = 11303;
    public const int NoKingNowText = 11388;
    public const int ElectionCloseText = 11401;
    public const int SenatorBallotClosedText = 11397;
    public const int PublicBallotClosedText = 11396;
    public const int ImpeachLevelText = 30540;
    public const int KingsOnlyText = 11300;
    public const int ItemIssuedText = 11411;
    public const int ItemOwnedText = 11410;
    public const int NoBagSlotText = 11409;
    public const int ChannelOnlyText = 10238;

    private static readonly ElectionRow[] MainRows =
    [
        new(CheckElectionDayText, ElectionAction.AskSchedule),
        new(ElectionForKingText, ElectionAction.ToElection),
        new(ImpeachmentText, ElectionAction.ToImpeachment),
    ];

    private static readonly ElectionRow[] ScheduleRows = [new(ScheduleBackText, ElectionAction.Back)];

    private static readonly ElectionRow[] ElectionRows =
    [
        new(NominateKingText, ElectionAction.Nominate),
        new(TurnDownNominationText, ElectionAction.TurnDown),
        new(NomineeBoardText, ElectionAction.OpenPlanEditor),
        new(ElectionText, ElectionAction.AskCandidates),
        new(ElectionBackText, ElectionAction.ToMain),
    ];

    private static readonly ElectionRow[] BoardRows =
    [
        new(PostText, ElectionAction.OpenPlanEditor),
        new(ViewText, ElectionAction.AskPlans),
        new(BoardBackText, ElectionAction.Back),
    ];

    private static readonly ElectionRow[] ImpeachmentRows =
    [
        new(ProposeImpeachmentText, ElectionAction.ToProposal),
        new(SenatorsListText, ElectionAction.AskSenators),
        new(ImpeachmentVoteText, ElectionAction.AskPublicBallot),
        new(ImpeachmentBackText, ElectionAction.ToMain),
    ];

    private static readonly ElectionRow[] ProposalRows =
    [
        new(ProposeAgainText, ElectionAction.Propose),
        new(ImpeachmentResultText, ElectionAction.AskSenatorBallot),
        new(ProposalBackText, ElectionAction.Back),
    ];

    private static readonly ElectionRow[] SenatorsRows = [new(SenatorsBackText, ElectionAction.Back)];

    public static IReadOnlyList<ElectionRow> Rows(ElectionPage page) => page switch
    {
        ElectionPage.Main => MainRows,
        ElectionPage.Schedule => ScheduleRows,
        ElectionPage.Election => ElectionRows,
        ElectionPage.Board => BoardRows,
        ElectionPage.Impeachment => ImpeachmentRows,
        ElectionPage.Proposal => ProposalRows,
        ElectionPage.Senators => SenatorsRows,
        _ => [],
    };

    public static int PageText(ElectionPage page) => page switch
    {
        ElectionPage.Main => NpcIntroText,
        ElectionPage.Election => ElectionPageText,
        ElectionPage.Board => BoardPageText,
        ElectionPage.Impeachment => ImpeachmentPageText,
        ElectionPage.Proposal => ProposalPageText,
        ElectionPage.Senators => SenatorsPageText,
        _ => 0,
    };

    public static ElectionPage PreviousOf(ElectionPage next, ElectionPage current) =>
        next is ElectionPage.Election or ElectionPage.Impeachment ? ElectionPage.Main : current;

    public static int KingLine(string king) => king.Length > 0 ? CurrentKingText : NoKingText;

    public static bool KnownScheduleKind(byte kind) =>
        kind is ScheduleElection or ScheduleImpeachment or ScheduleImpeachmentVote;

    public static int ScheduleText(int kind) => kind switch
    {
        ScheduleElection => ElectionScheduleText,
        ScheduleImpeachment => ImpeachmentScheduleText,
        ScheduleImpeachmentVote => ImpeachmentVoteScheduleText,
        _ => 0,
    };

    public static object[] ScheduleArgs(int month, int day, int hour, int minute) =>
        [month, day, hour, minute, hour == 0 ? MidnightHour : hour, minute];

    public static bool SenatorsAreVoting(int state) => state is SenatorsVoting or SenatorsVotingLate;

    public static int NominateWindowHour(int kind, int hour) => kind == ScheduleElection ? hour : NoElectionHour;

    public static (int TextId, int From, int Until) NominateHours(int hour) =>
        hour == NoElectionHour ? (NoScheduleText, 0, 0) : (NominateHoursText, hour, hour == 0 ? LastHour : hour - 1);

    public static int NomineeRefusal(string id) =>
        id.Length == 0 ? EnterIdText : id.Length > MaxNameLength ? InvalidIdText : 0;

    public static int PlanRefusal(string plan) => plan.Length > PlanLimit ? PlanTooLongText : 0;

    public static bool TwoChoices(KingBox box) => box is KingBox.SenatorBallot or KingBox.PublicBallot;

    public static string Senators(IEnumerable<string> names) => string.Join(", ", names);

    public static int ResultText(KingReply reply, short result) => reply switch
    {
        KingReply.Nominate => result switch
        {
            1 => NominatedText,
            -1 => IdMissingText,
            -2 => NotAcceptingText,
            -3 => NoAuthorityText,
            -4 => SameNationText,
            -5 => AlreadyNominatedText,
            -6 => RegisterFailedText,
            -7 => CandidateRangeText,
            NominateUnknownCode => UnknownResult,
            _ => 0,
        },
        KingReply.Withdraw => result switch
        {
            1 => WithdrawnText,
            -1 => IdMissingText,
            -2 => NotAcceptingText,
            -3 => NotNomineeText,
            _ => 0,
        },
        KingReply.PlanPosted => result switch
        {
            1 => PostedText,
            -1 => NotAcceptingText,
            -2 => TooLongText,
            -3 => NotNomineeText,
            -4 => CandidateRangeText,
            _ => 0,
        },
        KingReply.PlanRead => result switch
        {
            -1 => NotNomineeText,
            -2 => NoPlanText,
            _ => 0,
        },
        KingReply.Board or KingReply.Candidates or KingReply.Senators => result == Refused ? NotVotingTimeText : 0,
        KingReply.Vote => result switch
        {
            1 => VotedText,
            -1 => NotVotingTimeText,
            -2 => NotNomineeText,
            -3 => AlreadyVotedText,
            -4 => VoteLevelText,
            -5 => VotePremiumText,
            _ => UnknownResult,
        },
        KingReply.Propose => result switch
        {
            1 => ProposedText,
            -1 => SenatorsOnlyText,
            -2 => NotEnoughCoinsText,
            -3 => NoKingNowText,
            -4 => SenatorsVotingText,
            -5 => ElectionCloseText,
            _ => 0,
        },
        KingReply.SenatorVote => result switch
        {
            1 => VotedText,
            -1 => SenatorsOnlyText,
            -2 => SenatorBallotClosedText,
            -3 => AlreadyVotedText,
            _ => 0,
        },
        KingReply.PublicVote => result switch
        {
            1 => VotedText,
            -1 => PublicBallotClosedText,
            -2 => ImpeachLevelText,
            -3 => AlreadyVotedText,
            _ => 0,
        },
        KingReply.SenatorBallot => result == Refused ? SenatorBallotClosedText : 0,
        KingReply.PublicBallot => result == Refused ? PublicBallotClosedText : 0,
        KingReply.Treasury or KingReply.Fund or KingReply.TariffRead or KingReply.TariffSet or KingReply.Reserve =>
            result == Refused ? KingsOnlyText : 0,
        KingReply.KingItem => result switch
        {
            1 => ItemIssuedText,
            -1 => ItemOwnedText,
            -2 => NoBagSlotText,
            _ => 0,
        },
        KingReply.ChannelOnly => result == Success ? ChannelOnlyText : 0,
        _ => 0,
    };

    public static string UnknownResultLine(short result) => $"Unknown Result {result}";
}
