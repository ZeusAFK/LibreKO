namespace LibreKO.Presentation;

internal static class BuffRules
{
    internal static bool IsPowerBuff(SkillData.Skill s) =>
        BuffWindow.IsPower(s.UseItem, ItemData.Get(s.UseItem)?.Kind ?? 0);

    private const byte SpeedBuffType = 6;
    internal const byte Speed2BuffType = 40;

    internal static bool IsSpeedBuffType(int buffType) => buffType is SpeedBuffType or Speed2BuffType;

    internal static bool IsSpeedBuff(SkillData.Skill s) =>
        s.MoveSpeedPercent > 100 || s.BuffType == SpeedBuffType || s.BuffType == Speed2BuffType;
}
