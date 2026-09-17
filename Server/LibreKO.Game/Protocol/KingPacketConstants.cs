namespace LibreKO.Game.Protocol;

internal static class KingPacketConstants
{
    public const byte Election = 1;
    public const byte Impeachment = 2;
    public const byte Tax = 3;
    public const byte Event = 4;
    public const byte Npc = 5;
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

    public const byte EventNoah = 1;
    public const byte EventExp = 2;
    public const byte EventPrize = 3;
    public const byte EventWeather = 5;
    public const byte EventNotice = 6;

    public const byte ElectionListCandidate = 4;

    public const byte ImpeachmentRequest = 1;
    public const byte ImpeachmentRequestElect = 2;
    public const byte ImpeachmentList = 3;
    public const byte ImpeachmentElect = 4;
    public const byte ImpeachmentRequestUiOpen = 8;
    public const byte ImpeachmentElectionUiOpen = 9;

    public const byte ImpeachmentTypeRequest = 1;
    public const byte ImpeachmentTypeElection = 3;
}
