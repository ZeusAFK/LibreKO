using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class LookEditor : VBoxContainer
{
    private readonly VBoxContainer _races = new();
    private readonly List<(int Race, Button Button)> _raceButtons = new();
    private readonly Label _faceLbl, _hairLbl;
    private readonly Button[] _faceSteps, _hairSteps;
    private readonly ColorPickerButton _colour;
    private bool _locked;

    public int Race { get; private set; }
    public int Face { get; private set; }
    public int HairStyle { get; private set; }
    public Color HairColour => _colour.Color;
    public int Hair => HairCode.Pack(HairStyle, _colour.Color);

    public event Action? Changed;

    public LookEditor()
    {
        AddThemeConstantOverride("separation", 8);
        CustomMinimumSize = new Vector2(230, 0);
        AddChild(UiTheme.SectionTitle("Race"));
        _races.AddThemeConstantOverride("separation", 4);
        AddChild(_races);
        AddChild(UiTheme.SectionTitle("Appearance"));
        (_faceLbl, _faceSteps) = StepperRow("Face", dir => { Face = LookVariant.Step(Face, dir, CharacterPreview.FaceCount(Race)); Changed?.Invoke(); });
        (_hairLbl, _hairSteps) = StepperRow("Hair", dir => { HairStyle = LookVariant.Step(HairStyle, dir, CharacterPreview.HairCount(Race)); Changed?.Invoke(); });

        var colourRow = new HBoxContainer();
        colourRow.AddThemeConstantOverride("separation", 8);
        var colourLbl = HudStyle.Label(13);
        colourLbl.Text = "Hair colour";
        colourLbl.CustomMinimumSize = new Vector2(80, 0);
        colourRow.AddChild(colourLbl);
        _colour = new ColorPickerButton
        {
            CustomMinimumSize = new Vector2(0, 26),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            EditAlpha = false,
        };
        _colour.ColorChanged += _ => { if (!_locked) Changed?.Invoke(); };
        colourRow.AddChild(_colour);
        AddChild(colourRow);
    }

    public void Load(IReadOnlyList<int> races, int race, int face, int hair)
    {
        foreach (var (_, button) in _raceButtons)
            button.QueueFree();
        _raceButtons.Clear();
        foreach (int option in races)
        {
            int picked = option;
            var button = UiTheme.TopTabButton(StarterStats.RaceName(option), 13);
            button.Pressed += () => PickRace(picked);
            _races.AddChild(button);
            _raceButtons.Add((option, button));
        }
        Race = Contains(races, race) || races.Count == 0 ? race : races[0];
        Face = face;
        HairStyle = HairCode.StyleOf(hair);
        _colour.Color = HairCode.ColourOf(hair);
        Clamp();
        Refresh();
    }

    public void Refresh()
    {
        foreach (var (race, button) in _raceButtons)
        {
            button.SetPressedNoSignal(race == Race);
            button.Disabled = _locked;
        }
        _faceLbl.Text = Face.ToString();
        _hairLbl.Text = HairStyle.ToString();
        SetStepsDisabled(_faceSteps, _locked || !LookVariant.CanStep(CharacterPreview.FaceCount(Race)));
        SetStepsDisabled(_hairSteps, _locked || !LookVariant.CanStep(CharacterPreview.HairCount(Race)));
        _colour.Disabled = _locked;
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (locked) CloseColourPicker();
        Refresh();
    }

    public void CloseColourPicker() => _colour.GetPopup().Hide();

    private static void SetStepsDisabled(Button[] steps, bool disabled)
    {
        foreach (var step in steps)
            step.Disabled = disabled;
    }

    private void PickRace(int race)
    {
        if (_locked) return;
        Race = race;
        Clamp();
        Changed?.Invoke();
    }

    private void Clamp()
    {
        Face = LookVariant.Clamp(Face, CharacterPreview.FaceCount(Race));
        HairStyle = LookVariant.Clamp(HairStyle, CharacterPreview.HairCount(Race));
    }

    private static bool Contains(IReadOnlyList<int> races, int race)
    {
        foreach (int option in races)
            if (option == race) return true;
        return false;
    }

    private (Label Value, Button[] Steps) StepperRow(string label, Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var name = HudStyle.Label(13);
        name.Text = label;
        name.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(name);
        var previous = StepButton("<", () => { if (!_locked) step(-1); });
        row.AddChild(previous);
        var value = HudStyle.Label(13, HorizontalAlignment.Center);
        value.CustomMinimumSize = new Vector2(36, 0);
        row.AddChild(value);
        var next = StepButton(">", () => { if (!_locked) step(1); });
        row.AddChild(next);
        AddChild(row);
        return (value, [previous, next]);
    }

    public static Button StepButton(string text, Action pressed)
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(30, 24), FocusMode = FocusModeEnum.None };
        btn.Pressed += pressed;
        return btn;
    }
}
