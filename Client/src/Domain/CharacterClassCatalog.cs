namespace LibreKO.Domain;

public static class CharacterClassCatalog
{
    public const int TierUnknown = 0, TierBeginner = 1, TierNovice = 2, TierMaster = 3;

    public static int Family(int classCode)
    {
        int local = classCode % 100;
        return local switch
        {
            1 or 5 or 6 => 1,
            2 or 7 or 8 => 2,
            3 or 9 or 10 => 3,
            4 or 11 or 12 => 4,
            13 or 14 or 15 => 5,
            _ => 0,
        };
    }

    public static string DisplayName(int classCode) => Family(classCode) switch
    {
        1 => Localization.Loc.Tr("Warrior"),
        2 => Localization.Loc.Tr("Rogue"),
        3 => Localization.Loc.Tr("Mage"),
        4 => Localization.Loc.Tr("Priest"),
        5 => Localization.Loc.Tr("Kurian"),
        _ => $"{Localization.Loc.Tr("Class")} {classCode}",
    };

    public static int Tier(int classCode) => (classCode % 100) switch
    {
        1 or 2 or 3 or 4 or 13 => TierBeginner,
        5 or 7 or 9 or 11 or 14 => TierNovice,
        6 or 8 or 10 or 12 or 15 => TierMaster,
        _ => TierUnknown,
    };

    public static string TierName(int classCode) => Tier(classCode) switch
    {
        TierBeginner => Localization.Loc.Tr("Beginner"),
        TierNovice => Localization.Loc.Tr("Novice"),
        TierMaster => Localization.Loc.Tr("Master"),
        _ => Localization.Loc.Tr("Unknown"),
    };

    public static string SpecializationName(int classCode) => classCode switch
    {
        101 => Localization.Loc.Tr("Warrior"),   105 => Localization.Loc.Tr("Berserker"), 106 => Localization.Loc.Tr("Guardian"),
        102 => Localization.Loc.Tr("Rogue"),     107 => Localization.Loc.Tr("Hunter"),    108 => Localization.Loc.Tr("Penetrator"),
        103 => Localization.Loc.Tr("Wizard"),    109 => Localization.Loc.Tr("Sorcerer"),  110 => Localization.Loc.Tr("Necromancer"),
        104 => Localization.Loc.Tr("Priest"),    111 => Localization.Loc.Tr("Shaman"),    112 => Localization.Loc.Tr("Dark Priest"),
        201 => Localization.Loc.Tr("Warrior"),   205 => Localization.Loc.Tr("Blade"),     206 => Localization.Loc.Tr("Protector"),
        202 => Localization.Loc.Tr("Rogue"),     207 => Localization.Loc.Tr("Ranger"),    208 => Localization.Loc.Tr("Assassin"),
        203 => Localization.Loc.Tr("Wizard"),    209 => Localization.Loc.Tr("Mage"),      210 => Localization.Loc.Tr("Enchanter"),
        204 => Localization.Loc.Tr("Priest"),    211 => Localization.Loc.Tr("Cleric"),    212 => Localization.Loc.Tr("Druid"),
        _ => DisplayName(classCode),
    };
}
