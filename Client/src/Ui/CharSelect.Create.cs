using Godot;

namespace LibreKO;

public partial class CharSelect : Node3D
{
    private const int MaxSlots = 4;

    private static Vector2 StepperSize =>
        Platform.Pick(new Vector2(34, 26), new Vector2(52, Ui.TouchButtonHeight));

    private PanelContainer createPanel = null!;
    private VBoxContainer raceBox = null!;
    private VBoxContainer jobBox = null!;
    private LineEdit createName = null!;
    private Label createBonus = null!;
    private Label createStatus = null!;
    private Label faceLbl = null!;
    private Label hairLbl = null!;
    private ColorPickerButton hairColour = null!;
    private Button createConfirm = null!;
    private readonly Label[] statValues = new Label[5];
    private readonly Label[] statNames = new Label[5];
    private Label createDesc = null!;
    private const string RecommendedHint = "Recommended for this class";
    private readonly Button[] statUp = new Button[5];
    private readonly Button[] statDown = new Button[5];
    private readonly int[] alloc = new int[5];

    private int createNation = Nations.Karus;
    private int createRace;
    private int createClass;
    private int createFace;
    private int createHair;
    private bool creating;

    private int FreeSlot()
    {
        var used = new HashSet<int>();
        foreach (var c in characters) used.Add(characters.IndexOf(c));
        for (int i = 0; i < MaxSlots; i++)
            if (i >= characters.Count) return i;
        return -1;
    }

