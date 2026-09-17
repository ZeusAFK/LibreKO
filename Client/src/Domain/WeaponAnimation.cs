namespace LibreKO.Domain;

public static class WeaponAnimation
{
    public const int NoItem = -1;

    public const int Dagger = 11;
    public const int Sword = 21;
    public const int Sword2H = 22;
    public const int Axe = 31;
    public const int Axe2H = 32;
    public const int Mace = 41;
    public const int Mace2H = 42;
    public const int Spear = 51;
    public const int Polearm = 52;
    public const int Shield = 60;
    public const int Bow = 70;
    public const int Crossbow = 71;
    public const int LongBow = 80;
    public const int Launcher = 100;
    public const int Staff = 110;
    public const int Jamadar = 140;
    public const int MaceAlt = 181;

    public const int None = -1;

    public static bool IsOneHandBlade(int kind) => kind is Dagger or Sword or Axe;

    private const int TwoBluntClassFirst = 61;
    private const int TwoBluntClassLast = 63;

    private const int BreathBareHands = 86;
    private const int BreathBash = 98;
    private const int AttackStaff = 39;
    private const int AttackAxe1A = 72;
    private const int AttackTwoblunt0 = 63;

    private const float WeightPerUnit = 10f;
    private const float StanceWeightBlade = 5f;
    private const float StanceWeightMace = 8f;

    public static bool IsRangedLoadout(int rightKind, int leftKind) =>
        leftKind is Bow or LongBow || rightKind == Crossbow;

    private static readonly System.Collections.Generic.HashSet<int> WeaponKinds = new()
    {
        Dagger, 12, Sword, Sword2H, Axe, Axe2H, Mace, Mace2H, 43, Spear, Polearm,
        Bow, Crossbow, LongBow, Staff, 130, Jamadar, MaceAlt,
    };

    public static bool IsWeapon(int kind) => WeaponKinds.Contains(kind);

    public static int Group(int kind) => kind <= 0 ? 0 : kind / 10;

    public const int GroupNeedsNoWeapon = 9;

    public enum GearCheck { Ok, NoWeapon, WrongWeapon }

    public static GearCheck CheckGear(int itemGroup, int rightKind, int leftKind)
    {
        if (itemGroup == GroupNeedsNoWeapon) return GearCheck.Ok;
        if (!IsWeapon(rightKind) && !IsWeapon(leftKind)) return GearCheck.NoWeapon;
        if (itemGroup != 0 && itemGroup != Group(rightKind) && itemGroup != Group(leftKind))
            return GearCheck.WrongWeapon;
        return GearCheck.Ok;
    }

    public static bool IsLauncher(int rightKind) => rightKind == Launcher;

    public static int BreathBase(int rightKind, int leftKind, int rightWeight)
    {
        float w = rightWeight / WeightPerUnit;

        if (rightKind is Sword or Axe or Mace or MaceAlt && leftKind is Sword or Mace or Axe or MaceAlt)
            return StanceWeightBlade > w ? 44 : 47;

        switch (rightKind)
        {
            case NoItem:
                if (leftKind == Bow) return 92;
                return leftKind >= Shield ? BreathBash : BreathBareHands;
            case Dagger: return 38;
            case Sword: return StanceWeightBlade > w ? 32 : 35;
            case Sword2H: return 50;
            case Axe: return StanceWeightBlade > w ? 68 : 71;
            case Axe2H:
            case Mace2H:
            case 61:
            case 62:
            case 63: return 62;
            case Mace:
            case MaceAlt: return StanceWeightMace > w ? 56 : 59;
            case Spear: return 74;
            case Polearm: return 80;
            case Bow: return 92;
            case Crossbow: return leftKind == NoItem ? 93 : 0;
            case Launcher: return 94;
            case Staff: return 0;
            case Jamadar: return 148;
            default: return BreathBareHands;
        }
    }

    public static int AttackAnim(int rightKind, int leftKind, int rightWeight, System.Func<int, int> rand)
    {
        if (IsRangedLoadout(rightKind, leftKind)) return None;
        if (rightKind == Staff) return AttackStaff;
        if (rightKind >= TwoBluntClassFirst && rightKind <= TwoBluntClassLast)
            return rand(4) == 0 ? AttackAxe1A : AttackTwoblunt0 + rand(2);

        int b = BreathBase(rightKind, leftKind, rightWeight);
        return b == 0 ? None : b + 1 + rand(2);
    }
}
