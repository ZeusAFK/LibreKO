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
    private readonly ColorPickerButton _colour;

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
        _faceLbl = StepperRow("Face", dir => { Face = Wrap(Face + dir, CharacterPreview.FaceCount(Race)); Changed?.Invoke(); });
        _hairLbl = StepperRow("Hair", dir => { HairStyle = Wrap(HairStyle + dir, CharacterPreview.HairCount(Race)); Changed?.Invoke(); });

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
        _colour.ColorChanged += _ => Changed?.Invoke();
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
            button.SetPressedNoSignal(race == Race);
        _faceLbl.Text = Face.ToString();
        _hairLbl.Text = HairStyle.ToString();
    }

    private void PickRace(int race)
    {
        Race = race;
        Clamp();
        Changed?.Invoke();
    }

    private void Clamp()
    {
        Face = Mathf.Clamp(Face, 0, Mathf.Max(0, CharacterPreview.FaceCount(Race) - 1));
        HairStyle = Mathf.Clamp(HairStyle, 0, Mathf.Max(0, CharacterPreview.HairCount(Race) - 1));
    }

    private static bool Contains(IReadOnlyList<int> races, int race)
    {
        foreach (int option in races)
            if (option == race) return true;
        return false;
    }

    private static int Wrap(int value, int count)
    {
        int n = Mathf.Max(1, count);
        return ((value % n) + n) % n;
    }

    private Label StepperRow(string label, Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var name = HudStyle.Label(13);
        name.Text = label;
        name.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(name);
        row.AddChild(StepButton("<", () => step(-1)));
        var value = HudStyle.Label(13, HorizontalAlignment.Center);
        value.CustomMinimumSize = new Vector2(36, 0);
        row.AddChild(value);
        row.AddChild(StepButton(">", () => step(1)));
        AddChild(row);
        return value;
    }

    public static Button StepButton(string text, Action pressed)
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(30, 24), FocusMode = FocusModeEnum.None };
        btn.Pressed += pressed;
        return btn;
    }
}
