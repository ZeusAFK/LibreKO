using Godot;

namespace LibreKO;

public partial class World : Node3D
{
    private void TouchHudInit()
    {
        ScaleVitals(_statusRoot);
        TouchControlsInit();
        BuildPotionBar();
        ExpBarInit();
    }

    private void TouchHudDispose()
    {
        ExpBarDispose();
        TouchControlsDispose();
        PotionBarDispose();
    }

    private CanvasLayer NewHudLayer()
    {
        var layer = new CanvasLayer { Layer = HudLayerIndex };
        AddChild(layer);
        return layer;
    }
}
