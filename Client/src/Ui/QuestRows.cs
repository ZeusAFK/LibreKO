using Godot;

namespace LibreKO;

internal static class QuestRows
{
    public const float IconSide = 32f;
    public const float ParagraphWidth = 400f;

    public static VBoxContainer Section(Control parent, string title)
    {
        parent.AddChild(UiTheme.Text(title, 13, UiTheme.Gold));
        var panel = UiTheme.Section();
        parent.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);
        return body;
    }

    public static Control ObjectiveTarget(Control row, int questId, int group, bool hasTarget, INpcQuestRequests requests)
    {
        if (!hasTarget) return row;
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.TooltipText = "Show where to find this";
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                requests.QuestTargetRequest(questId, group);
        };
        return row;
    }

    public static string ObjectiveHeading(bool kills, bool deliveries) =>
        kills ? "Hunt" : deliveries ? "Collect" : "Objectives";

    public static RichTextLabel Paragraph(string text, Color color, string? playerName, float width = ParagraphWidth)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            Text = QuestMarkup.Rich(text, playerName ?? ""),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(width, 0)
        };
        label.AddThemeFontSizeOverride("normal_font_size", 14);
        label.AddThemeColorOverride("default_color", color);
        return label;
    }


    private const float GlyphInset = 0.18f;

    public static Control ItemRow(IItemTooltips tooltips, int itemId, string name, string value, Color color)
    {
        var row = UiKit.ValueRow(name, value, color);
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", IconFrame(itemId, false));
        frame.AddChild(RewardGlyph(itemId) ?? ItemIcon(itemId));
        row.AddChild(frame);
        row.MoveChild(frame, 0);
        return Hover(tooltips, row, itemId);
    }

    public static TextureRect ItemIcon(int itemId)
    {
        var icon = new TextureRect
        {
            Texture = ItemIcons.Get(itemId),
            CustomMinimumSize = new Vector2(IconSide, IconSide),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UpgradeBadge.Show(icon, itemId);
        return icon;
    }

    public static Control? RewardGlyph(int itemId)
    {
        string? glyph = itemId switch
        {
            QuestData.CoinItemId => "system/coins",
            QuestData.ExpItemId => "system/level",
            QuestData.LadderPointItemId => "system/medal",
            _ => null,
        };
        if (glyph == null) return null;
        var box = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        int inset = Mathf.RoundToInt(IconSide * GlyphInset);
        foreach (var margin in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            box.AddThemeConstantOverride(margin, inset);
        float side = IconSide - 2 * inset;
        var image = UiIcons.Image(glyph, new Vector2(side, side), UiTheme.GoldBright);
        image.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddChild(image);
        return box;
    }

    public static StyleBoxFlat IconFrame(int itemId, bool chosen) =>
        UiTheme.Slot(chosen ? UiTheme.GoldBright : QuestData.IsVirtualReward(itemId) ? null : ItemGrade.Tint(itemId), chosen);

    public static bool HasItemCard(int itemId) =>
        !QuestData.IsVirtualReward(itemId) && ItemData.Get(itemId) != null;

    public static T Hover<T>(IItemTooltips tooltips, T control, int itemId) where T : Control
    {
        if (!HasItemCard(itemId)) return control;
        control.MouseFilter = Control.MouseFilterEnum.Pass;
        control.MouseEntered += () => tooltips.Show(-1, UiKit.TooltipItem(itemId));
        control.MouseExited += tooltips.Hide;
        return control;
    }
}
