using Godot;

namespace LibreKO;

public sealed partial class HudLogText : RichTextLabel
{
    private const float ScrollbarWidth = 6f;
    private const float ScrollbarTopInset = 5f;
    private const float ScrollbarBottomInset = 22f;
    public bool ScrollbarOnLeft { get; init; }
    private bool? _wasScrollable;

    public override void _Ready()
    {
        LayoutDirection = Control.LayoutDirectionEnum.Ltr;
        TextDirection = Control.TextDirection.Ltr;
        StyleTextPadding();
        StyleScrollbar();
        ConstrainScrollbar();
    }

    public override void _Process(double delta)
    {
        ConstrainScrollbar();
        VScrollBar bar = GetVScrollBar();
        bool scrollable = bar.MaxValue > bar.Page + 0.5;
        if (_wasScrollable != scrollable)
        {
            _wasScrollable = scrollable;
            StyleScrollbarThumb(scrollable);
        }
    }

    private void StyleScrollbar()
    {
        VScrollBar bar = GetVScrollBar();
        bar.Show();

        bar.AddThemeStyleboxOverride("scroll", Fill(new Color(0.018f, 0.020f, 0.025f, 0.72f), 3));
        StyleScrollbarThumb(false);
    }

    private void StyleScrollbarThumb(bool scrollable)
    {
        VScrollBar bar = GetVScrollBar();
        if (!scrollable)
        {
            var clear = Fill(new Color(0, 0, 0, 0), 2);
            bar.AddThemeStyleboxOverride("grabber", clear);
            bar.AddThemeStyleboxOverride("grabber_highlight", clear);
            bar.AddThemeStyleboxOverride("grabber_pressed", clear);
            return;
        }

        bar.AddThemeStyleboxOverride("grabber", Fill(new Color("#b8793f"), 2));
        bar.AddThemeStyleboxOverride("grabber_highlight", Fill(new Color("#dfa35d"), 2));
        bar.AddThemeStyleboxOverride("grabber_pressed", Fill(new Color("#f0bd76"), 2));
    }

    private static StyleBoxFlat Fill(Color color, int radius)
    {
        var style = new StyleBoxFlat { BgColor = color };
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = style.ContentMarginRight = 1;
        return style;
    }

    private void StyleTextPadding()
    {
        var content = new StyleBoxFlat { BgColor = Colors.Transparent };
        content.ContentMarginTop = 3;
        content.ContentMarginBottom = 4;
        content.ContentMarginLeft = ScrollbarOnLeft ? 12 : 5;
        content.ContentMarginRight = ScrollbarOnLeft ? 5 : 12;
        AddThemeStyleboxOverride("normal", content);
    }

    private void ConstrainScrollbar()
    {
        VScrollBar bar = GetVScrollBar();
        bar.Show();
        bar.SetAnchorsPreset(LayoutPreset.TopLeft);
        bar.CustomMinimumSize = new Vector2(ScrollbarWidth, 0);
        bar.Position = new Vector2(
            ScrollbarOnLeft ? 2f : Mathf.Max(0f, Size.X - ScrollbarWidth - 2f),
            ScrollbarTopInset);
        bar.Size = new Vector2(
            ScrollbarWidth,
            Mathf.Max(0f, Size.Y - ScrollbarTopInset - ScrollbarBottomInset));
    }
}
