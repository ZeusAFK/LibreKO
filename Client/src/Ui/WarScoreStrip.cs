using Godot;

namespace LibreKO;

public sealed partial class WarScoreStrip : PanelContainer
{
    public static readonly Color KarusColour = new("d05a4a");
    public static readonly Color ElmoradColour = new("4a82d0");

    private const float StripWidth = 283f;
    private const float CentreWidth = 64f;
    private const float BarHeight = 4f;
    private const float EmptyShare = 1f;
    private const float SidePadding = 12f;
    private const float EdgePadding = 5f;

    private readonly Label _karus;
    private readonly Label _elmorad;
    private readonly Label _centre;
    private readonly Label? _status;
    private readonly ColorRect _karusBar;
    private readonly ColorRect _elmoradBar;

    public WarScoreStrip(bool withStatus)
    {
        CustomMinimumSize = new Vector2(StripWidth, 0);
        var style = UiTheme.Panel(6, true);
        style.ContentMarginLeft = style.ContentMarginRight = SidePadding;
        style.ContentMarginTop = style.ContentMarginBottom = EdgePadding;
        AddThemeStyleboxOverride("panel", style);

        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 3);
        AddChild(rows);

        var scores = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        scores.AddThemeConstantOverride("separation", 6);
        rows.AddChild(scores);
        _karus = Side(scores, KarusColour, HorizontalAlignment.Left);
        _centre = UiTheme.Text("", 14, UiTheme.GoldBright, HorizontalAlignment.Center);
        _centre.CustomMinimumSize = new Vector2(CentreWidth, 0);
        _centre.AddThemeConstantOverride("outline_size", 3);
        _centre.MouseFilter = MouseFilterEnum.Ignore;
        scores.AddChild(_centre);
        _elmorad = Side(scores, ElmoradColour, HorizontalAlignment.Right);

        var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, BarHeight), MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 0);
        rows.AddChild(bar);
        _karusBar = BarPart(bar, KarusColour);
        _elmoradBar = BarPart(bar, ElmoradColour);

        if (withStatus)
        {
            _status = UiTheme.Text("", 12, UiTheme.TextHi, HorizontalAlignment.Center);
            _status.AddThemeConstantOverride("outline_size", 1);
            _status.MouseFilter = MouseFilterEnum.Ignore;
            rows.AddChild(_status);
        }
        SetScores(0, 0);
    }

    public string Centre { set => _centre.Text = value; }

    public void SetScores(int karus, int elmorad)
    {
        _karus.Text = $"Karus  {karus}";
        _elmorad.Text = $"{elmorad}  El Morad";
        bool empty = karus <= 0 && elmorad <= 0;
        _karusBar.SizeFlagsStretchRatio = empty ? EmptyShare : Mathf.Max(0, karus);
        _elmoradBar.SizeFlagsStretchRatio = empty ? EmptyShare : Mathf.Max(0, elmorad);
    }

    public void SetStatus(string text, Color colour)
    {
        if (_status == null) return;
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour);
    }

    private static Label Side(HBoxContainer row, Color colour, HorizontalAlignment align)
    {
        var label = UiTheme.Text("", 14, colour, align);
        label.AddThemeConstantOverride("outline_size", 2);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(label);
        return label;
    }

    private static ColorRect BarPart(HBoxContainer bar, Color colour)
    {
        var part = new ColorRect
        {
            Color = colour,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.AddChild(part);
        return part;
    }
}
