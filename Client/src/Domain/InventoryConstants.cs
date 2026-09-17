namespace LibreKO.Domain;

public static class InventoryConstants
{
    public const int RightEar = 0;
    public const int Head = 1;
    public const int LeftEar = 2;
    public const int Neck = 3;
    public const int Breast = 4;
    public const int Pet = 5;
    public const int RightHand = 6;
    public const int Waist = 7;
    public const int LeftHand = 8;
    public const int RightRing = 9;
    public const int Leg = 10;
    public const int LeftRing = 11;
    public const int Glove = 12;
    public const int Foot = 13;

    public const int SlotMax = 14;
    public const int HaveMax = 28;
    public const int CospreMax = 15;
    public const int BagSlotMax = 3;
    public const int MagicBagMax = 12;

    public const int InventoryStart = SlotMax;
    public const int CospreStart = SlotMax + HaveMax;
    public const int BagSlotStart = CospreStart + CospreMax;
    public const int MagicBagStart = BagSlotStart + BagSlotMax;
    public const int MagicBagTotal = BagSlotMax * MagicBagMax;
    public const int InventoryTotal = MagicBagStart + MagicBagTotal;

    public const int CosPosWing = 0;
    public const int CosPosHelmet = 1;
    public const int CosPosGloveRight = 2;
    public const int CosPosGloveLeft = 3;
    public const int CosPosPauldron = 4;
    public const int CosPosEmblem = 5;
    public const int CosPosFairy = 7;
    public const int CosPosTattoo = 8;
    public const int CosPosTalisman = 9;

    public const int CosWing = CospreStart + CosPosWing;
    public const int CosHelmet = CospreStart + CosPosHelmet;
    public const int CosGloveRight = CospreStart + CosPosGloveRight;
    public const int CosGloveLeft = CospreStart + CosPosGloveLeft;
    public const int CosPauldron = CospreStart + CosPosPauldron;
    public const int CosEmblem = CospreStart + CosPosEmblem;
    public const int CosFairy = CospreStart + CosPosFairy;
    public const int CosTattoo = CospreStart + CosPosTattoo;
    public const int CosTalisman = CospreStart + CosPosTalisman;

    public const int CospreWireMax = 9;

    public const int MyInfoWireTotal = SlotMax + HaveMax + CospreWireMax + BagSlotMax + MagicBagTotal;

    public static readonly int[] CospreWirePositions =
        { CosPosWing, CosPosHelmet, CosPosGloveRight, CosPosGloveLeft, CosPosPauldron,
          CosPosEmblem, CosPosFairy, CosPosTattoo, CosPosTalisman };

    public static int MyInfoWireSlot(int wireIndex)
    {
        if (wireIndex < CospreStart)
            return wireIndex;

        if (wireIndex < CospreStart + CospreWireMax)
            return CospreStart + CospreWirePositions[wireIndex - CospreStart];

        return wireIndex + (CospreMax - CospreWireMax);
    }

    public static readonly int[] CospreUiPositions =
        { CosPosWing, CosPosHelmet, CosPosGloveRight, CosPosGloveLeft, CosPosPauldron,
          CosPosEmblem, CosPosFairy, CosPosTattoo, CosPosTalisman };

    public static readonly int[] VisualSlots =
        { Breast, Leg, Head, Glove, Foot, Pet, RightHand, LeftHand,
          CosWing, CosHelmet, CosGloveRight, CosGloveLeft, CosPauldron,
          CosEmblem, CosFairy, CosTattoo, CosTalisman };

    public static readonly int[] CharacterListVisualSlots =
        { Head, Breast, Pet, RightHand, LeftHand, Leg, Glove, Foot,
          CosWing, CosHelmet, CosGloveRight, CosGloveLeft, CosPauldron,
          CosEmblem, CosFairy, CosTattoo, CosTalisman };

    public const int VisBreast = 0;
    public const int VisLeg = 1;
    public const int VisHead = 2;
    public const int VisGlove = 3;
    public const int VisFoot = 4;
    public const int VisPet = 5;
    public const int VisRightHand = 6;
    public const int VisLeftHand = 7;
    public const int VisCosWing = 8;
    public const int VisCosHelmet = 9;
    public const int VisCosGloveRight = 10;
    public const int VisCosGloveLeft = 11;
    public const int VisCosPauldron = 12;
    public const int VisCosEmblem = 13;
    public const int VisCosFairy = 14;
    public const int VisCosTattoo = 15;
    public const int VisCosTalisman = 16;

    public const int VisualSlotCount = 17;

    public static int BagSlotFor(int bagIndex) => BagSlotStart + bagIndex;

    public static int MagicBagPageStart(int bagIndex) => MagicBagStart + bagIndex * MagicBagMax;

    public static int BagIndexForMagicBagPosition(int magicBagPosition) => magicBagPosition / MagicBagMax;

    public static bool IsCospreSlot(int abs) => abs >= CospreStart && abs < BagSlotStart;

    public static bool IsBagItemSlot(int abs) => abs >= BagSlotStart && abs < MagicBagStart;

    public static bool IsMagicBagSlot(int abs) => abs >= MagicBagStart && abs < InventoryTotal;
}
