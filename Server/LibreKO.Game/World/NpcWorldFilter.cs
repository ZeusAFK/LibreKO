namespace LibreKO.Game.World;

internal static class NpcWorldFilter
{
    public static bool ShouldSpawnNormally(NpcInstance npc) =>
        !npc.UsesNpcSpawnStyle
        || npc.IsScarecrow
        || npc.IsNationOwned
        || NpcInstance.FollowsAPath(npc.MoveType);
}
