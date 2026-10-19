using Godot;

namespace LibreKO.Features.Windows;

internal sealed class EventTitlesFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ZoneSession zone;
    private readonly PremiumFeature premium;

    public EventTitlesFeature(IWorldContext context, ZoneSession zone, PremiumFeature premium)
    {
        this.context = context;
        this.zone = zone;
        this.premium = premium;
    }

    private const int TitleGap = 4;
    private static readonly Vector2 TitleMinSize = new(140f, 23f);
    private const int TitleFontSize = 12;
    private const string ExpandGlyph = "▾";
    private const string CollapseGlyph = "▴";

    internal VBoxContainer TitleColumn { get; private set; } = null!;

    public void Build()
    {
        BuildTitleColumn();
        premium.Layer.AddChild(TitleColumn);
        premium.Chip.Resized += Place;
        zone.EventTitlesChangedEvent += Refresh;
        Refresh();
    }

    internal void BuildTitleColumn()
    {
        TitleColumn = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        TitleColumn.AddThemeConstantOverride("separation", TitleGap);
        TitleColumn.Resized += Place;
    }

    public void Dispose() => zone.EventTitlesChangedEvent -= Refresh;

    private void Place()
    {
        if (!GodotObject.IsInstanceValid(TitleColumn) || !GodotObject.IsInstanceValid(premium.Chip)) return;
        HudAnchor.Pin(TitleColumn, HudAnchor.Spot.TopRight, new Vector2(
            HudAnchor.Edge + MiniMap.SquareSize + UiKit.StatusHudGap,
            HudAnchor.Edge + premium.Chip.Size.Y + TitleGap));
    }

    internal void Refresh()
    {
        if (!GodotObject.IsInstanceValid(TitleColumn)) return;
        foreach (var child in TitleColumn.GetChildren())
        {
            TitleColumn.RemoveChild(child);
            child.QueueFree();
        }
        var titles = zone.EventTitles;
        for (int i = 0; i < titles.ShownCount; i++)
            TitleColumn.AddChild(TitleChip(titles.Entries[i], i == 0 && titles.HasToggle ? titles.Expanded : null));
        TitleColumn.Visible = titles.ShownCount > 0;
        TitleColumn.ResetSize();
        Callable.From(Place).CallDeferred();
    }

    private Control TitleChip(EventTitle entry, bool? expanded)
    {
        var chip = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = entry.Message,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            CustomMinimumSize = TitleMinSize,
        };
        chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        chip.AddChild(row);

        var label = UiTheme.Text(entry.Title, TitleFontSize, UiTheme.TextHi);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);

        if (expanded is { } open)
        {
            var toggle = new Button
            {
                Text = open ? CollapseGlyph : ExpandGlyph,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = open ? "Show one event notice" : "Show every event notice",
            };
            var bare = new StyleBoxEmpty();
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
                toggle.AddThemeStyleboxOverride(state, bare);
            toggle.AddThemeFontSizeOverride("font_size", TitleFontSize);
            toggle.AddThemeColorOverride("font_color", UiTheme.Gold);
            toggle.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
            toggle.Pressed += Toggle;
            row.AddChild(toggle);
        }
        return chip;
    }

    private void Toggle()
    {
        zone.EventTitles.ToggleExpanded();
        Refresh();
    }
}
