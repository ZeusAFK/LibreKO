namespace LibreKO.Domain;

public static class HotbarAssign
{
    public const int NoSecondType = 255;
    public const int Shouting = 130013;
    public const int Assault = 130014;
    public const int ElmoradShouting = 230013;
    public const int ElmoradAssault = 230014;

    public static bool CanDrag(int type1, int type2) => type1 switch
    {
        MagicType.None => type2 == NoSecondType,
        MagicType.Special => type2 != NoSecondType,
        _ => true,
    };

    public static bool CanAdd(int skillId, int type1) =>
        type1 != MagicType.None || skillId is Shouting or Assault or ElmoradShouting or ElmoradAssault;

    public static bool Holds(int skillId, int type1, int type2) => CanAdd(skillId, type1) || CanDrag(type1, type2);

    public static bool Holds(int skillId, int type1, int type2, int classCode) =>
        Holds(skillId, type1, type2) && !SkillData.IsOtherClassSkill(skillId, classCode);

    public static bool ClearRefused(int[] slots, System.Func<int, bool> keeps)
    {
        bool cleared = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == 0 || keeps(slots[i])) continue;
            slots[i] = 0;
            cleared = true;
        }
        return cleared;
    }
}
