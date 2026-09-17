namespace LibreKO;

public static class ZoneCatalog
{
    public readonly record struct Zone(int Id, string Stem, string Name);

    public static readonly Zone[] All =
    {
        new(1, "karus2004", "Karus"),
        new(2, "elmo2004", "El Morad"),
        new(5, "karus2004", "Karus 2"),
        new(7, "elmo2004", "El Morad 2"),
        new(11, "eslantzone", "Eslant"),
        new(12, "eslantzone", "Eslant II"),
        new(13, "eslantzone", "Eslant III"),
        new(15, "eslantzone", "Eslant IV"),
        new(21, "moradon", "Moradon"),
        new(22, "moradon", "Moradon 2"),
        new(30, "war_a", "Delos"),
        new(31, "dungeon_a", "Bi-Frost"),
        new(32, "dungeon_b1th", "Desperation Abyss"),
        new(33, "dungeon_b2th", "Hell Abyss"),
        new(34, "dragon_a", "Felankor Lair"),
        new(48, "arena", "Battle Arena"),
        new(51, "clanfight_b", "Orc Prisoner Quest"),
        new(52, "clanfight_b", "Blood Don Quest"),
        new(53, "clanfight_b", "Goblin Quest"),
        new(54, "clanfight_b", "Cape Quest"),
        new(55, "clanfight_b", "Forgotten Temple"),
        new(61, "battlezone", "Napies Gorge"),
        new(62, "battlezone_b", "Alseids Prairie"),
        new(63, "battlezone_d", "Nieds Triangle"),
        new(64, "battlezone_e", "Nereids Island"),
        new(66, "new_runawar", "Oreads"),
        new(69, "battlezone_b", "Snow War"),
        new(71, "freezone_b", "Ronark Land"),
        new(72, "freezone_a", "Ardream"),
        new(73, "freezone_c", "Ronark Land Base"),
        new(75, "itemzone_a", "Krowaz Domion"),
        new(77, "dragon_a", "Ardream Clan War"),
        new(79, "freezone_b", "Nation War"),
        new(81, "in_dungeon01", "Monster Suppression 1"),
        new(82, "in_dungeon02", "Monster Suppression 2"),
        new(83, "in_dungeon03", "Monster Suppression 3"),
        new(84, "in_dungeon05", "Border War Defence"),
        new(85, "sky_war_2009", "Chaos Dungeon"),
        new(86, "bossmode", "Under The Castle"),
        new(87, "in_dungeon05", "Juraid Mountain"),
        new(89, "in_dungeon04", "Border War Defence 2"),
        new(92, "clanfight_b", "Prison"),
        new(93, "dungeon_b2th", "Isillion Lair"),
        new(94, "dragon_a", "Felankor Lair 2"),
        new(96, "in_dungeon06", "Party Battle 1"),
        new(97, "in_dungeon06", "Party Battle 2"),
        new(98, "in_dungeon06", "Party Battle 3"),
        new(99, "in_dungeon06", "Party Battle 4"),
        new(105, "freezone_a", "Zinan War"),
        new(109, "freezone_a", "Dark Ardream"),
    };

    public static string? Stem(int zoneId)
    {
        foreach (var z in All)
            if (z.Id == zoneId) return z.Stem;
        return null;
    }
}
