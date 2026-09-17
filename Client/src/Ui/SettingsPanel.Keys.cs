using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class SettingsPanel : CanvasLayer
{
    private const int BindButtonWidth = 132;

    private readonly Dictionary<KeyAction, Button> _bindButtons = new();
    private readonly List<(BindGroup Group, string Label, Control Row)> _bindRows = new();
    private readonly Dictionary<BindGroup, Control> _bindHeaders = new();
    private KeyAction? _capturing;

    private void BuildKeysTab(VBoxContainer vb)
    {
        vb.AddThemeConstantOverride("separation", 6);

        var hint = new Label
        {
            Text = "Click a binding, then press the new key. Right-click a binding to clear it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.78f));
        vb.AddChild(hint);

        var filter = new LineEdit { PlaceholderText = "Search actions…", Name = "bind_filter" };
        Ui.StyleField(filter);
        filter.TextChanged += ApplyBindFilter;
        vb.AddChild(filter);

        BindGroup? section = null;
        foreach (var entry in KeyBinds.All)
        {
            if (section != entry.Group)
            {
                section = entry.Group;
                var header = GroupHeader(entry.Group.ToString());
                _bindHeaders[entry.Group] = header;
                vb.AddChild(header);
            }
            var row = BindRow(entry);
            _bindRows.Add((entry.Group, entry.Label, row));
            vb.AddChild(row);
        }

        vb.AddChild(new HSeparator());
        var reset = new Button { Text = "Reset All Bindings" };
        reset.Pressed += () =>
        {
            CancelCapture();
            KeyBinds.Reset();
            RefreshBindButtons();
        };
        vb.AddChild(reset);
    }

    private static Label GroupHeader(string text)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", UiTheme.Gold);
        return label;
    }

    private HBoxContainer BindRow(KeyBinds.Entry entry)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label
        {
            Text = entry.Label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });

        var button = new BindButton
        {
            Name = $"bind_{entry.Action}",
            CustomMinimumSize = new Vector2(BindButtonWidth, 0),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = $"{entry.Label} — default {entry.Default.Text}",
        };
        button.Pressed += () => BeginCapture(entry.Action);
        button.Cleared += () =>
        {
            CancelCapture();
            KeyBinds.Set(entry.Action, KeyChord.Unbound);
            RefreshBindButtons();
        };
        _bindButtons[entry.Action] = button;
        row.AddChild(button);
        ShowBinding(entry.Action);
        return row;
    }

    private void ApplyBindFilter(string text)
    {
        string needle = text.Trim();
        var shown = new HashSet<BindGroup>();
        foreach (var (group, label, row) in _bindRows)
        {
            bool match = needle.Length == 0
                         || label.Contains(needle, StringComparison.OrdinalIgnoreCase)
                         || group.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase);
            row.Visible = match;
            if (match) shown.Add(group);
        }
        foreach (var (group, header) in _bindHeaders) header.Visible = shown.Contains(group);
    }

    private void BeginCapture(KeyAction action)
    {
        CancelCapture();
        _capturing = action;
        if (_bindButtons.TryGetValue(action, out var button)) button.Text = "Press a key…";
    }

    private void CancelCapture()
    {
        if (_capturing is not { } action) return;
        _capturing = null;
        ShowBinding(action);
    }

    private bool CaptureBindKey(InputEventKey key)
    {
        if (_capturing is not { } action) return false;
        if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return true;

        _capturing = null;
        if (key.Keycode == Key.Escape)
        {
            ShowBinding(action);
            return true;
        }

        KeyBinds.Set(action, KeyChord.From(key));
        RefreshBindButtons();
        return true;
    }

    private void RefreshBindButtons()
    {
        foreach (var action in _bindButtons.Keys) ShowBinding(action);
    }

    private void ShowBinding(KeyAction action)
    {
        if (!_bindButtons.TryGetValue(action, out var button)) return;
        var chord = KeyBinds.Get(action);
        button.Text = chord.Text;
        button.AddThemeColorOverride("font_color",
            chord.Assigned ? UiTheme.TextHi : new Color(0.55f, 0.55f, 0.58f));
    }

    private sealed partial class BindButton : Button
    {
        public event Action? Cleared;

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
            Cleared?.Invoke();
            AcceptEvent();
        }
    }
}
