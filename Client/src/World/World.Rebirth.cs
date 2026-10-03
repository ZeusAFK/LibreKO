using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private static readonly string[] RebirthStatLabels = ["STR", "HP", "DEX", "INT", "MP"];

    private CanvasLayer _rebirthLayer = null!;
    private HudWindow _rebirthPanel = null!;
    private Label _rebirthLevelLbl = null!, _rebirthPointsLbl = null!, _rebirthStatus = null!;
    private readonly Label[] _rebirthBonusLbls = new Label[RebirthPick.StatCount];
    private readonly Label[] _rebirthPickLbls = new Label[RebirthPick.StatCount];
    private readonly Button[] _rebirthAddBtns = new Button[RebirthPick.StatCount];
    private readonly Button[] _rebirthRemoveBtns = new Button[RebirthPick.StatCount];
    private Button _rebirthBtn = null!;
    private readonly RebirthPick _rebirthPick = new();
    private byte[] _rebirthSent = [];
    private bool _rebirthShown;
    private bool _rebirthInFlight;

    private void RebirthInit()
    {
        BuildRebirthPanel();
        Net.I.RebStatChangeEvent += OnRebirthStatResult;
    }

    private void RebirthDispose()
    {
        Net.I.RebStatChangeEvent -= OnRebirthStatResult;
    }

    private void BuildRebirthPanel()
    {
        _rebirthLayer = new CanvasLayer { Layer = 74 };
        AddChild(_rebirthLayer);

        _rebirthPanel = new HudWindow("rebirth", Localization.Loc.Tr("Rebirth"), new Vector2(220, 130), 320) { Visible = false };
        _rebirthPanel.Closed += CloseRebirth;
        _rebirthLayer.AddChild(_rebirthPanel);

        var root = _rebirthPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _rebirthLevelLbl = UiTheme.Text("", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        root.AddChild(_rebirthLevelLbl);

        root.AddChild(new HSeparator());
        root.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Bonus points")));
        _rebirthPointsLbl = HudStyle.Label(13);
        root.AddChild(_rebirthPointsLbl);

        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            int index = row;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            root.AddChild(line);

            var name = HudStyle.Label(13);
            name.Text = RebirthStatLabels[row];
            name.CustomMinimumSize = new Vector2(48, 0);
            line.AddChild(name);

            _rebirthBonusLbls[row] = UiTheme.Text("", 12, UiTheme.TextDim);
            _rebirthBonusLbls[row].CustomMinimumSize = new Vector2(64, 0);
            line.AddChild(_rebirthBonusLbls[row]);

            _rebirthRemoveBtns[row] = RebirthStepButton("-", () => { _rebirthPick.Remove(index); RefreshRebirthUI(); });
            line.AddChild(_rebirthRemoveBtns[row]);

            _rebirthPickLbls[row] = HudStyle.Label(13, HorizontalAlignment.Center);
            _rebirthPickLbls[row].CustomMinimumSize = new Vector2(28, 0);
            line.AddChild(_rebirthPickLbls[row]);

            _rebirthAddBtns[row] = RebirthStepButton("+", () => { _rebirthPick.Add(index); RefreshRebirthUI(); });
            line.AddChild(_rebirthAddBtns[row]);
        }

        root.AddChild(new HSeparator());
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(actionRow);
        _rebirthBtn = new Button { Text = Localization.Loc.Tr("Rebirth"), FocusMode = Control.FocusModeEnum.None };
        _rebirthBtn.AddThemeFontSizeOverride("font_size", 13);
        _rebirthBtn.Pressed += OnRebirthPressed;
        actionRow.AddChild(_rebirthBtn);
        var cancel = new Button { Text = Localization.Loc.Tr("Not yet"), FocusMode = Control.FocusModeEnum.None };
        cancel.AddThemeFontSizeOverride("font_size", 13);
        cancel.Pressed += CloseRebirth;
        actionRow.AddChild(cancel);
        _rebirthStatus = HudStyle.Label(13, HorizontalAlignment.Right);
        _rebirthStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actionRow.AddChild(_rebirthStatus);
    }

    private static Button RebirthStepButton(string text, System.Action pressed)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(30, 24),
            FocusMode = Control.FocusModeEnum.None,
        };
        btn.Pressed += pressed;
        return btn;
    }

    private void OpenRebirthPicker()
    {
        _rebirthPick.Clear();
        _rebirthInFlight = false;
        SetRebirthStatus("", false);
        RefreshRebirthUI();
        _rebirthPanel.Visible = true;
        _rebirthShown = true;
    }

    private void CloseRebirth()
    {
        if (!_rebirthShown) return;
        _rebirthShown = false;
        _rebirthPanel.Visible = false;
    }

    private void RefreshRebirthUI()
    {
        int level = Sheet.RebirthLevel;
        _rebirthLevelLbl.Text = $"{Localization.Loc.Tr("Rebirth")} Lv {level}  →  Lv {level + 1}";
        _rebirthPointsLbl.Text = $"{Localization.Loc.Tr("Place")} {RebirthPick.PointsPerRebirth} {Localization.Loc.Tr("points")}  ({_rebirthPick.Remaining} {Localization.Loc.Tr("left")})";
        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            _rebirthBonusLbls[row].Text = $"{Localization.Loc.Tr("now")} +{Sheet.RebirthBonusAtRow(row)}";
            int picked = _rebirthPick.PickedAt(row);
            _rebirthPickLbls[row].Text = picked > 0 ? $"+{picked}" : "";
            _rebirthAddBtns[row].Disabled = _rebirthInFlight || !_rebirthPick.CanAdd(row);
            _rebirthRemoveBtns[row].Disabled = _rebirthInFlight || !_rebirthPick.CanRemove(row);
        }
        _rebirthBtn.Disabled = _rebirthInFlight || !_rebirthPick.Complete;
    }

    private void SetRebirthStatus(string text, bool warn)
    {
        _rebirthStatus.Text = text;
        _rebirthStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }

    private void OnRebirthPressed()
    {
        if (_rebirthInFlight || _selfDead) return;
        if (!_rebirthPick.Complete)
        {
            SetRebirthStatus($"{Localization.Loc.Tr("Place all")} {RebirthPick.PointsPerRebirth} {Localization.Loc.Tr("points first.")}", true);
            return;
        }
        _rebirthInFlight = true;
        _rebirthSent = _rebirthPick.Payload();
        SetRebirthStatus(Localization.Loc.Tr("Reincarnating…"), false);
        RefreshRebirthUI();
        Net.I.SendRebirthStatChange(_rebirthSent);
    }

    private void OnRebirthStatResult(int sub, int code)
    {
        if (sub != Net.ClassChangeRebirthStat)
        {
            CombatNotice(code == 1 ? Localization.Loc.Tr("Rebirth bonus points redistributed.") : Localization.Loc.Tr("The rebirth bonus points were not changed."));
            return;
        }
        _rebirthInFlight = false;
        if (code == 1)
        {
            Sheet.ApplyRebirth(_rebirthSent);
            CombatNotice($"{Localization.Loc.Tr("Rebirth")} Lv {Sheet.RebirthLevel}");
            CloseRebirth();
            return;
        }
        SetRebirthStatus(Localization.Loc.Tr("Mekin refused the rebirth."), true);
        if (_rebirthShown) RefreshRebirthUI();
    }
}
