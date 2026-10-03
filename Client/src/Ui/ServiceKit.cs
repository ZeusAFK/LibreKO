using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public static class ServiceKit
{
    public const double StatusSeconds = 5.0;
    private const float RuleHeight = 1f;
    private const float RuleMinWidth = 24f;
    private const int RuleTextureWidth = 64;

    public static HBoxContainer Section(string title, Texture2D? icon, out Label note)
    {
        var row = UiTheme.SectionTitle(title, icon);
        var ramp = new Gradient { Colors = new[] { UiTheme.GoldDark, new Color(UiTheme.GoldDark, 0f) } };
        row.AddChild(new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = ramp, Width = RuleTextureWidth, Height = 1 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(RuleMinWidth, RuleHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        note = UiTheme.Text("", 11, UiTheme.TextDim, HorizontalAlignment.Right);
        row.AddChild(note);
        return row;
    }

    public static PanelContainer Well() => UiTheme.Section();
}

public sealed partial class DropWell : PanelContainer
{
    public Func<Variant, bool>? CanDrop;
    public Action<Variant>? Dropped;

    public event Action<int>? Wheeled;

    public DropWell()
    {
        AddThemeStyleboxOverride("panel", UiTheme.Inset());
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (Wheeled == null || ev is not InputEventMouseButton { Pressed: true } mb) return;
        if (mb.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
        Wheeled(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
        AcceptEvent();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CanDrop?.Invoke(data) ?? false;

    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(data);
}

public sealed partial class ServicePager : HBoxContainer
{
    private const float ArrowWidth = 28f;
    private readonly Label _caption;
    private readonly Button _previous;
    private readonly Button _next;

    public event Action<int>? PageChanged;

    public int Page { get; private set; }
    public int Pages { get; private set; } = 1;

    public ServicePager()
    {
        AddThemeConstantOverride("separation", 4);
        _caption = UiTheme.Text("", 12, UiTheme.TextLo);
        _caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddChild(_caption);
        _previous = Arrow("<", "Previous page", -1);
        _next = Arrow(">", "Next page", 1);
        Set(0, 1);
    }

    private Button Arrow(string text, string tooltip, int delta)
    {
        var button = UiTheme.SmallButton(text, tooltip);
        button.CustomMinimumSize = new Vector2(ArrowWidth, button.CustomMinimumSize.Y);
        button.Pressed += () => Step(delta);
        AddChild(button);
        return button;
    }

    public void Set(int page, int pages)
    {
        Pages = Math.Max(1, pages);
        Page = Paging.Step(page, 0, Pages);
        _caption.Text = Paging.Caption(Page, Pages);
        _previous.Disabled = Page == 0;
        _next.Disabled = Page >= Pages - 1;
    }

    public void Step(int delta)
    {
        int page = Paging.Step(Page, delta, Pages);
        if (page == Page) return;
        Set(page, Pages);
        PageChanged?.Invoke(page);
    }
}

public sealed partial class ServiceTabs : HBoxContainer
{
    private const int TabFontSize = 12;
    private readonly List<Button> _buttons = new();
    private readonly ButtonGroup _group = new();
    private string[] _labels = Array.Empty<string>();

    public event Action<int>? Selected;

    public int Current { get; private set; }

    public ServiceTabs() => AddThemeConstantOverride("separation", 2);

    public void SetTabs(IReadOnlyList<string> labels, int selected)
    {
        _labels = new string[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            _labels[i] = labels[i];
            if (i == _buttons.Count)
            {
                var button = UiTheme.UnderlineTabButton("", TabFontSize);
                button.ButtonGroup = _group;
                button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                int index = i;
                button.Pressed += () => Select(index);
                _buttons.Add(button);
                AddChild(button);
            }
            _buttons[i].Text = labels[i];
            _buttons[i].Visible = true;
        }
        for (int i = labels.Count; i < _buttons.Count; i++) _buttons[i].Visible = false;
        if (labels.Count > 0) Select(Math.Clamp(selected, 0, labels.Count - 1), notify: false);
    }

    public void SetBadge(int index, string badge)
    {
        if (index < 0 || index >= _labels.Length) return;
        _buttons[index].Text = badge.Length > 0 ? $"{_labels[index]}  {badge}" : _labels[index];
    }

    public void Select(int index, bool notify = true)
    {
        if (index < 0 || index >= _labels.Length) return;
        Current = index;
        _buttons[index].ButtonPressed = true;
        if (notify) Selected?.Invoke(index);
    }
}

public sealed partial class DetailStrip : PanelContainer
{
    public ItemSlotView Slot { get; }
    public Label Title { get; }
    public Label Sub { get; }
    public HBoxContainer Right { get; }

    public DetailStrip(float slotSize)
    {
        AddThemeStyleboxOverride("panel", UiTheme.Row());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        AddChild(row);

        Slot = new ItemSlotView(slotSize);
        row.AddChild(Slot);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        row.AddChild(text);
        Title = UiTheme.Text("", 13, UiTheme.TextHi);
        Title.ClipText = true;
        Title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(Title);
        Sub = UiTheme.Text("", 11, UiTheme.TextLo);
        Sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(Sub);

        Right = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Right.AddThemeConstantOverride("separation", 6);
        row.AddChild(Right);
    }
}

public sealed partial class StatusLabel : Label
{
    private const int FontSize = 11;
    private readonly StatusLine _line = new();

    public StatusLabel()
    {
        AddThemeFontSizeOverride("font_size", FontSize);
        ClipText = true;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Apply();
    }

    public string Hint
    {
        set
        {
            _line.Hint = value;
            Apply();
        }
    }

    public void Status(string text, bool bad)
    {
        int token = _line.Show(text, bad);
        Apply();
        if (!IsInsideTree()) return;
        GetTree().CreateTimer(ServiceKit.StatusSeconds).Timeout += () =>
        {
            if (IsInstanceValid(this) && _line.Expire(token)) Apply();
        };
    }

    public void ResetStatus()
    {
        _line.Reset();
        Apply();
    }

    private void Apply()
    {
        Text = _line.Text;
        AddThemeColorOverride("font_color", _line.Tone switch
        {
            StatusTone.Good => UiTheme.Good,
            StatusTone.Bad => UiTheme.Bad,
            _ => UiTheme.TextDim,
        });
    }
}

public sealed partial class FooterBand : PanelContainer
{
    private const int PadSide = 10;
    private const int PadEdge = 6;
    private readonly StatusLabel _status;

    public HBoxContainer Left { get; } = new();
    public HBoxContainer Right { get; } = new();

    public FooterBand()
    {
        var style = new StyleBoxFlat { BgColor = new Color(0.03f, 0.03f, 0.037f, 0.7f), BorderColor = UiTheme.GoldDark };
        style.BorderWidthTop = 1;
        style.ContentMarginLeft = style.ContentMarginRight = PadSide;
        style.ContentMarginTop = style.ContentMarginBottom = PadEdge;
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        AddChild(row);
        Left.AddThemeConstantOverride("separation", 6);
        row.AddChild(Left);
        _status = new StatusLabel();
        row.AddChild(_status);
        Right.AddThemeConstantOverride("separation", 6);
        row.AddChild(Right);
    }

    public string Hint { set => _status.Hint = value; }

    public void Status(string text, bool bad) => _status.Status(text, bad);

    public void ResetStatus() => _status.ResetStatus();
}

public sealed partial class MoneyPlaque : PanelContainer
{
    private const float CoinSize = 14f;
    private readonly Label _caption;
    private readonly Label _value;

    public MoneyPlaque(string caption)
    {
        var style = UiTheme.Inset(4);
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        style.ContentMarginTop = style.ContentMarginBottom = 3;
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        AddChild(row);
        row.AddChild(new TextureRect
        {
            Texture = UiIcons.Get("system/coins"),
            CustomMinimumSize = new Vector2(CoinSize, CoinSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = UiTheme.Gold,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var text = new VBoxContainer();
        text.AddThemeConstantOverride("separation", -3);
        row.AddChild(text);
        _caption = UiTheme.Text("", 10, UiTheme.TextLo);
        text.AddChild(_caption);
        _value = UiTheme.Text("0", 13, UiTheme.GoldBright);
        text.AddChild(_value);
        Caption = caption;
    }

    public long Value { set => _value.Text = value.ToString("n0"); }

    public string Caption
    {
        set
        {
            _caption.Text = value;
            _caption.Visible = value.Length > 0;
        }
    }
}
