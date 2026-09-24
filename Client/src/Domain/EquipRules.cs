using System.Collections.Generic;

namespace LibreKO.Domain;

public enum EquipRefusal
{
    None,
    Race,
    Class,
    LevelTooLow,
    LevelTooHigh,
    Strength,
    Stamina,
    Dexterity,
    Intelligence,
    Charisma,
}

public readonly record struct EquipStats(int Class, int Race, int Level, int Str, int Sta, int Dex, int Intel, int Cha);

public static class EquipRules
{
    private const int ClassesPerNation = 100;
    private const int ItemClassAnyone = 0;
    private const int ItemClassMasters = 21;
    private const int ItemClassUnrestricted = 255;
    private const int RaceOpenAbove = 100;
    private const int RaceOpenRangeFirst = 69;
    private const int RaceOpenRangeLast = 79;
    private const int RaceOpenRangeGap = 72;

    private static readonly HashSet<int> OpenRaces = new() { 0, 20, 30, 50 };

    private static readonly HashSet<int> WarriorForbiddenKinds = new() { 70, 71, 140, 181, 200, 220, 230, 240 };
    private static readonly HashSet<int> RogueForbiddenKinds = new() { 181, 200, 210, 230, 240 };
    private static readonly HashSet<int> MageForbiddenKinds = new() { 70, 71, 140, 181, 200, 210, 220, 240 };
    private static readonly HashSet<int> PriestForbiddenKinds = new() { 70, 71, 140, 200, 210, 220, 230 };
    private static readonly HashSet<int> KurianForbiddenKinds = new() { 11, 12, 43, 60, 70, 71, 80, 110, 120, 130, 140, 181, 220, 230, 240 };

    private const int TextFirstItemClass = 1303;
    private const int TextMastersOnly = 1316;
    private const int TextKurianNovice = 1427;
    private const int TextKurianSkilled = 1428;
    private const int TextKurianMaster = 1429;
    private const int TextAllClasses = 1430;
    private const int TextGameMaster = 7807;
    private const int ItemClassGameMaster = 25;
    private const int LastJobItemClass = 12;

    public static int ClassNameTextId(int itemClass) => itemClass switch
    {
        >= 1 and <= LastJobItemClass => TextFirstItemClass + itemClass,
        13 => TextKurianNovice,
        14 => TextKurianSkilled,
        15 => TextKurianMaster,
        ItemClassMasters => TextMastersOnly,
        ItemClassGameMaster => TextGameMaster,
        ItemClassUnrestricted => TextAllClasses,
        _ => 0,
    };

    private static int Job(int cls) => cls % ClassesPerNation;
    private static bool IsWarrior(int cls) => Job(cls) is 1 or 5 or 6;
    private static bool IsRogue(int cls) => Job(cls) is 2 or 7 or 8;
    private static bool IsMage(int cls) => Job(cls) is 3 or 9 or 10;
    private static bool IsPriest(int cls) => Job(cls) is 4 or 11 or 12;
    private static bool IsKurian(int cls) => Job(cls) is 13 or 14 or 15;
    private static bool IsMaster(int cls) => Job(cls) is 6 or 8 or 10 or 12 or 15;

    public static bool ClassAllows(int playerClass, int itemClass) => itemClass switch
    {
        ItemClassAnyone or ItemClassUnrestricted => true,
        1 => IsWarrior(playerClass) || IsKurian(playerClass),
        2 => IsRogue(playerClass),
        3 => IsMage(playerClass),
        4 => IsPriest(playerClass),
        5 => Job(playerClass) is 5 or 14,
        6 => Job(playerClass) is 6 or 15,
        13 => IsKurian(playerClass),
        ItemClassMasters => IsMaster(playerClass),
        _ => Job(playerClass) == itemClass,
    };

    public static bool ForbidsKind(int playerClass, int kind)
    {
        var forbidden = IsWarrior(playerClass) ? WarriorForbiddenKinds
            : IsRogue(playerClass) ? RogueForbiddenKinds
            : IsMage(playerClass) ? MageForbiddenKinds
            : IsPriest(playerClass) ? PriestForbiddenKinds
            : IsKurian(playerClass) ? KurianForbiddenKinds
            : null;
        return forbidden != null && forbidden.Contains(kind);
    }

    public static bool RaceAllows(int itemRace, int playerRace) =>
        OpenRaces.Contains(itemRace)
        || (itemRace >= RaceOpenRangeFirst && itemRace <= RaceOpenRangeLast && itemRace != RaceOpenRangeGap)
        || itemRace > RaceOpenAbove
        || itemRace == playerRace;

    public static EquipRefusal Check(EquipStats who, ItemData.Item item)
    {
        if (!RaceAllows(item.Race, who.Race)) return EquipRefusal.Race;
        if (!ClassAllows(who.Class, item.Class) || ForbidsKind(who.Class, item.Kind)) return EquipRefusal.Class;
        if (who.Level < item.ReqLevel) return EquipRefusal.LevelTooLow;
        if (item.ReqLevelMax > 0 && who.Level > item.ReqLevelMax) return EquipRefusal.LevelTooHigh;
        if (who.Str < item.ReqStr) return EquipRefusal.Strength;
        if (who.Sta < item.ReqSta) return EquipRefusal.Stamina;
        if (who.Dex < item.ReqDex) return EquipRefusal.Dexterity;
        if (who.Intel < item.ReqInt) return EquipRefusal.Intelligence;
        if (who.Cha < item.ReqCha) return EquipRefusal.Charisma;
        return EquipRefusal.None;
    }
}
