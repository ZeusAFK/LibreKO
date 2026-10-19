namespace LibreKO.Domain;

public static class DrakiTower
{
    public const int Chapters = 5;
    public const int CertificateItem = 810595000;
    public const int OffrankedText = 40089;
    public const string OffrankedFallback = "Offranked";
    public const int UseCertificateText = 40078;
    public const string UseCertificateFallback = "You have used all 3 daily entrance limits. Do you wish to use Certificate of Draki?";
    public const int NoCertificateText = 40079;
    public const string NoCertificateFallback = "You have used all 3 daily entrance limits. You can enter instance by purchsing Certificate of Draki from PUS.";
    public const string ClearLabel = "Clear";
    public const string GiveUpLabel = "Give Up";
    public const int NoCertificate = 0;

    private const int MaxRank = 100;
    private const int SecondsPerHour = 3600;
    private const int SecondsPerMinute = 60;

    public static readonly string[] ChapterLines =
    [
        "Sector 1 | The Preparation : Lv.10-29",
        "Sector 2 | The Beginning : Lv.30-49",
        "Sector 3 | The Result : Lv.50-69",
        "Sector 4 | The Failure Ones : Lv.70-83",
        "Sector 5 | The Ultimate Stage : Lv.83 Above",
    ];

    public static bool Ranked(int rank) => rank is >= 1 and <= MaxRank;

    public static bool SectorOpen(int sector, int maxStage) => sector >= 1 && sector <= Math.Min(maxStage, Chapters);

    public static string FinishTime(int seconds)
    {
        int withinHour = seconds % SecondsPerHour;
        return $"{withinHour / SecondsPerMinute:00}:{withinHour % SecondsPerMinute:00}";
    }

    public static string StageEndLabel(bool gaveUp) => gaveUp ? GiveUpLabel : ClearLabel;

    public static (int Id, string Fallback)? EnterText(DrakiEnterResult result) => result switch
    {
        DrakiEnterResult.ShowBoard => null,
        DrakiEnterResult.WrongZone => (40066, "Teleporting is disabled in this zone."),
        DrakiEnterResult.AlreadyInInstance => (40067, "You are in an instance dungeon."),
        DrakiEnterResult.FloorNotReached => (40068, "Clear info and entrance info does not match."),
        DrakiEnterResult.NoLimit => (40069, "You have either spent all 3 entrance limits or do not have entrance item."),
        DrakiEnterResult.RoomCreationFailed => (40070, "Instance generation failed"),
        DrakiEnterResult.InvalidItem => (40071, "You do not have the enterance item."),
        DrakiEnterResult.Dead => (40072, "You cannot enter while you are dead."),
        DrakiEnterResult.ServerIndexMismatch => (40073, "Server internal instance index info does not match."),
        DrakiEnterResult.PacketIndexMismatch => (40074, "Packet instance index Info does not match."),
        DrakiEnterResult.NoUserInfo => (40075, "No user information"),
        DrakiEnterResult.SilentRefusal => (40090, ""),
        DrakiEnterResult.LadderPoints => (40091, "You do not have enough ladder point (30 pts) to enter Draki's Tower."),
        DrakiEnterResult.TowerFull => (40076, "Draki's Tower is currently at maximum capacity."),
        _ => (40077, "You cannot enter right now."),
    };
}
