using Godot;

namespace LibreKO;

public partial class PotionButton : TouchTapButton
{
    private readonly Color accent;
    private readonly TextureRect art;
    private readonly CooldownWipe cool;
    private readonly Label countLabel;

    public int HealTarget { get; }
    public int ItemId { get; private set; }

    public void SetCooldown(float remain) => cool.Remain = remain;

    public PotionButton(int healTarget, Color accent)
    {
        HealTarget = healTarget;
        this.accent = accent;
        float size = TouchControls.PotionSize;

        FocusMode = FocusModeEnum.None;
        ActionMode = ActionModeEnum.Press;
        CustomMinimumSize = new Vector2(size, size);
        assigned = Skin(0f, 0f);
        empty = Skin(0.42f, 0.45f);
        AddThemeStyleboxOverride("pressed", Skin(0.34f, 0.9f));
        AddThemeStyleboxOverride("disabled", empty);

        art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        art.SetAnchorsPreset(LayoutPreset.FullRect);
        float inset = size * 0.03f;
        art.OffsetLeft = art.OffsetTop = inset;
        art.OffsetRight = art.OffsetBottom = -inset;
        AddChild(art);

        cool = TouchControls.AddCooldown(this, size);

        var badgePanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var chip = new StyleBoxFlat { BgColor = new Color(0.02f, 0.022f, 0.028f, 0.90f) };
        chip.SetCornerRadiusAll((int)(size * 0.15f));
        chip.ContentMarginLeft = chip.ContentMarginRight = size * 0.10f;
        chip.ContentMarginTop = chip.ContentMarginBottom = size * 0.015f;
        badgePanel.AddThemeStyleboxOverride("panel", chip);
        badgePanel.SetAnchorsPreset(LayoutPreset.BottomRight);
        badgePanel.GrowHorizontal = GrowDirection.Begin;
        badgePanel.GrowVertical = GrowDirection.Begin;
        badgePanel.OffsetLeft = badgePanel.OffsetRight = -size * 0.06f;
        badgePanel.OffsetTop = badgePanel.OffsetBottom = -size * 0.06f;
        AddChild(badgePanel);

        countLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        countLabel.AddThemeFontSizeOverride("font_size", (int)(size * 0.20f));
        countLabel.AddThemeColorOverride("font_color", UiTheme.TextHi);
        badgePanel.AddChild(countLabel);
        badge = badgePanel;
    }

    private readonly Control badge;
    private readonly StyleBoxFlat assigned;
    private readonly StyleBoxFlat empty;

    internal void Set(int itemId, System.Func<int, int> countOf)
    {
        ItemId = itemId;
        Disabled = itemId == 0;
        var skin = itemId == 0 ? empty : assigned;
        AddThemeStyleboxOverride("normal", skin);
        AddThemeStyleboxOverride("hover", skin);
        art.Texture = itemId == 0 ? null : ItemIcons.Get(itemId);
        art.SelfModulate = new Color(1f, 1f, 1f, itemId == 0 ? 0f : 1f);
        int count = itemId == 0 ? 0 : countOf(itemId);
        countLabel.Text = count.ToString();
        badge.Visible = count > 0;
        TooltipText = itemId == 0
            ? HealTarget == Domain.HealTarget.Hp ? "No health potion" : "No mana potion"
            : $"{ItemData.DisplayName(itemId)} × {count}";
    }

    private StyleBoxFlat Skin(float fill, float edge)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(accent.R * 0.20f, accent.G * 0.20f, accent.B * 0.20f, fill),
            BorderColor = new Color(accent, edge),
            ShadowColor = new Color(0f, 0f, 0f, 0.5f),
            ShadowSize = (int)(TouchControls.PotionSize * 0.05f),
        };
        box.SetCornerRadiusAll((int)(TouchControls.PotionSize * 0.22f));
        box.SetBorderWidthAll(edge > 0f ? 2 : 0);
        return box;
    }
}
