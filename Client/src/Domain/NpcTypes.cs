namespace LibreKO.Domain;

public static class NpcTypes
{
    public static class Kind
    {
        public const int Monster = 1;
        public const int Npc = 2;
    }

    public static class ObjectType
    {
        public const int Npc = 0;
        public const int MapObject = 1;
    }

    public const int Monster = 0;

    public const int Boss = 3;
    public const int Guard = 11;
    public const int WarGuard = 14;
    public const int Merchant = 21;
    public const int Tinker = 22;
    public const int Anvil = 24;
    public const int Mark = 25;
    public const int Warehouse = 31;
    public const int Captain = 35;
    public const int Gate = 50;
    public const int Lever = 55;

    public const int Scarecrow = 171;
    public const int GuardSummon = 255;

    public const int FixedPose = 178;

    public static bool IsGuard(int npcType) => npcType >= Guard && npcType <= WarGuard;
}
