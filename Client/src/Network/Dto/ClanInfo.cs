using System.Collections.Generic;

namespace LibreKO.Network;

public struct ClanBrowseEntry
{
    public int Id;
    public string Name;
}

public struct ClanMember
{
    public string Name;
    public byte Fame;
    public byte Level;
    public int Class;
    public bool IsOnline;
    public string Memo;
    public int HoursSinceLogin;
}

public struct MyClanInfo
{
    public bool InClan;
    public int ClanId;
    public string Name;
    public byte Flag;
    public byte Fame;
    public byte Grade;
    public byte Ranking;
    public int AllianceId;
    public int MarkVersion;
    public int PointFund;
    public byte PointMethod;
    public string Notice;
    public int Online;
    public int MaxMembers;

    public bool IsChief => InClan && Fame == ClanRanks.Chief;
    public bool IsViceChief => InClan && Fame == ClanRanks.ViceChief;
    public bool CanInvite => IsChief || IsViceChief;
}

public struct AllianceOfficer
{
    public byte Fame;
    public string Name;
}

public struct AllianceClanEntry
{
    public int Id;
    public string Name;
    public bool InAlliance;
    public List<AllianceOfficer> Officers;
}

public struct ClanStanding
{
    public int ClanId;
    public byte Grade;
    public byte Ranking;
}

public static class ClanRanks
{
    public const byte Chief = 1;
    public const byte ViceChief = 2;
    public const byte Officer = 3;
    public const byte Trainee = 5;
    public const byte CommandCaptain = 100;

    public static string Name(byte fame) => fame switch
    {
        Chief => "Chief",
        ViceChief => "Vice-chief",
        Officer => "Officer",
        _ => "Member",
    };
}

public static class ClanTypes
{
    public const byte Training = 1;
    public const byte Promoted = 2;
    public const byte Accredited5 = 3;
    public const byte Accredited1 = 7;
    public const byte Royal5 = 8;
    public const byte Royal1 = 12;

    public const int Grades = 5;
    public const int MaxMembers = 50;
    public const int CreationLevel = 20;
    public const int CreationCoins = 10_000_000;
    public const int NationalPointsPerClanPoint = 36;
    public const byte AutomaticAccumulation = 0;
    public const byte FreeAccumulation = 1;

    public static bool AcceptsDonations(byte flag) => flag >= Accredited5;

    public static string Name(byte flag) => flag switch
    {
        Training => "Clan",
        Promoted => "Training Knights",
        >= Accredited5 and <= Accredited1 => "Accredited Knights",
        >= Royal5 and <= Royal1 => "Royal Knights",
        _ => "Clan",
    };

    public static string Standing(byte flag, byte grade) => flag switch
    {
        >= Accredited5 and <= Accredited1 => $"Accredited Knights, grade {Accredited1 - flag + 1}",
        >= Royal5 and <= Royal1 => $"Royal Knights, grade {Royal1 - flag + 1}",
        _ => $"{Name(flag)}, grade {(grade is >= 1 and <= Grades ? grade : Grades)}",
    };

    public static int Rank(byte flag) => flag is >= Training and <= Royal1 ? flag : Training;

    public static bool MeetsCapeRank(byte flag, byte grade, int capeRanking, int capeGrade) =>
        Rank(flag) >= capeRanking
        && (flag > Promoted || capeGrade == 0 || grade <= capeGrade);
}
