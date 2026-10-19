using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class EmoticonPicker : PopupPanel
{
    private const int Columns = 14;
    private const int IconSize = 22;
    private const int Gap = 2;
    private const int InlineIconSize = 16;
    private const int TooLongText = 44581;
    private const int EmojiColumns = 12;
    private const int EmojiCell = 28;
    private const int EmojiFontSize = 20;
    private static readonly Color EmojiHover = new(1f, 1f, 1f, 0.1f);
    private const int TabFontSize = 12;

    public event System.Action<string>? Picked;

    private readonly GridContainer emoticons;
    private readonly GridContainer emojiGrid;
    private readonly Button emoticonsTab;
    private readonly Button emojiTabButton;
    private static bool emojiTab;

    public EmoticonPicker()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Gap * 2);
        AddChild(column);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", Gap);
        column.AddChild(tabs);
        emoticons = Grid(Columns);
        emojiGrid = Grid(EmojiColumns);
        column.AddChild(emoticons);
        column.AddChild(emojiGrid);
        var group = new ButtonGroup();
        emoticonsTab = Tab("Emoticons", group, false);
        emojiTabButton = Tab("Emoji", group, true);
        tabs.AddChild(emoticonsTab);
        tabs.AddChild(emojiTabButton);
        foreach (string emoji in Domain.Emoji.Picker) emojiGrid.AddChild(EmojiButton(emoji));
        ShowTab(emojiTab);
        var grid = emoticons;
        var sheet = GD.Load<Texture2D>(Emoticons.SheetPath);
        foreach (var e in Emoticons.All)
        {
            string code = e.Code;
            var button = new TextureButton
            {
                TextureNormal = Icon(sheet, e),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(IconSize, IconSize),
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = code,
            };
            button.Pressed += () =>
            {
                Hide();
                Picked?.Invoke(code);
            };
            grid.AddChild(button);
        }
    }

    private static GridContainer Grid(int columns)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", Gap);
        grid.AddThemeConstantOverride("v_separation", Gap);
        return grid;
    }

    private Button Tab(string caption, ButtonGroup group, bool emoji)
    {
        var tab = UiTheme.TopTabButton(caption, TabFontSize);
        tab.ButtonGroup = group;
        tab.ButtonPressed = emoji == emojiTab;
        tab.Pressed += () =>
        {
            ShowTab(emoji);
            ResetSize();
        };
        return tab;
    }

    public void ShowEmojiTab()
    {
        emojiTabButton.SetPressedNoSignal(true);
        emoticonsTab.SetPressedNoSignal(false);
        ShowTab(true);
    }

    private void ShowTab(bool emoji)
    {
        emojiTab = emoji;
        emoticons.Visible = !emoji;
        emojiGrid.Visible = emoji;
    }

    private Button EmojiButton(string emoji)
    {
        var button = new Button
        {
            Text = emoji,
            Flat = true,
            CustomMinimumSize = new Vector2(EmojiCell, EmojiCell),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", EmojiFontSize);
        var hover = new StyleBoxFlat { BgColor = EmojiHover };
        hover.SetCornerRadiusAll(3);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.Pressed += () =>
        {
            Hide();
            Picked?.Invoke(emoji);
        };
        return button;
    }

    public static AtlasTexture? Icon(Texture2D? sheet, EmoticonInfo e) =>
        sheet == null ? null : new AtlasTexture { Atlas = sheet, Region = new Rect2(e.X, e.Y, Emoticons.Cell, Emoticons.Cell) };

    public static AtlasTexture? FirstIcon() =>
        Emoticons.All.Count > 0 ? Icon(GD.Load<Texture2D>(Emoticons.SheetPath), Emoticons.All[0]) : null;

    public static TextureButton InputButton(int size, float inset)
    {
        var button = new TextureButton
        {
            TextureNormal = FirstIcon(),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Emoticons and emoji",
        };
        button.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        button.GrowHorizontal = Control.GrowDirection.Begin;
        button.GrowVertical = Control.GrowDirection.Both;
        button.OffsetLeft = -inset - size;
        button.OffsetRight = -inset;
        button.OffsetTop = -size / 2f;
        button.OffsetBottom = size / 2f;
        return button;
    }

    public void ShowAt(Vector2 at)
    {
        ResetSize();
        var room = GetTree().Root.GetVisibleRect().Size;
        Position = new Vector2I(
            (int)Mathf.Clamp(at.X, 0, Mathf.Max(0, room.X - Size.X)),
            (int)Mathf.Clamp(at.Y - Size.Y, 0, Mathf.Max(0, room.Y - Size.Y)));
        Popup();
    }

    public static string? Insert(LineEdit input, string code)
    {
        if (input.MaxLength > 0 && TextRunes.Count(input.Text) + TextRunes.Count(code) > input.MaxLength)
            return ItemData.Text(TooLongText, "You cannot enter emoticons.");
        input.InsertTextAtCaret(code);
        return null;
    }

    public static string InlineImage(EmoticonInfo e) =>
        $"[img width={InlineIconSize} height={InlineIconSize} region={e.X},{e.Y},{Emoticons.Cell},{Emoticons.Cell}]{Emoticons.SheetPath}[/img]";
}
