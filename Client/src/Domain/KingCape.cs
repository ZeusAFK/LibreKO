namespace LibreKO.Domain;

public static class KingCape
{
    public const int Karus = 97;
    public const int ElMorad = 98;

    public static bool Is(int capeId) => capeId is Karus or ElMorad;

    public static int AfterClanChange(int worn, int clanCape) => Is(worn) ? worn : clanCape;
}
