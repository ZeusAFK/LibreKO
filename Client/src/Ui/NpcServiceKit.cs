using Godot;

namespace LibreKO;

internal static class NpcServiceKit
{
    public const string YesLabel = "Yes";
    public const string NoLabel = "No";

    public const string CancelLabel = "Cancel";
    public const string ConfirmLabel = "Confirm";
    private const float NpcOptionHeight = 34f;
    private const int NpcOptionFontSize = 13;
    private const float FooterButtonWidth = 110f;
    private const float FooterButtonHeight = 30f;

    private const float TaxArrowWidth = 34f;
    private const float TaxArrowHeight = 28f;
    private const float TaxValueWidth = 120f;

    public static string Text(int id, string fallback = "") => ItemData.Text(id, fallback);

    public static string Fill(int id, string fallback, params object[] args) =>
        TextTemplate.Fill(ItemData.Text(id, fallback), args);

    public static string WindowTitle(NpcQuestSession npcQuest, string fallback) =>
        npcQuest.TalkingNpcId >= 0 && npcQuest.LastNpcName.Length > 0 ? npcQuest.LastNpcName : fallback;

    public static HudWindow ServiceWindow(CanvasLayer layer, string id, string title, int width, Action closed)
    {
        var window = new HudWindow(id, title, bodyMinWidth: width) { Visible = false };
        window.Closed += closed;
        layer.AddChild(window);
        window.Body.AddThemeConstantOverride("separation", 8);
        return window;
    }

    public static PanelContainer NpcSpeech(float minHeight, out Label upper, out Label lower)
    {
        var box = UiTheme.Section();
        box.CustomMinimumSize = new Vector2(0, minHeight);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        box.AddChild(column);
        upper = SpeechLabel(UiTheme.TextHi);
        column.AddChild(upper);
        lower = SpeechLabel(UiTheme.TextLo);
        column.AddChild(lower);
        return box;
    }

    public static Label SpeechLabel(Color colour)
    {
        var label = UiTheme.Text("", 13, colour);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(1, 0);
        return label;
    }

    public static void SetSpeech(Label label, string text)
    {
        label.Text = text;
        label.Visible = text.Length > 0;
    }

    public static Button NpcOption(string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, NpcOptionHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", NpcOptionFontSize);
        button.Pressed += pressed;
        return button;
    }

    public static HBoxContainer FooterButtons(params Button[] buttons)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        foreach (var button in buttons)
        {
            button.CustomMinimumSize = new Vector2(FooterButtonWidth, FooterButtonHeight);
            row.AddChild(button);
        }
        return row;
    }


    public static HBoxContainer TaxArrows(out Label value, System.Action down, System.Action up)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        var less = UiTheme.SmallButton("<", "");
        less.CustomMinimumSize = new Vector2(TaxArrowWidth, TaxArrowHeight);
        less.Pressed += down;
        row.AddChild(less);
        var frame = UiTheme.Section();
        frame.CustomMinimumSize = new Vector2(TaxValueWidth, 0);
        row.AddChild(frame);
        value = UiTheme.Text("", 15, UiTheme.GoldBright, HorizontalAlignment.Center);
        frame.AddChild(value);
        var more = UiTheme.SmallButton(">", "");
        more.CustomMinimumSize = new Vector2(TaxArrowWidth, TaxArrowHeight);
        more.Pressed += up;
        row.AddChild(more);
        return row;
    }

    public static void StyleTextBox(TextEdit edit)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(4);
        box.SetContentMarginAll(6);
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = new Color(UiTheme.Gold, 0.9f);
        edit.AddThemeStyleboxOverride("normal", box);
        edit.AddThemeStyleboxOverride("focus", focus);
        edit.AddThemeStyleboxOverride("read_only", box);
        edit.AddThemeColorOverride("font_color", UiTheme.TextHi);
        edit.AddThemeFontSizeOverride("font_size", 13);
    }
}
