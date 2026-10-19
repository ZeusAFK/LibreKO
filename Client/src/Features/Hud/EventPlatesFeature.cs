using Godot;

namespace LibreKO.Features.Hud;

internal sealed class EventPlatesFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly TargetHudFeature targetHud;

    public EventPlatesFeature(IWorldContext context, TargetHudFeature targetHud)
    {
        this.context = context;
        this.targetHud = targetHud;
    }

    private const string Migration = "event_plates";

    private readonly List<Control> plateOrder = new();
    private readonly Dictionary<Control, string> plateLayoutIds = new();
    private bool platesQueued;

    public void Build()
    {
        Config.ForgetWindowPositionsOnce(Migration, new[] { UiKit.BattleBannerLayoutId, UiKit.BattleBoardLayoutId });
        context.Root.GetViewport().SizeChanged += Queue;
        if (targetHud.TargetBox is { } targetBox) targetBox.Resized += Queue;
    }

    public void Dispose()
    {
        if (context.Root.IsInsideTree()) context.Root.GetViewport().SizeChanged -= Queue;
    }

    internal void AddPlate(Control plate, HudLayout? layout = null, string? layoutId = null)
    {
        plate.SetAnchorsPreset(Control.LayoutPreset.TopLeft, true);
        plate.GrowHorizontal = Control.GrowDirection.End;
        plate.GrowVertical = Control.GrowDirection.End;
        plate.VisibilityChanged += () =>
        {
            plateOrder.Remove(plate);
            if (plate.Visible) plateOrder.Add(plate);
            Queue();
        };
        plate.Resized += Queue;
        if (layout != null && layoutId != null)
        {
            plateLayoutIds[plate] = layoutId;
            layout.Placed += Queue;
        }
        if (plate.Visible) plateOrder.Add(plate);
    }

    internal void Queue()
    {
        if (platesQueued) return;
        platesQueued = true;
        Callable.From(Run).CallDeferred();
    }

    private void Run()
    {
        platesQueued = false;
        if (!context.Root.IsInsideTree()) return;
        foreach (var (plate, spot) in EventPlateSpots())
            plate.Position = spot;
    }

    internal Vector2 PlateSpot(Control plate)
    {
        foreach (var (stacked, spot) in EventPlateSpots())
            if (stacked == plate) return spot;
        return plate.Position;
    }

    private List<(Control Plate, Vector2 Spot)> EventPlateSpots()
    {
        var stacked = plateOrder.Where(InStack).ToList();
        var result = new List<(Control, Vector2)>(stacked.Count);
        if (stacked.Count == 0 || !context.Root.IsInsideTree()) return result;
        var spots = WindowPlacement.TopCentreStack(
            context.Root.GetViewport().GetVisibleRect().Size,
            stacked.Select(UiKit.Footprint).ToArray(),
            PlateHudRects(),
            HudPlacement.BottomInset);
        for (int i = 0; i < stacked.Count; i++) result.Add((stacked[i], spots[i]));
        return result;
    }

    private bool InStack(Control plate) =>
        GodotObject.IsInstanceValid(plate) && plate.Visible
        && !(plateLayoutIds.TryGetValue(plate, out var id) && Config.HasWindowPos(id));

    private List<Rect2> PlateHudRects()
    {
        var rects = context.Hud.DockRects();
        foreach (var frame in new Control?[]
                 {
                     targetHud.TargetBox,
                     context.Hud.PluginPart(LibreKO.Plugins.HudPart.TargetFrame),
                 })
            if (frame != null && GodotObject.IsInstanceValid(frame) && frame.IsInsideTree())
                rects.Add(new Rect2(frame.GlobalPosition, UiKit.Footprint(frame)));
        return rects;
    }
}
