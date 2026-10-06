namespace LibreKO.Game.Protocol;

internal static class KingPacketConstants
{
    public const byte Election = 1;
    public const byte Impeachment = 2;
    public const byte Tax = 3;
    public const byte Event = 4;
    public const byte ElectionOfficer = 5;
    public const byte NationIntro = 6;

    public const byte ElectionSchedule = 1;
    public const byte ElectionNominate = 2;
    public const byte ElectionNoticeBoard = 3;
    public const byte ElectionPoll = 4;
    public const byte ElectionResign = 5;

    public const byte ElectionTypeNoTerm = 0;
    public const byte ElectionTypeNomination = 1;
    public const byte ElectionTypeElection = 3;

    public const byte CandidacyBoardWrite = 1;
    public const byte CandidacyBoardRead = 2;

    public const byte BoardReadCandidateList = 1;
    public const byte BoardReadPlan = 2;

    public const byte PollCandidateList = 1;
    public const byte PollCastVote = 2;

    public const byte ScheduleNone = 0;
    public const byte ScheduleKingElection = 1;
    public const byte ScheduleImpeachmentVote = 3;
    public const byte ScheduleNoImpeachment = 0;
    public const byte ScheduleSenatorVote = 2;

    public const byte EventNoah = 1;
    public const byte EventExp = 2;
    public const byte EventPrize = 3;
    public const byte EventWeather = 5;
    public const byte EventNotice = 6;

    public const byte ElectionListSenator = 3;
    public const byte ElectionListCandidate = 4;

    public const byte ImpeachmentRequest = 1;
    public const byte ImpeachmentRequestElect = 2;
    public const byte ImpeachmentList = 3;
    public const byte ImpeachmentElect = 4;
    public const byte ImpeachmentRequestUiOpen = 8;
    public const byte ImpeachmentElectionUiOpen = 9;

    public const byte ImpeachmentTypeNone = 0;
    public const byte ImpeachmentTypeRequest = 1;
    public const byte ImpeachmentTypeElection = 3;

    public const byte TaxTreasury = 1;
    public const byte TaxCollect = 2;
    public const byte TaxTariffView = 3;
    public const byte TaxTariffChange = 4;
    public const byte TaxSceptor = 7;

    public const short TreasuryKing = 1;
    public const short TreasuryCitizen = 2;

    public const byte NationIntroRead = 1;
    public const byte NationIntroWrite = 2;

    public const byte NationIntroSaved = 1;
    public const byte NationIntroRefused = 0;

    public const short NominateUnknownId = -1;
    public const short NominateClosed = -2;
    public const short NominateNoAuthority = -3;
    public const short NominateOtherNation = -4;
    public const short NominateAlreadyNominated = -5;
    public const short NominateFailed = -6;
    public const short NominateListFull = -7;

    public const short PlanWriteClosed = -1;
    public const short PlanWriteTooLong = -2;
    public const short PlanWriteNotANominee = -3;

    public const short PlanReadNotANominee = -1;
    public const short PlanReadEmpty = -2;

    public const short PollClosed = -1;
    public const short PollNotANominee = -2;
    public const short PollAlreadyVoted = -3;
    public const short PollLevelTooLow = -4;

    public const short ResignClosed = -2;
    public const short ResignNotANominee = -3;

    public const short ProposeNotSenator = -1;
    public const short ProposeNotEnoughCoins = -2;
    public const short ProposeNoKing = -3;
    public const short ProposeInProgress = -4;
    public const short ProposeTooCloseToElection = -5;

    public const short SenatorVoteNotSenator = -1;
    public const short SenatorVoteClosed = -2;

    public const short SupportersClosed = -1;

    public const short PublicVoteClosed = -1;
    public const short PublicVoteLevelTooLow = -2;

    public const short ImpeachmentVoteClosed = -1;

    public const short NotKing = -1;
    public const short SceptorAlreadyOwned = -1;
    public const short SceptorNoFreeSlot = -2;
}
