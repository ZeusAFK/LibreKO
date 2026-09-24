using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public static class StealthRules
{
    public const string InCombatRefusal = "You cannot use this while in combat.";

    public static bool IsInfiltrationPotion(IGameDataService gameData, int skillId)
    {
        var magic = gameData.GetMagic(skillId);
        return magic != null
            && magic.HasType(MagicSkillType.Stealth)
            && MagicTypeLookup.TryResolve(gameData.MagicType4Table, magic, skillId, out var type4Data)
            && (BuffType)type4Data.BuffType == BuffType.InvisibilityPotion;
    }

    public static InvisibilityType InvisibilityOf(IGameDataService gameData, int skillId, MagicStealthType stealthType) =>
        IsInfiltrationPotion(gameData, skillId) ? InvisibilityType.Infiltration : (InvisibilityType)stealthType;
}
