using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GenieControls
{
    private readonly Action edited;

    public GenieControls(Action edited) => this.edited = edited;

    internal bool Syncing { get; set; }

    internal static Button Button(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    internal CheckButton Option(Node parent, string text, Action<bool> write)
    {
        var button = new CheckButton { Text = text };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Toggled += on =>
        {
            if (Syncing) return;
            write(on);
            edited();
        };
        parent.AddChild(button);
        return button;
    }

    internal SpinBox Percent(Node parent, string title, int min, int max, Action<int> write)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        row.AddChild(UiTheme.Text(title, 12, UiTheme.TextLo));
        return Number(row, min, max, write);
    }

    internal SpinBox Number(Node parent, int min, int max, Action<int> write)
    {
        var input = new SpinBox { MinValue = min, MaxValue = max, Step = 1 };
        input.ValueChanged += value =>
        {
            if (Syncing) return;
            write((int)value);
            edited();
        };
        parent.AddChild(input);
        return input;
    }
}
