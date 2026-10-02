namespace LibreKO.Domain;

public static class RankerStatue
{
    public const int KarusFirstType = 82;
    public const int ElMoradFirstType = 85;
    public const int Places = 3;
    public const float Scale = 2f;
    public const float PedestalLift = 2.8f;
    public const int NoPiece = 0;

    private const int SetSpan = 1_000_000;
    private const int PieceSpan = 1000;
    private const int UpperPiece = 1;
    private const int LowerPiece = 2;
    private const int HelmetPiece = 3;
    private const int GlovePiece = 4;
    private const int BootPiece = 5;
    private const int WarriorSet = 507;
    private const int RogueSet = 557;
    private const int MageSet = 567;
    private const int PriestSet = 597;
    private const int NoSet = 0;

    private static readonly int[] WarriorClasses = { 101, 105, 106, 201, 205, 206 };
    private static readonly int[] RogueClasses = { 2, 102, 107, 108, 202, 207, 208 };
    private static readonly int[] MageClasses = { 3, 103, 109, 110, 203, 209, 210 };
    private static readonly int[] PriestClasses = { 4, 104, 111, 112, 204, 211, 212 };
    private static readonly int[] KurianClasses = { 113, 114, 115, 213, 214, 215 };

    public static bool Is(int npcType) => npcType >= KarusFirstType && npcType < ElMoradFirstType + Places;

    public static int[] Dress(int cls, int head, int breast, int leg, int glove, int foot, int right, int left)
    {
        var gear = new int[InventoryConstants.VisualSlotCount];
        if (System.Array.IndexOf(KurianClasses, cls) < 0)
        {
            int set = SetOf(cls);
            gear[InventoryConstants.VisHead] = Worn(set, HelmetPiece, head);
            gear[InventoryConstants.VisBreast] = Worn(set, UpperPiece, breast);
            gear[InventoryConstants.VisLeg] = Worn(set, LowerPiece, leg);
            gear[InventoryConstants.VisGlove] = Worn(set, GlovePiece, glove);
            gear[InventoryConstants.VisFoot] = Worn(set, BootPiece, foot);
        }
        gear[InventoryConstants.VisRightHand] = right;
        gear[InventoryConstants.VisLeftHand] = left;
        return gear;
    }

    private static int Worn(int set, int piece, int item) =>
        item != NoPiece ? item : set == NoSet ? NoPiece : set * SetSpan + piece * PieceSpan;

    private static int SetOf(int cls) =>
        System.Array.IndexOf(WarriorClasses, cls) >= 0 ? WarriorSet
        : System.Array.IndexOf(RogueClasses, cls) >= 0 ? RogueSet
        : System.Array.IndexOf(MageClasses, cls) >= 0 ? MageSet
        : System.Array.IndexOf(PriestClasses, cls) >= 0 ? PriestSet
        : NoSet;
}
