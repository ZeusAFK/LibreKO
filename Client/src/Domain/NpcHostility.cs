namespace LibreKO.Domain;

public static class NpcHostility
{
    public static bool IsHostile(EntitySnapshot e, int myNation, bool npcsAreTargets) =>
        e.IsNpc
        && e.ObjectType != NpcTypes.ObjectType.MapObject
        && (e.NpcType == NpcTypes.GuardSummon
            ? IsEnemyNation(e, myNation, npcsAreTargets)
            : e.IsMonster || e.NpcType == NpcTypes.Scarecrow || IsEnemyNation(e, myNation, npcsAreTargets));

    private static bool IsEnemyNation(EntitySnapshot e, int myNation, bool npcsAreTargets) =>
        e.Nation is Nations.Karus or Nations.ElMorad && e.Nation != myNation && npcsAreTargets;
}
