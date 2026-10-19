using Godot;

namespace LibreKO.Presentation;

public static class NodeTimers
{
    public static void After(this Node owner, double seconds, Action action) =>
        owner.GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(owner) && !owner.IsQueuedForDeletion()) action();
        };
}
