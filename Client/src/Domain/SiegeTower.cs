namespace LibreKO.Domain;

public static class SiegeTower
{
    public const int MovingTowerLook = 30070;
    public const int GuardTowerLook = 30210;
    public const int GuardTowerNpcType = 191;
    public const float BoardReach = 7f;
    public const double Cooldown = 10.0;
    public const int LeaveEffect = 30017;
    public const int WireScale = 10;

    public const int TooFarText = 16800;
    public const int NotOnboardText = 16807;
    public const int UnloadableText = 16806;
    public const int TownRefusedText = 16813;
    public const int BoardingText = 16814;
    public const int UseText = 16815;
    public const int ExitText = 16816;
    public const int CancelText = 16817;
    public const int CooldownText = 16820;

    private const byte BoardAlreadyOnboard = 2;
    private const byte BoardNoUser = 3;
    private const byte BoardNotTower = 4;
    private const byte BoardTooFar = 5;
    private const byte BoardFull = 6;
    private const byte LeaveNotOnboard = 2;
    private const byte LeaveTooFar = 3;
    private const byte LeaveUnloadable = 4;
    private const byte LeaveTooFarAgain = 5;
    private const byte UseInUse = 8;

    private static readonly int[] SiegeLooks = { 30010, 30040, 30050, 30060, 30061, MovingTowerLook, GuardTowerLook };

    public static bool InSiegeLook(int look) => System.Array.IndexOf(SiegeLooks, look) >= 0;

    public static (int Id, string Fallback) BoardRefusal(int result) => result switch
    {
        BoardAlreadyOnboard => (16802, "On boarding already"),
        BoardNoUser => (16803, "No available user"),
        BoardNotTower => (16804, "The user is not a tower"),
        BoardTooFar => (TooFarText, "It is too far"),
        BoardFull => (16805, "The tower is full"),
        _ => (16801, "Boarding failed"),
    };

    public static (int Id, string Fallback) LeaveRefusal(int result) => result switch
    {
        LeaveNotOnboard => (NotOnboardText, "Not onboard"),
        LeaveTooFar or LeaveTooFarAgain => (16808, "It is too far from the moving tower"),
        LeaveUnloadable => (UnloadableText, "Unloadable location"),
        _ => (16809, "Failed getting off"),
    };

    public static (int Id, string Fallback) EndedText(SiegeTowerResult result) =>
        result == SiegeTowerResult.Succeeded ? (16812, "Tower transformation is canceled") : (16811, "The tower is dismissed");

    public static (int Id, string Fallback) UseRefusal(int result) =>
        result == UseInUse ? (16821, "In use already") : (16818, "Not available now");

    public static (int Id, string Fallback) ExitRefusal => (16819, "Try again later");

    public static string Fallback(int id) => id switch
    {
        TooFarText => "It is too far",
        NotOnboardText => "Not onboard",
        UnloadableText => "Unloadable location",
        TownRefusedText => "You can't town",
        BoardingText => "Boarding",
        UseText => "Use",
        ExitText => "Exit",
        CancelText => "Cancel",
        CooldownText => "It is available in %d seconds",
        _ => "",
    };

    public static bool MayBoard(bool siegeZone, int myClan, int myAlliance, int towerClan, int towerAlliance, bool hostile) =>
        siegeZone
            ? (myClan > 0 && myClan == towerClan) || (myAlliance > 0 && myAlliance == towerAlliance)
            : !hostile;

    public static int CooldownSeconds(double left) => (int)(left + 1);

    public static ushort Wire(float position) => (ushort)System.Math.Clamp((int)(position * WireScale), 0, ushort.MaxValue);
}
