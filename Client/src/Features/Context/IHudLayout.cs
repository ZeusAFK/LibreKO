using Godot;

namespace LibreKO.Features;

internal interface IHudLayout
{
    void PluginSeam(CanvasLayer layer, HudPart part);

    Control? PluginPart(HudPart part);

    List<Rect2> DockRects();

    Control? StatusHud { get; }

    Vector2 VitalsSize { get; }

    TouchActionBar? TouchActions { get; }

    void RefreshVitalBars();

    void RefreshLevelOrb();
}
