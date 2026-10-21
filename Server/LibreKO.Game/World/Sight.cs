namespace LibreKO.Game.World;

public static class Sight
{
    public static bool Sees(UserSession viewer, UserSession target)
        => viewer.CharacterId == target.CharacterId
        || viewer.IsGM
        || (RegionManager.IsInView(viewer, target)
            && (!target.IsInvisible || target.Nation == viewer.Nation));
}
