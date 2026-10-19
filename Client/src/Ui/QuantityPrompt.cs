using System;
using Godot;

namespace LibreKO;

public sealed partial class QuantityPrompt : CanvasLayer, IClosableWindow
{
    private const float BoxWidth = 320f;
    private const float FieldWidth = 150f;

    private readonly TextureRect iconRect;
    private readonly Label titleLabel;
    private readonly Label limitLabel;
    private readonly MoneyEdit amount;
    private readonly Button okButton;
    private readonly Label summaryLabel;
    private long max;
    private Action<long>? confirm;
    private Func<long, string>? summarise;

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
        iconRect = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(iconRect);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        titleLabel = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(titleLabel);
        limitLabel = UiTheme.Text("", 11, UiTheme.Gold);
        headText.AddChild(limitLabel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        root.AddChild(row);
        var label = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        amount = new MoneyEdit(long.MaxValue, FieldWidth);
        amount.ValueChanged += _ => Refresh();
        amount.TextSubmitted += _ => Confirm();
        row.AddChild(amount);
        var all = new Button { Text = "All", FocusMode = Control.FocusModeEnum.None };
        all.Pressed += () => { amount.Value = max; Refresh(); };
        row.AddChild(all);

        summaryLabel = UiTheme.Text("", 12, UiTheme.TextLo);
        summaryLabel.Visible = false;
        root.AddChild(summaryLabel);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += Close;
        buttons.AddChild(cancel);
        okButton = new Button { Text = "OK", FocusMode = Control.FocusModeEnum.None };
        okButton.Pressed += Confirm;
        buttons.AddChild(okButton);
    }

    public void Open(Texture2D? icon, string title, string limit, long max, long start, Action<long> confirm,
        string okText = "OK", Func<long, string>? summary = null)
    {
        this.max = Math.Max(0, max);
        this.confirm = confirm;
        summarise = summary;
        summaryLabel.Visible = summary != null;
        okButton.Text = okText;
        iconRect.Texture = icon;
        iconRect.Visible = icon != null;
        titleLabel.Text = title;
        limitLabel.Text = limit;
        amount.Value = Math.Clamp(start, 0, this.max);
        Visible = true;
        Refresh();
        if (!amount.IsInsideTree()) return;
        amount.GrabFocus();
        amount.SelectAll();
    }

    public bool IsOpen => Visible;

    public void Close()
    {
        Visible = false;
        confirm = null;
    }

    private void Refresh()
    {
        okButton.Disabled = amount.Value <= 0 || amount.Value > max;
        if (summarise != null) summaryLabel.Text = summarise(amount.Value);
    }

    private void Confirm()
    {
        long value = amount.Value;
        if (value <= 0 || value > max || this.confirm is not { } confirm) return;
        Close();
        confirm(value);
    }
}
