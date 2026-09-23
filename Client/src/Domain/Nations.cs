namespace LibreKO.Domain;

public static class Nations
{
    public const int Unknown = -1;
    public const int NotSelected = 0;
    public const int Karus = 1;
    public const int ElMorad = 2;

    public const int Neutral = 3;

    public const int KarusClassBase = 100;
    public const int ElMoradClassBase = 200;

    public static int ClassBase(int nation) => nation == Karus ? KarusClassBase : ElMoradClassBase;

    public static string Name(int nation) => nation == Karus ? "Karus" : "El Morad";
}
