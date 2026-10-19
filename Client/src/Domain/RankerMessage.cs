namespace LibreKO.Domain;

public static class RankerMessage
{
    public const short Done = 1;
    public const int MessageMaxLength = 200;
    public const float Reach = 9.5f;
    public const double WriteCooldownSeconds = 30.0;

    public const int TitleText = 44922;
    public const int MenuTipText = 44915;
    public const int RedressText = 44920;
    public const int WriteText = 44921;
    public const int NotYoursText = 44919;
    public const int CooldownText = 44914;
    public const int SavedTitleText = 44909;

    public static bool Karus(int npcType) => npcType < RankerStatue.ElMoradFirstType;

    public static int ViewTextId(short result) => result switch
    {
        -1 => 44906,
        -2 => 44907,
        _ => 0,
    };

    public static int WriteTextId(short result) => result switch
    {
        Done => 44908,
        -1 => 44910,
        -2 => 44911,
        -3 => 44912,
        -4 => 44913,
        _ => 0,
    };

    public static int RedressTextId(short result) => result switch
    {
        Done => 44916,
        -1 => 44910,
        -2 => 44912,
        -3 => 44907,
        -4 or -5 => 44913,
        -6 => 44917,
        _ => 0,
    };
}
