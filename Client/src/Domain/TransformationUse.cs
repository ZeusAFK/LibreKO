namespace LibreKO.Domain;

public static class TransformationUse
{
    public const int Siege = 0;
    public const int Monster = 1;
    public const int Npc = 3;
    public const int Costume = 4;
    public const int GuardTower = 5;
    public const int MovingTower = 6;
    public const int NpcEvent = 7;

    public static bool WearsAccessories(int use) => use is Npc or NpcEvent;
}
