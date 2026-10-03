using System;
using Godot;

namespace LibreKO;

public sealed partial class QuantityPrompt : CanvasLayer
{
    private const float BoxWidth = 320f;
    private const float FieldWidth = 150f;

    private readonly TextureRect _icon;
    private readonly Label _title;
    private readonly Label _limit;
    private readonly MoneyEdit _amount;
    private readonly Button _ok;
    private readonly Label _summary;
    private long _max;
    private Action<long>? _confirm;
    private Func<long, string>? _summarise;

    public QuantityPrompt(int layer)
    {
        Layer = layer;
        Visible = false;

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 14, 12, 14, 12);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(BoxWidth, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        root.AddChild(head);
        _icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(_icon);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        _title = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(_title);
        _limit = UiTheme.Text("", 11, UiTheme.Gold);
        headText.AddChild(_limit);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        root.AddChild(row);
        var label = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        _amount = new MoneyEdit(long.MaxValue, FieldWidth);
        _amount.ValueChanged += _ => Refresh();
        _amount.TextSubmitted += _ => Confirm();
        row.AddChild(_amount);
        var all = new Button { Text = "All", FocusMode = Control.FocusModeEnum.None };
        all.Pressed += () => { _amount.Value = _max; Refresh(); };
        row.AddChild(all);

        _summary = UiTheme.Text("", 12, UiTheme.TextLo);
        _summary.Visible = false;
        root.AddChild(_summary);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += Close;
        buttons.AddChild(cancel);
        _ok = new Button { Text = "OK", FocusMode = Control.FocusModeEnum.None };
        _ok.Pressed += Confirm;
        buttons.AddChild(_ok);
    }

    public void Open(Texture2D? icon, string title, string limit, long max, long start, Action<long> confirm,
        string okText = "OK", Func<long, string>? summary = null)
    {
        _max = Math.Max(0, max);
        _confirm = confirm;
        _summarise = summary;
        _summary.Visible = summary != null;
        _ok.Text = okText;
        _icon.Texture = icon;
        _icon.Visible = icon != null;
        _title.Text = title;
        _limit.Text = limit;
        _amount.Value = Math.Clamp(start, 0, _max);
        Visible = true;
        Refresh();
        if (!_amount.IsInsideTree()) return;
        _amount.GrabFocus();
        _amount.SelectAll();
    }

    public void Close()
    {
        Visible = false;
        _confirm = null;
    }

    private void Refresh()
    {
        _ok.Disabled = _amount.Value <= 0 || _amount.Value > _max;
        if (_summarise != null) _summary.Text = _summarise(_amount.Value);
    }

    private void Confirm()
    {
        long value = _amount.Value;
        if (value <= 0 || value > _max || _confirm is not { } confirm) return;
        Close();
        confirm(value);
    }
}
