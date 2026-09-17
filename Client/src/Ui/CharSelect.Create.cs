using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class CharSelect : Node3D
{
    private const int MaxSlots = 4;

    private static Vector2 StepperSize =>
        Platform.Pick(new Vector2(34, 26), new Vector2(52, Ui.TouchButtonHeight));
    private static readonly string[] StatLabels = { "STR", "HP", "DEX", "INT", "MP" };

    private PanelContainer _createPanel = null!;
    private VBoxContainer _raceBox = null!;
    private VBoxContainer _jobBox = null!;
    private LineEdit _createName = null!;
    private Label _createBonus = null!;
    private Label _createStatus = null!;
    private Label _faceLbl = null!;
    private Label _hairLbl = null!;
    private ColorPickerButton _hairColour = null!;
    private Button _createConfirm = null!;
    private readonly Label[] _statValues = new Label[5];
    private readonly Button[] _statUp = new Button[5];
    private readonly Button[] _statDown = new Button[5];
    private readonly int[] _alloc = new int[5];

    private int _createNation = Nations.Karus;
    private int _createRace;
    private int _createClass;
    private int _createFace;
    private int _createHair;
    private bool _creating;

    private int FreeSlot()
    {
        var used = new HashSet<int>();
        foreach (var c in _characters) used.Add(_characters.IndexOf(c));
        for (int i = 0; i < MaxSlots; i++)
            if (i >= _characters.Count) return i;
        return -1;
    }

    private void BuildCreatePanel(Control overlay)
    {
        _createPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _createPanel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _createPanel.OffsetRight = PanelWidth + 30;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.028f, 0.80f),
            BorderColor = new Color(UiTheme.Gold, 0.35f),
        };
        style.BorderWidthRight = 1;
        _createPanel.AddThemeStyleboxOverride("panel", style);
        overlay.AddChild(_createPanel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        _createPanel.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        root.AddChild(Ui.Legend("Create Character", 22, UiTheme.GoldBright));

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        var form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(form);

        form.AddChild(SectionLabel("Name"));
        _createName = new LineEdit { PlaceholderText = "character name", MaxLength = 20 };
        Ui.StyleField(_createName);
        _createName.TextChanged += _ => RefreshCreateState();
        form.AddChild(_createName);

        if (Platform.TouchUi)
        {
            var picks = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            picks.AddThemeConstantOverride("separation", 12);
            form.AddChild(picks);
            _raceBox = PickColumn(picks, "Race");
            _jobBox = PickColumn(picks, "Job");
        }
        else
        {
            form.AddChild(SectionLabel("Race"));
            _raceBox = new VBoxContainer();
            _raceBox.AddThemeConstantOverride("separation", 5);
            form.AddChild(_raceBox);

            form.AddChild(SectionLabel("Job"));
            _jobBox = new VBoxContainer();
            _jobBox.AddThemeConstantOverride("separation", 5);
            form.AddChild(_jobBox);
        }

        form.AddChild(SectionLabel("Stats"));
        for (int i = 0; i < 5; i++) form.AddChild(BuildStatRow(i));
        _createBonus = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _createBonus.AddThemeFontSizeOverride("font_size", 13);
        _createBonus.AddThemeColorOverride("font_color", UiTheme.Neutral);
        form.AddChild(_createBonus);

        form.AddChild(SectionLabel("Appearance"));
        _faceLbl = BuildStepperRow(form, "Face", d => StepFace(d));
        _hairLbl = BuildStepperRow(form, "Hair", d => StepHair(d));

        var colourRow = new HBoxContainer();
        colourRow.AddThemeConstantOverride("separation", 8);
        var colourLbl = new Label { Text = "Hair colour", CustomMinimumSize = new Vector2(96, 0) };
        colourLbl.AddThemeFontSizeOverride("font_size", 13);
        colourLbl.AddThemeColorOverride("font_color", UiTheme.TextHi);
        colourRow.AddChild(colourLbl);
        _hairColour = new ColorPickerButton
        {
            Color = new Color(0.35f, 0.22f, 0.10f),
            CustomMinimumSize = new Vector2(0, Platform.Pick(28, Ui.TouchButtonHeight)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            EditAlpha = false,
        };
        _hairColour.ColorChanged += _ => RefreshCreatePreview();
        colourRow.AddChild(_hairColour);
        form.AddChild(colourRow);

        _createStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _createStatus.AddThemeFontSizeOverride("font_size", 12);
        _createStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
        root.AddChild(_createStatus);

        _createConfirm = Ui.MenuButton("Create", 44, 19);
        _createConfirm.Pressed += SubmitCreate;
        var cancel = Ui.MenuButton("Cancel", 34, 15);
        cancel.Pressed += CloseCreate;

        if (Platform.TouchUi)
        {
            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", 8);
            cancel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _createConfirm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            actions.AddChild(cancel);
            actions.AddChild(_createConfirm);
            root.AddChild(actions);
        }
        else
        {
            root.AddChild(_createConfirm);
            root.AddChild(cancel);
        }

        Net.I.CreateCharResultEvent += OnCreateResult;
    }

    private Control BuildStatRow(int index)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var label = new Label { Text = StatLabels[index], CustomMinimumSize = new Vector2(44, 0) };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", UiTheme.TextHi);
        row.AddChild(label);

        _statDown[index] = Ui.MenuButton("◀", 26, 13);
        _statDown[index].CustomMinimumSize = StepperSize;
        int down = index;
        _statDown[index].Pressed += () => StepStat(down, -1);
        row.AddChild(_statDown[index]);

        _statValues[index] = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(44, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Platform.Pick(Control.SizeFlags.Fill, Control.SizeFlags.ExpandFill),
        };
        _statValues[index].AddThemeFontSizeOverride("font_size", 15);
        _statValues[index].AddThemeColorOverride("font_color", UiTheme.Gold);
        row.AddChild(_statValues[index]);

        _statUp[index] = Ui.MenuButton("▶", 26, 13);
        _statUp[index].CustomMinimumSize = StepperSize;
        int up = index;
        _statUp[index].Pressed += () => StepStat(up, 1);
        row.AddChild(_statUp[index]);
        return row;
    }

    private static Label BuildStepperRow(Control parent, string label, System.Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var name = new Label { Text = label, CustomMinimumSize = new Vector2(96, 0) };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", UiTheme.TextHi);
        row.AddChild(name);

        var prev = Ui.MenuButton("◀", 26, 13);
        prev.CustomMinimumSize = StepperSize;
        prev.Pressed += () => step(-1);
        row.AddChild(prev);

        var value = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        value.AddThemeFontSizeOverride("font_size", 15);
        value.AddThemeColorOverride("font_color", UiTheme.Gold);
        row.AddChild(value);

        var next = Ui.MenuButton("▶", 26, 13);
        next.CustomMinimumSize = StepperSize;
        next.Pressed += () => step(1);
        row.AddChild(next);

        parent.AddChild(row);
        return value;
    }

    private static VBoxContainer PickColumn(HBoxContainer row, string label)
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 5);
        row.AddChild(col);
        col.AddChild(SectionLabel(label));

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        col.AddChild(box);
        return box;
    }

    private static Label SectionLabel(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", UiTheme.Bronze);
        return l;
    }

    public void OpenCreate()
    {
        if (FreeSlot() < 0)
        {
            _status.Text = $"All {MaxSlots} character slots are in use.";
            return;
        }
        _createPanel.Visible = true;
        _selectPanel.Visible = false;
        FrameStageCamera(forCreate: true);
        BuildRaceButtons(Net.I.Nation);
        if (Platform.PointerUi) _createName.GrabFocus();
    }

    private void CloseCreate()
    {
        _createPanel.Visible = false;
        _selectPanel.Visible = true;
        FrameStageCamera(forCreate: false);
        if (_selected != null) SelectCharacter(_selected);
    }

    private void BuildRaceButtons(int nation)
    {
        _createNation = nation;
        foreach (Node c in _raceBox.GetChildren()) c.QueueFree();
        int[] races = StarterStats.RacesFor(nation);
        foreach (int r in races)
        {
            var b = Ui.MenuButton(StarterStats.RaceName(r), 27, 14);
            int captured = r;
            b.Pressed += () => PickRace(captured);
            b.SetMeta("race", r);
            _raceBox.AddChild(b);
        }
        PickRace(races[0]);
    }

    private void PickRace(int race)
    {
        _createRace = race;
        foreach (Node c in _raceBox.GetChildren())
            if (c is Button b && b.HasMeta("race"))
                Ui.MarkSelected(b, b.GetMeta("race").AsInt32() == race);

        foreach (Node c in _jobBox.GetChildren()) c.QueueFree();
        int[] classes = StarterStats.ClassesFor(race);
        foreach (int cls in classes)
        {
            var b = Ui.MenuButton(StarterStats.ClassName(cls), 27, 14);
            int captured = cls;
            b.Pressed += () => PickClass(captured);
            b.SetMeta("job", cls);
            _jobBox.AddChild(b);
        }
        PickClass(classes[0]);
        _createFace = Mathf.Clamp(_createFace, 0, Mathf.Max(0, CharacterPreview.FaceCount(race) - 1));
        _createHair = Mathf.Clamp(_createHair, 0, Mathf.Max(0, CharacterPreview.HairCount(race) - 1));
        RefreshCreateState();
        RefreshCreatePreview();
    }

    private void PickClass(int cls)
    {
        _createClass = cls;
        foreach (Node c in _jobBox.GetChildren())
            if (c is Button b && b.HasMeta("job"))
                Ui.MarkSelected(b, b.GetMeta("job").AsInt32() == cls);

        System.Array.Clear(_alloc, 0, _alloc.Length);
        RefreshCreateState();
    }

    private void StepStat(int index, int dir)
    {
        if (StarterStats.For(_createRace, _createClass) is not { } roll) return;
        if (dir > 0 && Remaining(roll) <= 0) return;
        if (dir < 0 && _alloc[index] <= 0) return;
        _alloc[index] += dir;
        RefreshCreateState();
    }

    private int Remaining(StarterStats.Roll roll)
    {
        int spent = 0;
        foreach (int a in _alloc) spent += a;
        return roll.Bonus - spent;
    }

    private int[] FinalStats(StarterStats.Roll roll) => new[]
    {
        roll.Str + _alloc[0], roll.Sta + _alloc[1], roll.Dex + _alloc[2],
        roll.Int + _alloc[3], roll.Mag + _alloc[4],
    };

    private void StepFace(int dir)
    {
        int n = Mathf.Max(1, CharacterPreview.FaceCount(_createRace));
        _createFace = WrapRange(_createFace + dir, 0, n - 1);
        RefreshCreateState();
        RefreshCreatePreview();
    }

    private void StepHair(int dir)
    {
        int n = Mathf.Max(1, CharacterPreview.HairCount(_createRace));
        _createHair = WrapRange(_createHair + dir, 0, n - 1);
        RefreshCreateState();
        RefreshCreatePreview();
    }

    private static int WrapRange(int value, int min, int max)
        => value < min ? max : value > max ? min : value;

    private void RefreshCreateState()
    {
        if (StarterStats.For(_createRace, _createClass) is not { } roll)
        {
            _createStatus.Text = "That race and job combination is not available.";
            _createConfirm.Disabled = true;
            return;
        }

        var stats = FinalStats(roll);
        for (int i = 0; i < 5; i++)
        {
            _statValues[i].Text = stats[i].ToString();
            _statDown[i].Disabled = _alloc[i] <= 0;
            _statUp[i].Disabled = Remaining(roll) <= 0;
        }
        int left = Remaining(roll);
        _createBonus.Text = $"Bonus points: {left}";
        _faceLbl.Text = _createFace.ToString();
        _hairLbl.Text = _createHair.ToString();

        bool named = _createName.Text.StripEdges().Length > 0;
        if (!_creating) _createStatus.Text = "";
        _createConfirm.Disabled = _creating || !named || left != 0;
    }

    private void RefreshCreatePreview()
    {
        if (_characterModel != null && GodotObject.IsInstanceValid(_characterModel))
            _characterModel.QueueFree();
        _characterModel = CharacterPreview.Build(_createRace, _createFace,
            System.Array.Empty<int>(), _createHair, _hairColour.Color);
        if (_characterModel == null) return;
        _characterModel.Position = Vector3.Zero;
        _characterAnchor.RotationDegrees = new Vector3(0, _stageStandYaw, 0);
        _characterAnchor.AddChild(_characterModel);
        FrameCharacterWhenReady(_characterModel);
    }

    private void SubmitCreate()
    {
        if (StarterStats.For(_createRace, _createClass) is not { } roll) return;
        int slot = FreeSlot();
        if (slot < 0) { _createStatus.Text = "No free character slot."; return; }

        var stats = FinalStats(roll);
        _creating = true;
        _createConfirm.Disabled = true;
        _createStatus.Text = "Creating…";
        Net.I.CreateCharacter(
            slot, _createName.Text.StripEdges(), _createRace, _createClass,
            _createFace, HairCode.Pack(_createHair, _hairColour.Color),
            stats[0], stats[1], stats[2], stats[3], stats[4]);
    }

    private void OnCreateResult(int code)
    {
        _creating = false;
        if (code == 0)
        {
            _createPanel.Visible = false;
            _selectPanel.Visible = true;
            FrameStageCamera(forCreate: false);
            _createName.Text = "";
            _status.Text = "Character created.";
            Net.I.RequestCharList();
            return;
        }
        _createStatus.Text = code switch
        {
            3 => "That name is already taken.",
            5 => "Invalid name.",
            6 => "That name is not allowed.",
            7 => "Invalid race for that nation.",
            9 => "Invalid job for that race.",
            _ => $"Create failed (code {code}).",
        };
        RefreshCreateState();
    }
}
