using Godot;

namespace LibreKO.Features.Hud;

internal enum TopIconSlot { AttendanceGift, Trophy, Mail, PowerUpStore, Lottery, TimedItems, Events, Genie, DailyRank, Siege }

internal sealed class TopIconsFeature : IFeature
{
    private const float IconSize = 29f;
    private const float IconGap = 6f;
    private const float IconPadX = 9f;
    private const float TopRightTolerance = 48f;

    private readonly IWorldContext context;
    private readonly Action openPowerUpStore;
    private readonly Action toggleLottery;
    private readonly Action toggleGenie;
    private readonly Dictionary<TopIconSlot, Control> icons = new();
    private CanvasLayer layer = null!;
    private Control? anchor;
    private Rect2 row;
    private Button lotteryIcon = null!;
    private TextureRect lotteryImage = null!;
    private Button powerUpStoreIcon = null!;
    private TextureRect powerUpStoreImage = null!;

    public TopIconsFeature(IWorldContext context, Action openPowerUpStore, Action toggleLottery, Action toggleGenie)
    {
        this.context = context;
        this.openPowerUpStore = openPowerUpStore;
        this.toggleLottery = toggleLottery;
        this.toggleGenie = toggleGenie;
    }

    internal CanvasLayer HudLayer => layer;

    internal Rect2 Row => row;

    internal Control? Icon(TopIconSlot slot) => icons.TryGetValue(slot, out var icon) ? icon : null;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(layer);

        powerUpStoreIcon = Add(TopIconSlot.PowerUpStore, "system/gem", "Power-Up Store", openPowerUpStore, out powerUpStoreImage);
        powerUpStoreImage.SelfModulate = UiTheme.Premium;
        powerUpStoreIcon.Resized += () => Place(TopIconSlot.PowerUpStore, HudPlacement.PowerUpStoreIcon);

        lotteryIcon = Add(TopIconSlot.Lottery, "system/ticket", "Lottery Event", toggleLottery, out lotteryImage);
        lotteryImage.SelfModulate = UiKit.TopIconColor(true);
        lotteryIcon.Visible = false;
        lotteryIcon.Resized += () => Place(TopIconSlot.Lottery, HudPlacement.LotteryIcon);
        lotteryIcon.VisibilityChanged += () => Place(TopIconSlot.Lottery, HudPlacement.LotteryIcon);

        var genie = Add(TopIconSlot.Genie, "system/sparkle", "Genie", toggleGenie, out _);
        genie.Visible = !Platform.TouchUi;

        if (context.Hud.PluginPart(HudPart.MiniMap) is { } themedMap)
        {
            themedMap.ItemRectChanged += PlaceAll;
            themedMap.VisibilityChanged += PlaceAll;
        }
        Callable.From(PlaceAll).CallDeferred();
        Callable.From(() => Place(TopIconSlot.PowerUpStore, HudPlacement.PowerUpStoreIcon)).CallDeferred();
        Callable.From(() => Place(TopIconSlot.Lottery, HudPlacement.LotteryIcon)).CallDeferred();
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
    }

    internal void SetAnchor(Control chip)
    {
        anchor = chip;
        chip.Resized += PlaceAll;
    }

    internal Button Add(TopIconSlot slot, string iconId, string tooltip, Action pressed, out TextureRect image, CanvasLayer? layer = null)
    {
        var button = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip };
        var flat = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", flat);
        button.AddThemeStyleboxOverride("hover", flat);
        button.AddThemeStyleboxOverride("pressed", flat);
        button.Pressed += pressed;
        (layer ?? this.layer).AddChild(button);

        image = UiIcons.Image(iconId, new Vector2(IconSize, IconSize), UiKit.TopIconColor(false));
        image.MouseFilter = Control.MouseFilterEnum.Ignore;
        image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(image);
        icons[slot] = button;
        return button;
    }

    internal void RefreshLottery(bool active)
    {
        if (!GodotObject.IsInstanceValid(lotteryIcon)) return;
        lotteryIcon.Visible = active;
    }

    internal float MiniMapRightSpan()
    {
        var themed = context.Hud.PluginPart(HudPart.MiniMap);
        if (themed == null || !GodotObject.IsInstanceValid(themed))
            return HudAnchor.Edge + MiniMap.SquareSize;
        if (!themed.IsVisibleInTree() || themed.Size.X <= 0f) return HudAnchor.Edge;
        var rect = themed.GetGlobalRect();
        float screen = context.Root.GetViewport().GetVisibleRect().Size.X;
        bool topRight = rect.End.X >= screen - TopRightTolerance && rect.Position.Y <= TopRightTolerance;
        return topRight ? Mathf.Max(HudAnchor.Edge, screen - rect.Position.X) : HudAnchor.Edge;
    }

    internal void PinAnchor()
    {
        if (anchor == null || !GodotObject.IsInstanceValid(anchor)) return;
        HudAnchor.Pin(anchor, HudAnchor.Spot.TopRight, new Vector2(MiniMapRightSpan() + UiKit.StatusHudGap, HudAnchor.Edge));
    }

    internal void Place(TopIconSlot slot, HudPlacement.Slot touchSlot)
    {
        if (!Platform.TouchUi)
        {
            PlaceAll();
            return;
        }
        if (!icons.TryGetValue(slot, out var icon) || !GodotObject.IsInstanceValid(icon) || icon is not Button button) return;
        float side = HudPlacement.LauncherButtonSize;
        button.CustomMinimumSize = new Vector2(side, side);
        float inset = side * HudPlacement.LauncherGlyphInset;
        foreach (var child in button.GetChildren())
        {
            if (child is not TextureRect image) continue;
            image.OffsetLeft = image.OffsetTop = inset;
            image.OffsetRight = image.OffsetBottom = -inset;
        }
        touchSlot.ApplyTo(button);
    }

    internal void PlaceAll()
    {
        if (Platform.TouchUi || anchor == null || !GodotObject.IsInstanceValid(anchor)) return;

        PinAnchor();
        var row = anchor.Visible ? anchor.GetGlobalRect() : new Rect2();
        var size = new Vector2(IconSize + IconPadX * 2f, Mathf.Max(anchor.Size.Y, IconSize));
        float right = MiniMapRightSpan() + UiKit.StatusHudGap + anchor.Size.X;
        float top = HudAnchor.Edge + (anchor.Size.Y - size.Y) * 0.5f;
        foreach (TopIconSlot slot in Enum.GetValues<TopIconSlot>())
        {
            if (!icons.TryGetValue(slot, out var icon) || !GodotObject.IsInstanceValid(icon) || !icon.Visible) continue;
            right += IconGap;
            icon.CustomMinimumSize = size;
            HudAnchor.Pin(icon, HudAnchor.Spot.TopRight, new Vector2(right, top));
            right += size.X;
            row = row.HasArea() ? row.Merge(icon.GetGlobalRect()) : icon.GetGlobalRect();
        }
        if (row.IsEqualApprox(this.row)) return;
        this.row = row;
        PluginHost.Game.RaiseTopRightRow();
    }
}
