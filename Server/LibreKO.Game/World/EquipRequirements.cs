using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public enum EquipRefusal
{
    None,
    Race,
    Class,
    LevelTooLow,
    LevelTooHigh,
    Stats,
}

public static class EquipRequirements
{
    private const byte RaceOpenAbove = 100;
    private const byte RaceOpenRangeFirst = 69;
    private const byte RaceOpenRangeLast = 79;
    private const byte RaceOpenRangeGap = 72;

    private static readonly HashSet<byte> OpenRaces = [0, 20, 30, 50];

    private static readonly HashSet<byte> WarriorForbiddenKinds = [70, 71, 140, 181, 200, 220, 230, 240];
    private static readonly HashSet<byte> RogueForbiddenKinds = [181, 200, 210, 230, 240];
    private static readonly HashSet<byte> MageForbiddenKinds = [70, 71, 140, 181, 200, 210, 220, 240];
    private static readonly HashSet<byte> PriestForbiddenKinds = [70, 71, 140, 200, 210, 220, 230];
    private static readonly HashSet<byte> KurianForbiddenKinds = [11, 12, 43, 60, 70, 71, 80, 110, 120, 130, 140, 181, 220, 230, 240];

    public static EquipRefusal Check(UserSession session, ItemData item)
    {
        if (!RaceAllows(item.Race, session.Race))
            return EquipRefusal.Race;
        if (!ClassIdHelper.CanWear(session.Class, item.Class) || ForbidsKind(session.Class, item.Kind))
            return EquipRefusal.Class;
        if (session.Level < item.ReqLevel)
            return EquipRefusal.LevelTooLow;
        if (item.ReqLevelMax > 0 && session.Level > item.ReqLevelMax)
            return EquipRefusal.LevelTooHigh;
        if (session.GetStat(StatType.Strength) < item.ReqStr
            || session.GetStat(StatType.Stamina) < item.ReqSta
            || session.GetStat(StatType.Dexterity) < item.ReqDex
            || session.GetStat(StatType.Intelligence) < item.ReqIntel
            || session.GetStat(StatType.Magic) < item.ReqCha)
            return EquipRefusal.Stats;

        return EquipRefusal.None;
    }

    public static bool RaceAllows(byte itemRace, byte playerRace) =>
        OpenRaces.Contains(itemRace)
        || (itemRace is >= RaceOpenRangeFirst and <= RaceOpenRangeLast && itemRace != RaceOpenRangeGap)
        || itemRace > RaceOpenAbove
        || itemRace == playerRace;

    public static bool ForbidsKind(short classId, byte kind)
    {
        var forbidden = ClassIdHelper.IsWarrior(classId) ? WarriorForbiddenKinds
            : ClassIdHelper.IsRogue(classId) ? RogueForbiddenKinds
            : ClassIdHelper.IsMage(classId) ? MageForbiddenKinds
            : ClassIdHelper.IsPriest(classId) ? PriestForbiddenKinds
            : ClassIdHelper.IsPortuKurian(classId) ? KurianForbiddenKinds
            : null;
        return forbidden?.Contains(kind) == true;
    }
}
