using Godot;

namespace LibreKO.Presentation;

public partial class LookEditor : VBoxContainer
{
    private readonly VBoxContainer racesBox = new();
    private readonly List<(int Race, Button Button)> raceButtons = new();
    private readonly Label faceLbl, hairLbl;
    private readonly Button[] faceSteps, hairSteps;
    private readonly ColorPickerButton colourPicker;
    private readonly List<int> raceOptions = new();
    private bool locked;

    public int Race { get; private set; }
    public int Face { get; private set; }
    public int HairStyle { get; private set; }
    public Color HairColour => colourPicker.Color;
    public int Hair => HairCode.Pack(HairStyle, colourPicker.Color);

    public event Action? Changed;

    public LookEditor(bool withRace = true, bool withColour = true)
    {
        AddThemeConstantOverride("separation", 8);
        CustomMinimumSize = new Vector2(230, 0);
        if (withRace) AddChild(UiTheme.SectionTitle("Race"));
        racesBox.AddThemeConstantOverride("separation", 4);
        racesBox.Visible = withRace;
        AddChild(racesBox);
        AddChild(UiTheme.SectionTitle("Appearance"));
        (faceLbl, faceSteps) = StepperRow("Face", StepFace);
        (hairLbl, hairSteps) = StepperRow("Hair", StepHair);

        var colourRow = new HBoxContainer();
        colourRow.AddThemeConstantOverride("separation", 8);
        var colourLbl = HudStyle.Label(13);
        colourLbl.Text = "Hair colour";
        colourLbl.CustomMinimumSize = new Vector2(80, 0);
        colourRow.AddChild(colourLbl);
        colourPicker = new ColorPickerButton
        {
            CustomMinimumSize = new Vector2(0, 26),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            EditAlpha = false,
        };
        colourPicker.ColorChanged += _ => { if (!locked) Changed?.Invoke(); };
        colourRow.AddChild(colourPicker);
        colourRow.Visible = withColour;
        AddChild(colourRow);
    }

    public IReadOnlyList<int> RaceOptions => raceOptions;

    public void StepFace(int dir)
    {
        Face = LookVariant.Step(Face, dir, CharacterPreview.FaceCount(Race));
        Changed?.Invoke();
    }

    public void StepHair(int dir)
    {
        HairStyle = LookVariant.Step(HairStyle, dir, CharacterPreview.HairCount(Race));
        Changed?.Invoke();
    }

    public void StepRace(int dir)
    {
        if (raceOptions.Count < 2) return;
        int index = Math.Max(0, raceOptions.IndexOf(Race));
        PickRace(raceOptions[Wrap(index + dir, raceOptions.Count)]);
    }

    public void SetHairColour(Color colour)
    {
        colourPicker.Color = colour;
        Changed?.Invoke();
    }

    public void Load(IReadOnlyList<int> races, int race, int face, int hair)
    {
        foreach (var (_, button) in raceButtons)
            button.QueueFree();
        raceButtons.Clear();
        raceOptions.Clear();
        raceOptions.AddRange(races);
        foreach (int option in races)
        {
            int picked = option;
            var button = UiTheme.TopTabButton(StarterStats.RaceName(option), 13);
            button.Pressed += () => PickRace(picked);
            racesBox.AddChild(button);
            raceButtons.Add((option, button));
        }
        Race = Contains(races, race) || races.Count == 0 ? race : races[0];
        Face = face;
        HairStyle = HairCode.StyleOf(hair);
        colourPicker.Color = HairCode.ColourOf(hair);
        Clamp();
        Refresh();
    }

    public void Refresh()
    {
        foreach (var (race, button) in raceButtons)
        {
            button.SetPressedNoSignal(race == Race);
            button.Disabled = locked;
        }
        faceLbl.Text = Face.ToString();
        hairLbl.Text = HairStyle.ToString();
        SetStepsDisabled(faceSteps, locked || !LookVariant.CanStep(CharacterPreview.FaceCount(Race)));
        SetStepsDisabled(hairSteps, locked || !LookVariant.CanStep(CharacterPreview.HairCount(Race)));
        colourPicker.Disabled = locked;
    }

    public void SetLocked(bool locked)
    {
        this.locked = locked;
        if (locked) CloseColourPicker();
        Refresh();
    }

    public void CloseColourPicker() => colourPicker.GetPopup().Hide();

    private static void SetStepsDisabled(Button[] steps, bool disabled)
    {
        foreach (var step in steps)
            step.Disabled = disabled;
    }

    private void PickRace(int race)
    {
        if (locked) return;
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

    private static int Wrap(int value, int count)
    {
        int n = Mathf.Max(1, count);
        return ((value % n) + n) % n;
    }

    private (Label Value, Button[] Steps) StepperRow(string label, Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var name = HudStyle.Label(13);
        name.Text = label;
        name.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(name);
        var previous = StepButton("<", () => { if (!locked) step(-1); });
        row.AddChild(previous);
        var value = HudStyle.Label(13, HorizontalAlignment.Center);
        value.CustomMinimumSize = new Vector2(36, 0);
        row.AddChild(value);
        var next = StepButton(">", () => { if (!locked) step(1); });
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
