using Godot;

namespace LibreKO.Features.Windows;

internal sealed class RebirthFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly DevelopSession develop;
    private readonly SelfSession self;
    private readonly IDevelopRequests developRequests;

    public RebirthFeature(IWorldContext context, DevelopSession develop, SelfSession self, IDevelopRequests developRequests)
    {
        this.context = context;
        this.develop = develop;
        this.self = self;
        this.developRequests = developRequests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private Label levelLbl = null!;
    private Label pointsLbl = null!;
    private Label status = null!;
    private readonly Label[] bonusLbls = new Label[RebirthPick.StatCount];
    private readonly Label[] pickLbls = new Label[RebirthPick.StatCount];
    private readonly Button[] addBtns = new Button[RebirthPick.StatCount];
    private readonly Button[] removeBtns = new Button[RebirthPick.StatCount];
    private Button btn = null!;
    private readonly RebirthPick pick = new();
    private byte[] sent = [];
    private bool shown;

    public bool IsOpen => shown;

    private bool inFlight;
    private bool resetMode;

    public void Build()
    {
        BuildPanel();
        develop.RebStatChangeEvent += OnStatResult;
        develop.RebirthResetEvent += ResetPicker;
    }

    public void Dispose()
    {
        develop.RebStatChangeEvent -= OnStatResult;
        develop.RebirthResetEvent -= ResetPicker;
    }

    private void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        panel = new HudWindow("rebirth", "Rebirth", bodyMinWidth: 320) { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        levelLbl = UiTheme.Text("", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        root.AddChild(levelLbl);

        root.AddChild(new HSeparator());
        root.AddChild(UiTheme.SectionTitle("Bonus points"));
        pointsLbl = HudStyle.Label(13);
        root.AddChild(pointsLbl);

        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            int index = row;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            root.AddChild(line);

            var name = HudStyle.Label(13);
            name.Text = CharacterSheet.StatLabels[row];
            name.CustomMinimumSize = new Vector2(48, 0);
            line.AddChild(name);

            bonusLbls[row] = UiTheme.Text("", 12, UiTheme.TextDim);
            bonusLbls[row].CustomMinimumSize = new Vector2(64, 0);
            line.AddChild(bonusLbls[row]);

            removeBtns[row] = StepButton("-", () => EditPoint(index, false));
            line.AddChild(removeBtns[row]);

            pickLbls[row] = HudStyle.Label(13, HorizontalAlignment.Center);
            pickLbls[row].CustomMinimumSize = new Vector2(28, 0);
            line.AddChild(pickLbls[row]);

            addBtns[row] = StepButton("+", () => EditPoint(index, true));
            line.AddChild(addBtns[row]);
        }

        root.AddChild(new HSeparator());
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(actionRow);
        btn = new Button { Text = "Rebirth", FocusMode = Control.FocusModeEnum.None };
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.Pressed += OnPressed;
        actionRow.AddChild(btn);
        var cancel = new Button { Text = "Not yet", FocusMode = Control.FocusModeEnum.None };
        cancel.AddThemeFontSizeOverride("font_size", 13);
        cancel.Pressed += Close;
        actionRow.AddChild(cancel);
        status = HudStyle.Label(13, HorizontalAlignment.Right);
        status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actionRow.AddChild(status);
    }

    private static Button StepButton(string text, System.Action pressed)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(30, 24),
            FocusMode = Control.FocusModeEnum.None,
        };
        HoldRepeat.Attach(btn, pressed);
        return btn;
    }

    internal void OpenPicker()
    {
        if (!inFlight)
        {
            resetMode = false;
            pick.Clear();
        }
        OpenPanel();
    }

    internal void OpenReset()
    {
        if (self.Sheet.RebirthLevel <= 0) return;
        if (!inFlight)
        {
            resetMode = true;
            pick.Reset(self.Sheet.RebirthLevel * RebirthPick.PointsPerRebirth);
        }
        OpenPanel();
    }

    private void OpenPanel()
    {
        if (!inFlight) SetStatus("", false);
        RefreshUI();
        panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void RefreshUI()
    {
        int level = self.Sheet.RebirthLevel;
        bool locked = inFlight || context.Self.Dead || (!resetMode && !RebirthPick.Available(self.Sheet.Level, level));
        levelLbl.Text = resetMode ? $"Rebirth Lv {level}" : $"Rebirth Lv {level}  →  Lv {level + 1}";
        pointsLbl.Text = $"Place {pick.Capacity} points  ({pick.Remaining} left)";
        btn.Text = resetMode ? "Redistribute" : "Rebirth";
        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            bonusLbls[row].Text = $"now +{self.Sheet.RebirthBonusAtRow(row)}";
            int picked = pick.PickedAt(row);
            pickLbls[row].Text = picked > 0 ? $"+{picked}" : "";
            addBtns[row].Disabled = locked || !pick.CanAdd(row);
            removeBtns[row].Disabled = locked || !pick.CanRemove(row);
        }
        btn.Disabled = locked || !pick.Complete;
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }

    private void OnPressed()
    {
        if (!shown || inFlight || context.Self.Dead
            || !RebirthPick.Available(self.Sheet.Level, self.Sheet.RebirthLevel)) return;
        if (!pick.Complete)
        {
            SetStatus($"Place all {pick.Capacity} points first.", true);
            return;
        }
        inFlight = true;
        sent = pick.Payload();
        RefreshUI();
        if (resetMode)
        {
            SetStatus("Redistributing…", false);
            developRequests.RebirthStatReset(sent);
            return;
        }
        SetStatus("Reincarnating…", false);
        if (!developRequests.RebirthStatChange(sent))
            OnStatResult(ClassChangeSubOpcode.RebirthStatChange, (int)RebirthResult.Unavailable);
    }

    private void EditPoint(int row, bool add)
    {
        if (inFlight) return;
        if (add) pick.Add(row); else pick.Remove(row);
        RefreshUI();
    }

    private void ResetPicker()
    {
        inFlight = false;
        sent = [];
        pick.Clear();
        Close();
    }

    private void OnStatResult(ClassChangeSubOpcode sub, int code)
    {
        if (sub == ClassChangeSubOpcode.RebirthStatReset)
        {
            inFlight = false;
            if (code == 1)
            {
                if (resetMode) self.ApplyRebirthReset(this.sent);
                context.Log.Notice("Rebirth bonus points redistributed.");
                Close();
                return;
            }
            SetStatus("The rebirth bonus points were not changed.", true);
            if (shown) RefreshUI();
            return;
        }
        if (!inFlight) return;
        inFlight = false;
        byte[] sent = this.sent;
        this.sent = [];
        var result = (RebirthResult)code;
        if (result == RebirthResult.Success)
        {
            self.ApplyRebirth(sent);
            context.Log.Notice(ItemData.Text(RebirthWire.AcceptedText, $"Rebirth Lv {self.Sheet.RebirthLevel}"));
            Close();
            return;
        }
        string refusal = ItemData.Text(RebirthWire.ResultText(result), "Mekin refused the rebirth.");
        SetStatus(refusal, true);
        if (shown) RefreshUI();
        else context.Log.Notice(refusal);
    }
}
