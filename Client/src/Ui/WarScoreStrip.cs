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
    private const float CloseSize = 18f;
    private const int CloseIconWidth = 10;

    private readonly string leftName;
    private readonly string rightName;
    private readonly Label leftLabel;
    private readonly Label rightLabel;
    private readonly Label centre;
    private readonly Label? status;
    private readonly Label? clock;
    private readonly Button? ranking;
    private readonly ColorRect leftBar;
    private readonly ColorRect rightBar;

    public event System.Action? RankingPressed;

    public Button? CloseButton { get; }

    public WarScoreStrip(bool withStatus)
        : this(withStatus, "Karus", KarusColour, "El Morad", ElmoradColour, closable: false)
    {
    }

    public WarScoreStrip(bool withStatus, string leftName, Color leftColour, string rightName, Color rightColour, bool closable)
    {
        this.leftName = leftName;
        this.rightName = rightName;
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
        leftLabel = Side(scores, leftColour, HorizontalAlignment.Left);
        centre = UiTheme.Text("", 14, UiTheme.GoldBright, HorizontalAlignment.Center);
        centre.CustomMinimumSize = new Vector2(CentreWidth, 0);
        centre.AddThemeConstantOverride("outline_size", 3);
        centre.MouseFilter = MouseFilterEnum.Ignore;
        scores.AddChild(centre);
        rightLabel = Side(scores, rightColour, HorizontalAlignment.Right);
        if (closable)
        {
            var close = UiTheme.IconButton(UiIcons.Get("system/close"), "Close");
            close.CustomMinimumSize = new Vector2(CloseSize, CloseSize);
            close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            close.AddThemeConstantOverride("icon_max_width", CloseIconWidth);
            close.AddThemeColorOverride("icon_normal_color", new Color(UiTheme.TextHi, 0.7f));
            close.AddThemeColorOverride("icon_hover_color", UiTheme.TextHi);
            scores.AddChild(close);
            CloseButton = close;
        }

        var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, BarHeight), MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 0);
        rows.AddChild(bar);
        leftBar = BarPart(bar, leftColour);
        rightBar = BarPart(bar, rightColour);

        if (withStatus)
        {
            status = UiTheme.Text("", 12, UiTheme.TextHi, HorizontalAlignment.Center);
            status.AddThemeConstantOverride("outline_size", 1);
            status.MouseFilter = MouseFilterEnum.Ignore;
            rows.AddChild(status);

            var foot = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            foot.AddThemeConstantOverride("separation", 6);
            rows.AddChild(foot);
            clock = UiTheme.Text("", 12, UiTheme.TextLo);
            clock.AddThemeConstantOverride("outline_size", 1);
            clock.MouseFilter = MouseFilterEnum.Ignore;
            clock.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            clock.VerticalAlignment = VerticalAlignment.Center;
            foot.AddChild(clock);
            ranking = new Button { Text = "Ranking", FocusMode = FocusModeEnum.None, Visible = false };
            ranking.AddThemeFontSizeOverride("font_size", 11);
            ranking.Pressed += () => RankingPressed?.Invoke();
            foot.AddChild(ranking);
        }
        SetScores(0, 0);
    }

    public bool RankingShown { set { if (ranking != null) ranking.Visible = value; } }

    public void SetClock(string text)
    {
        if (clock == null) return;
        clock.Text = text;
        clock.GetParent<Control>().Visible = text.Length > 0 || ranking is { Visible: true };
    }

    public string Centre { set => centre.Text = value; }

    public void SetScores(int left, int right)
    {
        leftLabel.Text = $"{leftName}  {left}";
        rightLabel.Text = $"{right}  {rightName}";
        bool empty = left <= 0 && right <= 0;
        leftBar.SizeFlagsStretchRatio = empty ? EmptyShare : Mathf.Max(0, left);
        rightBar.SizeFlagsStretchRatio = empty ? EmptyShare : Mathf.Max(0, right);
    }

    public void SetStatus(string text, Color colour)
    {
        if (status == null) return;
        status.Text = text;
        status.AddThemeColorOverride("font_color", colour);
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
