using System;

namespace LibreKO.Domain;

[Flags]
public enum MoveKeys
{
    None = 0,
    Forward = 1,
    Backward = 2,
    TurnLeft = 4,
    TurnRight = 8,
    Walk = Forward | Backward,
}

public static class MoveInput
{
    public static bool AnyKeyPressed(MoveKeys previous, MoveKeys current) =>
        (current & ~previous) != MoveKeys.None;

    public static bool WalkKeyPressed(MoveKeys previous, MoveKeys current) =>
        (current & ~previous & MoveKeys.Walk) != MoveKeys.None;
}
