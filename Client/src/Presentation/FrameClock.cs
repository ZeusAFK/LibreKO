using Godot;

namespace LibreKO.Presentation;

internal static class FrameClock
{
    public static double Now() => Time.GetTicksMsec() / 1000.0;
}
