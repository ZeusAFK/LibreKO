using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class SettingsPanel : CanvasLayer
{
    private readonly Dictionary<KeyAction, Button> _padButtons = new();
    private readonly List<(BindGroup Group, string Label, Control Row)> _padRows = new();
    private readonly Dictionary<BindGroup, Control> _padHeaders = new();
    private KeyAction? _capturingPad;
    private Label _padStatus = null!;

    private void BuildPadTab(VBoxContainer vb)
    {
        vb.AddThemeConstantOverride("separation", 6);

        var hint = new Label
        {
            Text = "Click a binding, then press a controller button. Hold the left or right trigger "
                   + "while pressing to bind a trigger combination. Right-click a binding to clear it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.78f));
        vb.AddChild(hint);

        _padStatus = new Label { Name = "pad_status" };
        _padStatus.AddThemeFontSizeOverride("font_size", 12);
        vb.AddChild(_padStatus);
        RefreshPadStatus();
        Input.Singleton.JoyConnectionChanged += OnPadConnectionChanged;

        var filter = new LineEdit { PlaceholderText = "Search actions…", Name = "pad_filter" };
        Ui.StyleField(filter);
        filter.TextChanged += ApplyPadFilter;
        vb.AddChild(filter);

        BindGroup? section = null;
        foreach (var entry in KeyBinds.All)
        {
            if (section != entry.Group)
            {
                section = entry.Group;
                var header = GroupHeader(entry.Group.ToString());
                _padHeaders[entry.Group] = header;
                vb.AddChild(header);
            }
            var row = PadRow(entry);
            _padRows.Add((entry.Group, entry.Label, row));
            vb.AddChild(row);
        }

        vb.AddChild(new HSeparator());
        var reset = new Button { Text = "Reset All Bindings" };
        reset.Pressed += () =>
        {
            CancelPadCapture();
            KeyBinds.Reset();
            RefreshPadButtons();
            RefreshBindButtons();
        };
        vb.AddChild(reset);
    }

    private void OnPadConnectionChanged(long device, bool connected) => RefreshPadStatus();

    private void RefreshPadStatus()
    {
        int device = KeyBinds.PadDevice();
        bool on = device >= 0;
        _padStatus.Text = on
            ? $"Connected: {Input.GetJoyName(device)}"
            : "No controller detected — bindings can still be edited.";
        _padStatus.AddThemeColorOverride("font_color", on ? UiTheme.Good : UiTheme.TextLo);
    }

    private HBoxContainer PadRow(KeyBinds.Entry entry)
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
            Name = $"pad_{entry.Action}",
            CustomMinimumSize = new Vector2(BindButtonWidth, 0),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = $"{entry.Label} — default {KeyBinds.PadDefault(entry.Action).Text}",
        };
        button.Pressed += () => BeginPadCapture(entry.Action);
        button.Cleared += () =>
        {
            CancelPadCapture();
            KeyBinds.SetPad(entry.Action, PadChord.Unbound);
            RefreshPadButtons();
        };
        _padButtons[entry.Action] = button;
        row.AddChild(button);
        ShowPadBinding(entry.Action);
        return row;
    }

    private void ApplyPadFilter(string text)
    {
        string needle = text.Trim();
        var shown = new HashSet<BindGroup>();
        foreach (var (group, label, row) in _padRows)
        {
            bool match = needle.Length == 0
                         || label.Contains(needle, StringComparison.OrdinalIgnoreCase)
                         || group.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase);
            row.Visible = match;
            if (match) shown.Add(group);
        }
        foreach (var (group, header) in _padHeaders) header.Visible = shown.Contains(group);
    }

    private void BeginPadCapture(KeyAction action)
    {
        CancelPadCapture();
        CancelCapture();
        _capturingPad = action;
        if (_padButtons.TryGetValue(action, out var button)) button.Text = "Press a button…";
    }

    private void CancelPadCapture()
    {
        if (_capturingPad is not { } action) return;
        _capturingPad = null;
        ShowPadBinding(action);
    }

    private PadMod _triggerDown = PadMod.None;
    private bool _triggerUsedInChord;

    public override void _Process(double delta)
    {
        if (_capturingPad is not { } action)
        {
            _triggerDown = PadMod.None;
            return;
        }

        var down = KeyBinds.TriggerHeld(PadMod.LeftTrigger) ? PadMod.LeftTrigger
            : KeyBinds.TriggerHeld(PadMod.RightTrigger) ? PadMod.RightTrigger
            : PadMod.None;

        if (down != PadMod.None)
        {
            if (_triggerDown == PadMod.None) _triggerUsedInChord = false;
            _triggerDown = down;
            return;
        }

        if (_triggerDown == PadMod.None) return;
        var released = _triggerDown;
        _triggerDown = PadMod.None;
        if (_triggerUsedInChord) return;

        _capturingPad = null;
        KeyBinds.SetPad(action, new PadChord(JoyButton.Invalid, released));
        RefreshPadButtons();
    }

    private bool CapturePadButton(InputEventJoypadButton ev)
    {
        if (_capturingPad is not { } action) return false;
        if (_triggerDown != PadMod.None) _triggerUsedInChord = true;
        _capturingPad = null;
        if (ev.ButtonIndex == JoyButton.Back)
        {
            ShowPadBinding(action);
            return true;
        }
        KeyBinds.SetPad(action, PadChord.From(ev, KeyBinds.ActiveMod()));
        RefreshPadButtons();
        return true;
    }

    private void RefreshPadButtons()
    {
        foreach (var action in _padButtons.Keys) ShowPadBinding(action);
    }

    private void ShowPadBinding(KeyAction action)
    {
        if (!_padButtons.TryGetValue(action, out var button)) return;
        var chord = KeyBinds.GetPad(action);
        button.Text = chord.Text;
        button.AddThemeColorOverride("font_color",
            chord.Assigned ? UiTheme.TextHi : new Color(0.55f, 0.55f, 0.58f));
    }
}
