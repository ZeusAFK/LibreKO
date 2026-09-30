using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class ClanRules
{
    public const short NoCape = -1;
    public const short CapeUnchosen = 0;

    public const byte CreationLevel = 20;
    public const int CreationCoins = 10_000_000;
    public const int MaxMembers = 50;
    public const int MaxViceChiefs = 3;

    public const int DonorKeepsNationalPoints = 1_000;

    public const int Grade1Points = 720_000;
    public const int Grade2Points = 360_000;
    public const int Grade3Points = 144_000;
    public const int Grade4Points = 72_000;
    public const byte LowestGrade = 5;
    public const byte HighestGrade = 1;

    public const byte RankedClans = 5;
    public const byte Unranked = 0;

    public const byte AutomaticAccumulation = 0;
    public const byte FreeAccumulation = 1;

    public const int MembersPerContributionStep = 5;
    public const int MaxAutoContribution = 10;

    public const byte FameChief = 1;
    public const byte FameViceChief = 2;
    public const byte FameOfficer = 3;
    public const byte FameTrainee = 5;

    public static short CapeForType(ClanType type, short current) => type switch
    {
        ClanType.Training => NoCape,
        ClanType.Promoted => CapeUnchosen,
        _ => current,
    };

    public static byte GradeFromPoints(int points) => points switch
    {
        >= Grade1Points => 1,
        >= Grade2Points => 2,
        >= Grade3Points => 3,
        >= Grade4Points => 4,
        _ => LowestGrade,
    };

    public static bool AcceptsDonations(ClanType type) => type >= ClanType.Accredited5;

    public static int AutoContribution(int memberCount) =>
        Math.Clamp((memberCount + MembersPerContributionStep - 1) / MembersPerContributionStep, 1, MaxAutoContribution);

    public static bool CanInvite(byte fame) => fame is FameChief or FameViceChief;

    public static bool CanAdmit(byte fame) => fame is >= FameChief and <= FameOfficer;

    public static bool MeetsCapeRank(byte flag, byte grade, byte capeRanking, byte capeGrade) =>
        flag >= capeRanking
        && (flag > (byte)ClanType.Promoted || capeGrade == 0 || grade <= capeGrade);
}
