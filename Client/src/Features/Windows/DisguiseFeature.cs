using Godot;

namespace LibreKO.Features.Windows;

internal sealed class DisguiseFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly DisguiseSession disguise;
    private readonly PremiumSession premium;
    private readonly SelfSession self;
    private readonly ICombatRequests combatRequests;

    public DisguiseFeature(IWorldContext context, DisguiseSession disguise, PremiumSession premium, SelfSession self, ICombatRequests combatRequests)
    {
        this.context = context;
        this.disguise = disguise;
        this.premium = premium;
        this.self = self;
        this.combatRequests = combatRequests;
    }

    private const int LevelText = 10351;
    private const int ConfirmText = 10352;
    private const int TransformedText = 10353;
    private const int RefusalTextBase = 7626;
    private const int RefusalCodes = 3;
    private const string TablePath = "res://assets/skills/disguise.json";
    private const float Width = 240f;
    private const float GroupHeight = 190f;
    private const float FormHeight = 210f;

    internal DisguiseForm[] Table { get; set; } = Array.Empty<DisguiseForm>();
    private IReadOnlyList<DisguiseGroup> groups = Array.Empty<DisguiseGroup>();
    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private VBoxContainer groupRows = null!;
    private VBoxContainer formRows = null!;
    private Label note = null!;

    private bool shown;

    public bool IsOpen => shown;

    private int group = -1;
    private DisguiseForm? pick;

    public void Build()
    {
        Table = LoadTable();
        BuildPanel();
        disguise.TransformationListEvent += Open;
        disguise.TransformationRefusedEvent += OnTransformationRefused;
    }

    public void Dispose()
    {
        disguise.TransformationListEvent -= Open;
        disguise.TransformationRefusedEvent -= OnTransformationRefused;
    }

    internal static DisguiseForm[] LoadTable() =>
        Godot.FileAccess.FileExists(TablePath)
        ? Disguise.Parse(Godot.FileAccess.GetFileAsString(TablePath))
        : Array.Empty<DisguiseForm>();

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        Panel = new HudWindow("disguise", "Transformation", bodyMinWidth: (int)Width) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var body = Panel.Body;
        body.AddThemeConstantOverride("separation", 6);
        body.AddChild(UiTheme.SectionTitle("Level"));
        groupRows = List(body, GroupHeight);
        body.AddChild(UiTheme.SectionTitle("Form"));
        formRows = List(body, FormHeight);

        note = UiTheme.Text("", 12, UiTheme.Warning);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(Width, 0);
        body.AddChild(note);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        footer.AddThemeConstantOverride("separation", 8);
        body.AddChild(footer);
        var ok = UiTheme.ActionButton("OK", "Transform into the selected form");
        ok.CustomMinimumSize = new Vector2(100, 28);
        ok.Pressed += Confirm;
        footer.AddChild(ok);
        var close = UiTheme.SmallButton("Close", "Close");
        close.CustomMinimumSize = new Vector2(100, 28);
        close.Pressed += Close;
        footer.AddChild(close);
    }

    private static VBoxContainer List(VBoxContainer parent, float height)
    {
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        parent.AddChild(frame);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(Width, height),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        frame.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(rows);
        return rows;
    }

    internal void Open(int listSkill)
    {
        if (SkillData.Get(listSkill) is not { } skill) return;
        int premiumType = premium.HasPremium ? premium.PremiumType : 0;
        groups = Disguise.Groups(Disguise.FormsFor(Table, skill.UseItem, premiumType));
        string title = ItemData.DisplayName(skill.UseItem);
        Panel.Title = string.IsNullOrEmpty(title) ? "Transformation" : title;
        group = -1;
        PickGroup(0);
        shown = true;
        Panel.Visible = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
    }

    internal void PickGroup(int index)
    {
        group = index;
        foreach (var child in groupRows.GetChildren()) child.QueueFree();
        for (int i = 0; i < groups.Count; i++)
        {
            int at = i;
            groupRows.AddChild(DisguiseRow($"Lv. {groups[i].Level}", i == index, () => PickGroup(at), null));
        }
        PickForm(index >= 0 && index < groups.Count && groups[index].Forms.Count > 0
            ? groups[index].Forms[0]
            : null);
    }

    private void PickForm(DisguiseForm? form)
    {
        pick = form;
        foreach (var child in formRows.GetChildren()) child.QueueFree();
        if (group >= 0 && group < groups.Count)
        {
            foreach (var each in groups[group].Forms)
            {
                var row = each;
                formRows.AddChild(DisguiseRow(row.Name, row == form, () => PickForm(row), Confirm));
            }
        }
        note.Text = form?.Note ?? "";
        note.Visible = !string.IsNullOrEmpty(note.Text);
    }

    private static Button DisguiseRow(string text, bool selected, Action onPick, Action? activate)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 26),
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", selected ? UiTheme.GoldBright : UiTheme.TextLo);
        button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(selected));
        button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
        button.Pressed += onPick;
        if (activate != null)
        {
            button.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) activate();
            };
        }
        return button;
    }

    private void Confirm()
    {
        if (pick is not { } form) return;
        switch (Disguise.Refusal(form, self.Sheet.Level, self.TransformSkill != 0))
        {
            case DisguiseRefusal.Level:
                Notice.Show(context.Root, ItemData.Text(LevelText, "You cannot transform at your level."), "Transformation");
                return;
            case DisguiseRefusal.Transformed:
                Notice.Show(context.Root, ItemData.Text(TransformedText, "Transforming."), "Transformation");
                return;
        }
        Notice.Confirm(context.Root, ItemData.Text(ConfirmText, "Would you like to transform?"), "Yes", "No", () =>
        {
            combatRequests.Magic(MagicSub.Effecting, form.Skill, self.MyCharId);
            Close();
        }, title: "Transformation");
    }

    private void OnTransformationRefused(int code)
    {
        if (code < 1 || code > RefusalCodes) return;
        Notice.Show(context.Root, ItemData.Text(RefusalTextBase + code, "You cannot transform here."), "Transformation");
    }
}
