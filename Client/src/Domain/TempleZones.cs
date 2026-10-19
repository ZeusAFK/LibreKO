namespace LibreKO.Domain;

public static class TempleZones
{
    public const byte ForgottenTemple = 55;
    public const byte BorderDefenseWar = 84;
    public const byte ChaosDungeon = 85;
    public const byte UnderTheCastle = 86;
    public const byte JuraidMountain = 87;

    public static bool IsTempleEventZone(int zone) =>
        zone is ForgottenTemple or UnderTheCastle or JuraidMountain or BorderDefenseWar or ChaosDungeon;
}
