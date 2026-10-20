namespace LibreKO.Domain;

public static class SlaveMerchant
{
    public const int OneDayCertificate = 751730000;
    public const int ThirtyDayCertificate = 891063000;
    public const int OneDayHours = 24;
    public const int ThirtyDayHours = 720;

    public static int HoursOf(int itemId) => itemId switch
    {
        OneDayCertificate => OneDayHours,
        ThirtyDayCertificate => ThirtyDayHours,
        _ => 0,
    };

    public static bool IsCertificate(int itemId) => HoursOf(itemId) > 0;

    public static bool HasNoTime(int hours) => hours == 0;
}
