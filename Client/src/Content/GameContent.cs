using Godot;

namespace LibreKO.Content;

public partial class GameContent : Node
{
    public override void _EnterTree() => ContentLoaders.Install();

    public override void _Ready() => NpcNames.EnsureLoaded();
}
