namespace LibreKO.Game.World;

public static class Sight
{
    // A player's HP and effects are shown only to someone who can see them: in view, and not stealthed from another nation.
    public static bool Sees(UserSession viewer, UserSession target)
        => viewer.CharacterId == target.CharacterId
        || viewer.IsGM
        || (RegionManager.IsInView(viewer, target)
            && (!target.IsInvisible || target.Nation == viewer.Nation));
}
