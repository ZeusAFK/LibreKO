namespace LibreKO.Common.Enums;

public enum NpcMoveType : byte
{
    None = 0,
    Wander = 1,
    PatrolLoop = 2,
    PatrolOnce = 3,
    Stationary = 4,
    ScriptedPath = 5,
}