    private void BuildCreatePanel(Control overlay)
    {
        createPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        createPanel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        createPanel.OffsetRight = PanelWidth + 30;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.028f, 0.80f),
            BorderColor = new Color(UiTheme.Gold, 0.35f),
        };
        style.BorderWidthRight = 1;
        createPanel.AddThemeStyleboxOverride("panel", style);
        overlay.AddChild(createPanel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        createPanel.AddChild(margin);

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
        createName = new LineEdit { PlaceholderText = "character name", MaxLength = 20 };
        Ui.StyleField(createName);
        createName.TextChanged += _ => RefreshCreateState();
        form.AddChild(createName);

        if (Platform.TouchUi)
        {
            var picks = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            picks.AddThemeConstantOverride("separation", 12);
            form.AddChild(picks);
            raceBox = PickColumn(picks, "Race");
            jobBox = PickColumn(picks, "Job");
        }
        else
        {
            form.AddChild(SectionLabel("Race"));
            raceBox = new VBoxContainer();
            raceBox.AddThemeConstantOverride("separation", 5);
            form.AddChild(raceBox);

            form.AddChild(SectionLabel("Job"));
            jobBox = new VBoxContainer();
            jobBox.AddThemeConstantOverride("separation", 5);
            form.AddChild(jobBox);
        }

        createDesc = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        createDesc.AddThemeFontSizeOverride("font_size", 12);
        createDesc.AddThemeColorOverride("font_color", UiTheme.TextLo);
        form.AddChild(createDesc);

        form.AddChild(SectionLabel("Stats"));
        for (int i = 0; i < 5; i++) form.AddChild(BuildStatRow(i));
        createBonus = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        createBonus.AddThemeFontSizeOverride("font_size", 13);
        createBonus.AddThemeColorOverride("font_color", UiTheme.Neutral);
        form.AddChild(createBonus);

        form.AddChild(SectionLabel("Appearance"));
        faceLbl = BuildStepperRow(form, "Face", d => StepFace(d));
        hairLbl = BuildStepperRow(form, "Hair", d => StepHair(d));

        var colourRow = new HBoxContainer();
        colourRow.AddThemeConstantOverride("separation", 8);
        var colourLbl = new Label { Text = "Hair colour", CustomMinimumSize = new Vector2(96, 0) };
        colourLbl.AddThemeFontSizeOverride("font_size", 13);
        colourLbl.AddThemeColorOverride("font_color", UiTheme.TextHi);
        colourRow.AddChild(colourLbl);
        hairColour = new ColorPickerButton
        {
            Color = new Color(0.35f, 0.22f, 0.10f),
            CustomMinimumSize = new Vector2(0, Platform.Pick(28, Ui.TouchButtonHeight)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            EditAlpha = false,
        };
        hairColour.ColorChanged += OnHairColourChanged;
        colourRow.AddChild(hairColour);
        form.AddChild(colourRow);

        createStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        createStatus.AddThemeFontSizeOverride("font_size", 12);
        createStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
        root.AddChild(createStatus);

        createConfirm = Ui.MenuButton("Create", 44, 19);
        createConfirm.Pressed += SubmitCreate;
        var cancel = Ui.MenuButton("Cancel", 34, 15);
        cancel.Pressed += CloseCreate;

        if (Platform.TouchUi)
        {
            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", 8);
            cancel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            createConfirm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            actions.AddChild(cancel);
            actions.AddChild(createConfirm);
            root.AddChild(actions);
        }
        else
        {
            root.AddChild(createConfirm);
            root.AddChild(cancel);
        }

        Game.Session.PreGame.CreateCharResultEvent += OnCreateResult;
    }

    private Control BuildStatRow(int index)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var label = new Label
        {
            Text = CharacterSheet.StatLabels[index],
            CustomMinimumSize = new Vector2(44, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", UiTheme.TextHi);
        statNames[index] = label;
        row.AddChild(label);

        statDown[index] = Ui.MenuButton("◀", 26, 13);
        statDown[index].CustomMinimumSize = StepperSize;
        int down = index;
        statDown[index].Pressed += () => StepStat(down, -1);
        row.AddChild(statDown[index]);

        statValues[index] = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(44, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Platform.Pick(Control.SizeFlags.Fill, Control.SizeFlags.ExpandFill),
        };
        statValues[index].AddThemeFontSizeOverride("font_size", 15);
        statValues[index].AddThemeColorOverride("font_color", UiTheme.Gold);
        row.AddChild(statValues[index]);

        statUp[index] = Ui.MenuButton("▶", 26, 13);
        statUp[index].CustomMinimumSize = StepperSize;
        int up = index;
        statUp[index].Pressed += () => StepStat(up, 1);
        row.AddChild(statUp[index]);
        return row;
    }

    private void RefreshCreateGuide()
    {
        string race = Text(StarterStats.RaceTextId(createRace));
        string job = Text(StarterStats.ClassTextId(createClass));
        createDesc.Text = race.Length > 0 && job.Length > 0 ? $"{race}\n\n{job}" : race + job;
        createDesc.Visible = createDesc.Text.Length > 0;
        for (int i = 0; i < statNames.Length; i++)
        {
            bool recommended = StarterStats.Recommended(createClass, i);
            statNames[i].AddThemeColorOverride("font_color", recommended ? UiTheme.GoldVivid : UiTheme.TextHi);
            string hint = Text(StarterStats.StatHintTextId(i));
            statNames[i].TooltipText = recommended ? $"{RecommendedHint}\n{hint}" : hint;
        }
    }

    private static string Text(int id) => id > 0 ? ItemData.Text(id, "") : "";

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
            status.Text = $"All {MaxSlots} character slots are in use.";
            return;
        }
        createPanel.Visible = true;
        selectPanel.Visible = false;
        createZoom = 1f;
        FrameStageCamera(forCreate: true);
        BuildRaceButtons(Game.Session.Self.Nation);
        if (Platform.PointerUi) createName.GrabFocus();
    }

    private bool ColourPopupOpen() =>
        hairColour is { } picker && GodotObject.IsInstanceValid(picker) && picker.GetPopup().Visible;

    private void CloseCreate()
    {
        createPanel.Visible = false;
        selectPanel.Visible = true;
        FrameStageCamera(forCreate: false);
        if (selected != null) SelectCharacter(selected);
    }

    private void BuildRaceButtons(int nation)
    {
        createNation = nation;
        foreach (Node c in raceBox.GetChildren()) c.QueueFree();
        int[] races = StarterStats.RacesFor(nation);
        foreach (int r in races)
        {
            var b = Ui.MenuButton(StarterStats.RaceName(r), 27, 14);
            int captured = r;
            b.Pressed += () => PickRace(captured);
            b.SetMeta("race", r);
            raceBox.AddChild(b);
        }
        PickRace(races[0]);
    }

    private void PickRace(int race)
    {
        createRace = race;
        foreach (Node c in raceBox.GetChildren())
            if (c is Button b && b.HasMeta("race"))
                Ui.MarkSelected(b, b.GetMeta("race").AsInt32() == race);

        foreach (Node c in jobBox.GetChildren()) c.QueueFree();
        int[] classes = StarterStats.ClassesFor(race);
        foreach (int cls in classes)
        {
            var b = Ui.MenuButton(StarterStats.ClassName(cls), 27, 14);
            int captured = cls;
            b.Pressed += () => PickClass(captured);
            b.SetMeta("job", cls);
            jobBox.AddChild(b);
        }
        createFace = Mathf.Clamp(createFace, 0, Mathf.Max(0, CharacterPreview.FaceCount(race) - 1));
        createHair = Mathf.Clamp(createHair, 0, Mathf.Max(0, CharacterPreview.HairCount(race) - 1));
        PickClass(classes[0]);
    }

    private void PickClass(int cls)
    {
        createClass = cls;
        RefreshCreateGuide();
        foreach (Node c in jobBox.GetChildren())
            if (c is Button b && b.HasMeta("job"))
                Ui.MarkSelected(b, b.GetMeta("job").AsInt32() == cls);

        System.Array.Clear(alloc, 0, alloc.Length);
        createHelmetOff = false;
        RefreshCreateState();
        RefreshCreatePreview();
    }

    private void StepStat(int index, int dir)
    {
        if (StarterStats.For(createRace, createClass) is not { } roll) return;
        if (dir > 0 && Remaining(roll) <= 0) return;
        if (dir < 0 && alloc[index] <= 0) return;
        alloc[index] += dir;
        RefreshCreateState();
    }

    private int Remaining(StarterStats.Roll roll)
    {
        int spent = 0;
        foreach (int a in alloc) spent += a;
        return roll.Bonus - spent;
    }

    private int[] FinalStats(StarterStats.Roll roll) => new[]
    {
        roll.Str + alloc[0], roll.Sta + alloc[1], roll.Dex + alloc[2],
        roll.Int + alloc[3], roll.Mag + alloc[4],
    };

    private void StepFace(int dir)
    {
        int n = Mathf.Max(1, CharacterPreview.FaceCount(createRace));
        createFace = WrapRange(createFace + dir, 0, n - 1);
        createHelmetOff = true;
        RefreshCreateState();
        RefreshCreatePreview();
    }

    private void StepHair(int dir)
    {
        int n = Mathf.Max(1, CharacterPreview.HairCount(createRace));
        createHair = WrapRange(createHair + dir, 0, n - 1);
        createHelmetOff = true;
        RefreshCreateState();
        RefreshCreatePreview();
    }

    private static int WrapRange(int value, int min, int max)
        => value < min ? max : value > max ? min : value;

    private void RefreshCreateState()
    {
        if (StarterStats.For(createRace, createClass) is not { } roll)
        {
            createStatus.Text = "That race and job combination is not available.";
            createConfirm.Disabled = true;
            return;
        }

        var stats = FinalStats(roll);
        for (int i = 0; i < 5; i++)
        {
            statValues[i].Text = stats[i].ToString();
            statDown[i].Disabled = alloc[i] <= 0;
            statUp[i].Disabled = Remaining(roll) <= 0;
        }
        int left = Remaining(roll);
        createBonus.Text = $"Bonus points: {left}";
        faceLbl.Text = createFace.ToString();
        hairLbl.Text = createHair.ToString();

        bool named = createName.Text.StripEdges().Length > 0;
        if (!creating) createStatus.Text = "";
        createConfirm.Disabled = creating || !named || left != 0;
    }

    private bool createHelmetOff;
    private System.Collections.Generic.Dictionary<(int Race, int Class), NewCharacterLook>? newLooks;

    private int[] CreatePreviewGear()
    {
        if (newLooks == null)
        {
            using var f = Godot.FileAccess.Open(NewCharacterLooks.ResourcePath, Godot.FileAccess.ModeFlags.Read);
            newLooks = f != null ? NewCharacterLooks.Parse(f.GetAsText()) : new();
        }
        return newLooks.TryGetValue((createRace, createClass), out var look)
            ? NewCharacterLooks.Gear(look, helmet: !createHelmetOff)
            : System.Array.Empty<int>();
    }

    private void OnHairColourChanged(Color colour)
    {
        bool hairShown = createHelmetOff;
        createHelmetOff = true;
        if (hairShown && characterModel != null && CharacterPreview.RetintHair(characterModel, colour)) return;
        RefreshCreatePreview();
    }

    private void RefreshCreatePreview()
    {
        DetachCharacterModel();
        characterModel = CharacterPreview.Build(createRace, createFace,
            CreatePreviewGear(), createHair, hairColour.Color);
        if (characterModel == null) return;
        characterModel.Position = Vector3.Zero;
        characterAnchor.RotationDegrees = new Vector3(0, stageStandYaw, 0);
        characterAnchor.AddChild(characterModel);
        FrameCharacterWhenReady(characterModel);
    }

    private void SubmitCreate()
    {
        if (StarterStats.For(createRace, createClass) is not { } roll) return;
        int slot = FreeSlot();
        if (slot < 0) { createStatus.Text = "No free character slot."; return; }

        var stats = FinalStats(roll);
        creating = true;
        createConfirm.Disabled = true;
        createStatus.Text = "Creating…";
        Game.Requests.PreGame.CreateCharacter(
            slot, createName.Text.StripEdges(), createRace, createClass,
            createFace, HairCode.Pack(createHair, hairColour.Color),
            stats[0], stats[1], stats[2], stats[3], stats[4]);
    }

    private void OnCreateResult(CreateCharacterResult code)
    {
        creating = false;
        if (code == CreateCharacterResult.Success)
        {
            createPanel.Visible = false;
            selectPanel.Visible = true;
            FrameStageCamera(forCreate: false);
            createName.Text = "";
            status.Text = "Character created.";
            Game.Requests.PreGame.CharacterList();
            return;
        }
        string message = ItemData.Text(CreateCharacterTexts.TextFor(code), $"Create failed (code {(byte)code}).");
        createStatus.Text = message;
        Notice.Show(this, message, ItemData.Text(CreateCharacterTexts.TitleText, "Failed creating character"));
        RefreshCreateState();
    }
}
