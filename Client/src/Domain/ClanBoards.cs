using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public readonly record struct ClanApplicant(int Level, int Class, string Name, string Message);

public readonly record struct ClanAdvert(int ClanId, string ClanName, int Grade, int Type, string Chief, int MarkVersion, string Text);

public readonly record struct WishPost(string Name, string Introduction, int Level, int Class, int Age, bool Online, int TargetId, string Registered);

public readonly record struct WishRequest(int ClanId, string Leader, string ClanName, int Grade, int Type, int MarkVersion, string Message);

public sealed record ClanBoardStatus(ClanBoardState State, IReadOnlyList<ClanApplicant> Applicants, int AppliedClanId, int AppliedMarkVersion, string AppliedClanName)
{
    public bool Staff => State == ClanBoardState.Staff;
    public bool Applied => AppliedClanName.Length > 0;
}

public sealed record WishStatus(WishBoardRole Role, string RequestedName, int Received)
{
    public bool Recruiter => Role == WishBoardRole.Recruiter;
    public bool Member => Role == WishBoardRole.Member;
    public bool Seeker => Role >= WishBoardRole.Seeker;
}

public static class ClanBoards
{
    public const int RowsPerPage = 8;
    public const int ApplicantsPerPage = 5;
    public const int MaxRequests = 9;
    public const int MaxAdvertBytes = 139;
    public const int MaxIntroBytes = 139;
    public const int MinAge = 1;
    public const int MaxAge = 99;
    public const int NoTarget = -1;
    public const int TextOverLimit = 16502;
    public const int TextAgeWrong = 40063;
    public const int TextAlreadyRegistered = 16503;

    private static readonly int[] ApplyTexts = { 16510, 16502, 16503, 16508, 16509, 16505, 16506 };
    private static readonly int[] CancelApplyTexts = { 16501, 16502, 16507, 16508, 0, 16505, 16506 };
    private static readonly int[] AdvertTexts = { 16510, 16502, 16503, 16504, 16505, 16506 };
    private static readonly int[] DeleteAdvertTexts = { 16501, 16502, 16503, 16504, 16505, 16506 };
    private static readonly Dictionary<int, int> PromoteTexts = new()
    {
        [0] = 18631, [2] = 18632, [3] = 18634, [4] = 18633, [5] = 18635, [6] = 18636, [7] = 18637,
    };
    private static readonly int[] IntroTexts = { 40055, 16510, 16503, 40056 };
    private static readonly int[] DeleteIntroTexts = { 40057, 16501, 0, 40056 };
    private static readonly Dictionary<int, int> RequestTexts = new() { [0] = 40058, [1] = 40059, [2] = 40060, [4] = 40061 };
    private static readonly Dictionary<int, int> CancelRequestTexts = new() { [0] = 40057, [1] = 16501, [2] = 40057 };
    private static readonly Dictionary<int, int> WishPromoteTexts = new()
    {
        [0] = 18631, [3] = 16509, [4] = 40064, [6] = 18636, [7] = 18637,
    };

    public const int TextAdvertPromoted = 18630;
    public const int TextSeekingClan = 40062;
    public const int TextRefreshTooSoon = 40065;
    public const int TextRequestSentTo = 40051;
    public const int TextNoRequestSent = 40052;
    public const int TextMembersCannotRequest = 40050;
    public const int TextRequestsReceived = 40053;
    public const int TextNoRequests = 40054;
    public const int TextApplicantMessage = 16511;

    public static int ApplyResultText(ClanBoardAction kind, int code) => Pick(kind == ClanBoardAction.Delete ? CancelApplyTexts : ApplyTexts, code);

    public static int AdvertResultText(ClanBoardAction kind, int code) => Pick(kind == ClanBoardAction.Delete ? DeleteAdvertTexts : AdvertTexts, code);

    public static int PromoteText(int code) => PromoteTexts.GetValueOrDefault(code);

    public static int IntroResultText(ClanBoardAction action, int code) => Pick(action == ClanBoardAction.Delete ? DeleteIntroTexts : IntroTexts, code);

    public static int RequestResultText(ClanBoardAction action, int code) =>
        (action == ClanBoardAction.Delete ? CancelRequestTexts : RequestTexts).GetValueOrDefault(code);

    public static int WishPromoteText(int code) => WishPromoteTexts.GetValueOrDefault(code);

    public static int ClassLineText(int type) => type switch
    {
        0 or 1 => 15724,
        2 => 15700,
        >= 3 and <= 7 => 15701,
        _ => 15702,
    };

    public static bool AgeValid(string text) =>
        text.Length is > 0 and <= 2 && text.All(char.IsAsciiDigit) && int.Parse(text) is >= MinAge and <= MaxAge;

    public static bool CanPageForward(int rowsOnPage, int perPage) => rowsOnPage >= perPage;

    private static int Pick(int[] table, int code) => code >= 0 && code < table.Length ? table[code] : 0;
}
