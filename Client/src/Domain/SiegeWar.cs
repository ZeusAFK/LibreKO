using LibreKO.Network;

namespace LibreKO.Domain;

public static class SiegeWar
{
    public const int Delos = 30;

    public const int WarClockText = 30100;
    public const int HoldClockText = 30101;
    public const int OwnerText = 30102;
    public const int SiegeMemberText = 19505;
    public const int DefenseMemberText = 19506;
    public const int SiegeClanText = 19507;
    public const int DefenseClanText = 19508;
    public const int RefusedInWarText = 19515;

    private const int SecondsPerHour = 3600;
    private const int SecondsPerMinute = 60;
    private const short BaseNotNow = -1;
    private const short BaseNotSiegeZone = -2;
    private const short BaseNotHere = -3;
    private const string ScrambleGlyphs = "★○＠☜▒";
    private const string ScrambleTails = "$^&*+";
    private const string ScrambleSingle = "!";

    public static bool At(int zone, SiegeWarStatus status) => zone == Delos && status.Running;

    public static bool OwnerSide(int clan, int alliance, SiegeWarStatus status) =>
        status.OwnerClan > 0
        && (clan == status.OwnerClan || (status.OwnerAlliance > 0 && clan == status.OwnerAlliance)
            || (alliance > 0 && (alliance == status.OwnerClan || alliance == status.OwnerAlliance)));

    public static bool Hostile(int myClan, int myAlliance, int targetClan, int targetAlliance, SiegeWarStatus status)
    {
        int owner = status.OwnerClan;
        if (owner <= 0) return false;
        bool mine = myClan == owner || myAlliance == owner;
        bool theirs = targetClan == owner || targetAlliance == owner;
        return mine != theirs;
    }

    public static int TargetMaskText(bool selfIsGm, int myClan, int myAlliance, int clan, int alliance, SiegeWarStatus status)
    {
        if (selfIsGm || (myClan > 0 && clan == myClan) || (myAlliance > 0 && alliance == myAlliance)) return 0;
        return status.OwnerClan > 0 && (clan == status.OwnerClan || alliance == status.OwnerClan)
            ? DefenseMemberText
            : SiegeMemberText;
    }

    public static string Fallback(int textId) => textId switch
    {
        DefenseMemberText => "Defense Member",
        SiegeClanText => "Siege Clan",
        DefenseClanText => "Defense Clan",
        RefusedInWarText => "You cannot use this function while in Siege War.",
        _ => "Siege Member",
    };

    public static string Scramble(string text, System.Func<int, int> pick)
    {
        if (text.Length <= 1) return text.Length == 0 ? text : ScrambleSingle;
        var scrambled = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i += 2)
            scrambled.Append(i + 1 < text.Length
                ? ScrambleGlyphs[pick(ScrambleGlyphs.Length)]
                : ScrambleTails[pick(ScrambleTails.Length)]);
        return scrambled.ToString();
    }

    public static string Clock(string template, int seconds)
    {
        int left = System.Math.Max(0, seconds);
        return template
            .Replace("%2d", ((left % SecondsPerHour) / SecondsPerMinute).ToString().PadLeft(2))
            .Replace("%d", (left % SecondsPerMinute).ToString());
    }

    public static (int Id, string Fallback)? BaseRefusal(short result) => result switch
    {
        BaseNotNow => (10219, "You cannot set up your base at the current moment."),
        BaseNotSiegeZone => (10220, "This is not a Castle Siege War zone."),
        BaseNotHere => (10221, "You cannot set up your base here."),
        _ => null,
    };

    public static (int Id, string Fallback)? PointsText(SiegePointsKind kind) => kind switch
    {
        SiegePointsKind.Obtained => (44650, "Siege Point [%d] Obtained, [%d] Accumulated (It will be credited to the clan when you log out)"),
        SiegePointsKind.CreditedAtEnd => (44651, "Siege Point [%d] Obtained, [%d] Accumulated (It will be credited to the clan when the siege ends)"),
        SiegePointsKind.Renewed => (44653, "Siege points have been renewed for clans. Accumulated Points [%d]"),
        _ => null,
    };
}
