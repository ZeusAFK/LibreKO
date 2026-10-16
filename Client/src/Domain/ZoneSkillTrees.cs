namespace LibreKO.Domain;

public static class ZoneSkillTrees
{
    private const int DungeonDefenseZone = 89;
    private const int DungeonDefenseTree = 3500;
    private const int KnightRoyaleTree = 3100;
    private const int Zone45 = 45;
    private const int FirstZone45Skill = 490800;
    private const int LastZone45Skill = 490827;
    private const int FirstTree3200Zone = 57;
    private const int LastTree3200Zone = 60;
    private const int Tree3200 = 3200;

    public static bool Allowed(int zone, int tree, int skillId)
    {
        if (zone == ChaosBattle.Zone) return tree == ChaosBattle.SkillTree;
        if (zone == DungeonDefenseZone) return tree == DungeonDefenseTree;
        if (KnightRoyale.IsZone(zone)) return tree == KnightRoyaleTree;
        if (zone == Zone45) return skillId is >= FirstZone45Skill and <= LastZone45Skill;
        if (zone is >= FirstTree3200Zone and <= LastTree3200Zone) return tree == Tree3200;
        return true;
    }
}
