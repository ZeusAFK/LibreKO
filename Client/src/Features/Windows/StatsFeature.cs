using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class StatsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly DevelopSession develop;
    private readonly ItemsSession items;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly IProgressRequests progressRequests;
    private readonly HotbarFeature hotbar;
    private readonly SkillPresetFeature skillPreset;
    private readonly SkillWindowFeature skillWindow;

    public StatsFeature(IWorldContext context, ClanSession clan, DevelopSession develop, ItemsSession items, ProgressSession progress, SelfSession self, IProgressRequests progressRequests, HotbarFeature hotbar, SkillPresetFeature skillPreset, SkillWindowFeature skillWindow)
    {
        this.context = context;
        this.clan = clan;
        this.develop = develop;
        this.items = items;
        this.progress = progress;
        this.self = self;
        this.progressRequests = progressRequests;
        this.hotbar = hotbar;
        this.skillPreset = skillPreset;
        this.skillWindow = skillWindow;
    }

    private static readonly int[] StatDisplayOrder = { 0, 1, 2, 4, 3 };

    private static readonly string[] ResistLabels = { "Fire", "Ice", "Lightning", "Magic", "Curse", "Poison" };

    internal VBoxContainer Content { get; private set; } = null!;
    internal Label StHeaderName { get; private set; } = null!;
    internal Label StHeaderSub { get; private set; } = null!;
    private Label stExpLbl = null!;
    private Label stApLbl = null!;
    private Label stNpLbl = null!;
    private Label stMannerLbl = null!;
    private Label stBonusLbl = null!;
    private Label stLevelLbl = null!;
    private Label stNationLbl = null!;
    private readonly Label[] statValLbls = new Label[CharacterSheet.StatCount];
    private readonly Button[] statBtns = new Button[CharacterSheet.StatCount];
    private readonly Label[] resistLbls = new Label[CharacterSheet.ResistCount];

    public void Build()
    {
        BuildPanel();
        RefreshUI();

        progress.PointChangeEvent += OnPointChange;
        items.ItemStatsEvent += OnSheet;
        develop.StatResetEvent += OnStatReset;
        develop.ClassEligibilityEvent += OnClassEligibility;
        develop.JobChangeResultEvent += OnJobChangeResult;
        develop.ClassPromotedEvent += OnClassPromoted;
        skillPreset.StatPlanApplied += RefreshUI;
    }

    public void Dispose()
    {
        skillPreset.StatPlanApplied -= RefreshUI;
        progress.PointChangeEvent -= OnPointChange;
        items.ItemStatsEvent -= OnSheet;
        develop.StatResetEvent -= OnStatReset;
        develop.ClassEligibilityEvent -= OnClassEligibility;
        develop.JobChangeResultEvent -= OnJobChangeResult;
        develop.ClassPromotedEvent -= OnClassPromoted;
    }

    private void OnStatReset(StatResetReply reply)
    {
        if (!reply.Succeeded) return;
        self.Sheet.ApplyReset(reply.Stats, reply.StatPoints, reply.Ap);
        self.Vitals.ApplyMaxima(reply.MaxHp, reply.MaxMp);
        context.Hud.RefreshVitalBars();
        RefreshUI();
    }

    private void OnClassEligibility(JobChangeEligibility eligibility) =>
        context.Log.Notice(eligibility == JobChangeEligibility.Allowed ? "You are eligible to change class." : "You cannot change class yet.");

    private void OnClassPromoted(int charId, int newClass)
    {
        if (charId != context.Self.CharId && charId != self.LastEnter.CharId) return;
        ApplyClassChange(newClass);
        context.Log.Notice($"You are now a {CharacterClassCatalog.SpecializationName(newClass)}!");
    }

    internal void ApplyClassChange(int newClass)
    {
        RefreshUI();
        skillWindow.RebuildSkillWindow();
        hotbar.Clear();
    }

    private void OnJobChangeResult(JobChangeOutcome outcome)
    {
        context.Log.Notice(outcome switch
        {
            JobChangeOutcome.Succeeded => "Class changed!",
            JobChangeOutcome.EquipmentWorn => "Take off your equipment before changing class.",
            JobChangeOutcome.NoTokenOrSameClass => "You need a job-change scroll (and a different class).",
            _ => "Class change failed.",
        });
    }

    private const int CombatColumn = 132;
    private const int ResistColumn = 172;
    private const int IconSize = 22;
    private const int ResistIconSize = 20;

    private static readonly string[] StatIcons =
    { "system/stat-str", "system/stat-hp", "system/stat-dex", "system/stat-int", "system/stat-mp" };

    private static readonly Color[] StatIconTints =
    {
        new("e0855a"), new("d95f5f"), new("5fc7d9"), new("b98ce0"), new("5f9fd9"),
    };

    private static readonly string[] ResistIcons =
    {
        "system/res-fire", "system/res-ice", "system/res-lightning",
        "system/res-magic", "system/res-curse", "system/res-poison",
    };

    private static readonly Color[] ResistTints =
    {
        new("e2703a"), new("6fd0e8"), new("e8c53a"), new("b06fe8"), new("8f6fe8"), new("6fd07a"),
    };

    private Label stRebirthCaption = null!;
    private Label stRebirthLbl = null!;
    private Label stClanLbl = null!;
    private VSeparator stRebirthRule = null!;
    private TextureRect stMedal = null!;
    private readonly PanelContainer[] statRowFrames = new PanelContainer[CharacterSheet.StatCount];
    private Godot.Timer statRepeat = null!;
    private int statRepeatRow = -1;
    private const double StatRepeatDelay = 0.4;
    private const double StatRepeatEvery = 0.08;
    private const float MedalSize = 34f;
    private const float StatRowPadX = 4f;
    private const float StatRowPadY = 1f;
    private const string RecommendedStatHint = "Recommended for your class";
    private static readonly Color RecommendedFrame = new(UiTheme.Gold, 0.55f);
    private Label stAcLbl = null!;
    private ProgressBar stExpBar = null!;
    private Label stExpPercentLbl = null!;
    private readonly Label[] statBonusLbls = new Label[CharacterSheet.StatCount];

    private static Color BonusColour(int bonus) =>
        bonus > 0 ? UiTheme.Good : bonus < 0 ? UiTheme.Bad : UiTheme.TextDim;

    private static string BonusText(int bonus) =>
        bonus == 0 ? "" : bonus > 0 ? $"(+{bonus})" : $"({bonus})";

    internal void BuildPanel()
    {
        var root = new VBoxContainer { CustomMinimumSize = new Vector2(UiKit.CharacterPageWidth, 0) };
        root.AddThemeConstantOverride("separation", 8);
        Content = root;

        BuildHeader(root);
        BuildProgress(root);

        var middle = new HBoxContainer();
        middle.AddThemeConstantOverride("separation", 8);
        root.AddChild(middle);
        BuildCombat(middle);
        BuildAttributes(middle);

        var right = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(ResistColumn, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        right.AddThemeConstantOverride("separation", 8);
        middle.AddChild(right);
        BuildResistance(right);
    }

    private void BuildHeader(VBoxContainer root)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        root.AddChild(header);

        stMedal = new TextureRect
        {
            CustomMinimumSize = new Vector2(MedalSize, MedalSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            Visible = false,
        };
        header.AddChild(stMedal);

        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", 0);
        header.AddChild(names);
        StHeaderName = HudStyle.Label(17);
        names.AddChild(StHeaderName);
        StHeaderSub = UiTheme.Text("", 12, UiTheme.TextLo);
        names.AddChild(StHeaderSub);
        stClanLbl = UiTheme.Text("", 12, UiTheme.Gold);
        names.AddChild(stClanLbl);

        var presetBtn = new Button
        {
            Text = "Stat Preset",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Plan a stat or mastery spread",
        };
        presetBtn.Pressed += skillPreset.TogglePreset;
        header.AddChild(presetBtn);
    }

    private void BuildProgress(VBoxContainer root)
    {
        var section = Section(root, "Progress", out _);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        section.AddChild(top);
        stLevelLbl = PairField(top, "Level");
        top.AddChild(new VSeparator());
        stNationLbl = PairField(top, "Nation");
        stRebirthRule = new VSeparator();
        top.AddChild(stRebirthRule);
        stRebirthLbl = PairField(top, "Rebirth", out stRebirthCaption);

        section.AddChild(StatsRule());

        var expRow = new HBoxContainer();
        expRow.AddThemeConstantOverride("separation", 10);
        section.AddChild(expRow);
        var expCaption = UiTheme.Text("EXP", 13, UiTheme.TextLo);
        expCaption.CustomMinimumSize = new Vector2(70, 0);
        expRow.AddChild(expCaption);

        stExpBar = new ProgressBar
        {
            MaxValue = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(96, 14),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        stExpBar.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        stExpBar.AddThemeStyleboxOverride("fill", UiTheme.MeterFill(UiTheme.Gold));
        expRow.AddChild(stExpBar);

        stExpPercentLbl = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Right);
        stExpPercentLbl.CustomMinimumSize = new Vector2(58, 0);
        expRow.AddChild(stExpPercentLbl);

        stExpLbl = UiTheme.Text("", 12, UiTheme.TextDim, HorizontalAlignment.Right);
        section.AddChild(stExpLbl);

        section.AddChild(StatsRule());
        stNpLbl = CaptionRow(section, "Contribution");
        stMannerLbl = CaptionRow(section, "Manner");
    }

    private void BuildCombat(HBoxContainer parent)
    {
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(CombatColumn, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        parent.AddChild(column);
        var section = Section(column, "Combat", out _, fill: true);

        stApLbl = CombatEntry(section, "system/combat-attack", "Attack", UiTheme.GoldBright);
        section.AddChild(StatsRule());
        stAcLbl = CombatEntry(section, "system/combat-defence", "Defence", UiTheme.TextLo);
    }

    private static Label CombatEntry(VBoxContainer section, string icon, string caption, Color tint)
    {
        var box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        section.AddChild(box);

        var iconRow = new CenterContainer();
        iconRow.AddChild(UiIcons.Image(icon, new Vector2(38, 38), tint));
        box.AddChild(iconRow);

        box.AddChild(UiTheme.Text(caption, 12, UiTheme.TextLo, HorizontalAlignment.Center));
        var value = UiTheme.Text("0", 21, UiTheme.TextHi, HorizontalAlignment.Center);
        box.AddChild(value);
        return value;
    }

    private void BuildAttributes(HBoxContainer parent)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(column);
        var section = Section(column, "Attributes", out var titleRow);

        titleRow.AddChild(UiTheme.Text("Stat Point", 12, UiTheme.TextLo));
        stBonusLbl = UiTheme.Text("0", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        stBonusLbl.CustomMinimumSize = new Vector2(26, 0);
        var pointFrame = new PanelContainer();
        pointFrame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        var pointMargin = new MarginContainer();
        UiTheme.Margins(pointMargin, 6, 1, 6, 1);
        pointMargin.AddChild(stBonusLbl);
        pointFrame.AddChild(pointMargin);
        titleRow.AddChild(pointFrame);

        foreach (int row in StatDisplayOrder)
        {
            int index = row;
            if (row != StatDisplayOrder[0]) section.AddChild(StatsRule());

            var frame = new PanelContainer();
            statRowFrames[row] = frame;
            section.AddChild(frame);
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            frame.AddChild(line);

            line.AddChild(UiIcons.Image(
                StatIcons[row], new Vector2(IconSize, IconSize), StatIconTints[row]));

            var name = UiTheme.Text(CharacterSheet.StatLabels[row], 13, UiTheme.GoldBright);
            name.CustomMinimumSize = new Vector2(42, 0);
            line.AddChild(name);

            statValLbls[row] = UiTheme.Text("0", 14, UiTheme.TextHi, HorizontalAlignment.Right);
            statValLbls[row].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(statValLbls[row]);

            statBonusLbls[row] = UiTheme.Text("", 12, UiTheme.TextDim);
            statBonusLbls[row].CustomMinimumSize = new Vector2(48, 0);
            statBonusLbls[row].MouseFilter = Control.MouseFilterEnum.Pass;
            line.AddChild(statBonusLbls[row]);

            var btn = new Button
            {
                Text = "+",
                CustomMinimumSize = new Vector2(30, 24),
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"Spend a point on {CharacterSheet.StatLabels[row]}",
            };
            btn.ButtonDown += () => StartStatRepeat(index);
            btn.ButtonUp += StopStatRepeat;
            statBtns[row] = btn;
            line.AddChild(btn);
        }

        statRepeat = new Godot.Timer { OneShot = false };
        statRepeat.Timeout += RepeatStat;
        section.AddChild(statRepeat);
    }

    private void StartStatRepeat(int row)
    {
        OnAllocate(row);
        statRepeatRow = row;
        statRepeat.Start(StatRepeatDelay);
    }

    private void RepeatStat()
    {
        if (statRepeatRow < 0 || !self.Sheet.CanAllocate || !statBtns[statRepeatRow].IsPressed()) { StopStatRepeat(); return; }
        OnAllocate(statRepeatRow);
        statRepeat.WaitTime = StatRepeatEvery;
    }

    private void StopStatRepeat()
    {
        statRepeatRow = -1;
        statRepeat.Stop();
    }

    private static string StatBonusTooltip(int rebirth, int other)
    {
        if (rebirth == 0 && other == 0) return "";
        var lines = new System.Collections.Generic.List<string>();
        if (rebirth != 0) lines.Add($"Rebirth {rebirth:+#;-#}");
        if (other != 0) lines.Add($"Items and buffs {other:+#;-#}");
        return string.Join("\n", lines);
    }

    private void BuildResistance(VBoxContainer parent)
    {
        var section = Section(parent, "Resistance", out _, fill: true);
        for (int index = 0; index < CharacterSheet.ResistCount; index++)
        {
            if (index > 0) section.AddChild(StatsRule());
            AddResistEntry(section, index);
        }
    }

    private void AddResistEntry(VBoxContainer column, int index)
    {
        var line = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(UiIcons.Image(
            ResistIcons[index], new Vector2(ResistIconSize, ResistIconSize), ResistTints[index]));
        line.AddChild(UiTheme.Text(ResistLabels[index], 12, UiTheme.TextLo));

        resistLbls[index] = UiTheme.Text("0", 13, UiTheme.TextHi, HorizontalAlignment.Right);
        resistLbls[index].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(resistLbls[index]);
        column.AddChild(line);
    }

    private static VBoxContainer Section(
        Control parent, string title, out HBoxContainer titleRow, bool fill = false)
    {
        var box = UiTheme.Section();
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (fill) box.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        parent.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 10, 8, 10, 8);
        box.AddChild(margin);

        var column = new VBoxContainer();
        if (fill) column.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 6);
        column.AddChild(titleRow);
        titleRow.AddChild(UiTheme.Text("◆", 10, UiTheme.Bronze));
        var caption = UiTheme.Text(title.ToUpperInvariant(), 12, UiTheme.Gold);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(caption);

        column.AddChild(StatsRule());
        return column;
    }

    private static Control StatsRule()
    {
        var rule = new Panel { CustomMinimumSize = new Vector2(0, 1) };
        var style = new StyleBoxFlat { BgColor = UiTheme.EdgeSoft };
        rule.AddThemeStyleboxOverride("panel", style);
        return rule;
    }

    private static Label PairField(HBoxContainer row, string caption) =>
        PairField(row, caption, out _);

    private static Label PairField(HBoxContainer row, string caption, out Label captionLabel)
    {
        var name = UiTheme.Text(caption, 13, UiTheme.TextLo);
        name.CustomMinimumSize = new Vector2(60, 0);
        row.AddChild(name);
        captionLabel = name;

        var value = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Right);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(value);
        return value;
    }

    private static Label CaptionRow(VBoxContainer parent, string caption)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var name = UiTheme.Text(caption, 13, UiTheme.TextLo);
        name.CustomMinimumSize = new Vector2(70, 0);
        row.AddChild(name);

        var value = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Right);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(value);
        return value;
    }

    internal void RefreshUI()
    {
        UpdateLevelOrb();

        if (StHeaderName == null) return;
        var info = self.LastEnter;
        StHeaderName.Text = $"{info.Name}    Lv {self.Sheet.LevelLabel}";
        StHeaderSub.Text = $"{CharacterClassCatalog.DisplayName(info.Class)}   •   {Nations.Name(info.Nation)}";
        var clanInfo = clan.MyClan;
        stClanLbl.Text = clanInfo.InClan ? $"{clanInfo.Name}   •   {ClanRanks.Name(clanInfo.Fame)}" : "";
        stClanLbl.Visible = clanInfo.InClan;
        string? medal = RankSymbol.PlatePath(info.KnightsRank, info.PersonalRank);
        stMedal.Visible = medal != null;
        stMedal.Texture = medal != null ? GD.Load<Texture2D>(medal) : null;
        stMedal.TooltipText = info.PersonalRank > 0 ? $"National rank {info.PersonalRank}" : "";
        stLevelLbl.Text = self.Sheet.Level.ToString();
        stNationLbl.Text = Nations.Name(info.Nation);
        bool reborn = self.Sheet.RebirthLevel > 0;
        stRebirthRule.Visible = stRebirthCaption.Visible = stRebirthLbl.Visible = reborn;
        stRebirthLbl.Text = self.Sheet.RebirthLevel.ToString();
        stExpBar.Value = self.Sheet.ExpPercent;
        stExpPercentLbl.Text = $"{self.Sheet.ExpPercent:0.00}%";
        stExpLbl.Text = self.Sheet.MaxExp > 0
            ? $"{self.Sheet.Exp:n0} / {self.Sheet.MaxExp:n0}"
            : self.Sheet.Exp.ToString("n0");
        stNpLbl.Text = self.Sheet.MonthlyNp is { } monthlyNp ? $"{self.Sheet.Np:n0} / {monthlyNp:n0}" : self.Sheet.Np.ToString("n0");
        stMannerLbl.Text = self.Sheet.Manner.ToString("n0");

        stApLbl.Text = self.Sheet.Ap.ToString("n0");
        stAcLbl.Text = self.Sheet.Ac.ToString("n0");
        stBonusLbl.Text = self.Sheet.Points.ToString();

        for (int i = 0; i < CharacterSheet.StatCount; i++)
        {
            int bonus = self.Sheet.StatBonusAtRow(i);
            statValLbls[i].Text = self.Sheet.StatAtRow(i).ToString();
            statBonusLbls[i].Text = BonusText(bonus);
            statBonusLbls[i].AddThemeColorOverride("font_color", BonusColour(bonus));
            int rebirth = self.Sheet.RebirthBonusAtRow(i);
            statBonusLbls[i].TooltipText = StatBonusTooltip(rebirth, bonus - rebirth);
            statBtns[i].Disabled = !self.Sheet.CanAllocate;
            bool recommended = StarterStats.Recommended(self.Class > 0 ? self.Class : info.Class, i);
            statRowFrames[i].AddThemeStyleboxOverride("panel", recommended ? RecommendedRowStyle() : PlainRowStyle());
            statRowFrames[i].TooltipText = recommended ? RecommendedStatHint : "";
        }

        for (int i = 0; i < CharacterSheet.ResistCount; i++)
            resistLbls[i].Text = self.Sheet.ResistAt(i).ToString();
    }

    private static StyleBoxFlat RecommendedRowStyle()
    {
        var style = new StyleBoxFlat { BgColor = new Color(UiTheme.Gold, 0.06f), BorderColor = RecommendedFrame };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = style.ContentMarginRight = StatRowPadX;
        style.ContentMarginTop = style.ContentMarginBottom = StatRowPadY;
        return style;
    }

    private static StyleBoxEmpty PlainRowStyle() => new()
    {
        ContentMarginLeft = StatRowPadX, ContentMarginRight = StatRowPadX,
        ContentMarginTop = StatRowPadY, ContentMarginBottom = StatRowPadY,
    };

    internal void OnAllocate(int statIndex)
    {
        if (!self.Sheet.CanAllocate) return;
        progressRequests.PointChange(CharacterSheet.WireTypeForRow(statIndex));
    }

    private void OnPointChange(PointChangeReply reply)
    {
        self.Sheet.SetMaxWeight(reply.MaxWeight);
        if (!self.Sheet.ApplyPointChange(reply.Type, reply.Value, reply.TotalHit)) return;
        self.Vitals.ApplyMaxima(reply.MaxHp, reply.MaxMp);
        context.Hud.RefreshVitalBars();
        RefreshUI();
    }

    private void OnSheet(DerivedStats s)
    {
        self.Sheet.ApplyDerived(s);
        RefreshUI();
    }

    private void UpdateLevelOrb()
    {
        context.Hud.RefreshLevelOrb();
    }

}
