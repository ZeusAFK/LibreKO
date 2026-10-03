using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string EventPlatesMigration = "event_plates";

    private readonly List<Control> _plateOrder = new();
    private readonly Dictionary<Control, string> _plateLayoutIds = new();
    private bool _platesQueued;

    private void EventPlatesInit()
    {
        Config.ForgetWindowPositionsOnce(EventPlatesMigration, new[] { BattleBannerLayoutId, BattleBoardLayoutId });
        GetViewport().SizeChanged += QueueEventPlates;
        _targetBox.Resized += QueueEventPlates;
    }

    private void EventPlatesDispose()
    {
        if (IsInsideTree()) GetViewport().SizeChanged -= QueueEventPlates;
    }

    private void AddEventPlate(Control plate, HudLayout? layout = null, string? layoutId = null)
    {
        plate.SetAnchorsPreset(Control.LayoutPreset.TopLeft, true);
        plate.GrowHorizontal = Control.GrowDirection.End;
        plate.GrowVertical = Control.GrowDirection.End;
        plate.VisibilityChanged += () =>
        {
            _plateOrder.Remove(plate);
            if (plate.Visible) _plateOrder.Add(plate);
            QueueEventPlates();
        };
        plate.Resized += QueueEventPlates;
        if (layout != null && layoutId != null)
        {
            _plateLayoutIds[plate] = layoutId;
            layout.Placed += QueueEventPlates;
        }
        if (plate.Visible) _plateOrder.Add(plate);
    }

    private void QueueEventPlates()
    {
        if (_platesQueued) return;
        _platesQueued = true;
        Callable.From(RunEventPlates).CallDeferred();
    }

    private void RunEventPlates()
    {
        _platesQueued = false;
        if (!IsInsideTree()) return;
        foreach (var (plate, spot) in EventPlateSpots())
            plate.Position = spot;
    }

    private Vector2 EventPlateSpot(Control plate)
    {
        foreach (var (stacked, spot) in EventPlateSpots())
            if (stacked == plate) return spot;
        return plate.Position;
    }

    private List<(Control Plate, Vector2 Spot)> EventPlateSpots()
    {
        var stacked = _plateOrder.Where(InEventStack).ToList();
        var result = new List<(Control, Vector2)>(stacked.Count);
        if (stacked.Count == 0 || !IsInsideTree()) return result;
        var spots = WindowPlacement.TopCentreStack(
            GetViewport().GetVisibleRect().Size,
            stacked.Select(Footprint).ToArray(),
            EventPlateHudRects(),
            HudPlacement.BottomInset);
        for (int i = 0; i < stacked.Count; i++) result.Add((stacked[i], spots[i]));
        return result;
    }

    private bool InEventStack(Control plate) =>
        GodotObject.IsInstanceValid(plate) && plate.Visible
        && !(_plateLayoutIds.TryGetValue(plate, out var id) && Config.HasWindowPos(id));

    private List<Rect2> EventPlateHudRects()
    {
        var rects = DockHudRects();
        foreach (var frame in new Control?[]
                 {
                     _targetBox,
                     _pluginHud.TryGetValue(LibreKO.Plugins.HudPart.TargetFrame, out var plugin) ? plugin : null,
                 })
            if (frame != null && GodotObject.IsInstanceValid(frame) && frame.IsInsideTree())
                rects.Add(new Rect2(frame.GlobalPosition, Footprint(frame)));
        return rects;
    }
}
