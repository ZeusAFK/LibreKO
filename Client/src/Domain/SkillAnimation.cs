using System.Collections.Generic;

namespace LibreKO.Domain;

public static class SkillAnimation
{
    public const int None = -1;

    public const int Struck0 = 4;
    public const int NpcStruck0 = 6;
    public const int StruckPoses = 3;
    public const int ShootQuarrelA = 28;
    public const int ShootQuarrelB = 29;
    public const int SkillDaggerB = 117;

    public const int ItemClassJamadar = WeaponAnimation.Jamadar;
    public const int ItemClassCrossbow = WeaponAnimation.Crossbow;

    public const int NoItem = WeaponAnimation.NoItem;

    private static readonly HashSet<int> OneHandRight = new() { 11, 12, 43, 21, 51, 31, 41, 181, 110, 140 };
    private static readonly HashSet<int> OneHandLeft = new() { 11, 12, 43, 21, 31, 41, 181, 52, 110, 140 };
    private static readonly HashSet<int> TwoHandRight = new() { 22, 32, 42, 52, 61, 62, 63 };

    private static readonly HashSet<int> MainAnimMelee = new()
    {
        105725, 105735, 105760, 105775, 106725, 106735, 106760, 106775,
        205725, 205735, 205760, 205775, 206725, 206735, 206760, 206775,
        114515, 114535, 114557, 115515, 115535, 115557,
        214515, 214535, 214557, 215515, 215535, 215557,
        491214, 491215, 491216, 491217, 492433, 492434,
    };

    private static readonly Dictionary<int, int> Forced = new()
    {
        [108575] = SkillDaggerB, [208575] = SkillDaggerB,
    };

    public static int WeaponBucket(int rightKind, int leftKind)
    {
        int right = TwoHandRight.Contains(rightKind) ? 2 : OneHandRight.Contains(rightKind) ? 1 : 0;
        bool left = OneHandLeft.Contains(leftKind);
        if (right == 2) return 2;
        if (right == 1) return left ? 1 : 0;
        return left ? 0 : None;
    }

    public static int ForWeapon(SkillData.Skill s, int rightKind, int leftKind) =>
        WeaponBucket(rightKind, leftKind) switch
        {
            0 or 1 => (rightKind == NoItem ? leftKind : rightKind) == ItemClassJamadar
                ? s.AnimJamadar
                : s.Anim1H,
            2 => s.Anim2H,
            _ => None,
        };

    public static int Resolve(SkillData.Skill s, int rightKind, int leftKind, bool release)
    {
        if (s.IsNonAction) return None;
        if (!s.IsMelee && !release && !s.HasCastPhase) return None;
        int anim;
        bool perWeapon = s.IsMelee && !MainAnimMelee.Contains(s.Id);
        if (perWeapon)
        {
            anim = ForWeapon(s, rightKind, leftKind);
            if (anim < 0) return None;
        }
        else
        {
            bool second = release && !s.IsMelee;
            anim = second ? s.SelfAnim2 : s.SelfAnim1;
            if (s.IsRanged && (rightKind == ItemClassCrossbow || leftKind == ItemClassCrossbow))
                anim = second ? ShootQuarrelB : ShootQuarrelA;
        }
        return Forced.TryGetValue(s.Id, out int forced) ? forced : anim;
    }
}
